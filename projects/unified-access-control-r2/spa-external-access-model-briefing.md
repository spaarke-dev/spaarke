# SPA / External-Access Access & Membership Model — Briefing for UAC-r2

> **Status**: Reference briefing (2026-08-20). Read alongside [`unified-access-control-cascade.md`](unified-access-control-cascade.md).
> **Purpose**: Bring UAC-r2 up to speed on the access + membership services already built by the
> external-access SPA projects (`spaarke-SPA-external-access-platform-r1` / `-r2` / `teams-app-r1`),
> so the cascade design accounts for **both** access-control planes — not just the Dataverse-native one.
> **Source**: code read of `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/**`,
> `Services/Ai/Membership/**`, `Api/ExternalAccess/**` in the r3 worktree (2026-08-20). Code is ground truth;
> the published `sprk_externalrecordaccess` schema doc is **stale** (see §5).

---

## 0. TL;DR — the one thing to internalize

UAC-r2's cascade investigation frames the requirement as: **enumerate the members of a parent record,
then actively GRANT (POA share / owning-team sync) that access down to the child records.** That is a
**push / materialize** model, and it is written in Dataverse (POA rows, team membership).

The SPA projects solved the **same requirement** — "a member of a parent sees the parent's children" —
by the **opposite** mechanism: a **compute / derive** model. Nothing is granted on children. At read time
the BFF composes the caller's **accessible root-record set** (projects/matters/work-assignments) and a
child is visible **iff its parent-lookup value is in that set**. This is already live for **documents and
invoices**.

These two models are not interchangeable, and each only works on one plane:

| | **Push / materialize** (UAC-r2 cascade doc) | **Compute / derive** (SPA, already built) |
|---|---|---|
| Where access lives | POA rows / team membership in Dataverse | Nothing stored; computed per request in BFF |
| Works for `systemuser`? | ✅ yes | ✅ yes |
| Works for `contact` (external partner)? | ❌ **no — you cannot POA-share to a contact** | ✅ **yes — this is the only option for contacts** |
| Applies to native MDA grids? | ✅ yes | ❌ no (only the BFF read seam) |
| Applies to the external SPA / Teams app? | ❌ no (SPA never reads Dataverse directly) | ✅ yes |
| Needs a Create-trigger + reconciliation? | ✅ yes (children created later; members removed later) | ❌ **no — derived fresh each read, nothing to go stale** |
| POA-bloat risk | ✅ yes (the central cost) | ❌ none |

**Consequence for UAC-r2**: "members of a parent can access its children" is really **two features on two
planes that share one spine** (the ADR-034 membership service):

1. **External / SPA surface** (contacts + contact-backed internal users) → **extend the compute model**:
   register `sprk_event` / `sprk_communication` / `sprk_todo` as child modules with parent-lookup scope
   dimensions, exactly as documents/invoices already are. Cheap, no triggers, no POA, no reconciliation.
2. **Native MDA surface** (licensed `systemuser`s in Dataverse grids/forms) → the **push model** your
   cascade doc already analyzes (POA / owning-team, triggers, reconciliation).

Do not let the cascade doc's POA/team framing become the whole design — it silently drops every external
partner, who is the population the SPA exists to serve.

---

## 1. The two planes, precisely

A caller is classified into an authentication **plane** by
[`CallerPrincipalResolver.DeterminePlane`](../../../spaarke-wt-SPA-external-access-platform-r3/src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/CallerPrincipalResolver.cs) (`CallerPrincipalResolver.cs:234-254`):

- **CIAM plane** iff the validated token `iss` contains `ciamlogin.com` **OR** `tid == Ciam:TenantId`.
- **Workforce plane** otherwise (fail-forward default).

Each plane has a strategy (`ICallerPrincipalStrategy`, `:166-173`; add a plane = add one registration):

