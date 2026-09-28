# Multi-targeted conditional Worker signals

These projects prove that Worker signals conditioned on `TargetFramework` are evaluated in each MSBuild inner build. They also ensure that executable output and a service-lifetime package must occur in the same target framework before the package is treated as a Worker signal.
