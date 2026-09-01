param(
    [Parameter(Mandatory = $true)][string]$CandidateManifest,
    [Parameter(Mandatory = $true)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$candidatePath = (Resolve-Path -LiteralPath $CandidateManifest).Path
$candidate = Get-Content -Raw -LiteralPath $candidatePath | ConvertFrom-Json
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$logRoot = Join-Path $outputRoot 'local-gate-logs'
New-Item -ItemType Directory -Force -Path $logRoot | Out-Null
$startedAt = [DateTime]::UtcNow

$steps = @(
    [pscustomobject]@{ Id = 'dotnet-build'; File = 'dotnet'; Args = @('build', 'StockTrader.csproj', '--no-restore'); Work = $repoRoot },
    [pscustomobject]@{ Id = 'dotnet-test'; File = 'dotnet'; Args = @('test', 'tests/StockTrader.Tests/StockTrader.Tests.csproj', '--no-restore'); Work = $repoRoot },
    [pscustomobject]@{ Id = 'desktop-api-check'; File = 'npm.cmd'; Args = @('run', 'api:check'); Work = (Join-Path $repoRoot 'desktop-app') },
    [pscustomobject]@{ Id = 'desktop-test'; File = 'npm.cmd'; Args = @('run', 'test'); Work = (Join-Path $repoRoot 'desktop-app') },
    [pscustomobject]@{ Id = 'desktop-build'; File = 'npm.cmd'; Args = @('run', 'build'); Work = (Join-Path $repoRoot 'desktop-app') }
)
$results = @()
$failed = $false
foreach ($step in $steps) {
    $logPath = Join-Path $logRoot "$($step.Id).log"
    $timer = [Diagnostics.Stopwatch]::StartNew()
    if ($failed) {
        'Skipped after an earlier required command failed.' | Set-Content -LiteralPath $logPath -Encoding utf8NoBOM
        $exitCode = -1
    } else {
        Push-Location $step.Work
        try {
            & $step.File @($step.Args) 2>&1 | Tee-Object -FilePath $logPath
            $exitCode = $LASTEXITCODE
        } finally {
            Pop-Location
        }
        if ($exitCode -ne 0) { $failed = $true }
    }
    $timer.Stop()
    $testTotal = $null
    if ($step.Id -eq 'dotnet-test') {
        $match = Select-String -LiteralPath $logPath -Pattern '(?:Total|전체):\s*(\d+)' | Select-Object -Last 1
        if ($match) { $testTotal = [int]$match.Matches[0].Groups[1].Value }
    }
    $results += [ordered]@{
        commandId = $step.Id
        exitCode = $exitCode
        testTotal = $testTotal
        outputHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $logPath).Hash.ToLowerInvariant()
        durationMilliseconds = $timer.ElapsedMilliseconds
    }
}

$contractFiles = @(
    Get-Item -LiteralPath (Join-Path $repoRoot 'desktop-app/openapi/stocktrader_desktop.json')
    Get-Item -LiteralPath (Join-Path $repoRoot 'desktop-app/src/api/generated.ts') -ErrorAction SilentlyContinue
) | Where-Object { $null -ne $_ }
$contractHashes = [ordered]@{}
foreach ($file in $contractFiles) {
    $contractHashes[$file.Name] =
        (Get-FileHash -Algorithm SHA256 -LiteralPath $file.FullName).Hash.ToLowerInvariant()
}
$stopReasons = if ($failed) { @('required-local-command-failed') } else { @() }
$input = [ordered]@{
    contractVersion = 1
    candidateId = $candidate.candidateId
    commands = $results
    generatedContractHashes = $contractHashes
    startedAtUtc = $startedAt.ToString('O')
    endedAtUtc = [DateTime]::UtcNow.ToString('O')
    stopReasons = $stopReasons
}
$inputPath = Join-Path $outputRoot 'local-verification-input.json'
$manifestPath = Join-Path $outputRoot 'local-verification-manifest.json'
$input | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $inputPath -Encoding utf8NoBOM
& dotnet run --project (Join-Path $repoRoot 'src/StockTrader.Stage5Evidence/StockTrader.Stage5Evidence.csproj') `
    --no-build -- local $inputPath $manifestPath
$exitCode = $LASTEXITCODE
Write-Output $manifestPath
exit $exitCode
