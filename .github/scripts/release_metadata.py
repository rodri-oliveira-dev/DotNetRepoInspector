#!/usr/bin/env python3
"""Resolve and validate DotNetRepoInspector release metadata."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from dataclasses import dataclass
from pathlib import Path

_SEMVER_PATTERN = re.compile(
    r"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)"
    r"(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$"
)
_COMMIT_PATTERN = re.compile(r"^[0-9a-fA-F]{40}$")


@dataclass(frozen=True)
class ProductVersion:
    text: str
    major: int
    minor: int
    patch: int
    prerelease: tuple[str, ...]

    @property
    def is_prerelease(self) -> bool:
        return bool(self.prerelease)

    @property
    def tag(self) -> str:
        return f"v{self.text}"

    @property
    def aliases(self) -> tuple[str, ...]:
        if self.is_prerelease:
            return ()

        return (f"v{self.major}", f"v{self.major}.{self.minor}")

    @staticmethod
    def parse(value: str) -> "ProductVersion":
        text = value.strip()
        match = _SEMVER_PATTERN.fullmatch(text)
        if match is None:
            raise ValueError(
                f"'{value}' is not a supported Semantic Version. "
                "Use MAJOR.MINOR.PATCH with an optional prerelease suffix and no build metadata."
            )

        prerelease_text = match.group(4)
        prerelease = tuple(prerelease_text.split(".")) if prerelease_text else ()
        for identifier in prerelease:
            if identifier.isdigit() and len(identifier) > 1 and identifier.startswith("0"):
                raise ValueError(
                    f"Prerelease numeric identifier '{identifier}' must not contain leading zeroes."
                )

        return ProductVersion(
            text=text,
            major=int(match.group(1)),
            minor=int(match.group(2)),
            patch=int(match.group(3)),
            prerelease=prerelease,
        )


def _write_key_values(path: Path | None, values: dict[str, str]) -> None:
    if path is None:
        return

    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("a", encoding="utf-8", newline="\n") as stream:
        for key, value in values.items():
            if "\n" in value or "\r" in value:
                raise ValueError(f"Output '{key}' must be a single-line value.")
            stream.write(f"{key}={value}\n")


def _resolve(args: argparse.Namespace) -> int:
    requested = ProductVersion.parse(args.requested_version)

    values = {
        "version": requested.text,
        "tag": requested.tag,
        "major": str(requested.major),
        "minor": str(requested.minor),
        "patch": str(requested.patch),
        "is_prerelease": str(requested.is_prerelease).lower(),
        "major_alias": f"v{requested.major}",
        "minor_alias": f"v{requested.major}.{requested.minor}",
    }
    _write_key_values(args.github_output, values)
    print(json.dumps(values, indent=2, sort_keys=True))
    return 0


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def _validate_commit(commit: str) -> str:
    value = commit.strip().lower()
    if _COMMIT_PATTERN.fullmatch(value) is None:
        raise ValueError("Release commit must be a full 40-character Git SHA.")
    return value


def _manifest(args: argparse.Namespace) -> int:
    version = ProductVersion.parse(args.version)
    commit = _validate_commit(args.commit)
    package = args.package.resolve()
    report = args.inspection_report.resolve()

    expected_package_name = f"DotNetRepoInspector.{version.text}.nupkg"
    if package.name != expected_package_name:
        raise ValueError(
            f"Expected package '{expected_package_name}', actual '{package.name}'."
        )
    if not package.is_file():
        raise ValueError(f"Release package '{package}' does not exist.")
    if not report.is_file():
        raise ValueError(f"Inspection smoke report '{report}' does not exist.")

    report_document = json.loads(report.read_text(encoding="utf-8-sig"))
    schema_version = str(report_document.get("schemaVersion", "")).strip()
    if not schema_version:
        raise ValueError("Inspection smoke report does not contain schemaVersion.")

    package_sha256 = _sha256(package)
    manifest = {
        "manifestVersion": 1,
        "product": "DotNetRepoInspector",
        "version": version.text,
        "tag": version.tag,
        "sourceCommit": commit,
        "schemaVersion": schema_version,
        "artifacts": [
            {
                "name": package.name,
                "sha256": package_sha256,
            }
        ],
        "githubAction": {
            "immutableTag": version.tag,
            "movingAliases": list(version.aliases),
        },
    }

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(
        json.dumps(manifest, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
        newline="\n",
    )
    args.checksums_output.write_text(
        f"{package_sha256}  {package.name}\n",
        encoding="utf-8",
        newline="\n",
    )
    print(json.dumps(manifest, indent=2, sort_keys=True))
    return 0


def _verify(args: argparse.Namespace) -> int:
    version = ProductVersion.parse(args.version)
    commit = _validate_commit(args.commit)
    manifest_path = args.manifest.resolve()
    package = args.package.resolve()

    document = json.loads(manifest_path.read_text(encoding="utf-8"))
    expected = {
        "version": version.text,
        "tag": version.tag,
        "sourceCommit": commit,
    }
    for key, expected_value in expected.items():
        actual = str(document.get(key, ""))
        if actual != expected_value:
            raise ValueError(
                f"Manifest '{key}' mismatch. Expected '{expected_value}', actual '{actual}'."
            )

    artifacts = document.get("artifacts")
    if not isinstance(artifacts, list) or len(artifacts) != 1:
        raise ValueError("Release manifest must describe exactly one packaged artifact.")

    artifact = artifacts[0]
    expected_package_name = f"DotNetRepoInspector.{version.text}.nupkg"
    if artifact.get("name") != expected_package_name or package.name != expected_package_name:
        raise ValueError("Release package name does not match the manifest/product version.")

    expected_digest = str(artifact.get("sha256", "")).lower()
    actual_digest = _sha256(package)
    if expected_digest != actual_digest:
        raise ValueError("Release package SHA-256 does not match the release manifest.")

    aliases = document.get("githubAction", {}).get("movingAliases", [])
    if aliases != list(version.aliases):
        raise ValueError("Release manifest Action aliases do not match Semantic Versioning policy.")

    print(
        f"Verified release manifest for {version.tag} at {commit} "
        f"with package SHA-256 {actual_digest}."
    )
    return 0


def _build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)

    resolve = subparsers.add_parser(
        "resolve", description="Resolve release metadata from the requested Semantic Version."
    )
    resolve.add_argument("--requested-version", required=True)
    resolve.add_argument("--github-output", type=Path)
    resolve.set_defaults(handler=_resolve)

    manifest = subparsers.add_parser(
        "manifest", description="Generate deterministic release metadata and package checksums."
    )
    manifest.add_argument("--version", required=True)
    manifest.add_argument("--commit", required=True)
    manifest.add_argument("--package", type=Path, required=True)
    manifest.add_argument("--inspection-report", type=Path, required=True)
    manifest.add_argument("--output", type=Path, required=True)
    manifest.add_argument("--checksums-output", type=Path, required=True)
    manifest.set_defaults(handler=_manifest)

    verify = subparsers.add_parser(
        "verify", description="Verify downloaded release artifacts before publication."
    )
    verify.add_argument("--version", required=True)
    verify.add_argument("--commit", required=True)
    verify.add_argument("--manifest", type=Path, required=True)
    verify.add_argument("--package", type=Path, required=True)
    verify.set_defaults(handler=_verify)

    return parser


def main() -> int:
    parser = _build_parser()
    args = parser.parse_args()
    try:
        return int(args.handler(args))
    except (OSError, ValueError, json.JSONDecodeError) as error:
        print(f"release metadata error: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
