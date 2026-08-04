# Wave P4 Completion — Tasks 060 + 061 (2026-08-02)

**Result: ✅ both complete.** 2 parallel Sonnet agents (FULL rigor; 061 auto-escalated from STANDARD); main-session consolidation.

## 060 (preview/browse re-base + retirement)
- `RichFilePreviewDialog`: single-doc → `PreviewModal`, record-set → `BrowseModal` (SprkModal `nav` + the `onBeforeNavigate` guard seam — the shipped task-007 single-header architecture; the POML's "via RecordNavigationModalShell" wording predates it, delta documented). All hand-rolled chrome deleted (382→~245 lines); **public props byte-identical** — ~8 consumers across LegalWorkspace/PCFs/SpaarkeAi/Compose/DailyBriefing need zero changes.
- `RichFilePreview` (renderer): `showMetadataPane` added (default true) so the preset's meta column doesn't double with the renderer's own pane; dead P1-interim props removed (grep-proven zero callers).
- `@deprecated FilePreviewDialog.tsx` **DELETED**; sole consumer `FindSimilarResultsStep` migrated (escalation assessed, not fired: 1:1 service-callback mapping; `onEmailDocument` no-op per the shipped EmailReadingAttachments precedent). Grep proof: zero dangling imports in scope.

## 061 (FindSimilarDialog ×3 → xl)
Per-copy, honest to what each copy actually is (reconciling the P1 discovery):
- Viewer copy (self-enveloped): literal `SprkModal size="xl"`; open-in-new-tab moved to the `headerActions` slot; P1 interim wiring superseded.
- Viewer's `embedded` sub-path (dead code — zero callers): size-aligned via `getSurfaceStyle('xl')`; SprkModal's header is unconditional so a chromeless wrap is impossible (documented deviation).
- Wizard copy: xl numbers via its existing `WizardShell` props (wrapping would double-chrome; WizardShell's own re-base is task 080).
- LegalWorkspace adapter: pure pass-through, zero edits.
- Consolidation DEFERRED per POML → **Issue #714 / DEF-003** (one name, two UX patterns, no shared base; dead `embedded` prop).

## Main-session consolidation
- `headerActions?: ReactNode` passthrough added to `PreviewModal` + `BrowseModal` (060's gap report — SprkModal already owned the slot; presets now forward it).

## Verification (main session)
- UI.Components dist rebuild + full suite: pre-existing baseline only (figures in commit message); scoped preset/FilePreview/FindSimilar suites green per agents.
- `SemanticSearchControl` `npm run build:prod`: the load-bearing dual-React proof — this PCF deep-imports BOTH rewritten dialogs (FindSimilarDialog viewer + RichFilePreviewDialog).
- ADR-021 diff gate clean; LW scoped tsc clean for the untouched adapter.

Recommend a visual pass before merge (preview single + browse N-of-M + FindSimilar embedded) — sub-agents had no browser tooling.

Per-task detail: `task-060-completion.md` · `task-061-completion.md`.
