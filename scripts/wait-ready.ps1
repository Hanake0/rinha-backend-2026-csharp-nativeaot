param(
	[string]$Url = "http://localhost:9999/ready",
	[int]$MaxAttempts = 60,
	[int]$DelayMilliseconds = 1000
)

$ErrorActionPreference = "Stop"

for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
	try {
		$response = Invoke-WebRequest -Uri $Url -Method Get -UseBasicParsing

		if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 300) {
			return
		}
	} catch {
	}

	Start-Sleep -Milliseconds $DelayMilliseconds
}

throw "Timed out waiting for $Url"
