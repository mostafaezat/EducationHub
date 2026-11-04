#Requires -Version 5.1
<#
.SYNOPSIS
    Runs the backend + frontend verification on this machine (real SQL Server) and writes
    every result to tools\claude\logs so Claude can read it. Does not change any code.

.PARAMETER Only
    Run a subset: build, ef, unit, integration, frontend, smoke (comma separated). Default: all.

.PARAMETER Filter
    Optional dotnet test --filter expression for unit/integration (e.g. "FullyQualifiedName~Finance").
#>
[CmdletBinding()]
param(
    [string]$Only = 'build,ef,unit,integration,frontend,smoke',
    [string]$Filter = ''
)

$ErrorActionPreference = 'Continue'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Set-Location -LiteralPath $root

$logs = Join-Path $PSScriptRoot 'logs'
New-Item -ItemType Directory -Force -Path $logs | Out-Null
Get-ChildItem $logs -File | Remove-Item -Force

$steps = $Only.Split(',') | ForEach-Object { $_.Trim().ToLowerInvariant() }
$summary = New-Object System.Collections.Generic.List[string]
$summary.Add("machine=$env:COMPUTERNAME started=$(Get-Date -Format o) dotnet=$(dotnet --version) node=$(node --version 2>$null)")

function Run($name, [scriptblock]$cmd) {
    Write-Host "[run-checks] $name ..." -ForegroundColor Cyan
    $log = Join-Path $logs "$name.log"
    $sw = [Diagnostics.Stopwatch]::StartNew()
    & $cmd *>&1 | Tee-Object -FilePath $log | Out-Null
    $code = $LASTEXITCODE
    $sw.Stop()
    $line = "{0}: exit={1} seconds={2:N0}" -f $name, $code, $sw.Elapsed.TotalSeconds
    $summary.Add($line)
    $color = 'Green'; if ($code -ne 0) { $color = 'Red' }
    Write-Host "[run-checks] $line" -ForegroundColor $color
}

$filterArgs = @()
if ($Filter) { $filterArgs = @('--filter', $Filter) }

if ($steps -contains 'build') {
    Run 'build' { dotnet build EducationHub.sln -nologo -clp:NoSummary }
}
if ($steps -contains 'ef') {
    Run 'ef-pending' { dotnet ef migrations has-pending-model-changes -p src\EducationHub.Infrastructure -s src\EducationHub.API }
    Run 'ef-list' { dotnet ef migrations list -p src\EducationHub.Infrastructure -s src\EducationHub.API --no-build }
}
if ($steps -contains 'unit') {
    Run 'unit' { dotnet test tests\EducationHub.Tests.Unit --no-build --logger "trx;LogFileName=unit.trx" --results-directory $logs @filterArgs }
}
if ($steps -contains 'integration') {
    Run 'integration' { dotnet test tests\EducationHub.Tests.Integration --no-build --logger "trx;LogFileName=integration.trx" --results-directory $logs @filterArgs }
}
if ($steps -contains 'frontend') {
    Push-Location admin
    Run 'frontend-build' { npx ng build }
    Run 'frontend-test' { npm run test:ci }
    Pop-Location
}
if ($steps -contains 'smoke') {
    # Starts the real API against the dev database, waits for /health, hits Swagger, then stops it.
    Run 'smoke' {
        $env:ASPNETCORE_ENVIRONMENT = 'Development'
        $apiLog = Join-Path $logs 'api-runtime.log'
        $p = Start-Process dotnet -ArgumentList 'run','--no-build','--project','src\EducationHub.API','--launch-profile','EducationHub.API' `
            -RedirectStandardOutput $apiLog -RedirectStandardError (Join-Path $logs 'api-runtime.err.log') -PassThru -WindowStyle Hidden
        $ok = $false
        for ($i = 0; $i -lt 90 -and -not $ok; $i++) {
            Start-Sleep -Seconds 2
            try { $r = Invoke-WebRequest 'http://localhost:57735/health/ready' -UseBasicParsing -TimeoutSec 3; $ok = $r.StatusCode -eq 200 } catch { }
        }
        "health=$ok"
        try { $s = Invoke-WebRequest 'http://localhost:57735/swagger/v1/swagger.json' -UseBasicParsing -TimeoutSec 10; "swagger=$($s.StatusCode) bytes=$($s.Content.Length)"; $s.Content | Out-File (Join-Path $logs 'swagger.json') -Encoding utf8 } catch { "swagger=FAILED $_" }
        Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
        if (-not $ok) { $global:LASTEXITCODE = 1 } else { $global:LASTEXITCODE = 0 }
    }
}

$summary.Add("finished=$(Get-Date -Format o)")
$summary | Set-Content (Join-Path $logs 'SUMMARY.txt') -Encoding utf8
Write-Host ''
Write-Host '[run-checks] DONE. Results are in tools\claude\logs. Tell Claude it finished.' -ForegroundColor Green
