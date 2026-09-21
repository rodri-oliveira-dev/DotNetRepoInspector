param(
    [Parameter(Mandatory = $true)]
    [string]$PackageDirectory,

    [Parameter(Mandatory = $true)]
    [string]$Version,

    [string]$FixturePath = "tests/Fixtures/ProjectKinds",

    [string]$ArtifactsDirectory = "artifacts/mcp-package-validation",

    [string]$McpPublisherPath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Assert-Equal {
    param([string]$Name, [string]$Expected, [string]$Actual)
    if (-not [string]::Equals($Expected, $Actual, [StringComparison]::Ordinal)) {
        throw "$Name mismatch. Expected '$Expected', actual '$Actual'."
    }
}

function Get-MetadataNode {
    param([System.Xml.XmlNode]$Metadata, [string]$Name)
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

$packages = @(Get-ChildItem -LiteralPath $packageDirectoryPath -Filter "DotNetRepoInspector.Mcp.*.nupkg" -File)
if ($packages.Count -ne 1) {
    throw "Expected exactly one DotNetRepoInspector.Mcp .nupkg, found $($packages.Count)."
}
$symbols = @(Get-ChildItem -LiteralPath $packageDirectoryPath -Filter "DotNetRepoInspector.Mcp.*.snupkg" -File)
if ($symbols.Count -ne 1) {
    throw "Expected exactly one DotNetRepoInspector.Mcp .snupkg, found $($symbols.Count)."
}

$package = $packages[0]
$archive = [IO.Compression.ZipFile]::OpenRead($package.FullName)
try {
    $nuspecEntry = $archive.Entries |
        Where-Object { $_.FullName.EndsWith(".nuspec", [StringComparison]::OrdinalIgnoreCase) } |
        Select-Object -First 1
    if ($null -eq $nuspecEntry) { throw "The MCP package does not contain a nuspec." }

    $reader = [IO.StreamReader]::new($nuspecEntry.Open())
    try { [xml]$nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
    $metadata = $nuspec.SelectSingleNode("/*[local-name()='package']/*[local-name()='metadata']")
    if ($null -eq $metadata) { throw "NuGet package metadata was not found." }

    Assert-Equal "Package ID" "DotNetRepoInspector.Mcp" (Get-MetadataNode $metadata "id").InnerText
    Assert-Equal "Package version" $Version (Get-MetadataNode $metadata "version").InnerText
    Assert-Equal "Authors" "Rodrigo de Oliveira" (Get-MetadataNode $metadata "authors").InnerText
    Assert-Equal "License" "MIT" (Get-MetadataNode $metadata "license").InnerText
    Assert-Equal "Package icon metadata" "nuget-icon.png" (Get-MetadataNode $metadata "icon").InnerText

    $repository = Get-MetadataNode $metadata "repository"
    Assert-Equal "Repository URL" "https://github.com/rodri-oliveira-dev/DotNetRepoInspector" $repository.Attributes["url"].Value

    $packageTypes = @($metadata.SelectNodes("*[local-name()='packageTypes']/*[local-name()='packageType']") |
        ForEach-Object { $_.Attributes["name"].Value })
    foreach ($requiredType in @("DotnetTool", "McpServer")) {
        if ($packageTypes -notcontains $requiredType) {
            throw "Required package type '$requiredType' was not found."
        }
    }

    $dependencies = @($metadata.SelectNodes(".//*[local-name()='dependency']"))
    if ($dependencies.Count -ne 0) {
        throw "The MCP tool package must not declare package dependencies."
    }

    $entryNames = @($archive.Entries | ForEach-Object { $_.FullName })
    foreach ($requiredEntry in @("README.md", "LICENSE", "nuget-icon.png", ".mcp/server.json")) {
        if ($entryNames -notcontains $requiredEntry) {
            throw "Required package entry '$requiredEntry' was not found."
        }
    }
    foreach ($assemblyName in @(
        "DotNetRepoInspector.Mcp.dll",
        "DotNetRepoInspector.Core.dll",
        "DotNetRepoInspector.Engine.dll",
        "DotNetRepoInspector.Git.dll",
        "DotNetRepoInspector.MSBuild.dll")) {
        if (-not ($entryNames | Where-Object { $_.EndsWith("/$assemblyName", [StringComparison]::Ordinal) })) {
            throw "Required runtime assembly '$assemblyName' was not included."
        }
    }
    if (-not ($entryNames | Where-Object { $_.EndsWith("/DotnetToolSettings.xml", [StringComparison]::Ordinal) })) {
        throw "DotnetToolSettings.xml was not included."
    }

    $manifestEntry = $archive.GetEntry(".mcp/server.json")
    if ($null -eq $manifestEntry) {
        throw "The MCP package does not contain .mcp/server.json."
    }

    $manifestStream = $manifestEntry.Open()
    $manifestBuffer = [IO.MemoryStream]::new()
    try {
        $manifestStream.CopyTo($manifestBuffer)
        [byte[]]$manifestBytes = $manifestBuffer.ToArray()
    }
    finally {
        $manifestStream.Dispose()
        $manifestBuffer.Dispose()
    }

    if ($manifestBytes.Length -eq 0) {
        throw "The MCP server metadata file cannot be empty."
    }

    if ($manifestBytes.Length -gt 20000) {
        throw "The MCP server metadata file exceeds the NuGet.org 20,000-byte limit."
    }

    if ($manifestBytes.Length -ge 3 -and
        $manifestBytes[0] -eq 0xEF -and
        $manifestBytes[1] -eq 0xBB -and
        $manifestBytes[2] -eq 0xBF) {
        throw "The MCP server metadata file must be UTF-8 without a BOM; NuGet.org parses the raw UTF-8 bytes as JSON."
    }

    $strictUtf8 = [Text.UTF8Encoding]::new($false, $true)
    try {
        $manifestText = $strictUtf8.GetString($manifestBytes)
        $manifest = $manifestText | ConvertFrom-Json
    }
    catch {
        throw "The MCP server metadata file is not valid BOM-free UTF-8 JSON: $($_.Exception.Message)"
    }

    if (-not $manifestText.TrimStart().StartsWith("{", [StringComparison]::Ordinal)) {
        throw "The MCP server metadata root must be a JSON object."
    }

    $manifestValidationPath = Join-Path $artifactsFullPath "server.json"
    [IO.File]::WriteAllText($manifestValidationPath, $manifestText, [Text.UTF8Encoding]::new($false))

    Assert-Equal "MCP schema" "https://static.modelcontextprotocol.io/schemas/2025-12-11/server.schema.json" $manifest.'$schema'
    Assert-Equal "MCP name" "io.github.rodri-oliveira-dev/dotnet-repo-inspector-mcp" $manifest.name
    Assert-Equal "MCP version" $Version $manifest.version
    if (@($manifest.packages).Count -ne 1) { throw "MCP manifest must contain one package." }
    $manifestPackage = $manifest.packages[0]
    Assert-Equal "MCP registry type" "nuget" $manifestPackage.registryType
    Assert-Equal "MCP package ID" "DotNetRepoInspector.Mcp" $manifestPackage.identifier
    Assert-Equal "MCP package version" $Version $manifestPackage.version
    Assert-Equal "MCP runtime" "dnx" $manifestPackage.runtimeHint
    Assert-Equal "MCP transport" "stdio" $manifestPackage.transport.type
    $rootArgument = @($manifestPackage.packageArguments | Where-Object { $_.name -eq "--root" })
    if ($rootArgument.Count -ne 1 -or -not $rootArgument[0].isRequired) {
        throw "MCP manifest must require one explicit --root package argument."
    }

    $normalizedManifestText = $manifest | ConvertTo-Json -Depth 10 -Compress
    if ($normalizedManifestText -match '(?i)(api[_-]?key|token|password|secret)') {
        throw "MCP manifest contains a secret-like field or value."
    }

    if (-not [string]::IsNullOrWhiteSpace($McpPublisherPath)) {
        $publisherFullPath = (Resolve-Path -LiteralPath $McpPublisherPath).Path
        Write-Host "Validating MCP metadata with official mcp-publisher: $publisherFullPath"
        & $publisherFullPath validate $manifestValidationPath
        if ($LASTEXITCODE -ne 0) {
            throw "Official mcp-publisher schema/semantic validation failed with exit code $LASTEXITCODE."
        }
    }
}
finally {
    $archive.Dispose()
}

$installDirectory = Join-Path $artifactsFullPath "installed-tool"
if (Test-Path -LiteralPath $installDirectory) {
    Remove-Item -LiteralPath $installDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
& dotnet tool install DotNetRepoInspector.Mcp `
    --tool-path $installDirectory `
    --version $Version `
    --add-source $packageDirectoryPath `
    --no-cache
if ($LASTEXITCODE -ne 0) { throw "Local MCP dotnet tool installation failed." }

$toolExecutable = Join-Path $installDirectory "dotnet-repo-inspector-mcp"
if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) { $toolExecutable += ".exe" }
if (-not (Test-Path -LiteralPath $toolExecutable -PathType Leaf)) {
    throw "Installed MCP tool command was not found at '$toolExecutable'."
}

& ./.github/scripts/invoke_mcp_package_smoke.ps1 `
    -PackageSource $packageDirectoryPath `
    -Version $Version `
    -FixturePath $fixtureFullPath `
    -ArtifactsDirectory (Join-Path $artifactsFullPath "dnx")

$installedSmokeDirectory = Join-Path $artifactsFullPath "installed-tool-smoke"
$installedToolArguments = @(
    "run",
    "--project", "./evals/DotNetRepoInspector.Mcp.Evals/DotNetRepoInspector.Mcp.Evals.csproj",
    "--configuration", "Release",
    "--no-build",
    "--",
    "--server", $toolExecutable,
    "--fixtures", $fixtureFullPath,
    "--dataset", (Join-Path $artifactsFullPath "dnx/package-smoke-dataset.json"),
    "--output", $installedSmokeDirectory,
    "--client", "installed-tool-package-smoke",
    "--provider", "protocol",
    "--model", "deterministic-assertions",
    "--client-version", $Version
)
& dotnet @installedToolArguments
if ($LASTEXITCODE -ne 0) { throw "Installed MCP dotnet tool smoke failed." }

Write-Host "MCP package metadata, BOM-free NuGet.org JSON compatibility, official registry validation, contents, symbols, tool installation, dnx resolution, handshake, discovery, and inspect_repository smoke passed."
