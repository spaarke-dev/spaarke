# Unified Access Control R2 — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-08-21
> **Source**: [`design.md`](design.md) · [`notes/design-register.md`](notes/design-register.md) · [`notes/investigation/01`–`10`](notes/investigation/)

## Executive Summary

Spaarke has two disjoint authorization systems sharing a data resolver and nothing else, neither of which enforces what its documentation claims. This project unifies them into a single evaluator returning `(recordId → rights)` with explicit, reviewable policy; closes 22 confirmed enforcement gaps; makes Secure Project actually isolate; and delivers parent→child access inheritance as a consequence of the model rather than a bespoke mechanism.

## Scope

### In Scope
- Phase 0 enforcement remediation (22 confirmed findings)
- One access evaluator returning `(recordId → rights)` for both principal kinds
- Impersonated Dataverse reads for systemuser callers (replaces column pattern-matching)
- Explicit access-conferring allow-list for contact-typed **and** org-typed lookups
- Core-vs-child inheritance via denormalized core ancestor
- Secure Project rework — BU restructure + service-account ownership + share-only
- Access Permission PCF as the unified Manage Access surface (contacts, organizations, **system users**)
- Create Project wizard Secure step rework
- Attestation logging

### Out of Scope
- MDA authorization (Dataverse enforces natively)
- AI-search security trimming for contacts (finding A-21 files to the AI/indexing owner)
- Field-level visibility / show-hide by permission
- Break-glass emergency access
- Organization-hierarchy cascade of grants
- GDPR erasure of grant rows and the attestation log
- Project/matter closure and archival semantics beyond fixing the broken cascade (A-12)

### Affected Areas
- `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/**` — evaluator, principal resolution, grant reads
- `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/**` — endpoints, scope injection, delegation checks
- `src/server/api/Sprk.Bff.Api/Api/FileAccessEndpoints.cs`, `PermissionsEndpoints.cs`, `DataverseDocumentsEndpoints.cs` — Phase 0
- `src/server/shared/Spaarke.Core/Auth/**` — `AuthorizationService`, `OperationAccessPolicy`
- `src/server/shared/Spaarke.Dataverse/**` — impersonated read source, POA seam consolidation
- `src/client/shared/Spaarke.UI.Components/src/components/AccessGrantModal/**` — Manage Access PCF
- `src/client/shared/Spaarke.UI.Components/src/components/CreateProjectWizard/**` — Secure step
- `src/client/pcf/TrackingFieldTrio/**` — `sprk_accesspermission` (Restricted)

---

## Requirements

### Phase 0 — Enforcement remediation

All findings independently confirmed; evidence at [`notes/investigation/10-finding-confirmations.md`](notes/investigation/10-finding-confirmations.md).

