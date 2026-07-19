# 023 — VisualHost Summary Count MetricCard `sprk_chartdefinition` Config (FR-05, optional)

> **Task**: R2-023 (W2, FR-05) — an optional, config-only count/unread MetricCard on the record form,
> drill-through to Surface 1 (the regarding-mode threads view).
> **Rigor**: STANDARD (dataverse, config). **Date**: 2026-07-19. **Environment**: spaarkedev1.
> **Depends on**: task 010 (`by-regarding` endpoint — not consumed here; VisualHost fetches client-side,
> independent of the BFF) and, informationally, task 001's confirmation that `sprk_communication`'s 11
> `sprk_regarding{...}` lookups are **already live in production** (written server-side by
> `RegardingFieldMap.cs` / the association engine — pre-dates R2, no schema gate).
> **Deliverable**: config-only. No code, no new VisualHost visual type, no BFF change.

---

## 🚨 LIVE APPLY DEFERRED (Dataverse MCP unavailable this session)

**Dataverse MCP was unavailable this session** (same gate as tasks 001–003, 040–041). This document is the
**authored chart-definition specification** — the deliverable is complete and ready to apply, but creating
the live `sprk_chartdefinition` record and placing the VisualHost control on the form are **deferred to the
owner**, mirroring the R1/R2 build-and-defer-live pattern used throughout this project.

> **LIVE APPLY DEFERRED: the owner creates one `sprk_chartdefinition` record with the exact field values in
> §2 below**, adds a VisualHost control to the `sprk_matter` main form bound to that record (§4), and
> confirms the count renders + the drill-through opens (§5 verification checklist). No code deploy is
> required — this is a Dataverse record + form-customization change only.

Authoring is unblocked; only the live Dataverse write + form placement + visual verification are gated.

---

## 1. Decision: ship the card, scoped to one exemplar entity (not all 11)

Unlike task 022 (which places the regarding-mode Timeline PCF on **all 11** regarding-family forms per
spec Q5), FR-05 is explicitly optional and scoped as **"one optional config record"** (task constraint:
*"Scope: one optional config record + drill-through wiring"*). This document authors **one exemplar
record for `sprk_matter`** — the flagship/most complex regarding entity, and the same entity already
carrying the precedent config-only MetricCards this task is asked to mirror (Matter Financial Metrics
Scorecard, Matter KPI Scorecard, etc. — see
`projects/spaarke-matter-ui-enhancement-r1/notes/spikes/visualhost-chart-def-inventory.md`).

**§10 generalization note**: if the owner wants the card on other/all of the 11 regarding-family forms,
§6 below gives the exact per-entity `sprk_contextfieldname` substitution table — each additional entity is
one more config record (context field swapped) + one more form placement. This is NOT authored as a
requirement here because FR-05's acceptance criteria (spec.md) do not mandate the 11-entity breadth that
FR-04/task-022 mandates — it is genuinely one optional card, not an 11-form matrix.

---

## 2. The `sprk_chartdefinition` record — exact field values

