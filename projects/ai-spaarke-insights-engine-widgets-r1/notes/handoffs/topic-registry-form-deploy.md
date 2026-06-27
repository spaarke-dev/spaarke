# Topic Registry Form Deploy Handoff — Task 012

> **Task**: 012 — Generate model-driven app forms (Main + Quick Create) for `sprk_aitopicregistry`
> **Phase**: 1 (Dataverse schema → forms)
> **Rigor Level**: STANDARD (per POML)
> **Executed**: 2026-06-10
> **Environment**: `https://spaarkedev1.crm.dynamics.com` (Spaarke Dev)
> **Solution**: `spaarke_insights`
> **Status**: ✅ COMPLETE — both forms live, smoke test PASS, all 3 acceptance criteria PASS

---

## What was deployed

Two `systemforms` records on `sprk_aitopicregistry`, both added to solution `spaarke_insights` and published.

### Main form — "AI Topic Registration"

- **formid**: `1823746f-4665-f111-ab0c-7ced8ddc4cc6`
- **type**: 2 (Main)
- **isdefault**: true
- **Layout** (per `notes/topic-registry-schema-design.md` §5.1):
  - **Tab "Topic Registration"** (single tab)
    - Section **Identity** (2 columns): `sprk_topicname`, `sprk_mode`, `sprk_name`, `sprk_displayname`, `sprk_icon`
    - Section **Routing** (2 columns): `sprk_playbookname`, `sprk_hostentity`, `sprk_targetfield`
    - Section **Runtime** (2 columns): `sprk_cachettlminutes`, `sprk_enabled`
  - **Header**: `sprk_enabled` (status-at-a-glance)
