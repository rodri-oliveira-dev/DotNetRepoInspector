# MSBuild evaluation fixture

This fixture proves two related MSBuild evaluation behaviors with one minimal repository structure:

- `Directory.Build.props` supplies `InspectorInheritedProperty`.
- `SampleProject.csproj` evaluates `InspectorConditionalProperty` only when the inherited value is present.

The expected evaluated values are:

- `InspectorInheritedProperty=from-directory-build-props`
- `InspectorConditionalProperty=condition-evaluated`