| Field (logical name) | Value | Notes |
|---|---|---|
| `sprk_name` | `Communications` | Card label rendered in the UI (single-card mode shows this as `metricLabel` when no groupBy). |
| `sprk_visualtype` | `100000000` (MetricCard) | Existing visual type — no new type added. |
| `sprk_entitylogicalname` | `sprk_communication` | The **message**-grain entity (not the thread entity) — see §3 for why. |
| `sprk_groupbyfield` | *(leave empty)* | Single-card mode — one number, not a matrix. |
| `sprk_aggregationtype` | `100000000` (Count) | `AggregationType.Count` (`src/client/shared/Spaarke.Visuals/src/types/index.ts:75`). |
| `sprk_aggregationfield` | *(leave empty)* | Not needed for Count. |
| `sprk_baseviewid` | *(leave empty)* | No saved view needed — falls back to VisualHost's **Priority 4 Direct Entity Query**, which still honors the context filter (VISUALHOST-SETUP-GUIDE.md § Data Querying Options — "Direct Entity Query: Filter appended as a FetchXML condition in the fallback query"). Mirrors the existing "Due Date Count Card" / "Number of Documents Card" precedent (no `sprk_baseviewid`, no `sprk_optionsjson`). |
| `sprk_fetchxmlquery` | *(leave empty)* | Not needed — Direct Entity Query + context filter is sufficient for a plain count. |
| `sprk_contextfieldname` | `_sprk_regardingmatter_value` | The **existing, already-live** Lookup on `sprk_communication` pointing at `sprk_matter` (written server-side by `RegardingFieldMap.cs`; confirmed live in production by the task-001 audit — no schema dependency, no deferred-apply gate for this field). WebAPI lookup-reference format (`_..._value`) per VisualHost convention. |
| `sprk_optionsjson` | *(leave empty)* | No matrix/field-pivot/color config needed for a single count card. |
| `sprk_valueformat` | *(leave empty — defaults to Short Number, `100000000`)* | Mirrors the "Due Date Count Card" / "Number of Documents Card" precedent (no explicit value format on simple count cards). |
| `sprk_colorsource` | *(leave empty — defaults to None)* | No per-value coloring needed for a single count. |
| `sprk_metriccardshape` | *(leave empty — defaults to Horizontal Rectangle)* | Standard shape for a small at-a-glance card. |
| `sprk_onclickaction` | *(leave empty / None, `100000000`)* | Not using a click-action — the **expand icon** (see §4) is the drill-through affordance, not a card-body click. |
| `sprk_drillthroughtarget` | `sprk_communicationthread` | Entity logical name (NOT a `.html` web resource) — opens a Dataverse entity-list **dialog** of threads via `handleExpandClick`'s `pageType: 'entitylist'` branch. See §3 for why the thread entity (not the message entity) is the safe drill target. |
| `sprk_drillthroughviews` | *(leave empty)* | No view allowlist curated yet — all active thread views show in the dialog's view-switcher. Owner may curate later (same optional mechanism as task 041 §4's `sprk_drillthroughviews` note). |
| `sprk_createwizardenabled` | `No` / unset | Not applicable — this card has no "+" create affordance. |

**GUID**: assigned by Dataverse at creation. Record the resulting `sprk_chartdefinitionid` in
`current-task.md` / the PR once the owner creates it.

---

## 3. Two data-safety decisions (both hold the T-1 line)

### 3a. The COUNT query is safe by construction (T-1 / NFR-03)

The card's data source is a **Count aggregate** over `sprk_communication` — VisualHost's
`DataAggregationService` returns a single number (`chartData.totalRecords`); it selects **no content
columns** (no `sprk_subject`, no body/summary, no sender/recipient fields). The client-side fetch honors
Dataverse RLS (a user who cannot see a given `sprk_communication` row via sharing/security-role
already gets it excluded from the count) but does **not** honor `sprk_isinternalonly` — per spec's own
resolution, **"A count is safe (aggregate, no content leak); content is not."** This card renders `12` (a
number), never a row, a subject, or a sender. Acceptance criterion "count-only, T-1/NFR-03 honored" is met
by the query shape itself, not by a runtime guard.

**"Unread" was scoped out** — `sprk_communication` / `sprk_communicationthread` carry **no read/unread
tracking field** today (grepped; none exists in the R1/R2 schema). Shipping a second aggregate for
"unread" would require a new schema field (a real schema gap, not a config gap) — out of scope for this
config-only task. The card is **count-only** (total message count for the record), consistent with the
literal FR-05 acceptance text ("shows count" — no unread requirement in the acceptance criteria, only in
the illustrative title).

### 3b. The drill-through target is the THREAD entity, not the message entity (a deliberate, documented choice)

VisualHost's only click affordance for a single (non-matrix) MetricCard is the **expand icon** in
`CardChrome`, wired to `handleExpandClick` (`sprk_drillthroughtarget`). Reading `VisualHostRoot.tsx` and
`ChartRenderer.tsx` closely: `handleViewListClick` (the "switch to a tab on the current form" mechanism,
`sprk_viewlisttabname`) exists in `VisualHostRoot.tsx` but is **only wired into `ChartRenderer`'s
`DueDateCardList` case** (`onViewListClick` prop, `ChartRenderer.tsx` VT.DueDateCardList branch) — the
`MetricCard`/`ReportCardMetric` case does not consume it. Since Surface 1 (the regarding-mode Timeline) is
placed **directly on the same record form** (task 022, a form section — not a separate page), there is no
code-free way today to make the count card's expand icon "jump to that section" without adding
`onViewListClick` wiring to the MetricCard case in `ChartRenderer.tsx` — which would be a **code change**,
forbidden by this task's config-only scope.

