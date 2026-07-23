# Task 002 — `sprk_communicationthread` regarding-resolution + marker schema (FR-06)

> **For**: tasks 010 (reads over the thread), 070 (default-thread marker), 071 (naming re-derive +
> RegardingResolver placement binds the new Lookup discriminator), and 081 (architecture doc).
> **Grounded in**: task 001 audit (`notes/001-phase0-schema-audit.md`), `RegardingFieldMap.cs`
> (verbatim), RegardingResolver PCF binding contract, spec FR-06/FR-07/FR-09/NFR-04/Q2.
> **Author**: task-execute 002, 2026-07-19. STANDARD rigor, `sonnet@high`. Publisher prefix **`sprk`**.
> **Deliverable script**: [`scripts/Deploy-ThreadRegardingSchema.ps1`](../scripts/Deploy-ThreadRegardingSchema.ps1)
> (idempotent, describe-before-write, Web API + PowerShell per `dataverse-create-schema`).

---

## ⚠️ LIVE APPLY DEFERRED (describe-before-write gate)

Dataverse MCP was **unavailable this session** (same as task 001), so the schema was **authored but NOT
applied**. This mirrors R1's proven pattern: **owner-created + agent-verified** (R1 `messaging-schema-spec.md`
was owner-created in `spaarkedev1`, then MCP-`describe`-verified).

