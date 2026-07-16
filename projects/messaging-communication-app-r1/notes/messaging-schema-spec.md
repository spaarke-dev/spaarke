# Messaging R1 — Dataverse Schema Spec (maker-ready) — tasks 004 + 005

> **For**: owner to create in `spaarkedev1`. **Grounded in**: task-001 live audit (`notes/spikes/001-schema-audit.md`), design §5/§6.1/§6.4, spec FR-05/FR-08. Publisher prefix **`sprk`**.
> **Status**: awaiting owner creation. After creation, tasks 004/005 verify the live schema matches this spec (MCP `describe`).
> **Confirmed by 001 audit**: none of the below exist yet (clean adds); `sprk_communicationtype` global choice already has `Message = 100000004`; `sprk_communication` already carries the full ADR-024 regarding family (11 typed `sprk_regarding*` + 5 polymorphic `sprk_regardingrecord*`).

---

## Task 004 — NEW entity `sprk_communicationthread` + child table + relationships

### 4.1 Entity: `sprk_communicationthread` (the queryable grouping key, design §6.1 option A)

| Property | Value |
|---|---|
| Display name / Plural | Communication Thread / Communication Threads |
| Schema (logical) name | `sprk_communicationthread` |
| Ownership | **User/Team** (so record security composes with ADR-034 + option-B grants) |
| Primary column | `sprk_name` (Text, 200) — thread topic; derived from first message subject (email) or a participant/default label (chat), editable |
| Enable notes/activities | **No** (thin entity; MUST NOT use Activities per owner-lock) |
| Audit | On |

**Columns on `sprk_communicationthread`:**

| Schema name | Type | Details | Source |
|---|---|---|---|
| `sprk_name` | Text | 200; primary column = topic | design §6.1 "topic" |
| `sprk_threadtype` | Choice (local) | `Record-Anchored = 100000000`, `Direct 1:1 = 100000001` | design §6 topologies; drives 043 |
| `sprk_privacystate` | Choice (local) | `Open = 100000000`, `Private = 100000001` | design §5/§6.1 "thread-level privacy state" (D-04) |
| `sprk_privacyeffectivefrom` | Date and Time | nullable; set when flipped to Private/Open — drives **point-forward** switch (prior messages keep prior visibility) | design §5 "point-forward (D-04)" |
| **Anchor (regarding)** — reuse ADR-024, NOT a new mechanism | | see 4.2 | design §6.1 "anchor (ADR-024 regarding family)" |

> **Anchor note (one design detail to confirm at build)**: the thread must be filterable by "which record it's about" without a second regarding mechanism (ADR-024 MUST). **Recommended (thin)**: add a single **denormalized polymorphic anchor** = 2 fields mirroring the existing `sprk_regardingrecord*` convention on `sprk_communication`:
> - `sprk_anchorrecordid` (Text, 100) — the anchor record's GUID
> - `sprk_anchorrecordtype` (Text, 100) — the anchor entity logical name (e.g. `sprk_matter`, `sprk_project`)
>
> This mirrors the polymorphic pattern the audit found and keeps the entity thin. Task 004's `IThreadResolver` populates it from the message's resolved regarding at thread-create. **Alternative (symmetry)**: replicate the full typed `sprk_regarding*` lookup set — heavier, only choose if you want native lookups on the thread form. Confirm which at build; the rest of the spec is independent of this choice.

### 4.2 Child table: `sprk_communicationchannelref` (thread↔channel, design §6.1 D-03)

One row per `(thread, channel, external-ref)`. R1 populates the ACS `ChatThreadId` for the Message channel; email/SMS refs attach later with no schema change ("channel is an attribute").

| Property | Value |
|---|---|
| Display name / Plural | Communication Channel Ref / Communication Channel Refs |
| Schema name | `sprk_communicationchannelref` |
| Ownership | User/Team (or org-owned — child of thread; follow thread) |
| Primary column | `sprk_name` (Text, 200; auto/label) |

**Columns:**