1. **FR-01** (High): `GET /api/documents/{id}/download` must authorize the caller against the document before streaming — Acceptance: an authenticated user without access to document X receives 403; the existing R1 attack scenario fails.
2. **FR-02** (High): `AuthorizationService` must evaluate access **as the caller**, not as the application — Acceptance: `userAccessToken: null` no longer reaches `IAccessDataSource` on a caller-scoped path; a user without Dataverse access to a record is denied.
3. **FR-03** (High): every operation string passed to an authorization filter must exist in `OperationAccessPolicy` — Acceptance: automated test enumerates all filter call-sites and asserts each key resolves; `"read"`, `finance.read`, `finance.confirm`, `entity.associate_document` all resolve.
4. **FR-04** (High): the `AccessRights.Read` ceiling must not defeat Write+ policies — Acceptance: `canwritefiles` / `canmanagecontainers` succeed for an authorized user.
5. **FR-05** (High): `PermissionsEndpoints` must return caller-scoped capabilities — Acceptance: a user without access receives `CanPreview=false`.
6. **FR-06** (High): external grant expiry must be enforced — Acceptance: a grant with `sprk_expiresdate` in the past confers no access. If deferred, the expiry input must be removed from the UI (no promise-shaped no-op).
7. **FR-07** (High): grant/revoke/invite/invite-and-grant/close-project/provision-project must enforce the delegation rule — Acceptance: a caller without **Write** on the target record receives 403 (see FR-20).
8. **FR-08** (High): `PATCH /api/v1/external/todos/{id}` must scope-check the record — Acceptance: a caller with zero accessible roots cannot modify a To Do by GUID.
9. **FR-09** (High, A-11): `/grant` must be idempotent and `/revoke` must deactivate **all** matching active rows — Acceptance: granting twice then revoking once leaves zero active grants; effective access is `None`.
10. **FR-10** (High, A-17): the FetchXML guard must reject same-entity self-joins — Acceptance: a self-join projecting aliased columns of out-of-scope rows is rejected, not scoped.
11. **FR-11** (High, A-14): anonymous share links must be tracked and revocable, or disabled — Acceptance: no code path mints a permanent, unrevocable anonymous link.
12. **FR-12** (High, A-18): workforce contact-by-email resolution must apply the CIAM no-hijack `oid` check — Acceptance: an email matching a contact bound to a *different* `oid` is denied.
13. **FR-13** (Medium, A-19): the access cache key must include auth mode — Acceptance: a service-principal snapshot is never served to an OBO caller.
14. **FR-14** (Medium, A-10): membership paging must be deterministic and complete — Acceptance: FetchXML carries an `<order>`; page N+1 returns no skipped rows; a caller with >500 memberships resolves all of them.
15. **FR-15** (Medium, A-12): the close-project cascade must deactivate contact **and** organization grant rows — Acceptance: closure returns 200 and all active grants for the project are deactivated.
16. **FR-16** (Medium, A-13): the SPE membership revoke matcher must match on the same key membership is written with — Acceptance: a revoke removes the corresponding container permission, verified by test.
17. **FR-17** (Low, A-15/A-16/A-22): remove the orphaned `AccessibleRecordSetAuthorizationFilter`; bound the `in`-clause per FR-25; fix `LookupUserMembershipNodeExecutor`'s `["*"]` argument — Acceptance: no dead filter; node executes without throwing.
18. **FR-18** (Blocking): restructure the business-unit tree per design §5.2 — Acceptance: `Spaarke Operations` and `Secure Projects` exist as Level-1 siblings; no non-admin user remains in root; root-owned core records are re-homed to Operations; a user in the Operations subtree cannot read a record owned in `Secure Projects`.

### Phase 1 — One evaluator

19. **FR-19**: the evaluator returns **`(recordId → rights)`** for every principal kind, composed as additive terms (highest wins) followed by ordered vetoes: deny-list → Restricted → Secure — Acceptance: levels are carried for matters and work assignments, not projects alone; a `View Only` grant does not permit a write on any route.
20. **FR-20**: systemuser callers derive root sets from **Dataverse's own answer** via impersonated read, replacing column pattern-matching — Acceptance: a record shared with the caller appears; a record matched only by `owningbusinessunit` where the caller's role does not cover it does **not** appear; the negative canary (NFR-04) passes.
21. **FR-21**: `sprk_accesspermission = Restricted` denies **all** contact principals regardless of grant source — Acceptance: a contact with an explicit Full Access grant is denied on a Restricted record.
22. **FR-22**: `sprk_issecure = true` suppresses derived-member and org-expansion terms **for every principal kind**, before the max — Acceptance: a Type 1 user whose contact holds a standing or organization grant does not derive access to a secure project.
23. **FR-23**: a deny list vetoes access after the max, keyed by `(contact | organization) × (record | record's organization)` — Acceptance: a contact on the No Access List for organization X is denied on every record referencing X, even holding Full Access. **"No Access" is a veto, never a level.**

### Phase 2 — One definition of member

