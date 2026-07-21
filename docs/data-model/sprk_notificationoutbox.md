# Data Model — `sprk_notificationoutbox` (Notification Outbox — Layer B)

> **Last Updated**: 2026-07-21
> **Created by**: `spaarke-notification-spine-r1` task 011 (FR-02 schema half)
> **Status**: Current
> **Schema source**: live `spaarkedev1` `describe_table` (2026-07-21), created via `scripts/Deploy-NotificationOutboxEntity.ps1`

---

## 1. Purpose

`sprk_notificationoutbox` is the durable, per-user, `kind`-typed pending-notification **outbox** — Layer B of the notification spine. It is the source of truth that Layer C (Azure SignalR) accelerates but does not replace: a disconnected or offline client still receives its signal on next load by reading this table (spec FR-02/FR-06). It stores the row through its full lifecycle: **write → pending → delivered (optional live push) → dismissed/expired**.

`appnotification` (the native Dataverse OOB notification entity, used via `NotificationService.CreateNotificationAsync`) remains an **optional MDA-shell mirror** — it has no `kind` discriminator, no typed envelope payload, and no delivered/dismissed/expiry timestamp trio, which is exactly what this table adds. It is NOT the source of truth for the spine.

**Task 012** (outbox service layer — write/read/expire operations) builds directly against this schema. **Task 013** (typed envelope contract + `kind` taxonomy lock) defines the exact JSON shape stored in `sprk_envelope` and the closed set of valid `sprk_kind` string values.

---

## 2. Schema

### 2.1 Entity metadata

| Property | Value |
|---|---|
| **Logical name** | `sprk_notificationoutbox` |
| **Collection name** | `sprk_notificationoutboxes` |
| **Display name / plural** | Notification Outbox / Notification Outboxes |
| **Primary key** | `sprk_notificationoutboxid` (GUID) |
| **Primary name column** | `sprk_name` (NVARCHAR(200), required, autonumber `OUTBOX-{SEQNUM:6}` — producers never need to invent a display name for a system-written row) |
| **Ownership** | **User-owned** (`OwnershipType: UserOwned`) — the native `ownerid`/`owninguser` field IS the per-user pending-row consumer key. No custom owning-user lookup was added (see §4 decision log). |
| **State** | Standard `statecode`/`statuscode` (not currently used by the lifecycle model — see §3; dismiss/expiry are timestamp fields, not state transitions, so records stay Active and are queried by timestamp presence/absence). |

### 2.2 Columns (live spaarkedev1 schema, 2026-07-21)