Given that constraint, `sprk_drillthroughtarget` is set to the **entity logical name** `sprk_communicationthread`
(not a web resource, not `sprk_communication`). This opens a native Dataverse entity-list **dialog** via
`handleExpandClick`'s non-web-resource branch (`pageType: 'entitylist', entityName: 'sprk_communicationthread'`),
context-filtered once task 002's thread-side regarding lookups are live (see §6). Two entities were
considered and rejected in favor of the thread entity:

- **`sprk_communication` (rejected)** — its columns are message-grain: subject/summary, sender, recipient(s),
  direction, channel. An entity-list dialog of this entity would show **row-level message metadata**
  (subject lines, sender names) via the same RLS-honors/`internal-only`-does-not client-fetch path T-1
  warns about — reintroducing exactly the content-adjacent exposure this task exists to avoid, just via
  `Xrm.Navigation.navigateTo` instead of VisualHost's own React tree.
- **The standalone `sprk_communicationspage` Code Page (rejected)** — task 040 built this page as
  **global, with NO `parentContext`** (Owner Q-B: no per-record filtering by design). Pointing the
  drill-through there would open an **unfiltered, all-communications** grid — not "this record's threads,"
  failing the FR-05 acceptance ("drill-through targets Surface 1") outright.
- **`sprk_communicationthread` (chosen)** — the thread entity's schema (`sprk_name`/topic,
  `sprk_threadtype`, `sprk_privacystate`, denormalized regarding fields, plus task-002's markers) carries
  **no message body, subject-per-message, sender, or recipient columns** — those live exclusively on the
  child `sprk_communication` entity. A thread-grain list shows conversation *labels* (closer to a folder/
  subject-line-once-per-thread), not per-message content rows. This is the closest code-free approximation
  of "Surface 1: the regarding-mode Timeline / **threads view**" (the task's own alternate framing, spec
  Assumptions), and does not introduce a new class of exposure beyond what a thread's `sprk_name` already
  represents.

This is a **Path C (comply)** resolution under CLAUDE.md §6.5: the ideal UX (same-form tab-jump) is not
achievable config-only under the current `ChartRenderer` dispatch; the fallback (thread-entity dialog) was
chosen specifically because it is the config-only option that does **not** reintroduce message-content
exposure, not because it is a perfect substitute for Surface 1. **This is flagged for reviewer awareness,
not silently decided** — see §7 for the alternative (`onViewListClick` wiring for MetricCard) if the owner
wants an exact same-form jump in a future code-touching task.

---

## 4. Form placement (owner action, `sprk_matter` main form)

1. Import/confirm the VisualHost control is already on the target solution (it is — VisualHost ships across
   the Matter form today for the existing Financial/KPI cards).
2. Add a new VisualHost control instance to a small section on the Matter main form (e.g., an "At a Glance" /
   header summary area — NOT the same section as task 022's full regarding-mode Timeline PCF, which is a
   larger dedicated tab/section for the grouped view).
3. Set PCF properties:

   | Property | Value |
   |---|---|
   | `chartDefinitionId` | the GUID of the record created in §2 (or bind `chartDefinition` lookup if the form uses that pattern) |
   | `contextFieldName` | `_sprk_regardingmatter_value` — **must match** the chart definition's `sprk_contextfieldname` exactly (VISUALHOST-SETUP-GUIDE.md § Required Configuration for Context Filtering) |
   | `showToolbar` | Yes (so the expand icon renders — required for the drill-through) |
   | `enableDrillThrough` | Yes |
   | `width` / `justification` | small single-card sizing, left-justified is fine |

4. Publish the form.

---

## 5. Verification checklist (owner, post-apply)

- [ ] Open a Matter record with ≥1 communication regarding it; confirm the card shows a whole number (the
      message count) — not "0" for a Matter known to have messages, not an error state.
- [ ] Confirm the card renders **no** message rows, subjects, senders, or bodies — number + label only.
- [ ] Click the expand icon; confirm a Dataverse entity-list dialog opens for `sprk_communicationthread`
      (thread rows — name/topic, type, dates), NOT a message-level list.
- [ ] Confirm the dialog is **context-filtered** to this Matter's threads once task 002's thread-side
      `sprk_regardingmatter` lookup is live (see §6 — until then, the dialog may show unfiltered threads;
      RLS still governs visibility, so this is a UX gap, not a security gap).
- [ ] Dark mode: card renders correctly (Fluent v9 tokens) — standard MetricCard behavior, unchanged.

---

## 6. Per-entity substitution table (if the owner replicates to other/all 11 forms)

