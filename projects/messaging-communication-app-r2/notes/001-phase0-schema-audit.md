# Task 001 — Phase-0 Schema + Participant Audit (findings note)

> **For**: tasks 002 (thread typed-regarding lookups + markers), 003 (participant junction), 004 (junction ADR).
> **Grounded in**: code + docs already in this worktree (see method note below). **Read-only spike** — no schema
> mutated, no code changed.
> **Author**: task-execute 001, 2026-07-19. STANDARD rigor, `sonnet@high`.

---

## ⚠️ LIVE-VERIFY DEFERRED (MCP unavailable this session)

The `mcp__dataverse__*` tools were disconnected for this session, so the live `describe`/`read_query` calls the
POML prescribes could not run. This audit is therefore **grounded on repo code + the R1 as-built schema doc**
(`../messaging-communication-app-r1/notes/messaging-schema-spec.md`, which was itself verified-live in R1 on
2026-07-16 via MCP `describe`) — the same "built + verified-live-deferred" pattern R1 used.

**Owner / next-session action before task 002 and 003 APPLY their schema:** re-confirm the live
`sprk_communicationthread` + `sprk_communication` schema (and the `sprk_communicationtype` option-set integers)
with Dataverse MCP `describe`, and confirm no `sprk_communicationparticipant` table exists yet. The delta below is
the hypothesis to confirm; the R1 doc + code make it high-confidence, but the create calls in 002/003 must
`describe`-before-write per the standard Dataverse schema pattern. Nothing here blocks authoring 002/003; it only
gates their live apply.

---

## 1. email-r4 `Services/Communication` merged state — CONFIRMED PRESENT on this worktree

Grep/glob-verified (not trusting the sync claim). All required reference files exist:

| File | Present |
|---|---|
| `Services/Communication/CommunicationThreadReadService.cs` | ✅ |
| `Services/Communication/ThreadResolver.cs` | ✅ |
| `Services/Communication/Engine/RegardingFieldMap.cs` | ✅ |
| `Services/Communication/Engine/Rungs/ParticipantCorrelationRung.cs` | ✅ |
| `Services/Communication/Models/ParticipantReference.cs` | ✅ |
| `Services/Communication/Api` → `Api/CommunicationEndpoints.cs` | ✅ (at `src/server/api/Sprk.Bff.Api/Api/CommunicationEndpoints.cs`) |
| `Services/Communication/Access/CommunicationAccessFilter.cs` | ✅ |
| `Services/Communication/IImpersonatedCommunicationQuery.cs` | ✅ |

The full `Services/Communication/**` tree (119 .cs files: `Access/`, `Acs/`, `Channels/`, `Engine/`,
`Engine/Rungs/`, `Membership/`, `Models/`) is present. **R2 builds additively — do NOT re-create these.**

`ParticipantReference` (Models) is a `sealed record` with `EntityLogicalName` ∈ {`systemuser`, `contact`} +
`RecordId` (Guid), with `SystemUser(id)` / `Contact(id)` factories. This is the reusable person-identity
primitive tasks 003/050 align the junction's two typed lookups to.

---

## 2. `RegardingFieldMap.All` — VERBATIM (task 002 mirrors this exactly)

Source: `src/server/api/Sprk.Bff.Api/Services/Communication/Engine/RegardingFieldMap.cs` lines 13–26.
**11 entities, in ADR-024 priority order**, entity logical name → typed `sprk_regarding*` lookup field:

| # | Entity logical name | Regarding lookup field (on `sprk_communication`; task 002 adds the same name to `sprk_communicationthread`) |
|---|---|---|
| 1 | `sprk_matter` | `sprk_regardingmatter` |
| 2 | `sprk_project` | `sprk_regardingproject` |
| 3 | `sprk_invoice` | `sprk_regardinginvoice` |
| 4 | `sprk_servicerequest` | `sprk_regardingservicerequest` |
| 5 | `sprk_workassignment` | `sprk_regardingworkassignment` |
| 6 | `sprk_event` | `sprk_regardingevent` |
| 7 | `sprk_budget` | `sprk_regardingbudget` |
| 8 | `sprk_analysis` | `sprk_regardinganalysis` |
| 9 | `sprk_organization` | `sprk_regardingorganization` |
| 10 | `account` | `sprk_regardingaccount` |
| 11 | `contact` | `sprk_regardingperson` |

> Note the two non-`sprk_`-prefixed entities: `account` → `sprk_regardingaccount`, `contact` →
> `sprk_regardingperson` (the lookup field for contact is **`sprk_regardingperson`**, NOT `sprk_regardingcontact`).
> Task 002 creates these 11 typed lookups **on the thread** pointing at the same 11 target entities.

