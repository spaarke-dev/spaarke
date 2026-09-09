# Current Task

## Quick Recovery

| Field | Value |
|---|---|
| **Task** | none — 017 complete |
| **Task File** | — |
| **Phase** | 0 De-risk and baseline (closing out) |
| **Status** | not-started |
| **Started** | — |
| **Next Action** | Operator picks next. Two independently-startable tasks are now ready: **002** (Spike-1, `document.url` shape for SPE files in Word desktop — deps: none) and **010** (FR-04 consolidate onto one Word adapter via `HostAdapterFactory` — deps: 006, 007, 008, all ✅). 010 gates most of Phase 1 (`parallel-group P1-a`, opus/xhigh, `<steps mode="prescriptive">` per project CLAUDE.md). Phase 0's remaining spikes/typecheck/harness work (001, 003-009, 017) are now all ✅ except 002. |

## Critical Context (task 017, complete 2026-09-09)

Resolved task 009's escalation trigger 2 with the 2026-09-09 operator decision: React 19 stays; RTL aligns
to it. Bumped `@testing-library/react` `^14.2.1` → `^16.1.0` in `src/client/office-addins` (matching
SemanticSearch's pin, the higher of the two sibling precedents: PlaybookBuilder `^16.0.0`, SemanticSearch
`^16.1.0`). Mid-task discovery: RTL 16 demotes `@testing-library/dom` from a bundled dependency to a peer
dependency — added explicitly as `^10.4.1` (the exact pin both siblings already carry); without it 17/21
suites failed to load entirely (a transient trigger-3 condition, self-resolved within the task).

