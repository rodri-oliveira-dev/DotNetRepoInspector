param(
    [Parameter(Mandatory = $true)]
    [string]$PackageSource,

    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$FixturePath,

    [string]$ArtifactsDirectory = "artifacts/mcp-package-smoke",

    [ValidateRange(1, 20)]
    [int]$MaxAttempts = 1,

    [ValidateRange(0, 120)]
    [int]$RetryDelaySeconds = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$fixtureFullPath = (Resolve-Path -LiteralPath $FixturePath).Path
$artifactsFullPath = [IO.Path]::GetFullPath($ArtifactsDirectory)
New-Item -ItemType Directory -Path $artifactsFullPath -Force | Out-Null

$nugetPackagesPath = Join-Path $artifactsFullPath "nuget-packages"
$nugetConfigPath = Join-Path $artifactsFullPath "NuGet.PackageSmoke.Config"
$escapedPackageSource = [Security.SecurityElement]::Escape($PackageSource)
@"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="package-smoke-source" value="$escapedPackageSource" />
  </packageSources>
</configuration>
"@ | Set-Content -LiteralPath $nugetConfigPath -Encoding utf8

@{
    packageId = "DotNetRepoInspector.Mcp"
    version = $Version
    packageSource = $PackageSource
    nugetConfig = $nugetConfigPath
    nugetPackages = $nugetPackagesPath
    noHttpCache = $true
    sourceIsolation = "exclusive"
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $artifactsFullPath "package-source-evidence.json") -Encoding utf8

$datasetPath = Join-Path $artifactsFullPath "package-smoke-dataset.json"
$dataset = @{
    schemaVersion = 1
    id = "mcp-package-smoke-v1"
    description = "Deterministic packaged MCP handshake and inspect_repository smoke."
    cases = @(
        @{
            id = "packaged-inspect-repository"
            question = "Inspect the packaged ProjectKinds fixture."
            fixturePath = "."
            expectedTool = "inspect_repository"
            assertions = @(
                @{
                    kind = "project_classification"
                    projectPath = "Web/Web.csproj"
                    value = "web"
                }
            )
        }
    )
}
$dataset | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $datasetPath -Encoding utf8

$serverArgumentsPath = Join-Path $artifactsFullPath "dnx-server-arguments.json"
@(
    "DotNetRepoInspector.Mcp@$Version",
    "--configfile", $nugetConfigPath,
    "--source", $PackageSource,
    "--no-http-cache",
    "--yes",
    "--"
) | ConvertTo-Json | Set-Content -LiteralPath $serverArgumentsPath -Encoding utf8

$evalArguments = @(
    "run",
    "--project", "./evals/DotNetRepoInspector.Mcp.Evals/DotNetRepoInspector.Mcp.Evals.csproj",
    "--configuration", "Release",
    "--no-build",
    "--",
    "--server", "dnx",
    "--server-arguments-file", $serverArgumentsPath,
    "--fixtures", $fixtureFullPath,
    "--dataset", $datasetPath,
    "--output", $artifactsFullPath,
    "--client", "dnx-package-smoke",
    "--provider", "protocol",
    "--model", "deterministic-assertions",
    "--client-version", $Version
)

$previousNuGetPackages = $env:NUGET_PACKAGES
$succeeded = $false

try {
    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        if (Test-Path -LiteralPath $nugetPackagesPath) {
            Remove-Item -LiteralPath $nugetPackagesPath -Recurse -Force
        }
        New-Item -ItemType Directory -Path $nugetPackagesPath -Force | Out-Null
        $env:NUGET_PACKAGES = $nugetPackagesPath

        Write-Host "Packaged MCP smoke attempt $attempt/$MaxAttempts from exclusive source '$PackageSource' with isolated NuGet package cache '$nugetPackagesPath'."
        & dotnet @evalArguments

        if ($LASTEXITCODE -eq 0) {
            $succeeded = $true
            break
        }

        if ($attempt -lt $MaxAttempts -and $RetryDelaySeconds -gt 0) {
            Start-Sleep -Seconds $RetryDelaySeconds
        }
    }
}
finally {
    if ([string]::IsNullOrEmpty($previousNuGetPackages)) {
        Remove-Item Env:NUGET_PACKAGES -ErrorAction SilentlyContinue
    }
    else {
        $env:NUGET_PACKAGES = $previousNuGetPackages
    }
}

if (-not $succeeded) {
    throw "The exact MCP package version failed its dnx smoke after $MaxAttempts attempt(s)."
}

Write-Host "The exact MCP package version completed dnx resolution from the exclusive package source with an isolated cache, stdio handshake, discovery, and inspect_repository."
