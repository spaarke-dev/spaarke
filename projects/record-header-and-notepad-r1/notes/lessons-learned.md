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

*Written by Claude Code sub-agent + main session at project wrap-up 2026-07-03.*
