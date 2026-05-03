param(
	[string]$TestDataPath = (Join-Path $PSScriptRoot "..\\..\\rinha-de-backend-2026\\test\\test-data.json"),
	[string]$RuntimeDataRoot = (Join-Path $PSScriptRoot "..\\runtime-data"),
	[string]$IndexKind = "HierarchicalBeamIvf",
	[string]$ParseMode = "ServiceManual",
	[int]$BeamLevel1 = 8,
	[int]$BeamLevel2 = 128,
	[int]$RerankCount = 48,
	[int]$BoundaryRerankCount = $RerankCount,
	[int]$TopK = 5,
	[double]$ApprovalThreshold = 0.6,
	[bool]$UseLeafRadiusPruning = $true,
	[bool]$UseLastTransactionPartitionPruning = $true,
	[int]$StartIndex = 0,
	[int]$Limit = 0,
	[int]$TraceEvery = 0,
	[int]$MismatchLimit = 32
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$resolvedTestDataPath = (Resolve-Path $TestDataPath).Path
$resolvedRuntimeDataRoot = (Resolve-Path $RuntimeDataRoot).Path

dotnet run -c Release --project (Join-Path $repoRoot "tools\\Rinha2026.Evaluator\\Rinha2026.Evaluator.csproj") -- `
	--test-data $resolvedTestDataPath `
	--runtime-data $resolvedRuntimeDataRoot `
	--index-kind $IndexKind `
	--parse-mode $ParseMode `
	--beam-level1 $BeamLevel1 `
	--beam-level2 $BeamLevel2 `
	--rerank-count $RerankCount `
	--boundary-rerank-count $BoundaryRerankCount `
	--top-k $TopK `
	--approval-threshold $ApprovalThreshold `
	--use-leaf-radius-pruning $UseLeafRadiusPruning `
	--use-last-transaction-partition-pruning $UseLastTransactionPartitionPruning `
	--start-index $StartIndex `
	--limit $Limit `
	--trace-every $TraceEvery `
	--mismatch-limit $MismatchLimit