| Schema name | Type | Details |
|---|---|---|
| `sprk_name` | Text | 200; primary (e.g. "ACS: {chatThreadId}") |
| `sprk_thread` | **Lookup → `sprk_communicationthread`** | Required; N:1 (see 4.3) |
| `sprk_channeltype` | Choice | reuse the **global** `sprk_communicationtype` option set if it is global; else local choice mirroring `Email=100000000 … Message=100000004`. R1 rows = `Message` |
| `sprk_externalref` | Text | 400; the channel's external id — R1 = ACS `ChatThreadId` |

### 4.3 Relationships

| Relationship | Type | Fields |
|---|---|---|
| `sprk_communicationthread` → `sprk_communicationchannelref` | 1:N | via `sprk_communicationchannelref.sprk_thread` |
| `sprk_communicationthread` → `sprk_communication` | 1:N | via `sprk_communication.sprk_thread` (created in task 005 below) |

---

## Task 005 — Columns on existing entities

### 5.1 On `sprk_communication`

| Schema name | Type | Details | FR |
|---|---|---|---|
| `sprk_thread` | **Lookup → `sprk_communicationthread`** | nullable; the grouping key; set by `IThreadResolver` (040) both directions | FR-05/FR-06 |
| `sprk_isprivate` | Two-Option (Yes/No) | default **No**; message-level privacy | FR-08 |
| `sprk_isinternalonly` | Two-Option (Yes/No) | default **No**; hidden from external participants (R2/R3) | FR-08 (D-05) |
| `sprk_privilegeclassification` | Choice (local) | `None = 100000000`, `Potentially Privileged = 100000001`, `Privileged = 100000002` | FR-08; AI may FLAG never decide (ADR-015) |
| `sprk_acsmessageid` | Text | 200; **the idempotency/echo-dedup key** (ACS `SendChatMessageResult.Id`); index for dedupe lookups | FR-04, NFR-03 |
| `sprk_acschatthreadid` | Text | 200; the ACS thread this message belongs to (denormalized convenience; canonical home is the channel-ref row) | FR-02 |

> **Do NOT reuse** `sprk_graphmessageid` / `sprk_internetmessageid` / `sprk_correlationid` / `sprk_inreplyto` for the ACS message id — the 001 audit confirmed those are email/Graph-specific. `sprk_acsmessageid` is net-new.

### 5.2 On `systemuser` AND `contact` (both)

| Schema name | Type | Details |
|---|---|---|
| `sprk_communicationuserid` | Text | 500; the ACS `communicationUserId` (MRI) mapped to this identity; set on first use by task 010. Add to **both** `systemuser` and `contact`. |

---

## Verification (tasks 004/005 close-out, after owner creates)

Run via Dataverse MCP `describe` and confirm each of the above exists with the stated type. Checklist:
- [ ] `sprk_communicationthread` entity exists (User/Team owned) with `sprk_threadtype`, `sprk_privacystate`, `sprk_privacyeffectivefrom`, anchor fields
- [ ] `sprk_communicationchannelref` exists with `sprk_thread` lookup + `sprk_channeltype` + `sprk_externalref`
- [ ] `sprk_communication.sprk_thread` lookup → thread; `sprk_isprivate`, `sprk_isinternalonly`, `sprk_privilegeclassification`, `sprk_acsmessageid`, `sprk_acschatthreadid`
- [ ] `sprk_communicationuserid` on BOTH `systemuser` and `contact`
- [ ] Option-set integers match exactly (`sprk_privilegeclassification`, `sprk_threadtype`, `sprk_privacystate`)
- [ ] Add all to the messaging solution; publish

## Option-set integer summary (reserve these)

| Choice field | Members (name = int) |
|---|---|
| `sprk_threadtype` | Record-Anchored=100000000, Direct 1:1=100000001 |
| `sprk_privacystate` | Open=100000000, Private=100000001 |
| `sprk_privilegeclassification` | None=100000000, Potentially Privileged=100000001, Privileged=100000002 |
| `sprk_channeltype` | reuse global `sprk_communicationtype` (Email=100000000 … Message=100000004) if global |

> Verify these integers aren't already taken in your environment before creating (task 001 pattern: MCP `describe` the option set). Adjust and tell me if any collide — downstream tasks read them by logical name, not hardcoded int, but 005's verification records the actual values.
