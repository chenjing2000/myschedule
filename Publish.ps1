$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
& dotnet publish OneSchedule.Wpf.csproj -c Release -o (Join-Path $PSScriptRoot 'dist/OneSchedule') -p:UseAppHost=true -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Publishing needs temporary apphosts; keep only the published executable afterward.
foreach ($relative in @('bin/Release/net8.0-windows/OneSchedule.exe', 'obj/Release/net8.0-windows/apphost.exe')) {
    $target = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot $relative))
    if (-not $target.StartsWith($PSScriptRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Build artifact path escaped the project directory'
    }
    if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Force }
}
Write-Output 'Published: dist/OneSchedule/OneSchedule.exe'
