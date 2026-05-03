param(
	[string]$ComposeFile = "docker-compose.yml",
	[string]$TestScriptPath = "..\\rinha-de-backend-2026\\test\\test.js",
	[string]$TestDataPath = "..\\rinha-de-backend-2026\\test\\test-data.json",
	[string]$OutputDirectory = "artifacts\\compose-k6",
	[string]$RuntimeDataDir = "runtime-data",
	[string]$ApprovalThreshold = "0.6",
	[string]$HttpInlineScheduling = "",
	[string]$HttpIoQueueCount = "",
	[string]$HttpNoDelay = "",
	[string]$HttpParserMode = "Manual",
	[int]$BeamLevel1 = 8,
	[int]$BeamLevel2 = 72,
	[string]$SearchIndexKind = "HierarchicalBeamIvf",
	[int]$RerankCount = 48,
	[bool]$UseLastTransactionPartitionPruning = $true,
	[int]$TopK = 5,
	[double]$LbCpus = 0.15,
	[double]$ApiCpus = 0.425,
	[string]$LbMemLimit = "48m",
	[string]$ApiMemLimit = "151m"
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $false

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$composePath = Join-Path $repoRoot $ComposeFile
$testScriptFullPath = (Resolve-Path (Join-Path $repoRoot $TestScriptPath)).Path
$testDataFullPath = (Resolve-Path (Join-Path $repoRoot $TestDataPath)).Path
$outputRoot = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
	$OutputDirectory
} else {
	Join-Path $repoRoot $OutputDirectory
}
$runtimeDataRoot = if ([System.IO.Path]::IsPathRooted($RuntimeDataDir)) {
	$RuntimeDataDir
} else {
	Join-Path $repoRoot $RuntimeDataDir
}
$k6Workdir = Join-Path $outputRoot "k6-workdir"
$patchedScriptPath = Join-Path $k6Workdir "test.js"
$patchedDataPath = Join-Path $k6Workdir "test-data.json"
$stdoutPath = Join-Path $outputRoot "k6-output.txt"
$resultsPath = Join-Path $k6Workdir "test\\results.json"
$statsPath = Join-Path $outputRoot "docker-stats.txt"
$stderrPath = Join-Path $outputRoot "k6-stderr.txt"

if (-not (Test-Path (Join-Path $runtimeDataRoot "index\\manifest.json"))) {
	throw "$runtimeDataRoot\\index\\manifest.json is missing. Run scripts/prepare-runtime-data.ps1 first."
}

New-Item -ItemType Directory -Path $k6Workdir -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $k6Workdir "test") -Force | Out-Null

$patchedScript = (Get-Content $testScriptFullPath -Raw).Replace(
	"http://localhost:9999/fraud-score",
	"http://lb:9999/fraud-score")
Set-Content -LiteralPath $patchedScriptPath -Value $patchedScript -NoNewline
Copy-Item -LiteralPath $testDataFullPath -Destination $patchedDataPath -Force

$env:SEARCH_BEAM_LEVEL1 = $BeamLevel1.ToString([System.Globalization.CultureInfo]::InvariantCulture)
$env:SEARCH_BEAM_LEVEL2 = $BeamLevel2.ToString([System.Globalization.CultureInfo]::InvariantCulture)
$env:SEARCH_INDEX_KIND = $SearchIndexKind
$env:SEARCH_RERANK_COUNT = $RerankCount.ToString([System.Globalization.CultureInfo]::InvariantCulture)
$env:SEARCH_USE_LAST_TRANSACTION_PARTITION_PRUNING = $UseLastTransactionPartitionPruning.ToString().ToLowerInvariant()
$env:RUNTIME_DATA_DIR = $runtimeDataRoot
$env:RUNTIME_INDEX_DIRECTORY = "/app/data/index"
$env:RUNTIME_MCC_RISK_PATH = "/app/data/mcc_risk.json"
$env:RUNTIME_NORMALIZATION_PATH = "/app/data/normalization.json"
$env:APPROVAL_THRESHOLD = $ApprovalThreshold
$env:HTTP_INLINE_SCHEDULING = $HttpInlineScheduling
$env:HTTP_IO_QUEUE_COUNT = $HttpIoQueueCount
$env:HTTP_NO_DELAY = $HttpNoDelay
$env:HTTP_PARSER_MODE = $HttpParserMode
$env:TOP_K = $TopK.ToString([System.Globalization.CultureInfo]::InvariantCulture)
$env:LB_CPUS = $LbCpus.ToString([System.Globalization.CultureInfo]::InvariantCulture)
$env:API_CPUS = $ApiCpus.ToString([System.Globalization.CultureInfo]::InvariantCulture)
$env:LB_MEM_LIMIT = $LbMemLimit
$env:API_MEM_LIMIT = $ApiMemLimit
$env:LB_MEM_LIMIT_DEPLOY = $LbMemLimit.ToUpperInvariant()
$env:API_MEM_LIMIT_DEPLOY = $ApiMemLimit.ToUpperInvariant()

