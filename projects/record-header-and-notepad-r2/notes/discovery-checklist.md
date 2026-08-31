# Discovery Checklist — ✅ CLOSED 2026-08-24

> **Created**: 2026-08-21 during the R2 re-scope. **Closed**: 2026-08-24.
> **Why it existed**: [`design.md` §9](../design.md) inherited its field lists **unverified** from the withdrawn 2026-07-05 seed. Acceptance criteria cannot be locked against guessed schema.
> **How it closed**: §C from code (2026-08-22); §A / §B / §D from **live Dataverse metadata** against `spaarkedev1` (2026-08-24) via the Web API (`az account get-access-token` → `EntityDefinitions`), Dataverse MCP not being loaded in that session. Verified results are folded into `design.md` §9 — **that is now the authoritative copy**; this file is the audit trail.

---

## Why this was not optional

R1 shipped `MatterHeaderPcf` with a sparkle popover that was **silently empty on every Matter record in production** for multiple releases. The cause: the design assumed `sprk_recordsummary`, but that field is written on **zero** Matter records — the real narrative summaries live in `sprk_mattersummary`.

**Live confirmation 2026-08-24**: `sprk_recordsummary` = **0 populated of 55** Matters. `sprk_mattersummary` = **1 of 55**. The R1 lesson holds, and the trap was real.

---

## A. Per-entity schema — ✅ CLOSED (live-verified)

Full verified field lists, option-set values and attribute types are in [`design.md` §9](../design.md). Headline results:

| Question | Answer |
|---|---|
| Primary name attributes | `sprk_project` → **`sprk_projectnumber`** (⚠️ not `sprk_projectname`, though that also exists) · `sprk_workassignment` → `sprk_name` · `sprk_invoice` → `sprk_name` · `sprk_event` → **`sprk_eventname`** |
| Custom status option sets | **None on Project or Work Assignment** — `statuscode` only. Invoice has `sprk_invoicestatus` (2 options) + `sprk_visibilitystate` (6). Event has `sprk_eventstatus` (0–7) |
| Fields drafted in the seed that **do not exist** | Project start date · Project target-end date · WA start date · WA estimated hours · Invoice due date · Event `sprk_location` |
| Event `DateAndTime` pair | **`sprk_plannedstart` / `sprk_plannedend`** (plus `sprk_actualstart` / `sprk_actualend`). `scheduledstart` / `scheduledend` / `sprk_startdate` **do not exist** |
| Summary fields + population | Matter `sprk_mattersummary` 1/55 · Matter `sprk_recordsummary` 0/55 · Invoice `sprk_aisummary` 0/10 · Project `sprk_financialsummary`/`sprk_performancesummary`/`sprk_tasksummary` 0/18 each · **WA and Event have no summary column at all** |
| Required levels (`*` marker) | Primary name + primary id on all four; `ownerid`; `statecode`; plus `sprk_regardingrecordtype` and `sprk_extractionstatus` on Invoice |
| Main forms | Live GUIDs in `design.md` §9. Each entity also has a legacy **"Information"** form — do not bind it. Event has 10 forms; bind only **"Event main form"** |

**Owner decision folded in**: sparkle and summary fields are **kept** — to be populated by a separate project. Visibility keys on *attribute existence*, not population. WA and Event need summary columns created by that project before they can show a sparkle.

---

## B. Lookup metadata derivation — ✅ CLOSED (live-verified)

R1's hard-coded `LOOKUP_META` **can be deleted**. Both assumptions held exactly:

| Lookup | Target | PrimaryIdAttribute | PrimaryNameAttribute |
|---|---|---|---|
| `sprk_matter.sprk_mattertype` | `sprk_mattertype_ref` | `sprk_mattertype_refid` ✅ | `sprk_mattertypename` ✅ |
| `sprk_matter.sprk_practicearea` | `sprk_practicearea_ref` | `sprk_practicearea_refid` ✅ | `sprk_practiceareaname` ✅ |

⇒ The optional `fields[].lookup: { entity, idField, nameField }` escape hatch is **not needed**; keep it out of the v1.0 schema.

**And the convention is confirmed non-uniform** — `sprk_projecttype_ref` → `sprk_name`, `sprk_eventtype_ref` → `sprk_name`. A hard-coded naming rule would break on two of the four rollout entities, which is exactly why §5.4 reads `primaryNameAttribute` from metadata.

