# Systemd service integration signal

This fixture represents a long-running service built with the common `Microsoft.NET.Sdk`: executable output plus an explicit `Microsoft.Extensions.Hosting.Systemd` package reference.

The package exists specifically to host a .NET application as a Linux systemd service. Issue #47 treats this as a moderate Worker signal when stronger Test/Web evidence is absent. The same decision applies to `Microsoft.Extensions.Hosting.WindowsServices`.

Production classification intentionally remains `console` until #152 implements the approved fallback.
