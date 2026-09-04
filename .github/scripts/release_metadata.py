#!/usr/bin/env python3
"""Resolve and validate DotNetRepoInspector release metadata."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path

_SEMVER_PATTERN = re.compile(
    r"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)"
    r"(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$"
)
_COMMIT_PATTERN = re.compile(r"^[0-9a-fA-F]{40}$")
_DIGEST_PATTERN = re.compile(r"^sha256:[0-9a-f]{64}$")
_CONTAINER_IMAGES = (
    "ghcr.io/rodri-oliveira-dev/dotnet-repo-inspector",
    "docker.io/rodrigodotnet/dotnet-repo-inspector",
)
_CONTAINER_PLATFORMS = ("linux/amd64", "linux/arm64")
_CONTAINER_TITLE = "DotNetRepoInspector"
_CONTAINER_DESCRIPTION = (
    "Deterministic inspection and classification of .NET repositories using evaluated "
    "MSBuild metadata."
)
_CONTAINER_SOURCE = "https://github.com/rodri-oliveira-dev/DotNetRepoInspector"
_CONTAINER_DOCUMENTATION = (
    "https://github.com/rodri-oliveira-dev/DotNetRepoInspector/blob/main/docs/en/container.md"
)
_CONTAINER_LICENSES = "MIT"
_CONTAINER_AUTHORS = "Rodrigo de Oliveira"


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


def _validate_digest(digest: str) -> str:
    value = digest.strip().lower()
    if _DIGEST_PATTERN.fullmatch(value) is None:
        raise ValueError("Container digest must be an immutable sha256 digest.")
    return value


def _validate_created(created: str) -> str:
    value = created.strip()
    if not value or value != created:
        raise ValueError("Container created timestamp must be a non-empty trimmed RFC 3339 value.")

    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError as error:
        raise ValueError("Container created timestamp must be RFC 3339 compatible.") from error

    if parsed.tzinfo is None:
        raise ValueError("Container created timestamp must include a timezone.")
    return value


def _container_tag_suffixes(version: ProductVersion) -> tuple[str, ...]:
    if version.is_prerelease:
        return (version.text,)

    return (
        version.text,
        f"{version.major}.{version.minor}",
        str(version.major),
        "latest",
    )


def _build_container_plan(
    version: ProductVersion,
    commit: str,
    created: str,
) -> dict[str, object]:
    labels = {
        "org.opencontainers.image.title": _CONTAINER_TITLE,
        "org.opencontainers.image.description": _CONTAINER_DESCRIPTION,
        "org.opencontainers.image.source": _CONTAINER_SOURCE,
        "org.opencontainers.image.documentation": _CONTAINER_DOCUMENTATION,
        "org.opencontainers.image.licenses": _CONTAINER_LICENSES,
        "org.opencontainers.image.authors": _CONTAINER_AUTHORS,
        "org.opencontainers.image.version": version.text,
        "org.opencontainers.image.revision": commit,
        "org.opencontainers.image.created": created,
    }
    tags = [
        f"{image}:{suffix}"
        for image in _CONTAINER_IMAGES
        for suffix in _container_tag_suffixes(version)
    ]

    return {
        "schemaVersion": 1,
        "product": _CONTAINER_TITLE,
        "version": version.text,
        "revision": commit,
        "created": created,
        "isPrerelease": version.is_prerelease,
        "platforms": list(_CONTAINER_PLATFORMS),
        "images": list(_CONTAINER_IMAGES),
        "tags": tags,
        "labels": labels,
        "annotations": dict(labels),
    }


def _load_container_plan(
    path: Path,
    version: ProductVersion,
    commit: str,
) -> dict[str, object]:
    document = json.loads(path.resolve().read_text(encoding="utf-8"))
    if not isinstance(document, dict):
        raise ValueError("Container release plan must be a JSON object.")

    created = _validate_created(str(document.get("created", "")))
    expected = _build_container_plan(version, commit, created)
    if document != expected:
        raise ValueError(
            "Container release plan does not match the repository's registry, tag, platform, or OCI metadata policy."
        )
    return document


def _container_digest_from_metadata(path: Path) -> str:
    document = json.loads(path.resolve().read_text(encoding="utf-8"))
    if not isinstance(document, dict):
        raise ValueError("Container build metadata must be a JSON object.")

    digest = _validate_digest(str(document.get("containerimage.digest", "")))
    descriptor = document.get("containerimage.descriptor")
    if not isinstance(descriptor, dict):
        raise ValueError("Container build metadata is missing containerimage.descriptor.")
    descriptor_digest = _validate_digest(str(descriptor.get("digest", "")))
    if descriptor_digest != digest:
        raise ValueError(
            "Container build metadata descriptor digest does not match containerimage.digest."
        )
    return digest


def _build_container_distribution(
    plan: dict[str, object],
    digest: str,
) -> dict[str, object]:
    version = str(plan["version"])
    images = [str(image) for image in plan["images"]]
    return {
        "schemaVersion": 1,
        "product": str(plan["product"]),
        "version": version,
        "revision": str(plan["revision"]),
        "created": str(plan["created"]),
        "digest": digest,
        "platforms": list(plan["platforms"]),
        "images": [
            {
                "name": image,
                "versionReference": f"{image}:{version}",
                "immutableReference": f"{image}@{digest}",
            }
            for image in images
        ],
        "attestations": {
            "sbom": {
                "format": "SPDX",
                "predicateType": "https://spdx.dev/Document",
            },
            "provenance": {
                "mode": "max",
                "predicateTypePrefix": "https://slsa.dev/provenance/",
            },
        },
    }


def _container_plan(args: argparse.Namespace) -> int:
    version = ProductVersion.parse(args.version)
    commit = _validate_commit(args.commit)
    created = _validate_created(args.created)
    plan = _build_container_plan(version, commit, created)

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(
        json.dumps(plan, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
        newline="\n",
    )
    print(json.dumps(plan, indent=2, sort_keys=True))
    return 0


def _verify_container_plan(args: argparse.Namespace) -> int:
    version = ProductVersion.parse(args.version)
    commit = _validate_commit(args.commit)
    _load_container_plan(args.plan, version, commit)

    print(
        f"Verified container release plan for {version.text} at {commit} "
        f"across {', '.join(_CONTAINER_PLATFORMS)}."
    )
    return 0


def _container_distribution(args: argparse.Namespace) -> int:
    version = ProductVersion.parse(args.version)
    commit = _validate_commit(args.commit)
    plan = _load_container_plan(args.plan, version, commit)
    digest = _container_digest_from_metadata(args.build_metadata)
    distribution = _build_container_distribution(plan, digest)

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(
        json.dumps(distribution, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
        newline="\n",
    )
    print(json.dumps(distribution, indent=2, sort_keys=True))
    return 0


def _verify_container_distribution(args: argparse.Namespace) -> int:
    version = ProductVersion.parse(args.version)
    commit = _validate_commit(args.commit)
    plan = _load_container_plan(args.plan, version, commit)
    digest = _container_digest_from_metadata(args.build_metadata)
    document = json.loads(args.distribution.resolve().read_text(encoding="utf-8"))
    expected = _build_container_distribution(plan, digest)
    if document != expected:
        raise ValueError(
            "Container distribution manifest does not match the validated release plan and immutable build digest."
        )

    print(
        f"Verified container distribution manifest for {version.text} at {commit} "
        f"and immutable digest {digest}."
    )
    return 0


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

    container_plan = subparsers.add_parser(
        "container-plan",
        description="Generate deterministic multi-registry container release metadata.",
    )
    container_plan.add_argument("--version", required=True)
    container_plan.add_argument("--commit", required=True)
    container_plan.add_argument("--created", required=True)
    container_plan.add_argument("--output", type=Path, required=True)
    container_plan.set_defaults(handler=_container_plan)

    verify_container_plan = subparsers.add_parser(
        "verify-container-plan",
        description="Verify container registry, tag, platform, and OCI metadata policy.",
    )
    verify_container_plan.add_argument("--version", required=True)
    verify_container_plan.add_argument("--commit", required=True)
    verify_container_plan.add_argument("--plan", type=Path, required=True)
    verify_container_plan.set_defaults(handler=_verify_container_plan)

    container_distribution = subparsers.add_parser(
        "container-distribution",
        description="Generate immutable container distribution metadata from Buildx metadata.",
    )
    container_distribution.add_argument("--version", required=True)
    container_distribution.add_argument("--commit", required=True)
    container_distribution.add_argument("--plan", type=Path, required=True)
    container_distribution.add_argument("--build-metadata", type=Path, required=True)
    container_distribution.add_argument("--output", type=Path, required=True)
    container_distribution.set_defaults(handler=_container_distribution)

    verify_container_distribution = subparsers.add_parser(
        "verify-container-distribution",
        description="Verify the immutable container distribution manifest.",
    )
    verify_container_distribution.add_argument("--version", required=True)
    verify_container_distribution.add_argument("--commit", required=True)
    verify_container_distribution.add_argument("--plan", type=Path, required=True)
    verify_container_distribution.add_argument("--build-metadata", type=Path, required=True)
    verify_container_distribution.add_argument("--distribution", type=Path, required=True)
    verify_container_distribution.set_defaults(handler=_verify_container_distribution)

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
