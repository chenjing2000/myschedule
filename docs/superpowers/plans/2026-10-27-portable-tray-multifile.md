# Portable multi-file schedules and tray implementation plan

> Execute the approved conversation specification; preserve existing schedules and backups. Apply focused tests before implementation and one full review before publication.

**Goal:** Framework-dependent single EXE, deterministic multi-JSON storage, one persisted startup setting, quiet tray lifecycle.
**Architecture:** ScheduleService owns file snapshots and event source mapping; SettingsService owns adjacent settings plus current-user Run registration; App owns tray lifecycle. Existing occurrence and JSON schema remain unchanged.
**Stack:** .NET8 WPF, Windows Forms NotifyIcon only for tray, current-user Windows registry.

## Approved contract
- Only first-level .json files in adjacent data, sorted OrdinalIgnoreCase. Create valid empty MySchedule.json only if no JSON exists; never replace corrupt sources.
- Add into first file; update/delete/reminder state into source file. Atomic per-file save, external edit checks and rollback; report malformed/duplicate-ID sources while displaying valid independent events.
- Adjacent OneSchedule.settings.json default startWithWindows=true, persist user choice; current-user startup command uses quoted executable path plus --startup. No real startup-registration writes from tests.
- Settings gear after Refresh opens one rounded checkbox, immediate apply with errors visible; preserve app width/layout.
- Manual launch shows window, --startup stays in tray. Minimize and X hide quietly, taskbar absent; tray open/exit, double-click and second launch restore deepest modal. Reminders restore hidden window as needed. Dispose icon and instance objects on true exit.
- Published folder has only EXE plus existing user files; no runtime bundled. Missing runtime uses native .NET host dialog. Keep one final executable, preserve current data bytes.

## Tasks
- [x] Storage: new directory-mode checks for initialization, sorting, partial load, conflict, source ownership, backups, external modification; implement source snapshots and item-targeted reminder save. Preserve explicit-file constructor compatibility used by current tools/tests.
- [x] Settings: SettingsService, SettingsWindow, isolated file/registration tests. Default-on first load, remember false, rollback failed toggles, no actual user registry mutations in tests.
- [x] Tray: App/TrayService lifecycle, isolated test seam, single-instance restoration from hidden, no notifications, explicit shutdown only. Process tests use --test-host argument to isolate startup/settings registry effects.
- [x] Integrate gear and settings startup, runtime restoration for reminders; full STA checks and process tests for close/minimize/second launch.
- [x] Publish framework-dependent single EXE, validate EXE-only directory, safe missing-runtime isolated host test, synchronize docs/tests/scripts and ignore settings. Review all changes and publish preserving data.

## Review focus
- Partial failures must never overwrite a damaged file or duplicate reminder state.
- Mixed-file edits must not rewrite unaffected sources; modified/deleted first source requires reload.
- Startup must not re-enable an explicitly disabled setting; failures cannot leave checkbox falsely reporting success.
- Hide/show cannot repeat window initialization/timers or strand owned dialogs.
- Test hosts must not enable the real application's startup registration or overwrite user data/settings.


## Execution evidence and rulings
- All STA checks passed, including settings UI/default/persistence/rollback, multi-file ownership/errors/conflicts, tray lifecycle and existing85event/52calendar checks.
- Published EXE-only path-with-spaces fixture initialized valid empty data/settings; native host reported missing compatible runtime with GUI suppressed only in test.
- Published process checks passed close/minimize hide, repeat activation, nested modal restore, concurrent launch pairs and silent startup.
- Native taskbar visibility changes left HWND iconic after managed restore; IsIconic/ShowWindow restore fixes the reproduced process failure.
- Explicit single-file constructor retained for existing tools/tests; production default service scans the data directory.
- Test-host registry writes are mocked and single-instance identity is isolated by fixture directory; real startup registration was read-only checked against saved user setting.
- Existing user schedule/backup/settings hashes unchanged by final publication. No actual Windows sign-out/reboot was performed.
