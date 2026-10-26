# Numeric visibility and executable output

Approved scope: keep42DIP/14DIP numeric fields, reduce their horizontal inset and avoid duplicate internal spacing; verify100/999 and caret positions. Long text fields retain existing look. Normal app/test builds emit DLLs without apphost executables; final executable only in dist/OneSchedule. build-release publishes there; run.bat launches it without rebuilding. Single-instance tests may create necessary temporary copies but clean them. Preserve all user data and existing behavior.

- [x] Reproduce three-digit clipping using real text/caret rectangles and horizontal offset before fixing.
- [x] Add numeric-only small inset style; verify repeat/reminder text in focused/unfocused states.
- [x] Disable default apphost in app/test projects, update build/run scripts and process probes to use dotnet DLL hosts where needed.
- [x] Full build/test, published process checks, independent review and docs. Verify normal builds have no EXE outputs, and only the final runtime EXE remains after cleanup.

Ledger: current clean checkout; no user app running. Do not modify the schedule or backup files.
- RED: repeat100 in42DIP field had only20DIP viewport; final caret outside viewport. Padding is applied to both content-host margin and TextBox internal layout. Numeric-only Padding1 restores36DIP visible width.
- Build decision: existing batch release entry delegates publishing to Publish.ps1 so path-checked native PowerShell cleanup removes only the two SDK temporary apphost EXEs after successful publication. Default builds and tests never generate apphosts.
- GREEN:100/999/99 fit36DIP viewport with zero horizontal scroll in both focus states and caret endpoints; focus state now explicitly asserted following independent review recommendation.
- Actual batch publication passed, all user-data hashes unchanged. Debug/Release app/test outputs had noEXE; final release retained onlydist EXE. NormalDLL and publishedEXE process probes passed; run.bat tested in spaced isolated path for success/missing-runtime/no-rebuild.
- Independent read-only review found no required correction.
