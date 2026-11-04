#Requires -Version 5.1
<#
.SYNOPSIS
    Packs the exact NuGet packages EducationHub needs (plus the dotnet-ef tool) so Claude's
    cloud workspace, which cannot reach nuget.org, can build and test the backend.

.DESCRIPTION
    1. Restores EducationHub.sln into tools\claude\offline\packages (a private copy).
    2. Installs dotnet-ef (same version as EF Core) into tools\claude\offline\tools.
    3. Zips both and splits the zip into <= 300 MB parts in tools\claude\offline-parts.
    4. Restores the solution again normally so your own builds keep using your usual cache.
    Nothing outside the project folder is changed. Safe to run again (it starts clean).
#>
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Set-Location -LiteralPath $root

$work  = Join-Path $PSScriptRoot 'offline'
$parts = Join-Path $PSScriptRoot 'offline-parts'
$zip   = Join-Path $PSScriptRoot 'offline.zip'

function Step($m) { Write-Host "[prepare-offline] $m" -ForegroundColor Cyan }

foreach ($p in @($work, $parts, $zip)) {
    if (Test-Path $p) { Remove-Item $p -Recurse -Force }
}
New-Item -ItemType Directory -Path $work, $parts | Out-Null

Step ".NET SDK: $(dotnet --version)"

Step 'Restoring EducationHub.sln into a private packages folder...'
dotnet restore EducationHub.sln --packages (Join-Path $work 'packages')
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed' }

$efVersion = ([xml](Get-Content (Join-Path $root 'Directory.Packages.props'))).Project.ItemGroup.PackageVersion |
    Where-Object { $_.Include -eq 'Microsoft.EntityFrameworkCore.Design' } |
    Select-Object -ExpandProperty Version -First 1

Step "Installing dotnet-ef $efVersion..."
dotnet tool install dotnet-ef --version $efVersion --tool-path (Join-Path $work 'tools')
if ($LASTEXITCODE -ne 0) { throw 'dotnet tool install dotnet-ef failed' }

Step 'Zipping...'
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($work, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)

Step 'Splitting into 300 MB parts...'
$chunk = 300MB
$buffer = New-Object byte[] (4MB)
$in = [IO.File]::OpenRead($zip)
try {
    $index = 0
    while ($in.Position -lt $in.Length) {
        $index++
        $name = Join-Path $parts ('offline.zip.{0:D3}' -f $index)
        $out = [IO.File]::Create($name)
        try {
            $written = 0
            while ($written -lt $chunk -and ($read = $in.Read($buffer, 0, [Math]::Min($buffer.Length, $chunk - $written))) -gt 0) {
                $out.Write($buffer, 0, $read)
                $written += $read
            }
        }
        finally { $out.Dispose() }
    }
}
finally { $in.Dispose() }

Remove-Item $zip -Force
Remove-Item $work -Recurse -Force

Step 'Restoring normally again (puts your obj\ folders back on your usual NuGet cache)...'
dotnet restore EducationHub.sln | Out-Null

$files = Get-ChildItem $parts
$total = ($files | Measure-Object Length -Sum).Sum / 1MB
Write-Host ''
Write-Host ("[prepare-offline] DONE: {0} part(s), {1:N0} MB in tools\claude\offline-parts" -f $files.Count, $total) -ForegroundColor Green
Write-Host '[prepare-offline] Tell Claude it finished.' -ForegroundColor Green
