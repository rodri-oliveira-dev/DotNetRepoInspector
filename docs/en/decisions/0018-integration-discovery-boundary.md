# ADR 0018: Keep Integration Discovery opt-in and isolated

- Status: Accepted
- Date: 2026-10-01

## Context

The stable inspection contract is based on evaluated MSBuild and Git facts. Discovering outbound integrations requires bounded inspection of C# source, but source code is sensitive input and must not become collected output. Provider rules also need to evolve without coupling the Core or Engine to individual SDKs.

## Decision

`DotNetRepoInspector.Core` owns only the normalized `IntegrationFinding` contract and its validation. `DotNetRepoInspector.IntegrationDiscovery` owns source handling and the `IIntegrationDetector` extension point. Concrete detectors remain isolated behind that interface. The Engine will invoke one discovery pipeline only when a single product opt-in is enabled; delivery adapters will expose that option without implementing detection rules.

Discovery is syntax-only. It does not compile, load, invoke, or otherwise execute target repository code. MSBuild and Git remain authoritative for structured repository facts, while source syntax supplies only integration evidence with relative path and line provenance.

Every run must enforce configurable ceilings for paths visited, eligible files, bytes read, findings returned, and elapsed work. Cancellation must be propagated through enumeration, parsing, and detectors. Reaching a ceiling returns deterministic partial output with explicit truncation metadata or diagnostics; it must not silently broaden the scan.

Only allow-listed evidence may cross into `InspectionReport`: logical resource names, hostnames when directly observed, configuration keys without values, type or contract names, locations, confidence, and stable signal codes. Source bodies, payloads, queries, connection strings, configuration values, credentials, tokens, and authentication data are never findings or diagnostics.

Schema `1.6` adds optional top-level `integrations`. Canonical serialization emits an empty array when discovery is disabled or produces no findings, while compatible older `1.x` payloads may omit it. IDs derive from canonical finding identity, paths and signals are normalized, and ordering is deterministic.

## Consequences

- Existing inspection behavior and cost remain unchanged unless discovery is enabled.
- Core remains independent of Roslyn and provider SDKs.
- Detector authors receive source as transient analysis input but cannot add arbitrary output fields.
- Syntax-only analysis is intentionally conservative and cannot prove runtime topology or remote ownership.
- Numeric defaults and operational truncation behavior belong to the discovery pipeline implementation and can evolve independently of the public finding shape.

## Rejected alternatives

- Making source analysis mandatory would change default cost and trust boundaries.
- Putting provider logic in Engine or delivery adapters would duplicate behavior and reverse dependency direction.
- Serializing arbitrary syntax, configuration values, or complete property bags would create an unacceptable data-exposure surface.
- Requiring semantic compilation would execute a broader and less predictable toolchain and is not needed for the first catalog.
