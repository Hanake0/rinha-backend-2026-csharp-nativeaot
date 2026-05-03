param(
	[string]$ComposeFile = "docker-compose.yml",
	[string]$ProfileComposeFile = "docker-compose.profile.yml",
	[string]$TestScriptPath = "..\\rinha-de-backend-2026\\test\\test.js",
	[string]$TestDataPath = "..\\rinha-de-backend-2026\\test\\test-data.json",
	[string]$OutputDirectory = "artifacts\\compose-profile",
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
	[int]$K6StartRate = 1,
	[int]$K6TargetRate = 900,
	[int]$K6PreAllocatedVUs = 100,
	[int]$K6MaxVUs = 250,
	[string]$K6StageDuration = "120s",
	[string]$K6GracefulStop = "10s",
	[switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $false

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$composePath = Join-Path $repoRoot $ComposeFile
$profileComposePath = Join-Path $repoRoot $ProfileComposeFile
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
$stderrPath = Join-Path $outputRoot "k6-stderr.txt"
$memorySamplesPath = Join-Path $outputRoot "memory-samples.csv"
$api1ProfilePath = Join-Path $outputRoot "api1-profile.json"
$api2ProfilePath = Join-Path $outputRoot "api2-profile.json"

if (-not (Test-Path (Join-Path $runtimeDataRoot "index\\manifest.json"))) {
	throw "$runtimeDataRoot\\index\\manifest.json is missing. Run scripts/prepare-runtime-data.ps1 first."
}

New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
New-Item -ItemType Directory -Path $k6Workdir -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $k6Workdir "test") -Force | Out-Null

$patchedScript = (Get-Content $testScriptFullPath -Raw).Replace(
	"http://localhost:9999/fraud-score",
	"http://lb:9999/fraud-score")
$patchedScript = $patchedScript.Replace(
	"startRate: 1,",
	"startRate: __ENV.K6_START_RATE ? Number(__ENV.K6_START_RATE) : 1,")
$patchedScript = $patchedScript.Replace(
	"preAllocatedVUs: 100,",
	"preAllocatedVUs: __ENV.K6_PRE_ALLOCATED_VUS ? Number(__ENV.K6_PRE_ALLOCATED_VUS) : 100,")
$patchedScript = $patchedScript.Replace(
	"maxVUs: 250,",
	"maxVUs: __ENV.K6_MAX_VUS ? Number(__ENV.K6_MAX_VUS) : 250,")
$patchedScript = $patchedScript.Replace(
	"gracefulStop: '10s',",
	"gracefulStop: __ENV.K6_GRACEFUL_STOP || '10s',")
$patchedScript = $patchedScript.Replace(
	"{ duration: '120s', target: 900 },",
	"{ duration: __ENV.K6_STAGE_DURATION || '120s', target: __ENV.K6_TARGET_RATE ? Number(__ENV.K6_TARGET_RATE) : 900 },")
Set-Content -LiteralPath $patchedScriptPath -Value $patchedScript -NoNewline
Copy-Item -LiteralPath $testDataFullPath -Destination $patchedDataPath -Force

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

docker compose -f $composePath -f $profileComposePath down --remove-orphans

if ($SkipBuild) {
	docker compose -f $composePath -f $profileComposePath up -d
} else {
	docker compose -f $composePath -f $profileComposePath up -d --build
}

& (Join-Path $PSScriptRoot "wait-ready.ps1")

Invoke-RestMethod -Uri "http://localhost:10001/debug/profile/reset" -Method Post | Out-Null
Invoke-RestMethod -Uri "http://localhost:10002/debug/profile/reset" -Method Post | Out-Null

$samplerScript = {
	param($composePathValue, $profileComposePathValue, $outputPath)

	[System.IO.File]::WriteAllText(
		$outputPath,
		"timestamp,container,cpu_percent,mem_usage,mem_limit,mem_percent,net_io,block_io,pids`n",
		[System.Text.Encoding]::UTF8)

	while ($true) {
		$timestamp = (Get-Date).ToString("o", [System.Globalization.CultureInfo]::InvariantCulture)
		$stats = docker compose -f $composePathValue -f $profileComposePathValue ps -q | ForEach-Object {
			docker stats --no-stream --format "{{.Name}},{{.CPUPerc}},{{.MemUsage}},{{.MemPerc}},{{.NetIO}},{{.BlockIO}},{{.PIDs}}" $_
		}

		foreach ($line in $stats) {
			[System.IO.File]::AppendAllText(
				$outputPath,
				"$timestamp,$line`n",
				[System.Text.Encoding]::UTF8)
		}

		Start-Sleep -Seconds 1
	}
}

$samplerJob = Start-Job -ScriptBlock $samplerScript -ArgumentList $composePath, $profileComposePath, $memorySamplesPath

try {
	$lbContainerId = (docker compose -f $composePath -f $profileComposePath ps -q lb).Trim()
	$inspect = docker inspect $lbContainerId | ConvertFrom-Json
	$networkName = $inspect[0].NetworkSettings.Networks.PSObject.Properties.Name | Select-Object -First 1

	docker pull grafana/k6:latest | Out-Null

	$k6Arguments = @(
		"run",
		"--rm",
		"--network", $networkName,
		"-v", "${k6Workdir}:/work",
		"-w", "/work",
		"-e", "K6_NO_USAGE_REPORT=true",
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
} finally {
	if ($samplerJob -is [System.Management.Automation.Job]) {
		Stop-Job -Job $samplerJob | Out-Null
		Receive-Job -Job $samplerJob -Wait | Out-Null
		Remove-Job -Job $samplerJob | Out-Null
	}
}

Invoke-RestMethod -Uri "http://localhost:10001/debug/profile" -Method Get | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $api1ProfilePath
Invoke-RestMethod -Uri "http://localhost:10002/debug/profile" -Method Get | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $api2ProfilePath

Write-Host ""
Write-Host "Profile outputs:"
Write-Host "  api1: $api1ProfilePath"
Write-Host "  api2: $api2ProfilePath"
Write-Host "  memory: $memorySamplesPath"