24. **FR-24**: replace the `sprk_assigned*` naming convention with an explicit column allow-list covering contact-typed **and** organization-typed lookups — Acceptance: an organization referenced in a non-allow-listed lookup (e.g. opposing counsel) confers no access; adding a new conferring column is a registry edit, not a rename.
25. **FR-25**: Standing Grant contributes at the subject's `sprk_accesspermissiongrant` baseline (View Only `100000000` / Collaborate `100000001` / Full Access `100000002`) for **contacts and organizations** — Acceptance: a contact with baseline `View Only` and a standing grant receives exactly `Read`.

### Phase 3 — Child inheritance

26. **FR-26**: `RegardingResolver` denormalizes the **ultimate core-record ancestor** onto every child record, re-stamped on reparent — Acceptance: a To Do regarding an Email regarding a Matter carries `sprk_regardingmatter`; access resolves in one hop.
27. **FR-27**: child records inherit their core ancestor's rights; `sprk_event`, `sprk_communication` and `sprk_todo` become scoped child modules — Acceptance: a contact with access to Project 1 sees its invoices, events, communications and To Dos; per-child revocation via FR-23 removes one child without affecting the parent.

### Phase 4 — Secure Project, Manage Access, wizard

28. **FR-28**: Secure Projects are owned by a service account in the `Secure Projects` BU; all human access is by explicit Dataverse share — Acceptance: no principal reads a secure project via ownership or business unit; a shared user reads it in **both** the MDA and the SPA.
29. **FR-29**: the Manage Access PCF adds a **"+ User"** system-user picker writing Dataverse shares, and surfaces service-user assignment and BU alignment — Acceptance: an internal user can be granted and revoked from the modal.
30. **FR-30**: every Current Access row displays **provenance** (share · explicit grant · organization grant · standing grant · derived-from-field) and level; Secure and Restricted states render suppressed rows as suppressed with a reason — Acceptance: the modal answers "who can see this record and why" without leaving the form.
31. **FR-31**: the Create Project wizard Secure step aligns to FR-28, removes the retired Power Pages claim, and drops the permanence warning — Acceptance: copy matches implemented behaviour; the secure designation is reversible.

### Phase 5 — Attestation

32. **FR-32**: an append-only access event log records grant and deny state changes; derived access is reconstructed by evaluator replay over Dataverse field audit — Acceptance: "who could see record X on date D" is answerable; derived access is **not** materialized into rows.

### Non-Functional Requirements

- **NFR-01**: Fail-closed everywhere. Any error, unresolved principal, unknown operation, or missing access data denies. No path may fail open.
- **NFR-02**: No regression in request cost — the impersonated root-set source uses the same 3 Dataverse round trips per request as today's membership queries.
- **NFR-03**: Result caps must never be silent. When a result set is capped, the user sees **"Only 5,000 records displayed"**.
- **NFR-04**: A negative canary test gates Phase 1: an impersonated low-privilege read must return a **strict subset** of, and **strictly fewer** rows than, the same query app-only. Equality means impersonation is inert and the build fails.
- **NFR-05**: A standing assertion verifies no security role grants a depth on `sprk_project` / `sprk_matter` that reaches the `Secure Projects` BU. A role edit that re-opens secure projects must fail the build, not ship.
- **NFR-06**: BFF publish size ≤ 60 MB compressed (baseline ~44.96 MB incl. PDBs); measured and reported per BFF-touching task.
- **NFR-07**: Phase 0 builds the characterization suite for the access path before Phase 1 changes behaviour. The current baseline is near-zero.

---

## Technical Constraints

### Applicable ADRs
- **ADR-003** — authorization seams (`IAccessDataSource`, `OperationAccessRule`, fail-closed)
- **ADR-034** — user↔record membership, 1-hop cap, identity-table discovery
- **ADR-028 A1/A2** — auth architecture; CIAM SPA and workforce Teams host; broker-only
- **ADR-024** — polymorphic child→parent regarding model (the ancestor-stamp mechanism)
- **ADR-008** — endpoint filters for authorization · **ADR-010** — DI minimalism · **ADR-038** — testing strategy (NFR-04 is a KEEP-path seam test)

