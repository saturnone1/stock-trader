param(
    [Parameter(Mandatory = $true)][string]$ArchiveDirectory,
    [Parameter(Mandatory = $true)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$archiveRoot = (Resolve-Path -LiteralPath $ArchiveDirectory).Path
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

if ((git -C $repoRoot status --porcelain).Count -ne 0) {
    throw 'Candidate creation requires a clean working tree.'
}

function Get-TextHash([string]$Value) {
    $bytes = [Text.Encoding]::UTF8.GetBytes($Value)
    $hash = [Security.Cryptography.SHA256]::HashData($bytes)
    return [Convert]::ToHexString($hash).ToLowerInvariant()
}

function Get-FileSetHash([IO.FileInfo[]]$Files) {
    $lines = foreach ($file in $Files | Sort-Object FullName) {
        $relative = [IO.Path]::GetRelativePath($repoRoot, $file.FullName).Replace('\', '/')
        "$relative=$((Get-FileHash -Algorithm SHA256 -LiteralPath $file.FullName).Hash.ToLowerInvariant())"
    }
    return Get-TextHash ($lines -join "`n")
}

function Get-RequiredMetadata([hashtable]$Values, [string]$Name) {
    if (-not $Values.ContainsKey($Name) -or [string]::IsNullOrWhiteSpace($Values[$Name])) {
        throw "Missing stage5 metadata: $Name"
    }
    return $Values[$Name]
}

$checksumPath = Join-Path $archiveRoot 'SHA256SUMS'
$metadataPath = Join-Path $archiveRoot 'stage5-metadata.env'
if (-not (Test-Path -LiteralPath $checksumPath) -or -not (Test-Path -LiteralPath $metadataPath)) {
    throw 'The archive directory does not contain a sealed Stage 5 image set.'
}
foreach ($line in Get-Content -LiteralPath $checksumPath) {
    if ($line -notmatch '^([0-9a-f]{64})\s+\*?(.+)$') { throw "Invalid checksum line: $line" }
    $path = Join-Path $archiveRoot $Matches[2]
    if (-not (Test-Path -LiteralPath $path) -or
        (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash.ToLowerInvariant() -ne $Matches[1]) {
        throw "Stage 5 artifact checksum mismatch: $($Matches[2])"
    }
}

$metadata = @{}
foreach ($line in Get-Content -LiteralPath $metadataPath) {
    $parts = $line -split '=', 2
    if ($parts.Count -eq 2) { $metadata[$parts[0]] = $parts[1] }
}
$images = [ordered]@{
    'edge-local' = Get-RequiredMetadata $metadata 'EDGE_LOCAL_IMAGE_DIGEST'
    edge = Get-RequiredMetadata $metadata 'EDGE_IMAGE_DIGEST'
    'trading-core' = Get-RequiredMetadata $metadata 'TRADING_CORE_IMAGE_DIGEST'
    'trading-core-shadow' = Get-RequiredMetadata $metadata 'TRADING_CORE_SHADOW_IMAGE_DIGEST'
    'market-data' = Get-RequiredMetadata $metadata 'MARKET_DATA_IMAGE_DIGEST'
    'acceptance-core' = Get-RequiredMetadata $metadata 'ACCEPTANCE_CORE_IMAGE_DIGEST'
    'broker-emulator' = Get-RequiredMetadata $metadata 'BROKER_EMULATOR_IMAGE_DIGEST'
    driver = Get-RequiredMetadata $metadata 'DRIVER_IMAGE_DIGEST'
    coordinator = Get-RequiredMetadata $metadata 'COORDINATOR_IMAGE_DIGEST'
    'rollback-importer' = Get-RequiredMetadata $metadata 'ROLLBACK_IMPORTER_IMAGE_DIGEST'
}
$bases = [ordered]@{
    'dotnet-sdk' = Get-RequiredMetadata $metadata 'DOTNET_SDK_BASE_DIGEST'
    'dotnet-aspnet' = Get-RequiredMetadata $metadata 'DOTNET_ASPNET_BASE_DIGEST'
    'dotnet-runtime' = Get-RequiredMetadata $metadata 'DOTNET_RUNTIME_BASE_DIGEST'
}
$assemblies = [ordered]@{
    'service-contracts' = Get-RequiredMetadata $metadata 'SERVICE_CONTRACTS_HASH'
    engine = Get-RequiredMetadata $metadata 'ENGINE_HASH'
    'trading-core' = Get-RequiredMetadata $metadata 'TRADING_CORE_HASH'
    runtime = Get-RequiredMetadata $metadata 'RUNTIME_HASH'
}

$inventories = [ordered]@{}
foreach ($archive in Get-ChildItem -LiteralPath $archiveRoot -Filter '*.tar') {
    $listing = (& tar -tf $archive.FullName | Sort-Object) -join "`n"
    if ($LASTEXITCODE -ne 0) { throw "Cannot inspect $($archive.Name)" }
    $inventories[$archive.BaseName] = Get-TextHash $listing
}
$sboms = [ordered]@{}
foreach ($sbom in Get-ChildItem -LiteralPath $archiveRoot -Filter '*.cdx.json') {
    $sboms[$sbom.Name.Replace('.cdx.json', '')] =
        (Get-FileHash -Algorithm SHA256 -LiteralPath $sbom.FullName).Hash.ToLowerInvariant()
}
if ($sboms.Count -ne $inventories.Count) { throw 'Every candidate image must have one SBOM.' }

$projectGraphs = Get-ChildItem -LiteralPath $repoRoot -Recurse -File |
    Where-Object {
        $_.FullName -notmatch '[\\/](bin|obj|node_modules|\.git)[\\/]' -and
        ($_.Extension -in @('.csproj', '.fsproj') -or
         $_.Name -in @('packages.lock.json', 'NuGet.Config'))
    }
$packageLock = Get-Item -LiteralPath (Join-Path $repoRoot 'desktop-app/package-lock.json')
$packageGraphs = [ordered]@{
    dotnet = Get-FileSetHash $projectGraphs
    desktop = Get-FileSetHash @($packageLock)
}
$edgeMigrations = Get-ChildItem -LiteralPath (Join-Path $repoRoot 'Data/EfMigrations') -File
$coreSchema = Get-ChildItem -LiteralPath (Join-Path $repoRoot 'workers/trading-core-runtime') -File |
    Where-Object Name -Match '(Store|Schema|Transfer|Ledger).*\.fs$'
$migrations = [ordered]@{
    edge = Get-FileSetHash $edgeMigrations
    'trading-core' = Get-FileSetHash $coreSchema
}
$kubernetes = [ordered]@{
    stage5 = Get-FileSetHash (Get-ChildItem -LiteralPath (Join-Path $repoRoot 'k8s') -File -Filter '*.yaml')
    deployment = Get-FileSetHash @(
        (Get-Item -LiteralPath (Join-Path $repoRoot 'scripts/deploy-k3s.sh'))
        (Get-Item -LiteralPath (Join-Path $repoRoot 'scripts/build-k3s-image-archives.sh')))
}
$catalogs = [ordered]@{
    acceptance = Get-FileSetHash @(
        Get-Item -LiteralPath (Join-Path $repoRoot 'src/StockTrader.ServiceContracts/TradingCoreAcceptanceContracts.cs'))
    authority = Get-FileSetHash @(
        Get-Item -LiteralPath (Join-Path $repoRoot 'src/StockTrader.ServiceContracts/TradingCoreCoordinatorContracts.cs'))
    transfer = Get-FileSetHash @(
        Get-Item -LiteralPath (Join-Path $repoRoot 'src/StockTrader.ServiceContracts/TradingCoreTransferContracts.cs'))
}
$openApiPath = Join-Path $repoRoot 'desktop-app/openapi/stocktrader_desktop.json'
if (-not (Test-Path -LiteralPath $openApiPath)) { throw 'Generated desktop OpenAPI contract is missing.' }

$input = [ordered]@{
    contractVersion = 1
    repositoryCommit = Get-RequiredMetadata $metadata 'REPOSITORY_COMMIT'
    worktreeInputHash = Get-TextHash ((git -C $repoRoot ls-files -s) -join "`n")
    dependencyLockHash = Get-TextHash (($packageGraphs.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join "`n")
    buildId = Get-RequiredMetadata $metadata 'BUILD_ID'
    imageDigests = $images
    baseImageDigests = $bases
    sharedAssemblyHashes = $assemblies
    assemblyInventoryHashes = $inventories
    sbomHashes = $sboms
    packageGraphHashes = $packageGraphs
    migrationHashes = $migrations
    openApiContractHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $openApiPath).Hash.ToLowerInvariant()
    kubernetesObjectHashes = $kubernetes
    catalogHashes = $catalogs
    deploymentScopes = @('trading-core-shadow-candidate', 'trading-core-acceptance',
        'trading-core-cutover', 'trading-core-rollback', 'trading-core-recutover')
    rollbackRequirements = @('edge-checked-backup', 'trading-core-checked-backup',
        'financial-transfer', 'staging-import-receipt', 'preserved-tls-generation',
        'preserved-encryption-generation')
    createdAtUtc = [DateTime]::UtcNow.ToString('O')
}
$inputPath = Join-Path $outputRoot 'candidate-input.json'
$manifestPath = Join-Path $outputRoot 'candidate-manifest.json'
$input | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $inputPath -Encoding utf8NoBOM
& dotnet run --project (Join-Path $repoRoot 'src/StockTrader.Stage5Evidence/StockTrader.Stage5Evidence.csproj') `
    --no-build -- candidate $inputPath $manifestPath
if ($LASTEXITCODE -ne 0) { throw 'Candidate manifest sealing failed.' }
Write-Output $manifestPath
