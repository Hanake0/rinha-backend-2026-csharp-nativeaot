param(
	[string]$ComposeFile = "docker-compose.yml",
	[string]$OutputDirectory = "artifacts\compose-k6-quick",
	[string]$RuntimeDataDir = "runtime-data-256x128-radii-f32-stable-s524k",
	[string]$ApprovalThreshold = "0.6",
	[string]$HttpInlineScheduling = "true",
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
	[bool]$UseLastTransactionPartitionPruning = $false,
	[int]$TopK = 5,
	[double]$LbCpus = 0.15,
	[double]$ApiCpus = 0.425,
	[string]$LbMemLimit = "48m",
	[string]$ApiMemLimit = "151m",
	[string]$DotNetProcessorCount = "1",
	[string]$DotNetSocketInlineCompletions = "1",
	[string]$DotNetSocketThreadCount = "1",
	[int]$K6PreAllocatedVUs = 100,
	[int]$K6MaxVUs = 250,
	[string]$K6StageDuration = "12s",
	[string]$K6GracefulStop = "2s",
	[int]$MeasurementRuns = 3,
	[int]$WarmupRate = 900,
	[int]$WarmupPreAllocatedVUs = 100,
	[int]$WarmupMaxVUs = 250,
	[string]$WarmupStageDuration = "8s",
	[string]$WarmupGracefulStop = "1s",
	[switch]$NoWarmup,
	[switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

$scriptPath = Join-Path $PSScriptRoot "benchmark-official-compose.ps1"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$outputRoot = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
	$OutputDirectory
} else {
	Join-Path $repoRoot $OutputDirectory
}

New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

function Get-Median {
	param([double[]]$Values)

	if ($Values.Count -eq 0) {
		return [double]::NaN
	}

	$sorted = $Values | Sort-Object
	$middle = [int]($sorted.Count / 2)

	if (($sorted.Count % 2) -eq 1) {
		return [double]$sorted[$middle]
	}

	return ([double]$sorted[$middle - 1] + [double]$sorted[$middle]) / 2.0
}

$commonParameters = @{
	ComposeFile = $ComposeFile
	RuntimeDataDir = $RuntimeDataDir
	ApprovalThreshold = $ApprovalThreshold
	HttpInlineScheduling = $HttpInlineScheduling
	HttpIoQueueCount = $HttpIoQueueCount
	HttpNoDelay = $HttpNoDelay
	HttpParserMode = $HttpParserMode
	HttpServerMode = $HttpServerMode
	BeamLevel1 = $BeamLevel1
	BeamLevel2 = $BeamLevel2
	SearchIndexKind = $SearchIndexKind
	RerankCount = $RerankCount
	BoundaryRerankCount = $BoundaryRerankCount
	UseLeafRadiusPruning = $UseLeafRadiusPruning
	UseLastTransactionPartitionPruning = $UseLastTransactionPartitionPruning
	TopK = $TopK
	LbCpus = $LbCpus
	ApiCpus = $ApiCpus
	LbMemLimit = $LbMemLimit
	ApiMemLimit = $ApiMemLimit
	DotNetProcessorCount = $DotNetProcessorCount
	DotNetSocketInlineCompletions = $DotNetSocketInlineCompletions
	DotNetSocketThreadCount = $DotNetSocketThreadCount
}

if (-not $NoWarmup) {
	& $scriptPath @commonParameters `
		-OutputDirectory (Join-Path $outputRoot "warmup") `
		-K6StartRate $WarmupRate `
		-K6TargetRate $WarmupRate `
		-K6PreAllocatedVUs $WarmupPreAllocatedVUs `
		-K6MaxVUs $WarmupMaxVUs `
		-K6StageDuration $WarmupStageDuration `
		-K6GracefulStop $WarmupGracefulStop `
		-SkipBuild:$SkipBuild
}

$runSummaries = @()

for ($runIndex = 1; $runIndex -le $MeasurementRuns; $runIndex++) {
	$reuseRunningStack = (-not $NoWarmup) -or ($runIndex -gt 1)
	$runOutputDirectory = Join-Path $outputRoot ("run-{0:D2}" -f $runIndex)
	$shouldSkipBuild = $SkipBuild -or $reuseRunningStack

	& $scriptPath @commonParameters `
		-OutputDirectory $runOutputDirectory `
		-K6StartRate 900 `
		-K6TargetRate 900 `
		-K6PreAllocatedVUs $K6PreAllocatedVUs `
		-K6MaxVUs $K6MaxVUs `
		-K6StageDuration $K6StageDuration `
		-K6GracefulStop $K6GracefulStop `
		-SkipBuild:$shouldSkipBuild `
		-SkipComposeRestart:$reuseRunningStack

	$resultsPath = Join-Path $runOutputDirectory "k6-workdir\test\results.json"

	if (-not (Test-Path $resultsPath)) {
		throw "$resultsPath is missing after quick benchmark run $runIndex."
	}

	$result = Get-Content $resultsPath -Raw | ConvertFrom-Json
	$breakdown = $result.scoring.breakdown
	$runSummaries += [pscustomobject]@{
		Run = $runIndex
		P99Ms = [double]$result.p99.Replace("ms", "")
		FalsePositive = [int]$breakdown.false_positive_detections
		FalseNegative = [int]$breakdown.false_negative_detections
		HttpErrors = [int]$breakdown.http_errors
		FinalScore = [double]$result.scoring.final_score
	}
}

$p99Values = [double[]]($runSummaries | ForEach-Object { $_.P99Ms })
$scoreValues = [double[]]($runSummaries | ForEach-Object { $_.FinalScore })
$allExact = ($runSummaries | Where-Object { $_.FalsePositive -ne 0 -or $_.FalseNegative -ne 0 -or $_.HttpErrors -ne 0 }).Count -eq 0

$summary = [pscustomobject]@{
	configuration = [pscustomobject]@{
		compose_file = $ComposeFile
		runtime_data_dir = $RuntimeDataDir
		http_inline_scheduling = $HttpInlineScheduling
		use_last_transaction_partition_pruning = $UseLastTransactionPartitionPruning
		dotnet_processor_count = $DotNetProcessorCount
		dotnet_socket_inline_completions = $DotNetSocketInlineCompletions
		dotnet_socket_thread_count = $DotNetSocketThreadCount
		measurement_runs = $MeasurementRuns
		warmup_enabled = -not $NoWarmup
		warmup_rate = $WarmupRate
		warmup_stage_duration = $WarmupStageDuration
		measurement_rate = 900
		measurement_stage_duration = $K6StageDuration
	}
	aggregate = [pscustomobject]@{
		all_exact = $allExact
		min_p99_ms = [double](($p99Values | Measure-Object -Minimum).Minimum)
		median_p99_ms = [double](Get-Median $p99Values)
		max_p99_ms = [double](($p99Values | Measure-Object -Maximum).Maximum)
		min_final_score = [double](($scoreValues | Measure-Object -Minimum).Minimum)
		median_final_score = [double](Get-Median $scoreValues)
		max_final_score = [double](($scoreValues | Measure-Object -Maximum).Maximum)
	}
	runs = $runSummaries
}

$summaryPath = Join-Path $outputRoot "summary.json"
$summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $summaryPath

Write-Host ""
Write-Host "Quick benchmark summary:"
$runSummaries | Format-Table Run, P99Ms, FalsePositive, FalseNegative, HttpErrors, FinalScore -AutoSize | Out-Host
Write-Host "All exact: $allExact"
Write-Host ("P99 range: {0:N2} ms .. {1:N2} ms (median {2:N2} ms)" -f $summary.aggregate.min_p99_ms, $summary.aggregate.max_p99_ms, $summary.aggregate.median_p99_ms)
Write-Host ("Final score range: {0:N2} .. {1:N2} (median {2:N2})" -f $summary.aggregate.min_final_score, $summary.aggregate.max_final_score, $summary.aggregate.median_final_score)
Write-Host "Summary written to: $summaryPath"
