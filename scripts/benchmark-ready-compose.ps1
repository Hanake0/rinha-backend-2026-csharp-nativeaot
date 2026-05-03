param(
	[string]$ComposeFile = "docker-compose.yml",
	[string]$K6ScriptPath = "benchmarks\k6\ready.js",
	[string]$OutputDirectory = "artifacts\compose-ready",
	[string]$RuntimeDataDir = "runtime-data",
	[string]$ApprovalThreshold = "0.6",
	[string]$HttpInlineScheduling = "",
	[string]$HttpIoQueueCount = "",
	[string]$HttpNoDelay = "",
	[string]$HttpParserMode = "Manual",
	[string]$HttpServerMode = "Kestrel",
	[int]$BeamLevel1 = 8,
	[int]$BeamLevel2 = 128,
	[string]$SearchIndexKind = "HierarchicalBeamIvf",
	[int]$RerankCount = 48,
	[int]$BoundaryRerankCount = $RerankCount,
	[bool]$UseLeafRadiusPruning = $true,
	[bool]$UseLastTransactionPartitionPruning = $true,
	[int]$TopK = 5,
	[double]$LbCpus = 0.15,
	[double]$ApiCpus = 0.425,
	[string]$LbMemLimit = "48m",
	[string]$ApiMemLimit = "151m",
	[int]$K6StartRate = 900,
	[int]$K6TargetRate = 900,
	[int]$K6PreAllocatedVUs = 100,
	[int]$K6MaxVUs = 250,
	[string]$K6StageDuration = "12s",
	[string]$K6GracefulStop = "2s",
	[switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $false

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$composePath = Join-Path $repoRoot $ComposeFile
$k6ScriptFullPath = (Resolve-Path (Join-Path $repoRoot $K6ScriptPath)).Path
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
$resultsPath = Join-Path $k6Workdir "test\results.json"
$stdoutPath = Join-Path $outputRoot "k6-output.txt"
$stderrPath = Join-Path $outputRoot "k6-stderr.txt"

if (-not (Test-Path (Join-Path $runtimeDataRoot "index\manifest.json"))) {
	throw "$runtimeDataRoot\index\manifest.json is missing. Run scripts/prepare-runtime-data.ps1 first."
}

New-Item -ItemType Directory -Path $k6Workdir -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $k6Workdir "test") -Force | Out-Null
Copy-Item -LiteralPath $k6ScriptFullPath -Destination (Join-Path $k6Workdir "test.js") -Force

$env:SEARCH_BEAM_LEVEL1 = $BeamLevel1.ToString([System.Globalization.CultureInfo]::InvariantCulture)
$env:SEARCH_BEAM_LEVEL2 = $BeamLevel2.ToString([System.Globalization.CultureInfo]::InvariantCulture)
$env:SEARCH_INDEX_KIND = $SearchIndexKind
$env:SEARCH_RERANK_COUNT = $RerankCount.ToString([System.Globalization.CultureInfo]::InvariantCulture)
$env:SEARCH_BOUNDARY_RERANK_COUNT = $BoundaryRerankCount.ToString([System.Globalization.CultureInfo]::InvariantCulture)
$env:SEARCH_USE_LEAF_RADIUS_PRUNING = $UseLeafRadiusPruning.ToString().ToLowerInvariant()
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
$env:HTTP_SERVER_MODE = $HttpServerMode
$env:TOP_K = $TopK.ToString([System.Globalization.CultureInfo]::InvariantCulture)
$env:LB_CPUS = $LbCpus.ToString([System.Globalization.CultureInfo]::InvariantCulture)
$env:API_CPUS = $ApiCpus.ToString([System.Globalization.CultureInfo]::InvariantCulture)
$env:LB_MEM_LIMIT = $LbMemLimit
$env:API_MEM_LIMIT = $ApiMemLimit
$env:LB_MEM_LIMIT_DEPLOY = $LbMemLimit.ToUpperInvariant()
$env:API_MEM_LIMIT_DEPLOY = $ApiMemLimit.ToUpperInvariant()

docker compose -f $composePath down --remove-orphans

if ($SkipBuild) {
	docker compose -f $composePath up -d
} else {
	docker compose -f $composePath up -d --build
}

& (Join-Path $PSScriptRoot "wait-ready.ps1")

$lbContainerId = (docker compose -f $composePath ps -q lb).Trim()
$inspect = docker inspect $lbContainerId | ConvertFrom-Json
$networkName = $inspect[0].NetworkSettings.Networks.PSObject.Properties.Name | Select-Object -First 1

$k6Arguments = @(
	"run",
	"--rm",
	"--network", $networkName,
	"-v", "${k6Workdir}:/work",
	"-w", "/work",
	"-e", "K6_NO_USAGE_REPORT=true",
	"-e", "K6_URL=http://lb:9999/ready",
	"-e", "K6_START_RATE=$K6StartRate",
	"-e", "K6_TARGET_RATE=$K6TargetRate",
	"-e", "K6_PRE_ALLOCATED_VUS=$K6PreAllocatedVUs",
	"-e", "K6_MAX_VUS=$K6MaxVUs",
	"-e", "K6_STAGE_DURATION=$K6StageDuration",
	"-e", "K6_GRACEFUL_STOP=$K6GracefulStop",
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

if (Test-Path $resultsPath) {
	Get-Content $resultsPath
} else {
	Write-Error "k6 completed without producing test/results.json."
}
