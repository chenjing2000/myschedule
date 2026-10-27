param([string]$Configuration = 'Release', [string]$RuntimeDirectory = '')
$ErrorActionPreference = 'Stop'
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class InstanceProbeNative {
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] public static extern bool IsWindowEnabled(IntPtr window);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", EntryPoint="GetWindowLongW")] public static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern IntPtr GetLastActivePopup(IntPtr window);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr window, StringBuilder text, int count);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
"@
$source = Join-Path $PSScriptRoot "bin/$Configuration/net8.0-windows"
$fixtureParent = Join-Path $source 'instance-fixtures'
$fixture = Join-Path $fixtureParent ([guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
$processes = [Collections.Generic.List[Diagnostics.Process]]::new()
function Launch([string]$name, [string]$arguments = '') {
    $arguments += ' --test-host'
    if ([IO.Path]::GetExtension($name) -eq '.dll') {
        $info = [Diagnostics.ProcessStartInfo]::new('dotnet', ('"' + (Join-Path $fixture $name) + '" ' + $arguments))
    } else {
        $info = [Diagnostics.ProcessStartInfo]::new((Join-Path $fixture $name), $arguments)
    }
    $info.CreateNoWindow = $true
    $info.WorkingDirectory = $fixture
    $info.UseShellExecute = $false
    $info.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $process = [Diagnostics.Process]::Start($info)
    $processes.Add($process)
    return $process
}
function Until([scriptblock]$condition, [string]$failure) {
    $deadline = [DateTime]::UtcNow.AddSeconds(8)
    do { if (& $condition) { return }; Start-Sleep -Milliseconds 50 } while ([DateTime]::UtcNow -lt $deadline)
    throw $failure
}
function WindowOf($process) {
    Until { $process.Refresh(); -not $process.HasExited -and $process.MainWindowHandle -ne [IntPtr]::Zero } 'First instance did not create its main window'
    return $process.MainWindowHandle
}
function CloseProbe($process) {
    if (-not $process.HasExited) { [void]$process.CloseMainWindow(); if (-not $process.WaitForExit(3000)) { $process.Kill(); $process.WaitForExit() } }
}
try {
    foreach ($name in @('OneSchedule.dll','OneSchedule.deps.json','OneSchedule.runtimeconfig.json',
                         'CalendarChecks.dll','CalendarChecks.deps.json','CalendarChecks.runtimeconfig.json')) {
        Copy-Item -LiteralPath (Join-Path $source $name) -Destination $fixture
    }
    $applicationHost = 'OneSchedule.dll'
    if ($RuntimeDirectory) {
        Copy-Item -LiteralPath (Join-Path $RuntimeDirectory 'OneSchedule.exe') -Destination $fixture
        $applicationHost = 'OneSchedule.exe'
    }
    $first = Launch $applicationHost
    $handle = WindowOf $first
    [void][InstanceProbeNative]::ShowWindow($handle, 6)
    Until { -not [InstanceProbeNative]::IsWindowVisible($handle) -and ([InstanceProbeNative]::GetWindowLong($handle,-20) -band 0x40000) -eq 0 } 'Minimize did not hide window and taskbar entry'
    $second = Launch $applicationHost
    if (-not $second.WaitForExit(5000)) { throw 'Second launch created another running instance' }
    if ($second.ExitCode -ne 0) { throw 'Second launch did not exit successfully' }
    try { Until { -not [InstanceProbeNative]::IsIconic($handle) } 'Existing instance was not restored' }
    catch {
        $first.Refresh()
        Write-Output "DIAG old=$handle new=$($first.MainWindowHandle) foreground=$([InstanceProbeNative]::GetForegroundWindow())"
        foreach ($probeHandle in @($handle,$first.MainWindowHandle,[InstanceProbeNative]::GetForegroundWindow())) {
            $probeCaption=[Text.StringBuilder]::new(100)
            [void][InstanceProbeNative]::GetWindowText($probeHandle,$probeCaption,100)
            Write-Output ("DIAG handle={0} caption={1} visible={2} enabled={3} iconic={4}" -f $probeHandle,$probeCaption,[InstanceProbeNative]::IsWindowVisible($probeHandle),[InstanceProbeNative]::IsWindowEnabled($probeHandle),[InstanceProbeNative]::IsIconic($probeHandle))
        }
        throw
    }
    $first.Refresh()
    if ($first.HasExited -or $first.MainWindowHandle -ne $handle) { throw 'First window identity changed' }
    Until { [uint32]$foregroundProcess = 0; [void][InstanceProbeNative]::GetWindowThreadProcessId([InstanceProbeNative]::GetForegroundWindow(), [ref]$foregroundProcess); $foregroundProcess -eq $first.Id } 'Existing instance was not activated in foreground'
    Write-Output 'PASS: second launch exits and restores/activates the original process and window'
    [void][InstanceProbeNative]::SendMessage($handle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
    Until { -not [InstanceProbeNative]::IsWindowVisible($handle) } 'Close did not hide to tray'
    $first.Refresh()
    if ($first.HasExited) { throw 'Close unexpectedly exited application' }
    $reopen = Launch $applicationHost
    if (-not $reopen.WaitForExit(5000)) { throw 'Reopen after close created another instance' }
    Until { [InstanceProbeNative]::IsWindowVisible($handle) } 'Second launch did not restore hidden window'
    if (-not (Test-Path -LiteralPath (Join-Path $fixture 'data/MySchedule.json'))) { throw 'First run did not initialize schedule file' }
    if (-not (Test-Path -LiteralPath (Join-Path $fixture 'OneSchedule.settings.json'))) { throw 'First run did not initialize settings' }
    Write-Output 'PASS: close hides quietly; second launch restores; first-run files initialize'

    CloseProbe $first
    $modalHost = Launch 'CalendarChecks.dll' '--instance-modal-host'
    $modalMain = WindowOf $modalHost
    Until { -not [InstanceProbeNative]::IsWindowEnabled($modalMain) } 'Modal host did not open its owned dialog'
    Until { $caption = [Text.StringBuilder]::new(100); $popup = [InstanceProbeNative]::GetLastActivePopup($modalMain); [void][InstanceProbeNative]::GetWindowText($popup, $caption, 100); $caption.ToString() -eq 'Instance probe editor' } 'Two nested modal dialogs did not open'
    $popup = [InstanceProbeNative]::GetLastActivePopup($modalMain)
    [void][InstanceProbeNative]::ShowWindow($modalMain, 6)
    $third = Launch $applicationHost
    if (-not $third.WaitForExit(5000) -or $third.ExitCode -ne 0) { throw 'Modal activation created another instance' }
    try { Until { [InstanceProbeNative]::GetForegroundWindow() -eq $popup } 'Owned modal was not activated' }
    catch {
        foreach ($probeHandle in @($modalMain, $popup, [InstanceProbeNative]::GetLastActivePopup($modalMain), [InstanceProbeNative]::GetForegroundWindow())) {
            $probeCaption = [Text.StringBuilder]::new(100)
            [void][InstanceProbeNative]::GetWindowText($probeHandle, $probeCaption, 100)
            Write-Output ("DIAG handle={0} caption={1} visible={2} enabled={3} iconic={4}" -f $probeHandle, $probeCaption, [InstanceProbeNative]::IsWindowVisible($probeHandle), [InstanceProbeNative]::IsWindowEnabled($probeHandle), [InstanceProbeNative]::IsIconic($probeHandle))
        }
        throw
    }
    Write-Output 'PASS: restart after shutdown succeeds; repeat launch activates the deepest of two owned modal dialogs'
    CloseProbe $modalHost
    for ($attempt = 0; $attempt -lt 3; $attempt++) {
        $left = Launch $applicationHost
        $right = Launch $applicationHost
        Until { $left.Refresh(); $right.Refresh(); $left.HasExited -or $right.HasExited } 'Concurrent launches both stayed running'
        $survivor = if ($left.HasExited) { $right } else { $left }
        $loser = if ($left.HasExited) { $left } else { $right }
        if (-not $loser.WaitForExit(5000) -or $loser.ExitCode -ne 0 -or $survivor.HasExited) { throw 'Concurrent launch did not leave one successful instance' }
        $null = WindowOf $survivor
        CloseProbe $survivor
    }
    Write-Output 'PASS: three immediate launch pairs each leave exactly one working instance'
    $startup = Launch $applicationHost '--startup'
    Until { Test-Path -LiteralPath (Join-Path $fixture 'OneSchedule.settings.json') } 'Startup did not initialize settings'
    Start-Sleep -Milliseconds 700
    $startup.Refresh()
    if ($startup.HasExited -or $startup.MainWindowHandle -ne [IntPtr]::Zero) { throw 'Startup launch should remain hidden without a taskbar window' }
    $wake = Launch $applicationHost
    if (-not $wake.WaitForExit(5000)) { throw 'Startup wake created another instance' }
    $null = WindowOf $startup
    Write-Output 'PASS: --startup remains quiet in tray and manual relaunch restores it'

}
finally {
    foreach ($process in $processes) { CloseProbe $process; $process.Dispose() }
    $resolvedFixture = [IO.Path]::GetFullPath($fixture)
    if (-not $resolvedFixture.StartsWith([IO.Path]::GetFullPath($fixtureParent) + [IO.Path]::DirectorySeparatorChar)) { throw 'Fixture cleanup escaped its test directory' }
    Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
}
