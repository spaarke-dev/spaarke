# Configurable Record Header — R2 — AI Context

> **Purpose**: Context for Claude Code when working on `record-header-and-notepad-r2`.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: 0 — design complete, pre-spec
- **Last Updated**: 2026-08-21
- **Current Task**: none
- **Next Action**: Discovery pass ([`notes/discovery-checklist.md`](notes/discovery-checklist.md)) → `/design-to-spec` on `design.md`

---

## ⚠️ Read this before anything else

**The plan changed on 2026-08-21.** The 2026-07-05 seed proposed four cloned per-entity PCFs (`ProjectHeaderPcf`, `InvoiceHeaderPcf`, `WorkAssignmentHeaderPcf`, `EventHeaderPcf`). **That is withdrawn.** R2 ships **ONE configuration-driven control**, `Spaarke.Records.RecordHeader`.

Stale references to the withdrawn plan still exist in the repo and will mislead you:

| Location | Staleness |
|---|---|
| [`docs/guides/RECORD-HEADER-PCF-AUTHORING-GUIDE.md`](../../docs/guides/RECORD-HEADER-PCF-AUTHORING-GUIDE.md) | Teaches the retired per-entity recipe end-to-end. **Rewriting it is an R2 deliverable** (design.md §3.1). Its bundle-optimization triad section (§6) is still correct and still mandatory. |
| [`projects/record-header-and-notepad-r1/plan-extension.md`](../record-header-and-notepad-r1/plan-extension.md) DEF-05 | Describes R2 as "four per-entity PCFs". Historical record — do not edit. |
| [`projects/record-header-and-notepad-r1/CLAUDE.md`](../record-header-and-notepad-r1/CLAUDE.md) | Same. Historical — do not edit. |
| [`projects/record-header-and-notepad-r1/design.md`](../record-header-and-notepad-r1/design.md) §1 + architecture diagram | Same. Historical — do not edit. |

**Corrected on this branch** (all three previously steered agents toward the retired approach):

- [`.claude/patterns/ui/record-header-composition.md`](../../.claude/patterns/ui/record-header-composition.md) — now warns against it
- [`.claude/patterns/ui/INDEX.md`](../../.claude/patterns/ui/INDEX.md) — the row description said "Authoring a new per-entity Record Header PCF". Highest-traffic of the three: this is the index agents scan to choose a pattern.
- [`.claude/patterns/pcf/xrm-webapi-related-count.md`](../../.claude/patterns/pcf/xrm-webapi-related-count.md) — "future `ProjectHeaderPcf` / `InvoiceHeaderPcf`" in its Typical-uses list

**Not stale — do not "fix"**: `docs/architecture/finance-intelligence-architecture.md` mentions an `InvoiceHeader`, but that is a C# model class in `Sprk.Bff.Api/Services/Finance/Models/`, unrelated to this PCF.

---

## Quick Reference

### Key Files

- [`design.md`](design.md) — **authoritative**; re-scoped 2026-08-21. Config schema §5.2, metadata sourcing §5.4, shared-lib work §6.
- [`notes/discovery-checklist.md`](notes/discovery-checklist.md) — blocking verification before spec
- [`README.md`](README.md) — overview + decisions
- [`current-task.md`](current-task.md) — active task state (context recovery)
- `spec.md` / `plan.md` / `tasks/` — ⏳ not generated yet

### Project Metadata

- **Project Name**: `record-header-and-notepad-r2` (folder/ID retained for R1 cross-link continuity; the deliverable is no longer Notepad work)
- **Type**: PCF + Shared Library + Documentation
- **Complexity**: Medium
- **Portfolio**: not yet registered — run `/devops-project-register`

### Hot-Path Declaration

- **BFF**: N — no `Sprk.Bff.Api` touches; all Dataverse via `Xrm.WebApi`
- **SpaarkeAi**: N — form-embedded PCF, not a workspace widget
- **CI workflows**: N — DEF-06 dropped (design.md §7.1), so no `pcf-scripts` ripple
- **Skill directives**: N — touches `.claude/patterns/` (pointer refresh), not `.claude/skills/`
- **Root CLAUDE.md**: N

---

## Key Technical Constraints

