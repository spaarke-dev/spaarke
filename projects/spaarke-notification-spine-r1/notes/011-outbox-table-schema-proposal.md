# Task 011 — Outbox Table Schema Proposal

> Written BEFORE any Dataverse schema creation, per POML step 1.

## Table

| Property | Value |
|---|---|
| Logical name | `sprk_notificationoutbox` |
| Display name | Notification Outbox |
| Plural / collection | Notification Outboxes |
| Ownership | **User-owned** (`OwnershipType: UserOwned`) — gives the native `ownerid`/`owninguser` field as the per-user consumer key (constraint: "table's primary consumer key is the owning user"). No custom owning-user lookup added — the native field already provides this. |
| Primary name attribute | `sprk_name`, autonumber `OUTBOX-{SEQNUM:6}` (producers never need to invent a display name for a system-written row; still overridable). |
| Naming-collision check | `mcp__dataverse__search("sprk_notificationoutbox")` and `mcp__dataverse__search("notification outbox")` both returned no matching table — confirmed no collision. |

## Column set (custom, beyond native owner/audit columns)

| Logical name | Type | Required | Purpose |
|---|---|---|---|
| `sprk_kind` | String(50) | ApplicationRequired | The `kind` discriminator — kebab-case string matching task 013's C# closed-set taxonomy verbatim (`suggestion`, `communication-assessed`, `communication-arrived`; reserved `job-complete`, `share`, `system-alert`). **Text, not Choice** — see rationale below. |
| `sprk_envelope` | Memo(4000) | ApplicationRequired | The typed envelope JSON payload (task 013's contract: IDs + minimal display metadata only — no message bodies, no privileged content, no action tokens). 4000 chars is generous headroom for the locked field lists (communication envelope: ~9 short fields; suggestion envelope: ~7 short fields). |
| `sprk_regardingrecordid` | String(50) | None (optional) | ADR-024 MINIMAL pattern — GUID string of the regarding record. Optional because reserved kinds (`job-complete`, `share`, `system-alert`) have no envelope shape defined yet and may not carry a regarding record. |
| `sprk_regardingrecordtype` | String(100) | None (optional) | ADR-024 MINIMAL pattern record-type discriminator — the regarding entity's logical name (e.g. `sprk_communication`, `sprk_matter`), as plain text. |
| `sprk_delivered` | DateTime | None (nullable) | Timestamp the row was pushed via Layer C (SignalR); null = not yet delivered live. |
| `sprk_dismissed` | DateTime | None (nullable) | Timestamp the user dismissed/acknowledged the pending item; null = still pending. |
| `sprk_expiresat` | DateTime | None (nullable at schema level) | Expiry timestamp for the pending row (expiry sweep boundary). Left optional at the SCHEMA level (not Dataverse `ApplicationRequired`) so a future producer/kind is never blocked from writing by a missing value; task 012's write path is expected to always populate it in practice. |

## Design decisions / deviations from the POML's literal proposal

1. **`sprk_kind` is TEXT, not Choice.** The POML step 1 offered "text or choice matching task 013's taxonomy" as an either/or. Task 013 (parallel task) defines the taxonomy as a closed C# string-const/enum discriminator serialized as kebab-case strings (`suggestion`, `communication-arrived`, …) — a Dataverse Choice column would require a second, parallel int-value mapping that could drift from the C# taxonomy (two sources of truth for the same closed set). A plain string column lets task 012's service write the exact same string literal task 013 defines, with zero mapping-table risk. The taxonomy is still closed — it's closed in C#, which is where every producer/consumer in this project already lives.
2. **`sprk_regardingrecordtype` is plain TEXT, not a lookup to `sprk_recordtype_ref`.** ADR-024's full dual-field pattern uses a lookup to `sprk_recordtype_ref` for the resolver's record-type field specifically to support the AssociationResolver/RegardingResolver PCF's dynamic entity picker and cross-entity unified views. The outbox table has no UI picker and no subgrid-filtering requirement (per the POML constraint, which explicitly authorizes skipping the full pattern absent such a need) — it's written and read exclusively by BFF service code (task 012), which already has the entity logical name as a plain string at write time. A lookup would add a hard dependency on `sprk_recordtype_ref` row provisioning for zero behavioral benefit here.
3. **No custom owning-user lookup field.** Dataverse's native `ownerid` (via `OwnershipType: UserOwned`) already provides the per-user key, is natively queryable/filterable (`?$filter=_ownerid_value eq ...`), and gets Dataverse's standard security-role trimming for free. Adding a redundant `sprk_owneruserid` lookup would duplicate this with no added capability.
4. **`sprk_expiresat` left schema-optional despite the POML listing it without a "(nullable)" tag.** Making it Dataverse `ApplicationRequired` would hard-block any future producer that legitimately has no fixed expiry (e.g., a reserved kind not yet designed). The lifecycle guarantee (every active-taxonomy row gets an expiry) is a task-012 service-layer invariant, not a schema-level constraint — consistent with how `sprk_delivered`/`sprk_dismissed` are already modeled as nullable state, not required.

## Escalation-trigger check (per POML step 2 + `<escalation>`)

Both `sprk_playbookconsumer` (Binding) and `appnotification` were inspected via `mcp__dataverse__describe` against their LIVE spaarkedev1 schemas before any table was created. Neither can express the per-user `kind`-typed pending row with delivered/dismissed/expiry semantics:

- **`sprk_playbookconsumer`**: `OwnershipType` is **Organization**-owned (`organizationid` only — no user/owner field at all). It is a routing/config table (18 enabled rows total, per `docs/data-model/sprk-playbookconsumer.md`) describing which AI capability handles which invocation context — not a per-user data row store. `sprk_consumertype` is an AI-routing capability code (`chat-summarize`, `create-task`, …), a different axis entirely from the notification `kind` taxonomy. `sprk_disposition`'s `Notification` (100000005) option is an *output-routing* choice ("this capability's result should render as a notification") — it does not create per-user pending rows and is explicitly listed as "not yet dispatchable" in the live doc. There is no delivered/dismissed/expiry timestamp trio anywhere on the table. **Cannot express the outbox.**
- **`appnotification`**: User/team-owned (has `owninguser`) but confirmed via live `describe_table` to have no `kind` discriminator matching task 013's taxonomy, no typed envelope JSON column (`data` is a generic unstructured action-data memo, not the locked envelope contract), no `delivered` timestamp at all, and `sprk_briefingstate` (Unread/Checked/Removed) + `ttlinseconds` (relative TTL) are NOT equivalent to explicit `dismissed`/`expiresat` absolute timestamps. This matches spec's explicit design: `appnotification` stays an OPTIONAL MDA-shell mirror, not the source of truth.

**Escalation trigger does NOT fire.** Proceeding to table creation.

## Idempotent creation approach

Following `.claude/skills/dataverse-create-schema/SKILL.md` + exemplar `scripts/Deploy-ChartDefinitionEntity.ps1`: PowerShell + Web API (not `pac table create`), `Test-EntityExists`/attribute-presence checks before each create call, safe to re-run. Script: `scripts/Deploy-NotificationOutboxEntity.ps1`.
