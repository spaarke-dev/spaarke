# ADR-048: Communication Participant Index — Message-Grain Junction, Two Typed Lookups

> **Status**: Accepted (2026-07-19)
> **Domain**: Communication — queryable participant index over `sprk_communication`
> **Source project**: `messaging-communication-app-r2` (Communication Workspace)
> **Concise version**: `.claude/adr/ADR-048-communication-participant-index.md`
> **Supersedes / amends**: none. Complies-with-intent with ADR-034 (does NOT amend it).

---

## Context

The R2 Communication Workspace must answer "which communications involve person X, in what role?" — the person filter behind `GET /api/communications?participant=…` (spec FR-02). The R1 data model cannot: message participants are stored as `;`-joined **TEXT** (`sprk_from`/`sprk_to`/`sprk_cc`/`sprk_bcc`), and only the primary `sprk_sentby` / `sprk_regardingperson` lookups are typed. A `LIKE` query over those text fields is unindexed, identity-blind (no resolution to a person record), brittle under contact rename/merge, and role-blind. There is no `sprk_communicationparticipant` table anywhere (confirmed by the task-001 Phase-0 audit).

ADR-034 solved the adjacent problem — person↔**record** membership — with a materialized junction (`sprk_userentityassociation`) keyed by a `(personId, personIdType)` **tuple**. That tuple was chosen deliberately: ADR-034 spans **six** identity target tables (`systemuser`, `contact`, `team`, `businessunit`, `account`, `sprk_organization`), and a polymorphic lookup across six targets is awkward; the tuple also let ADR-034 explicitly reject fuzzy text-name matching.

R2 needs the analogous person↔**communication** index. The question this ADR settles: does R2 copy ADR-034's tuple verbatim, or model identity differently? The owner-locked answer (spec FR-08, 2026-07-18) is a **message-grain junction with two typed person lookups**. Because that deviates from ADR-034's literal mechanism, CLAUDE.md §6.5 requires it be surfaced and resolved at the point of decision — this ADR is that record, resolving it as **path C (comply-with-intent)**.

## Decision

Ship `sprk_communicationparticipant`, a message-grain participant index, per the five rules in the concise ADR (message grain; two typed nullable lookups XOR; unresolved rows first-class; role precision; populated by reuse). The schema (authored, live apply owner-deferred):

| Field | Type | Target / detail | Required | Delete behavior |
|---|---|---|---|---|
| `sprk_communicationparticipantid` | Uniqueidentifier | primary key | (auto) | — |
| `sprk_name` | Text (400) | `"{personDisplay\|address} — {role}"` | required | — |
| `sprk_communication` | Lookup (N:1) | → `sprk_communication` (message parent) | **required** | **Cascade** |
| `sprk_systemuser` | Lookup (N:1) | → `systemuser` | nullable | RemoveLink |
| `sprk_contact` | Lookup (N:1) | → `contact` | nullable | RemoveLink |
| `sprk_role` | Choice (local) | From=100000000 / To=100000001 / Cc=100000002 / Bcc=100000003 | none | — |
| `sprk_addresstext` | Text (400, Email) | raw email / provenance | none | — |
| `sprk_isresolved` | Boolean | Yes=1 / No=0 (default No) | none | — |

Organization-owned; N:1 to `sprk_communication` with **Cascade** delete (mirrors the R1 `sprk_communicationattachment` message-child convention); person lookups use **RemoveLink** so an identity record never becomes undeletable and the clear-lookup-keep-address path is the intended back-fill seam.

## The ADR-034 tension — resolved as path C (comply-with-intent)

**Claim**: two typed lookups honor ADR-034's intent better than the tuple does *for a 2-target identity space*.

- ADR-034's tuple exists for **two** reasons: (a) avoid an awkward polymorphic lookup across **six** targets, and (b) forbid fuzzy text-name matching.
- R2's person space is **two** targets (`systemuser`, `contact`) — the ParticipantReference model. At two targets, reason (a) evaporates: two typed nullable lookups (exactly one set) are clean, not awkward.
- Reason (b) is preserved verbatim: identity comes only from email→contact/systemuser resolution (`ParticipantCorrelationRung`), never from name text.
- Two typed lookups additionally deliver what the raw-Guid tuple cannot: **referential integrity** (FK delete behavior) and **DataGrid person-chip auto-derivation** (the grid framework derives filter chips from real Lookup columns; a Guid text column yields no chip).

Therefore the deviation is **comply-with-intent (path C)**: ADR-034 remains correct as written for its 6-target domain; R2's 2-target domain honors its intent with a mechanism ADR-034's own rationale would prefer at this cardinality. This is **not** a path-B amendment and **not** a polymorphic lookup.

## Alternatives Considered

| Alternative | Why rejected |
|---|---|
| **ADR-034 `(personId, personIdType)` Guid+type tuple (verbatim)** | The tuple's driving rationale (6-target polymorphic-lookup avoidance) doesn't apply at 2 targets. A raw-Guid person column loses FK integrity and yields no DataGrid filter chip; `participant={guid}` would still filter but the person-filter UX the Workspace needs (chip) would be impossible. Stricter ADR-letter, worse outcome. |
| **Single polymorphic person lookup** (`Customer`-style / `Owner`-style) | Dataverse polymorphic lookups are limited/awkward and mix `systemuser`+`contact` in one column with weaker query ergonomics; ADR-034 explicitly avoided this shape. Two explicit typed lookups are clearer and directly chip-able. |
| **Text index** — a denormalized `sprk_participantsindex` string on `sprk_communication`, queried by `LIKE` | Exactly ADR-034's rejected "denormalized text column": `LIKE` isn't indexed, identity is unresolved, stale on rename/merge, no role precision. This is the status-quo failure the junction exists to fix. |
| **Thread-grain rows** (parent = `sprk_communicationthread`) | Loses per-message precision — `participant=` could only answer "who is in this thread", not "who sent/received this message". Weaker filter; the message is the natural capture point. |
| **Both-grain rows** (dual lookup to message AND thread) | ~2× write volume + a polymorphic-ish parent ADR-034 cautions against; thread participation is cheaply derivable by rollup over message-grain rows, so the thread-grain rows are redundant. Message grain only (Q-A). |
| **Drop unresolved external addresses** (resolved-only rows) | External-only recipients would be invisible to `participant=` — the filter would silently omit them. Q-D writes an `sprk_isresolved=false` + `sprk_addresstext` row so external parties stay filterable and back-fillable. |

## Consequences

See the concise ADR. In short: exact/indexed/role-aware person filter; external parties first-class; FK integrity + chip auto-derivation free; zero new dependency (publish-size ≈0); thread participation is a single-source rollup. Risk concentrates in the shared-path write (task 050, `parallel-safe:false`) — mitigated by characterization tests, best-effort/non-fatal + idempotent semantics, and seam tests (ADR-038, task 080). The XOR invariant is code-enforced (task 050). `sprk_role` integers are confirmed by a describe-before-write gate at live apply (schema authored; live application owner-deferred, R1 pattern).

## Related

ADR-034 (identity intent, central), ADR-045/ADR-046 (communication + thread model indexed), ADR-024 (regarding family — orthogonal), ADR-032 (Null-Object if gated), ADR-038 (seam tests = DoD), ADR-027 (managed-solution placement). ADR-047 reserved for `spaarke-notification-spine-r1` (not claimed).

## Revision Log

| Date | Change |
|---|---|
| 2026-07-19 | Authored (Accepted) — `messaging-communication-app-r2` task 004. Documents the owner-locked (2026-07-18) participant-index design + the ADR-034 path-C comply-with-intent resolution. |
