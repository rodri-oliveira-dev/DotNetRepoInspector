Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (Get-Variable -Name PSNativeCommandUseErrorActionPreference -ErrorAction SilentlyContinue) {
    $PSNativeCommandUseErrorActionPreference = $false
}

. (Join-Path $PSScriptRoot "repository-exclusion.ps1")

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

function Set-SkippedRepositoryOutputs {
    Set-ActionOutput -Name "repository-excluded" -Value "true"
    Set-ActionOutput -Name "inspector-version" -Value ""
    Set-ActionOutput -Name "exit-code" -Value "0"
    Set-ActionOutput -Name "report-path" -Value ""
    Set-ActionOutput -Name "schema-version" -Value ""
}

function Get-RemoteActionTags {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Repository
    )

    if ($Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') {
        throw "Cannot resolve the Tool version for Action repository '$Repository'."
    }

    $remoteUrl = "https://github.com/$Repository.git"
    $remoteTags = @(& git ls-remote --tags $remoteUrl)
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to read release tags for Action repository '$Repository'."
    }

    return $remoteTags
}

function Resolve-ToolVersionFromRef {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ActionRef,

        [Parameter(Mandatory = $true)]
        [string]$Repository
    )

    $normalizedRef = $ActionRef.Trim()
    if ($normalizedRef -match '^v(?<version>(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?)$') {
        return $Matches.version
    }

    $isCommit = $normalizedRef -match '^[0-9a-fA-F]{40}$'
    $isMajorAlias = $normalizedRef -match '^v(?<aliasMajor>0|[1-9]\d*)$'
    $aliasMajor = if ($isMajorAlias) { $Matches.aliasMajor } else { $null }
    $isMinorAlias = $normalizedRef -match '^v(?<aliasMajor>0|[1-9]\d*)\.(?<aliasMinor>0|[1-9]\d*)$'
    $aliasMinor = if ($isMinorAlias) { $Matches.aliasMinor } else { $null }
    if ($isMinorAlias) {
        $aliasMajor = $Matches.aliasMajor
    }

    if (-not $isCommit -and -not $isMajorAlias -and -not $isMinorAlias) {
        throw "Action ref '$normalizedRef' must be an immutable version tag, a stable major/minor alias, or a full commit SHA."
    }

    $remoteTags = @(Get-RemoteActionTags -Repository $Repository)
    $targetCommit = if ($isCommit) {
        $normalizedRef.ToLowerInvariant()
    }
    else {
        $directRef = "refs/tags/$normalizedRef"
        $peeledRef = "$directRef^{}"
        $peeledCommits = @(
            foreach ($line in $remoteTags) {
                $parts = $line -split '\s+', 2
                if ($parts.Count -eq 2 -and $parts[1] -eq $peeledRef) {
                    $parts[0].ToLowerInvariant()
                }
            }
        )
        $aliasCommits = if ($peeledCommits.Count -gt 0) {
            $peeledCommits
        }
        else {
            @(
                foreach ($line in $remoteTags) {
                    $parts = $line -split '\s+', 2
                    if ($parts.Count -eq 2 -and $parts[1] -eq $directRef) {
                        $parts[0].ToLowerInvariant()
                    }
                }
            )
        }

        $uniqueAliasCommits = @($aliasCommits | Sort-Object -Unique)
        if ($uniqueAliasCommits.Count -ne 1) {
            throw "Action alias '$normalizedRef' did not resolve to exactly one commit."
        }

        $uniqueAliasCommits[0]
    }

    $versions = @(
        foreach ($line in $remoteTags) {
            $parts = $line -split '\s+', 2
            if ($parts.Count -ne 2 -or $parts[0].ToLowerInvariant() -ne $targetCommit) {
                continue
            }

            if ($parts[1] -match '^refs/tags/v(?<version>(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?)(?:\^\{\})?$') {
                $Matches.version
            }
        }
    )

    $uniqueVersions = @($versions | Sort-Object -Unique)
    if ($uniqueVersions.Count -eq 0) {
        throw "Action ref '$normalizedRef' does not resolve to an immutable Semantic Version tag."
    }

    if ($uniqueVersions.Count -ne 1) {
        throw "Action ref '$normalizedRef' maps to multiple immutable versions: $($uniqueVersions -join ', ')."
    }

    $resolvedVersion = $uniqueVersions[0]
    if ($isMajorAlias -and -not $resolvedVersion.StartsWith("$aliasMajor.", [StringComparison]::Ordinal)) {
        throw "Action alias '$normalizedRef' resolved outside its major version line."
    }

    if ($isMinorAlias -and -not $resolvedVersion.StartsWith("$aliasMajor.$aliasMinor.", [StringComparison]::Ordinal)) {
        throw "Action alias '$normalizedRef' resolved outside its minor version line."
    }

    return $resolvedVersion
}