### MUST

- **MUST** use `Xrm.WebApi` / `Xrm.Page` for all Dataverse I/O (ADR-028 host-context boundary; R1 NFR-05)
- **MUST** use Fluent v9 semantic tokens exclusively — zero hex/rgb/hsl (ADR-021)
- **MUST** keep shared components React 16/17-safe — no `use()`, no `useSyncExternalStore`, no `createRoot` (ADR-022)
- **MUST** preserve R1's form-buffer dirty-state pattern exactly. Edits stage via `Xrm.Page.getAttribute(n).setValue(v)`, **not** `Xrm.WebApi.updateRecord`. This exists because writing straight to Dataverse re-rendered the whole PCF on every edit (R1 v1.0.7).
- **MUST** keep the Notepad + SmartTodo launch contracts byte-identical — `regardingEntity`/`regardingId` and `action=openTodos&regardingType=…&regardingId=…` are external API (R1 NFR-09)
- **MUST** keep the bundle-optimization triad intact: `featureconfig.json` + `webpack.config.js` + deep-path `@spaarke/ui-components/dist/*` imports. ~40 KB vs 1.6 MB without.
- **MUST** degrade gracefully on bad config — `console.warn` + derived defaults. A malformed JSON paste must never blank a production form.

### MUST NOT

- **MUST NOT** add any endpoint, service, or DI registration to `src/server/api/Sprk.Bff.Api/**` (R1 NFR-07 continues)
- **MUST NOT** import `@spaarke/auth`
- **MUST NOT** wire the sparkle refresh icon (needs a BFF endpoint — DEF-01, out of scope)
- **MUST NOT** create a `sprk_headerconfiguration` Dataverse table — explicitly rejected (design.md §5.4)
- **MUST NOT** do the DEF-06 `exports`/`moduleResolution` migration here (design.md §7.1) — and do not "clean up" the `dist/*` deep-path imports, they are load-bearing for bundle size
- **MUST NOT** promote `useSprkMemoRepository` (DEF-08) — no second consumer
- **MUST NOT** modify `src/client/pcf/VisualHost/**` or `src/solutions/EventDetailSidePane/**`
- **MUST NOT** fork any R1 shared primitive. Missing behavior lands in the shared lib so every entity gets it.

### ADR posture

No conflict; no CLAUDE.md §6.5 escalation. Worth knowing why: R1's project CLAUDE.md paraphrases ADR-011 as "typed components > runtime schemas," which reads like a blocker for a config-driven control. [ADR-011](../../.claude/adr/ADR-011-dataset-pcf.md) contains no such rule — its actual MUSTs ("reuse shared components", "MUST NOT duplicate UI primitives") point toward this design, and VisualHost/DataGrid are established config-driven precedent.

Applicable: ADR-006, ADR-012, ADR-021, ADR-022, ADR-024, ADR-038. ADR-028 is N/A (host-context only).

---

## 🚨 Task Execution Protocol

All task work MUST use the `task-execute` skill — do not read POML files and implement manually. Trigger phrases ("work on task X", "continue", "next task", "keep going", "resume task X", "pick up where we left off") → invoke `task-execute`. Parallel tasks = ONE message with MULTIPLE Skill invocations.

See [task-execute SKILL.md](../../.claude/skills/task-execute/SKILL.md).

---

## Decisions Made

- **2026-08-21**: One configurable control replaces four cloned PCFs. ~180 lines of generic machinery in `MatterHeaderView.tsx` would otherwise be duplicated 4×.
- **2026-08-21**: Config = JSON on a `layoutJson` manifest property, **not** a Dataverse config table. Owner rationale: few instances ever, unlike VisualHost. Bonus: config travels in form XML, so nothing to seed per environment. Resolver is tier-shaped so a config-record tier can slot in later without touching renderers.
- **2026-08-21**: Rollout = Project + Work Assignment → Invoice + Event → Matter last. Invoice is explicitly required, not optional; it forces the currency + date renderer work.
- **2026-08-21**: DEF-06 and DEF-08 both dropped from R2 scope (design.md §7).
- **2026-08-21**: New control identity `Spaarke.Records.RecordHeader` + one-time Matter form re-bind, rather than keeping `constructor="MatterHeader"` forever.

