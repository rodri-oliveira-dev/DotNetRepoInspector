#!/usr/bin/env bash
set -euo pipefail

command_name="${1:?usage: container_release.sh <check|publish|verify> <plan.json> [build-metadata.json]}"
plan_path="${2:?usage: container_release.sh <check|publish|verify> <plan.json> [build-metadata.json]}"
metadata_path="${3:-}"

if [[ ! -f "${plan_path}" ]]; then
  echo "Container release plan not found: ${plan_path}" >&2
  exit 2
fi

for dependency in docker jq; do
  if ! command -v "${dependency}" >/dev/null 2>&1; then
    echo "Required command '${dependency}' is not available." >&2
    exit 2
  fi
done

version="$(jq -er '.version' "${plan_path}")"
revision="$(jq -er '.revision' "${plan_path}")"
platforms="$(jq -er '.platforms | join(",")' "${plan_path}")"

mapfile -t images < <(jq -er '.images[]' "${plan_path}")
mapfile -t tags < <(jq -er '.tags[]' "${plan_path}")
mapfile -t expected_platforms < <(jq -er '.platforms[]' "${plan_path}")
mapfile -t labels < <(jq -r '.labels | to_entries[] | "\(.key)=\(.value)"' "${plan_path}")
mapfile -t annotations < <(jq -r '.annotations | to_entries[] | "\(.key)=\(.value)"' "${plan_path}")

build_args=(
  --pull
  --platform "${platforms}"
  --build-arg "PRODUCT_VERSION=${version}"
  --build-arg "REPOSITORY_COMMIT=${revision}"
  --sbom=true
  --provenance=mode=max
)

require_metadata_path() {
  if [[ -z "${metadata_path}" ]]; then
    echo "Build metadata path is required for '${command_name}'." >&2
    exit 2
  fi
}

metadata_digest() {
  local path="$1"
  if [[ ! -f "${path}" ]]; then
    echo "Build metadata file not found: ${path}" >&2
    exit 3
  fi

  local digest
  digest="$(jq -er '."containerimage.digest"' "${path}")"
  if [[ ! "${digest}" =~ ^sha256:[0-9a-f]{64}$ ]]; then
    echo "Build metadata does not contain a valid immutable container digest." >&2
    exit 3
  fi

  local descriptor_digest
  descriptor_digest="$(jq -er '."containerimage.descriptor".digest' "${path}")"
  if [[ "${descriptor_digest}" != "${digest}" ]]; then
    echo "Build metadata descriptor digest does not match containerimage.digest." >&2
    exit 3
  fi

  printf '%s\n' "${digest}"
}

manifest_json() {
  local reference="$1"
  docker buildx imagetools inspect "${reference}" --format '{{json .Manifest}}'
}

