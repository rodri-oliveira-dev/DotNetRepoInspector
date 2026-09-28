# Windows Service Worker signal

This fixture uses the common `Microsoft.NET.Sdk` and references `Microsoft.Extensions.Hosting.WindowsServices`.

The package provides Windows Service lifetime integration for a .NET executable. ADR 0010 treats this as a moderate Worker signal when stronger Test/Web evidence is absent. It mirrors the approved systemd service-lifetime signal without relying on project names or source-code inspection.
