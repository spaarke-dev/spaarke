# Current Task State — record-header-and-notepad-r2

> **Last Updated**: 2026-08-25 (design verified against live schema; spec generated)
> **Recovery**: Read "Quick Recovery" first, then [`CLAUDE.md`](CLAUDE.md) — especially its "Read this before anything else" section.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | none — implementation not started |
| **Phase** | 1 — [`spec.md`](spec.md) generated; ready for `/project-pipeline` |
| **Status** | not started |
| **Next Action** | `/project-pipeline` |
| **Blocked by** | Nothing. All 10 owner decisions closed; discovery closed against `spaarkedev1`. |

### 🔴 Two live production breakages found during verification (spec FR-23)

Not caused by this project, but discovered by it and now in R2's scope to fix:

1. **The shipped `MatterHeaderPcf` v1.0.20 header does not load at all.** `MatterHeaderView.tsx:83` selects `sprk_mattersummary`, a column that was deleted during the 2026-08-25 summary-field standardization → HTTP 400 → the whole header fails, not just the sparkle. Consider a v1.0.21 hotfix if R2 will not ship soon.
2. **`sprk_aitopicregistry` row "Matter Summary" targets the same deleted column** (`sprk_targetfield=sprk_mattersummary`, enabled). The BFF OutputRouter `work_product` leg writes to nothing. Dataverse **data** fix → `sprk_recordsummary`.

### Scope changes since the 2026-08-21 re-scope

- **6 entities**, not 5 — `sprk_agreement` added 2026-08-25
- **Summary field standardized to `sprk_recordsummary`** everywhere (avoids collision with Microsoft OOB "AI summary"). Columns already created by the owner, so R2 does **no** schema work
- **Lookups use the OOB `Xrm.Utility.lookupObjects` picker**, retiring the custom inline type-ahead — this deletes the custom OData search builder rather than hoisting it
- **One toolbar-map change**: `sprk_agreement` added to both `SUPPORTED_TODO_PARENTS` and `SUPPORTED_MEMO_PARENTS`

### What just happened (2026-08-21)

The R2 design was **re-scoped** from "four cloned per-entity PCFs" to "ONE configuration-driven `Spaarke.Records.RecordHeader` control." Owner decisions in the same session:

- Project + Work Assignment ship first; **Invoice is explicitly required** (it forces the currency + date renderer work)
- Config = **JSON on a manifest property**, not a Dataverse config table — few instances ever, unlike VisualHost
- DEF-06 (`exports` migration) and DEF-08 (memo-repo promotion) both **dropped** from R2 scope

### Files changed on this branch (uncommitted at scaffold time → committed in the setup commit)

- `projects/record-header-and-notepad-r2/design.md` — full rewrite
- `.claude/patterns/ui/record-header-composition.md` — corrected; it previously instructed agents to author a new per-entity PCF
- `projects/record-header-and-notepad-r2/README.md`, `CLAUDE.md`, `current-task.md`, `notes/discovery-checklist.md` — new

### Not yet generated

`spec.md` (`/design-to-spec`) · `plan.md` (`/project-pipeline`) · `tasks/TASK-INDEX.md` (`task-create`)

---

## Decisions Log

| Date | Decision |
|------|----------|
| 2026-08-21 | One configurable control replaces four cloned PCFs |
| 2026-08-21 | JSON-on-manifest config; `sprk_headerconfiguration` table explicitly rejected |
| 2026-08-21 | Rollout: Project + Work Assignment → Invoice + Event → Matter last |
| 2026-08-21 | DEF-06 + DEF-08 dropped from R2 |
| 2026-08-21 | New control identity `Spaarke.Records.RecordHeader`; Matter form re-bound once |
| 2026-08-22 | Option B reaffirmed after the corrected trade; forms ship inside a transported solution; metadata reuses `IDataverseClient` (extended with `targets`) |
| 2026-08-24 | JSON-only config confirmed; retire `MatterHeaderPcf` on delivery; §9 rewritten from live schema; per-entity layouts confirmed; `BooleanField` kept; skeleton takes a `columns` prop; em-dash `''` everywhere (required marker NOT adopted) |
| 2026-08-25 | Summary field standardized to **`sprk_recordsummary`**; lookups use the **OOB `lookupObjects` picker**; **`sprk_agreement`** added as a sixth entity |

---

## Open Questions

| # | Question | Owner | Resolve by |
|---|---|---|---|
| OQ-1 | Do the four entities' field lists in design.md §9 match real schema? All are `TBD-CONFIRM`. | discovery | before `/design-to-spec` |
| OQ-2 | Does a populated summary field exist per entity? R1 shipped a silently-empty sparkle for weeks on Matter — assume nothing. | discovery | before `/design-to-spec` |
| OQ-3 | Are `sprk_mattertype_ref`'s primary id/name attributes `sprk_mattertype_refid` / `sprk_mattertypename`? Determines whether `LOOKUP_META` fully disappears or an escape hatch is needed. | discovery | before `/design-to-spec` |
| OQ-4 | Register on Project #2 portfolio board? | owner | before `/project-pipeline` |

---

*Reset this file at each task transition per root CLAUDE.md §7. History lives in `tasks/TASK-INDEX.md`.*
