$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$staging = Join-Path $PSScriptRoot 'obj/portable-publish'
$destination = Join-Path $PSScriptRoot 'dist/OneSchedule'
if (Get-Process OneSchedule -ErrorAction SilentlyContinue) { throw 'Please save schedules and close OneSchedule before publishing.' }
& dotnet publish OneSchedule.Wpf.csproj -c Release -r win-x64 --self-contained false -o $staging -p:UseAppHost=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$files = @(Get-ChildItem -LiteralPath $staging -File)
if ($files.Count -ne 1 -or $files[0].Name -ne 'OneSchedule.exe') { throw 'Single-file publish produced unexpected files.' }
New-Item -ItemType Directory -Path $destination -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $staging 'OneSchedule.exe') -Destination (Join-Path $destination 'OneSchedule.exe') -Force
# Only obsolete application binaries are removed. User schedules/settings/backups stay in place.
foreach ($relative in @('dist/OneSchedule/OneSchedule.dll','dist/OneSchedule/OneSchedule.deps.json','dist/OneSchedule/OneSchedule.runtimeconfig.json',
 'bin/Release/net8.0-windows/win-x64/OneSchedule.exe','obj/Release/net8.0-windows/win-x64/apphost.exe',
 'obj/portable-publish/OneSchedule.exe')) {
    $target = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot $relative))
    if (-not $target.StartsWith($PSScriptRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Build artifact path escaped project.' }
    if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Force }
}
Write-Output 'Published single EXE (requires .NET 8 Windows Desktop Runtime x64): dist/OneSchedule/OneSchedule.exe'