function Resolve-ToolVersionSpec {
    $selfTestVersion = $env:DOTNET_REPO_INSPECTOR_SELF_TEST_TOOL_VERSION
    if (-not [string]::IsNullOrWhiteSpace($selfTestVersion)) {
        return $selfTestVersion.Trim()
    }

    $actionRef = $env:DOTNET_REPO_INSPECTOR_ACTION_REF
    $actionRepository = $env:DOTNET_REPO_INSPECTOR_ACTION_REPOSITORY
    if ([string]::IsNullOrWhiteSpace($actionRef) -or [string]::IsNullOrWhiteSpace($actionRepository)) {
        throw "The Action ref and repository are required to resolve an exact DotNetRepoInspector version."
    }

    return Resolve-ToolVersionFromRef -ActionRef $actionRef -Repository $actionRepository
}

if ([string]::IsNullOrWhiteSpace($env:GITHUB_WORKSPACE)) {
    throw "GITHUB_WORKSPACE is not available."
}

if (Test-RepositoryExcluded -Repository $env:GITHUB_REPOSITORY -ExcludedRepositories $env:DRI_INPUT_EXCLUDE_REPOSITORIES) {
    Write-Host "Repository '$env:GITHUB_REPOSITORY' is explicitly excluded from fleet inventory."
    Set-SkippedRepositoryOutputs
    exit 0
}

if ([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) {
    throw "RUNNER_TEMP is not available."
}

$toolVersionSpec = Resolve-ToolVersionSpec

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

$noConfigInput = $env:DRI_INPUT_NO_CONFIG
if ([string]::IsNullOrWhiteSpace($noConfigInput)) {
    $noConfigInput = "false"
}

$noConfig = switch ($noConfigInput.Trim().ToLowerInvariant()) {
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

$installArguments = @(
    "tool",
    "install",
    "DotNetRepoInspector",
    "--tool-path",
    $toolPath,
    "--version",
    $toolVersionSpec,
    "--configfile",
    $nugetConfigPath,
    "--no-cache"
)

Push-Location $invocationRoot
try {
    & dotnet @installArguments

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to install DotNetRepoInspector version '$toolVersionSpec' from the isolated package source."
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

$versionOutput = @(& $toolCommand --version)
if ($LASTEXITCODE -ne 0) {
    throw "The installed DotNetRepoInspector version could not be determined."
}

$installedToolVersion = ($versionOutput -join [Environment]::NewLine).Trim()
if ([string]::IsNullOrWhiteSpace($installedToolVersion)) {
    throw "The installed DotNetRepoInspector returned an empty version."
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

$sinkUrl = $env:DRI_INPUT_SINK_URL
if ([string]::IsNullOrWhiteSpace($sinkUrl)) {
    if (-not [string]::IsNullOrWhiteSpace($env:DOTNET_REPO_INSPECTOR_HTTP_TOKEN)) {
        throw "sink-token requires sink-url."
    }
}
else {
    $arguments += @("--sink", "http", "--sink-url", $sinkUrl)
    $arguments += @(
        "--sink-timeout-seconds",
        $(if ([string]::IsNullOrWhiteSpace($env:DRI_INPUT_SINK_TIMEOUT_SECONDS)) { "15" } else { $env:DRI_INPUT_SINK_TIMEOUT_SECONDS }),
        "--sink-failure-mode",
        $(if ([string]::IsNullOrWhiteSpace($env:DRI_INPUT_SINK_FAILURE_MODE)) { "non-fatal" } else { $env:DRI_INPUT_SINK_FAILURE_MODE }),
        "--sink-max-attempts",
        $(if ([string]::IsNullOrWhiteSpace($env:DRI_INPUT_SINK_MAX_ATTEMPTS)) { "3" } else { $env:DRI_INPUT_SINK_MAX_ATTEMPTS }))
}

& $toolCommand @arguments
$exitCode = $LASTEXITCODE

Set-ActionOutput -Name "repository-excluded" -Value "false"
Set-ActionOutput -Name "inspector-version" -Value $installedToolVersion
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
