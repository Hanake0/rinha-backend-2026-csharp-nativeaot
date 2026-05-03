param(
	[string]$Url = "http://localhost:9999/fraud-score",
	[int]$StartIndex = 0,
	[int]$Limit = 0,
	[int]$Concurrency = 1,
	[int]$TimeoutMs = 2001,
	[int]$MismatchLimit = 32,
	[string]$TestDataPath = "..\\rinha-de-backend-2026\\test\\test-data.json",
	[string]$DockerNetwork = "rinha-2026-csharp-nativeaot_default",
	[switch]$UseDockerNetwork
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = "tools/Rinha2026.HttpReplay/Rinha2026.HttpReplay.csproj"

$toolArgs = @(
	"run",
	"-c", "Release",
	"--project", $projectPath,
	"--",
	"--url", $Url,
	"--test-data", $TestDataPath,
	"--start-index", $StartIndex,
	"--limit", $Limit,
	"--concurrency", $Concurrency,
	"--timeout-ms", $TimeoutMs,
	"--mismatch-limit", $MismatchLimit
)

if ($UseDockerNetwork) {
	$workspaceRoot = Split-Path -Parent $repoRoot
	$containerRepoRoot = "/workroot/rinha-2026-csharp-nativeaot"
	$containerTestDataPath = "/workroot/rinha-de-backend-2026/test/test-data.json"

	$dockerArgs = @(
		"run",
		"--rm",
		"--network", $DockerNetwork,
		"-v", "${workspaceRoot}:/workroot",
		"-w", $containerRepoRoot,
		"mcr.microsoft.com/dotnet/sdk:10.0",
		"dotnet"
	) + @(
		"run",
		"-c", "Release",
		"--project", "tools/Rinha2026.HttpReplay/Rinha2026.HttpReplay.csproj",
		"--",
		"--url", $Url,
		"--test-data", $containerTestDataPath,
		"--start-index", $StartIndex,
		"--limit", $Limit,
		"--concurrency", $Concurrency,
		"--timeout-ms", $TimeoutMs,
		"--mismatch-limit", $MismatchLimit
	)

	& docker @dockerArgs
	exit $LASTEXITCODE
}

	& dotnet @toolArgs
exit $LASTEXITCODE
