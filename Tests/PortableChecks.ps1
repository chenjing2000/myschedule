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
    $info=[Diagnostics.ProcessStartInfo]::new($exe,'--test-host')
    $info.UseShellExecute=$false;$info.RedirectStandardError=$true;$info.RedirectStandardOutput=$true
    $info.Environment['DOTNET_ROLL_FORWARD']='Disable'
    $info.Environment['DOTNET_DISABLE_GUI_ERRORS']='1'
    $process=[Diagnostics.Process]::Start($info)
    if (-not $process.WaitForExit(8000)) { throw 'Expected incompatible runtime selection to fail before startup' }
    $failure=$process.StandardError.ReadToEnd()+$process.StandardOutput.ReadToEnd()
    if ($process.ExitCode -eq 0 -or $failure -notmatch 'You must install or update .NET|Framework:|Download') { throw ('Native runtime error absent: '+$failure) }
    Write-Output 'PASS: native .NET host reports missing compatible runtime; GUI suppressed only in test'
}
finally {
    if ($process) { if (-not $process.HasExited) { $process.Kill();$process.WaitForExit() };$process.Dispose() }
    $resolved=[IO.Path]::GetFullPath($fixture)
    if (-not $resolved.StartsWith([IO.Path]::GetFullPath($parent)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe fixture cleanup path' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
