param(
	[string]$OutputDir = (Join-Path $PSScriptRoot "..\\runtime-data")
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$devResourcesDir = Join-Path $repoRoot "artifacts\\.dev-runtime-resources"
$referencesPath = Join-Path $devResourcesDir "references.json.gz"
$mccRiskPath = Join-Path $devResourcesDir "mcc_risk.json"
$normalizationPath = Join-Path $devResourcesDir "normalization.json"

if (Test-Path $devResourcesDir) {
	Remove-Item -Recurse -Force -LiteralPath $devResourcesDir
}

New-Item -ItemType Directory -Path $devResourcesDir -Force | Out-Null

$json = @"
[
  { "vector": [0.0041, 0.1667, 0.05, 0.7826, 0.3333, -1, -1, 0.0292, 0.15, 0, 1, 0, 0.15, 0.006], "label": "fraud" },
  { "vector": [0.0041, 0.1667, 0.05, 0.7826, 0.3333, -1, -1, 0.0292, 0.15, 0, 1, 0, 0.15, 0.006], "label": "fraud" },
  { "vector": [0.0041, 0.1667, 0.05, 0.7826, 0.3333, -1, -1, 0.0292, 0.15, 0, 1, 0, 0.15, 0.006], "label": "fraud" },
  { "vector": [0.0041, 0.1667, 0.05, 0.7826, 0.3333, -1, -1, 0.0292, 0.15, 0, 1, 0, 0.15, 0.006], "label": "fraud" },
  { "vector": [0.0041, 0.1667, 0.05, 0.7826, 0.3333, -1, -1, 0.0292, 0.15, 0, 1, 0, 0.15, 0.006], "label": "legit" }
]
"@

[System.IO.File]::WriteAllText($mccRiskPath, @"
{
  "5411": 0.15,
  "5812": 0.30,
  "5912": 0.20,
  "5944": 0.45,
  "7801": 0.80,
  "7802": 0.75,
  "7995": 0.85,
  "4511": 0.35,
  "5311": 0.25,
  "5999": 0.50
}
"@)
[System.IO.File]::WriteAllText($normalizationPath, @"
{
  "max_amount": 10000,
  "max_installments": 12,
  "amount_vs_avg_ratio": 10,
  "max_minutes": 1440,
  "max_km": 1000,
  "max_tx_count_24h": 20,
  "max_merchant_avg_amount": 10000
}
"@)

$bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
$fileStream = [System.IO.File]::Create($referencesPath)
$gzipStream = [System.IO.Compression.GZipStream]::new($fileStream, [System.IO.Compression.CompressionMode]::Compress)
$gzipStream.Write($bytes, 0, $bytes.Length)
$gzipStream.Dispose()
$fileStream.Dispose()

& (Join-Path $PSScriptRoot "prepare-runtime-data.ps1") `
	-ResourcesDir $devResourcesDir `
	-OutputDir $OutputDir `
	-Level1Clusters 2 `
	-Level2PerLevel1 2 `
	-TrainingSampleSize 5 `
	-KMeansIterations 2
