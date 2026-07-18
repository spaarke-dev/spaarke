# Spike 001 — Live `sprk_communication` Schema Audit + `Message=100000004` Confirmation

> **Task**: `001-phase0-communication-schema-audit`
> **Phase**: W0 — Phase 0 Verification + Foundation
> **Date**: 2026-07-16
> **Posture**: READ-ONLY. Dataverse MCP `describe` only (no `read_query` needed — option-set members returned inline by `describe`). No write tool invoked, no code authored, no build run.
> **Infra path**: ✅ **Dataverse MCP LIVE** — schema read directly from the connected environment (NOT a code/design fallback reconstruction). All findings below are VERIFIED against live metadata unless explicitly marked otherwise.

---

## TL;DR (for tasks 004 / 005 / 006)

- ✅ **`Message = 100000004` CONFIRMED** in the live `sprk_communicationtype` global choice. Task 006 (C# enum) is cleanly unblocked — **no HARD blocker**.
- ✅ **ADR-024 regarding family already present** on `sprk_communication` (11 typed `sprk_regarding*` lookups + 5 polymorphic `sprk_regardingrecord*` fields). Task 004's thread anchor reuses these — do NOT add a second regarding mechanism.
- 🟡 **7 columns MUST-ADD** (privacy, internal-only, privilege, ACS `messageId`, ACS `chatThreadId`, `sprk_thread` lookup) + `communicationUserId` on **both** `systemuser` and `contact`. None exist today.
- ❌ **`sprk_conversationid` does NOT exist** (R4 did not add it). Design §6.1 option (C) is moot — grouping key is LOCKED to option (A) `sprk_communicationthread` entity + `sprk_thread` lookup (design §14.6 / line 228, 375).

**No HARD blockers. Tasks 004/005/006 can be authored directly against this report.**

---

## 1. Column Delta — `sprk_communication`

Live attribute set captured via `describe('tables/sprk_communication')`. Every spec-required column below was checked against the full live attribute list.

| Spec-required column | Status | Live logical name / type (if exists) | Source of truth | FR mapping | Note |
|---|---|---|---|---|---|
| Message-level **privacy** flag | 🟡 **MUST-ADD** | — | live `describe` (absent) | FR-08 / design §5 | New BIT (or choice) message-privacy flag; BFF query-filter enforces (NFR-06). |
| **Internal-only** flag | 🟡 **MUST-ADD** | — | live `describe` (absent) | FR-08 / design §5 (D-05) | Visibility flag; composes with the D-05 user attribute. |
| **Privilege** classification | 🟡 **MUST-ADD** | — | live `describe` (absent) | FR-08 / design §5 | Classification metadata, distinct from privacy; AI may *flag* never *decide* (ADR-015). |
| ACS **`messageId`** correlation key | 🟡 **MUST-ADD** | — | live `describe` (absent) | FR-02 / FR-04 (idempotency / echo-dedup, NFR-03) | ACS-native message id. **Near-misses that are NOT this**: `sprk_graphmessageid` (Graph/email), `sprk_internetmessageid` (RFC-2822 email), `sprk_correlationid` (generic), `sprk_inreplyto` (email ancestry). A dedicated ACS id column is required for dedupe. |
| ACS **`chatThreadId`** correlation key | 🟡 **MUST-ADD** | — | live `describe` (absent) | FR-05 (D-03 thread↔channel child) | **Placement decision for 004/005**: design §6.1/FR-05 puts the ACS `ChatThreadId` on the **thread↔channel child table** (one row per `(thread, channel, external-ref)`), NOT on `sprk_communication`. Confirm intended home during 004; it is net-new either way. |
| **`sprk_thread`** lookup | 🟡 **MUST-ADD** | — | live `describe` (absent) | FR-05 / FR-06 | Lookup → `sprk_communicationthread` (created by task 004). `ThreadContinuityRung`/`IThreadResolver` sets it direction-symmetrically. |
| **`communicationUserId`** on **`systemuser`** | 🟡 **MUST-ADD** | — | live `describe('tables/systemuser')` (absent) | FR-03 | ACS identity mapping. Existing sprk_ cols on systemuser: `sprk_containerid`, `sprk_primarycontact`, `sprk_userprofile`, `sprk_usertype` — none is an ACS id. |
| **`communicationUserId`** on **`contact`** | 🟡 **MUST-ADD** | — | live `describe('tables/contact')` (absent) | FR-03 | ACS identity mapping. Existing sprk_ cols on contact: `sprk_containerid`, `sprk_invoice`, `sprk_organization`, `sprk_systemuser`, `contact_regardingrecordnumber` — none is an ACS id. |
| R4-anticipated **`sprk_conversationid`** (design §6.1 option C) | ❌ **DOES NOT EXIST** | — | live `describe` (absent) | design §6.1 | R4 did NOT ship this field. Design line 72 already asserted "no `sprk_conversationid`"; live schema confirms. Grouping key LOCKED to option (A) — this fallback is not used. |

### Columns that already EXIST and are relevant to reuse (not additions)

| Live column | Type | Relevance |
|---|---|---|
| `sprk_communicationtype` | CHOICE | **Contains `Message (100000004)`** — see §2. |
| `sprk_body` / `sprk_bodyformat` | MULTILINE TEXT / CHOICE (PlainText 100000000, HTML 100000001) | Message content + quoting (FR-13); timeline render (FR-10). |
| `sprk_direction` | CHOICE (Incoming 100000000, Outgoing 100000001) | Direction-symmetric enrichment/thread assignment. |
| `sprk_inreplyto` / `sprk_internetmessageid` / `sprk_graphmessageid` | NVARCHAR | Email ancestry the `ThreadContinuityRung` already walks (FR-06) — reused, NOT the ACS messageId. |
| `sprk_correlationid` | NVARCHAR(100) | Generic correlation; not the ACS dedupe key. |
| `sprk_attachmentcount` / `sprk_hasattachments` | INT / BIT | Attachment model (FR-14). |
| `sprk_sentby` | LOOKUP → systemuser | Sender resolution. |

---

## 2. Choice Integer Confirmation — `sprk_communicationtype`

**Source**: inline option-set members from `describe('tables/sprk_communication')` (live).

Full member list (label → integer):

| Label | Integer |
|---|---|
| Email | 100000000 |
| Teams Message | 100000001 |
| SMS | 100000002 |
| Notification | 100000003 |
| **Message** | **100000004** ✅ |

✅ **CONFIRMED**: `sprk_communicationtype` contains a **`Message`** member at integer **`100000004`** in the live environment. This matches spec FR-16 and design §14.6 exactly.

**Current C# enum** (`src/server/api/Sprk.Bff.Api/Services/Communication/Models/CommunicationType.cs`): has `Email=100000000, TeamsMessage=100000001, SMS=100000002, Notification=100000003` — **`Message` NOT yet present**. Task 006 adds `Message = 100000004` and the enum↔choice contract will agree. **No mismatch → no HARD blocker for 006.**

---

## 3. Thread Anchor Path — ADR-024 Regarding Family (already present)

Confirmed on live `sprk_communication` — task 004's thread anchor **reuses** these; do NOT recommend a second regarding mechanism (constraint per POML + ADR-024).

Typed regarding lookups present: `sprk_regardingmatter`, `sprk_regardingaccount`, `sprk_regardingperson` (→ contact), `sprk_regardingevent`, `sprk_regardinginvoice`, `sprk_regardingorganization`, `sprk_regardingproject`, `sprk_regardingservicerequest`, `sprk_regardingworkassignment`, `sprk_regardinganalysis`, `sprk_regardingbudget`.

Polymorphic overlay present: `sprk_regardingrecordid`, `sprk_regardingrecordname`, `sprk_regardingrecordnumber`, `sprk_regardingrecordtype` (→ `sprk_recordtype_ref`), `sprk_regardingrecordurl`.

→ The `sprk_communicationthread` entity (task 004) anchors via this existing family (design §6.1 option A: "anchor (ADR-024 regarding family, not a new mechanism)").

---

## 4. Downstream Impact — exactly what 004 / 005 / 006 add

**Task 004 (thread entity + `sprk_thread` lookup)** — cleanly unblocked:
- Create `sprk_communicationthread` entity (topic, anchor via existing ADR-024 regarding family, thread-level privacy state, participant set).
- Add `sprk_communication.sprk_thread` lookup → `sprk_communicationthread` (does not exist today).
- Add thread↔channel child table (one row per `(thread, channel, external-ref)`); R1 populates the ACS `ChatThreadId` here (decide ACS `chatThreadId` home = child table vs `sprk_communication` at 004 design).

**Task 005 (new columns + `communicationUserId`)** — cleanly unblocked, all 5+2 are net-new:
- On `sprk_communication`: privacy flag, internal-only flag, privilege classification, ACS `messageId` (idempotency dedupe key). (ACS `chatThreadId` per 004's placement decision.)
- On `systemuser`: `communicationUserId` (ACS identity map).
- On `contact`: `communicationUserId` (ACS identity map).
- ⚠️ Do NOT re-add `sprk_conversationid` — grouping is via option (A), not (C).

**Task 006 (C# enum extension)** — cleanly unblocked, **no discrepancy**:
- Add `Message = 100000004` to `CommunicationType.cs`. Live Dataverse choice already = 100000004; contract agrees.

**HARD blockers**: **NONE.** No `Message`-integer mismatch; no duplicate-column risk (all spec-required columns confirmed absent). Tasks 004/005/006 authorable against this report with zero further Dataverse guessing.

---

## 5. Evidence / Method Log

| Check | MCP call | Result |
|---|---|---|
| `sprk_communication` attribute set + option set | `describe('tables/sprk_communication')` | Full attribute list + `sprk_communicationtype` members returned inline. |
| `contact` `communicationUserId` | `describe('tables/contact')` | Absent (custom sprk_ cols enumerated above). |
| `systemuser` `communicationUserId` | `describe('tables/systemuser')` | Absent (custom sprk_ cols enumerated above). |
| Choice integer `Message` | (inline in `sprk_communication` describe) | `Message (100000004)` present. |

**Constraints honored**: no create/update/delete MCP tool invoked; no `dotnet build`; no schema deploy; markdown-only output.

**Acceptance criteria**: all 5 met — column table (EXISTS/MUST-ADD + logical name + type) ✅; `Message=100000004` confirmed with full member list ✅; `sprk_conversationid` presence resolved (does not exist) ✅; downstream-impact + HARD-blocker section present ✅; no write tool / no code / no build ✅.
