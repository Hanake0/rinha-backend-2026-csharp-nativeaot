param(
	[string]$ComposeFile = "docker-compose.yml",
	[string]$OfficialRepoPath = "..\rinha-de-backend-2026",
	[string]$OutputDirectory = "artifacts\official-host-benchmark",
	[int]$Repetitions = 1,
	[switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $false

if (-not $IsLinux) {
	throw "benchmark-official-host.ps1 is intended for Linux hosts (for docker --network host)."
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$officialRoot = if ([System.IO.Path]::IsPathRooted($OfficialRepoPath)) {
	(Resolve-Path $OfficialRepoPath).Path
} else {
	(Resolve-Path (Join-Path $repoRoot $OfficialRepoPath)).Path
}
$composePaths = $ComposeFile.Split(';', [System.StringSplitOptions]::RemoveEmptyEntries) | ForEach-Object {
	$composeFilePath = $_.Trim()

	if ([System.IO.Path]::IsPathRooted($composeFilePath)) {
		(Resolve-Path $composeFilePath).Path
	} else {
		(Resolve-Path (Join-Path $repoRoot $composeFilePath)).Path
	}
}
$composeArguments = @("compose")

foreach ($composePath in $composePaths) {
	$composeArguments += @("-f", $composePath)
}

$testScriptPath = Join-Path $officialRoot "test\test.js"
$testDataPath = Join-Path $officialRoot "test\test-data.json"

if (-not (Test-Path $testScriptPath)) {
	throw "$testScriptPath was not found."
}

if (-not (Test-Path $testDataPath)) {
	throw "$testDataPath was not found."
}

$outputRoot = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
	$OutputDirectory
} else {
	Join-Path $repoRoot $OutputDirectory
}

New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

function Get-Median {
	param([double[]]$Values)

	$sorted = $Values | Sort-Object
	$middle = [int]($sorted.Count / 2)

	if (($sorted.Count % 2) -eq 1) {
		return [double]$sorted[$middle]
	}

	return ([double]$sorted[$middle - 1] + [double]$sorted[$middle]) / 2.0
}

docker pull grafana/k6:latest | Out-Null

$runSummaries = @()

for ($runIndex = 1; $runIndex -le $Repetitions; $runIndex++) {
	$runOutputDirectory = Join-Path $outputRoot ("run-{0:D2}" -f $runIndex)
	$k6Workdir = Join-Path $runOutputDirectory "k6-workdir"
	$stdoutPath = Join-Path $runOutputDirectory "k6-output.txt"
	$stderrPath = Join-Path $runOutputDirectory "k6-stderr.txt"
	$resultsPath = Join-Path $k6Workdir "test\results.json"
	$statsPath = Join-Path $runOutputDirectory "docker-stats.txt"

	New-Item -ItemType Directory -Path (Join-Path $k6Workdir "test") -Force | Out-Null
	Copy-Item -LiteralPath $testScriptPath -Destination (Join-Path $k6Workdir "test.js") -Force
	Copy-Item -LiteralPath $testDataPath -Destination (Join-Path $k6Workdir "test-data.json") -Force

	& docker @composeArguments down --remove-orphans

	if ($SkipBuild) {
		& docker @composeArguments up -d
	} else {
		& docker @composeArguments up -d --build
	}

	& (Join-Path $PSScriptRoot "wait-ready.ps1")

	$lbContainerId = (& docker @composeArguments ps -q lb).Trim()
	& docker @composeArguments ps
	docker stats --no-stream $lbContainerId rinha2026-api1 rinha2026-api2 | Tee-Object -FilePath $statsPath

	$k6Arguments = @(
		"run",
		"--rm",
		"--network", "host",
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

	if (-not (Test-Path $resultsPath)) {
		throw "$resultsPath is missing after run $runIndex."
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
$summary = [pscustomobject]@{
	compose_file = $ComposeFile
	official_repo = $officialRoot
	repetitions = $Repetitions
	runs = $runSummaries
	aggregate = [pscustomobject]@{
		min_p99_ms = [double](($p99Values | Measure-Object -Minimum).Minimum)
		median_p99_ms = [double](Get-Median $p99Values)
		max_p99_ms = [double](($p99Values | Measure-Object -Maximum).Maximum)
		min_final_score = [double](($scoreValues | Measure-Object -Minimum).Minimum)
		median_final_score = [double](Get-Median $scoreValues)
		max_final_score = [double](($scoreValues | Measure-Object -Maximum).Maximum)
	}
}

$summaryPath = Join-Path $outputRoot "summary.json"
$summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $summaryPath

Write-Host ""
Write-Host "Official host benchmark summary:"
$runSummaries | Format-Table Run, P99Ms, FalsePositive, FalseNegative, HttpErrors, FinalScore -AutoSize | Out-Host
Write-Host ("P99 range: {0:N2} ms .. {1:N2} ms (median {2:N2} ms)" -f $summary.aggregate.min_p99_ms, $summary.aggregate.max_p99_ms, $summary.aggregate.median_p99_ms)
Write-Host ("Final score range: {0:N2} .. {1:N2} (median {2:N2})" -f $summary.aggregate.min_final_score, $summary.aggregate.max_final_score, $summary.aggregate.median_final_score)
Write-Host "Summary written to: $summaryPath"