> **Mechanism note**: design §5.4 routes this through [`IDataverseClient.retrieveEntityMetadata`](../../../src/client/shared/Spaarke.UI.Components/src/services/IDataverseClient.ts#L171) extended with `targets?: string[]` (owner decision D-5), **not** raw-`fetch` `EntityDefinitions/ManyToOneRelationships`.

---

## C. Toolbar support — ✅ CLOSED 2026-08-22 (code-verified, no MCP needed)

Read directly from [`toolbarLaunchDefaults.ts`](../../../src/client/shared/Spaarke.UI.Components/src/hooks/toolbarLaunchDefaults.ts):

- **`SUPPORTED_MEMO_PARENTS` — 6 entries** (`:105-112`): `sprk_matter`, `sprk_project`, `sprk_event`, `sprk_invoice`, `sprk_budget`, `sprk_workassignment` → their `sprk_regarding*` lookups
- **`SUPPORTED_TODO_PARENTS` — 11 entries** (`:150-162`): the same six **plus** `sprk_analysis`, `sprk_communication`, `contact` (OOB, no prefix), `sprk_document`, `sprk_organization`
- All five rollout entities present in **both** maps → **no toolbar map changes needed**
- `buildMemoFilterForParent` (`:130-134`) and `buildTodoFilterForParent` (`:178-182`) both return `null` for unsupported parents, so §6.4's auto-hide is a small hook change, not new logic

---

## D. Matter parity baseline (for the §8 migration)

Matter migrates **last** and is the strongest regression test. Capture the baseline **before** any code changes:

- [x] Matter main form GUID: `4fa382f2-c273-f011-b4cb-6045bdd6a665` (live-verified 2026-08-24; the "Information" form `071c5a8e-…` is the legacy one)
- [x] Lookup metadata for the parity `layoutJson` — see §B
- [ ] Screenshot `MatterHeaderPcf` v1.0.20 on the Matter main form (light **and** dark)
- [ ] Record the exact five-field layout + spans so the equivalent `layoutJson` can be written against it
- [ ] Confirm the deployed version in the footer is v1.0.20
- [ ] Note which field `boundField` is bound to (expected `sprk_matternumber`) — needed to re-bind

---

## E. `layoutJson` mechanism spike (design.md §5.1.1) — still to run, no longer blocking

**Reframed 2026-08-22, downgraded 2026-08-24** — this is an *ergonomics* check, not an existence check. `of-type="SingleLine.Text"` + `usage="input"` is already proven in this control (`title`), and because a static input value lives in form XML rather than a Dataverse column, the 4,000-char column limit does not apply. The §5.2 example layout minifies to **~330 characters**. There is always a working path; the spike only decides which editor makers get, and it cannot change the design — only the manifest `of-type`.

- [ ] In the **classic** form designer (R1 found the modern designer unreliable for header-region PCF binding), does a static `Multiple` input property present a usable multi-line editor, or a single-line box?
- [ ] Does it accept and round-trip a ~1 KB JSON paste (quotes XML-escaped into `customizations.xml`) without truncation?
- [ ] Does the value survive a solution export → import cycle? **(The one that actually matters — silent truncation on export is the dangerous failure: the header quietly falls back to derived defaults and nobody knows why.)**

**If `Multiple` gives a bad editor**: fall back to `of-type="SingleLine.Text"` with minified JSON. Resolver, schema and every renderer are unchanged. **If both truncate**: reinstate the config-record tier, which the resolver's tier design already accommodates.

---

## F. Schema-drift defects — **OUT of R2 scope; documented separately** (owner direction 2026-08-24)

Seven non-existent columns across six files. Captured as three standalone issue documents grouped by record / component type, for evaluation as focused fix projects:

📄 **[`issues/README.md`](issues/README.md)** → [Event](issues/ISSUE-event-schema-drift.md) · [Daily Briefing](issues/ISSUE-daily-briefing-schema-drift.md) · [Work Assignment](issues/ISSUE-work-assignment-schema-drift.md)

The table below is the raw discovery record.

**Empirically confirmed by executing the shipped queries against `spaarkedev1`:**

```
EVENT_FULL_SELECT_FIELDS (as shipped)   -> HTTP 400
  "Could not find a property named 'scheduledstart' on type 'Microsoft.Dynamics.CRM.sprk_event'."
same list minus the 3 bad columns       -> HTTP 200

sprk_projects.$select=sprk_description          -> HTTP 400   (real: sprk_projectdescription)
sprk_events.$select=sprk_eventdescription       -> HTTP 400   (real: sprk_description)
sprk_documents.$select=sprk_documentdescription -> HTTP 200   (fine)
sprk_matters.$select=sprk_matterdescription     -> HTTP 200   (fine)
```

| ID | File | Bad columns | Fix |
|---|---|---|---|
| SD-1 | `EventDetailSidePane/src/types/EventRecord.ts` (`:29,31,33`, `:186-188`) | `scheduledstart`, `scheduledend`, `sprk_location` | → `sprk_plannedstart` / `sprk_plannedend`; drop `sprk_location` |
| SD-2 | `EventDetailSidePane/src/services/eventService.ts` (`:278-280`) | same | same |
| SD-3 | `Spaarke.UI.Components/src/services/EventTypeService.ts` (`:47-48,51`, `:167-169`) | same | same |
| SD-4 | `DailyBriefingCollector.cs:408` | Project `sprk_description` | → `sprk_projectdescription` |
| SD-5 | `DailyBriefingCollector.cs:424` | Event `sprk_eventdescription` | → `sprk_description` |
| SD-6 | `WorkAssignmentEndpoints.cs:79,82` | `sprk_matterid`, `sprk_duedate` | → `sprk_regardingmatter`, `sprk_responseduedate` |
| SD-7 | `CreateNotificationNodeExecutor.cs:145,789` | `{{item.scheduledend}}` in author-facing text | → `{{item.sprk_plannedend}}` |

**Open questions** (carried into the issue docs): (1) `sprk_location` has no replacement column — drop it from code, or create the column? (2) SD-6 should probably also populate the ADR-024 resolver fields alongside `sprk_regardingmatter`, which may need a server-side resolver helper that does not yet exist.

**Constraint note**: SD-1/SD-2 touch `src/solutions/EventDetailSidePane/**`, which stays behind R2's MUST NOT. A separate fix project would need that lifted for those two files — which would not reopen DEF-04.

---

## Results

**2026-08-24 — discovery COMPLETE.** All blocking items closed. Verified field lists, option-set values, population counts, main-form GUIDs and lookup metadata are folded into [`design.md` §9](../design.md). `design.md` is ready for `/design-to-spec`; the §E spike runs as the first implementation task.

*Environment: `spaarkedev1`. Record counts at verification: Matter 55 · Event 72 · Work Assignment 22 · Project 18 · Invoice 10.*
