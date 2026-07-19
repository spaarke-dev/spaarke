# Task 003 — `sprk_communicationparticipant` junction schema (authored, LIVE APPLY DEFERRED)

> **For**: tasks 004 (junction schema ADR), 050 (participant-index write), 051 (`participant=` facet),
> 081 (architecture doc), and the §11 Component Justification.
> **Deliverable**: the executable Web-API/PowerShell create script —
> [`scripts/Deploy-CommunicationParticipantSchema.ps1`](../scripts/Deploy-CommunicationParticipantSchema.ps1).
> This note is the maker-ready inventory + the live-apply gate.
> **Grounds**: spec FR-08 (locked 2026-07-18) · task 001 audit (`notes/001-phase0-schema-audit.md`) ·
> R1 sibling `sprk_communicationattachment` schema (`../x-email-communication-solution-r1/notes/communicationattachment-entity-schema.md`) ·
> `ParticipantReference.cs`.
> **Author**: task-execute 003, 2026-07-19. STANDARD rigor, `sonnet@high`.

---

## ⚠️ LIVE APPLY DEFERRED (describe-before-write gate)

Dataverse MCP was **UNAVAILABLE** this session, so the live `describe`/create/publish could not run.
The schema is **fully authored** (the script above is the artifact); only the **live apply** is gated —
the standard R1 "built + verified-live-deferred" pattern (same gate task 001 left).

**Owner / next-session action before this schema is relied on by 050/051:**
1. `az login` (Dataverse-audience token).
2. Run `Deploy-CommunicationParticipantSchema.ps1 -DataverseOrg https://spaarkedev1.crm.dynamics.com -WhatIf`
   — the `-WhatIf` pass runs the **describe-before-write** existence check and confirms no
   `sprk_communicationparticipant` exists (task 001 already concluded it is net-new; this re-confirms live).
3. Run without `-WhatIf` (add `-SolutionUniqueName <messaging solution>` to land it in the project's
   unmanaged solution per ADR-022/ADR-027).
4. Verify (script Step 6 / MCP `describe`) the 7 attributes + 3 relationships, and **record the actual
   `sprk_role` integers** here.

**`sprk_role` collision note**: `sprk_role` is a **LOCAL** option set on a **net-new** entity, so
100000000–100000003 are always free within its own scope — no cross-entity collision is possible (unlike the
global-option-set concern in 001 §6). The describe gate is really confirming the *entity* is absent.

---

## Entity

| Property | Value |
|---|---|
| Display / Plural | Communication Participant / Communication Participants |
| Logical name | `sprk_communicationparticipant` |
| Schema name | `sprk_CommunicationParticipant` |
| **Ownership** | **Organization-owned** — mirrors sibling `sprk_communicationattachment`; access inherits from the parent `sprk_communication` via the required lookup + Cascade delete |
| Primary name | `sprk_name` (Text 400) |
| Notes / Activities | No / No (thin intersection; no plugin logic per ADR-002) |
| Grain | **Message** (parent is `sprk_communication`) — Q-A. **No thread-grain lookup** (thread participation is derived by rollup). |

## Fields (LOCKED — spec FR-08; do NOT add/drop/rename)

| # | Logical name | Schema name | Type | Target / members | Required | Delete behavior |
|---|---|---|---|---|---|---|
| 1 | `sprk_name` | `sprk_Name` | Text (400) | primary; `"{personDisplay\|address} - {role}"` | ApplicationRequired | — |
| 2 | `sprk_communication` | `sprk_Communication` | Lookup (N:1) | → `sprk_communication` (message-grain parent) | **ApplicationRequired** | **Cascade** |
| 3 | `sprk_systemuser` | `sprk_SystemUser` | Lookup (N:1) | → `systemuser` | None (nullable) | RemoveLink |
| 4 | `sprk_contact` | `sprk_Contact` | Lookup (N:1) | → `contact` | None (nullable) | RemoveLink |
| 5 | `sprk_role` | `sprk_Role` | Choice (LOCAL) | From=100000000, To=100000001, Cc=100000002, Bcc=100000003 | None | — |
| 6 | `sprk_addresstext` | `sprk_AddressText` | Text (400, Email format) | raw email; unresolved parties + provenance | None | — |
| 7 | `sprk_isresolved` | `sprk_IsResolved` | Boolean (default **No**) | Yes=1 / No=0 | None | — |

**`sprk_role` option-set members + integers** (per task 001 §6 plan, convention = 100000000-base sequential,
matching every existing local Spaarke choice — `sprk_threadtype`, `sprk_privacystate`, `sprk_bodyformat`):

| Member | Integer |
|---|---|
| From | 100000000 |
| To   | 100000001 |
| Cc   | 100000002 |
| Bcc  | 100000003 |

## Relationships (N:1; created via RelationshipDefinitions)

| Relationship schema name | Referenced (1) | Lookup on participant | Required | Delete behavior |
|---|---|---|---|---|
| `sprk_communicationparticipant_communication` | `sprk_communication` | `sprk_communication` | Yes | **Cascade** (delete participant rows when the message is deleted) |
| `sprk_communicationparticipant_systemuser` | `systemuser` | `sprk_systemuser` | No | RemoveLink (deleting a user just clears the lookup; row survives as provenance) |
| `sprk_communicationparticipant_contact` | `contact` | `sprk_contact` | No | RemoveLink (deleting a contact clears the lookup; row survives; can back-fill later) |

