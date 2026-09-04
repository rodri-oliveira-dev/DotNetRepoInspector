#!/usr/bin/env bash
set -euo pipefail

image="${1:?usage: container_smoke.sh <image>}"
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
work_root="$(mktemp -d)"
artifacts="${work_root}/artifacts"
host_uid="$(id -u)"
host_gid="$(id -g)"

cleanup() {
  rm -rf "${work_root}"
}
trap cleanup EXIT

mkdir -p "${artifacts}"
# The smoke suite deliberately exercises arbitrary host UID/GID execution.
# Keep only the disposable output directory writable by that identity.
chmod 0777 "${artifacts}"

fail() {
  echo "container smoke test failed: $*" >&2
  exit 1
}

expect_exit() {
  local expected="$1"
  shift

  set +e
  "$@"
  local actual=$?
  set -e

  if [[ "${actual}" -ne "${expected}" ]]; then
    fail "expected exit code ${expected}, got ${actual}: $*"
  fi
}

hardened_run() {
  local source="$1"
  shift

  docker run --rm \
    --user "${host_uid}:${host_gid}" \
    --read-only \
    --network none \
    --cap-drop=ALL \
    --security-opt=no-new-privileges \
    --tmpfs /tmp:rw,nosuid,nodev,size=64m,mode=1777 \
    --mount "type=bind,src=${source},dst=/repo,readonly" \
    --mount "type=bind,src=${artifacts},dst=/artifacts" \
    "${image}" \
    "$@"
}

validate_report() {
  local report="$1"
  local sdk_family="$2"

  python3 - "${report}" "${sdk_family}" <<'PY'
import json
import pathlib
import sys

path = pathlib.Path(sys.argv[1])
sdk_family = sys.argv[2]

if not path.is_file():
    raise SystemExit(f"inspection report was not created: {path}")

with path.open(encoding="utf-8") as stream:
    report = json.load(stream)

if report.get("schemaVersion") != "1.3":
    raise SystemExit(f"unexpected schemaVersion: {report.get('schemaVersion')!r}")

resolved = (report.get("dotNetSdk") or {}).get("resolvedVersion")
if not isinstance(resolved, str) or not resolved.startswith(f"{sdk_family}."):
    raise SystemExit(
        f"expected resolved SDK family {sdk_family}.x, got {resolved!r}"
    )

projects = report.get("projects")
if not isinstance(projects, list) or not projects:
    raise SystemExit("successful compatibility inspection did not contain projects")
PY
}

inspect_fixture() {
  local fixture_name="$1"
  local sdk_family="$2"
  local source="${repo_root}/tests/Fixtures/Compatibility/${fixture_name}"
  local report="${artifacts}/${fixture_name,,}-inspection.json"

  rm -f "${report}"
  set +e
  hardened_run "${source}" /repo --output "/artifacts/$(basename "${report}")"
  local actual=$?
  set -e

  if [[ "${actual}" -ne 0 ]]; then
    echo "Inspection of ${fixture_name} exited with code ${actual}." >&2
    if [[ -f "${report}" ]]; then
      echo "Generated report:" >&2
      cat "${report}" >&2
    else
      echo "No inspection report was generated." >&2
    fi
    fail "expected successful ${fixture_name} inspection"
  fi

  validate_report "${report}" "${sdk_family}"
}

echo "Verifying default non-root identity..."
default_uid="$(docker run --rm --network none --entrypoint sh "${image}" -c 'id -u')"
if [[ -z "${default_uid}" || "${default_uid}" == "0" ]]; then
  fail "image default user must be non-root; actual uid=${default_uid:-<empty>}"
fi

echo "Verifying CLI entrypoint..."
help_output="$(docker run --rm --network none "${image}" --help)"
grep -Fq "DotNetRepoInspector" <<<"${help_output}" || fail "--help output is not the Inspector help text"
version_output="$(docker run --rm --network none "${image}" --version)"
[[ -n "${version_output}" ]] || fail "--version returned empty output"

echo "Verifying side-by-side SDK families..."
sdk_output="$(docker run --rm --network none --entrypoint dotnet "${image}" --list-sdks)"
echo "${sdk_output}"
grep -Eq '^8\.0\.' <<<"${sdk_output}" || fail ".NET 8 SDK family is missing"
grep -Eq '^10\.0\.' <<<"${sdk_output}" || fail ".NET 10 SDK family is missing"

echo "Verifying hardened offline inspections and SDK selection..."
inspect_fixture "Net8" "8.0"
inspect_fixture "Net10" "10.0"

echo "Verifying the repository mount is actually read-only..."
readonly_source="${repo_root}/tests/Fixtures/Compatibility/Net8"
if docker run --rm \
  --user "${host_uid}:${host_gid}" \
  --read-only \
  --network none \
  --cap-drop=ALL \
  --security-opt=no-new-privileges \
  --tmpfs /tmp:rw,nosuid,nodev,size=64m,mode=1777 \
  --mount "type=bind,src=${readonly_source},dst=/repo,readonly" \
  --entrypoint sh \
  "${image}" \
  -c 'touch /repo/.dotnet-repo-inspector-write-probe'; then
  fail "read-only source mount unexpectedly accepted a write"
fi

echo "Verifying documented CLI exit codes 0-5 with controlled scenarios..."
# 0 is already proven by the successful Net8/Net10 inspections above.
missing_sdk_source="${repo_root}/tests/Fixtures/Compatibility/MissingSdk"
missing_sdk_report="${artifacts}/missing-sdk-inspection.json"
expect_exit 1 hardened_run \
  "${missing_sdk_source}" \
  /repo --output /artifacts/missing-sdk-inspection.json

python3 - "${missing_sdk_report}" <<'PY'
import json
import pathlib
import sys

path = pathlib.Path(sys.argv[1])
with path.open(encoding="utf-8") as stream:
    report = json.load(stream)

diagnostics = report.get("diagnostics") or []
if not any(
    item.get("code") == "DRI1002" and item.get("severity") == "error"
    for item in diagnostics
):
    raise SystemExit("missing-SDK report did not contain DRI1002/error")
PY

expect_exit 2 docker run --rm --network none "${image}" --definitely-invalid-option
expect_exit 3 docker run --rm --network none "${image}" /path-that-does-not-exist
expect_exit 4 hardened_run \
  "${readonly_source}" \
  /repo --output /repo/forbidden-output.json
expect_exit 5 hardened_run \
  "${readonly_source}" \
  /repo \
  --output /artifacts/persistence-failure-inspection.json \
  --sink http \
  --sink-url http://127.0.0.1:9/snapshots \
  --sink-timeout-seconds 1 \
  --sink-max-attempts 1 \
  --sink-failure-mode fatal

echo "Container smoke suite passed for ${image}."
