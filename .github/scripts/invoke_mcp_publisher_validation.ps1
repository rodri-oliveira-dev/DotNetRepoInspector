param(
    [Parameter(Mandatory = $true)]
    [string]$PublisherPath,

    [Parameter(Mandatory = $true)]
    [string]$ManifestPath,

    [ValidateRange(1, 10)]
    [int]$MaxAttempts = 4,

    [ValidateRange(1, 300)]
    [int]$AttemptTimeoutSeconds = 30,

    [ValidateRange(0, 300)]
    [int]$InitialDelaySeconds = 5,

    [ValidateRange(0, 300)]
    [int]$MaxDelaySeconds = 30
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Test-TransientRegistryTransportFailure {
    param([string]$StandardError)

    $transportMatch = [regex]::Match(
        $StandardError,
        '(?im)^Error:\s+validation failed:\s+(?<cause>error sending request|error reading response|server returned status (?<status>\d{3})):\s*(?<detail>.*)$'
    )

    if (-not $transportMatch.Success) {
        return $false
    }

    $status = $transportMatch.Groups["status"].Value
    if (-not [string]::IsNullOrWhiteSpace($status)) {
        return @("408", "425", "429", "500", "502", "503", "504") -contains $status
    }

    $detail = $transportMatch.Groups["detail"].Value
    $transientDetailPatterns = @(
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
        '(?i)server misbehaving'
    )

    foreach ($pattern in $transientDetailPatterns) {
        if ($detail -match $pattern) {
            return $true
        }
    }

    return $false
}

function Invoke-PublisherValidationAttempt {
    param(
        [string]$Publisher,
        [string]$Manifest,
        [int]$TimeoutSeconds
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $Publisher
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.ArgumentList.Add("validate")
    $startInfo.ArgumentList.Add($Manifest)

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo

    try {
        if (-not $process.Start()) {
            throw "Could not start official mcp-publisher."
        }

        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()

        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            try {
                $process.Kill($true)
            }
            catch {
                Write-Warning "Could not terminate timed-out mcp-publisher process: $($_.Exception.Message)"
            }

            $process.WaitForExit()
            return [pscustomobject]@{
                ExitCode = $null
                TimedOut = $true
                StandardOutput = $stdoutTask.GetAwaiter().GetResult()
                StandardError = $stderrTask.GetAwaiter().GetResult()
            }
        }

        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            TimedOut = $false
            StandardOutput = $stdoutTask.GetAwaiter().GetResult()
            StandardError = $stderrTask.GetAwaiter().GetResult()
        }
    }
    finally {
        $process.Dispose()
    }
}

function Write-PublisherOutput {
    param([string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return
    }

    foreach ($line in ($Text -split "\r?\n")) {
        if (-not [string]::IsNullOrEmpty($line)) {
            Write-Host $line
        }
    }
}

$publisherFullPath = (Resolve-Path -LiteralPath $PublisherPath).Path
$manifestFullPath = (Resolve-Path -LiteralPath $ManifestPath).Path

for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
    Write-Host "Official MCP registry validation attempt $attempt/$MaxAttempts."

    $result = Invoke-PublisherValidationAttempt -Publisher $publisherFullPath -Manifest $manifestFullPath -TimeoutSeconds $AttemptTimeoutSeconds

    Write-PublisherOutput -Text $result.StandardOutput
    Write-PublisherOutput -Text $result.StandardError

    if (-not $result.TimedOut -and $result.ExitCode -eq 0) {
        Write-Host "Official MCP registry validation succeeded."
        return
    }

    $isTransient = if ($result.TimedOut) {
        Write-Warning "Official mcp-publisher exceeded the $AttemptTimeoutSeconds-second attempt deadline and was terminated."
        $true
    }
    else {
        Test-TransientRegistryTransportFailure -StandardError $result.StandardError
    }

    if (-not $isTransient) {
        throw "Official mcp-publisher schema/semantic validation failed with exit code $($result.ExitCode). The failure was not classified as a transient transport error."
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
