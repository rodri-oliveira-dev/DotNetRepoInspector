[CmdletBinding()]
param(
    [string] $ManifestPath = "./.github/real-repositories/manifest.json",
    [string] $CliProject = "./src/DotNetRepoInspector.Cli/DotNetRepoInspector.Cli.csproj",
    [string] $ArtifactsRoot = "./artifacts/real-repositories"
)

Set-StrictMode -Version Latest
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

function Assert-StableId {
    param(
        [string] $Value,
        [string] $Description
    )

    Assert-Condition (-not [string]::IsNullOrWhiteSpace($Value)) "$Description must not be empty."
    Assert-Condition ($Value -match '^[a-z0-9][a-z0-9-]*$') "$Description '$Value' must use lowercase letters, digits, and hyphens only."
}

function Resolve-SafeRelativePath {
    param(
        [string] $Root,
        [string] $RelativePath,
        [string] $Description
    )

    Assert-Condition (-not [string]::IsNullOrWhiteSpace($RelativePath)) "$Description must not be empty."
    Assert-Condition (-not [IO.Path]::IsPathRooted($RelativePath)) "$Description '$RelativePath' must be relative."
    Assert-Condition (-not $RelativePath.Contains('\', [StringComparison]::Ordinal)) "$Description '$RelativePath' must use '/' separators."

    $segments = @($RelativePath.Split('/', [StringSplitOptions]::RemoveEmptyEntries))
    Assert-Condition (-not ($segments -contains '..')) "$Description '$RelativePath' must stay within its root."

    $rootFullPath = [IO.Path]::GetFullPath($Root)
    $normalizedRelativePath = $RelativePath.Replace('/', [IO.Path]::DirectorySeparatorChar)
    $candidate = if ($RelativePath -eq '.') {
        $rootFullPath
    }
    else {
        [IO.Path]::GetFullPath((Join-Path $rootFullPath $normalizedRelativePath))
    }

    $relativeToRoot = [IO.Path]::GetRelativePath($rootFullPath, $candidate)
    $outsideRoot = $relativeToRoot -eq '..' -or
        $relativeToRoot.StartsWith("..$([IO.Path]::DirectorySeparatorChar)", [StringComparison]::Ordinal) -or
        $relativeToRoot.StartsWith("..$([IO.Path]::AltDirectorySeparatorChar)", [StringComparison]::Ordinal)

    Assert-Condition (-not $outsideRoot) "$Description '$RelativePath' must stay within its root."
    return $candidate
}

function Invoke-NativeChecked {
    param(
        [string] $FilePath,
        [string[]] $Arguments,
        [string] $FailureMessage
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FailureMessage Exit code: $LASTEXITCODE."
    }
}

function Invoke-Inspector {
    param(
        [string] $RepositoryPath,
        [string] $CliProjectPath
    )

    $stdoutPath = [IO.Path]::GetTempFileName()
    $stderrPath = [IO.Path]::GetTempFileName()

    try {
        & dotnet run `
            --project $CliProjectPath `
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

function Assert-StringCollectionContains {
    param(
        [object[]] $Actual,
        [object[]] $Expected,
        [string] $Description
    )

    $actualStrings = @($Actual | ForEach-Object { [string] $_ })
    foreach ($expectedValue in @($Expected)) {
        $expectedString = [string] $expectedValue
        $found = $actualStrings | Where-Object {
            [string]::Equals($_, $expectedString, [StringComparison]::Ordinal)
        } | Select-Object -First 1

        Assert-Condition ($null -ne $found) "$Description is missing '$expectedString'. Actual: [$($actualStrings -join ', ')]."
    }
}

function Assert-ProjectExpectation {
    param(
        [object] $Report,
        [object] $ExpectedProject,
        [string] $ScenarioName
    )

    $project = @($Report.projects) | Where-Object {
        [string]::Equals($_.path, $ExpectedProject.path, [StringComparison]::Ordinal)
    } | Select-Object -First 1

    Assert-Condition ($null -ne $project) "$ScenarioName did not produce expected project '$($ExpectedProject.path)'."

    if ($ExpectedProject.PSObject.Properties.Name -contains 'classification') {
        Assert-Condition ($null -ne $project.classification) "$ScenarioName project '$($ExpectedProject.path)' has no classification."
        Assert-Condition (
            [string]::Equals($project.classification.kind, $ExpectedProject.classification, [StringComparison]::Ordinal)) `
            "$ScenarioName project '$($ExpectedProject.path)' classification '$($project.classification.kind)' differs from '$($ExpectedProject.classification)'."
    }

    if ($ExpectedProject.PSObject.Properties.Name -contains 'isTestProject') {
        Assert-Condition (
            $project.isTestProject -eq [bool] $ExpectedProject.isTestProject) `
            "$ScenarioName project '$($ExpectedProject.path)' isTestProject '$($project.isTestProject)' differs from '$($ExpectedProject.isTestProject)'."
    }

    if ($ExpectedProject.PSObject.Properties.Name -contains 'targetFrameworks') {
        Assert-StringCollectionContains `
            -Actual @($project.targetFrameworks) `
            -Expected @($ExpectedProject.targetFrameworks) `
            -Description "$ScenarioName project '$($ExpectedProject.path)' target frameworks"
    }

    if ($ExpectedProject.PSObject.Properties.Name -contains 'sdkNames') {
        $sdkNames = @($project.sdks | ForEach-Object { $_.name })
        Assert-StringCollectionContains `
            -Actual $sdkNames `
            -Expected @($ExpectedProject.sdkNames) `
            -Description "$ScenarioName project '$($ExpectedProject.path)' SDKs"
    }

    if ($ExpectedProject.PSObject.Properties.Name -contains 'references') {
        $referencePaths = @($project.references | ForEach-Object { $_.path })
        Assert-StringCollectionContains `
            -Actual $referencePaths `
            -Expected @($ExpectedProject.references) `
            -Description "$ScenarioName project '$($ExpectedProject.path)' references"
    }
}

function Assert-SdkExpectation {
    param(
        [object] $Report,
        [object] $ExpectedSdk,
        [string] $ScenarioName
    )

    if ($ExpectedSdk.PSObject.Properties.Name -contains 'configuredVersion') {
        Assert-Condition ($null -ne $Report.dotNetSdk.configured) "$ScenarioName expected configured SDK metadata."
        Assert-Condition (
            [string]::Equals($Report.dotNetSdk.configured.version, $ExpectedSdk.configuredVersion, [StringComparison]::Ordinal)) `
            "$ScenarioName configured SDK '$($Report.dotNetSdk.configured.version)' differs from '$($ExpectedSdk.configuredVersion)'."
    }

    if ($ExpectedSdk.PSObject.Properties.Name -contains 'resolvedVersion') {
        Assert-Condition (
            [string]::Equals($Report.dotNetSdk.resolvedVersion, $ExpectedSdk.resolvedVersion, [StringComparison]::Ordinal)) `
            "$ScenarioName resolved SDK '$($Report.dotNetSdk.resolvedVersion)' differs from '$($ExpectedSdk.resolvedVersion)'."
    }

    if ($ExpectedSdk.PSObject.Properties.Name -contains 'resolvedVersionPrefix') {
        Assert-Condition (
            $Report.dotNetSdk.resolvedVersion.StartsWith($ExpectedSdk.resolvedVersionPrefix, [StringComparison]::Ordinal)) `
            "$ScenarioName resolved SDK '$($Report.dotNetSdk.resolvedVersion)' does not start with '$($ExpectedSdk.resolvedVersionPrefix)'."
    }
}

$manifestFullPath = (Resolve-Path -LiteralPath $ManifestPath).Path
$cliProjectFullPath = (Resolve-Path -LiteralPath $CliProject).Path
$artifactsFullPath = [IO.Path]::GetFullPath($ArtifactsRoot)

if (Test-Path -LiteralPath $artifactsFullPath) {
    Remove-Item -LiteralPath $artifactsFullPath -Recurse -Force
}
[IO.Directory]::CreateDirectory($artifactsFullPath) | Out-Null

$manifest = Get-Content -LiteralPath $manifestFullPath -Raw | ConvertFrom-Json
Assert-Condition ($manifest.schemaVersion -eq 1) "Unsupported real-repository manifest schema '$($manifest.schemaVersion)'."
Assert-Condition (@($manifest.repositories).Count -gt 1) "The real-repository manifest must contain more than one repository."

$repositoryIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$scenarioIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$failures = [Collections.Generic.List[string]]::new()
$summaryRows = [Collections.Generic.List[string]]::new()
$summaryRows.Add('# Real repository validation')
$summaryRows.Add('')
$summaryRows.Add('| Scenario | Repository @ commit | Projects | Result |')
$summaryRows.Add('| --- | --- | ---: | --- |')

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("DotNetRepoInspector-RealRepositories-" + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($tempRoot) | Out-Null

try {
    foreach ($repository in @($manifest.repositories)) {
        $repositoryId = [string] $repository.id
        $cases = @($repository.cases)

        try {
            Assert-StableId -Value $repositoryId -Description 'Repository id'
            Assert-Condition ($repositoryIds.Add($repositoryId)) "Duplicate repository id '$repositoryId'."
            Assert-Condition ($repository.repository -match '^https://github\.com/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+\.git$') "Repository '$repositoryId' must use a public https://github.com/...git URL."
            Assert-Condition ($repository.commit -match '^[0-9a-f]{40}$') "Repository '$repositoryId' must pin a full lowercase 40-character commit SHA."
            Assert-Condition ($cases.Count -gt 0) "Repository '$repositoryId' must define at least one case."

            $repositoryRoot = Join-Path $tempRoot $repositoryId
            [IO.Directory]::CreateDirectory($repositoryRoot) | Out-Null

            Invoke-NativeChecked -FilePath 'git' -Arguments @('init', '--quiet', $repositoryRoot) -FailureMessage "Could not initialize '$repositoryId'."
            Invoke-NativeChecked -FilePath 'git' -Arguments @('-C', $repositoryRoot, 'remote', 'add', 'origin', [string] $repository.repository) -FailureMessage "Could not configure origin for '$repositoryId'."
            Invoke-NativeChecked -FilePath 'git' -Arguments @('-C', $repositoryRoot, 'fetch', '--quiet', '--depth', '1', '--no-tags', 'origin', [string] $repository.commit) -FailureMessage "Could not fetch pinned commit for '$repositoryId'."
            Invoke-NativeChecked -FilePath 'git' -Arguments @('-C', $repositoryRoot, 'checkout', '--quiet', '--detach', 'FETCH_HEAD') -FailureMessage "Could not checkout pinned commit for '$repositoryId'."

            $actualCommit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
            Assert-Condition ($LASTEXITCODE -eq 0) "Could not read HEAD for '$repositoryId'."
            Assert-Condition ([string]::Equals($actualCommit, $repository.commit, [StringComparison]::Ordinal)) "Repository '$repositoryId' checked out '$actualCommit' instead of '$($repository.commit)'."

            if ($repository.PSObject.Properties.Name -contains 'sourceAssertions') {
                foreach ($requiredFile in @($repository.sourceAssertions.requiredFiles)) {
                    $requiredFilePath = Resolve-SafeRelativePath `
                        -Root $repositoryRoot `
                        -RelativePath ([string] $requiredFile) `
                        -Description "Repository '$repositoryId' required file"

                    Assert-Condition (Test-Path -LiteralPath $requiredFilePath -PathType Leaf) "Repository '$repositoryId' is missing required source file '$requiredFile'."
                }
            }
        }
        catch {
            $message = "Repository '$repositoryId': $($_.Exception.Message)"
            $failures.Add($message)
            foreach ($case in $cases) {
                $caseId = if ($null -eq $case.id) { '<unknown>' } else { [string] $case.id }
                $summaryRows.Add("| $repositoryId/$caseId | $repositoryId @ $([string] $repository.commit).Substring(0, [Math]::Min(12, ([string] $repository.commit).Length)) | - | FAIL |")
            }
            Write-Host "FAIL: $message"
            continue
        }

        foreach ($case in $cases) {
            $caseId = [string] $case.id
            $scenarioName = "$repositoryId/$caseId"
            $scenarioArtifactRoot = Join-Path $artifactsFullPath "$repositoryId/$caseId"
            [IO.Directory]::CreateDirectory($scenarioArtifactRoot) | Out-Null

            try {
                Assert-StableId -Value $caseId -Description "Case id for '$repositoryId'"
                Assert-Condition ($scenarioIds.Add($scenarioName)) "Duplicate scenario id '$scenarioName'."

                $inspectionRoot = Resolve-SafeRelativePath `
                    -Root $repositoryRoot `
                    -RelativePath ([string] $case.inspectionRoot) `
                    -Description "Inspection root for '$scenarioName'"

                Assert-Condition (Test-Path -LiteralPath $inspectionRoot -PathType Container) "Inspection root '$($case.inspectionRoot)' for '$scenarioName' does not exist."

                $result = Invoke-Inspector -RepositoryPath $inspectionRoot -CliProjectPath $cliProjectFullPath
                [IO.File]::WriteAllText((Join-Path $scenarioArtifactRoot 'inspection.json'), $result.Stdout, [Text.UTF8Encoding]::new($false))
                [IO.File]::WriteAllText((Join-Path $scenarioArtifactRoot 'stderr.txt'), $result.Stderr, [Text.UTF8Encoding]::new($false))

                Assert-Condition ($result.ExitCode -eq [int] $case.expectedExitCode) "$scenarioName returned exit code $($result.ExitCode); expected $($case.expectedExitCode). stderr: $($result.Stderr)"
                Assert-Condition (-not [string]::IsNullOrWhiteSpace($result.Stdout)) "$scenarioName produced empty JSON output."

                $report = $result.Stdout | ConvertFrom-Json
                Assert-Condition ($null -ne $report.repository) "$scenarioName did not produce repository metadata."
                Assert-Condition ([string]::Equals($report.repository.commitSha, $repository.commit, [StringComparison]::Ordinal)) "$scenarioName report commit '$($report.repository.commitSha)' differs from pinned commit '$($repository.commit)'."

                $projects = @($report.projects)
                Assert-Condition ($projects.Count -ge [int] $case.minimumProjectCount) "$scenarioName produced $($projects.Count) projects; expected at least $($case.minimumProjectCount)."

                if ($case.PSObject.Properties.Name -contains 'expectedSdk') {
                    Assert-SdkExpectation -Report $report -ExpectedSdk $case.expectedSdk -ScenarioName $scenarioName
                }

                foreach ($expectedProject in @($case.requiredProjects)) {
                    Assert-ProjectExpectation -Report $report -ExpectedProject $expectedProject -ScenarioName $scenarioName
                }

                $summaryRows.Add("| $scenarioName | $repositoryId @ $($repository.commit.Substring(0, 12)) | $($projects.Count) | PASS |")
                Write-Host "PASS: $scenarioName -> $($projects.Count) project(s), commit $($repository.commit.Substring(0, 12))"
            }
            catch {
                $message = "$scenarioName: $($_.Exception.Message)"
                $failures.Add($message)
                $summaryRows.Add("| $scenarioName | $repositoryId @ $($repository.commit.Substring(0, 12)) | - | FAIL |")
                Write-Host "FAIL: $message"
            }
        }
    }
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}

$summary = ($summaryRows -join [Environment]::NewLine) + [Environment]::NewLine
$summaryPath = Join-Path $artifactsFullPath 'summary.md'
[IO.File]::WriteAllText($summaryPath, $summary, [Text.UTF8Encoding]::new($false))

if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_STEP_SUMMARY)) {
    [IO.File]::AppendAllText($env:GITHUB_STEP_SUMMARY, $summary, [Text.UTF8Encoding]::new($false))
}

if ($failures.Count -gt 0) {
    $details = $failures | ForEach-Object { "- $_" }
    throw "Real repository validation failed:`n$($details -join [Environment]::NewLine)"
}

Write-Host "Real repository validation passed for $($scenarioIds.Count) scenario(s) across $($repositoryIds.Count) repository/repositories."