| Logical name | Type | Required | Purpose |
|---|---|---|---|
| `sprk_notificationoutboxid` | GUID (PK) | — | Row identity. |
| `sprk_name` | NVARCHAR(200) | ApplicationRequired (autonumber) | System-generated identifier (`OUTBOX-000001`, …). |
| `owninguser` / `ownerid` | Lookup (native) → `systemuser` | — (Dataverse-managed) | **The per-user pending-row consumer key.** Every outbox row belongs to exactly one target user; `task 012`'s read path queries by owner. |
| `sprk_kind` | NVARCHAR(50) | ApplicationRequired | The `kind` discriminator. Kebab-case string matching task 013's closed C# taxonomy verbatim: active `suggestion` \| `communication-assessed` \| `communication-arrived`; reserved (no shape/consumer yet) `job-complete` \| `share` \| `system-alert`. |
| `sprk_envelope` | Memo (MULTILINE TEXT), MaxLength 4000 | ApplicationRequired | The typed envelope JSON payload — task 013's contract. IDs + minimal display metadata only. **MUST NOT** carry a message body, privileged content beyond a gated `snippet?`, or a pre-authorized action token (spec NFR-02/NFR-03). |
| `sprk_regardingrecordid` | NVARCHAR(50) | Optional | ADR-024 MINIMAL pattern — GUID string of the regarding record (mirrors the envelope's `regardingRecordId`). Optional because reserved `kind`s have no envelope shape defined yet and may not carry a regarding record. |
| `sprk_regardingrecordtype` | NVARCHAR(100) | Optional | ADR-024 MINIMAL pattern record-type discriminator — the regarding entity's **logical name as plain text** (e.g. `sprk_communication`, `sprk_matter`). NOT a lookup to `sprk_recordtype_ref` — see §4 decision log. |
| `sprk_delivered` | DATETIME, nullable | Optional | Set when Layer C (SignalR) successfully pushes the row live. `null` = not yet delivered live (row may still be visible via the FR-06 poll endpoint). |
| `sprk_dismissed` | DATETIME, nullable | Optional | Set when the user acknowledges/dismisses the pending item. `null` = still pending. |
| `sprk_expiresat` | DATETIME, nullable | Optional (schema level) | Expiry boundary for the pending row (task 012's expiry sweep). Left schema-optional rather than Dataverse-required so no future producer/kind is hard-blocked from writing — the "every active-taxonomy row gets an expiry" guarantee is a task-012 service-layer invariant, not a schema constraint (mirrors how `sprk_delivered`/`sprk_dismissed` are already modeled as nullable state). |

### 2.3 Relationships

None. This table intentionally has **no lookup relationships** — task 012's service reads/writes it directly by owner + kind + timestamps; the regarding reference uses the ADR-024 MINIMAL text pattern (§2.2), not a lookup.

---

## 3. Lifecycle states

A row moves through the following states, tracked purely by the presence/absence of the two nullable timestamp columns (no `statuscode` state-machine is used):

| State | `sprk_delivered` | `sprk_dismissed` | Meaning |
|---|---|---|---|
| **Pending, undelivered** | `null` | `null` | Row written by a producer; Layer C push has not (yet) succeeded — e.g. user offline, SignalR unreachable. Visible via the FR-06 poll/pending endpoint. |
| **Pending, delivered** | set | `null` | Layer C successfully pushed a live signal; the user has not dismissed it. Still returned by the pending-read path until dismissed or expired. |
| **Dismissed** | set or `null` | set | User acknowledged/dismissed the item. Excluded from pending-read results going forward. |
| **Expired** | any | `null` (typically) | `sprk_expiresat` has passed; task 012's expiry sweep excludes it from pending reads (and may hard-delete/archive per task 012's design — not decided by this schema task). |

**Write-before-ping invariant** (ADR-041/043, spec): the durable outbox row (`sprk_delivered = null`) is always written FIRST; the best-effort SignalR ping (which sets `sprk_delivered`) happens AFTER. Producers remain correct when SignalR is unreachable.

---

## 4. Design decisions / deviations from the literal task-011 POML column list

1. **`sprk_kind` is TEXT, not a Dataverse Choice column.** Task 013 defines the taxonomy as a closed C# string-const/enum discriminator serialized as kebab-case strings. A Choice column would require a second, parallel int-value mapping that could drift from the C# taxonomy. A plain string column lets the service write the exact string literal task 013 defines — the taxonomy stays closed in C#, where every producer/consumer in this project already lives.
2. **`sprk_regardingrecordtype` is plain TEXT, not a lookup to `sprk_recordtype_ref`.** ADR-024's full dual-field pattern uses a `sprk_recordtype_ref` lookup specifically to support a dynamic entity picker PCF and cross-entity unified views/subgrid filtering. This table has no UI picker and no subgrid-filtering requirement — it's written and read exclusively by BFF service code that already has the entity logical name as a plain string at write time. Per the task-011 POML's explicit authorization, the full 8-lookup pattern was skipped.
3. **No custom owning-user lookup field** — the native `ownerid`/`owninguser` (via `OwnershipType: UserOwned`) already provides the per-user key, is natively filterable (`$filter=_ownerid_value eq ...`), and gets Dataverse's standard security-role trimming for free.
4. **`sprk_expiresat` is schema-optional** despite the task-011 POML listing it without an explicit "(nullable)" tag — see §2.2 rationale. The lifecycle guarantee is enforced by task 012's service layer, not the schema.

Full proposal + escalation-trigger evidence: `projects/spaarke-notification-spine-r1/notes/011-outbox-table-schema-proposal.md`.

---

## 5. Escalation-trigger check (negative — new table was created)

Both `sprk_playbookconsumer` (Binding) and `appnotification` were inspected via live `mcp__dataverse__describe` before creating this table. Neither can express the per-user `kind`-typed pending row with delivered/dismissed/expiry semantics:

- **`sprk_playbookconsumer`**: Organization-owned (no per-user field at all); a routing/config table (18 enabled rows) describing which AI capability handles which invocation context, not a per-user data-row store. Its `sprk_disposition` = `Notification` option is an output-routing choice, explicitly "not yet dispatchable" per the live doc, and creates no per-user rows. No delivered/dismissed/expiry columns exist.
- **`appnotification`**: User-owned, but confirmed (live `describe_table`) to have no `kind` discriminator matching task 013's taxonomy, no typed envelope column (`data` is an unstructured generic blob), no `delivered` timestamp, and `sprk_briefingstate`/`ttlinseconds` are not equivalent to explicit `dismissed`/`expiresat` absolute timestamps.

---

## 6. Related docs

| Doc | Topic |
|---|---|
| `projects/spaarke-notification-spine-r1/spec.md` | FR-02 (this schema), FR-03 (envelope contract, task 013), FR-06 (poll fallback) |
| `.claude/adr/ADR-024-polymorphic-resolver-pattern.md` | Full dual-field pattern (NOT used here — MINIMAL variant only) |
| `.claude/skills/dataverse-create-schema/SKILL.md` | Schema-creation pattern followed by `scripts/Deploy-NotificationOutboxEntity.ps1` |
| `src/server/api/Sprk.Bff.Api/Services/NotificationService.cs` | `appnotification` mirror-write path (unchanged by this task) |
| `docs/data-model/sprk-playbookconsumer.md` | Binding schema — ruled out as an outbox substitute (§5 above) |
