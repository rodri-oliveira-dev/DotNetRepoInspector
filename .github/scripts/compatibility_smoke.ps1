[CmdletBinding()]
param(
    [string] $CliProject = "./src/DotNetRepoInspector.Cli/DotNetRepoInspector.Cli.csproj",
    [string] $FixturesRoot = "./tests/Fixtures/Compatibility"
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $false

function Assert-Condition {
    param(
        [bool] $Condition,
        [string] $Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Invoke-Inspector {
    param(
        [Parameter(Mandatory = $true)]
        [string] $RepositoryPath
    )

    $stdoutPath = [IO.Path]::GetTempFileName()
    $stderrPath = [IO.Path]::GetTempFileName()

    try {
        & dotnet run `
            --project $CliProject `
            --configuration Release `
            --no-build `
            -- $RepositoryPath `
            1> $stdoutPath `
            2> $stderrPath

        return [pscustomobject]@{
            ExitCode = $LASTEXITCODE
            Stdout = [IO.File]::ReadAllText($stdoutPath)
            Stderr = [IO.File]::ReadAllText($stderrPath)
        }
    }
    finally {
        Remove-Item -LiteralPath $stdoutPath, $stderrPath -Force -ErrorAction SilentlyContinue
    }
}

function Read-Report {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Json
    )

    Assert-Condition (-not [string]::IsNullOrWhiteSpace($Json)) "Inspector produced an empty stdout payload."
    return $Json | ConvertFrom-Json
}

$supportedCases = @(
    @{
        Name = "Net8"
        SdkPrefix = "8.0."
        TargetFramework = "net8.0"
        ProjectPath = "Compatibility.Net8.csproj"
    },
    @{
        Name = "Net10"
        SdkPrefix = "10.0."
        TargetFramework = "net10.0"
        ProjectPath = "Compatibility.Net10.csproj"
    }
)

foreach ($case in $supportedCases) {
    $repositoryPath = Join-Path $FixturesRoot $case.Name
    $result = Invoke-Inspector -RepositoryPath $repositoryPath

    Assert-Condition ($result.ExitCode -eq 0) "Compatibility case '$($case.Name)' failed with exit code $($result.ExitCode). stderr: $($result.Stderr)"

    $report = Read-Report -Json $result.Stdout
    Assert-Condition ($report.dotNetSdk.resolvedVersion.StartsWith($case.SdkPrefix, [StringComparison]::Ordinal)) "Compatibility case '$($case.Name)' resolved SDK '$($report.dotNetSdk.resolvedVersion)' instead of '$($case.SdkPrefix)*'."

    $project = @($report.projects) | Select-Object -First 1
    Assert-Condition ($null -ne $project) "Compatibility case '$($case.Name)' did not produce a project entry."
    Assert-Condition ($project.path -eq $case.ProjectPath) "Compatibility case '$($case.Name)' produced project path '$($project.path)'."
    Assert-Condition (-not $project.path.Contains("\", [StringComparison]::Ordinal)) "Compatibility case '$($case.Name)' emitted a Windows path separator in JSON."
    Assert-Condition (@($project.targetFrameworks) -contains $case.TargetFramework) "Compatibility case '$($case.Name)' did not report target framework '$($case.TargetFramework)'."
    Assert-Condition ($project.resolvedSdkVersion.StartsWith($case.SdkPrefix, [StringComparison]::Ordinal)) "Compatibility case '$($case.Name)' evaluated the project with SDK '$($project.resolvedSdkVersion)' instead of '$($case.SdkPrefix)*'."

    Write-Host "PASS: $($case.Name) -> SDK $($report.dotNetSdk.resolvedVersion), TFM $($case.TargetFramework)"
}

$pathCasingResult = Invoke-Inspector -RepositoryPath (Join-Path $FixturesRoot "PathCasing")
Assert-Condition ($pathCasingResult.ExitCode -eq 0) "Path/casing compatibility failed with exit code $($pathCasingResult.ExitCode). stderr: $($pathCasingResult.Stderr)"
$pathCasingReport = Read-Report -Json $pathCasingResult.Stdout
$pathCasingProject = @($pathCasingReport.projects) | Select-Object -First 1
Assert-Condition ($pathCasingProject.path -eq "MixedCase/Compatibility.MixedCase.CSPROJ") "Project path casing was not preserved: '$($pathCasingProject.path)'."
Assert-Condition (-not $pathCasingProject.path.Contains("\", [StringComparison]::Ordinal)) "Normalized project path contains a Windows separator."
Write-Host "PASS: path separator and casing normalization"

$missingSdkResult = Invoke-Inspector -RepositoryPath (Join-Path $FixturesRoot "MissingSdk")
Assert-Condition ($missingSdkResult.ExitCode -eq 1) "Missing SDK case should return exit code 1 but returned $($missingSdkResult.ExitCode). stderr: $($missingSdkResult.Stderr)"
$missingSdkReport = Read-Report -Json $missingSdkResult.Stdout
$missingSdkDiagnostic = @($missingSdkReport.diagnostics) | Where-Object { $_.code -eq "DRI1002" } | Select-Object -First 1
Assert-Condition ($null -ne $missingSdkDiagnostic) "Missing SDK case did not emit DRI1002."
Assert-Condition ($missingSdkDiagnostic.severity -eq "error") "Missing SDK diagnostic severity should be 'error' but was '$($missingSdkDiagnostic.severity)'."
Write-Host "PASS: missing SDK -> DRI1002/error"

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("DotNetRepoInspector-Compatibility-" + [Guid]::NewGuid().ToString("N"))
Directory.CreateDirectory($tempRoot) | Out-Null

try {
    $projectPath = Join-Path $tempRoot "CrLfProject.csproj"
    $globalJsonPath = Join-Path $tempRoot "global.json"

    $projectContent = "<Project Sdk=`"Microsoft.NET.Sdk`">`r`n  <PropertyGroup>`r`n    <TargetFramework>net10.0</TargetFramework>`r`n  </PropertyGroup>`r`n</Project>`r`n"
    $globalJsonContent = "{`n  `"sdk`": {`n    `"version`": `"10.0.100`",`n    `"rollForward`": `"latestFeature`",`n    `"allowPrerelease`": false`n  }`n}`n"
    $utf8NoBom = [Text.UTF8Encoding]::new($false)

    [IO.File]::WriteAllText($projectPath, $projectContent, $utf8NoBom)
    [IO.File]::WriteAllText($globalJsonPath, $globalJsonContent, $utf8NoBom)

    $lineEndingResult = Invoke-Inspector -RepositoryPath $tempRoot
    Assert-Condition ($lineEndingResult.ExitCode -eq 0) "CRLF compatibility failed with exit code $($lineEndingResult.ExitCode). stderr: $($lineEndingResult.Stderr)"
    $lineEndingReport = Read-Report -Json $lineEndingResult.Stdout
    $lineEndingProject = @($lineEndingReport.projects) | Select-Object -First 1
    Assert-Condition (@($lineEndingProject.targetFrameworks) -contains "net10.0") "CRLF project was not evaluated as net10.0."
    Write-Host "PASS: CRLF project evaluation"
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
