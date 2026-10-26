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
        $fileSource = if ($RuntimeDirectory -and $name.StartsWith('OneSchedule.')) { $RuntimeDirectory } else { $source }
        Copy-Item -LiteralPath (Join-Path $fileSource $name) -Destination $fixture
    }
    $applicationHost = 'OneSchedule.dll'
    if ($RuntimeDirectory) {
        Copy-Item -LiteralPath (Join-Path $RuntimeDirectory 'OneSchedule.exe') -Destination $fixture
        $applicationHost = 'OneSchedule.exe'
    }
    $first = Launch $applicationHost
    $handle = WindowOf $first
    [void][InstanceProbeNative]::ShowWindow($handle, 6)
    Until { [InstanceProbeNative]::IsIconic($handle) } 'Could not minimize first instance'
    $second = Launch $applicationHost
    if (-not $second.WaitForExit(5000)) { throw 'Second launch created another running instance' }
    if ($second.ExitCode -ne 0) { throw 'Second launch did not exit successfully' }
    Until { -not [InstanceProbeNative]::IsIconic($handle) } 'Existing instance was not restored'
    $first.Refresh()
    if ($first.HasExited -or $first.MainWindowHandle -ne $handle) { throw 'First window identity changed' }
    Until { [uint32]$foregroundProcess = 0; [void][InstanceProbeNative]::GetWindowThreadProcessId([InstanceProbeNative]::GetForegroundWindow(), [ref]$foregroundProcess); $foregroundProcess -eq $first.Id } 'Existing instance was not activated in foreground'
    Write-Output 'PASS: second launch exits and restores/activates the original process and window'
    CloseProbe $first
    $modalHost = Launch 'CalendarChecks.dll' '--instance-modal-host'
    $modalMain = WindowOf $modalHost
    Until { -not [InstanceProbeNative]::IsWindowEnabled($modalMain) } 'Modal host did not open its owned dialog'
    Until { $caption = [Text.StringBuilder]::new(100); $popup = [InstanceProbeNative]::GetLastActivePopup($modalMain); [void][InstanceProbeNative]::GetWindowText($popup, $caption, 100); $caption.ToString() -eq 'Instance probe editor' } 'Two nested modal dialogs did not open'
    $popup = [InstanceProbeNative]::GetLastActivePopup($modalMain)
    [void][InstanceProbeNative]::ShowWindow($modalMain, 6)
    $third = Launch $applicationHost
    if (-not $third.WaitForExit(5000) -or $third.ExitCode -ne 0) { throw 'Modal activation created another instance' }
    Until { [InstanceProbeNative]::GetForegroundWindow() -eq $popup } 'Owned modal was not activated'
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
}
finally {
    foreach ($process in $processes) { CloseProbe $process; $process.Dispose() }
    $resolvedFixture = [IO.Path]::GetFullPath($fixture)
    if (-not $resolvedFixture.StartsWith([IO.Path]::GetFullPath($fixtureParent) + [IO.Path]::DirectorySeparatorChar)) { throw 'Fixture cleanup escaped its test directory' }
    Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
}
