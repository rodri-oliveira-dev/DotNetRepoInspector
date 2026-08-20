param(
    [Parameter(Mandatory = $true)]
    [string]$PackageDirectory,

    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$FixturePath = "tests/Fixtures/ProjectKinds",

    [string]$ArtifactsDirectory = "artifacts/tool-package-smoke"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Assert-Equal {
    param(
        [string]$Name,
        [string]$Expected,
        [string]$Actual
    )

    if (-not [string]::Equals($Expected, $Actual, [StringComparison]::Ordinal)) {
        throw "$Name mismatch. Expected '$Expected', actual '$Actual'."
    }
}

function Invoke-DotNet {
    param([string[]]$Arguments)

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Invoke-DotNetCapture {
    param([string[]]$Arguments)

    $output = @(& dotnet @Arguments)
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }

    return $output
}

function Get-MetadataNode {
    param(
        [System.Xml.XmlNode]$Metadata,
        [string]$Name
    )

    $node = $Metadata.SelectSingleNode("*[local-name()='$Name']")
    if ($null -eq $node) {
        throw "NuGet metadata '$Name' was not found."
    }

    return $node
}

$packageDirectoryPath = (Resolve-Path -LiteralPath $PackageDirectory).Path
$fixtureFullPath = (Resolve-Path -LiteralPath $FixturePath).Path
$artifactsFullPath = [IO.Path]::GetFullPath($ArtifactsDirectory)
New-Item -ItemType Directory -Path $artifactsFullPath -Force | Out-Null

$packages = @(Get-ChildItem -LiteralPath $packageDirectoryPath -Filter "DotNetRepoInspector.*.nupkg" -File)
if ($packages.Count -ne 1) {
    throw "Expected exactly one DotNetRepoInspector .nupkg in '$packageDirectoryPath', found $($packages.Count)."
}

$package = $packages[0]
Write-Host "Validating package: $($package.FullName)"

$archive = [IO.Compression.ZipFile]::OpenRead($package.FullName)
try {
    $nuspecEntry = $archive.Entries |
        Where-Object { $_.FullName.EndsWith(".nuspec", [StringComparison]::OrdinalIgnoreCase) } |
        Select-Object -First 1

    if ($null -eq $nuspecEntry) {
        throw "The package does not contain a .nuspec file."
    }

    $reader = [IO.StreamReader]::new($nuspecEntry.Open())
    try {
        [xml]$nuspec = $reader.ReadToEnd()
    }
    finally {
        $reader.Dispose()
    }

    $metadata = $nuspec.SelectSingleNode("/*[local-name()='package']/*[local-name()='metadata']")
    if ($null -eq $metadata) {
        throw "NuGet package metadata was not found."
    }

    Assert-Equal "Package ID" "DotNetRepoInspector" (Get-MetadataNode $metadata "id").InnerText
    Assert-Equal "Package version" $Version (Get-MetadataNode $metadata "version").InnerText
    Assert-Equal "Authors" "Rodrigo de Oliveira" (Get-MetadataNode $metadata "authors").InnerText

    $description = (Get-MetadataNode $metadata "description").InnerText
    if ([string]::IsNullOrWhiteSpace($description)) {
        throw "Package description must not be empty."
    }

    $license = Get-MetadataNode $metadata "license"
    Assert-Equal "License type" "expression" $license.Attributes["type"].Value
    Assert-Equal "License" "MIT" $license.InnerText

    $repository = Get-MetadataNode $metadata "repository"
    Assert-Equal "Repository type" "git" $repository.Attributes["type"].Value
    Assert-Equal "Repository URL" "https://github.com/rodri-oliveira-dev/DotNetRepoInspector" $repository.Attributes["url"].Value

    $packageType = $metadata.SelectSingleNode("*[local-name()='packageTypes']/*[local-name()='packageType']")
    if ($null -eq $packageType) {
        throw "NuGet package type metadata was not found."
    }

    Assert-Equal "Package type" "DotnetTool" $packageType.Attributes["name"].Value

    $tags = (Get-MetadataNode $metadata "tags").InnerText
    $tagValues = @($tags -split '[;\s]+' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    foreach ($requiredTag in @("dotnet", "tool", "msbuild", "inspection")) {
        if ($tagValues -notcontains $requiredTag) {
            throw "Required NuGet tag '$requiredTag' was not found."
        }
    }

    $dependencies = @($metadata.SelectNodes(".//*[local-name()='dependency']"))
    if ($dependencies.Count -ne 0) {
        throw "The tool package must not declare unpublished DotNetRepoInspector project packages as dependencies."
    }

    $entryNames = @($archive.Entries | ForEach-Object { $_.FullName })
    foreach ($assemblyName in @(
        "DotNetRepoInspector.Cli.dll",
        "DotNetRepoInspector.Core.dll",
        "DotNetRepoInspector.Engine.dll",
        "DotNetRepoInspector.Git.dll",
        "DotNetRepoInspector.MSBuild.dll")) {
        if (-not ($entryNames | Where-Object { $_.EndsWith("/$assemblyName", [StringComparison]::Ordinal) })) {
            throw "Required runtime assembly '$assemblyName' was not included in the tool package."
        }
    }

    if (-not ($entryNames -contains "README.md")) {
        throw "The package README was not included."
    }

    if (-not ($entryNames | Where-Object { $_.EndsWith("/DotnetToolSettings.xml", [StringComparison]::Ordinal) })) {
        throw "DotnetToolSettings.xml was not included; the package is not a valid .NET Tool package."
    }

    $unnecessaryEntries = @($entryNames | Where-Object {
        $_.EndsWith(".cs", [StringComparison]::OrdinalIgnoreCase) -or
        $_.EndsWith(".csproj", [StringComparison]::OrdinalIgnoreCase) -or
        $_.EndsWith(".sln", [StringComparison]::OrdinalIgnoreCase) -or
        $_.EndsWith(".slnx", [StringComparison]::OrdinalIgnoreCase)
    })

    if ($unnecessaryEntries.Count -ne 0) {
        throw "Unexpected development files were included in the tool package: $($unnecessaryEntries -join ', ')."
    }
}
finally {
    $archive.Dispose()
}

$escapedPackageDirectory = [Security.SecurityElement]::Escape($packageDirectoryPath)
$nugetConfigPath = Join-Path $artifactsFullPath "NuGet.ToolSmoke.Config"
@"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local-tool-package" value="$escapedPackageDirectory" />
  </packageSources>
</configuration>
"@ | Set-Content -LiteralPath $nugetConfigPath -Encoding utf8

$packageId = "DotNetRepoInspector"
$globalInstalled = $false
try {
    Invoke-DotNet @(
        "tool", "install", "--global", $packageId,
        "--version", $Version,
        "--configfile", $nugetConfigPath,
        "--no-cache")
    $globalInstalled = $true

    $userProfile = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
    $globalToolPath = Join-Path $userProfile ".dotnet/tools"
    $env:PATH = "$globalToolPath$([IO.Path]::PathSeparator)$env:PATH"

    $globalHelp = Invoke-DotNetCapture @("repo-inspect", "--help")
    $globalHelp | Set-Content -LiteralPath (Join-Path $artifactsFullPath "global-help.txt") -Encoding utf8
    if (-not (($globalHelp -join [Environment]::NewLine).Contains("Usage:", [StringComparison]::Ordinal))) {
        throw "Global tool --help output does not contain the expected usage section."
    }

    $globalVersion = (Invoke-DotNetCapture @("repo-inspect", "--version") -join [Environment]::NewLine).Trim()
    $globalVersion | Set-Content -LiteralPath (Join-Path $artifactsFullPath "global-version.txt") -Encoding utf8
    Assert-Equal "Global tool reported version" $Version $globalVersion

    $globalInspectionPath = Join-Path $artifactsFullPath "global-inspection.json"
    Invoke-DotNet @("repo-inspect", $fixtureFullPath, "--output", $globalInspectionPath)
    $globalReport = Get-Content -LiteralPath $globalInspectionPath -Raw | ConvertFrom-Json
    if ($null -eq $globalReport.schemaVersion -or @($globalReport.projects).Count -eq 0) {
        throw "Global tool inspection did not produce a valid inspection report."
    }
}
finally {
    if ($globalInstalled) {
        & dotnet tool uninstall --global $packageId | Out-Host
    }
}

$localManifestDirectory = Join-Path $artifactsFullPath "local-manifest"
New-Item -ItemType Directory -Path $localManifestDirectory -Force | Out-Null
$localInstalled = $false
Push-Location $localManifestDirectory
try {
    Invoke-DotNet @("new", "tool-manifest", "--force")
    Invoke-DotNet @(
        "tool", "install", "--local", $packageId,
        "--version", $Version,
        "--configfile", $nugetConfigPath,
        "--no-cache")
    $localInstalled = $true

    $localHelp = Invoke-DotNetCapture @("repo-inspect", "--help")
    $localHelp | Set-Content -LiteralPath (Join-Path $artifactsFullPath "local-help.txt") -Encoding utf8
    if (-not (($localHelp -join [Environment]::NewLine).Contains("Usage:", [StringComparison]::Ordinal))) {
        throw "Local tool --help output does not contain the expected usage section."
    }

    $localVersion = (Invoke-DotNetCapture @("repo-inspect", "--version") -join [Environment]::NewLine).Trim()
    $localVersion | Set-Content -LiteralPath (Join-Path $artifactsFullPath "local-version.txt") -Encoding utf8
    Assert-Equal "Local tool reported version" $Version $localVersion

    $localInspectionPath = Join-Path $artifactsFullPath "local-inspection.json"
    Invoke-DotNet @("repo-inspect", $fixtureFullPath, "--output", $localInspectionPath)
    $localReport = Get-Content -LiteralPath $localInspectionPath -Raw | ConvertFrom-Json
    if ($null -eq $localReport.schemaVersion -or @($localReport.projects).Count -eq 0) {
        throw "Local tool inspection did not produce a valid inspection report."
    }
}
finally {
    if ($localInstalled) {
        & dotnet tool uninstall --local $packageId | Out-Host
    }

    Pop-Location
}

Write-Host "The .NET Tool package metadata, contents, global installation, local installation, help, version, and real inspection smoke tests passed."
