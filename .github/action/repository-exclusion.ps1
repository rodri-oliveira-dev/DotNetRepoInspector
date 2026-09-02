function Get-ActionInputLines {
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

function Test-RepositoryExcluded {
    param(
        [AllowEmptyString()]
        [string]$Repository,

        [AllowEmptyString()]
        [string]$ExcludedRepositories
    )

    $excludedRepositoryIds = @(Get-ActionInputLines -Value $ExcludedRepositories)
    if ($excludedRepositoryIds.Count -eq 0) {
        return $false
    }

    if ([string]::IsNullOrWhiteSpace($Repository)) {
        throw "GITHUB_REPOSITORY is required when exclude-repositories is configured."
    }

    foreach ($excludedRepositoryId in $excludedRepositoryIds) {
        if ($excludedRepositoryId -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') {
            throw "Excluded repository '$excludedRepositoryId' must use the full owner/repository identifier."
        }
    }

    $normalizedRepository = $Repository.Trim()
    foreach ($excludedRepositoryId in $excludedRepositoryIds) {
        if ([string]::Equals($normalizedRepository, $excludedRepositoryId, [StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }

    return $false
}
