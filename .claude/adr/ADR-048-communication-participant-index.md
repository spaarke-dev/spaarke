# ADR-048: Communication Participant Index — Message-Grain Junction, Two Typed Lookups (Concise)

> **Status**: Accepted
> **Domain**: Communication (queryable participant index over `sprk_communication`) — schema + identity modeling
> **Last Updated**: 2026-07-19
> **Source project**: `messaging-communication-app-r2` (Communication Workspace) — `projects/messaging-communication-app-r2/spec.md` FR-08 + §ADR Tensions + Owner Clarifications Q-A/Q-C/Q-D
> **Cross-references**: complies-with-intent with **ADR-034** (user-record membership / `(personId, personIdType)` tuple — the central citation); indexes the **ADR-045**/**ADR-046** communication + thread model; orthogonal to **ADR-024** (regarding family); **ADR-032** (Null-Object kill-switch, if the write is gated); **ADR-038** (seam tests = DoD); **ADR-027** (managed-solution placement). Sibling **ADR-047** reserved for `spaarke-notification-spine-r1` (NOT claimed here).

---

## Context

R1 (ADR-046) made every message a `sprk_communication` and gave conversations a first-class `sprk_communicationthread`. But **who participated in a message is not queryable**: sender/recipients are stored as `;`-joined **TEXT** (`sprk_from`/`sprk_to`/`sprk_cc`/`sprk_bcc`), and only the primary `sprk_sentby` / `sprk_regardingperson` lookups are typed. The Workspace's person filter (`GET /api/communications?participant=…`, FR-02) needs an exact, role-aware, indexed answer to "which communications involve person X" — a `LIKE` over text can't provide it (no index, no identity resolution, breaks on rename/merge, no role precision).

ADR-034 solved the sibling problem (person↔**record** membership) with a materialized junction keyed by a `(personId, personIdType)` **tuple**, chosen specifically to avoid an awkward polymorphic lookup across its **6** identity target tables and to forbid fuzzy text-name matching. R2 needs the analogous person↔**communication** index. This ADR records that decision — a **message-grain** junction with **two typed person lookups** — and argues, on the record, why that honors ADR-034's *intent* (path C, comply-with-intent) rather than amending it. The design is owner-locked (spec FR-08, 2026-07-18); this ADR documents it at the point of decision (CLAUDE.md §6.5 forbids deferring the exception).

---

## Decision

Ship **`sprk_communicationparticipant`** — a queryable participant index — governed by five coupled rules:

1. **Message grain (Q-A).** One row per **(message × person/address × role)**; the parent is a **required** lookup to `sprk_communication` only. Thread-level participation is **DERIVED by rollup** over these rows — never written as thread-grain rows. This is the grain the person filter needs and the cheapest correct one; a both-grain design was rejected (see Consequences).

2. **Two typed nullable person lookups — ADR-034 path C, comply-with-intent (the load-bearing decision).** Identity = `sprk_systemuser` (→`systemuser`) **XOR** `sprk_contact` (→`contact`): **exactly one set** for a resolved person, **both null** for an unresolved external address. ADR-034 chose a Guid+type tuple to dodge a **6-target** polymorphic lookup and to ban text-name matching. Here the person space has only **2** targets, so two typed lookups deliver ADR-034's *intent* (typed identity, no fuzzy text-name matching) **plus** referential integrity (FK cascade/removelink) and DataGrid person-chip auto-derivation that a raw-Guid tuple cannot. This is **comply-with-intent (path C)**, NOT a path-B amendment of ADR-034 and NOT a polymorphic lookup.

3. **Unresolved external rows are first-class (Q-D).** When capture can't resolve an address to a `systemuser`/`contact`, write a row with **both person lookups null**, `sprk_isresolved=false`, and `sprk_addresstext` = the raw email. External parties stay filterable and back-fillable when a `contact` is later created; `participant=` never silently omits them. `sprk_addresstext` is retained on resolved rows too as provenance.

4. **Role precision.** `sprk_role` is a Choice `{From=100000000, To=100000001, Cc=100000002, Bcc=100000003}` — per-role precision the `;`-joined text fields lack.

5. **Populated by reuse, not a new mechanism.** Rows are written at capture/send (task 050) reusing `ParticipantCorrelationRung.QueryContactByEmailAsync` — the existing email→contact resolution. **No new resolver, no new AI dependency, no new external SDK, no new NuGet.** The write is best-effort/non-fatal and idempotent (re-processing a message creates no duplicate rows).

### ✅ MUST

- **MUST** write participant rows at **message grain** (parent = `sprk_communication`); derive thread participation by rollup. MUST NOT write thread-grain rows.
- **MUST** model person identity as the two typed lookups `sprk_systemuser` XOR `sprk_contact`; exactly one set for a resolved person.
- **MUST** write an unresolved row (`sprk_isresolved=false` + `sprk_addresstext`, both lookups null) for an address that can't be resolved — never drop it.
- **MUST** populate rows by reusing `ParticipantCorrelationRung` resolution; the write is best-effort/non-fatal and idempotent.
- **MUST** treat this table as the source for the `participant=` facet (FR-02) and gate its reads through the same impersonation + 2-rule access filter (ADR-046 / R1 access model).
- **MUST** ship the table in the project managed solution (ADR-027) and cover the write + query with vertical-slice seam tests (ADR-038, task 080).

### ❌ MUST NOT

- **MUST NOT** model person identity as an ADR-034-style Guid+type tuple, a **single polymorphic** person lookup, or a **text index** over `sprk_from/to/cc` (all rejected — see full ADR).
- **MUST NOT** re-introduce fuzzy text-**name** matching for identity (ADR-034's ban carries; email→contact resolution is the only path).
- **MUST NOT** frame the junction as a regarding mechanism (ADR-024) or a second communication pipeline (ADR-045/046) — it is a message-grain participant **index**, orthogonal to both.
- **MUST NOT** let a `systemuser`/`contact` become undeletable — the person lookups use **RemoveLink** delete behavior (clearing the lookup + flipping `sprk_isresolved` is the intended provenance/back-fill path); only the `sprk_communication` parent cascades.
- **MUST NOT** amend ADR-034 (this is path C, not path B).

---

## Consequences

- **Positive**: the person filter becomes exact, indexed, and role-aware; external parties are filterable and back-fillable; referential integrity + DataGrid chip auto-derivation come for free from real typed lookups; the index is populated by reusing shipped resolution with zero new dependency (publish-size delta ≈0). Thread participation is a rollup, so there is one source of truth and no dual-write drift.
- **Cost / risk**: the write lands on the shared `Services/Communication/` persist path (task 050, `parallel-safe:false`) — mitigated by characterization-testing existing email/messaging flows before extending, best-effort/non-fatal semantics, and idempotency. The XOR-exactly-one-set invariant is enforced in code (task 050), not by schema. ACS/Dataverse resolution is eventually consistent for late-created contacts — the `sprk_isresolved=false` + `sprk_addresstext` back-fill path is the reconciliation seam.
- **Coordination**: shares `Services/Communication/` with `email-communication-solution-r4` (merged) — build additively, `/conflict-check` before the BFF wave. `sprk_role` integers are confirmed by a describe-before-write gate at live apply (Dataverse schema authored, live application owner-deferred).

## ADR Tensions (per CLAUDE.md §6.5)

| ADR | Rule | Path | Resolution |
|---|---|---|---|
| **ADR-034** | Person identity uses the `(personId, personIdType)` tuple; no polymorphic parent lookups | **C — comply-with-intent** | ADR-034's tuple exists to avoid a **6-target** polymorphic lookup + to forbid text-name matching. R2's person space is **2** targets, so two typed lookups honor that intent (typed identity, no text-name matching) while adding FK integrity + chip auto-derivation the tuple cannot. Owner-approved 2026-07-18. Not an amendment. |

## Related

| ADR | Relationship |
|---|---|
| ADR-034 (User-record membership) | The identity **intent** this ADR complies with (path C) — typed identity, no text-name matching; central citation |
| ADR-045 / ADR-046 (Communication + ACS thread model) | The communication/thread model this junction **indexes**; not a second pipeline |
| ADR-024 (Regarding family) | Orthogonal — the junction is a message-grain participant index, never a regarding mechanism |
| ADR-032 (Null-Object kill-switch) | If the participant-write path is feature-gated (task 050), it uses symmetric registration |
| ADR-038 (Testing strategy) | Vertical-slice seam tests for the write + `participant=` query are the DoD (task 080) |
| ADR-027 (Provisioning / solution mgmt) | The junction ships in the project managed solution |
| ADR-047 (reserved) | Reserved for `spaarke-notification-spine-r1` — NOT claimed here |

## References

- Full ADR (rationale, alternatives, revision log): `docs/adr/ADR-048-communication-participant-index.md`
- Source: `projects/messaging-communication-app-r2/spec.md` FR-08 + §ADR Tensions + Owner Clarifications (Q-A/Q-C/Q-D); schema: `projects/messaging-communication-app-r2/notes/003-communicationparticipant-schema.md` + `scripts/Deploy-CommunicationParticipantSchema.ps1`
- Complies-with-intent: ADR-034 (`.claude/adr/ADR-034-user-record-membership.md`)
