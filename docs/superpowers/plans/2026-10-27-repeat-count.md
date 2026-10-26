# Finite recurrence and compact editor

Approved specification: total occurrences include the start date; invalid dates do not consume occurrences. Defaults: yearly100, daily/weekly/monthly2, none1. Count1..999; field disabled for none, enabled otherwise; switching period sets its default, opening an edit preserves saved count. Store repeatCount in strict schemaVersion2. Convert current v1 data with an immutable original backup; preserve identity and fields, clear reminder record only if no longer an occurrence. Do not silently auto-migrate other v1 files.

Layout: action buttons60; calendar-type48; year72; small combos/reminder/repeat inputs42 DIP. One-character date/time unit slots22; period/repeat labels36. Reduce reminder-to-footer blank height by about2/3 in shared new/edit editor. Integer DIP widths, no clipping at380 outer width; error messages stay visible.

## Steps
- [x] RED: finite daily/weekly/solar/lunar bounds, skipped dates, JSONv2 validation and editor defaults/layout.
- [x] Implement model/count calculation in the shared occurrence predicate, strict JSONv2, reminder-record retention when valid.
- [x] Implement compact layout, numeric repeat input and period defaults. Update existing tests/examples for v2.
- [x] Add explicit one-time data conversion helper to test CLI, verify conversion against real parser; backup original then atomically replace current data after tests pass.
- [x] Full tests/render/native modal checks, independent review, update docs, publish runtime and preserve backup.

## Ledger
- User explicitly approved replacing existing unlimited schedules with period defaults, counted from their original start date; some may already be expired. No event is automatically deleted.
- Work in current checkout with a clean Git baseline. No unrelated refactor or dependency.
- RED/GREEN: core missing model field reproduced before implementation; editor missing count field reproduced after core passed; final finite and existing suites pass.
- Review fix RED: mixed-case v1 Yearly incorrectly received2; converter now parses enum case-insensitively and reuses model defaults, with Yearly/NONE regression.
- Test correction: day-list save test now targets event ID rather than assuming its09:00 item sorts before a new event after midnight.
- Delivery: Release build 0 warnings/errors; finite tests and all existing85event/52calendar/3dropdown/3interaction/native-modal checks passed. Published isolated process tests also passed.
- Explicit data upgrade:3existing events retained, defaults applied,0reminder records cleared. Independent original backup MySchedule.json.schema-v1-016cafc6.bak matches the original SHA256; previous rolling.bak unchanged. Runtime publish did not alter upgraded data.
- Review confirmation: converter case fix and active schema2 docs/skill consistent, no remaining required finding.