### MUST Rules
- ✅ MUST fail closed on every error path (NFR-01)
- ✅ MUST evaluate vetoes after the additive max, never as levels
- ✅ MUST apply the access-conferring allow-list to contact **and** organization lookups
- ❌ MUST NOT materialize derived access into grant rows
- ❌ MUST NOT issue an app-only query on an access-scoped read path (`RetrieveMultipleImpersonatedAsync` already enforces this)
- ❌ MUST NOT reach beyond 1 hop from child to core ancestor

### Existing Patterns
- `Spaarke.Dataverse/DataverseImpersonation.cs` + `DataverseWebApiService.RetrieveMultipleImpersonatedAsync:953-989` — the impersonated read seam (built, live)
- `Infrastructure/ExternalAccess/AccessibleRecordSetService.cs` — the fail-closed gate to extend
- `Services/Ai/PlaybookSharingService.cs:302-350` — the POA client that already does team grants and revoke

## Placement & New Components (CLAUDE.md §10 / §11)

```xml
<hot-path-declaration>
  <bff>Y</bff>
  <spaarkeai>N</spaarkeai>
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```

| New component | Existing overlap | Extend instead? | Cost-of-doing-nothing |
|---|---|---|---|
| `ImpersonatedRootSetSource` | `RetrieveMultipleImpersonatedAsync` (live) | No — thin new source over the existing primitive | SPA access keeps disagreeing with the MDA in both directions |
| Deny-list store | none found | No | Ethical walls and per-child revocation are unimplementable |
| Access event log | `sprk_externalrecordaccess` (current-state only) | No — that table has no history and no derived rows | Point-in-time attestation is unanswerable for privilege logs |
| Consolidated POA seam | `IDataverseAccessGrantService` + `PlaybookSharingService` client | **Yes — consolidate the two** | A third POA client; internal shares unrevocable from the UI |

No new NuGet packages. Publish-size delta expected ≈0.

## ADR Tensions (CLAUDE.md §6.5)

| ADR | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| ADR-003 | "two seams only"; "new auth logic MUST be an `IAuthorizationRule`"; "MUST NOT create new service layers for auth"; "cache per-request only" | None describe reality — the external stack and `CachedAccessDataSource` already diverge, and the rules would force the evaluator into a shape that cannot carry rights or vetoes | **B** | Amend/supersede with a two-surface ADR that describes what is actually enforced |
| ADR-034 | Resolver is canonical for membership; discovery admits all 6 identity tables | Discovery is correct for AI scoping and over-inclusive for authorization; the allow-list must become first-class and per-surface | **B** | Amend. The 1-hop cap needs no exception — FR-26 makes every chain one hop |
| ADR-028 A2 | Workforce callers resolve to systemuser → ADR-034 membership | FR-20 replaces the derivation with Dataverse's real answer | **B** | Narrow amendment: the token stays workforce, only the derivation policy changes |

## Success Criteria

1. [ ] All 22 Phase 0 findings closed — Verify: regression test per finding
2. [ ] One evaluator; `AuthorizationService` path repaired or retired — Verify: no caller-scoped path passes `userAccessToken: null`
3. [ ] Negative canary green — Verify: NFR-04 test in CI
4. [ ] Role-depth assertion green — Verify: NFR-05 test in CI
5. [ ] A user in the Operations subtree cannot read a `Secure Projects`-owned record — Verify: live dev test
6. [ ] A shared user reads a secure project in both MDA and SPA — Verify: live dev test
7. [ ] Manage Access answers "who can see this and why" with provenance per row — Verify: UAT
8. [ ] Contact with Project access sees its invoices, events, communications and To Dos — Verify: live dev test
9. [ ] Point-in-time attestation answerable — Verify: replay a historical date

## Dependencies

### Prerequisites
- BU restructure + user migration + record re-homing (FR-18) — **blocking for Phase 4**
- `prvActOnBehalfOfAnotherUser` granted to the BFF application user, recorded in the deployment runbook
- BFF application user remains Org-scoped (impersonated privileges are the intersection)
- `sprk_accesspermissiongrant` populated for existing contacts and organizations
- Field Security Profile membership for `contact.sprk_standinggrant`
- **A dedicated non-admin test user** in the Operations subtree, holding the Operations-level role and NO Global-read role. Required by FR-28 and NFR-05 — isolation cannot be verified from an administrator account, which sees every record by definition

