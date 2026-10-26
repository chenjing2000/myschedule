# Calendar interactions implementation plan

> **For agentic workers:** Use superpowers:executing-plans task by task, with test-driven-development and a final independent review.

**Goal:** Implement the calendar, single-instance and dialog interactions approved in this conversation.
**Architecture:** Retain WPF code-behind and the existing day circle. Keep one selected date in CalendarView; handle selection and invocation separately. Guard application startup with a session/user-scoped mutex and activation signal.
**Tech Stack:** .NET 8, WPF, existing console checks; no new dependency.
**Spec:** User-approved discussion in this chat, including 44 DIP circles and unchanged font sizes.

## Global constraints
- Preserve data/MySchedule.json beside the executable, its bytes, JSON schema, recurrence and reminders.
- Date single click selects; double left click opens the chosen day's dialog.
- Today retains its filled circle; another selected date uses an accent outline and text; hover is circular.
- Retain selection across month rebuilds; Today also selects today.
- Circle diameter 44 DIP; minimum main window remains 380 x 440 DIP.
- One instance per interactive user/session; restore and activate the existing window or its active modal.
- New reminders default to 0; edits retain stored values; clicking the reminder field places the caret at the end.
- Reduce textbox inset; keep dropdown widths equal except year; fit the 380 DIP editor without clipping traditional lunar text.
- Double click an event row edits that row; empty list space creates a blank event for the viewed date; scrollbars do neither.

## Review focus
- Today + selection + hover + reminder flashing must remain readable without a square hover background.
- Enlarged circles must not overlap in the minimum-height grid, including during reminders.
- Repeated activation during startup, minimized state and modal editing must not spawn another window or overwrite data.
- Narrow date row must fit lunar leap-month names, all unit labels and dropdown arrows.
- Empty-space double click must ignore an already-selected item; row whitespace must still edit its own item.

## Task 1: Calendar and dialog interactions
Files: Controls/CalendarView.xaml(.cs), Views/MainWindow.xaml(.cs), Views/ScheduleEditWindow.xaml(.cs), Views/DayEventsWindow.xaml.cs, Models/Schedule.cs, Resources/Theme.xaml, Tests/InteractionChecks.cs, Tests/Program.cs, Tests/EventChecks.cs.
- [x] Add failing routed-event checks for click/double click, circle and selected appearance, defaults/caret, equal widths/text bounds, and row vs blank double click.
- [x] Run checks and record expected failures before production changes.
- [x] Implement the approved behavior with existing controls and event handlers.
- [x] Run the whole console suite; inspect rendered calendar/editor images.

## Task 2: Single-instance startup
Files: App.xaml(.cs), Tests/InstanceChecks.ps1.
- [x] Add a process-level regression check using a disposable application copy, verifying second launch exits and restores the same first instance.
- [x] Run against the original startup and observe a second instance.
- [x] Implement mutex + event activation, explicit main-window creation, foreground activation and shutdown cleanup.
- [x] Re-run process checks, including restarting after first-instance shutdown.

## Task 3: Review and delivery
Files: README.md, CHANGELOG.md and this plan.
- [x] Update current interaction documentation; retain historical notes.
- [x] Independent review of the changed sources and tests; resolve correctness findings.
- [x] Build, whole-suite and process checks; publish updated runtime and verify user-data hash.

## Execution ledger
- Ruling: Work in the user's current directory. Git has no commits (all sources untracked), so a worktree/commit baseline cannot be created without committing unrelated user files. Do not initialize, commit or push the user's project.
- Ruling: Execute the approved specification now; no additional design approval is required by the user's instruction.
- Pre-flight: Task 1 changes selection/dialog behavior; Task 2 activates the deepest visible owned dialog using existing ownership. Neither changes persistence.

- Task 1 RED: original code failed Calendar (single click opens), Editor (15-minute default), DayList (blank opens selected item).
- Task 1 GREEN: new Calendar/Editor/DayList routed-event groups plus original 85 event, modal-layout and 51 calendar checks passed.
- Task 2 RED: real second launch remained running in an isolated application copy.
- Review regression RED: hovered date used pale hover background during dark reminder flash. Added a dark hover state; longest lunar dropdown text passed at 380 DIP.
- Task 2 GREEN: real process checks passed restore/foreground identity, two nested modals, restart and three immediate launch pairs.
- Independent review: one required dark-hover contrast issue corrected and regression verified; follow-up review found no remaining required issue.
- Final verification: Release build 0 warnings/errors; 3 interaction groups, 85 event checks, modal layout at 380/400/520 and 52 calendar checks (all 2400 months) passed. Published runtime also passed isolated process tests.
- Data preservation: published data matched the immediately pre-publish SHA256 24274B9FE8CFC8B8DC458D397223FB0A3596964922A35F7D4C47503AD15EA205. The earlier-session hash is stale and was not used to restore or overwrite the user file.
- Boundary: automated routed-event, native modal-layout and real process tests plus rendered-image inspection; no cross-monitor DPI or physical mouse manual QA.
