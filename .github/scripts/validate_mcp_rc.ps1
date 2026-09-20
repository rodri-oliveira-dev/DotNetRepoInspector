param(
    [Parameter(Mandatory = $true)]
    [string]$PackageDirectory,

    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$ArtifactsDirectory = "artifacts/mcp-rc-validation"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($Version -notmatch '^\d+\.\d+\.\d+-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*$') {
    throw "RC validation requires an exact Semantic Version prerelease."
}

$packageSource = (Resolve-Path -LiteralPath $PackageDirectory).Path
$artifactsPath = [IO.Path]::GetFullPath($ArtifactsDirectory)
New-Item -ItemType Directory -Path $artifactsPath -Force | Out-Null

& ./.github/scripts/validate_mcp_package.ps1 `
    -PackageDirectory $packageSource `
    -Version $Version `
    -FixturePath ./tests/Fixtures/ProjectKinds `
    -ArtifactsDirectory (Join-Path $artifactsPath "package")
if ($LASTEXITCODE -ne 0) { throw "Packaged MCP validation failed." }

$serverArgumentsPath = Join-Path $artifactsPath "dnx-server-arguments.json"
@(
    "DotNetRepoInspector.Mcp@$Version",
    "--source", $packageSource,
    "--yes",
    "--"
) | ConvertTo-Json | Set-Content -LiteralPath $serverArgumentsPath -Encoding utf8

& dotnet run `
    --project ./evals/DotNetRepoInspector.Mcp.Evals/DotNetRepoInspector.Mcp.Evals.csproj `
    --configuration Release `
    --no-build `
    -- `
    --server dnx `
    --server-arguments-file $serverArgumentsPath `
    --fixtures ./tests/Fixtures `
    --dataset ./evals/DotNetRepoInspector.Mcp.Evals/Dataset/mcp-evals-v1.json `
    --output (Join-Path $artifactsPath "evals") `
    --client dnx-rc-controlled-feed `
    --provider protocol `
    --model deterministic-assertions `
    --client-version $Version
if ($LASTEXITCODE -ne 0) { throw "RC deterministic evals from the controlled feed failed." }

$repositoryDatasetPath = Join-Path $artifactsPath "repository-smoke-dataset.json"
@{
    schemaVersion = 1
    id = "mcp-rc-repository-smoke-v1"
    description = "Packaged MCP smoke against the DotNetRepoInspector solution."
    cases = @(
        @{
            id = "inspect-real-repository"
            question = "Inspect the MCP project in the real repository."
            fixturePath = "."
            expectedTool = "list_projects"
            assertions = @(
                @{
                    kind = "project_target_frameworks"
                    projectPath = "src/DotNetRepoInspector.Mcp/DotNetRepoInspector.Mcp.csproj"
                    values = @("net10.0")
                }
            )
        }
    )
} | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $repositoryDatasetPath -Encoding utf8

& dotnet run `
    --project ./evals/DotNetRepoInspector.Mcp.Evals/DotNetRepoInspector.Mcp.Evals.csproj `
    --configuration Release `
    --no-build `
    -- `
    --server dnx `
    --server-arguments-file $serverArgumentsPath `
    --fixtures . `
    --dataset $repositoryDatasetPath `
    --output (Join-Path $artifactsPath "repository") `
    --client dnx-rc-controlled-feed `
    --provider protocol `
    --model deterministic-assertions `
    --client-version $Version
if ($LASTEXITCODE -ne 0) { throw "RC packaged-server repository smoke failed." }

Write-Host "RC package validation, deterministic evals, and repository smoke passed for exact version $Version."