**Final suite state is IDENTICAL to 009's baseline**: 12 failed / 9 passed / 21 total suites; 92 failed /
237 passed / 329 total tests (within 009's 92-94 range). `useAnnounce.test.ts` — the suite this task
existed to fix — still fails 16/16 with the same `NotFoundError`, **unchanged by the RTL bump**.
Root-caused: the real cause is a production-code defect (`useAnnounce.ts` appends/removes DOM nodes
directly on `document.body`, outside React's own tracked tree), not an RTL-version peer mismatch as 009
hypothesized — a version bump cannot fix it. All 12 failing suites individually categorized: 0
harness/config, 0 mechanical-RTL-16-migration-owed, 12 suites (92 tests) genuine product/test defects,
unchanged before/after. Production typecheck confirmed still **0** (protects 006/007/008's clearance);
test-file bucket re-measured at **289**, unchanged (not this task's job to clear). No test
deleted/skipped (ADR-038). Quality gates: code-review + adr-check both 0 critical/0 violations (CLAUDE.md
§8 TEST-MODIFYING override — mandatory, ran clean). Full record:
`notes/017-testing-library-alignment.md`.

**The 12 failing suites / 92 failing tests remain unowned** — this task sharpened the diagnosis (a
production-code fix for `useAnnounce`, ~50 accessible-name/role-query mismatches across `SaveView`/
`SaveFlow`/`TaskPaneShell`/`TaskPaneNavigation`/`useSaveFlow`, a `ShareView` timeout cluster, and two
Office.js mock gaps) but did not fix any of it — out of scope by design. Recommendation section in the
notes doc sizes three follow-on tasks; none currently exist in TASK-INDEX.

## Critical Context (task 006, prior — preserved for history)

Task **006 closed 2026-09-09 (per RE-SCOPE operator decision B1).** Cleared all 73 production typecheck
diagnostics under `shared/taskpane/**` (384 → 289 total; 73 → 0 in-scope; zero increase in any directory
this task does not own — several actually decreased as a side-effect of concurrent tasks 007/008/009 and
this task's own barrel-defect removals). The three known barrel defects (`ViewType`, `SaveOptions` × 2)
were resolved by **removal**, not repointing — neither type exists anywhere in the package (verified via
repo-wide grep); `ViewType` finding recorded for task 015 (FR-03) to pick up when it builds the real tab
type system. `npm run build` passes clean. `npm test` was explicitly NOT attempted (re-scope block removes
it as this task's acceptance criterion — the suite's jest-dom/react19 issues are task 009's territory, now
also closed but not fully green — see task 009's notes).

**Notable judgment call**: `AttachmentSelector` and `EntityPicker` were converted from `React.forwardRef`
to plain function components — this package's `React 19` + `@fluentui/react-components@^9.54.0` combo
makes `forwardRef`-to-any-Fluent-slot-component untypeable (`Ref<never>`, a real library/React-19 typing
gap, not app-code strictness debt). Verified zero behavior change (no caller anywhere passes a `ref` to
either component) before converting. Flagged prominently rather than applied silently — see
`notes/typecheck-fix-patterns.md` § "Task 006" pattern 9 for the full root-cause trace and the note that
any *future* component wanting real ref-forwarding to a Fluent v9 component in this package hits the same
wall (needs a `@fluentui/react-components` version bump, outside a typecheck task's authority).

Full record: `notes/typecheck-fix-patterns.md` § "Task 006" (11 canonical fix-shape patterns, displacement
check, deviations). Files touched: 14, all under `shared/taskpane/**` — no file under `shared/adapters/`,
`shared/services/`, `shared/__mocks__/`, `word/`, or `outlook/` was edited. `tsconfig.json`,
`package.json`, `package-lock.json` untouched by this task (those DID change in the shared worktree, but
from concurrent tasks 007/009 — verified not this task's doing).

## Completed Steps (task 006)

- [x] 0 Rigor declared: FULL. Loaded typecheck-baseline.md, project CLAUDE.md, POML.
- [x] 1 Verified `node_modules` + `@spaarke/auth` dist already present (no fresh install needed)
- [x] 2 Resolved the three barrel defects (index.ts × 2, components/views/index.ts × 1) by removal
- [x] 3 Worked remaining 70 errors file-by-file, highest count first (SaveFlow.tsx 19 → useSaveFlow.ts
      14 → App.tsx 10 → AttachmentSelector.tsx 8 → EntityPicker.tsx 6 → SaveView.tsx 3 → TaskPaneShell.tsx
      3 → errorMessages.ts 3 → index.ts (already 0) → TaskPaneNavigation.tsx 1 → TaskPaneHeader.tsx 1 →
      ErrorBoundary.tsx 1 → SseClient.ts 1)
- [x] 4 Re-ran `npx tsc --noEmit -p .`, filtered to `shared/taskpane/**` production files: 0 remaining
- [x] 5 Displacement check: `shared/adapters` 44→33, `shared/services` 1→0, `shared/__mocks__` 26→24,
      `word/` 0→0, `outlook/` 4→0 — all decreased or held, none increased
- [x] 6 `npm run build` — exit 0 (env vars supplied from `deploy-office-addins.yml`'s non-secret values)
- [x] 7 `npm test` — explicitly not attempted per re-scope
- [x] 8 UI tests — not run live (no `--chrome` session / deployed host in this execution); build-clean is
      the recorded evidence for this pass
- [x] 9 Wrote `notes/typecheck-fix-patterns.md` § "Task 006" (11 fix-shape patterns + `ViewType` finding)
- [x] 9.5 Quality gates — `code-review` + `adr-check`: 0 critical, 0 ADR violations. code-review's own
      pass flagged one minor edge-case behavior nuance (see Decisions Made below), judged in-bounds.
- [x] 10 TASK-INDEX 006 → ✅, POML status → completed with full `<notes>`

## Decisions Made

- **Barrel defects resolved by removal, not repointing** — neither `ViewType` nor `SaveOptions` exist
  anywhere in the package (production or test). `ViewType` finding recorded for task 015 (FR-03).
- **`forwardRef` → plain function component for `AttachmentSelector`/`EntityPicker`** — see Critical
  Context above. Zero-behavior-change verified; documented inline at each component export.
- **Removed a compiler-proven-unreachable `'Attachment'` content-type branch in `useSaveFlow.ts`** —
  `contentType` is only ever assigned `'Email'` or `'Document'`; TS's own exhaustiveness check flagged the
  dead `else if (contentType === 'Attachment')` arm (TS2367). Recoverable from git history if a future task
  revives client-initiated Attachment-type saves.
- **Removed two whole dead code blocks**: `SaveFlow.tsx`'s `renderProcessingOptions` (never called — AI
  processing is always-on per a 2026-09-02 product decision) and `App.tsx`'s entire "Save operation state"
  placeholder (`handleSave` + 4 `useState`s, never wired to any button — the real save path is
  `SaveView`/`useSaveFlow`). Verified self-contained/unreferenced before deleting each.
- **Widened `JobStatus.jobType`/`.progress`/`.createdAt` from required to optional** — the hook's own
  client-constructed "Queued" initial state genuinely omits all three; no code anywhere reads them
  unconditionally. Reflects reality rather than fabricating placeholder values at the call site.
- **Minor edge-case behavior nuance (flagged by my own code-review pass, judged in-bounds)**: in
  `useSaveFlow.ts`'s duplicate-response handling, the `onDuplicate` callback now receives the SAME
  fallback-applied message (`'This item was previously saved.'`) that `setDuplicateInfo` already used one
  line above, instead of the raw (possibly-`undefined`) `responseData.message`. This only differs from the
  pre-existing behavior when the server sends a duplicate response with no `message` field — a real
  behavior-preserving fix was not possible here without widening `onDuplicate`'s `message` parameter to
  `string | undefined` (which would just push the same ambiguity downstream to every consumer). Judged the
  more honest fix since it reuses an already-established fallback in the same function.
- **No §6.5 ADR conflict arose.** adr-check found 0 violations across ADR-021, ADR-012 (Path A), ADR-028.
