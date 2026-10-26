# Editor dropdown appearance implementation plan

> Execute with superpowers:executing-plans, test-driven-development and verification-before-completion. The user approved this design in chat.

**Goal:** Center text, remove arrows and reserved width, round editor dropdowns, integrate background with the window and provide subtle hover/focus feedback. Year width 72 DIP, other widths 64 DIP. Center reminder digits.
**Architecture:** One shared non-editable ComboBox/ComboBoxItem template in Theme.xaml, retaining WPF ComboBox behavior, selection, keyboard navigation and its Popup/ScrollViewer. No new runtime classes or dependency.
**Spec:** Approved chat; preserve 14 DIP font, main/editor dimensions, lunar labels and all schedule data/contracts.

## Tasks
- [x] Add native-popup checks for real option counts, scroll-bottom geometry and blank entries; add red checks for centered full-width text, 72 DIP year and centered reminder.
- [x] Reproduce and identify blank-end geometry before changing production; implement shared templates and editor widths/alignment.
- [x] Check open/close, bottom selection and keyboard navigation, narrow windows and long lunar years; inspect rendered images.
- [x] Update README/changelog, independent read-only review, full build and regression suite.
- [x] Publish runtime and verify current user-data/backup hashes; remove generated output.

## Review focus
- WPF popup keyboard/mouse capture must work after removing the default arrow button.
- Bottom of a long dropdown must end at the last real item with no blank row or horizontal scroll gutter.
- Four-character lunar years must fit 72 DIP at 14 DIP font and remain visually centered.
- Popup placement above/below must not clip items; at most eight rows visible and scrollable when needed.
- Main field is fully clickable and focus/selection/disabled states remain recognizable.

## Ledger
- Work in place: current repository has no commits and all sources are untracked; do not create a baseline commit or alter Git configuration.
- User authorized implementation; no additional design gate is required. Current running user instance will not be killed or closed without preserving unsaved work.
- RED: default year popup has 200 nonempty options but a 28 DIP bottom gap (logical viewport 7 items); 72 DIP year appearance check fails on the original implementation.
- Cause: default logical item scrolling rounds down the partially fitting eighth row, leaving almost one row at the bottom. Use pixel scrolling and put the 240 DIP viewport limit inside the popup border.
- GREEN: all six popup bottom gaps are 0 DIP; appearance and input behavior groups pass. Existing 85 event, 52 calendar, 3 interaction groups and real modal checks at 380/400/520 DIP also pass.
- Independent read-only review found no required issue; added toggle automation, hit testing across both field edges, F4/End/Enter/Escape and hover checks. Physical mouse click/outside-click manual QA and cross-monitor DPI remain outside automated evidence.
- Delivery: Release build 0 warnings/errors; published executable and two existing data/backup hashes preserved. Published isolated process tests passed original-window activation, deepest two-modal activation and three immediate-launch pairs. Removed generated build/test outputs.