**Convention mirrored** (per POML instruction — "match how `sprk_communicationattachment` relates to
`sprk_communication`"): the sibling message-child intersection `sprk_communicationattachment` is
**Organization-owned** with an N:1 **Cascade-delete** parent lookup to `sprk_communication` and inherits
security from the parent. This junction adopts the same ownership + parental Cascade on the message lookup.
It diverges only on the *person* lookups: attachment uses Cascade-**Restrict** to `sprk_document` (protect a
shared document); this junction uses **RemoveLink** to systemuser/contact (a person record must never be
undeletable because a message referenced them, and clearing the lookup while keeping `sprk_addresstext` +
flipping `sprk_isresolved` is exactly the intended provenance/back-fill behavior).

## Resolved vs unresolved rows (Q-D — both creatable at schema level)

| Scenario | `sprk_systemuser` | `sprk_contact` | `sprk_addresstext` | `sprk_isresolved` |
|---|---|---|---|---|
| Resolved internal (Entra user) | set | null | set (provenance) | true |
| Resolved external (contact) | null | set | set (provenance) | true |
| **Unresolved external address** | null | null | **set** | **false** |

The **exactly-one-of systemuser/contact** rule and the isresolved coupling are **write invariants enforced by
task 050**, NOT schema constraints — the schema intentionally allows both lookups null so unresolved external
rows are first-class and `participant=` never silently omits external parties.

## Alignment to `ParticipantReference.cs` (so task 050's write is a clean map)

`ParticipantReference` is a `sealed record` with `EntityLogicalName ∈ {systemuser, contact}` + `RecordId`
(Guid), factories `SystemUser(id)` / `Contact(id)`. The write (050) maps:
`EntityLogicalName=="systemuser"` → set `sprk_systemuser`; `=="contact"` → set `sprk_contact`;
`RecordId` → the lookup id; role → `sprk_role`; the raw address (from `ParticipantCorrelationRung`) →
`sprk_addresstext`; a set person lookup → `sprk_isresolved=true`, else `false`.

## ADR-034 tension (path C — comply-with-intent; documented by task 004, not decided here)

ADR-034's `(personId, personIdType)` tuple exists to avoid a wide polymorphic lookup and to forbid fuzzy
text-name matching. This junction has only **2** person targets (systemuser, contact), so it uses **two typed
nullable lookups** — honoring ADR-034's intent (typed identity, no text-name matching) while adding **FK
integrity** + **DataGrid person-chip auto-derivation** the tuple can't provide. NOT the Guid+type tuple, NOT
a polymorphic lookup. The formal ADR write-up is task 004's deliverable.

## §11 Component Justification (a NEW entity — concrete FR-08 failure modes, not "flexibility")

1. **Existing overlap** — none queryable. `sprk_from/to/cc/bcc` on `sprk_communication` are `;`-joined **TEXT**
   (task 001 §4, grep-confirmed); `ThreadMembershipDerivationService` answers record→people, not
   person→communications; task 001 confirms **no junction exists**.
2. **Extension instead** — no existing structure can be extended into a queryable person→communications index.
   A text-LIKE filter over the `;`-joined fields gives wrong results, no role precision, and can't surface
   external parties — rejected.
3. **Cost-of-doing-nothing** — without the junction the `participant=` facet (FR-02) is impossible, the person
   filter degrades to text-LIKE (wrong results, no role precision), and external parties are invisible
   (spec §New-Components). Named contract failures (FR-08 acceptance), not future-proofing.

## Acceptance-criteria trace (task 003)

- ✅ Entity + EXACTLY 6 fields + primary `sprk_name` authored — no added/dropped/renamed field.
- ✅ `sprk_communication` = **required** Lookup → `sprk_communication` (message grain); **no** thread-grain lookup (Q-A).
- ✅ `sprk_systemuser` (→systemuser) + `sprk_contact` (→contact) = two nullable typed Lookups — not a tuple, not polymorphic (ADR-034 path C).
- ✅ `sprk_role` = Choice {From,To,Cc,Bcc} at 100000000–100000003; `sprk_addresstext` Text; `sprk_isresolved` Boolean.
- ✅ Resolved row (one person lookup, isresolved=true) AND unresolved external row (both null, addresstext set, isresolved=false) both creatable (Q-D) — schema allows both lookups null.
- ✅ NEGATIVE: schema only — no capture/send write (050), no `participant=` join (051), no BFF code; junction is net-new per the 001 audit.
- ⚠️ Live create/publish deferred (MCP unavailable) — owner/next-session gate above; run the script + record actual `sprk_role` integers.

## Unblocks

- **050** — participant-index write (maps `ParticipantReference` → junction rows; reuses `ParticipantCorrelationRung`).
- **051** — `participant=` facet on the filtered `query` endpoint (joins the junction).
- **004** — the schema ADR consumes this inventory + the ADR-034 path-C rationale.