- **`CiamContactPrincipalStrategy`** (`:263-343`) — external partners. **Grant-only, flat.** Resolves the
  Contact by `oid` (→ `contact.sprk_externalobjectid`) with verified-email fallback, loads grants, and
  **explicitly takes no membership / assignment / rollup-derived access** (`:316-318`).
- **`WorkforcePrincipalStrategy`** (`:352-451`) — internal callers. Wraps `WorkforcePrincipalResolver`
  (token → principal) + `AccessibleRecordSetService` (principal → accessible sets).

Both strategies end by populating the **same** `CallerPrincipal` object with three precomputed accessible
root-id sets (`ProjectAccess`, `AccessibleMatterIds`, `AccessibleWorkAssignmentIds`,
`CallerPrincipalResolver.cs:90-113`). **This is the unifying seam**: downstream child-rollup reads those
id sets and does not care how they were composed. CIAM fills them from grants only; workforce fills them
from membership ∪ grants.

---

## 2. Principal model — how a caller becomes a principal

`WorkforcePrincipalKind` has exactly two values (`ExternalCallerContext.cs:141-150`):

| Kind | Meaning | Carries |
|---|---|---|
| **`SystemUser`** | caller has a Dataverse `systemuser` row (AAD oid → systemuser) | `SystemUserId` + **derived** `ContactId?` (via `sprk_primarycontact`, may be null) + `Oid` + `TenantId` + `Email` |
| **`ContactOnly`** | caller has **no** systemuser; resolves to a `contact` (by oid or verified email) | `ContactId` (non-null anchor) + `Oid` + `TenantId` |

Resolution order in [`WorkforcePrincipalResolver.ResolveAsync`](../../../spaarke-wt-SPA-external-access-platform-r3/src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/WorkforcePrincipalResolver.cs) (`:88-171`) is **systemuser-first**:

1. `oid → systemuser` (`ResolveSystemUserIdAsync`, `:109`). If found → `SystemUser` principal.
2. else `oid / verified-email → contact` (`TryResolveContactByWorkforceIdentityAsync`, `:150`) → `ContactOnly`.
3. else **explicit deny** (`:169-170`) — never a silent unscoped pass-through.

**Terminology note for UAC-r2 discussions**: "workforce" vs "partner" is an *authentication* distinction.
The *access-control* distinction is `SystemUser` vs `ContactOnly`. A contact-backed internal user (branch 2)
is, at the access layer, identical to an external partner. Neither consumes a Power Apps license (see §5).

---

## 3. The unified authorization primitive

One gate, `IAccessibleRecordSetService` (`AccessibleRecordSetService.cs:39-55`):

```csharp
Task<AccessibleRecordSet> ComposeAsync(WorkforcePrincipal principal, string entityType, CancellationToken ct);
Task<bool> IsRecordAccessibleAsync(WorkforcePrincipal principal, string entityType, Guid recordId, CancellationToken ct);
```

- **Fail-closed**: empty recordId → deny (`:168-171`); membership is `Contains(recordId)` on the composed set.
- **Composition depends on principal Kind** (`:153-161`), not plane:
  - `SystemUser` → **ADR-034 membership ∪ own-contact grants** (`ComposeForSystemUserAsync`, `:184-241`).
  - `ContactOnly` → **explicit grants ∪ standing-grant membership** (gated on the flag; the negative case
    is load-bearing — no flag ⇒ grants only, `ComposeForContactAsync`, `:244-301`).
- **Called per-root-type only** — `WorkforcePrincipalStrategy` makes exactly three `ComposeAsync` calls,
  for `sprk_project` / `sprk_matter` / `sprk_workassignment` (`CallerPrincipalResolver.cs:422-427`).
  **Children are never composed here.**

> ⚠️ **Key extensibility fact**: the **grant** term is allow-listed to the three root types
> (`GrantSupportedRootEntities`, `AccessibleRecordSetService.cs:99-104`), but the **membership** term is
> **not** — it passes `entityType` straight to the entity-type-generic resolver (`:192`, `:276`). So the
> gate can already be *called* for a child entity type; it just returns no grant-derived ids for one today.