docker compose -f $composePath down --remove-orphans
docker compose -f $composePath up -d --build

& (Join-Path $PSScriptRoot "wait-ready.ps1")

$lbContainerId = (docker compose -f $composePath ps -q lb).Trim()
$inspect = docker inspect $lbContainerId | ConvertFrom-Json
$networkName = $inspect[0].NetworkSettings.Networks.PSObject.Properties.Name | Select-Object -First 1

docker compose -f $composePath ps
docker stats --no-stream $lbContainerId rinha2026-api1 rinha2026-api2 | Tee-Object -FilePath $statsPath

docker pull grafana/k6:latest | Out-Null

$k6Arguments = @(
	"run",
	"--rm",
	"--network", $networkName,
	"-v", "${k6Workdir}:/work",
	"-w", "/work",
	"-e", "K6_NO_USAGE_REPORT=true",
	"grafana/k6:latest",
	"run",
	"test.js"
)
$k6Process = Start-Process `
	-FilePath "docker" `
	-ArgumentList $k6Arguments `
	-NoNewWindow `
	-PassThru `
	-RedirectStandardOutput $stdoutPath `
	-RedirectStandardError $stderrPath `
	-Wait

Get-Content $stdoutPath

if (Test-Path $stderrPath) {
	$stderrLines = Get-Content $stderrPath

	if ($stderrLines.Count -gt 0) {
		$stderrLines
	}
}

if ($k6Process.ExitCode -ne 0) {
	throw "k6 exited with code $($k6Process.ExitCode)."
}

"" | Tee-Object -FilePath $statsPath -Append | Out-Null
"Post-load container stats:" | Tee-Object -FilePath $statsPath -Append | Out-Null
docker stats --no-stream $lbContainerId rinha2026-api1 rinha2026-api2 | Tee-Object -FilePath $statsPath -Append

"" | Tee-Object -FilePath $statsPath -Append | Out-Null
"Benchmark configuration:" | Tee-Object -FilePath $statsPath -Append | Out-Null
"RuntimeDataDir=$runtimeDataRoot" | Tee-Object -FilePath $statsPath -Append | Out-Null
"BeamLevel1=$BeamLevel1" | Tee-Object -FilePath $statsPath -Append | Out-Null
"BeamLevel2=$BeamLevel2" | Tee-Object -FilePath $statsPath -Append | Out-Null
"RerankCount=$RerankCount" | Tee-Object -FilePath $statsPath -Append | Out-Null
"UseLastTransactionPartitionPruning=$UseLastTransactionPartitionPruning" | Tee-Object -FilePath $statsPath -Append | Out-Null
"TopK=$TopK" | Tee-Object -FilePath $statsPath -Append | Out-Null
"ApprovalThreshold=$ApprovalThreshold" | Tee-Object -FilePath $statsPath -Append | Out-Null
"HttpInlineScheduling=$HttpInlineScheduling" | Tee-Object -FilePath $statsPath -Append | Out-Null
"HttpIoQueueCount=$HttpIoQueueCount" | Tee-Object -FilePath $statsPath -Append | Out-Null
"HttpNoDelay=$HttpNoDelay" | Tee-Object -FilePath $statsPath -Append | Out-Null
"HttpParserMode=$HttpParserMode" | Tee-Object -FilePath $statsPath -Append | Out-Null
"SearchIndexKind=$SearchIndexKind" | Tee-Object -FilePath $statsPath -Append | Out-Null
"LbCpus=$LbCpus" | Tee-Object -FilePath $statsPath -Append | Out-Null
"ApiCpus=$ApiCpus" | Tee-Object -FilePath $statsPath -Append | Out-Null
"LbMemLimit=$LbMemLimit" | Tee-Object -FilePath $statsPath -Append | Out-Null
"ApiMemLimit=$ApiMemLimit" | Tee-Object -FilePath $statsPath -Append | Out-Null

if (Test-Path $resultsPath) {
	Get-Content $resultsPath
} else {
	Write-Error "k6 completed without producing test/results.json."
}
