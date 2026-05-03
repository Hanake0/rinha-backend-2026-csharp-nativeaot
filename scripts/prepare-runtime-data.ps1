param(
	[string]$ResourcesDir = (Join-Path $PSScriptRoot "..\\resources"),
	[string]$OutputDir = (Join-Path $PSScriptRoot "..\\runtime-data"),
	[int]$Level1Clusters = 128,
	[int]$Level2PerLevel1 = 16,
	[int]$TrainingSampleSize = 32768,
	[int]$KMeansIterations = 8,
	[bool]$UseLastTransactionPartitioning = $false
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$resourcesRoot = (Resolve-Path $ResourcesDir).Path
$outputRoot = if ([System.IO.Path]::IsPathRooted($OutputDir)) {
	$OutputDir
} else {
	Join-Path $repoRoot $OutputDir
}
$referencesPath = Join-Path $resourcesRoot "references.json.gz"
$mccRiskPath = Join-Path $resourcesRoot "mcc_risk.json"
$normalizationPath = Join-Path $resourcesRoot "normalization.json"
$indexOutputDir = Join-Path $outputRoot "index"

foreach ($path in @($referencesPath, $mccRiskPath, $normalizationPath)) {
	if (-not (Test-Path $path)) {
		throw "Missing required resource file: $path"
	}
}

if (Test-Path $outputRoot) {
	Remove-Item -Recurse -Force -LiteralPath $outputRoot
}

New-Item -ItemType Directory -Path $indexOutputDir -Force | Out-Null
Copy-Item -LiteralPath $mccRiskPath -Destination (Join-Path $outputRoot "mcc_risk.json")
Copy-Item -LiteralPath $normalizationPath -Destination (Join-Path $outputRoot "normalization.json")

dotnet run -c Release --project (Join-Path $repoRoot "src\\Rinha2026.IndexBuilder\\Rinha2026.IndexBuilder.csproj") -- `
	--input $referencesPath `
	--output $indexOutputDir `
	--level1-clusters $Level1Clusters `
	--level2-per-level1 $Level2PerLevel1 `
	--training-sample-size $TrainingSampleSize `
	--kmeans-iterations $KMeansIterations `
	--use-last-transaction-partitioning $UseLastTransactionPartitioning
