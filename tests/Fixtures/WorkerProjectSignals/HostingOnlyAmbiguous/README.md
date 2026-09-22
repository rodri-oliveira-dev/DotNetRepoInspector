# Generic Host ambiguity

This fixture is an executable common-SDK project with an explicit `Microsoft.Extensions.Hosting` reference.

That shape can be a Worker, but it can also be a normal console application using Generic Host for dependency injection, configuration, logging, or lifetime management. Issue #47 therefore keeps `Microsoft.Extensions.Hosting` alone as supporting evidence only and does not approve it as an authoritative Worker rule.