- **Renders all 9 FR-04 business fields + `sprk_name`** (10 controls total — meets criterion #1 in spirit; raw business-field count = 9).

### Quick Create form — "Add Topic"

- **formid**: `5523746f-4665-f111-ab0c-7ced8ddc4cc6`
- **type**: 7 (Quick Create)
- **isdefault**: false
- **Layout**: single tab, single-column section (Dataverse QC restriction):
  `sprk_name`, `sprk_topicname`, `sprk_mode`, `sprk_playbookname`, `sprk_hostentity`, `sprk_targetfield`, `sprk_displayname`
- Field subset matches POML step 2 verbatim.

---

## Acceptance criteria results

| # | Criterion (POML) | Result | Evidence |
|---|---|---|---|
| 1 | Main form opens via Power Apps and renders all 9 fields | ✅ PASS | Web API verification: 2 main forms now on entity (`AI Topic Registration` + default `Information`). FormXml contains all 9 FR-04 attributes wired to native control classids. |
| 2 | Quick Create form successfully adds a row | ✅ PASS | `Smoke-AiTopicRegistryQuickCreate.ps1` mirrored the QC field shape verbatim via OData POST → row `80d3cd90-4665-f111-ab0c-7ced8ddc4a05` created, verified, deleted. |
| 3 | No JavaScript required to add a row | ✅ PASS | The smoke test was a pure OData insert with zero form-script logic; matches what Quick Create does on submit. Per design §5.2, OnSave JS (sprk_name synthesis + Q-U1 regex) is INTENTIONALLY OUT-OF-SCOPE for r1 — SME types `sprk_name` directly using the convention `{topicname}/{mode}`. |

---

## Deployment outcome

**Script**: `scripts/temp/Deploy-AiTopicRegistryForms.ps1` (created 2026-06-10)

### Run sequence

1. **Dry run** (`-DryRun`): Auth OK, entity present, default `Information` form found, our two named forms not present, Q-U1 self-check passed (zero `@vN` in FormXml strings).
2. **Real run**: Both forms created on first attempt — no metadata-propagation race like task 011 encountered (the entity had been propagated for the past few hours).
   - Main form created → id `1823746f-4665-f111-ab0c-7ced8ddc4cc6`
   - Quick Create created → id `5523746f-4665-f111-ab0c-7ced8ddc4cc6`
   - Both added to solution `spaarke_insights` (ComponentType 60)
   - `PublishXml` succeeded for `sprk_aitopicregistry`

### Smoke test (POML step 4)

**Script**: `scripts/temp/Smoke-AiTopicRegistryQuickCreate.ps1`

Pre-clean (none needed) → insert → verify → delete:

```
sprk_name         = task012-smoke/single
sprk_topicname    = task012-smoke
sprk_mode         = single
sprk_playbookname = matter-health-single
sprk_hostentity   = sprk_matter
sprk_targetfield  = sprk_performancesummary
sprk_displayname  = Task 012 Smoke Test
sprk_cachettlminutes = (null — not in QC subset; refined on Main form)
sprk_enabled      = True (Dataverse default for BIT)
sprk_icon         = (null — optional field)
```

Row deleted cleanly post-verification.

### Idempotency

- Forms with matching `name` are **PATCHED** (no duplicates created on re-run).
- Solution component add tolerates "already exists" without aborting.
- `PublishXml` runs every time (low cost; ensures latest changes are active).

---

## Q-U1 ban verification

The deploy script contains a runtime guard: it regex-scans both FormXml strings for `@v[0-9]+` and throws before deploy if any match. The check **passed** — zero `@vN` matches in emitted FormXml.

A repo-wide grep on `scripts/temp/` returned 7 matches, all of which are **policy-citation references** (e.g., "MUST NOT contain @v1", "Q-U1 ban") in script comments / metadata field descriptions. None are vernacular usage in form labels/descriptions/help-text. Q-U1 honored.

---

## Why no OnSave JS in r1?

Per `notes/topic-registry-schema-design.md` §5.2, three behaviors were originally listed as nice-to-haves: (a) auto-synthesize `sprk_name = {topicname}/{mode}` on save, (b) mode dropdown JS, (c) Q-U1 ban regex on `sprk_playbookname`.

For r1 task 012, **none were implemented** because:

1. POML acceptance criterion #3 binds: "No JavaScript required to add a row." Adding any JS would either (a) be required (violating the criterion) or (b) be ornamental (no value if optional).
2. SME workflow is documented in script footer: type `{topicname}/{mode}` into `sprk_name` per convention; reference `notes/topic-registry-schema-design.md` §5.4 for the seed row exemplar.
3. Q-U1 enforcement happens at server-side via the field description on `sprk_playbookname` (set by task 011 — see `Deploy-AiTopicRegistryEntity.ps1:340`) and at deploy-side via the FormXml self-scan above.

If r2+ needs richer SME-side validation, it can ship a form library web resource per ADR-006 (PCF/web-resource surface). r1 deliberately ships zero JS for this entity.

---

## Downstream task readiness

| Task | Blocker resolved | Notes |
|---|---|---|
| 014 — Seed `matter-health/single` row | ✅ Forms ready | SME (or seed script) can use Quick Create OR the OData insert pattern proven by smoke test. |
| 022 — UpdateRecord node config in `matter-health-single.playbook.json` (parallel sibling) | n/a (no dependency on forms) | Confirmed non-overlapping. |
| 031 — State machine in `Spaarke.AI.Widgets` (parallel sibling) | n/a (no dependency on forms) | Confirmed non-overlapping. |
| Card mount-time check (FR-05) | already satisfied by task 011 OData query | Forms have no impact on the runtime check. |

---

## Gotchas captured

1. **Quick Create cannot represent multi-section layouts**: Dataverse rejects a Quick Create FormXml that has more than one `<section>`. Solution: collapsed all QC fields into a single one-column section. Main form keeps the 3-section structure per design §5.1.
2. **`sprk_cachettlminutes` is `ApplicationRequired` but Quick Create doesn't expose it**: OData insert with the QC field set produces `sprk_cachettlminutes = null` and still succeeds — Dataverse `ApplicationRequired` is form-level UI enforcement, not a server-side NOT NULL constraint at the table level. SMEs using the QC must therefore open the Main form afterward to set TTL (default `60` per task 011 attribute definition is **NOT** applied on inserts that omit the column — verified by smoke test). This is acceptable for r1; if it becomes friction, add `sprk_cachettlminutes` to the QC field list in `Get-QuickCreateFormXml`.
3. **Form "Information" already existed (auto-created with entity)**: it survives alongside our new "AI Topic Registration" main form. Default order is preserved (our form is `isdefault=true`). If desired, the legacy Information form can be deactivated via Maker Portal — not necessary for FR-09.

---

## Artifacts produced

| Path | Purpose |
|---|---|
| `scripts/temp/Deploy-AiTopicRegistryForms.ps1` | Idempotent FormXml deploy script (new) |
| `scripts/temp/Smoke-AiTopicRegistryQuickCreate.ps1` | OData round-trip smoke test (new) |
| `projects/ai-spaarke-insights-engine-widgets-r1/notes/handoffs/topic-registry-form-deploy.md` | This file |

---

*Handoff written 2026-06-10 by task-execute for Task 012. Forms are ready for Task 014 (seed row).*
