#!/usr/bin/env bash
set -euo pipefail

command_name="${1:?usage: container_release.sh <check|publish|verify> <plan.json>}"
plan_path="${2:?usage: container_release.sh <check|publish|verify> <plan.json>}"

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
mapfile -t labels < <(jq -r '.labels | to_entries[] | "\(.key)=\(.value)"' "${plan_path}")
mapfile -t annotations < <(jq -r '.annotations | to_entries[] | "\(.key)=\(.value)"' "${plan_path}")

build_args=(
  --pull
  --platform "${platforms}"
  --build-arg "PRODUCT_VERSION=${version}"
  --build-arg "REPOSITORY_COMMIT=${revision}"
)

case "${command_name}" in
  check)
    docker buildx build \
      --check \
      "${build_args[@]}" \
      .
    ;;

  publish)
    publish_args=("${build_args[@]}" --push)
    for tag in "${tags[@]}"; do
      publish_args+=(--tag "${tag}")
    done
    for label in "${labels[@]}"; do
      publish_args+=(--label "${label}")
    done
    for annotation in "${annotations[@]}"; do
      publish_args+=(--annotation "index:${annotation}")
    done

    docker buildx build "${publish_args[@]}" .
    ;;

  verify)
    expected_platforms=("linux/amd64" "linux/arm64")
    canonical_digest=""

    for image in "${images[@]}"; do
      reference="${image}:${version}"
      raw_manifest="$(docker buildx imagetools inspect --raw "${reference}")"

      for expected_platform in "${expected_platforms[@]}"; do
        os="${expected_platform%%/*}"
        architecture="${expected_platform##*/}"
        if ! jq -e \
          --arg os "${os}" \
          --arg architecture "${architecture}" \
          '.manifests | any(.platform.os == $os and .platform.architecture == $architecture)' \
          <<<"${raw_manifest}" >/dev/null; then
          echo "${reference} is missing required platform ${expected_platform}." >&2
          exit 3
        fi
      done

      while IFS= read -r annotation_key; do
        expected_value="$(jq -er --arg key "${annotation_key}" '.annotations[$key]' "${plan_path}")"
        actual_value="$(jq -r --arg key "${annotation_key}" '.annotations[$key] // empty' <<<"${raw_manifest}")"
        if [[ "${actual_value}" != "${expected_value}" ]]; then
          echo "${reference} annotation ${annotation_key} mismatch." >&2
          exit 3
        fi
      done < <(jq -r '.annotations | keys[]' "${plan_path}")

      digest="$(docker buildx imagetools inspect "${reference}" | awk '$1 == "Digest:" { print $2; exit }')"
      if [[ -z "${digest}" ]]; then
        echo "Unable to resolve manifest digest for ${reference}." >&2
        exit 3
      fi
      if [[ -z "${canonical_digest}" ]]; then
        canonical_digest="${digest}"
      elif [[ "${digest}" != "${canonical_digest}" ]]; then
        echo "Registry manifest digest mismatch: ${reference}=${digest}, expected ${canonical_digest}." >&2
        exit 3
      fi

      docker pull --platform linux/amd64 "${reference}" >/dev/null
      image_labels="$(docker image inspect "${reference}" --format '{{json .Config.Labels}}')"
      while IFS= read -r label_key; do
        expected_value="$(jq -er --arg key "${label_key}" '.labels[$key]' "${plan_path}")"
        actual_value="$(jq -r --arg key "${label_key}" '.[$key] // empty' <<<"${image_labels}")"
        if [[ "${actual_value}" != "${expected_value}" ]]; then
          echo "${reference} label ${label_key} mismatch." >&2
          exit 3
        fi
      done < <(jq -r '.labels | keys[]' "${plan_path}")

      actual_version="$(docker run --rm --network none "${reference}" --version)"
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
          alias_digest="$(docker buildx imagetools inspect "${alias_reference}" | awk '$1 == "Digest:" { print $2; exit }')"
          if [[ "${alias_digest}" != "${canonical_digest}" ]]; then
            echo "Stable alias ${alias_reference} does not resolve to ${canonical_digest}." >&2
            exit 3
          fi
        done
      done
    fi

    echo "Verified ${version} container manifests for ${platforms} in both registries at ${canonical_digest}."
    ;;

  *)
    echo "Unsupported command '${command_name}'. Expected check, publish, or verify." >&2
    exit 2
    ;;
esac