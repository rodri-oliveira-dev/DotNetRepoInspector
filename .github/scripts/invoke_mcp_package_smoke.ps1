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
    "--source", $PackageSource,
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

for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
    Write-Host "Packaged MCP smoke attempt $attempt/$MaxAttempts from '$PackageSource'."
    & dotnet @evalArguments

    if ($LASTEXITCODE -eq 0) {
        Write-Host "The exact MCP package version completed dnx resolution, stdio handshake, discovery, and inspect_repository."
        exit 0
    }

    if ($attempt -lt $MaxAttempts -and $RetryDelaySeconds -gt 0) {
        Start-Sleep -Seconds $RetryDelaySeconds
    }
}

throw "The exact MCP package version failed its dnx smoke after $MaxAttempts attempt(s)."