The design **already declares UAC-r2's requirement as intended-but-unbuilt**
([`ExternalCallerContext.cs:92-95`](../../../spaarke-wt-SPA-external-access-platform-r3/src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ExternalCallerContext.cs#L92-L95)):
> *"Direct document/invoice-level grants are intentionally OUT OF SCOPE (design §6 — access to a child
> derives from an accessible ROOT), so the grant table's `sprk_invoice` lookup is not read here."*

---

## 4. The membership spine (ADR-034) — the shared "who are the members" service

`Services/Ai/Membership/MembershipResolverService.cs`. **This is the one service both planes and both
child-derivation models depend on**, and it is exactly the "member group" resolver UAC-r2's cascade doc
already found.

- **Membership = discovery over lookup columns, NOT a junction table.** For any `entityType`, discovery
  keeps every Lookup whose target is one of 6 identity tables — `systemuser, contact, team, businessunit,
  account, sprk_organization` (`MembershipFieldDiscoveryService.cs:195-352`; `MembershipOptions.cs:241-249`)
  — plus synthesized `ownerid`→{systemuser,team} and Customer→{account,contact} (`:494-550`). ADR-034
  **bans** joining through a `*teammember` entity.
- **Two entry points, one engine**:
  - `ResolveAsync(systemUserId, entityType, options, ct)` (`:135-346`) — full identity normalization
    (contactId, teamIds, BU, account, orgs), all roles, supports transitive expansion.
  - `ResolveByContactAsync(contactId, entityType, options, ct)` (`:349-491`) — bare contact; runs a
    **security allow-list first** (`FilterToAccessConferringContactRoles`, `:511-558`): keeps only
    `Contact`-typed lookups whose field starts with `sprk_assigned` and is not excluded, so adverse /
    opposing-counsel / `sprk_regarding*` contact lookups **never** confer access (NFR-05).
    **No transitive expansion** (`RelatedByRole` forced null, `:479`).
- **The child-inheritance primitive already exists for systemusers** — `ResolveTransitiveAsync`
  (`:926-1020`) + `DiscoverLookupsTargetingAsync` (`MembershipFieldDiscoveryService.cs:732-793`) do exactly
  *"find the child's lookup fields that target the parent, then one `in`-FetchXML over the parent ids →
  child ids"* (`BuildTransitiveFetchXml`, `:1030-1077`); results land in `MembershipResponse.RelatedByRole`.
  **Capped at 1 hop** (ADR-034 binding MUST NOT beyond 1 — fine for direct parent→child; deeper needs a
  §6.5 ADR exception).

**Three gaps UAC-r2 must close on this spine:**
1. `ResolveByContactAsync` has **no** transitive path → **external contacts get no child inheritance today.**
2. `AccessibleRecordSetService.ComposeAsync` does not call the transitive path (or any child union).
3. Contact membership is gated behind the standing-grant flag → contact child-inheritance must respect the
   same negative-case gate.

---

## 5. The external grant model (the CIAM access source)

Entity `sprk_externalrecordaccess`. **Live field names (code is truth — the v1.0 schema doc is stale):**

| Field | Role |
|---|---|
| `sprk_Contact` → contact | grantee (read form `_sprk_contact_value`); **empty ⇒ this is an org grant** |
| `sprk_Project` / `sprk_Matter` / `sprk_WorkAssignment` | the **one** typed root FK per row |
| `sprk_Organization` → `sprk_organization` | firm scoping metadata (per-contact grant) **or** the match key for an org grant (NOT OOB `account`) |
| `sprk_GrantedBy` → systemuser | audit |
| `sprk_accesslevel` | `ViewOnly=100000000 / Collaborate=…001 / FullAccess=…002` |
| `sprk_granteddate`, `sprk_expiresdate` | (note: **`sprk_expiresdate`**, not `sprk_expirydate`) |
| `statecode` | active flag — everything filters `statecode eq 0`; **deactivation = revocation** |
| `sprk_invoice` → invoice | **exists on the table but intentionally never read** (child-derives-from-root) |

Read: `ExternalParticipationService.QueryGrantSetAsync` (`:392-490`) → `ExternalGrantSet { Projects(+level),
Matters, WorkAssignments }`. Two extra inheritance vectors already live here:

- **Organization grant** (`:499-537`): a contact-empty + org-bound row; access is materialized **per active
  member** by resolving the contact's active orgs from the `sprk_contactorganization` junction
  (`statecode eq 0`) — leaving a firm drops the access. *(This is itself a "membership → derived access"
  cascade, org-scoped, worth studying as prior art.)*
- **Standing grant** (`ContactStandingGrantReader.cs:69-128`): the FLS-secured boolean
  `contact.sprk_standinggrant`; when true, unions the contact's ADR-034 (allow-listed) membership into the
  accessible set as a **runtime** term (no grant rows). **Fail-closed**, and it reads dark unless the BFF
  Dataverse Application User is in the "Standing Grant Administrators" Field Security Profile (per-env
  operator step — applied only to `spaarkedev1` so far).

**Write path / notification** (relevant to any UAC-r2 grant writes): `/grant` writes the row + invalidates
the 60s Redis cache and **sends no notification**. The onboarding email fires **only on first CIAM
provisioning** via an idempotency gate (`InviteExternalUserEndpoint.ProvisionAsync:127-135`); a subsequent
grant to an already-provisioned contact is silent (this is R3 gap 6A, separately scoped).

**Licensing note**: a `contact` is business data, not a Dataverse security principal — **zero Power Apps /
Dataverse license** is consumed; external users authenticate to Entra External ID (CIAM, MAU-billed) and
reach only the BFF, which acts app-only. This is *why* you cannot POA-share to them (no principal to share
to) and why the compute model is mandatory for the external plane.

---

## 6. Tier-1 vs Tier-2 (do not conflate)

- **Tier-1 — `ModuleEntitlementResolver`** (`/api/v1/external/me/entitlements`): "which module **tabs**
  show." CIAM → blanket `{ "assigned-work" }`; workforce → Entra App-Role claims ∩ `sprk_approlemodulemap`.
  **UX data only, explicitly not the security boundary** (NFR-06), cached 60s.
- **Tier-2 — `ExternalModuleRegistry` + `AccessibleRecordSetService`**: "which **records** within a module."
  The real, fail-closed gate. Being entitled to a tab reveals no record unless Tier-2 says so.

UAC-r2 child inheritance is a **Tier-2** concern.

---

## 7. ⭐ The existing child-inheritance precedent (read this twice)

The R2 module-host read seam (`POST /api/v1/external/api/dataverse/fetch`,
`ExternalModuleDataEndpoints.cs`) is where children already inherit access. Mechanics:

1. Caller POSTs `{ entityName, fetchXml }`. **Single-entity guard** (`:147-172`): the FetchXML may
   reference **only** the module's own entity — **`<link-entity>` joins are rejected**. ⇒ *the parent-lookup
   must be an attribute on the child row itself; you cannot join child→parent in the query.*
2. Each registered module (`ExternalModuleDescriptor`) declares one or more **`ScopeDimension`s**
   (`ExternalModuleRegistry.cs:49-58`) = `(childLookupAttribute, accessibleRootIdSet)`.
3. `Tier2ScopeFilterInjector.Inject` pushes a `<filter type="or">` with one `<condition
   attribute="{lookup}" operator="in">` per non-empty dimension into the FetchXML; then `ScopeRows`
   re-filters in memory (defense-in-depth). Empty on all dimensions ⇒ 0 rows without querying (fail-closed).

**The two child modules that already inherit** (`ExternalAccessModule.cs`):

- **`documents` (`sprk_document`)** — three OR'd dimensions: `sprk_project ∈ P` OR `sprk_matter ∈ M` OR
  `sprk_workassignment ∈ W` (`:166-176`).
- **`invoices` (`sprk_invoice`)** — two: `sprk_matter ∈ M` OR `sprk_project ∈ P` (`:181-190`).

A document/invoice holds **no grant of its own** — it is visible because its typed parent lookup points at
a root already in the caller's accessible root set. **This is precisely UAC-r2's requirement, already
shipping for two child entities.** Extending it to `sprk_event` / `sprk_communication` / `sprk_todo` is
primarily **new module descriptors**, not a new mechanism.

(There is also an older R1 path, `ExternalDataService.cs`: bespoke per-project queries — documents by
`_sprk_project_value`, todos by `_sprk_regardingproject_value` — **project-only, single-parent, not a
registered module**. It's legacy; the module seam supersedes it. Events and communications have **no**
external read path today.)

---

## 8. Two child-derivation implementations already exist — pick one, don't add a third

The tree already contains **two** independent ways to derive child ids from parent access. UAC-r2 should
unify on one rather than invent a third:

| | **A — ScopeDimension OR-filter** (`ExternalAccessModule` + `Tier2ScopeFilterInjector`) | **B — `ResolveTransitiveAsync`** (`MembershipResolverService`) |
|---|---|---|
| How | child's parent-lookup ∈ precomputed accessible-root-id set, injected into the caller's FetchXML | discovers child lookups targeting the parent, runs its own `in`-FetchXML over parent ids |
| In use today | ✅ documents, invoices (external read seam) | ⚠️ built (task 054) but **not** wired into the external gate; systemuser-only |
| Works for contacts | ✅ (reads precomputed `CallerPrincipal` sets, plane-agnostic) | ❌ (`ResolveByContactAsync` disables it) |
| Hop limit | 1 (parent-lookup on child) | 1 (ADR-034 MUST NOT beyond) |

**Recommendation to evaluate in `design.md`**: for the external plane, extend **model A** (add child module
descriptors) — it already serves both principal kinds and needs no membership round-trip. Reserve **model
B** for cases needing the discovery/role metadata. Whichever is chosen, the "who is a member" answer comes
from the **same ADR-034 spine** (§4).

---

## 9. The lookup-convention split UAC-r2 must reconcile

Children point at parents via **two different naming conventions**, and the R2 scoping deliberately reads
the **bare typed lookups**, not the ADR-024 resolver fields (because `sprk_document` lacks the denormalized
`sprk_regarding*` fields):

| Child | Parent-link convention |
|---|---|
| `sprk_document`, `sprk_invoice` | **bare** `sprk_project` / `sprk_matter` / `sprk_workassignment` |
| `sprk_todo` | **11 `sprk_regarding{entity}` lookups** (`sprk_regardingmatter`, `…project`, `…workassignment`, `…invoice`, `…communication`, `…event`, …) |
| `sprk_event` | 8 typed `sprk_regarding*` (Matter, Project, Invoice, Analysis, Account, Contact, Work Assignment, Budget) |
| `sprk_communication` | typed `sprk_regarding*` (Matter, Project, Invoice, Work Assignment, Budget, Analysis, Org, Person) |
| `sprk_servicerequest` | `sprk_requestedby` → contact (submitter self-ownership, **not** a regarding parent) |

So adding events/communications/to-dos as child modules means their `ScopeDimension`s point at
`sprk_regarding{root}` attributes (e.g. `sprk_regardingmatter ∈ M`), whereas documents/invoices use bare
`sprk_matter ∈ M`. **UAC-r2 must handle both conventions** in whatever it generalizes. (ADR-024 is the
authority on the polymorphic child→parent model; `docs/architecture/spaarke-todo-architecture.md` lists
`sprk_todo`'s 11 regarding lookups.)

---

## 10. Reframing UAC-r2's §7 open decisions with the external-plane dimension

Your cascade doc's §7 decisions were written from the native-MDA (POA) angle. Each needs the external-plane
answer added:

1. **Which members confer child access?** — the SPA already answers this with
   `FilterToAccessConferringContactRoles` (`sprk_assigned*` allow-list, NFR-05). **Reuse that convention**
   on both planes instead of inventing a new policy; it already excludes adverse/regarding parties.
2. **Mechanism (POA vs team vs access-team)?** — this decision **only applies to the native plane**. On the
   external plane the mechanism is fixed: **compute/derive** (there is nothing to POA-share to). State this
   explicitly so the design doesn't accidentally scope contacts out.
3. **Trigger (flow vs webhook→BFF)?** — **the external plane needs no trigger at all** (derived at read
   time). Triggers/reconciliation are a native-plane-only cost. This materially shrinks the external-plane
   work.
4. **Reconciliation / removal scope?** — again **native-plane only**; the compute model has nothing stored
   to reconcile, and a removed member simply stops resolving on the next read.
5. **Spend the one parental relationship?** — native-plane only; irrelevant to the external surface.
6. **BFF placement (§10)?** — the external-plane extension is **small and additive** (child module
   descriptors + possibly a contact-capable transitive union in `AccessibleRecordSetService`). Placement
   justification is light: it extends an existing BFF subsystem, adds no new top-level service, no new
   package. Publish-size delta ≈ negligible.

**Net**: splitting the feature by plane makes the external half **cheap and low-risk** (reuse the shipping
document/invoice pattern) and isolates the expensive, POA-bloat-prone half to the native MDA surface where
it's actually required.

---

## 11. Seams / insertion points (where UAC-r2 plugs in)

| To do | Where |
|---|---|
| Add `event`/`communication`/`todo` child modules (external plane) | `Infrastructure/DI/ExternalAccessModule.cs:146-246` (mirror the `documents`/`invoices` descriptors) |
| The scope-dimension model to reuse | `Infrastructure/ExternalAccess/ExternalModuleRegistry.cs:49-58` (`ScopeDimension`, `ScopeRows`, `TryGetAttributeId`) |
| Server-side OR-filter injection | `Api/ExternalAccess/Tier2ScopeFilterInjector.cs` |
| Contact-capable child union (if going via model B) | `Infrastructure/ExternalAccess/AccessibleRecordSetService.cs:244-301` (add a child term to `ComposeForContactAsync`, respecting the standing-grant gate) |
| Enable transitive for contacts | `Services/Ai/Membership/MembershipResolverService.cs:349-491` (`ResolveByContactAsync` currently nulls `RelatedByRole`) |
| "Who is a member" (shared spine) | `Services/Ai/Membership/MembershipResolverService.cs` + `FilterToAccessConferringContactRoles:511-558` |
| Native-plane grant seam (push model) | `IDataverseAccessGrantService` / `DataverseWebApiService.GrantAccessAsync` (per your cascade doc §3) |
| 1-hop cap (may need §6.5 exception) | ADR-034 `.claude/adr/ADR-034-user-record-membership.md` |

---

## 12. File & ADR index (r3 worktree, `src/server/api/Sprk.Bff.Api/`)

**Principal + gate**
- `Infrastructure/ExternalAccess/CallerPrincipalResolver.cs` — plane selection (`:234-254`), strategies (`:263-451`), `CallerPrincipal` root sets (`:90-134`), root allow-list (`:362-366`)
- `Infrastructure/ExternalAccess/WorkforcePrincipalResolver.cs` — token→principal (`:88-171`)
- `Infrastructure/ExternalAccess/ExternalCallerContext.cs` — `WorkforcePrincipal`/`WorkforcePrincipalKind` (`:141-192`), CIAM `ExternalCallerContext` (`:9-72`), `ExternalGrantSet` (`:96-114`), child-derives-from-root note (`:92-95`)
- `Infrastructure/ExternalAccess/AccessibleRecordSetService.cs` — the gate (`:39-176`), root allow-list (`:99-108`), systemuser compose (`:184-241`), contact compose (`:244-301`)

**Membership spine (ADR-034)**
- `Services/Ai/Membership/MembershipResolverService.cs` — `ResolveAsync` (`:135-346`), `ResolveByContactAsync` (`:349-491`), access-conferring allow-list (`:511-558`), `BuildFetchXml` (`:625-755`), `ResolveTransitiveAsync` (`:926-1020`), `BuildTransitiveFetchXml` (`:1030-1077`)
- `Services/Ai/Membership/MembershipFieldDiscoveryService.cs` — discovery (`:195-352`), owner/customer synthesis (`:494-550`), parent-targeting lookup discovery (`:732-793`)
- `.claude/adr/ADR-034-user-record-membership.md` — MUST/MUST NOT, 1-hop cap
- `.claude/adr/ADR-024-polymorphic-resolver-pattern.md` — child→parent regarding model
- `docs/architecture/spaarke-todo-architecture.md` — `sprk_todo` 11 regarding lookups

**Grant model + schema**
- `Infrastructure/ExternalAccess/ExternalParticipationService.cs` — grant read (`:392-490`), org grants (`:499-583`), contact resolve (`:211-258`)
- `Infrastructure/ExternalAccess/ContactStandingGrantReader.cs` — standing-grant flag (`:69-128`)
- `Infrastructure/ExternalAccess/ExternalGrantRoot.cs` — the 3 typed root FKs (`:8-69`)
- `Api/ExternalAccess/GrantExternalAccessEndpoint.cs`, `InviteAndGrantExternalUserEndpoint.cs`, `InviteExternalUserEndpoint.cs`
- ⚠️ stale schema doc: `src/solutions/SpaarkeCore/entities/sprk_externalrecordaccess/entity-schema.md` (v1.0); live field names in `projects/spaarke-SPA-external-access-platform-r2/notes/task-070-deviations.md`
- standing-grant schema: `projects/teams-app-r1/notes/050-standing-grant-field-schema.md`

**Read path / child precedent**
- `Api/ExternalAccess/ExternalModuleDataEndpoints.cs` — fetch seam, single-entity guard (`:113-240`)
- `Api/ExternalAccess/Tier2ScopeFilterInjector.cs` — OR `in`-filter injection
- `Infrastructure/ExternalAccess/ExternalModuleRegistry.cs` — `ScopeDimension` model (`:49-187`)
- `Infrastructure/DI/ExternalAccessModule.cs` — module descriptors incl. documents/invoices rollup (`:146-246`)
- `Infrastructure/ExternalAccess/ModuleEntitlementResolver.cs` — Tier-1
- `Infrastructure/ExternalAccess/ExternalDataService.cs` — R1 legacy per-project path
- design: `projects/spaarke-SPA-external-access-platform-r2/notes/external-access-polymorphic-scoping-design.md` (§2/§5/§6), `notes/task-028-deviations.md`

---

## 13. Open questions this briefing raises for UAC-r2 design

1. **Scope split confirmation** — is UAC-r2 chartered for **both** planes, or only the external/SPA surface?
   (The cascade doc reads native-plane-only; this briefing argues the external plane is the cheaper, higher-
   value half and is currently unbuilt for events/communications/to-dos.)
2. **Model A vs B** (§8) for the external child union — module descriptors vs contact-enabled transitive.
3. **Contact child inheritance + standing grant** — should a partner see a parent's children only when they
   hold the parent grant (natural under model A), or also under standing-grant runtime membership? (The
   negative-case gate in `ComposeForContactAsync` must be honored either way.)
4. **Lookup-convention generalization** (§9) — one config that handles both bare `sprk_{root}` and
   `sprk_regarding{root}` per child entity.
5. **Org-grant interaction** — org-scoped grants already cascade access to a firm's active members; confirm
   child inheritance composes correctly on top of org-derived root access (it should, since children read
   `CallerPrincipal`'s root sets regardless of source).
