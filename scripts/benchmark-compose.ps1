param(
	[string]$ComposeFile = "docker-compose.yml",
	[string]$PayloadPath = "benchmarks/loadtest/payload.json",
	[int]$Requests = 5000,
	[int]$Concurrency = 64,
	[int]$QueriesPerSecond = 900,
	[string]$OutputPath = "artifacts/compose-benchmarks/latest.txt",
	[switch]$UseDevData
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$composePath = Join-Path $repoRoot $ComposeFile
$payloadFullPath = (Resolve-Path (Join-Path $repoRoot $PayloadPath)).Path
$outputFullPath = if ([System.IO.Path]::IsPathRooted($OutputPath)) {
	$OutputPath
} else {
	Join-Path $repoRoot $OutputPath
}
$outputDirectory = Split-Path -Parent $outputFullPath

if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
	New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

if ($UseDevData) {
	& (Join-Path $PSScriptRoot "prepare-dev-runtime-data.ps1")
} elseif (-not (Test-Path (Join-Path $repoRoot "runtime-data\\index\\manifest.json"))) {
	throw "runtime-data/index/manifest.json is missing. Run scripts/prepare-runtime-data.ps1 or use -UseDevData."
}

docker compose -f $composePath down --remove-orphans
docker compose -f $composePath up -d --build

& (Join-Path $PSScriptRoot "wait-ready.ps1")

$lbContainerId = (docker compose -f $composePath ps -q lb).Trim()
$inspect = docker inspect $lbContainerId | ConvertFrom-Json
$networkName = $inspect[0].NetworkSettings.Networks.PSObject.Properties.Name | Select-Object -First 1

docker compose -f $composePath ps
docker stats --no-stream $lbContainerId rinha2026-api1 rinha2026-api2

$ohaArguments = @(
	"run",
	"--rm",
	"--network", $networkName,
	"-v", "${payloadFullPath}:/data/payload.json:ro",
	"ghcr.io/hatoo/oha:latest",
	"-n", $Requests,
	"-c", $Concurrency,
	"-m", "POST",
	"-T", "application/json",
	"-D", "/data/payload.json",
	"--stats-success-breakdown",
	"http://lb:9999/fraud-score"
)

if ($QueriesPerSecond -gt 0) {
	$ohaArguments += @("-q", $QueriesPerSecond)
}

docker @ohaArguments 2>&1 | Tee-Object -FilePath $outputFullPath

"" | Tee-Object -FilePath $outputFullPath -Append | Out-Null
"Post-load container stats:" | Tee-Object -FilePath $outputFullPath -Append | Out-Null
docker stats --no-stream $lbContainerId rinha2026-api1 rinha2026-api2 | Tee-Object -FilePath $outputFullPath -Append
