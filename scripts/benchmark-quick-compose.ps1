param(
	[string]$ComposeFile = "docker-compose.yml",
	[string]$OutputDirectory = "artifacts\compose-k6-quick",
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
	[int]$K6PreAllocatedVUs = 100,
	[int]$K6MaxVUs = 250,
	[string]$K6StageDuration = "12s",
	[string]$K6GracefulStop = "2s",
	[switch]$SkipBuild
)

$scriptPath = Join-Path $PSScriptRoot "benchmark-official-compose.ps1"

& $scriptPath `
	-ComposeFile $ComposeFile `
	-OutputDirectory $OutputDirectory `
	-RuntimeDataDir $RuntimeDataDir `
	-ApprovalThreshold $ApprovalThreshold `
	-HttpInlineScheduling $HttpInlineScheduling `
	-HttpIoQueueCount $HttpIoQueueCount `
	-HttpNoDelay $HttpNoDelay `
	-HttpParserMode $HttpParserMode `
	-HttpServerMode $HttpServerMode `
	-BeamLevel1 $BeamLevel1 `
	-BeamLevel2 $BeamLevel2 `
	-SearchIndexKind $SearchIndexKind `
	-RerankCount $RerankCount `
	-BoundaryRerankCount $BoundaryRerankCount `
	-UseLeafRadiusPruning $UseLeafRadiusPruning `
	-UseLastTransactionPartitionPruning $UseLastTransactionPartitionPruning `
	-TopK $TopK `
	-LbCpus $LbCpus `
	-ApiCpus $ApiCpus `
	-LbMemLimit $LbMemLimit `
	-ApiMemLimit $ApiMemLimit `
	-K6StartRate 900 `
	-K6TargetRate 900 `
	-K6PreAllocatedVUs $K6PreAllocatedVUs `
	-K6MaxVUs $K6MaxVUs `
	-K6StageDuration $K6StageDuration `
	-K6GracefulStop $K6GracefulStop `
	-SkipBuild:$SkipBuild
