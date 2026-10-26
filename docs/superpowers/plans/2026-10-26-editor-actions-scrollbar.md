# Unified editor actions and slim dropdown scrollbar

Approved chat specification: remove editor Delete and its result branch; keep deletion in day list. Keep existing Cancel/Save positions and shared new/edit window. Dropdowns use a 6 DIP vertical scrollbar without end-arrow buttons, pale track/thumb with hover/drag feedback, shown only when needed. Preserve all selection, scrolling, data and single-instance behavior.

## Steps
- [x] RED: assert editor has only Cancel/Save in new/edit modes; assert popup scrollbar is at most 6 DIP, absent for five-item recurrence, and still supports bottom scrolling and thumb dragging.
- [x] Remove editor delete UI/handler; simplify ShowEditor to ScheduleItem? and update both callers.
- [x] Add a popup-scoped slim vertical ScrollBar template retaining Track/Thumb/page navigation and native ScrollViewer routing.
- [x] Full suite, render inspection, independent review, document synchronization, publish and preserve data hashes.
- [x] Inspect complete initial Git tree, exclude data/generated output, commit and push to the configured empty GitHub repository; verify remote HEAD and clean status.

## Ledger
- Git is initialized on main with no commits; origin git@github.com:chenjing2000/myschedule.git has no refs (ls-remote succeeded). Initial project commit and push are explicitly authorized by the user.
- Use existing directory: an isolated Git worktree cannot be based on an unborn branch. No unrelated refactor or dependency.
- RED: native popup width check failed on original scrollbar; shared editor action check found the extra Delete action. Removed editor-only deletion protocol and added popup-scoped 6 DIP Track/Thumb style.
- Verified: new/edit actions, save/add/update identity preservation and day-list deletion, 6 DIP width, no arrow buttons, thumb drag, track paging, bottom gap 0; full 85 event/52 calendar/3 dropdown/3 interaction groups and native modal layouts passed.
- Release build: 0 warnings/errors. Published application and isolated single-instance tests passed; user data and backup hashes unchanged. Independent read-only review found no required issue.
- Publication: first project snapshot on main; origin confirmed empty, exclude all user data and generated artifacts, no force push.