---

## Implementation Notes

- **Entity self-detection**: `context.mode.contextInfo.entityTypeName`. Proven in [`VisualHostRoot.tsx:253`](../../src/client/pcf/VisualHost/control/components/VisualHostRoot.tsx#L253) and [`TrackingFieldTrio/index.ts:346`](../../src/client/pcf/TrackingFieldTrio/index.ts#L346). Not in the current `@types/powerapps-component-framework` — use the established type-cast idiom.
- **`LOOKUP_META` removal hinges on the §5.4 discovery item.** If `sprk_mattertype_ref`'s primary id/name attributes aren't what we expect, add the optional `fields[].lookup` escape hatch. Nothing else in the design moves.
- **Sequence matters**: shared-lib renderers + resolver land **before** any form binding. Matter migrates **last**.
- **`Xrm.Navigation.navigateTo` gotchas** (R1 lost releases to each): call it **directly** on `xrm.Navigation` — aliasing strips `this` and makes it a silent no-op. Property is `webresourceName`, not `name`. `data` must be a URL-encoded **string**, not an object.
- **`Xrm.WebApi` does not expose `@odata.count`** — `useRelatedCount` counts `entities.length` client-side. See [`.claude/patterns/pcf/xrm-webapi-related-count.md`](../../.claude/patterns/pcf/xrm-webapi-related-count.md).
- **Reference resolver**: [`configResolution.ts`](../../src/client/shared/Spaarke.UI.Components/src/components/DataGrid/configResolution.ts) is the proven in-repo shape for tiered config resolution — mirror its structure and test approach.

---

## Resources

### Applicable Skills

`fluent-v9-component` (CRITICAL) · `pcf-deploy` (CRITICAL) · `dataverse-mcp-usage` (discovery) · `ui-test` · `code-review` + `adr-check` (Step 9.5 gates) · `adr-aware`, `spaarke-conventions`, `context-handoff`

### Applicable Patterns

- [`.claude/patterns/ui/record-header-composition.md`](../../.claude/patterns/ui/record-header-composition.md) — corrected on this branch
- [`.claude/patterns/pcf/pcf-build-scaffold.md`](../../.claude/patterns/pcf/pcf-build-scaffold.md) — the 10 build gotchas from R1 UAT
- [`.claude/patterns/pcf/xrm-webapi-related-count.md`](../../.claude/patterns/pcf/xrm-webapi-related-count.md)
- [`.claude/patterns/pcf/dataverse-queries.md`](../../.claude/patterns/pcf/dataverse-queries.md) · [`.claude/patterns/pcf/fluent-v9-modern-theming.md`](../../.claude/patterns/pcf/fluent-v9-modern-theming.md)
- [`.claude/patterns/ui/fluent-v9-component-authoring.md`](../../.claude/patterns/ui/fluent-v9-component-authoring.md) · [`.claude/patterns/ui/fluent-v9-react-version-boundaries.md`](../../.claude/patterns/ui/fluent-v9-react-version-boundaries.md)

### Related Projects

- **`record-header-and-notepad-r1`** — the source of every primitive R2 consumes. Read its [`notes/lessons-learned.md`](../record-header-and-notepad-r1/notes/lessons-learned.md) (12 lessons through v1.0.19) before touching the PCF build.
- **`smart-todo-r4/r5`** — SmartTodo code page the To Do icon launches.

---

## Deferrals & Issues

File via `/project-defer-issue-tracking` (alias `/defer`) — writes to BOTH `notes/defer-issues.md` and a GitHub Issue. Never only one.

Carried in from R1: **DEF-01** (sparkle refresh → BFF endpoint; absorbed by Insights Engine), **DEF-03** (VisualHost `CardChrome`), **DEF-04** (EventDetailSidePane `MemoSection`), **DEF-06** (`exports` migration — dropped from R2, standalone project when wanted), **DEF-08** (memo-repo promotion — trigger stays on DEF-04).

CLAUDE.md §11 applies: every entry must name a concrete behavior or contract that fails without the work. "Future flexibility" is not valid.

---

*Created 2026-08-21 during the R2 re-scope.*
