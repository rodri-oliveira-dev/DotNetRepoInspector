Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (Get-Variable -Name PSNativeCommandUseErrorActionPreference -ErrorAction SilentlyContinue) {
    $PSNativeCommandUseErrorActionPreference = $false
}

function Set-ActionOutput {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [AllowEmptyString()]
        [string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT)) {
        throw "GITHUB_OUTPUT is not available. This script must run inside GitHub Actions."
    }

    $delimiter = "DRI_$([Guid]::NewGuid().ToString('N'))"
    Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value "$Name<<$delimiter" -Encoding utf8
    Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value $Value -Encoding utf8
    Add-Content -LiteralPath $env:GITHUB_OUTPUT -Value $delimiter -Encoding utf8
}

function Resolve-WorkspacePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [switch]$MustExist
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "A path value cannot be empty."
    }

    $candidate = if ([IO.Path]::IsPathFullyQualified($Path)) {
        [IO.Path]::GetFullPath($Path)
    }
    else {
        [IO.Path]::GetFullPath((Join-Path $env:GITHUB_WORKSPACE $Path))
    }

    if ($MustExist -and -not (Test-Path -LiteralPath $candidate -PathType Container)) {
        throw "Repository path '$candidate' does not exist or is not a directory."
    }

    return $candidate
}

function Get-InputLines {
    param(
        [AllowEmptyString()]
        [string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return @()
    }

    return @(
        $Value -split "\r?\n" |
            ForEach-Object { $_.Trim() } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )
}

if ([string]::IsNullOrWhiteSpace($env:GITHUB_WORKSPACE)) {
    throw "GITHUB_WORKSPACE is not available."
}

if ([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) {
    throw "RUNNER_TEMP is not available."
}

if ([string]::IsNullOrWhiteSpace($env:DRI_TOOL_VERSION)) {
    throw "The Action does not define a pinned DotNetRepoInspector version."
}

$verbosity = if ([string]::IsNullOrWhiteSpace($env:DRI_INPUT_VERBOSITY)) {
    "normal"
}
else {
    $env:DRI_INPUT_VERBOSITY.Trim().ToLowerInvariant()
}

$verbosityArgument = switch ($verbosity) {
    "normal" { $null }
    "verbose" { "--verbose" }
    "debug" { "--debug" }
    default { throw "Unsupported verbosity '$($env:DRI_INPUT_VERBOSITY)'. Expected normal, verbose, or debug." }
}

$noConfig = switch (($env:DRI_INPUT_NO_CONFIG ?? "false").Trim().ToLowerInvariant()) {
    "" { $false }
    "false" { $false }
    "true" { $true }
    default { throw "Unsupported no-config value. Expected true or false." }
}

$repositoryInput = if ([string]::IsNullOrWhiteSpace($env:DRI_INPUT_PATH)) { "." } else { $env:DRI_INPUT_PATH }
$repositoryPath = Resolve-WorkspacePath -Path $repositoryInput -MustExist

$invocationRoot = Join-Path $env:RUNNER_TEMP ("dotnet-repo-inspector/" + [Guid]::NewGuid().ToString("N"))
$toolPath = Join-Path $invocationRoot "tool"
$nugetConfigPath = Join-Path $invocationRoot "NuGet.ActionBootstrap.Config"
New-Item -ItemType Directory -Path $toolPath -Force | Out-Null

$reportPath = if ([string]::IsNullOrWhiteSpace($env:DRI_INPUT_OUTPUT)) {
    Join-Path $invocationRoot "inspection.json"
}
else {
    Resolve-WorkspacePath -Path $env:DRI_INPUT_OUTPUT
}

$reportDirectory = Split-Path -Parent $reportPath
if (-not [string]::IsNullOrWhiteSpace($reportDirectory)) {
    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
}

$packageSource = "https://api.nuget.org/v3/index.json"
$selfTestPackageSource = $env:DOTNET_REPO_INSPECTOR_SELF_TEST_PACKAGE_SOURCE
if (-not [string]::IsNullOrWhiteSpace($selfTestPackageSource)) {
    if (-not [string]::Equals($env:GITHUB_REPOSITORY, "rodri-oliveira-dev/DotNetRepoInspector", [StringComparison]::OrdinalIgnoreCase)) {
        throw "DOTNET_REPO_INSPECTOR_SELF_TEST_PACKAGE_SOURCE is restricted to the DotNetRepoInspector repository CI."
    }

    $packageSource = [IO.Path]::GetFullPath($selfTestPackageSource)
    if (-not (Test-Path -LiteralPath $packageSource -PathType Container)) {
        throw "Self-test package source '$packageSource' does not exist."
    }
}

$escapedPackageSource = [Security.SecurityElement]::Escape($packageSource)
@"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="dotnet-repo-inspector" value="$escapedPackageSource" />
  </packageSources>
</configuration>
"@ | Set-Content -LiteralPath $nugetConfigPath -Encoding utf8

Push-Location $invocationRoot
try {
    & dotnet tool install DotNetRepoInspector `
        --tool-path $toolPath `
        --version $env:DRI_TOOL_VERSION `
        --configfile $nugetConfigPath `
        --no-cache

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to install DotNetRepoInspector $($env:DRI_TOOL_VERSION) from the isolated package source."
    }
}
finally {
    Pop-Location
}

$toolCommandName = if ($IsWindows) { "dotnet-repo-inspect.exe" } else { "dotnet-repo-inspect" }
$toolCommand = Join-Path $toolPath $toolCommandName
if (-not (Test-Path -LiteralPath $toolCommand -PathType Leaf)) {
    throw "The installed tool command '$toolCommand' was not found."
}

$arguments = @($repositoryPath, "--output", $reportPath)
if ($null -ne $verbosityArgument) {
    $arguments += $verbosityArgument
}

if (-not [string]::IsNullOrWhiteSpace($env:DRI_INPUT_CONFIG)) {
    $arguments += @("--config", $env:DRI_INPUT_CONFIG)
}

if ($noConfig) {
    $arguments += "--no-config"
}

foreach ($excludedPath in @(Get-InputLines -Value $env:DRI_INPUT_EXCLUDE)) {
    $arguments += @("--exclude", $excludedPath)
}

foreach ($classificationOverride in @(Get-InputLines -Value $env:DRI_INPUT_CLASSIFY)) {
    $arguments += @("--classify", $classificationOverride)
}

& $toolCommand @arguments
$exitCode = $LASTEXITCODE

Set-ActionOutput -Name "inspector-version" -Value $env:DRI_TOOL_VERSION
Set-ActionOutput -Name "exit-code" -Value ([string]$exitCode)

if (Test-Path -LiteralPath $reportPath -PathType Leaf) {
    Set-ActionOutput -Name "report-path" -Value ([IO.Path]::GetFullPath($reportPath))

    $schemaVersion = ""
    try {
        $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
        if ($null -ne $report.schemaVersion) {
            $schemaVersion = [string]$report.schemaVersion
        }
    }
    catch {
        Write-Warning "The inspection report exists but schemaVersion could not be read: $($_.Exception.Message)"
    }

    Set-ActionOutput -Name "schema-version" -Value $schemaVersion
}
else {
    Set-ActionOutput -Name "report-path" -Value ""
    Set-ActionOutput -Name "schema-version" -Value ""
}

exit $exitCode
