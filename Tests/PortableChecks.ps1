param([string]$RuntimeDirectory='dist/OneSchedule')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$parent=Join-Path $PSScriptRoot 'bin/portable fixtures'
$fixture=Join-Path $parent ([guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
$process=$null
try {
    $exe=Join-Path $fixture 'OneSchedule.exe'
    Copy-Item -LiteralPath (Join-Path $root "$RuntimeDirectory/OneSchedule.exe") -Destination $exe
    if (@(Get-ChildItem -LiteralPath $fixture).Count -ne 1) { throw 'Fixture must start with EXE only' }
    $info=[Diagnostics.ProcessStartInfo]::new($exe,'--test-host')
    $info.UseShellExecute=$false;$info.WorkingDirectory=$fixture
    $process=[Diagnostics.Process]::Start($info)
    $deadline=[DateTime]::UtcNow.AddSeconds(12)
    $document=$null;$settings=$null;$readError=''
    do {
        $process.Refresh()
        if ($process.HasExited) { throw 'Single EXE exited unexpectedly' }
        # The native window handle exists before Loaded finishes writing first-run JSON.
        try {
            $document=Get-Content -LiteralPath (Join-Path $fixture 'data/MySchedule.json') -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
            $settings=Get-Content -LiteralPath (Join-Path $fixture 'OneSchedule.settings.json') -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
        } catch { $readError=$_.Exception.Message }
        if ($document.schemaVersion -eq 2 -and @($document.events).Count -eq 0 -and
            $settings.startWithWindows -eq $true -and $process.MainWindowHandle -ne [IntPtr]::Zero) { break }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    if ($process.MainWindowHandle -eq [IntPtr]::Zero -or $document.schemaVersion -ne 2 -or
        @($document.events).Count -ne 0 -or $settings.startWithWindows -ne $true) { throw ('EXE-only first-run initialization failed: '+$readError) }
    if (Get-ChildItem -LiteralPath $fixture -Filter *.dll) { throw 'Single EXE emitted adjacent DLLs' }
    $process.Kill();$process.WaitForExit();$process.Dispose();$process=$null
    Write-Output 'PASS: EXE alone runs from path with spaces and initializes adjacent data/settings'
    # Isolate runtime selection so this also works when 10.0.0 is installed.
    # Include the newest installed .NET 8 runtimes, when available, to verify that older runtimes do not satisfy the .NET 10 requirement.
    $installedRoot=Split-Path (Get-Command dotnet -CommandType Application | Select-Object -First 1).Source -Parent
    $runtimeRoot=Join-Path $fixture 'runtime without net10'
    $fxr=Get-ChildItem -LiteralPath (Join-Path $installedRoot 'host/fxr') -Directory |
        Where-Object { $_.Name -match '^\d+\.\d+\.\d+$' } | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    if (-not $fxr) { throw 'Cannot locate native runtime host for isolated fixture' }
    New-Item -ItemType Directory -Path (Join-Path $runtimeRoot 'host/fxr') -Force | Out-Null
    Copy-Item -LiteralPath $fxr.FullName -Destination (Join-Path $runtimeRoot 'host/fxr') -Recurse
    foreach ($framework in @('Microsoft.NETCore.App','Microsoft.WindowsDesktop.App')) {
        $shared=Join-Path $installedRoot ('shared/'+$framework)
        $version8=Get-ChildItem -LiteralPath $shared -Directory |
            Where-Object { $_.Name -match '^8\.0\.\d+$' } | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
        if ($version8) {
            $destination=Join-Path $runtimeRoot ('shared/'+$framework)
            New-Item -ItemType Directory -Path $destination -Force | Out-Null
            Copy-Item -LiteralPath $version8.FullName -Destination $destination -Recurse
        }
    }
    $info=[Diagnostics.ProcessStartInfo]::new($exe,'--test-host')
    $info.UseShellExecute=$false;$info.RedirectStandardError=$true;$info.RedirectStandardOutput=$true
    $info.Environment['DOTNET_ROOT_X64']=$runtimeRoot
    $info.Environment['DOTNET_ROOT']=$runtimeRoot
    # Keep patch selection deterministic; the published runtimeconfig policy is checked separately.
    $info.Environment['DOTNET_ROLL_FORWARD']='LatestPatch'
    $info.Environment['DOTNET_DISABLE_GUI_ERRORS']='1'
    $process=[Diagnostics.Process]::Start($info)
    if (-not $process.WaitForExit(8000)) { throw 'Expected incompatible runtime selection to fail before startup' }
    $failure=$process.StandardError.ReadToEnd()+$process.StandardOutput.ReadToEnd()
    if ($process.ExitCode -eq 0 -or $failure -notmatch 'You must install or update .NET|Framework:|Download') { throw ('Native runtime error absent: '+$failure) }
    if ($failure -notmatch "version '10\.0\.") { throw ('Published EXE does not require .NET 10: '+$failure) }
    Write-Output 'PASS: isolated native host reports missing .NET 10; GUI suppressed only in test'
}
finally {
    if ($process) { if (-not $process.HasExited) { $process.Kill();$process.WaitForExit() };$process.Dispose() }
    $resolved=[IO.Path]::GetFullPath($fixture)
    if (-not $resolved.StartsWith([IO.Path]::GetFullPath($parent)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe fixture cleanup path' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
