# SDK fixture without global.json

Proves a repository that contains a .NET project but no `global.json` in the fixture tree.

Because DotNetRepoInspector itself has a root `global.json`, SDK-resolution tests must copy this fixture to an isolated temporary workspace before invoking `dotnet`. That preserves the intended "no configured SDK" semantics instead of allowing upward traversal to find the product repository's configuration.
