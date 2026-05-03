param(
	[string]$TestDataPath = (Join-Path $PSScriptRoot "..\\..\\rinha-de-backend-2026\\test\\test-data.json"),
	[string[]]$RuntimeDataDirs = @("runtime-data"),
	[int[]]$BeamLevel1Values = @(10),
	[int[]]$BeamLevel2Values = @(32),
	[int[]]$RerankCountValues = @(48),
	[string]$ParseMode = "ManualParser",
	[string]$IndexKind = "HierarchicalBeamIvf",
	[int]$TopK = 5,
	[double]$ApprovalThreshold = 0.6,
	[bool]$UseLeafRadiusPruning = $false,
	[bool]$UseLastTransactionPartitionPruning = $true,
	[string]$OutputCsv = "",
	[int]$TraceEvery = 0
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$resolvedTestDataPath = (Resolve-Path $TestDataPath).Path
$evaluatorProjectPath = Join-Path $repoRoot "tools\\Rinha2026.Evaluator\\Rinha2026.Evaluator.csproj"

if ([string]::IsNullOrWhiteSpace($OutputCsv)) {
	$outputDirectory = Join-Path $repoRoot "artifacts\\evaluator-sweeps"
	New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
	$timestamp = Get-Date -Format "yyyy-MM-dd-HHmmss"
	$OutputCsv = Join-Path $outputDirectory "sweep-$timestamp.csv"
}

Write-Host "Building evaluator once before the sweep..."
dotnet build $evaluatorProjectPath -c Release | Out-Null

$results = [System.Collections.Generic.List[object]]::new()
$totalRuns = $RuntimeDataDirs.Count * $BeamLevel1Values.Count * $BeamLevel2Values.Count * $RerankCountValues.Count
$currentRun = 0

foreach ($runtimeDataDir in $RuntimeDataDirs) {
	$resolvedRuntimeDataDir = if ([System.IO.Path]::IsPathRooted($runtimeDataDir)) {
		(Resolve-Path $runtimeDataDir).Path
	} else {
		(Resolve-Path (Join-Path $repoRoot $runtimeDataDir)).Path
	}

	foreach ($beamLevel1 in $BeamLevel1Values) {
		foreach ($beamLevel2 in $BeamLevel2Values) {
			foreach ($rerankCount in $RerankCountValues) {
				$currentRun++
				Write-Host ("[{0}/{1}] runtime={2} b1={3} b2={4} rerank={5}" -f `
					$currentRun, $totalRuns, $runtimeDataDir, $beamLevel1, $beamLevel2, $rerankCount)

				$json = dotnet run -c Release --no-build --project $evaluatorProjectPath -- `
					--test-data $resolvedTestDataPath `
					--runtime-data $resolvedRuntimeDataDir `
					--index-kind $IndexKind `
					--parse-mode $ParseMode `
					--beam-level1 $beamLevel1 `
					--beam-level2 $beamLevel2 `
					--rerank-count $rerankCount `
					--top-k $TopK `
					--approval-threshold $ApprovalThreshold `
					--use-leaf-radius-pruning $UseLeafRadiusPruning `
					--use-last-transaction-partition-pruning $UseLastTransactionPartitionPruning `
					--trace-every $TraceEvery

				$result = $json | ConvertFrom-Json
				$trace = $result.Trace
				$results.Add([pscustomobject]@{
						runtime_data = $runtimeDataDir
						beam_level1 = $beamLevel1
						beam_level2 = $beamLevel2
						rerank_count = $rerankCount
						fp = [int]$result.Breakdown.FalsePositiveDetections
						fn = [int]$result.Breakdown.FalseNegativeDetections
						detection_score = [double]$result.Detection.Score
						search_p50_us = [double]$result.Latency.SearchUs.P50
						search_p95_us = [double]$result.Latency.SearchUs.P95
						search_p99_us = [double]$result.Latency.SearchUs.P99
						search_mean_us = [double]$result.Latency.SearchUs.Mean
						throughput_per_second = [double]$result.Latency.ThroughputPerSecond
						trace_sample_count = if ($trace) { [int]$trace.SampleCount } else { 0 }
						trace_candidate_scan_p50 = if ($trace) { [int]$trace.CandidateScanCount.P50 } else { 0 }
						trace_candidate_scan_p95 = if ($trace) { [int]$trace.CandidateScanCount.P95 } else { 0 }
						trace_candidate_scan_p99 = if ($trace) { [int]$trace.CandidateScanCount.P99 } else { 0 }
						trace_candidate_scan_mean = if ($trace) { [int]$trace.CandidateScanCount.Mean } else { 0 }
						trace_candidate_rerank_p50 = if ($trace) { [int]$trace.CandidateRerankCount.P50 } else { 0 }
						trace_candidate_rerank_p95 = if ($trace) { [int]$trace.CandidateRerankCount.P95 } else { 0 }
						trace_candidate_rerank_p99 = if ($trace) { [int]$trace.CandidateRerankCount.P99 } else { 0 }
						trace_candidate_rerank_mean = if ($trace) { [int]$trace.CandidateRerankCount.Mean } else { 0 }
						trace_selected_leaf_p50 = if ($trace) { [int]$trace.SelectedLeafCount.P50 } else { 0 }
						trace_selected_leaf_p95 = if ($trace) { [int]$trace.SelectedLeafCount.P95 } else { 0 }
						trace_selected_leaf_p99 = if ($trace) { [int]$trace.SelectedLeafCount.P99 } else { 0 }
						trace_selected_leaf_mean = if ($trace) { [int]$trace.SelectedLeafCount.Mean } else { 0 }
						trace_max_leaf_size_p50 = if ($trace) { [int]$trace.MaxSelectedLeafSize.P50 } else { 0 }
						trace_max_leaf_size_p95 = if ($trace) { [int]$trace.MaxSelectedLeafSize.P95 } else { 0 }
						trace_max_leaf_size_p99 = if ($trace) { [int]$trace.MaxSelectedLeafSize.P99 } else { 0 }
						trace_max_leaf_size_mean = if ($trace) { [int]$trace.MaxSelectedLeafSize.Mean } else { 0 }
						trace_pruned_leaf_p50 = if ($trace) { [int]$trace.PrunedLeafCount.P50 } else { 0 }
						trace_pruned_leaf_p95 = if ($trace) { [int]$trace.PrunedLeafCount.P95 } else { 0 }
						trace_pruned_leaf_p99 = if ($trace) { [int]$trace.PrunedLeafCount.P99 } else { 0 }
						trace_pruned_leaf_mean = if ($trace) { [int]$trace.PrunedLeafCount.Mean } else { 0 }
						trace_pruned_candidate_p50 = if ($trace) { [int]$trace.PrunedCandidateCount.P50 } else { 0 }
						trace_pruned_candidate_p95 = if ($trace) { [int]$trace.PrunedCandidateCount.P95 } else { 0 }
						trace_pruned_candidate_p99 = if ($trace) { [int]$trace.PrunedCandidateCount.P99 } else { 0 }
						trace_pruned_candidate_mean = if ($trace) { [int]$trace.PrunedCandidateCount.Mean } else { 0 }
						trace_secondary_scan_p50 = if ($trace) { [int]$trace.SecondaryCandidateScanCount.P50 } else { 0 }
						trace_secondary_scan_p95 = if ($trace) { [int]$trace.SecondaryCandidateScanCount.P95 } else { 0 }
						trace_secondary_scan_p99 = if ($trace) { [int]$trace.SecondaryCandidateScanCount.P99 } else { 0 }
						trace_secondary_scan_mean = if ($trace) { [int]$trace.SecondaryCandidateScanCount.Mean } else { 0 }
					})
			}
		}
	}
}

$results `
| Sort-Object fp, fn, search_p99_us, search_mean_us `
| Export-Csv -Path $OutputCsv -NoTypeInformation

Write-Host ""
Write-Host "Sweep written to: $OutputCsv"
Write-Host ""
Write-Host "Best candidates:"
$results `
| Sort-Object fp, fn, search_p99_us, search_mean_us `
| Select-Object -First 10 `
| Format-Table -AutoSize
