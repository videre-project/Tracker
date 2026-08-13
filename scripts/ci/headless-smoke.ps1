param(
  [string]$PublishDirectory = (Join-Path (Get-Location) 'publish'),
  [string]$DiagnosticsDirectory = (Join-Path (Get-Location) 'ci-diagnostics'),
  [int]$Port = 7101,
  [int]$TimeoutSeconds = 45
)

$ErrorActionPreference = 'Stop'

$executable = Join-Path $PublishDirectory 'Videre Tracker.exe'
if (-not (Test-Path $executable)) {
  throw "Published executable was not found: $executable"
}

$userData = Join-Path ([System.IO.Path]::GetTempPath()) "tracker-ci-$([guid]::NewGuid())"
New-Item -ItemType Directory -Path $userData -Force | Out-Null
New-Item -ItemType Directory -Path $DiagnosticsDirectory -Force | Out-Null
$stdoutLog = Join-Path $DiagnosticsDirectory 'tracker.stdout.log'
$stderrLog = Join-Path $DiagnosticsDirectory 'tracker.stderr.log'

$env:TRACKER_DISABLE_UI = '1'
$env:TRACKER_DISABLE_INSTALLER = '1'
$env:TRACKER_CI_TEST = '1'
$env:TRACKER_USER_DATA_FOLDER = $userData
$env:ASPNETCORE_ENVIRONMENT = 'Production'

$process = $null
try {
  $process = Start-Process -FilePath $executable -PassThru -WorkingDirectory $PublishDirectory -RedirectStandardOutput $stdoutLog -RedirectStandardError $stderrLog
  $uri = "http://localhost:$Port/api/client/getstate"
  $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
  $response = $null

  do {
    if ($process.HasExited) {
      throw "Tracker exited before becoming ready with code $($process.ExitCode)."
    }

    try {
      $response = Invoke-WebRequest -Uri $uri -SkipCertificateCheck -UseBasicParsing -TimeoutSec 3
    } catch {
      Start-Sleep -Milliseconds 500
    }
  } while ($null -eq $response -and (Get-Date) -lt $deadline)

  if ($null -eq $response) {
    throw "Tracker did not serve $uri within $TimeoutSeconds seconds."
  }

  if ($response.StatusCode -ne 200) {
    throw "Unexpected client-state status: $($response.StatusCode)."
  }

  $state = $response.Content | ConvertFrom-Json
  if ($state.status -ne 'disconnected') {
    throw "Expected disconnected state without MTGO, got '$($state.status)'."
  }

  foreach ($database in @('Event.db', 'Collection.db', 'Trade.db')) {
    $databasePath = Join-Path $userData "Database\$database"
    if (-not (Test-Path $databasePath)) {
      throw "Expected database was not created: $databasePath"
    }
  }

  Invoke-WebRequest -Uri "http://localhost:$Port/api/ci/shutdown" `
    -Method Post -UseBasicParsing -TimeoutSec 5 | Out-Null

  if (-not $process.WaitForExit(10000)) {
    throw 'Tracker did not exit cleanly after the smoke-test shutdown request.'
  }

  if ($process.ExitCode -ne 0) {
    throw "Tracker exited with code $($process.ExitCode) after graceful shutdown."
  }
} finally {
  if ($null -ne $process -and -not $process.HasExited) {
    Stop-Process -Id $process.Id -Force
    $process.WaitForExit()
  }

  Remove-Item Env:TRACKER_DISABLE_UI -ErrorAction SilentlyContinue
  Remove-Item Env:TRACKER_DISABLE_INSTALLER -ErrorAction SilentlyContinue
  Remove-Item Env:TRACKER_CI_TEST -ErrorAction SilentlyContinue
  Remove-Item Env:TRACKER_USER_DATA_FOLDER -ErrorAction SilentlyContinue
  Remove-Item Env:ASPNETCORE_ENVIRONMENT -ErrorAction SilentlyContinue
  Remove-Item -LiteralPath $userData -Recurse -Force -ErrorAction SilentlyContinue
}