---

## 3. `sprk_communicationthread` schema-as-built + the exact task-002 delta

**As-built attributes** (from R1 `messaging-schema-spec.md`, verified-live 2026-07-16):

| Attribute | Type | Notes |
|---|---|---|
| `sprk_name` | Text (200) | primary; thread topic, one-shot `ThreadResolver.BuildTopic()` at create; user-editable |
| `sprk_threadtype` | Choice (local) | Record-Anchored=100000000, Direct 1:1=100000001 |
| `sprk_privacystate` | Choice (local) | Open=100000000, Private=100000001 |
| `sprk_privacyeffectivefrom` | DateTime | nullable; point-forward privacy switch |
| `sprk_regardingrecordid` | **Text (100)** | denormalized regarding quartet — anchor GUID |
| `sprk_regardingrecordtype` | **Text (100)** | 🚫 **MUST-NOT-RETYPE** — anchor entity logical name |
| `sprk_regardingrecordname` | Text (400) | denormalized display name |
| `sprk_regardingrecordurl` | Text (400) | denormalized deep link |

**CONFIRMED ABSENT today** (this IS the task-002 delta — all ADDITIVE, non-breaking):

- ❌ NO typed `sprk_regarding{...}` lookups on the thread (the 11 from §2) → 002 ADDS all 11.
- ❌ NO **Lookup discriminator** field (RegardingResolver needs a Lookup binding; the existing
  `sprk_regardingrecordtype` is **Text**, not a Lookup) → 002 ADDS a new Lookup discriminator field.
- ❌ NO **naming-edited marker** (auto-vs-user-edited flag gating `BuildTopic` re-derive, FR-07) → 002 ADDS it.
- ❌ NO **default-thread marker** (FR-09 lazy per-record default) → 002 ADDS it.
- ❌ **Q2 negative check** — NO `category`, NO `tags`, NO `description` on the thread. **002/003 MUST NOT add
  them** (owner Q2: threads = regarding + name only; revisit post-UAT).

### 🚫 MUST-NOT-RETYPE (NFR-04, spec Out-of-Scope)
`sprk_communicationthread.sprk_regardingrecordtype` is **Text** and MUST stay Text. Retyping it to a Lookup is
**breaking** — `ThreadResolver`, membership derivation, and the timeline filters all read the Text field. The
002 discriminator is a **NEW, separately-named Lookup field**, never a retype of this one.

### Columns that already exist — do NOT recreate (002)
`sprk_name`, `sprk_threadtype`, `sprk_privacystate`, `sprk_privacyeffectivefrom`, `sprk_regardingrecordid`,
`sprk_regardingrecordtype`, `sprk_regardingrecordname`, `sprk_regardingrecordurl`.

---

## 4. `sprk_communication` schema-as-built + task-003 net-new confirmation

**Read/participant dependencies CONFIRMED present** (grounded in code — field names referenced in
`CommunicationService.cs`, `CommunicationThreadReadService.cs`, `MessagingIngestor.cs`,
`IncomingCommunicationProcessor.cs`, plus R1 as-built doc):

| Attribute | Type | Grounding |
|---|---|---|
| `sprk_communicationtype` | Choice (global) | `CommunicationThreadReadService.cs:43` (`TypeField`); Email=100000000 … Message=100000004 |
| `sprk_sentby` | Lookup → `systemuser` | `CommunicationService.cs:1566` (`new EntityReference("systemuser", ...)`) |
| `sprk_sentat` | DateTime | `CommunicationThreadReadService.cs:45` (`SentAtField`) |
| `sprk_regardingperson` | Lookup → `contact` | `CommunicationService.cs:1719` (`contact` → `sprk_regardingperson`); the contact member of `RegardingFieldMap.All` |
| `sprk_from` | **Text** (`;`-joined) | `CommunicationService.cs:706,1550` — single from address as text |
| `sprk_to` | **Text** (`;`-joined) | `CommunicationService.cs:705,1549` (`string.Join("; ", request.To)`) |
| `sprk_cc` | **Text** (`;`-joined) | `CommunicationService.cs:721,1585` |
| `sprk_bcc` | **Text** (`;`-joined) | `CommunicationService.cs:1587,1686` |
| `sprk_subject`, `sprk_body`, `sprk_bodyformat` | Text / Text / Choice | `CommunicationService.cs:705–708`; `sprk_bodyformat` Plain=100000000/HTML=100000001 |
| `sprk_communicationthread` | Lookup → `sprk_communicationthread` | message→thread lookup (R1 as-built; `_sprk_communicationthread_value` at read `:39`) |