**Owner / next-session action before downstream tasks (010/070/071) build against live schema:**
1. `az login`, then run
   `./scripts/Deploy-ThreadRegardingSchema.ps1 -Environment spaarkedev1.crm.dynamics.com -SolutionUniqueName <messaging-solution-unique-name>`.
   (Set `-SolutionUniqueName` to the project's unmanaged solution per ADR-027; omit only for a scratch apply.)
2. MCP `describe_table('sprk_communicationthread')` and confirm the **14 new columns** below exist, the
   existing **Text `sprk_regardingrecordtype` is unchanged (String)**, and **no** category/tags/description
   were added (Q2).

The script is idempotent (each attribute/relationship is skipped if present) and begins with a hard NFR-04
guard that **aborts** if `sprk_regardingrecordtype` is anything other than Text. Authoring is fully unblocked;
only the live apply is gated.

---

## The exact task-002 delta — 14 new columns, all ADDITIVE (non-breaking)

### 1. Eleven typed `sprk_regarding{...}` lookups (mirror `RegardingFieldMap.All`, verbatim)

Each is a **Lookup** (1:N, N-side = `sprk_communicationthread`), RequiredLevel **None**, delete behavior
**RemoveLink**. Same field-name convention `sprk_communication` already uses (task 001 §2).

| # | Lookup logical name | Schema name | Target entity (1-side) |
|---|---|---|---|
| 1 | `sprk_regardingmatter` | `sprk_RegardingMatter` | `sprk_matter` |
| 2 | `sprk_regardingproject` | `sprk_RegardingProject` | `sprk_project` |
| 3 | `sprk_regardinginvoice` | `sprk_RegardingInvoice` | `sprk_invoice` |
| 4 | `sprk_regardingservicerequest` | `sprk_RegardingServiceRequest` | `sprk_servicerequest` |
| 5 | `sprk_regardingworkassignment` | `sprk_RegardingWorkAssignment` | `sprk_workassignment` |
| 6 | `sprk_regardingevent` | `sprk_RegardingEvent` | `sprk_event` |
| 7 | `sprk_regardingbudget` | `sprk_RegardingBudget` | `sprk_budget` |
| 8 | `sprk_regardinganalysis` | `sprk_RegardingAnalysis` | `sprk_analysis` |
| 9 | `sprk_regardingorganization` | `sprk_RegardingOrganization` | `sprk_organization` |
| 10 | `sprk_regardingaccount` | `sprk_RegardingAccount` | `account` (non-`sprk_`) |
| 11 | `sprk_regardingperson` | `sprk_RegardingPerson` | `contact` (non-`sprk_`) |

> ⚠️ **contact → `sprk_regardingperson`** (NOT `sprk_regardingcontact`), and **account → `sprk_regardingaccount`**.
> These two non-`sprk_`-prefixed targets are the ones most easily fat-fingered — they match `RegardingFieldMap.All`
> lines 24–25 exactly.

### 2. NEW Lookup discriminator — `sprk_regardingrecordtype_ref`

| Logical name | Schema name | Type | Target entity | Required |
|---|---|---|---|---|
| `sprk_regardingrecordtype_ref` | `sprk_RegardingRecordType_Ref` | **Lookup** | `sprk_recordtype_ref` | None |

**Why this name (the load-bearing decision).** RegardingResolver is entity-agnostic; its bound property
`regardingRecordType` (`Lookup.Simple`, `usage="bound"`, `required="true"`) is a **Lookup → `sprk_recordtype_ref`**.
On `sprk_communication` / `sprk_todo` / `sprk_event` that field is named literally `sprk_regardingrecordtype`.
**On the thread that name is already taken by the in-use Text field** (MUST-NOT-RETYPE, NFR-04), so the new
Lookup needs a distinct name. `sprk_regardingrecordtype_ref` is chosen because the resolver's write handler
discovers the discriminator **dynamically**, not by hard-coded name:

```
// src/client/pcf/RegardingResolver/.../handlers/ResolverWriteHandler.ts:342-345
const recordTypeNavProp = navProps.find(
  n => n.referencedEntity === 'sprk_recordtype_ref'
    && n.columnName.toLowerCase().includes('regardingrecordtype')
);
const recordTypeKey = recordTypeNavProp?.navPropName ?? 'sprk_RegardingRecordType';
```

`sprk_regardingrecordtype_ref` satisfies **both** predicates — it references `sprk_recordtype_ref` **and** its
lowercased column name contains `regardingrecordtype`. So in task 071 the maker binds the PCF's `regardingRecordType`
property to `sprk_regardingrecordtype_ref` and the resolver writes it **with zero code change**. The existing Text
`sprk_regardingrecordtype` is not a lookup relationship, so it never appears in `navProps` and cannot be matched.

> Task-071 note: the resolver's *subgrid auto-detect* path (`RegardingResolverApp.tsx:1008`) still calls
> `setFormLookupValue('sprk_regardingrecordtype', 'sprk_recordtype_ref', ...)` with a hard-coded name. On the
> thread that name is Text, so that best-effort path no-ops gracefully (NFR-06 graceful-blank). The **primary
> write path** (`ResolverWriteHandler.applyRegardingSelection`, dynamic nav-prop discovery) is what the thread
> form uses and it targets `sprk_regardingrecordtype_ref` correctly. Flagged for 071 verification.

### 3. Naming-edited marker — `sprk_nameisautoderived` (Boolean)

| Logical name | Schema name | Type | Options | Default |
|---|---|---|---|---|
| `sprk_nameisautoderived` | `sprk_NameIsAutoDerived` | Boolean (Two Options) | `Auto` = 1 / `Edited` = 0 | **Auto (1)** |

Task 071 gate: `ThreadResolver.BuildTopic()` re-derives `sprk_name` on regarding change **only while
`sprk_nameisautoderived == true (Auto)`**. A user edit to the name flips it to `Edited (0)` and the name is
preserved. Default **Auto** so new/existing threads re-derive until a user overrides.

### 4. Default-thread marker — `sprk_isdefaultthread` (Boolean)

| Logical name | Schema name | Type | Options | Default |
|---|---|---|---|---|
| `sprk_isdefaultthread` | `sprk_IsDefaultThread` | Boolean (Two Options) | `Yes` = 1 / `No` = 0 | **No (0)** |

Task 070 gate: identifies a regarding record's **default catch-all thread** so messages never orphan (FR-09).
Simplest queryable form — task 070 selects the thread for a regarding record where
`sprk_isdefaultthread == true`. Chosen over a typed/self-lookup because a Boolean is the minimal surface 070 needs.

---

## 🚫 Do-NOT-touch / Do-NOT-add (from task 001 audit §3 + §7)

- **MUST-NOT-RETYPE**: `sprk_regardingrecordtype` (Text, 100) — read by ThreadResolver + membership derivation +
  timeline filters. Stays the denormalized copy. The new discriminator is `sprk_regardingrecordtype_ref`, a
  separate Lookup — **never** a retype. (Script step [0] aborts if this field is not Text.)
- **Do-NOT-recreate** (8 existing columns): `sprk_name`, `sprk_threadtype`, `sprk_privacystate`,
  `sprk_privacyeffectivefrom`, `sprk_regardingrecordid`, `sprk_regardingrecordtype`, `sprk_regardingrecordname`,
  `sprk_regardingrecordurl`. (Script is idempotent — skips existing.)
- **MUST-NOT-ADD (Q2)**: no `category`, no `tags`, no `description`. Threads = regarding + name only.

---

## §11 Component Justification (new lookups + Lookup discriminator + 2 markers = new surface)

1. **Existing overlap** — the thread's `sprk_regardingrecordtype` is **Text**, not a bindable Lookup; there are
   **no** typed regarding lookups and **no** auto-vs-edited / default-thread markers on the thread (task 001
   grep/audit-confirmed §3). No overlap.
2. **Extension instead** — retyping the Text field to a Lookup was considered and **REJECTED** (breaking, NFR-04).
   The additive path is a parallel Lookup discriminator + the 11 typed lookups; the markers are new Booleans with
   no existing home. The 11 lookups reuse the **existing** `RegardingFieldMap` mechanism (ADR-024) — no new
   regarding mechanism (ADR-046).
3. **Concrete cost-of-doing-nothing** — without the Lookup discriminator + typed lookups, RegardingResolver
   cannot attach to the thread form and thread regarding stays manual/denormalized (FR-06/FR-07 fail); without
   `sprk_nameisautoderived`, name re-derive clobbers user edits (FR-07 fail); without `sprk_isdefaultthread`,
   messages orphan with no per-record default thread (FR-09 fail). Named contract failures, not "future flexibility."

---

## ADR alignment

- **ADR-024** — the 11 typed lookups mirror `RegardingFieldMap.All` exactly (same 11 targets + `sprk_regarding{...}`
  names). No new regarding mechanism or field-name scheme.
- **ADR-046** — thread regarding is the existing RegardingResolver PCF (via these lookups + the discriminator),
  NOT CommunicationConnections or the Field Mapping Framework.
- **ADR-027 / ADR-022** — components authored via Web API are unmanaged (correct for dev); `-SolutionUniqueName`
  lands them in the project's unmanaged solution, exported as managed for higher environments.

---

## Acceptance-criteria trace

- ✅ All 11 typed `sprk_regarding{...}` lookups defined, mirroring `RegardingFieldMap.All` (same 11 targets +
  field-name convention) — §1 + script step [1].
- ✅ NEW Lookup discriminator `sprk_regardingrecordtype_ref` → `sprk_recordtype_ref` defined; RegardingResolver
  binds it via its dynamic nav-prop discovery (`referencedEntity=='sprk_recordtype_ref'` +
  `columnName.includes('regardingrecordtype')`) — §2 + script step [2]. Placement is 071.
- ✅ Existing Text `sprk_regardingrecordtype` UNTOUCHED (same type + name); script step [0] guards it — §3.
- ✅ `sprk_nameisautoderived` (Boolean, default Auto) + `sprk_isdefaultthread` (Boolean, default No) defined —
  §3/§4 + script step [3].
- ✅ NEGATIVE: no category/tags/description (Q2); no existing thread column recreated (idempotent) — §3.
- ✅ NEGATIVE: schema only — no resolver/naming logic (070/071), no RegardingResolver PCF placement (071), no
  BFF code. This task produced one PowerShell script + this note.
- ⚠️ Live apply + MCP verification DEFERRED (MCP unavailable) — owner/next-session gate above.

---

## Unblocks

- **010** — reads over the thread's regarding (the typed lookups + discriminator are the resolvable surface).
- **070** — `sprk_isdefaultthread` marker for the auto-threading default catch-all.
- **071** — `sprk_nameisautoderived` marker for BuildTopic re-derive gating + RegardingResolver PCF placement
  binds `sprk_regardingrecordtype_ref`.
