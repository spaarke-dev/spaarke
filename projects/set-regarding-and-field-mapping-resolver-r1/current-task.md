# Current Task State — set-regarding-and-field-mapping-resolver-r1

> **Last Updated**: 2026-07-03 (by context-handoff)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Project** | set-regarding-and-field-mapping-resolver-r1 |
| **Branch** | `work/set-regarding-and-field-mapping-resolver-r1` (worktree) |
| **Portfolio** | [Project #536](https://github.com/spaarke-dev/spaarke/issues/536) · Epic [#535 ENTITY FUNCTIONALITY](https://github.com/spaarke-dev/spaarke/issues/535) |
| **Tasks Completed** | 34 of ~36 (28 original + 8 out-of-plan iterations 034/035/036/037/038/039/041/042) |
| **Active Task** | none (SRFR-042 v1.3.5 deployed successfully) |
| **Status** | idle |
| **Next Action** | Owner hard-refreshes to verify link works on pre-loaded records; then dispatch **SRFR-084 UAT** (3 scenarios) + **SRFR-090 wrap-up** |

### Critical Context (1-3 sentences)

RegardingResolver PCF v1.3.5 is deployed to spaarkedev1. SRFR-042 fixed the pre-loaded-record hyperlink no-op: added `resolveClickTarget()` helper that derives `entityName` + `entityId` at click time via three-tier priority (selectedTarget → parse etn+id from `sprk_regardingrecordurl` Xrm.Page attr → fallback id + entityType hint). Additive fix — no manifest changes, no shared-lib changes. Build + test + deploy all green; 65 tests pass (47 pre-existing + 3 new SRFR-042 tests).

### Files Modified This Session (post-Wave-8 iteration cycle)

**PCF version bumps** (all committed + deployed to spaarkedev1 via `pac solution import`):
- v1.3.0 → v1.3.1 (SRFR-034 — UI polish: title uppercase, showVersionFooter, icon flip, refresh button, OOB styling, Row 2 name-cell removed, version bump)
- v1.3.1 → v1.3.2 (SRFR-035 — auto-refresh after selection, CREATE-mode gate protects presave bridge)
- v1.3.2 → v1.3.3 (SRFR-037 — title font-weight 400 → 600 to match OOB `.pa-hw`; root paddingTop 0 for reduced top spacing)
- v1.3.3 → v1.3.4 (SRFR-039 — Row 2 restored as 1fr 2fr grid; "Regarding Number" + "Regarding Name" labels 12px gray top-aligned; name cell as `<Text>` not `<Link>`)
- v1.3.4 → v1.3.5 (SRFR-042 — **IN-FLIGHT** — hyperlink onLoad fix via bound-field derivation + URL parse fallback)

**Form config iterations**:
- SRFR-036 — sprk_todo FormXml: fields + companion webresource `sprk_regardingrecordnumber_hyperlink.js` v1.0.0
- SRFR-038 — flipped both cells to visible=true (WRONG — owner had them hidden by intent)
- SRFR-041 — reverted SRFR-038 (both cells back to visible=false, wire preserved for future maker enable)

**Companion webresource** (deployed, still active but unused):
- `sprk_regardingrecordnumber_hyperlink.js` v1.0.0 — DOM-transforms OOB field to hyperlink

## Full State (Detailed)

### Wave-level completion status

| Wave | Status |
|---|---|
| 0 Discovery + data-fix | ✅ SRFR-001, 002 (surfaced D-1..D-11) |
| 1 Schema (10 targets + Matter) | ✅ SRFR-010 (surfaced D-12..D-15 including MCP underscore convention) |
| 2 Shared lib (Poly + FMH + interface) | ✅ SRFR-020, 021, 022, 023 (91 tests pass) |
| 3 RegardingResolver PCF | ✅ SRFR-030, 031, 032, 033 + iterative polish 034/035/037/039/**042** |
| 4 Presave webresource | ✅ SRFR-040 (v1.2.0 shipped) |
| 5 AssociationResolver PCF | ✅ SRFR-050, 051 (Path A exception), 052, 053 |
| 6 Field Mapping subsystem | ✅ SRFR-060 (MDA form), 061 (push webresource ADR-006 Path A), 062 (ribbons) |
| 7 Docs + audit | ✅ SRFR-070 (audit), 071 (ADR-024 amend main-session), 072 (idempotent) |
| 8 Deploy + UAT | ✅ 080/081/082/083 deployed; ⏳ 084 UAT pending owner verify |
| 9 Wrap-up | ⏳ 090 pending after UAT |

### In-flight background agent

- **agentId**: `a152406bc76c0c25c` (SRFR-042)
- **Task**: RegardingResolver v1.3.4 → v1.3.5; hyperlink onLoad fix via bound-field derivation
- **Approach given to agent**: priority-ordered (1) selectedTarget state (fresh selection wins) → (2) Xrm.Page attribute read for `sprk_regardingrecordtype` + `sprk_regardingrecordid` + catalog lookup for entity name → (3) URL parse fallback from `sprk_regardingrecordurl` `?etn=X&id=Y` querystring
- **Deploy**: agent will pack + import; verify via `pac solution list`
- **On notification**: main session commits, pushes, prompts owner to hard-refresh + test click on pre-loaded record

### Cumulative divergences resolved during project (15 D-series findings)

D-1 profile schema uses lookups (spec Appendix A rewritten) · D-2 sprk_mapping_type added · D-3 per-rule syncmode · D-4 3 catalog typos fixed · D-5 all catalog rows populated · D-6 Contact catalog → OOB `contact` · D-7 13 record types (later 12 after D-9 removed Billing Analysis) · D-8 3 sprk_ entities missing number fields → added · D-9 sprk_billinganalysis doesn't exist → catalog row deleted · D-10 sprk_communication uses sprk_regardingperson (deferred) · D-11 MCP underscore naming · D-12 Matter didn't have sprk_regardingrecordnumber → added · D-13 OOB entities (contact/account) get entity-prefix not sprk_ → convention-derived in resolver · D-14 MaxLength MAX not 100 (accepted) · D-15 IsSearchable not set (accepted)

### Known deferred issues

- **@spaarke/sdap-client missing module** — pre-existing; blocks full shared-lib `npm run build` but PCF `build:prod` unaffected (uses pre-compiled dist). Fix as separate project.
- **React 19 vs React 16 types mismatch** in `@spaarke/ui-components` — cast at seam in both RegardingResolver + AssociationResolver. Follow-on idea: `@spaarke/ui-components-react-types-alignment`.
- **Admin batch-cascade service** — deferred to `admin-cascade-batch-job-r1` (open follow-on Idea Issue during SRFR-090 wrap-up).

### Recovery commands

```bash
# Resume: check running agent status
# If SRFR-042 completed: read its output-file + commit
# If still running: wait for notification

# See current TASK-INDEX
cat projects/set-regarding-and-field-mapping-resolver-r1/tasks/TASK-INDEX.md

# See most recent commit
git log -1

# Portfolio state
gh issue view 536

# Deploy verification
pac solution list | grep RegardingResolver
```

### Latest committed state

- **Last commit**: `c3dba3339` — SRFR-039 v1.3.4 restore Name cell + SRFR-041 form revert
- **Portfolio**: Project #536 → Task Count 35, Tasks Completed 33 (will be 34 after SRFR-042 commits)
- **Owner-visible on spaarkedev1**: RegardingResolver v1.3.4 renders correctly per screenshot; hyperlink click on pre-loaded record is the only remaining bug (fix in-flight)