> **This confirms the CC-2 problem statement**: participant data is **NOT queryable** today — `sprk_from/to/cc/bcc`
> are `;`-joined **TEXT** blobs. There is no per-person structure and no role precision. `sprk_sentby`
> (systemuser) + `sprk_regardingperson` (contact) are the only typed person lookups, and only for the primary
> sender/regarding — not To/Cc/Bcc recipients.

**CONFIRMED: NO `sprk_communicationparticipant` junction exists yet.** No such table/file/reference anywhere in
the repo. **Task 003 creates it net-new** — the exact 6-field schema is locked in spec FR-08:

- `sprk_communication` — Lookup → `sprk_communication` (**required**, message-grain parent)
- `sprk_systemuser` — Lookup → `systemuser` (nullable)
- `sprk_contact` — Lookup → `contact` (nullable) — exactly one of systemuser/contact set for a resolved person
- `sprk_role` — Choice {From, To, Cc, Bcc} (integers proposed in §6)
- `sprk_addresstext` — Text (raw email; unresolved parties + provenance)
- `sprk_isresolved` — Boolean (false when no person lookup set)
- `sprk_name` — primary Text (`"{personDisplay|address} — {role}"`)

---

## 5. Compose / participant field mapping (context for 003/050/060)

Persist path `CommunicationService.cs:705–727` (send) + `MessagingIngestor.cs:169–185` (inbound). Recipients are
written as `string.Join("; ", ...)` into TEXT columns — i.e. `sprk_from/to/cc/bcc` are the raw `;`-joined
addresses that task 050's participant-index write must parse and resolve (reusing
`ParticipantCorrelationRung.QueryContactByEmailAsync`) into junction rows.

---

## 6. `sprk_role` choice-integer plan (proposed for task 003 — deterministic)

**Spaarke option-set convention = 100000000-base sequential** (confirmed across every existing local choice in the
R1 schema doc: `sprk_threadtype` 100000000/1, `sprk_privacystate` 100000000/1, `sprk_privilegeclassification`
100000000/1/2, `sprk_bodyformat` 100000000/1, global `sprk_communicationtype` 100000000…100000004).

**Proposed `sprk_role` local Choice integers** (order = From, To, Cc, Bcc per FR-08):

| Member | Integer |
|---|---|
| From | **100000000** |
| To | **100000001** |
| Cc | **100000002** |
| Bcc | **100000003** |

Rationale: convention-aligned, sequential, deterministic. Downstream code reads by logical name (not hardcoded
int), but 003 records the actual assigned values at create time. **Live-verify caveat**: task 003 should
`describe`-confirm these four integers aren't already taken in a pre-existing collision before `publish` (standard
R1 task-001 pattern) and adjust + note if any collide.

---

## 7. Do-not-recreate / do-not-retype summary (the load-bearing outputs)

- **002 do-NOT-recreate**: the 8 existing thread columns in §3.
- **002 MUST-NOT-RETYPE**: `sprk_communicationthread.sprk_regardingrecordtype` (Text — keep; add a *new* Lookup
  discriminator instead).
- **002 MUST-NOT-ADD (Q2)**: category, tags, description.
- **002 ADDS**: 11 typed `sprk_regarding*` lookups (§2 verbatim) + 1 new Lookup discriminator + naming-edited
  marker + default-thread marker.
- **003 net-new**: `sprk_communicationparticipant` + its 6 fields (FR-08); `sprk_role` integers per §6.
- **Nothing live was changed by this task** (read-only spike).

---

## Acceptance-criteria trace

- ✅ Thread live attrs enumerated + absence of typed regarding lookups / Lookup discriminator / naming-edited
  marker / default-thread marker confirmed (§3).
- ✅ `sprk_regardingrecordtype` = Text, flagged MUST-NOT-RETYPE; category/tags/description ABSENT (Q2) (§3).
- ✅ `sprk_communication` attrs enumerated; NO participant junction exists yet (003 net-new) (§4).
- ✅ Five (eight) `Services/Communication/**` reference files present; `RegardingFieldMap.All` verbatim (§1–§2).
- ✅ Deterministic convention-aligned `sprk_role` {From,To,Cc,Bcc} integers proposed (§6).
- ✅ NEGATIVE: no Dataverse attribute/entity/option-set and no source code created/updated/deleted.
- ⚠️ Live MCP re-verification deferred (MCP unavailable) — owner/next-session gate before 002/003 apply.
