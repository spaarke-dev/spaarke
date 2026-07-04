# Lessons Learned — record-header-and-notepad-r1

> **Project period**: 2026-07-02 to 2026-07-03
> **Status at write time**: 33 of 36 tasks complete (91.7%); 237 tests passing; 3 tasks env-dependent (25 PCF deploy, 39 Notepad deploy, 40 entity-agnostic launch test) deferred to owner for Dataverse env access + manual QA.
> **PR**: #545 (draft)

## What worked

### Parallel sub-agent execution
The pipeline's parallel-group design paid dividends. Waves 1, 2, 5, 6, 7, 8 each dispatched 2–6 sub-agents concurrently. Total dev-time compression: what would have been ~40+ hours of sequential work landed in under 24 hours of wall-clock elapsed time. Key insight: **truly parallel-safe tasks that write to disjoint files are the sweet spot**. The 4 field renderers (005-008) each wrote to their own `fields/*.tsx` and appended to `fields/index.ts` + `fields.test.tsx` — the race resolved cleanly via additive appends.

### Task 001 as the schema-truth gate
Task 001 (sprk_memo schema verification) caught THREE material discrepancies before any code shipped:
1. Body field `sprk_memobody` (not `sprk_body`)
2. Title field `sprk_name` NOT NULL (design assumed derived from body)
3. Full ADR-024 dual-field pattern (owner clarification O1 was incomplete)
4. `sprk_recordsummary` is a **field on Matter**, not a separate entity

Also fixed: SmartTodo webresource actual name is `sprk_smarttodo` (not `sprk_smarttodo_page` — no `_page` suffix). This is the pattern to keep: **spec-time assumptions are validated empirically before Phase 1 code lands.**

### Dataverse MCP for schema verification
`mcp__dataverse__describe('tables/sprk_memo')` + `describe('tables/sprk_matter')` gave definitive schema truth in seconds. Avoided the fallback of "inspect existing consumer" (which would have surfaced `sprk_memobody` but missed the full lookup + resolver-field structure). Use MCP describe as the FIRST verification step for any Dataverse-touching project.

### PCF bundle-optimization triad
Wave 8 discovered that MatterHeaderPcf bundled to 1.57 MiB (6.4× the 250 KB NFR-04 ceiling) because task 021's initial config was incomplete. Owner intervention pointed to the `/pcf-deploy` skill documentation. Applying the three-part fix (documented in [`notes/bundle-size.md`](bundle-size.md)) dropped the bundle to **38 KB** — a 43× reduction:

1. `featureconfig.json`: `pcfReactPlatformLibraries: on` + `pcfAllowCustomWebpack: on`
2. `webpack.config.js`: Fluent icon tree-shaking
3. Deep-path imports: `@spaarke/ui-components/dist/components/RecordHeader` (bypasses top-level barrel's `EntityCreationService` → `mammoth` chain)

**Convention captured for future PCFs** in the authoring guide + pattern pointer.

## What surprised us

### Owner O1 clarification was incomplete
Owner's initial answer to "how is `sprk_memo` regarding modeled?" was "text field `sprk_regardingrecordid`, GUID only". Actually the full ADR-024 dual-field pattern applies. The owner accepted correction path-C when task 001's MCP verification surfaced the truth. This is a case where the **AI process caught a human blind spot**, exactly as the schema-verification-first workflow is designed to.

### The pre-existing `@spaarke/sdap-client` barrier
The shared library's top-level barrel drags `EntityCreationService` → `@spaarke/sdap-client` → `mammoth` docx-processing chain. Every sub-agent that tried to work through the top-level barrel hit this — some in TypeScript compilation (`@spaarke/sdap-client` missing), some in the bundle. Two workarounds emerged:
1. **In tests**: mock `@spaarke/ui-components` at the module boundary (task 014, task 022)
2. **In consumer code**: deep-path imports (Wave 8 PCF fix)

Neither is ideal. **Root cause: `@spaarke/ui-components/package.json` has no `exports` field.** DEF-06 filed for follow-on remediation.

### Fluent v9 Menu + Popover portal to `document.body`
Not surprising to Fluent v9 veterans, but the Notepad test suite had to consistently query portals via `document.body.querySelectorAll(...)` rather than the rendered container. Sub-agents documented this quirk 4+ times independently. Should be captured in a general Fluent v9 test-authoring pattern.

### Jest config typo: `setupFilesAfterEach` (silent no-op)
Task 036 sub-agent flagged that Notepad's `jest.config.cjs` had `setupFilesAfterEach` (which silently no-ops) instead of `setupFilesAfterEnv`. Task 038 fixed it; all 102 tests continued to pass. Reveals that our polyfills (matchMedia + ResizeObserver) weren't loading — tests happened not to exercise those code paths. **Add a `jest.config.cjs` lint / validation check** to catch this class of typo repo-wide.

### Prettier CI amendments interleaved with our commits
Every wave push encountered "non-fast-forward" rejection because Prettier's CI job committed formatting fixes to our branch. Workflow: commit → pull with merge → push (never rebase, per user's non-destructive preference). No content conflicts; just re-formatting. **Consider running Prettier locally before commit** to avoid the CI ping-pong.

