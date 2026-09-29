param(
    [Parameter(Mandatory = $true)]
    [string]$PublisherPath,

    [Parameter(Mandatory = $true)]
    [string]$ManifestPath,

    [ValidateRange(1, 10)]
    [int]$MaxAttempts = 4,

    [ValidateRange(0, 300)]
    [int]$InitialDelaySeconds = 5,

    [ValidateRange(0, 300)]
    [int]$MaxDelaySeconds = 30
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Test-TransientRegistryFailure {
    param([string]$Text)

    $patterns = @(
        '(?i)connection refused',
        '(?i)connection reset',
        '(?i)network is unreachable',
        '(?i)no such host',
        '(?i)temporary failure in name resolution',
        '(?i)name or service not known',
        '(?i)tls handshake timeout',
        '(?i)i/o timeout',
        '(?i)context deadline exceeded',
        '(?i)client\.timeout exceeded',
        '(?i)unexpected eof',
        '(?i)server misbehaving',
        '(?i)\b(?:408|425|429|500|502|503|504)\b'
    )

    foreach ($pattern in $patterns) {
        if ($Text -match $pattern) {
            return $true
        }
    }

    return $false
}

$publisherFullPath = (Resolve-Path -LiteralPath $PublisherPath).Path
$manifestFullPath = (Resolve-Path -LiteralPath $ManifestPath).Path

for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
    Write-Host "Official MCP registry validation attempt $attempt/$MaxAttempts."

    $outputLines = @(
        & $publisherFullPath validate $manifestFullPath 2>&1 |
            ForEach-Object { $_.ToString() }
    )
    $exitCode = $LASTEXITCODE
    $outputText = $outputLines -join [Environment]::NewLine

    foreach ($line in $outputLines) {
        Write-Host $line
    }

    if ($exitCode -eq 0) {
        Write-Host "Official MCP registry validation succeeded."
        return
    }

    if (-not (Test-TransientRegistryFailure -Text $outputText)) {
        throw "Official mcp-publisher schema/semantic validation failed with exit code $exitCode. The failure was not classified as transient."
    }

    if ($attempt -eq $MaxAttempts) {
        Write-Warning "Official MCP Registry validation is temporarily unavailable after $MaxAttempts attempts. Continuing because deterministic local package validation already passed; schema/semantic rejections remain fatal."
        return
    }

    $delaySeconds = [Math]::Min(
        $MaxDelaySeconds,
        [int]($InitialDelaySeconds * [Math]::Pow(2, $attempt - 1))
    )

    Write-Warning "Transient MCP Registry failure detected. Retrying in $delaySeconds second(s)."
    if ($delaySeconds -gt 0) {
        Start-Sleep -Seconds $delaySeconds
    }
}
