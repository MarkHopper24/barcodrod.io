# General inventory UX implementation plan

> Execute inline in this existing checkout, preserving the earlier uncommitted inventory work.

**Goal:** Generalize item tracking and fix UX defects found during the live review.

**Architecture:** Preserve the Core JSON/Excel schema. Improve native dialogs, page presentation, selection state, and command availability; keep optional business data available.

**Tech stack:** WinUI 3, C#, CommunityToolkit.Mvvm, ClosedXML, ZXing, xUnit, winapp UI Automation.

**Spec:** ../specs/2026-11-09-general-inventory-design.md

## Constraints and review focus

- No data migration, package upgrade, release, or replacement of the installed Store app.
- Existing mapped headers, IDs, action values, and custom fields must survive round trips.
- Exact counts must not accidentally reset totals to one; reserved quantities stay protected.
- Refreshes and filtering must preserve eligible batch selections without operating on hidden records.
- Optional sections must preserve hidden values and expose validation errors.
- Empty/error/narrow layouts must keep primary actions reachable.

## Tasks

- [x] Add and run regression tests for minimum warnings and generated IDs; implement Core helpers.
- [x] Replace the all-fields item form with basic and optional sections; add friendly quantity actions and previews.
- [x] Fix selection refreshes, command states, readable details, scanning, Excel discoverability, recovery, and page reflow.
- [x] Update import hints, guide, and repeatable UI checks without changing workbook field keys.
- [x] Build and run all Core tests; exercise isolated end-to-end desktop flows, Excel conflicts/readback, labels/PDF, and narrow/wide layouts.
- [x] Inspect final screenshots and record findings, fixes, and current verification limits.

## Evidence and rulings

- Core: 32 tests passed, including red/green minimum-warning, generated-ID, and decimal-editor regressions.
- Desktop: 16 inventory checks and 5 integration checks passed using native UI Automation. Actual keyboard input was interrupted by desktop locking; the scan submission handler was exercised directly through its native automation peer instead. No physical scanner/printer or installed Excel was available.
- Review: a fresh read-only reviewer found decimal rounding of hidden fields, a double-tap selection bypass, and missing narrow-window activity. All three were fixed and rechecked.
- Runtime review additionally fixed native button Click handling, picker ownership, cropped Create previews, and expanded tools hiding the compact item list.
- Preserved the earlier dirty checkout and workbook/JSON schema. Used a separate development package identity and isolated fixtures on the migrated PC.
- Test traces, before/after screenshots, sample workbooks, PDFs, and final preview data live under `artifacts/ux-review-2026-11-09/`.