## What to change for follow-on

### Split fields/index.ts editing across parallel tasks
Wave 2's 4-way race on `fields/index.ts` + `fields.test.tsx` resolved cleanly but was fragile. For future field-renderer batches, either:
- (a) Serialize the field renderer tasks (accept the wall-clock cost), OR
- (b) Have each task write to its own `fields.test.tsx` (or its own `describe` block file), and a final aggregation task combines them.

### Schedule env-dependent tasks separately
Tasks 025 (PCF deploy + QA), 039 (Notepad deploy), 040 (entity-agnostic launch test) require Dataverse env access + manual browser QA. Autonomous execution can prepare artifacts but cannot verify. **Recommendation**: schedule these as a separate "owner deploy session" at project close rather than treating them as autonomous.

### Shared lib `exports` field
DEF-06 (filed): add `exports` to `@spaarke/ui-components/package.json` so consumers get clean sub-path imports without the fragile `dist/` string. Would benefit MatterHeaderPcf + all future PCFs consuming the lib.

### Test framework consistency
Notepad chose `react-dom/client` + `act()` harness (no `@testing-library/react`) to keep bundle lean. Shared lib uses `@testing-library/react`. The two conventions coexist but new authors need to know which pattern to follow where. **Document this in the authoring guide.** (Already added in task 051.)

## For the next Record Header PCF (ProjectHeaderPcf / InvoiceHeaderPcf / ...)

The authoring guide ([`docs/guides/RECORD-HEADER-PCF-AUTHORING-GUIDE.md`](../../../docs/guides/RECORD-HEADER-PCF-AUTHORING-GUIDE.md)) has the complete recipe. Key highlights:

1. **Do NOT skip** `featureconfig.json` + `webpack.config.js` — bundle bloats to 1.5+ MB without them
2. **Use deep-path imports** from `@spaarke/ui-components/dist/components/RecordHeader` (not top-level barrel)
3. **Copy the MatterHeaderPcf structure verbatim** — including `<platform-library>` entries and `control-type="virtual"`
4. **Version bump the 4 locations** per PCF-DEPLOYMENT-GUIDE (manifest, view footer, solution.xml, Ship ControlManifest.xml)
5. **Match SUPPORTED_MEMO_PARENTS entries** if the entity needs the annotation icon to work

Estimated time for a follow-on entity: **4-6 hours** if the entity is already in `SUPPORTED_MEMO_PARENTS` and has a `sprk_recordsummary` field.

---

## Phase 6 additional lessons (added 2026-07-04)

Phase 6 (DEF absorption — user's "reasonable scope in R1" ask) shipped 5 of 6 DEFs in ~1 dev-day. Six additional lessons that didn't surface in Phases 1–5:

### 1. Sparkle popover odyssey → shared-component-first is a rule, not a suggestion

Rounds 6–9 of live QA (v1.0.5 → v1.0.11) chased sparkle popover styling divergence — no background, wrong border-radius, no shadow, wrong font. Each round we tuned CSS on our re-implementation. Odyssey ended when the user asked: **"why isn't this just the shared `<AiSummaryPopover>` we already have?"** Answer: because we didn't check. Once we swapped to the shared component + wrapped `MatterHeaderHost` in `<FluentProvider>` (VisualHost's proven pattern for portal-vars to reach Popover), it worked in one build.

**Rule (CLAUDE.md §11 restated)**: Before implementing ANY new UI surface, `Grep` for existing shared components matching the intent. `@spaarke/ui-components/components/` catalog is small enough to skim. 5 minutes of grep beats 5 rounds of styling divergence.

### 2. Half-shipped contracts are a real risk (DEF-11 Part 2 saga)

DEF-11's original launch (Part 1) emitted `data: "action=openTodos&regardingType=<entity>&regardingId=<id>"` to SmartTodo. QA revealed the Kanban rendered unfiltered — because SmartTodo's `useLaunchContext.ts` **parses** the openTodos contract completely (with tests) but **nothing consumed** `regardingFilter` in the Kanban query. The stale comment in `SmartTodoApp.tsx:512` referring to "R4 task 030" led us to believe the consumer was wired; investigation showed R4 task 030 was actually a "4-row layout" task. R4 shipped the parser and never wired the consumer. Nobody noticed for months because no upstream caller emitted the payload until DEF-11 Part 1.

**Rule for reviewers**: When reviewing PRs that add a "parse-side" of a launch contract, insist on the consumer side landing in the SAME PR (or an immediately-adjacent one) with a QA scenario that exercises the round trip. Otherwise the contract is dead code that fails silently when a real consumer arrives.