If the card is adopted beyond the `sprk_matter` exemplar, clone the record and swap **only**
`sprk_contextfieldname` (and the PCF property to match) per target entity — every value below is
**already live today** on `sprk_communication` (task-001-audit-confirmed, no schema gate):

| Host entity | `sprk_contextfieldname` value |
|---|---|
| `sprk_matter` | `_sprk_regardingmatter_value` |
| `sprk_project` | `_sprk_regardingproject_value` |
| `sprk_invoice` | `_sprk_regardinginvoice_value` |
| `sprk_servicerequest` | `_sprk_regardingservicerequest_value` |
| `sprk_workassignment` | `_sprk_regardingworkassignment_value` |
| `sprk_event` | `_sprk_regardingevent_value` |
| `sprk_budget` | `_sprk_regardingbudget_value` |
| `sprk_analysis` | `_sprk_regardinganalysis_value` |
| `sprk_organization` | `_sprk_regardingorganization_value` |
| `account` | `_sprk_regardingaccount_value` |
| `contact` | `_sprk_regardingperson_value` |

(Source: `RegardingFieldMap.All` mirrored onto `sprk_communication`, verified in
`notes/041-grid-curation.md` §2's "11-entity regarding family" table — the same lookups the DataGrid
filter chips use.)

---

## 7. Follow-on (out of scope here, noted for a future code-touching task)

If the owner wants the count card's expand icon to jump to the **same-form** regarding-mode Timeline
section (task 022) instead of opening a thread-entity dialog, `ChartRenderer.tsx`'s `MetricCard`/
`ReportCardMetric` case would need to consume the existing `onViewListClick` prop (already plumbed through
`VisualHostRoot.tsx` for `DueDateCardList`) — a small, well-scoped code change, but a code change
nonetheless, and therefore out of scope for this STANDARD-rigor, config-only task. Noted here rather than
silently worked around.

---

## 8. Acceptance-criteria disposition

| FR-05 acceptance criterion | Status |
|---|---|
| Config-only, NO code, no new visual type (mirrors Matter Health/Budget/Tasks cards) | ✅ One `sprk_chartdefinition` record, existing MetricCard visual type, existing VisualHost control — §2. |
| Count-only: aggregate query, no message content rendered | ✅ Count aggregate over `sprk_communication`, no content columns selected — §3a. "Unread" scoped out (no schema field exists). |
| Drill-through targets Surface 1 (the threads view) | ✅ (with documented Path-C limitation) — `sprk_drillthroughtarget = sprk_communicationthread` opens a thread-grain dialog; an exact same-form tab-jump would require a small `ChartRenderer` code change (§3b, §7), out of scope here. |
| No thread-preview LIST / new visual type / new panel PCF | ✅ No new component — reuses shipped VisualHost + existing MetricCard type unchanged. |
| Optional — may be dropped with documented rationale | Not dropped — shipped as a low-risk, high-value exemplar config (§1); Surface 1 (020–022) is unaffected either way. |
| Live apply | ⚠️ **DEFERRED** — Dataverse MCP unavailable this session; owner creates the record (§2) + places the control (§4) + verifies (§5). |

---

## 9. Decisions recorded

1. **Scoped to one exemplar entity (`sprk_matter`)**, not all 11 — FR-05's own scope language says "one
   optional config record," unlike FR-04/task-022's explicit 11-entity mandate. §6 gives the replication
   table if the owner wants more.
2. **Count = total message count (`sprk_communication`, Count aggregate)**; "unread" dropped — no
   read/unread schema field exists in R1/R2 (a schema gap, not a config gap; out of scope).
3. **Drill-through target = `sprk_communicationthread` (entity-list dialog)**, not `sprk_communication`
   (message-grain, content-adjacent — rejected) and not the standalone `sprk_communicationspage` (global,
   unfiltered by design per Q-B — rejected). Documented as a Path-C (comply) resolution given
   `ChartRenderer`'s current `onViewListClick` wiring only covers `DueDateCardList`, not `MetricCard`
   (§3b, §7 follow-on noted for a future code-touching task).
4. **`sprk_contextfieldname` = `_sprk_regardingmatter_value`** — confirmed **already live** on
   `sprk_communication` today (no dependency on task 002's deferred thread-side schema for the COUNT
   query itself; only the drill-through dialog's OWN context filter depends on task 002's thread-side
   lookups going live).
5. **Live apply deferred** — Dataverse MCP unavailable this session, consistent with every other schema/
   config task in this project (001, 002, 003, 040, 041).
