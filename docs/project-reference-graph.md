# Project-reference graph

DotNetRepoInspector builds project-reference edges from the **evaluated** MSBuild `ProjectReference` item collection rather than parsing raw project XML. This means MSBuild conditions, imports, and item transformations are respected before a dependency becomes part of the graph.

## Path semantics

Reference paths in the normalized contract are relative to the inspection root and use `/` as the separator.

- A project inside the inspection root is represented as `src/Library/Library.csproj`.
- An existing project outside the inspection root is retained using a repository-relative path such as `../Shared/Shared.csproj`.
- Absolute checkout paths are not exposed in the normalized reference metadata.

This keeps output stable across machines while still making external references identifiable.

## Unresolved references

A `ProjectReference` whose evaluated target does not exist is not discarded. The graph keeps the edge and adds warning diagnostic `DRI1003` (`ProjectReferenceUnresolved`) to the source project. Diagnostic context contains the normalized `referencePath` and does not expose the absolute checkout path.

## Cycles

The graph is an adjacency representation. Circular references are represented as ordinary outgoing edges and do not cause recursive traversal, so cycles such as `A → B → A` are deterministic and safe to inspect.
