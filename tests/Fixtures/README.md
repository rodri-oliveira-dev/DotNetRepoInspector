# Inspection fixtures

This directory contains synthetic .NET repository/project structures used to prove discovery, MSBuild evaluation, and classification behavior.

Fixtures must be intentionally minimal and must not contain real application source or secrets.

`Directory.Build.props` and `Directory.Packages.props` in this directory intentionally stop fixture projects from inheriting DotNetRepoInspector's own root build/package configuration. Individual fixture repositories may add their own local configuration when a test needs to exercise inheritance explicitly.
