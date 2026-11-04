# EducationHub verification runner (written by Claude; safe to delete the _claude_verify folder).
# Stage 1: environment, build, unit tests, integration tests (throw-away test DB per run).
$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent $PSScriptRoot
$logs = Join-Path $PSScriptRoot 'logs'
New-Item -ItemType Directory -Force -Path $logs | Out-Null
Remove-Item -Force -ErrorAction SilentlyContinue (Join-Path $logs '*')
Set-Location -LiteralPath $root
"started $(Get-Date -Format o)" | Out-File (Join-Path $logs 'status.txt')

dotnet --info *> (Join-Path $logs '00-env.log')
try { Get-Service | Where-Object { $_.Name -like 'MSSQL*' -or $_.Name -like 'SQLAgent*' } | Format-Table -AutoSize | Out-String | Out-File -Append (Join-Path $logs '00-env.log') } catch {}

dotnet build EducationHub.sln -nologo *> (Join-Path $logs '01-build.log')
"build exit $LASTEXITCODE" | Out-File -Append (Join-Path $logs 'status.txt')

dotnet test tests\EducationHub.Tests.Unit --no-build -nologo --logger "trx;LogFileName=unit.trx" --results-directory $logs *> (Join-Path $logs '02-unit.log')
"unit exit $LASTEXITCODE" | Out-File -Append (Join-Path $logs 'status.txt')

dotnet test tests\EducationHub.Tests.Integration --no-build -nologo --logger "trx;LogFileName=integration.trx" --logger "console;verbosity=normal" --results-directory $logs *> (Join-Path $logs '03-integration.log')
"integration exit $LASTEXITCODE" | Out-File -Append (Join-Path $logs 'status.txt')

"finished $(Get-Date -Format o)" | Out-File -Append (Join-Path $logs 'status.txt')