### External
- Multitenant app registration + per-customer admin consent (Type 2 workforce sign-in)
- Notification of `spaarke-SPA-external-access-platform-r3` (draft assumes the dual-plane model)

## Owner Clarifications

| Topic | Question | Answer | Impact |
|---|---|---|---|
| Derived access default | Default-on, or opt-in per contact? | **Default-on; Secure is the veto** | Standing Grant becomes a standing arrangement, not the enabling switch |
| Level precedence | How do conflicting levels resolve? | **Highest wins** | Additive max, then vetoes |
| SPA identity | Do licensed users reach the SPA as systemusers? | **Types 1 & 2 via workforce Entra; Type 3 via CIAM.** Licence is irrelevant to SPA access | Workforce plane retained; derivation policy changed instead |
| Record taxonomy | Which records need direct grants? | **Core**: project, matter, work assignment, service request. **Child**: invoice, communication, document, event, to-do, analysis | Matter does NOT inherit from Project |
| Child revocation | Acceptable that Contact A sees an invoice via Project 1? | **Yes, and revocable at invoice level** | Requires the deny term (FR-23) |
| Ethical wall | Is "No Access" a level? | Data model per owner: No Access List subgrid matched on organization | Implemented as a **veto**, not a level (FR-23) |
| Standing grant scope | Who can hold one? | **Organizations, contacts, and internal workforce**, each with a baseline level | FR-25 |
| Secure Project | Mechanism? | **Secure BU sibling of Operations + service-account owner + share-only** | FR-18, FR-28 |
| BU topology | Local, or restructure? | **Restructure** — Operations and Secure Projects as Level-1 siblings; users at Deep in the Operations subtree | Avoids matrix-data-access dependency |
| Phase 0 scope | Fix findings in other modules? | **Policy-key fixes in scope; A-21 files out** | FR-03, FR-04 in; AI trimming out |
| Result caps | Behaviour above 5,000? | **Show "Only 5,000 records displayed"** | NFR-03 |
| Deferrals | Break-glass, org hierarchy, GDPR? | **All out of scope**; no org-hierarchy cascade | Recorded as known gaps |

## Assumptions

- **Truncation**: caps are display-level and user-visible; the system never silently under-grants without telling the user (NFR-03).
- **A-21 ownership**: AI-search trimming is filed to the AI/indexing owner and gated before contacts gain AI access (register D-4).
- **Contact org change**: person-scoped access is **flagged for review**, not auto-revoked, when a contact's organization changes.

## Unresolved Questions

*All resolved by owner 2026-08-21:*

- ✅ **Level-2 role granularity** — security roles are assigned **at the Operations level**; one Deep-scoped role covers Operations and its Level-2 children. No per-BU roles.
- ✅ **Global read** — accepted for the Microsoft platform service accounts (`#`-prefixed, incl. `RelevanceSearch`); unavoidable and nobody authenticates as them. Secure records may surface in Dataverse relevance search: **accepted risk**.
  > ⚠️ **Constraint recorded**: Global read and "sees only granted secure projects" are **mutually exclusive** — Dataverse has no per-record deny (design §5). Any principal holding Global read (System Administrator included) sees every secure project by definition. Isolation is therefore **only testable with a non-admin test user** — see Prerequisites. An optional BFF-side veto could scope the SPA view for admins, at the cost of deliberate MDA/SPA divergence; **not in scope** unless requested.
- ✅ **Access event log retention** — no bespoke retention mechanism (owner: "whatever is easiest"). The log inherits the environment's Dataverse audit retention. GDPR erasure remains out of scope and is recorded as a known gap: an append-only attestation log is in tension with right-to-erasure and needs a deliberate policy before any production privacy commitment.

---
*AI-optimized specification. Original design: `design.md`*
