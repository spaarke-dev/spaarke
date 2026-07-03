# Deferrals + Issues — record-header-and-notepad-r1

> **Status**: at project wrap-up (2026-07-03). Env-dependent tasks (025/039/040) still open — separate from the DEFs below.

## Deferrals filed during execution

| ID | Title | Root cause | Recommended action | Cost-of-doing-nothing |
|---|---|---|---|---|
| DEF-01 | Refresh icon in sparkle popover — wire to BFF regeneration endpoint | R1 explicitly deferred wiring per FR-08a and NFR-07 | Follow-on project adds a `Sprk.Bff.Api` endpoint + client wiring | Sparkle refresh remains a placeholder; users can't regenerate `sprk_recordsummary` from the header |
| DEF-02 | Matter form binding — add `MatterHeaderPcf` to Matter form XML | Owner clarification O3 moved this to follow-on maker task | Deploy via unmanaged solution form update; add PCF to Matter form's header section | PCF is deployed to environment but not visible on the Matter form until a maker binds it |
| DEF-03 | VisualHost `CardChrome` migration to consume `HeaderToolbar` | Out of R1 scope per §8.1; CardChrome remains internal to VisualHost | Separate refactor project migrating VisualHost's CardChrome to the new shared HeaderToolbar | VisualHost + this project's HeaderToolbar overlap slightly (2 toolbar implementations); one project already caught the anti-pattern per §11 default-to-reuse |
| DEF-04 | `MemoSection.tsx` adoption of `useSprkMemoRepository` | Out of R1 scope; extract-now-adopt-later pattern per spec §3.6 | Refactor `src/solutions/EventDetailSidePane/src/components/MemoSection.tsx` to consume `useSprkMemoRepository` from Notepad (or promote the hook to shared lib first — see DEF-08) | Notepad + MemoSection maintain duplicate CRUD logic for `sprk_memo`; drift possible |
| DEF-05 | Per-entity PCF templates (`ProjectHeaderPcf`, `InvoiceHeaderPcf`, `EventHeaderPcf`) | Each is a separate ~80 LOC follow-on per §Scope OS-02 | Individual follow-on projects following `docs/guides/RECORD-HEADER-PCF-AUTHORING-GUIDE.md` | Only Matter gets the new header experience until each entity's PCF ships |
| DEF-06 | Add `exports` field to `@spaarke/ui-components/package.json` | Uncovered in Wave 8 (task 024): top-level barrel drags `EntityCreationService` → `mammoth` chain (~550 KiB) via CommonJS re-exports; consumers must use fragile `dist/*` paths | Update shared lib `package.json` with public `exports` map; consumers migrate to clean paths like `@spaarke/ui-components/RecordHeader` | Every PCF consuming the shared lib needs the "deep-path imports" workaround; DEF-04's MemoSection consolidation is also blocked by this |
| DEF-07 | Capture the PCF build-repair pattern (tsconfig placement + manifest location + contextInfo type-cast + ajv hoist) as a pattern file | Task 024 discovered 5 build-blocking gaps that task 021 didn't catch | Author `.claude/patterns/pcf/pcf-build-scaffold.md` covering: control/ subfolder placement, contextInfo type-cast idiom, tsconfig.json copy from SemanticSearchControl, ajv v8 devDep, featureconfig.json + webpack.config.js | Future PCF authors repeat the same 5 mistakes; slow ramp-up per PCF |
| DEF-08 | Promote `useSprkMemoRepository` to `@spaarke/ui-components/hooks/` | Currently lives inside `src/solutions/Notepad/`; DEF-04's MemoSection adoption would benefit from a shared version | After DEF-04 lands: extract hook + types + `discoverMemoNavProps` to shared lib; deprecate local Notepad copy | Hook lives inside Notepad forever; MemoSection either forks it (anti-pattern) or stays inline |

## Env-dependent tasks (deferred to owner, NOT DEFs)

These are the 3 tasks blocked on Dataverse env access + manual QA:

| Task | What owner needs to do | Prerequisites | Est. time |
|---|---|---|---|
| **025** MatterHeaderPcf deploy + QA | Import solution ZIP + manual QA sparkle popover, checkmark modal (85%×85%), annotation modal (70%×80%), dark mode | `pac auth list` shows active connection to target env; PCF ZIP built via task 023's `pack.ps1` | 60-90 min |
| **039** Notepad Vite build + deploy | Build Vite bundle + register `sprk_notepad_page` webresource via `/code-page-deploy` | Same env access as 025; also may need customization publisher | 30-45 min |
| **040** Entity-agnostic launch test | Launch Notepad with synthetic non-Matter `regardingEntity` + `regardingId`; verify FR-19 | 039 complete first | 30 min |

**Owner unblocking sequence**: Run 025 first (verifies MatterHeaderPcf end-to-end including annotation icon opening Notepad — but Notepad isn't deployed yet so annotation will 404); OR run 039 first (Notepad deploys) then 025 (MatterHeaderPcf can complete QA including annotation). Recommend **039 before 025** for cleaner QA.

## Uncovered issues (not in original scope)

### `EntityCreationService.ts` `@spaarke/sdap-client` compile gap
Pre-existing repo state (not introduced by this project). `src/client/shared/Spaarke.UI.Components/src/services/EntityCreationService.ts` imports `@spaarke/sdap-client` which is missing from the workspace. Multiple sub-agents flagged this. **Recommendation**: file a separate repo-hygiene issue outside this project's scope.

### Fluent v9 `MessageBar` render as `role="group"` not `role="alert"`
Task 038 documented this. If Notepad's error banner needs to be announced by screen readers, we'd need to set `politeness="assertive"` on the MessageBar. Current default (polite → `role="group"`) is acceptable since the banner is already visually prominent and occupies the full shell. **No action needed unless a11y audit says otherwise.**

## How to file NEW deferrals

Invoke `/project-defer-issue-tracking` (or `/defer`). Every entry MUST name a concrete failure mode per CLAUDE.md §11. NEVER add an entry only here without filing the corresponding GitHub Issue.

## Status at wrap-up

- 33 of 36 tasks complete (91.7%)
- 237 unit + integration tests passing
- PR #545 draft; ready to mark ready-for-review once 025/039/040 complete + owner sign-off