validate_attestations() {
  local image="$1"
  local manifest="$2"

  for expected_platform in "${expected_platforms[@]}"; do
    local os architecture subject_digest
    os="${expected_platform%%/*}"
    architecture="${expected_platform##*/}"
    subject_digest="$(
      jq -er \
        --arg os "${os}" \
        --arg architecture "${architecture}" \
        '[.manifests[] | select(.platform.os == $os and .platform.architecture == $architecture)] | if length == 1 then .[0].digest else error("expected exactly one runnable platform manifest") end' \
        <<<"${manifest}"
    )"

    mapfile -t attestation_digests < <(
      jq -r \
        --arg subject "${subject_digest}" \
        '.manifests[] | select((.annotations["vnd.docker.reference.type"] // "") == "attestation-manifest" and (.annotations["vnd.docker.reference.digest"] // "") == $subject) | .digest' \
        <<<"${manifest}"
    )

    if (( ${#attestation_digests[@]} == 0 )); then
      echo "${image} is missing an attestation manifest for ${expected_platform} (${subject_digest})." >&2
      exit 3
    fi

    local has_sbom=false
    local has_provenance=false
    for attestation_digest in "${attestation_digests[@]}"; do
      local attestation_manifest
      attestation_manifest="$(docker buildx imagetools inspect "${image}@${attestation_digest}" --raw)"

      if jq -e 'any(.layers[]?; (.annotations["in-toto.io/predicate-type"] // "") == "https://spdx.dev/Document")' <<<"${attestation_manifest}" >/dev/null; then
        has_sbom=true
      fi
      if jq -e 'any(.layers[]?; (.annotations["in-toto.io/predicate-type"] // "") | startswith("https://slsa.dev/provenance/"))' <<<"${attestation_manifest}" >/dev/null; then
        has_provenance=true
      fi
    done

    if [[ "${has_sbom}" != "true" ]]; then
      echo "${image} is missing the SPDX SBOM attestation for ${expected_platform}." >&2
      exit 3
    fi
    if [[ "${has_provenance}" != "true" ]]; then
      echo "${image} is missing the SLSA provenance attestation for ${expected_platform}." >&2
      exit 3
    fi
  done
}

case "${command_name}" in
  check)
    docker buildx build \
      --check \
      "${build_args[@]}" \
      .
    ;;

  publish)
    require_metadata_path
    mkdir -p "$(dirname "${metadata_path}")"

    publish_args=(
      "${build_args[@]}"
      --push
      --metadata-file "${metadata_path}"
    )
    for tag in "${tags[@]}"; do
      publish_args+=(--tag "${tag}")
    done
    for label in "${labels[@]}"; do
      publish_args+=(--label "${label}")
    done
    for annotation in "${annotations[@]}"; do
      publish_args+=(--annotation "index:${annotation}")
    done

    BUILDX_METADATA_PROVENANCE=max docker buildx build "${publish_args[@]}" .
    digest="$(metadata_digest "${metadata_path}")"
    echo "Published ${version} container index at ${digest}."
    ;;

  verify)
    require_metadata_path
    expected_digest="$(metadata_digest "${metadata_path}")"

    for image in "${images[@]}"; do
      reference="${image}:${version}"
      root_manifest="$(manifest_json "${reference}")"
      registry_digest="$(jq -er '.digest' <<<"${root_manifest}")"
      if [[ "${registry_digest}" != "${expected_digest}" ]]; then
        echo "Registry manifest digest mismatch: ${reference}=${registry_digest}, expected ${expected_digest}." >&2
        exit 3
      fi

      for expected_platform in "${expected_platforms[@]}"; do
        os="${expected_platform%%/*}"
        architecture="${expected_platform##*/}"
        if ! jq -e \
          --arg os "${os}" \
          --arg architecture "${architecture}" \
          '.manifests | any(.platform.os == $os and .platform.architecture == $architecture)' \
          <<<"${root_manifest}" >/dev/null; then
          echo "${reference} is missing required platform ${expected_platform}." >&2
          exit 3
        fi
      done

      while IFS= read -r annotation_key; do
        expected_value="$(jq -er --arg key "${annotation_key}" '.annotations[$key]' "${plan_path}")"
        actual_value="$(jq -r --arg key "${annotation_key}" '.annotations[$key] // empty' <<<"${root_manifest}")"
        if [[ "${actual_value}" != "${expected_value}" ]]; then
          echo "${reference} annotation ${annotation_key} mismatch." >&2
          exit 3
        fi
      done < <(jq -r '.annotations | keys[]' "${plan_path}")

      validate_attestations "${image}" "${root_manifest}"

      immutable_reference="${image}@${expected_digest}"
      docker pull --platform linux/amd64 "${immutable_reference}" >/dev/null
      image_labels="$(docker image inspect "${immutable_reference}" --format '{{json .Config.Labels}}')"
      while IFS= read -r label_key; do
        expected_value="$(jq -er --arg key "${label_key}" '.labels[$key]' "${plan_path}")"
        actual_value="$(jq -r --arg key "${label_key}" '.[$key] // empty' <<<"${image_labels}")"
        if [[ "${actual_value}" != "${expected_value}" ]]; then
          echo "${reference} label ${label_key} mismatch." >&2
          exit 3
        fi
      done < <(jq -r '.labels | keys[]' "${plan_path}")

      actual_version="$(docker run --rm --platform linux/amd64 --network none "${immutable_reference}" --version)"
      if [[ "${actual_version}" != "${version}" ]]; then
        echo "${reference} CLI version mismatch: expected ${version}, actual ${actual_version}." >&2
        exit 3
      fi
    done

    if [[ "$(jq -r '.isPrerelease' "${plan_path}")" == "false" ]]; then
      major="${version%%.*}"
      remainder="${version#*.}"
      minor="${remainder%%.*}"
      aliases=("${major}.${minor}" "${major}" "latest")
      for image in "${images[@]}"; do
        for alias in "${aliases[@]}"; do
          alias_reference="${image}:${alias}"
          alias_manifest="$(manifest_json "${alias_reference}")"
          alias_digest="$(jq -er '.digest' <<<"${alias_manifest}")"
          if [[ "${alias_digest}" != "${expected_digest}" ]]; then
            echo "Stable alias ${alias_reference} does not resolve to ${expected_digest}." >&2
            exit 3
          fi
        done
      done
    fi

    echo "Verified ${version} container manifests, SBOM, provenance, and ${platforms} in both registries at ${expected_digest}."
    ;;

  *)
    echo "Unsupported command '${command_name}'. Expected check, publish, or verify." >&2
    exit 2
    ;;
esac
