# Unified Access Control — R2 · Design

> **Project**: `unified-access-control-r2` · **Branch**: `work/unified-access-control-r2`
> **Portfolio**: [Project #808](https://github.com/spaarke-dev/spaarke/issues/808) under Epic #535 · **Status**: DESIGN
> **Date**: 2026-08-20 · **Supersedes the framing in** [`unified-access-control-cascade.md`](unified-access-control-cascade.md)
> **Evidence base**: [`notes/investigation/01`–`10`](notes/investigation/) · consolidated in [`notes/design-register.md`](notes/design-register.md)

---

## 1. What this project is

Spaarke has **two disjoint authorization systems** that share a data resolver and nothing else:

- `Spaarke.Core/Auth` (`AuthorizationService` + `OperationAccessPolicy` + `IAccessDataSource`) — the subsystem `docs/architecture/uac-access-control.md` calls "UAC".
- `Infrastructure/ExternalAccess/**` (`CallerPrincipalResolver` + `AccessibleRecordSetService` + `ExternalModuleRegistry`) — built by the external-access SPA and Teams projects.

A CIAM contact can never transit the first; internal BFF endpoints never transit the second (verified: zero cross-references). Neither enforces what its documentation claims.

**This project unifies them into one evaluator with explicit, reviewable policy, and closes the enforcement gaps found on the way.** The parent→child access cascade that seeded the project falls out of the unified model as one feature rather than being built as a bespoke mechanism.

### Scope

| In | Out |
|---|---|
| One access evaluator returning `(recordId → rights)` for both principal kinds | MDA authorization (Dataverse enforces natively — no code) |
| Phase 0 enforcement remediation (§8) | AI-plane security trimming for contacts (deferred, D-4) |
| Core-vs-child inheritance | Field-level visibility (deferred, D-3) |
| Secure Project rework | Expiry enforcement beyond the Phase 0 minimum (D-1) |
| Access Permission PCF as the unified Manage Access surface | Project/matter closure semantics (D-2) |
| Attestation logging | |

---

## 2. Hot-path declaration

```xml
<hot-path-declaration>
  <bff>Y</bff>                 <!-- evaluator, impersonated read source, delegation checks, grant/share endpoints -->
  <spaarke-ai>N</spaarke-ai>
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```

**Coordination**: `teams-app-r1` and `spaarke-SPA-external-access-platform-r2` have shipped — this amends settled code rather than colliding with work in flight. `SPA-r3` is a draft that assumes the dual-plane model and must be notified. `/conflict-check` before every BFF PR; `parallel-safe:false` on shared `Infrastructure/ExternalAccess/**` and `Api/ExternalAccess/**` files.

## 3. Placement justification (CLAUDE.md §10 / §11)

| Component | Existing overlap | Extend or new? | Cost of doing nothing |
|---|---|---|---|
| Access evaluator | `AccessibleRecordSetService` (fail-closed, principal-agnostic, ADR-sanctioned decision point) | **Extend in place** — change return type to carry rights; add veto terms | Two evaluators keep diverging; the broader one guards the weaker surface |
| Impersonated root-set source | `DataverseWebApiService.RetrieveMultipleImpersonatedAsync` + `DataverseImpersonation` — **already built and live** | **Reuse** — new thin `ImpersonatedRootSetSource` calling it | Type 1 SPA access keeps disagreeing with the MDA in both directions |
| POA share seam | `IDataverseAccessGrantService` (systemuser-only, no revoke) **and** a duplicate client in `PlaybookSharingService` (does teams + revoke) | **Consolidate the two**, parameterize principal | A third POA client; internal shares unrevocable from the UI |
| Membership resolver | `MembershipResolverService` (ADR-034) | **Reuse unchanged** for the contact term; other consumers need `byRole` | — |
| Attestation log | none | **New** — smallest possible append-only table | "Who could see this matter on 3 March?" is unanswerable; required for privilege logs and breach inquiry |

No new NuGet packages. Publish-size delta expected ≈0; measured per CLAUDE.md §10 bullet 4 (ceiling 60 MB, baseline ~44.96 MB incl. PDBs).

---

## 4. The access model

### 4.1 Surfaces, not principal kinds

The axis that governs risk is **which system enforces**, not who the user is:

| Surface | Enforced by | Consequence |
|---|---|---|
| **MDA** | Dataverse natively (role depth × owner/BU/team + sharing) | Nothing to build. Do not reimplement |
| **SPA / Teams** | **The BFF filter, and nothing else** — all reads are app-only, so Dataverse row security is inert | A bug here is a disclosure, not a nuisance |

### 4.2 User types

| Type | Description | Door | Record permission derives from |
|---|---|---|---|
| **1** | systemuser, Power Apps licence, MDA | workforce Entra | **Dataverse's real answer** (§4.4) ∪ contact grants |
| **2** | customer employee, no licence, has contact | workforce Entra (multitenant, per-customer consent) | `sprk_assigned*` ∪ org. **No business unit** — a contact is not a security principal |
| **3** | external contact, no licence | CIAM | `sprk_assigned*` ∪ org. **No business unit** |

Types 2 and 3 are **identical** on record permission; they differ only by credential. Licensing is irrelevant to SPA access — nothing on this path checks a seat.

### 4.3 Records: core vs child

| Class | Entities | Access |
|---|---|---|
| **Core** | `sprk_project`, `sprk_matter`, `sprk_workassignment`, `sprk_servicerequest` | Direct grants required |
| **Child** | `sprk_invoice`, `sprk_communication`, `sprk_document`, `sprk_event`, `sprk_todo`, analysis | **Inherit** from parent |

**One hop, by construction.** `RegardingResolver` denormalizes the *ultimate core-record ancestor* onto each child, so a To Do regarding an Email regarding a Matter carries `sprk_regardingmatter` directly. Chains never need traversal; ADR-034's 1-hop cap holds unamended. The ancestor stamp must be re-applied on reparent.

Consequence to state plainly for users: **a Matter associated to a Project does not inherit its access.** Both are core. Inheritance flows only core → child.

Accepted behaviour (operator decision): a contact with access to Project 1 sees its Invoice even if that invoice's own assigned attorney is someone else. Per-child revocation is available via the deny term (§4.5).

### 4.4 Type 1 root sets — impersonated read (Option B)

Replace column pattern-matching with Dataverse's own answer, using the **already-live** `MSCRMCallerID` path:

```
ImpersonatedRootSetSource.GetAsync(systemUserId, entityType)
  → DataverseWebApiService.RetrieveMultipleImpersonatedAsync(entitySet, "$select=id", systemUserId)
```

Dataverse applies ownership, role depth, BU, teams, POA shares and hierarchy natively. **3 round trips per request** — identical to today's membership queries. Cacheable per `(systemUserId, entityType)`.

This fixes both error directions: it stops granting BU-matched records to users whose role doesn't cover them, and it starts showing records that were explicitly shared. It also **removes the need for a systemuser allow-list** — there is no approximation left to tame. The contact-grants union stays: grants live in `sprk_externalrecordaccess`, not POA, and Dataverse cannot see them.

`RetrieveMultipleImpersonatedAsync` refuses `Guid.Empty` and cannot silently degrade to app-only.

### 4.5 The evaluator

Returns **`(recordId → rights)`**, not a set of ids. Today's `HashSet<Guid>` structurally cannot carry a level — which is why matters and work assignments have none.

```
ADDITIVE  (union; HIGHEST WINS)
  1. Dataverse answer      Type 1 only, via §4.4
  2. Explicit grant        sprk_externalrecordaccess (contact or org), carries level
  3. Derived member        allow-listed sprk_assigned* lookups → contact + org identities
  4. Org expansion         org identity → active contacts via sprk_contactorganization
  5. Inheritance           child takes its core ancestor's rights

VETO  (after the max; order matters)
  6. Deny list             ethical wall + per-child revocation      → None
  7. Restricted            sprk_accesspermission = Restricted       → None for ALL contacts
  8. Secure                sprk_issecure = true → suppress terms 3 + 4 BEFORE the max,
                           for EVERY principal kind (see §5)
```

**"No Access" is not a level.** Under highest-wins, `max()` would ignore it and an ethical wall would fail silently in exactly the case it exists for. It is a veto.

**Allow-lists apply to both contact-typed AND org-typed lookups.** Today only contact lookups are allow-listed (`FilterToAccessConferringContactRoles`); org expansion would otherwise confer access from *any* organization on the record, including opposing counsel. Replace the `sprk_assigned*` prefix convention with an explicit column registry — a naming convention silently admits `sprk_assignedmonitor` and silently denies `sprk_leadcontact`.

---

## 5. Secure Project

**Requirement**: only specifically granted system users and contacts, overriding any organization or standing grant.

**Platform reality** (verified 2026-08-20 against current Microsoft documentation): Dataverse has **no per-record deny**, GA or preview. Column-level security is the only restrictive primitive. Isolation is achieved by scoping the baseline and granting additively.

### 5.1 Mechanism

- One **`Secure Projects` business unit**; secure records live there. Resolved **by NAME, from
  configuration** — never by GUID, which differs per environment (owner decision 2026-08-25). Absent
  BU = fail closed; never fall back to the root or the caller's BU.
- The BU's **default owner team owns the records**, so they stay there naturally — no
  matrix-data-access dependency, no BU-per-project proliferation.
- **All human access is by explicit Dataverse share**, including the creating attorney's. Nobody gets access by ownership; nobody by business unit.
- For types 2 and 3, the §4.5 veto suppresses derived and org terms — explicit `sprk_externalrecordaccess` grants only.

#### 5.1a Ownership: an owner TEAM, not a service account (decided 2026-08-25)

This section previously specified "a service account in that BU owns the records". **It does not need
one, and should not have one.** Three Dataverse facts settle it:

1. **Every business unit is created with a default owner team**, named after the BU. The team that
   corresponds to `Secure Projects` therefore **already exists** and requires no provisioning.
2. `sprk_project` is user-or-team owned (`ownerid` is an `OWNER` field, confirmed against live
   metadata), so a team is a valid owner.
3. Ownership and privilege are independent concerns in Dataverse.

Team ownership costs **no licence, no credential to rotate, and adds no identity to audit**, all of
which a service account would. The only requirement is that the BFF application user holds `Assign`
and `Write` on `sprk_project`.

**The owner team DOES require a security role** — this is the part that is easy to get wrong. Dataverse
validates that an assignment target holds entity privileges; assigning a record to a team with no
privileges on that entity fails. So:

- Define a dedicated **`Secure Project Owner`** role: Create / Read / Write / Delete / Append /
  AppendTo / Share on `sprk_project` and its child entities, at **Business Unit** depth.
- Assign it to the **`Secure Projects` default owner team only**.
- **Keep that team free of human members.** The role's BU depth means any member would read every
  secure project *by membership*, which is exactly the ownership-derived access this section forbids.
  The team exists to hold ownership, not to confer access.

⚠️ **A POA share is only effective if the user also holds the entity privilege at some depth.** Normal
user roles must therefore **retain Read on `sprk_project` at Basic/User depth**. That grants nothing on
its own — without ownership or a share it matches no records — but stripping it in the name of
"securing" the entity would silently stop sharing from working at all.

**Consequence for NFR-05** — see spec. The assertion as originally written ("no security role may
reach the `Secure Projects` BU") becomes false by construction, because `Secure Project Owner` must
reach it. The assertion must be restated to exempt that one role and instead assert the property that
actually matters: **no role held by a human principal reaches the `Secure Projects` BU, and the owner
team has no human members.**

#### 5.1b Licensed-user access: access teams, not per-user shares (decided 2026-08-25)

Both satisfy "explicit Dataverse share". Access teams are preferred: **one POA row per record instead
of N**, revocation is a single membership delete rather than hunting POA rows, and it is Dataverse's
purpose-built mechanism for per-record sharing to a changing set of users.

Build it on the **existing** POA-with-teams code in `Services/Ai/PlaybookSharingService.cs:302-350`,
consolidated with `IDataverseAccessGrantService` per CLAUDE.md §11 — do **not** write a third sharing
client.

Do **not** create a static per-project owner team. That is BU-per-project proliferation in a different
costume.

#### 5.1c SPE container: a secure-project special case, not a re-architecture (decided 2026-08-25)

**The general BU-based approach stays.** A secure project is the exception: when `sprk_issecure = true`,
the container comes from **the project's own `sprk_containerid`**, provisioned uniquely for it.

This is the right call and it is far smaller than making resolution record-based everywhere. But
"just special-case it" understates the surface slightly, because there are **three** container
resolution strategies in the codebase today, not one:

| # | Strategy | Where | Secure-project behaviour needed |
|---|---|---|---|
| 1 | Acting **user's BU** → `businessunit.sprk_containerid` | 7 client sites; canonical resolver `xrmProvider.getSpeContainerIdFromBusinessUnit`. The BFF deliberately does not resolve this server-side (INV-7) | **special case** → project's own `sprk_containerid` |
| 2 | A single global `ArchiveContainerId` from config | server-side email/communication ingest (`IncomingCommunicationProcessor:868, 991`) | **special case** → the parent secure project's container, else secure attachments land in the shared archive |
| 3 | The document's own `GraphDriveId` / `GraphItemId` | every read/download path once a document exists | ✅ no change — already per-document |

Strategy 3 needs nothing. Strategies 1 and 2 both need the special case, and **2 is the one easiest to
forget** because it has no client involved and no wizard to put a resolver in.

**Implementation rule**: one shared, record-aware resolver — *if the context record is a secure project,
use its `sprk_containerid`; otherwise the existing BU cascade* — and route **every** call site through
it. Do not add the `issecure` test at seven client sites; that is seven places to drift. This respects
INV-7 (the resolver stays client-side for strategy 1) while giving strategy 2 a server-side equivalent.

**Also required**: the wizard's BU cascade (`EntityCreationService.applyUserBuDefaults`) must **not**
apply `sprk_containerid` to a secure project. Today it does, which both defeats isolation and collides
with provisioning's idempotency marker (see the workflow review, §4).

**Sequencing note**: until this lands, a per-project container stamped by provisioning is written but
never read. Provisioning is still worth correcting first — it removes a live failure and is a
prerequisite — but isolation of documents is not achieved until strategies 1 and 2 are special-cased.

Result: *on a secure project, all access is explicit on both surfaces.* And because Type 1's SPA access is Dataverse's own answer, **the share that grants MDA access grants SPA access automatically** — the two surfaces cannot diverge.

The veto must apply **regardless of principal kind**: a Type 1 user whose contact holds a standing or org grant would otherwise derive access through the contact term, which Dataverse knows nothing about. The Secure BU covers the Dataverse half; the veto covers the grant half. Both are required.

**Reversibility**: under this mechanism the designation is reversible (reassign owner + BU). The wizard's "cannot be removed" warning was a consequence of BU-per-project and should be removed.

### 5.2 🔴 Blocking prerequisite — role depth (empirically confirmed)

Live query against dev, 2026-08-20:

| Role | `prvReadsprk_Project` | `prvReadsprk_Matter` |
|---|---|---|
| **Spaarke Basic User** | **4 — Deep (Parent:Child BU)** | **4 — Deep** |
| Service Reader / Writer | 8 — Global | 8 — Global |
| System Administrator / Customizer | 8 — Global | 8 — Global |
| Support User | 1 — Basic | 1 — Basic |

`Spaarke` is the **root** BU (`parentbusinessunitid` = null) and users sit in it. **Deep from the root covers every descendant BU** — and `ProvisionProjectEndpoint` creates each secure BU as a child of root. Therefore **every secure project is currently visible to every `Spaarke Basic User`**, silently.

**Root cause**: Dataverse permits exactly **one** root business unit, so a secure BU is always a descendant of root. While non-admin users sit in root at Deep, nothing can be placed outside their reach.

**Fix (operator decision 2026-08-21) — restructure the BU tree so the hierarchy encodes the boundary:**

```
Spaarke                                  ← Level 0 (root). Sysadmins only — they hold Global regardless
├── Spaarke Operations                   ← Level 1. Users are assigned here or below, at DEEP depth
│   ├── Spaarke Operations/Business Unit 1   ← Level 2
│   ├── Spaarke Operations/Business Unit 2
│   └── …
└── Secure Projects                      ← Level 1, SIBLING of Operations — NOT a descendant.
    └── (future per-project secure BUs)      No standing security role for ANY principal
```

Deep from `Spaarke Operations` reaches Operations **and its Level-2 children** — so Business Unit 1/2 are visible and remain usable for record ownership (canonical case step 8 holds). `Secure Projects` is a sibling of Operations, so **no BU depth reaches it from anywhere in the Operations subtree**.

Users sit in **Spaarke Operations or below**, retaining **Deep** depth — so Deep reaches Operations and its descendants (canonical case step 8 holds, and Level-2 BUs remain usable for record ownership). **Secure Projects is a sibling of Operations, not a descendant**, so no BU depth reaches it. Per-record access comes from the explicit share, which is BU-independent. Future per-project secure BUs nest under Secure Projects and remain outside Operations' subtree.

Chosen over `Local` + matrix data access (Record-ownership-across-business-units): the hierarchy encodes the boundary structurally, with no platform toggle dependency and no per-user per-BU role administration.

**Two one-time migration obligations — both blocking:**
1. **No non-admin user may remain in the root BU.** Deep from root still reaches Secure Projects. Migrate all non-admin users to Operations or below, and assert it thereafter.
2. **Re-home existing root-owned records to Operations.** BU depth reaches only *downward*; a user in Operations cannot see root-owned records. All `sprk_project` / `sprk_matter` / other core records currently owned in root must move.

Service Reader/Writer holders are Microsoft platform accounts (`#`-prefixed) — a platform default, not a Spaarke hole, but note `RelevanceSearch` holds Global read, so secure records may surface in Dataverse relevance search.

**This must be verified as a standing assertion, not a one-time check** — a future role edit would silently undo every secure project.

**Check before implementing**: whether legitimate data lives in the `Spaarke Demo` / `Spaarke Dev 1` / `Spaarke Test 1` child BUs that Local-depth users would lose access to.

---

## 6. Manage Access (the PCF)

`AccessGrantModal` becomes the single Manage Access surface for both planes.

| Capability | Today | Target |
|---|---|---|
| Contact access | ✅ `/external-access/grant` | unchanged |
| Organization access | ✅ | + org standing grant + org level (C-3) |
| **System user access** | ❌ absent | **"+ User" picker → Dataverse share (POA)** |
| Service-user assignment / BU alignment | ❌ | secure-project owner + BU shown and settable |
| Level per principal | partial | View Only / Collaborate / Full Access on every row |
| Standing grant | display only | level-bearing, contact **and** organization |
| Deny list | ❌ | add/remove ethical-wall entries |
| Secure / Restricted state | banner only | suppressed rows rendered struck through with reason |
| **Provenance per row** | partial | share · grant · org grant · standing · derived-from-field |

Provenance is what makes this double as the **attestation surface** (§7) and answers "who can see this, and why" in one place. Behind it, two stores (POA for system users, `sprk_externalrecordaccess` for contacts); the user sees one uniform list.

**Prerequisite — delegation (blocking)**: the modal writes exclusively through BFF endpoints (`authenticatedFetch` → `/api/v1/external-access/*`), which run app-only and consult no Dataverse privilege. Today any authenticated caller can grant. **Rule: you may grant access to a record if you have Write on that record**, enforced by an OBO check in the endpoint. This must ship *before* the "+ User" button, or a read-only user gains a one-click path to Full Access on a confidential matter.

## 6.1 Create Project wizard

The Secure Project step must be reworked to §5. Independently, its current copy is wrong today: it promises *"A Power Pages workspace is activated"* — Power Pages is retired (SWA + CIAM). The permanence warning also goes (§5.1).

## 7. Attestation

Do **not** materialize derived access into rows — that reintroduces every staleness and reconciliation problem of a push model.

- **Append-only access event log** for grant/deny state changes — few, exact, legally defensible.
- **Evaluator replay** for derived access: it is a pure function of record lookups, org junctions, flags and deny entries, all already covered by Dataverse field audit. No new storage; the evaluator must be **versioned** so historical answers remain reproducible.

---

## 8. Phase plan

**Phase 0 — close the enforcement gaps.** Included in this project per operator decision (no separate fast remediation). Non-negotiable before Phase 3, because A-7 becomes a genuine vulnerability the moment To Dos become an access-scoped child module. Verified items A-1…A-9 plus confirmed items from A-10…A-22 ([register](notes/design-register.md) §A). Includes the minimum expiry fix (add to the read filter, or hide the UI input) and the role-depth remediation (§5.2).

**Phase 1 — one evaluator.** `(recordId → rights)`; consolidate on `AccessibleRecordSetService`; repair or retire the `AuthorizationService` path.

**Phase 2 — one definition of member.** Explicit column allow-list for contact-typed *and* org-typed lookups. Delegation rule (§6).

**Phase 3 — child inheritance.** Core/child taxonomy; `RegardingResolver` ancestor denormalization; child modules for event/communication/todo.

**Phase 4 — Secure Project + Manage Access PCF + wizard.** Depends on §5.2 remediation and the E-8 spike.

**Phase 5 — attestation.**

## 9. ADR tensions (CLAUDE.md §6.5)

| ADR | Tension | Path |
|---|---|---|
| **ADR-003** | "Two seams only", "new auth logic MUST be an `IAuthorizationRule`", "MUST NOT create new service layers for auth", "cache per-request only" — none describe reality; the external stack and `CachedAccessDataSource` already diverge | **B — amend/supersede** with a two-surface ADR |
| **ADR-034** | Resolver is canonical for membership; the allow-list becomes a first-class per-surface concept; record→members inverse read | **B — amend**. 1-hop cap needs no exception (§4.3) |
| **ADR-028 A2** | A2 mandates workforce callers resolve to systemuser→ADR-034 membership. §4.4 replaces the derivation with Dataverse's real answer | **B — narrow amendment.** The token stays workforce; only the derivation policy changes |
| **ADR-028 A4** | A4 forbids `.WithClientSecret(...)` on a BFF-identity client (use MI-FIC or a KV certificate); exception E-3 covers enumerated transitional sites and *"does not license expansion"*. Task 008's `CallerRecordAccessProbe` performs an OBO exchange, which A4 itself notes `DefaultAzureCredential` cannot do — and there is **no `WithClientAssertion` anywhere in the repository** (all 7 pre-existing OBO/confidential sites use a client secret). Complying would mean building the shared MI-FIC provider inside an authorization filter | **A — project-scoped exception. ACCEPTED by the owner 2026-08-23**, to be resolved as part of the broader MI migration. The new site reads the *same* `AzureAd:ClientSecret` as the other seven and uses the identical shape to its nearest precedents (`DataverseUserClient`, `DataverseAccessDataSource`), so all **8** migrate together. Net operational impact today: none — one more site on the E-3 migration list |

## 10. Open decisions

Carried from [register](notes/design-register.md) §C. Resolved since: standing grant applies to organizations, contacts **and** internal workforce (C-2/C-3 → wire org-level `sprk_standinggrant` + `sprk_accesspermissions` + a level); Secure applies to Type 1 too (C-10 → §5).

Still open: truncation semantics above the `in`-clause bound (C-1); organization hierarchy (C-6); break-glass (C-7); GDPR erasure of grant rows and the attestation log (C-8).

**Residual, not fully closed**: changing a contact's primary organization drops *org-derived* access, but individual explicit grants and person-derived access (`sprk_assignedattorney1 = Contact A`) are keyed to the person and survive. Recommendation: flag for review on org change; do not auto-revoke.

## 11. Prerequisites

Register §E. Blocking: role-depth remediation (§5.2), `prvActOnBehalfOfAnotherUser` on the BFF app user with a runbook entry, the impersonation **negative canary** (impersonated low-privilege read must return a strict subset AND strictly fewer rows — equality means impersonation is inert), BFF app user stays Org-scoped, and the E-8 secure-BU spike in dev.

## 12. Risks

| Risk | Mitigation |
|---|---|
| Impersonation silently inert → org-wide disclosure | Negative canary as a merge gate; `RetrieveMultipleImpersonatedAsync` already refuses `Guid.Empty` |
| Role-depth regression re-opens Secure Project | Standing assertion, not a one-time audit (§5.2) |
| Near-zero behavioural test baseline on the access path | Phase 0 builds the characterization suite before Phase 1 changes behaviour |
| Dev is the sole live environment | Every prerequisite recorded as a re-provisioning obligation |
| Truncation false-denies above the `in`-clause bound | C-1; explicit paging decision required |
