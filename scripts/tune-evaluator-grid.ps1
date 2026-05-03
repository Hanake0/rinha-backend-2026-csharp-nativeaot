param(
	[string]$TestDataPath = (Join-Path $PSScriptRoot "..\\..\\rinha-de-backend-2026\\test\\test-data.json"),
	[string]$RuntimeDataRoot = (Join-Path $PSScriptRoot "..\\runtime-data"),
	[string]$IndexKind = "HierarchicalBeamIvf",
	[string]$ParseMode = "ServiceManual",
	[int[]]$BeamLevel1Values = @(10),
	[int[]]$BeamLevel2Values = @(32),
	[int[]]$RerankCountValues = @(48),
	[int]$TopK = 5,
	[double]$ApprovalThreshold = 0.6,
	[int]$StartIndex = 0,
	[int]$Limit = 0,
	[int]$TraceEvery = 0,
	[int]$MismatchLimit = 32,
	[string]$OutputDirectory = "artifacts\\evaluator-grid"
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$outputRoot = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
	$OutputDirectory
} else {
	Join-Path $repoRoot $OutputDirectory
}

New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

$results = [System.Collections.Generic.List[object]]::new()
$configCount = $BeamLevel1Values.Count * $BeamLevel2Values.Count * $RerankCountValues.Count
$index = 0

foreach ($beamLevel1 in $BeamLevel1Values) {
	foreach ($beamLevel2 in $BeamLevel2Values) {
		foreach ($rerankCount in $RerankCountValues) {
			$index++
			Write-Host "[$index/$configCount] Evaluating b1=$beamLevel1 b2=$beamLevel2 rerank=$rerankCount"

			$json = & (Join-Path $PSScriptRoot "evaluate-official.ps1") `
				-TestDataPath $TestDataPath `
				-RuntimeDataRoot $RuntimeDataRoot `
				-IndexKind $IndexKind `
				-ParseMode $ParseMode `
				-BeamLevel1 $beamLevel1 `
				-BeamLevel2 $beamLevel2 `
				-RerankCount $rerankCount `
				-TopK $TopK `
				-ApprovalThreshold $ApprovalThreshold `
				-StartIndex $StartIndex `
				-Limit $Limit `
				-TraceEvery $TraceEvery `
				-MismatchLimit $MismatchLimit

			$evaluation = $json | ConvertFrom-Json

			$results.Add([pscustomobject]@{
				BeamLevel1 = $beamLevel1
				BeamLevel2 = $beamLevel2
				RerankCount = $rerankCount
				TopK = $TopK
				ApprovalThreshold = $ApprovalThreshold
				ParseMode = $ParseMode
				IndexKind = $IndexKind
				EvaluatedRequests = $evaluation.settings.evaluatedRequests
				FalsePositives = $evaluation.breakdown.falsePositiveDetections
				FalseNegatives = $evaluation.breakdown.falseNegativeDetections
				WeightedErrors = $evaluation.detection.weightedErrors
				DetectionScore = [double]$evaluation.detection.score
				CutTriggered = [bool]$evaluation.detection.cutTriggered
				SearchMeanUs = [double]$evaluation.latency.searchUs.mean
				SearchP95Us = [double]$evaluation.latency.searchUs.p95
				SearchP99Us = [double]$evaluation.latency.searchUs.p99
				TotalMeanUs = [double]$evaluation.latency.totalUs.mean
				TotalP95Us = [double]$evaluation.latency.totalUs.p95
				TotalP99Us = [double]$evaluation.latency.totalUs.p99
				ThroughputPerSecond = [double]$evaluation.latency.throughputPerSecond
			})
		}
	}
}

$sorted = $results | Sort-Object `
	@{ Expression = "CutTriggered"; Ascending = $true }, `
	@{ Expression = "DetectionScore"; Descending = $true }, `
	@{ Expression = "TotalP99Us"; Ascending = $true }, `
	@{ Expression = "TotalMeanUs"; Ascending = $true }

$timestamp = Get-Date -Format "yyyy-MM-dd-HHmmss"
$jsonPath = Join-Path $outputRoot "$timestamp-evaluator-grid.json"
$mdPath = Join-Path $outputRoot "$timestamp-evaluator-grid.md"

$sorted | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $jsonPath

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add("# Evaluator Grid")
$lines.Add("")
$lines.Add("- runtime data: $RuntimeDataRoot")
$lines.Add("- parse mode: $ParseMode")
$lines.Add("- index kind: $IndexKind")
$lines.Add("- topK: $TopK")
$lines.Add("- approval threshold: $ApprovalThreshold")
$lines.Add("")
$lines.Add("| Rank | Beam1 | Beam2 | Rerank | FP | FN | Weighted | Detection | Search P99 us | Total P99 us | Throughput/s |")
$lines.Add("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |")

$rank = 0

foreach ($entry in $sorted) {
	$rank++
	$lines.Add("| $rank | $($entry.BeamLevel1) | $($entry.BeamLevel2) | $($entry.RerankCount) | $($entry.FalsePositives) | $($entry.FalseNegatives) | $($entry.WeightedErrors) | $([string]::Format([System.Globalization.CultureInfo]::InvariantCulture, "{0:F2}", $entry.DetectionScore)) | $([string]::Format([System.Globalization.CultureInfo]::InvariantCulture, "{0:F3}", $entry.SearchP99Us)) | $([string]::Format([System.Globalization.CultureInfo]::InvariantCulture, "{0:F3}", $entry.TotalP99Us)) | $([string]::Format([System.Globalization.CultureInfo]::InvariantCulture, "{0:F2}", $entry.ThroughputPerSecond)) |")
}

$lines | Set-Content -LiteralPath $mdPath

Write-Host ""
Write-Host "Top configurations:"
$sorted | Select-Object -First ([Math]::Min(10, $sorted.Count))
Write-Host ""
Write-Host "JSON: $jsonPath"
Write-Host "Markdown: $mdPath"
