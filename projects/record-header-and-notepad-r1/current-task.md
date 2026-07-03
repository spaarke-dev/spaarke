# Current Task State — record-header-and-notepad-r1

> **Last Updated**: 2026-07-03 (by /context-handoff skill before /compact)
> **Recovery**: Read "Quick Recovery" section first
> **Session focus**: Post-deployment testing feedback → v1.0.2 remediation with scope expansion (editable fields)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | v1.0.2 fixes — post-testing feedback from user (out of formal task numbering; a mini-project after task 090 partial wrap-up) |
| **Step** | Ready to start implementing fixes (user just approved scope expansion for editable fields) |
| **Status** | in-progress (awaiting the compact so the next agent has clean context) |
| **Next Action** | Implement v1.0.2 changes: (1) fix `$top=0` bug + sprk_todo filter, (2) UI restructure (title inline w/ icons like VisualHost CardChrome, no border, verify Segoe UI 14px), (3) new manifest props (title, showVersion), (4) inline editing (scope-expansion, user approved). Then rebuild → new ZIP → deliver path to user. See "v1.0.2 Fix Plan" section below. |

### Files touched this session (summary)

Recently committed / uncommitted highlights:
- v1.0.1 solution ZIP built at `src/client/pcf/MatterHeader/Solution/bin/MatterHeaderPcf_v1.0.1.0.zip` (deployed to user's env, PCF now loads on Matter form)
- Notepad Vite artifact at `src/solutions/Notepad/dist/notepad.html` (1.12 MB — user has path)
- All Phase 1-4 code + docs committed through `75b196f81`

### Critical Context

User deployed v1.0.1 (which fixed "PCF not showing in form designer" root cause: `usage="input"` → `usage="bound"`). PCF now loads on Matter form. **Live testing surfaced three distinct bug categories, plus PCF config asks, plus a scope expansion request (editable fields) that the user just approved.** All fixes bundled into v1.0.2. Post-compact agent should NOT delegate to sub-agents for the implementation — it's a ~15-file surgical edit across shared lib + PCF best done in main session, then rebuild + provide the new ZIP path. Notepad HTML does not need a rebuild for v1.0.2.

---

## Project state summary (as of handoff)

- **Task count**: 33 of 36 complete (91.7%); 237 tests passing; PR #545 draft
- **Env-dependent open tasks**:
  - 025 MatterHeaderPcf deploy + QA — **user is doing this NOW** (feedback → v1.0.2 fixes)
  - 039 Notepad Vite deploy — user has the `.html` artifact path; whether uploaded is unclear from context
  - 040 Entity-agnostic launch test (FR-19)
- **Deployed to Dataverse env `spaarkedev1.crm.dynamics.com`**:
  - MatterHeaderPcf **v1.0.1** (published) — PCF loads on Matter form, sparkle popover works with `sprk_recordsummary` field, but icons throw HTTP 400 errors

---

## v1.0.2 Fix Plan — this is the current work unit

### 1. Icon-click errors (BLOCKING; sub-agent MUST verify each fix works)

**Error 1**: `GET /api/data/v9.0/sprk_memos?$filter=_sprk_regardingmatter_value eq {guid}&$count=true&$top=0 → 400 Bad Request: Invalid value for $top query option`

- **Root cause**: Dataverse Web API rejects `$top=0` explicitly. Docs state "Values of 0 (zero)" are unsupported.
- **File to fix**: `src/client/shared/Spaarke.UI.Components/src/hooks/useRelatedCount.ts` line 175 — `const query = \`?$filter=${currentFilter}&$count=true&$top=0\`;`
- **Fix**: change `$top=0` → `$top=1` (smallest positive value; response returns 1 record + `@odata.count`; hook already reads `@odata.count` correctly and ignores `.entities`)

**Error 2**: `GET /api/data/v9.0/sprk_todos?$filter=_regardingobjectid_value eq {guid}&... → 400 Bad Request: Could not find a property named '_regardingobjectid_value' on type 'Microsoft.Dynamics.CRM.sprk_todo'`

- **Root cause**: `sprk_todo` does NOT have a polymorphic `regardingobjectid` lookup. Verified via Dataverse MCP schema query. `sprk_todo` uses the same ADR-024 dual-field pattern as `sprk_memo` BUT with **11 supported parent lookups** (not 6 like memo):
  ```
  sprk_regardinganalysis, sprk_regardingbudget, sprk_regardingcommunication,
  sprk_regardingcontact, sprk_regardingdocument, sprk_regardingevent,
  sprk_regardinginvoice, sprk_regardingmatter, sprk_regardingorganization,
  sprk_regardingproject, sprk_regardingworkassignment
  ```
- **File to fix**: `src/client/shared/Spaarke.UI.Components/src/hooks/toolbarLaunchDefaults.ts` — add `SUPPORTED_TODO_PARENTS` map + `buildTodoFilterForParent(entity, id)` helper mirroring the memo helpers
- **Also fix**: `src/client/shared/Spaarke.UI.Components/src/hooks/useRecordHeaderToolbarActions.ts` — change the todo count query from hardcoded `_regardingobjectid_value eq ${recordId}` to `buildTodoFilterForParent(entity, recordId)`
- **Tests to update**:
  - `toolbarLaunchDefaults.test.ts` — add SUPPORTED_TODO_PARENTS + buildTodoFilterForParent tests
  - `useRecordHeaderToolbarActions.test.ts` — update mocks to expect new filter shape
  - `useRelatedCount.test.ts` — assertions expect `$top=1` in URL
- **Recompile shared lib `dist/`** after code changes (CRITICAL per `/pcf-deploy` skill — the PCF bundles from `dist/`, not source)

### 2. UI restructuring (per user feedback)

Reference implementation: `src/client/pcf/VisualHost/control/components/CardChrome.tsx` — the pattern the user wants ("see header (title and icons) as in the Visual Host"). CardChrome's structure:
```
[Title (grows, ellipsis) ................. [icon] [icon] [icon]]
[   body content (chart / fields) ....................... ]
```

Currently `MatterHeaderView` has toolbar rendered by `RecordHeaderShell` in its own row ABOVE the field grid, and there's no title. Restructure:

- **`src/client/shared/Spaarke.UI.Components/src/components/RecordHeader/RecordHeaderShell.tsx`** — add `title?: string` prop; render title left-aligned in the SAME row as `<HeaderToolbar>`; also add `borderless?: boolean` prop (default `false` for backward compat; MatterHeader will pass `true`)
- **`HeaderToolbar.tsx`** — verify `title` prop is already supported per FR-01 (it should be per the current impl). If HeaderToolbar's own title slot works, RecordHeaderShell can just pipe title through.
- **`src/client/pcf/MatterHeader/control/MatterHeaderView.tsx`** — pass `title={context.parameters.title.raw || "Matter"}` and `borderless={true}` to RecordHeaderShell
- **Remove border**: in `RecordHeaderShell.tsx`, wrap current `border` + `borderRadius` style rules in a conditional based on `borderless` prop
- **Font check**: verify styles use `tokens.fontFamilyBase` (= Segoe UI Variable) and `tokens.fontSizeBase300` (= 14px). Fluent v9 default IS Segoe UI 14px so this should already be correct. Audit field renderers: labels should be 12px (`fontSizeBase200`, matches OOB), values should be 14px (`fontSizeBase300`).

### 3. New PCF manifest properties

Add to `src/client/pcf/MatterHeader/control/ControlManifest.Input.xml`:

```xml
<!-- Maker-configurable header title (defaults to "Matter" if empty). -->
<property name="title" display-name-key="Title"
          description-key="Title text to render on the left of the header row (defaults to Matter)."
          of-type="SingleLine.Text" usage="input" required="false" />

<!-- Show/hide the version footer in the header card. Default: hidden. -->
<property name="showVersion" display-name-key="Show version footer"
          description-key="If true, renders a subtle version footer at bottom-right of the header card. Defaults to false."
          of-type="TwoOptions" usage="input" required="false" />
```

- **`src/client/pcf/MatterHeader/control/index.ts`** — pass `title` and `showVersion` from `context.parameters` to MatterHeaderView props
- **`src/client/pcf/MatterHeader/control/MatterHeaderView.tsx`** — accept new props; use `title || "Matter"`; only render version footer when `showVersion === true`
- **Tests to update**: `MatterHeaderView.test.tsx` — new cases for title default, title override, showVersion off/on

### 4. Editable fields — SCOPE EXPANSION (user approved via AskUserQuestion 2026-07-03)

The biggest change. Spec `§OS-05` originally said "v1 fields are read-only" — **user approved expansion** via the AskUserQuestion right before the compact. Add DEF-09 to `notes/defer-issues.md` documenting the scope expansion for auditability.

- **Update spec.md §OS-05** — remove "Inline field editing" from OS-05; add a new FR-04a documenting the added inline-edit behavior. Change is post-deploy per user 2026-07-03 approval — cite explicitly.
- **Field renderer enhancements**:
  - `TextField.tsx` — add `onSave?: (newValue: string) => Promise<void>`, `disabled?: boolean`; when `onSave` provided, render Fluent v9 `Input` in edit-on-click mode; save on blur + Ctrl+Enter; show spinner during save; error → revert + toast
  - `LookupField.tsx` — more complex; add `onSave?: (newLookup: ILookupValue | null) => Promise<void>`; use `Xrm.Utility.lookupObjects()` to open the OOB lookup dialog on edit
  - `OptionSetField.tsx` — add `onSave?: (newValue: number) => Promise<void>`; use Fluent v9 `Dropdown` on edit; save on select
  - `TextareaField.tsx` — add `onSave?: (newValue: string) => Promise<void>`; edit uses `Textarea`; save on Ctrl+Enter + blur
- **`MatterHeaderView.tsx`** — wire onSave handlers using Xrm.WebApi.updateRecord. Optimistic UI: update local state immediately, revert on error.
- **New hook** (optional): `useRecordFieldUpdate(entity, recordId)` returning a callback that wraps `Xrm.WebApi.updateRecord(entity, recordId, { [fieldName]: value })` with error handling. Colocate with `useRecordFieldValues.ts`.
- **NFR compliance**: still zero @spaarke/auth (uses Xrm.WebApi); still zero BFF; still React 16/17 compat; still Fluent v9 tokens.

### 5. Version bump 1.0.1 → 1.0.2 (all 5 locations per /pcf-deploy skill)

- `control/ControlManifest.Input.xml` `version="1.0.2"` + description
- `control/version.ts` `CONTROL_VERSION = '1.0.2'`
- `Solution/solution.xml` `<Version>1.0.2.0</Version>`
- `Solution/Controls/.../ControlManifest.xml` — auto-regenerated from build
- `Solution/pack.ps1` `$version = "1.0.2.0"`

### 6. Rebuild + provide artifact paths

**Sequence per /pcf-deploy skill**:
1. `cd src/client/shared/Spaarke.UI.Components && npm run build` — **CRITICAL** because we're changing shared lib code
2. `cd src/client/pcf/MatterHeader && Remove-Item -Recurse -Force out, Solution/bin, Solution/Controls -ErrorAction SilentlyContinue`
3. `cd src/client/pcf/MatterHeader/Solution && powershell -File pack.ps1` (composite: build:prod + copy + zip)
4. Verify: bundle size still ≤ 250 KB (should be ~40-50 KB); tests still pass
5. Report the new ZIP path: `src/client/pcf/MatterHeader/Solution/bin/MatterHeaderPcf_v1.0.2.0.zip`

**Notepad** does NOT need a rebuild for v1.0.2 (only PCF-side changes).

---

## User's exact feedback (verbatim for reference)

> **1. Icon Functions**
> - click any icons gives this error:
>   ```
>   GET .../sprk_memos?$filter=_sprk...atter_value eq 504c5276-... &$count=true&$top=0 → 400
>   [storage] Error Messages: 1: Invalid value for $top query option.
>
>   GET .../sprk_todos?$filter=_rega...ectid_value eq 504c5276-... &$count=true&$top=0 → 400
>   [storage] Error Messages: 1: Could not find a property named '_regardingobjectid_value' on type 'Microsoft.Dynamics.CRM.sprk_todo'.
>   ```
>
> **2. UI**
> - the title should be on the form and inline with the icons
> - fields are not editable
> - remove border
> - font does not match our standard (segoe ui 14px responsive)
>
> **3. PCF settings**
> - make the version number show/hide option
> - make the title editable
>
> **NOTE: see header (title and icons) as in the Visual Host**

---

## Full session file list (chronological)

- Wave 8 remediation (NFR-04):
  - `src/client/pcf/MatterHeader/featureconfig.json` (new)
  - `src/client/pcf/MatterHeader/webpack.config.js` (new)
  - `src/client/pcf/MatterHeader/control/MatterHeaderView.tsx` (deep-path imports)
  - `projects/record-header-and-notepad-r1/notes/bundle-size.md` (updated with PASS)
- Phase 4 docs:
  - `docs/guides/RECORD-HEADER-PCF-AUTHORING-GUIDE.md` (new, 2586 words)
  - `.claude/patterns/ui/record-header-composition.md` (new, 25 lines)
  - `.claude/patterns/ui/INDEX.md` (updated)
- Partial 090 wrap-up:
  - `projects/record-header-and-notepad-r1/notes/lessons-learned.md` (new)
  - `projects/record-header-and-notepad-r1/notes/defer-issues.md` (new, 8 DEFs)
  - `projects/INDEX.md` (status updated)
- Deployment fixes:
  - `src/client/pcf/MatterHeader/control/ControlManifest.Input.xml` — `recordId` (input) → `boundField` (bound); v 1.0.0 → 1.0.1
  - `src/client/pcf/MatterHeader/control/index.ts` — reads recordId from contextInfo.entityId only
  - `src/client/pcf/MatterHeader/control/version.ts` — 1.0.0 → 1.0.1
  - `src/client/pcf/MatterHeader/Solution/solution.xml` — 1.0.0.0 → 1.0.1.0
  - `src/client/pcf/MatterHeader/Solution/pack.ps1` — version comment + `$version`
  - `src/client/pcf/MatterHeader/Solution/bin/MatterHeaderPcf_v1.0.1.0.zip` (built artifact)
- Notepad Vite build:
  - `src/solutions/Notepad/dist/notepad.html` (1.12 MB, self-contained HTML)

## Session commit history (see git log for full list)

- `2110ad6d6` — docs Phase 4 + partial 090 wrap-up
- `75b196f81` — boundField fix + v1.0.1 solution ZIP
- Earlier session commits (chronological): `0ebdf986f` (Wave 1) · `801bca67e`+`9acd093ba` (Wave 2) · `71ccfd65c` (Wave 3) · `d231cd36f` (Wave 4 Phase 1 complete) · `999fae7c5` (Wave 5) · `70fccd095` (Wave 6 merge) · `a671f1bec` (Wave 7) · `b2d53f05a` (Wave 8) · `0f35e140b` (task 038) · `6c1c1d631` (task 041) · `2183b9702` (task 001 corrections)

## Decisions made this session

- **NFR-04 remediation (Wave 8)**: three-part fix — featureconfig.json + webpack.config.js + deep-path imports. Bundle 1.57 MiB → 38 KB (43×). Root cause: shared lib top-level barrel drags EntityCreationService → mammoth chain. Documented in bundle-size.md + authoring guide.
- **`boundField` manifest fix (v1.0.1)**: PCF wasn't listed in form designer because all properties were `usage="input"`. Changed to `usage="bound"` `required="true"`. Bound field value is ignored; recordId still read from `context.mode.contextInfo.entityId`.
- **Editable fields scope expansion approved (2026-07-03)**: user chose "Implement inline editing now in v1.0.2" via AskUserQuestion. Original spec §OS-05 excluded this. Need to update spec.md §OS-05 to reflect the expansion + file DEF-09.
- **Env-dependent tasks 025/039/040** deferred to owner as documented in `notes/defer-issues.md`. User is now doing 025 in real time.
- **Notepad artifact path provided to user**. User's next step re: 039 is manual upload to `sprk_notepad_page` webresource. Whether uploaded yet is unclear.

## Post-compaction recovery instructions (for next Claude instance)

1. **Read this file first** — the Quick Recovery section tells you where you are
2. **Load these key files into context** (in order):
   - `projects/record-header-and-notepad-r1/spec.md` — FRs + NFRs
   - `projects/record-header-and-notepad-r1/CLAUDE.md` — MUST/MUST-NOT rules
   - `projects/record-header-and-notepad-r1/notes/design-alignment-corrections.md` — post-001 schema truth
   - `projects/record-header-and-notepad-r1/notes/defer-issues.md` — DEFs + open env tasks
   - `.claude/skills/pcf-deploy/SKILL.md` — deploy conventions (must-follow for v1.0.2 build)
3. **Read the "v1.0.2 Fix Plan" section above** — that's your work unit
4. **Sub-agent guidance**: Do the v1.0.2 fixes in main session; don't delegate. It's ~15 files across shared lib + PCF; a sub-agent lacks the full session context.
5. **Before starting**: briefly confirm user still wants editable fields NOW (they said yes just before compact; a one-liner confirmation avoids a wasted iteration).
6. **Follow the version-bump discipline strictly** (5 locations per pcf-deploy skill)
7. **Verify tests still pass** before packing the new ZIP: run `npm test` in the shared lib + `npx jest` in `src/solutions/Notepad`
8. **Provide the user the new v1.0.2 ZIP path** at the end

## Test totals (as of handoff)

- Phase 1 (shared lib): 118 tests
- Phase 2 (MatterHeaderView unit): 7 tests
- Phase 3 (Notepad): 112 tests
- **Total: 237 tests, all passing**

Expected new test count after v1.0.2: ~260-275 (adds SUPPORTED_TODO_PARENTS/buildTodoFilterForParent tests, borderless+title RecordHeaderShell tests, manifest prop tests, inline-editing tests per field renderer).

## Blockers

**Status**: None. User has approved all scope. Just needs implementation.

## Applicable ADRs (project-wide, still relevant for v1.0.2)

- ADR-006 (PCF over webresources)
- ADR-012 (shared component library)
- ADR-021 (Fluent v9 semantic tokens)
- ADR-022 (React 16/17 in shared lib; React 18 in code page)
- ADR-024 (polymorphic resolver — sprk_todo AND sprk_memo both use this, verified 2026-07-03)
- ADR-028 (auth v2 — N/A here; host-context Xrm.WebApi only)
- ADR-038 (testing strategy)

---

## Next action (SINGLE MOST IMPORTANT LINE)

Implement the v1.0.2 Fix Plan above (5 sub-tasks: query fixes, UI restructure, manifest props, inline editing, version bump), rebuild + repack, deliver the new ZIP path to the user. **User will upload the new ZIP to their Dataverse env** — same manual upload flow as v1.0.0 and v1.0.1.

---

*Checkpoint written by /context-handoff skill before /compact — 2026-07-03. Post-compact agent: read the Quick Recovery + v1.0.2 Fix Plan sections first.*