**Rule for authors**: If you MUST land a parser without a consumer, add a `// TODO: consumer wire-up — TASK-NNN` comment plus a runtime `console.warn`. Otherwise stale pointer comments (like R4 task 030) mislead future contributors.

### 3. Trust user test feedback over the original design (DEF-11 pivot)

The DEF-11 pivot ("reuse SmartTodo with matter filter" vs "build a new sprk_todospage DataGrid Code Page") saved ~6 hours AND gave users a better UX (full interactive Kanban vs read-only grid). The pivot came from one line of user QA feedback: *"could we instead open the Smart To Do (as it is now) but filtered by the matter?"* Six hours of research went into the original design that a QA insight replaced in 30 seconds.

**Rule**: When user QA feedback conflicts with the original design, treat the feedback as authoritative unless a hard constraint blocks it. The original design was hypothesis; feedback is data. This paired with the "extend existing over introduce new" rule (CLAUDE.md §11) to make the pivot obviously correct once considered.

### 4. Pre-existing test-assertion drift is normal — investigate before assuming regression

The `useRecordHeaderToolbarActions` test asserted `pageInput.name === SMARTTODO_WEBRESOURCE_NAME` but source has been sending `webresourceName` (the correct Power Apps property) for a while. Similarly Notepad has 2 tests failing since v1.0.9 (memo-state-on-keystroke fix) and 9 sparkle tests failing since v1.0.10 (sparkle slot retired from the hook). When Phase 6 changes caused test failures, first move was `git stash` + rerun on baseline — confirmed these were pre-existing, not Phase 6 regressions.

**Rule**: When tests fail on new code, ALWAYS test the baseline first with `git stash` before assuming your change broke them. Silent test drift accumulates over time; new work often just surfaces it.

### 5. `exports` field ≠ single-project change (DEF-06 revert)

DEF-06 looked like a 4-8 hour clean-imports migration on `@spaarke/ui-components/package.json`. Turned into ecosystem-wide `pcf-scripts/tsconfig_base.json` `moduleResolution: "bundler"` bump because Webpack v5 (which reads `exports`) demands directory-index resolution that Node's legacy `moduleResolution: "node"` doesn't support. Every PCF in the repo would ripple.

**Rule**: Anything touching `package.json` `exports` requires the ENTIRE build ecosystem (webpack, ts-jest, pcf-scripts, vite, rollup, ts-loader) to be on `moduleResolution: "bundler"` or `"node16"` FIRST. Filed as R2B project with explicit ecosystem-migration scope. Reversing quickly (same-day, before commit) prevented a broken checkpoint.

### 6. current-task.md as pre-compact insurance is invaluable

Before running `/compact` mid-Phase 6, we wrote a comprehensive `current-task.md` with full DEF-10 + DEF-11 research embedded (76 + 95 lines of specifics: root causes, file-by-file migration tables, exact commands). Post-compact work continued with zero re-investigation. Without it, we'd have re-discovered SmartTodo's openTodos contract 3 times.

**Rule**: When approaching context limits mid-investigation, embed the research findings + concrete file paths + expected outcomes in `current-task.md` BEFORE compact — not just "resume here." The Quick Recovery section handles the "where was I" question; the full-body research section handles the "what was I learning" question. Both matter.

### Phase 6 numbers

| Metric | Value |
|---|---|
| Original R1 estimate (Phases 1–5) | 88–116 h |
| Phase 6 add-on estimate (6 DEFs) | 40–64 h |
| **Actual Phase 6 elapsed** | ~1 dev-day (mostly Ralph's QA + one context-handoff cycle) |
| DEF-11 estimate → actual | 2–3 days DataGrid page → ~30 min pivot + 2 h consumer wire + 30 min filter enhancement |
| DEF-10 bundle reduction | 1,176,060 → 478,612 bytes = **59% reduction** |
| MatterHeader PCF version | 1.0.11 → 1.0.12 (DEF-09 + DEF-11 Part 1) |
| Deployables shipped this phase | MatterHeaderPcf_v1.0.12.0.zip (18 KB); Notepad `smarttodo.html` twice (Part 2 + Part 3); Notepad `notepad.html` (478 KB) |
| Files modified in Phase 6 | 25 initial + 4 DEF-11 Part 2 + 4 DEF-11 Part 3 = 33 |
| Tests baseline vs Phase 6 | No Phase-6-introduced failures. Pre-existing drift documented, not fixed (scope) |
| BFF endpoints added | 0 (NFR-07 preserved) |

---

*Original body written by Claude Code sub-agent + main session at project wrap-up 2026-07-03.*
*Phase 6 addendum written by Claude Code 2026-07-04 (Opus 4.7) after user acceptance.*
