# Investigation 02 — The ADR-034 Membership Spine: Fitness as the Shared "Who Are the Members" Service for Access Decisions

> **Status**: Investigation findings (2026-08-20), UAC-r2.
> **Method**: Full code read of `src/server/api/Sprk.Bff.Api/Services/Ai/Membership/**` in THIS worktree
> (`spaarke-wt-unified-access-control-r2`), plus the ExternalAccess consumers, ADR-034 (both versions), and the
> membership test suite. All citations are `path:line` in this worktree. Code is ground truth.

---

## 1. Summary

The ADR-034 membership spine is a **well-built, well-tested AI-scoping service that is already being used as an
authorization input** by the SPA plane (`AccessibleRecordSetService`), and it is *conditionally* fit for UAC-r2 —
provided four defects/asymmetries are addressed before more weight is put on it:

1. **App-only computation** — every query runs as the BFF's Dataverse Application User
   (`src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs:54-118`), so results are NOT trimmed by the
   caller's Dataverse security. Membership = "a lookup on the record points at one of your identities", full stop.
2. **Truncation false-denies** — the SPA gate composes access sets with `options: null` (Limit=500) and never consumes
   the continuation token (`Infrastructure/ExternalAccess/AccessibleRecordSetService.cs:192-194, 276-278`), so a
   principal with >500 memberships gets an arbitrarily truncated accessible set. Worse, the resolver's paging itself
   is unsound (no `<order>`, off-by-one sentinel loss — §7.3), so "just paginate" is not currently a fix.
3. **Asymmetric role policy** — only the *contact* entry point applies the access-conferring allowlist (NFR-05).
   The *systemuser* entry point confers membership on EVERY discovered descriptor, including adverse contact lookups
   reached via the user's linked contact, `sprk_regarding*` fields, account-typed and BU-typed lookups (§4, §5).
4. **Staleness window** — 5-min membership cache + 10-min identity cache; the pub/sub invalidator ships kill-switched
   default-off (`Infrastructure/DI/MembershipModule.cs:196-219`).

All three gaps asserted by the briefing are **confirmed** (§8). The transitive parent→child primitive exists, is live
on the HTTP membership endpoint, is 1-hop capped, does not work for contacts, and has one **broken live caller**
(the playbook node passes a `"*"` sentinel the resolver no longer ignores — §7.5).

---

## 2. Mechanics — what "membership" means, exactly

### 2.1 Discovery (`MembershipFieldDiscoveryService.cs`)

For any entity, discovery fetches metadata (`RetrieveEntityRequest`, `EntityFilters.Attributes` —
`MembershipFieldDiscoveryService.cs:415-452`) and keeps each Lookup attribute whose `Targets[]` intersects the
configured identity tables (`:286-300`). The **6 canonical identity tables** are seeded by
`MembershipOptionsDefaults` when config leaves the list empty (`MembershipOptions.cs:241-249`, post-configure at
`:265-277`; the R7 W12 "0 memberships everywhere" root-cause is documented at `:213-237`):

| Table | IdentityType |
|---|---|
| `systemuser` | SystemUser |
| `contact` | Contact |
| `team` | Team |
| `businessunit` | BusinessUnit |
| `account` | Account |
| `sprk_organization` | Organization |

**Owner/Customer synthesis** (`:88-97`, `:494-550`): the SDK drops polymorphic Owner/Customer attributes from the
`OfType<LookupAttributeMetadata>()` pass, so a fallback pass synthesizes rows with well-known targets —
`ownerid → {systemuser, team}`, customer columns `→ {account, contact}`. The rationale block (`:454-493`) records the
production defect this fixed ("user owns 44 matters, rows=0").

**First-matching-target rule** (`:286-300`): a polymorphic lookup binds to exactly ONE identity type — the first of
its `Targets[]` found in the identity-table map. For `ownerid` (targets `[systemuser, team]`) that is **SystemUser**,
so `ownerid` conditions bind the user's own id only; team-owned rows are matched via the separate `owningteam` column,
not via `ownerid`.

**Role derivation** (`:362-404`): strip `sprk_` prefix → strip trailing digits → camelCase
(`sprk_assignedattorney1` → `assignedAttorney`); `FieldRoleOverrides` win verbatim (`:319-337`).

### 2.2 The allow-list / deny-list boundary

- **Allow-list at the TABLE level**: only lookups targeting the 6 identity tables become descriptors; everything
  else is emitted as `IgnoredField` reason `target-table-not-in-identity-list` (`:302-313`).
- **Deny-list at the FIELD level**: `GlobalFieldExclusions` (defaults: `createdby`, `modifiedby`,
  `createdonbehalfby`, `modifiedonbehalfby` — `MembershipOptions.cs:256-262`) and per-entity
  `EntityOverrides.ExcludedFields`; per-entity exclusion is unconditional and beats force-include (`:267-270`);
  `IncludedFields` can resurrect a globally-excluded field (`:273-279`).
- **Second allow-list, contact path only**: the access-conferring convention (§4).

**Deliberately excluded** from membership: the 4 audit lookups (touch-history ≠ association), lookups to
non-identity tables, and unknown identity types (silently skipped in FetchXml construction —
`MembershipResolverService.cs:744-747`).

### 2.3 Identity normalization (`IdentityNormalizationService.cs`)

`systemuserid` → `PersonIdentity {SystemUserId, ContactId?, PrimaryEmail?, TeamIds[], BusinessUnitId?, AccountId?,
OrganizationIds[]}` (`Models/PersonIdentity.cs:70-92`). Paths are independent and fail-soft (`:127-190`):

- systemuser row → BU, email, AAD oid, **`sprk_primarycontact`** (`:199-224`);
- **Contact**: primary = the user's own `sprk_primarycontact` lookup; fallback = `contact.azureactivedirectoryobjectid`
  cross-ref (`:139-150`). *(Note: ADR-034's identity table documents only the AAD-oid mechanism — stale, §10.)*
- Teams via the **real** `teammembership` intersect entity (`:375-426`);
- Account via contact's `parentcustomerid` when it points at an account (`:429-466`);
- Organizations via `IIdentityOrganizationResolver` — config-driven lookup field on `sprk_organization`
  (`OrganizationMembershipResolver.cs:100-139`; default empty = fail-soft empty; cap 1000
  `MembershipOptions.cs:159`).

Cached 10 min (`:55`). Failure of one path yields null/empty for that path only — **under-inclusion, never a throw**.

### 2.4 Resolution (`MembershipResolverService.cs`)

One OR-joined FetchXml per request (`BuildFetchXml`, `:625-755`): projects each descriptor field, then for each
descriptor emits `eq` conditions per matching identity value — SystemUser 1, Contact 1 (if ContactId), Team **N (one
per team)**, BU 1, Account 1, Organization **N** (`:672-749`). `Guid.Empty` values are guarded out (`:757-762`).
`top = limit+1` has-more sentinel (`:647`); **no `distinct`** (r5 completeness fix, `:638-646`, CacheVersion 3 `:77-81`);
**no `<order>` element** (see §7.3). Materialization dedupes, sorts by GUID ascending, truncates to limit, and builds
`byRole` from whichever descriptor fields are populated on each row (`:796-852`). Response cached 5 min per
`{tenant, user, entityType, optionsHash}` (`:84`, `:1239-1270`); tenant comes from the `tid` claim, falling back to
`"anonymous"` outside an HTTP context (`:129-132`).

### 2.5 Phase 2 materialization (write-side only)

`sprk_userentityassociation` is written by exactly one component — `MembershipJunctionUpdater.HandleAsync`
(natural-key idempotent Create/Update/Delete, `MembershipJunctionUpdater.cs:82, 192-244`) — fed by (a) the Service
Bus subscription host (kill-switched, default off, `MembershipModule.cs:255-268`) and (b) the nightly
`MembershipReconciliationJob` (`MembershipReconciliationJob.cs:215-449`), which is **enabled by default**
(`MembershipReconciliationOptions.cs:73`, cron `0 2 * * *` `:62`, default entity set matter/document/event/task/
opportunity `:48-55`) and dispatches to the handler directly, independent of the topic. **The resolver read path never
consults the junction** — Phase 1A per-request FetchXml is the live read mechanism.

---

## 3. The two entry points compared

| Dimension | `ResolveAsync(systemUserId, …)` (`:135-346`) | `ResolveByContactAsync(contactId, …)` (`:349-491`) |
|---|---|---|
| Identity normalization | Full 6-path `PersonIdentity` via `IIdentityNormalizationService` (`:223`) | **None** — bare `PersonIdentity(Guid.Empty, contactId)` (`:373-384`); normalization service deliberately bypassed |
| Identity types that can bind | All 6 (SystemUser/Contact/Team/BU/Account/Organization) | **Contact only** — structurally (all other identity values empty → 0 conditions) AND by descriptor filter |
| Role policy | ALL discovered descriptors (minus discovery deny-lists), then optional `Roles`/`IdentityTypes` narrowing (`:217-218`, `:561-592`). **No access-conferring allowlist.** | Access-conferring allowlist FIRST (`:403-410`), then options narrowing. Allowlist = Contact-typed ∧ `sprk_assigned*` prefix ∧ not excluded (`:511-558`) |
| Adverse-party exposure | **Yes** — a systemuser whose linked contact appears in `sprk_opposingcounsel`, `sprk_regardingcontact`, etc. gets membership via those fields | No — such fields are filtered before FetchXml (`:540-553`; proven by test asserting the field never reaches the query) |
| Transitive expansion (`includeRelated`) | Supported (`:308-320`), preflight chain-syntax validation (`:164-185`) | **Never** — `IncludeRelated` silently ignored (no preflight, no expansion); `RelatedByRole` hard-null (`:468-479`) |
| `member_skipped` diagnostics | Emits structured warning when a Contact descriptor can't bind (`:685-712`) | n/a (contact always binds) |
| Limit / paging | ClampLimit → default 500, max 5000 (`IMembershipResolverService.cs:149-155`; `:1178-1185`); continuation token | Identical (`:427-428`, `:462-466`) |
| Caching | 5-min TTL, id `{systemUserId}:{entity}:{optionsHash}` (`:1239-1240`) | 5-min TTL, disjoint namespace `contact:{contactId}:…` (`:1249-1250`) |
| Failure shape | Discovery throw propagates; identity fail-soft; fetch throw propagates; cache fail-open | Same, minus identity path |

**Confirming the allowlist predicate** (`FilterToAccessConferringContactRoles`, `:511-558`) — a descriptor survives iff
ALL of:

1. `d.IdentityType == "Contact"` (OrdinalIgnoreCase) — `:540-543`. Excludes systemuser/team/BU/account/org lookups
   AND polymorphic `sprk_regardingrecord*` fields whose matched target is non-contact.
2. `field.StartsWith(prefix)` where prefix = `Membership:AccessConferringRoles:ConventionPrefix`, default
   **`sprk_assigned`** (`MembershipOptions.cs:115-125`) — `:545-547`. Excludes adverse contact lookups
   (e.g., `sprk_opposingcounsel`).
3. Field not in `AccessConferringRoles.ExcludedFields` (config, default empty — `MembershipOptions.cs:128-132`) —
   `:549-553`.

Exclusion lists consulted on this path, in order: discovery-level `GlobalFieldExclusions` + per-entity
`EntityOverrides.ExcludedFields` (already applied inside `DiscoverAsync` before the allowlist sees anything), then
the contact-path `AccessConferringRoles.ExcludedFields`. **Confirmed**: the restriction is to `sprk_assigned*`
Contact lookups; adverse and `sprk_regarding*` parties are excluded. Note the prefix is *configurable*, so the
guarantee is "convention + exclusions", not a hardcoded field list (NFR-05 by design — a new `sprk_assigned*`
contact lookup auto-qualifies with no code change; locked by test
`ResolveByContactAsync_NewlyAddedAssignedConventionField_AutoQualifiesWithoutCodeChange`).

---

## 4. Fitness-for-authorization verdict

**Verdict: conditionally fit — it is already the authorization input on the BFF/SPA plane, and that is a deliberate,
fail-closed design; but it must not be treated as "Dataverse access" and it has four concrete defects for UAC-r2.**

### 4.1 It runs app-only, not in user context

`IDataverseService` is `DataverseServiceClientImpl` — Managed Identity (or ClientSecret fallback) app identity
(`DataverseServiceClientImpl.cs:54-118`). The ExternalAccess consumers state this explicitly ("reads
membership/grant/flag data APP-ONLY … no OBO", `AccessibleRecordSetService.cs:21-24`). **Therefore ResolveAsync can and
will return records the caller has NO Dataverse access to** — being pointed at by a lookup grants zero Dataverse
access (the cascade doc's own §2 load-bearing fact). Nothing in the membership pipeline performs a privilege or POA
check; nothing re-validates against `RetrievePrincipalAccess`.

### 4.2 The over-inclusion asymmetry, and what guards it

- **In an index/scoping context** (the service's original purpose — "My Matters", AI retrieval scope), over-inclusion
  costs relevance and mild information-radius widening.
- **In an access decision**, over-inclusion is disclosure. The codebase contains exactly ONE structural guard for
  this: `FilterToAccessConferringContactRoles` on the contact path (`:493-509` marks it "security-load-bearing,
  NFR-05"). **The systemuser path has no equivalent guard**, and `AccessibleRecordSetService.ComposeForSystemUserAsync`
  consumes it unfiltered (`AccessibleRecordSetService.cs:192-196`). Concretely: an internal user whose linked contact
  (`systemuser.sprk_primarycontact`, `IdentityNormalizationService.cs:146-150`) is referenced by an adverse
  contact lookup on a matter — or whose employer account is referenced by an account-typed lookup, or whose BU is
  referenced by a BU-typed lookup — is "a member" of that record for SPA-gate purposes. UAC-r2 MUST decide the
  access-conferring role policy for the systemuser plane explicitly (mirror the `sprk_assigned*` + owner/team
  convention) rather than inherit "every descriptor confers access".
- **Under-inclusion also exists** and is worse for an access gate: identity paths fail soft (a transient
  `teammembership` query failure silently drops all team-derived membership,
  `IdentityNormalizationService.cs:417-425`), and truncation at Limit=500 with the continuation token ignored by
  `ComposeAsync` produces **arbitrary (GUID-ordered) false denies** for heavy users. Fail-closed is the right
  direction for security, but silent partial results are not a documented contract anywhere.

### 4.3 Staleness

Membership response 5 min (`:84`) on top of identity 10 min (`IdentityNormalizationService.cs:55`) — worst-case ~10-min
revocation lag for identity-derived membership (team removal), ~5-min for lookup edits. The Redis pub/sub invalidator
(task 086) exists but defaults off (`MembershipModule.cs:196-219`). Acceptable for scoping; must be an explicitly
accepted risk (or the invalidator enabled) for access decisions.

### 4.4 Bottom line for UAC-r2

Use the spine as the **"who are the members" policy source on the BFF plane** (it is the only mechanism that works for
contacts, and `AccessibleRecordSetService` already builds fail-closed denial on top of it) — but (a) never conflate its
answer with Dataverse-plane access (the push/POA model must do its own grants), (b) fix the completeness contract
(limit/continuation in `ComposeAsync`, ordering in the resolver) before relying on it for deny decisions at scale, and
(c) define the systemuser-plane access-conferring role policy explicitly.

---

## 5. The transitive/child primitive

### 5.1 What it does

`ResolveTransitiveAsync` (`MembershipResolverService.cs:926-1020`): for each requested related entity —

1. `DiscoverLookupsTargetingAsync(relatedEntity, primaryEntity)` (`MembershipFieldDiscoveryService.cs:713-775`)
   returns every Lookup on the child whose `Targets[]` includes the parent (sorted, lowercase). **Not Redis-cached** —
   one `RetrieveEntityRequest` per call, relying on SDK-level warmth (`:737-743`).
2. Zero back-refs → `MembershipDepthExceededException("not-a-direct-lookup-target")` (`:981-990`); unknown entity →
   `("unknown-entity")` (`:969-979`). Endpoint maps both to 400 (`MembershipEndpoints.cs:297-318`).
3. `BuildTransitiveFetchXml` (`:1030-1077`) emits ONE query per related entity:

```xml
<fetch top='5000'>            <!-- MembershipResolveOptions.MaxLimit; no paging, no order -->
  <entity name='{child}'>
    <attribute name='{backref1}' /> ... <attribute name='{backrefN}' />
    <filter type='or'>
      <condition attribute='{backref1}' operator='in'>
        <value>{parentId1}</value> ... <value>{parentIdK}</value>
      </condition>
      ...one in-condition per back-ref lookup...
    </filter>
  </entity>
</fetch>
```

4. Materialization mirrors the primary path per back-ref "role" (role name derived from the field name,
   `:1085-1175`); results land in `MembershipResponse.RelatedByRole` (outer key = child entity, inner key = role,
   value = child ids — `Models/MembershipResponse.cs:100-122`).

**Input/output shape**: parent entity + parent ids (the just-resolved, limit-truncated `ids[]`) + child entity names →
`relatedEntity → (role → childIds[])`. Empty parent ids still runs discovery (validation) and returns empty inner maps
(`:992-1002`).

### 5.2 Caps and cardinality risks

- **Hop cap = 1**, enforced twice: preflight rejection of `.`/`/` chain syntax before any I/O (`:164-185`) and the
  no-back-ref throw (`:981-990`). ADR-034 MUST (`.claude/adr/ADR-034-user-record-membership.md:58, 68`).
- **`in`-clause cardinality**: value count = primary ids, defensively capped at `MaxLimit` **5000**
  (`:1038-1040`) — but the method's own doc-comment claims the `in` operator "handles up to 500 values per condition
  in standard Dataverse" (`:1026-1028`). At default Limit=500 this is fine; a caller that raises Limit toward 5000
  walks into the contradiction. Unverified against live Dataverse limits — flag for UAC-r2 perf testing.
- **Result cap**: `top='5000'`, **no paging, no continuation** — a parent set whose children exceed 5000 rows is
  silently truncated (`:1042-1048`). For an access decision this is a silent false-deny vector.
- **A child with many parent lookups** (e.g., `sprk_todo`'s 11 `sprk_regarding*`): all back-refs land in ONE query as
  OR'd `in` conditions — 11 × (≤500 values) ≈ 5,500 values in one FetchXml. Size/limit risk, single round trip.

### 5.3 Live wiring today

| Caller | Status |
|---|---|
| `GET /api/users/me/memberships/{entityType}?includeRelated=` | **LIVE** — `MembershipEndpoints.cs:152, 256, 265-267`; exercised by `tests/integration/Sprk.Bff.Api.IntegrationTests/Membership/TransitiveMembershipTests.cs:45,108,144` |
| `LookupUserMembershipNodeExecutor` (playbook node, ActionType 52) | **BROKEN when `includeRelated=true`** — passes sentinel `["*"]` (`Services/Ai/Nodes/LookupUserMembershipNodeExecutor.cs:234`) under a stale "resolver accepts-but-ignores" comment (`:226-230`). Post-task-054 the resolver treats `"*"` as an entity name → metadata fetch fails → `MembershipDepthExceededException("unknown-entity")` (or a raw Dataverse fault) → node errors. Latent bug; file with UAC-r2 or defer-issue. |
| `AccessibleRecordSetService.ComposeAsync` | **NOT wired** — passes `options: null` on both planes (`AccessibleRecordSetService.cs:193, 277`); never reads `RelatedByRole` |
| Contact entry point | **Cannot use it** — `ResolveByContactAsync` never calls it (`:468-479`) |

---

## 6. The three asserted gaps — all CONFIRMED

| # | Briefing claim | Verdict | Evidence |
|---|---|---|---|
| (a) | `ResolveByContactAsync` has no transitive path | **CONFIRMED** | `MembershipResolverService.cs:468-479` (`RelatedByRole: null`, comment "the contact-anchored path does not do transitive expansion"); contract doc `IMembershipResolverService.cs:73-79`. Additionally: `options.IncludeRelated` is *silently ignored* on this path (no preflight, no error) — a caller cannot even detect the omission. |
| (b) | `AccessibleRecordSetService.ComposeAsync` never calls the transitive path | **CONFIRMED** | `AccessibleRecordSetService.cs:192-194` (`ResolveAsync(systemUserId, entityType, options: null, ct)`) and `:276-278` (`ResolveByContactAsync(contactId, entityType, options: null, ct)`). No `IncludeRelated`, no `RelatedByRole` consumption anywhere in the file. |
| (c) | Contact membership is gated behind the standing-grant flag | **CONFIRMED** | `AccessibleRecordSetService.cs:272-284` — `HasStandingGrantAsync(contactId)` gates the `ResolveByContactAsync` union term; negative case explicitly load-bearing (`:12-15`, `:272-273`). Reader is fail-closed on any fault/FLS-stripped read (`ContactStandingGrantReader.cs:69-127`, FLS observability warning `:94-102`). Locked by seam test `tests/integration/seam/ExternalAccess/StandingGrantRuntimeUnionSeamTests.cs:52` (enable→access, disable→revoke). |

---

## 7. Performance and scale

### 7.1 Round trips per request (systemuser path, cold)

| Step | Dataverse round trips | Cache |
|---|---|---|
| Response cache probe | 0 | 1 Redis GET |
| Discovery | 0-1 (`RetrieveEntityRequest`, 60-min Redis TTL) | 1 GET (+1 SET on miss) |
| Identity normalization | on miss: systemuser Retrieve + `teammembership` RetrieveMultiple (parallel), + contact-by-oid query (only if no `sprk_primarycontact`), + contact Retrieve for account, + 1 per org resolver | 1 GET (+1 SET), 10-min TTL |
| Primary FetchXml | 1 | — |
| Transitive (per `includeRelated` entry) | **2** (uncached metadata + query) | — |
| Response cache write | 0 | 1 SET |

Cold ≈ **4-7 Dataverse round trips** (+2 per related entity); warm = 1 Redis GET. Contact path cold = 2 (discovery
cached) round trips.

### 7.2 Condition fan-out on the primary query (N+1-shaped risk in-query)

Team and Organization descriptors emit **one `eq` condition per identity value per field** (`:716-741`) — not `in`.
A user in T teams with two team-typed fields ⇒ 2T conditions; org ids are capped at 1000
(`MembershipOptions.cs:159`) ⇒ up to 1000 conditions per org-typed field. Large team/org cardinality can push the OR
filter past Dataverse FetchXml condition limits (≈500 conditions/query historically) → runtime query failure. The
transitive path already uses `in`; the primary path should too before UAC-r2 leans on it.

### 7.3 Paging is not sound (two defects)

1. **No `<order>` element** in `BuildFetchXml` (`:647-655`) — server row order across pages is not guaranteed stable;
   client-side GUID sort of an arbitrarily-windowed page makes the continuation cursor semantically incoherent.
2. **Off-by-one at every page boundary**: page 1 fetches `top=limit+1` and keeps `limit`; page 2 is emitted as
   `page='2' count='limit+1'`, which in FetchXml semantics skips `(page-1)×count = limit+1` rows — so the sentinel row
   (position limit+1) is **never returned by any page** (`:647-654`, `:305`, `:836-840`).

For scoping this loses a record per page; for an access decision it is a structural false-deny. Nobody currently pages
(the SPA gate stops at 500), which is itself defect #2 of §4.

### 7.4 11 parent lookups × many roots

For "children of everything I'm a member of" across a child like `sprk_todo` (11 parent types): 11 × `ResolveAsync`
(one per parent entity) + 11 transitive queries ≈ **22+ Dataverse round trips cold**, ~11 Redis hits warm, with a
5000-row silent cap per child query and ≤500-value `in` lists. Feasible for read-time compose with caching, but the
per-request cost argues for either (a) the ScopeDimension precomputed-root-set model (children filtered by parent
lookup ∈ root set — no extra membership round trips), or (b) junction-table reads (Phase 2 exists write-side only).

### 7.5 Reconciliation job scale note

`MembershipReconciliationJob` upserts **one junction row per (parent, field, person) triple per night** via individual
`HandleAsync` calls (`MembershipReconciliationJob.cs:520-569`) — no batching (`ExecuteMultiple`). At matter/document/
event/task/opportunity volume this is a large nightly write fan-out; fine today, worth watching if UAC-r2 adds entity
types to `Membership:Reconciliation:EntityTypes`.

---

## 8. ADR-034 constraint map (concise ADR is the binding source: `.claude/adr/ADR-034-user-record-membership.md`)

| # | Rule (near-verbatim) | Line | UAC-r2 impact | §6.5 path needed? |
|---|---|---|---|---|
| M1 | MUST use `MembershipResolverService` (via DI) for any "records this user is associated with" query; no ad-hoc FetchXML re-derivation | :52 | Binds the cascade design: the "who are the members" answer must come from this service. A NEW record→members read direction (see M-note below) is an extension, not a violation, but should be surfaced | No (C) if resolver reused; B if a parallel membership mechanism is introduced |
| M2 | MUST include all 6 identity tables | :53 | Inherited automatically (seeded defaults) | No |
| M3 | MUST apply the 4 global audit-field exclusions | :54 | Inherited | No |
| M4 | MUST resolve `sprk_assignedlawfirm1/2` to `identityType="Organization"` | :55 | Inherited | No |
| M5 | MUST reuse `SystemAdmin` policy for admin endpoints | :56 | Any new UAC admin surface must reuse it | No |
| M6 | MUST use standard Auth v2 OBO on the user-facing endpoint | :57 | New user-facing endpoints follow suit | No |
| M7 | **MUST cap `includeRelated` at 1 hop**; multi-hop → 400 | :58 | Parent→child is exactly 1 hop — the core UAC-r2 requirement is **compliant as-is**. Grandchild inheritance (parent→child→grandchild, e.g., matter→event→todo-regarding-event) would violate | Path B (amendment) only if >1 hop is required; otherwise C |
| M8 | MUST publish `MembershipChangedEvent` to topic `sprk-membership-changes` (not queue, not the job queue) | :59 | If UAC-r2 triggers cascade grants off membership changes, it must consume this topic (subscription-per-consumer), not invent a new channel | No |
| M9 | MUST use fire-and-forget publish semantics; recon job is the backstop | :60 | Cascade triggers built on the topic inherit at-least-once + 24h-backstop semantics — reconciliation is mandatory in any push design | No |
| M10 | MUST keep the Phase 1A ↔ Phase 2 endpoint contract byte-identical | :61 | Adding transitive output to the contact path changes `ResolveByContactAsync`'s documented shape (`RelatedByRole` always null — `IMembershipResolverService.cs:78-79`); an additive nullable field is arguably contract-compatible but should be stated | Likely C (document); A if reviewer deems it a contract change |
| N1 | MUST NOT match identity by free-text display-name | :65 | Keep any new member policy off display names | No |
| N2 | MUST NOT join through `sprk_matterteammember` **or any other non-existent entity** | :66 | **Precision matters (see below)** | No for lookup-based cascade; B if a real member entity is introduced |
| N3 | MUST NOT introduce a new "PlatformAdmin" policy | :67 | Reuse SystemAdmin | No |
| N4 | MUST NOT extend transitive queries beyond 1 hop; reject with 400 before any Dataverse query | :68 | Same as M7 | Same as M7 |
| N5 | MUST NOT assume Phase 1A per-request FetchXML suffices forever; monitor p95, Phase 2 junction is the escape hatch | :69 | UAC-r2's scale analysis (§7) is exactly this monitoring; a junction-read path may become the fix for §7.2-7.4 | No |
| N6 | MUST NOT confuse "Membership" with the `AssociationResolver` PCF | :70 | Naming hygiene in UAC-r2 docs | No |

**On the "`*teammember` ban" (N2)** — the rule as written bans joining through **non-existent entities** (the R2-UAT
defect was FetchXML joining `sprk_matterteammember`, which does not exist — full ADR context at
`docs/adr/ADR-034-user-record-membership.md:16`). It is **not** a general ban on teammember-shaped intersect entities:
the real `teammembership` intersect is legitimately queried at `IdentityNormalizationService.cs:383-391`. If UAC-r2's
design introduced a REAL per-record member entity (access-team-style), ADR-034 does not literally forbid it — but M1
("one canonical mechanism") makes that a **path B amendment** conversation, not a silent addition.

**Direction note**: ADR-034's contract is strictly **user → records**. A "record → members" read API (which a push
cascade needs to enumerate grantees) is outside the current contract; however the data already exists both in the
source lookups (the recon job scans record→(person, role) triples, `MembershipReconciliationJob.cs:520-569`) and in
`sprk_userentityassociation` (queryable by `sprk_entityrecordid`). Adding an inverse read is an ADR-034 **path B
amendment** (extend the canonical mechanism) — cheap, and better than a bespoke resolver.

---

## 9. Test baseline

### 9.1 What exists (behavioral, membership-relevant)

**Unit — `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Membership/`**
- `MembershipResolverServiceTests.cs` (~33 tests): happy path; **no-distinct regression** (`:94` — asserts the
  generated FetchXml, pinning the r5 id-loss fix); byRole attribution; empty-bucket semantics; cache-hit; role/
  identity-type filters; empty-descriptor/zero-condition/zero-row paths; `member_skipped` warnings (3 tests);
  transitive: nested `RelatedByRole` (`:581`, `:639`), null-when-not-requested (`:693`), chain-syntax 400 (`:709`),
  unknown-entity (`:731`), no-back-ref (`:758`), empty-primary nested shape (`:786`); serialization contract
  (`:814`, `:848`); **contact allowlist block** (`:873-1019`): allowlisted role resolves without identity
  normalization (strict mock proves it), **adverse/non-allowlisted fields never reach the FetchXml** (`:909-942` —
  asserts `sprk_opposingcounsel`/`sprk_regardingrecordid` absent from the query), new convention field auto-qualifies
  (`:945`), exclusion-list suppression (`:967`).
- `MembershipFieldDiscoveryServiceTests.cs`, `IdentityNormalizationServiceTests.cs`,
  `OrganizationMembershipResolverTests.cs`, `MembershipOptionsTests.cs`, `MembershipJunctionUpdaterTests.cs`
  (idempotency), `MembershipReconciliationJobTests.cs`, event publisher/cache-invalidator tests.
- `Api/Membership/MembershipEndpointsTests.cs`, `Api/Admin/MembershipAdminEndpointsTests.cs`.

**Unit — `tests/unit/Sprk.Bff.Api.Tests/Infrastructure/ExternalAccess/AccessibleRecordSetServiceTests.cs`** (14):
includes true negatives — `IsRecordAccessibleAsync_SystemUser_RecordOutsideMembership_DeniesFalse` (`:78`),
`ComposeAsync_ContactWithGrantNoStanding_ReturnsExactlyGrantsAndNeverAutomaticMembership` (`:182`),
`…NonGrantedRecord_DeniesFalse` (`:207`), `…RecordOutsideUnion_DeniesFalse` (`:266`),
`…EmptyRecordId_DeniesFalseWithoutComposing` (`:284`), cross-root-type non-leak (`:240`, `:330`).

**Integration** — `tests/integration/Sprk.Bff.Api.IntegrationTests/Membership/`: `TransitiveMembershipTests.cs`
(live nested byRole; multi-hop 400; no-back-ref 400), `TransitiveMembershipPerfTests.cs`, `Phase2EndToEndTests.cs`;
**seam** — `tests/integration/seam/ExternalAccess/StandingGrantRuntimeUnionSeamTests.cs` (standing-grant
enable→union→disable→revoke without materialized rows; cache invalidation on toggle).

### 9.2 Is there a negative test proving a non-member is excluded?

- **At the gate (mocked)**: yes — the `DeniesFalse` family above.
- **At the resolver (query-shape)**: partially — the adverse-field tests assert exclusion *from the emitted FetchXml*,
  which is the strongest unit-level form available since row filtering is delegated to Dataverse.
- **Against live Dataverse**: **no** — no integration test provisions a non-member and asserts zero rows come back
  from the real OR-filter. The unit mocks return canned rows, so the actual data-layer exclusion is untested.

### 9.3 Adequacy for building access decisions on top

**Not yet adequate.** Missing, in priority order for UAC-r2:
1. Any test of **truncation/continuation semantics** (>limit membership; `hasMore`; token round-trip) — the exact
   seam where §4's false-deny defect lives; nothing pins the (broken) paging behavior of §7.3.
2. A live-Dataverse **non-member exclusion** test (integration, `tests/integration/auth/**` shape per ADR-038).
3. Any test of `ComposeAsync` completeness above 500 records.
4. Contact + transitive combination tests — cannot exist until the capability does; must arrive with it.
5. A test pinning `LookupUserMembershipNodeExecutor` + `includeRelated=true` (currently broken, §5.3, untested).

---

## 10. Doc accuracy — corrections to the two project docs (and two stale sources)

### 10.1 Briefing `spa-external-access-model-briefing.md` §4

| # | Claim | Verdict | Correction |
|---|---|---|---|
| B1 | "Membership = discovery over lookup columns, NOT a junction table" | **IMPRECISE** | True for the READ path; but the materialized junction `sprk_userentityassociation` exists, is written nightly by default (`MembershipJunctionUpdater.cs:82,192-244`; `MembershipReconciliationOptions.cs:73` Enabled=true), and is a record↔person index UAC-r2 could read. Only the resolver ignores it. |
| B2 | 6 identity tables, cites `MembershipFieldDiscoveryService.cs:195-352`, `MembershipOptions.cs:241-249` | **CONFIRMED** | Line refs valid in this worktree. |
| B3 | Owner/Customer synthesis `:494-550` | **CONFIRMED** | — |
| B4 | "ADR-034 **bans** joining through a `*teammember` entity" | **WRONG as stated** | ADR-034 (`.claude/adr/…:66`) bans `sprk_matterteammember` "or any other **non-existent** entity". The real `teammembership` intersect is used by the spine itself (`IdentityNormalizationService.cs:383-391`). The ban does not preclude a future real member entity — but M1/one-canonical-mechanism makes that a §6.5 path B. |
| B5 | `ResolveAsync` `:135-346` full normalization, transitive support | **CONFIRMED** | — |
| B6 | `ResolveByContactAsync` `:349-491`; allowlist-first `:511-558`; `sprk_assigned` prefix; no transitive `:479` | **CONFIRMED** | Add: `IncludeRelated` is *silently ignored* (no 400), and the prefix is configurable (`Membership:AccessConferringRoles:ConventionPrefix`). |
| B7 | Transitive primitive cites incl. `DiscoverLookupsTargetingAsync` at `MembershipFieldDiscoveryService.cs:732-793` | **STALE line ref** | In this worktree: `:713-775` (r3-worktree offsets differ). `ResolveTransitiveAsync :926-1020` and `BuildTransitiveFetchXml :1030-1077` are correct here. |
| B8 | "Capped at 1 hop … deeper needs a §6.5 ADR exception" | **CONFIRMED** | — |
| B9 | Three gaps (§4 list) | **ALL CONFIRMED** | See §6 above with exact lines. |

### 10.2 Briefing §8 (and §10.1, §11 membership rows)

| # | Claim | Verdict | Correction |
|---|---|---|---|
| B10 | Model B "built (task 054) but **not** wired into the external gate; systemuser-only" | **IMPRECISE** | Not wired into the external gate: TRUE (`AccessibleRecordSetService.cs:193,277`). But it is NOT dormant: it is **live** on `GET /api/users/me/memberships/{entityType}?includeRelated=` (`MembershipEndpoints.cs:256,265-267`) with integration coverage. And one live caller is **broken**: `LookupUserMembershipNodeExecutor.cs:234` passes `["*"]` under a stale accepts-but-ignores comment — now throws. |
| B11 | Model B "works for contacts ❌ (`ResolveByContactAsync` disables it)" | **CONFIRMED** | — |
| B12 | Hop limit 1 for both models | **CONFIRMED** | — |
| B13 | §10.1: "the SPA already answers this with `FilterToAccessConferringContactRoles`… **Reuse that convention on both planes**" | **IMPRECISE framing** | The allowlist exists ONLY on the contact path. The systemuser plane today confers membership (and therefore SPA-gate access) on **every** descriptor — adverse contact lookups (via linked contact), `sprk_regarding*`, account, BU included. "Reuse on both planes" is a genuine NEW work item, not an already-answered question. |
| B14 | §11 row "Enable transitive for contacts … `:349-491` currently nulls `RelatedByRole`" | **CONFIRMED** | — |

### 10.3 Cascade doc `unified-access-control-cascade.md` §3/§4

| # | Claim | Verdict | Correction |
|---|---|---|---|
| C1 | Membership resolver: generic, metadata-driven, direction user→records | **CONFIRMED** | — |
| C2 | Field discovery: 6 identity tables | **CONFIRMED** | — |
| C3 | Identity normalization: systemuser → {…6 components} | **CONFIRMED**, one addition | Contact resolution is `sprk_primarycontact`-FIRST with AAD-oid cross-ref as fallback (`IdentityNormalizationService.cs:139-150`) — relevant because it is the channel through which a systemuser inherits contact-lookup memberships. |
| C4 | Grant seam: `IDataverseAccessGrantService` / `DataverseWebApiService.GrantAccessAsync`, used by Communication thread-access | **CONFIRMED** | `Services/Communication/Access/IDataverseAccessGrantService.cs`, `DirectThreadAccessService.cs`, `Spaarke.Dataverse/DataverseWebApiService.cs`. |
| C5 | "Reconciliation: nightly rebuild of `sprk_userentityassociation`; Service-Bus event sync (operator-gated, default off)" | **IMPRECISE** | Split the gating correctly: the **recon job is enabled by default** (`MembershipReconciliationOptions.cs:73`, cron 02:00 UTC, entity set matter/document/event/task/opportunity `:48-55`) and writes the junction directly; only the **event publisher / SB junction-updater host / cache invalidator** are kill-switched default-off (`MembershipModule.cs:157-268`). Also "rebuild" overstates: it is an idempotent upsert + orphan-removal pass, not a truncate-and-rebuild. |
| C6 | Gap: "resolver answers user→records, not record→members-then-cascade; no parent↔child access bridge; `sprk_userentityassociation` is a user↔record index, not a cascade" | **CONFIRMED with a refinement** | No PUBLIC record→members API exists — true. But the record→members DATA exists twice: the recon job's parent scan enumerates (record, field, person, role) triples (`MembershipReconciliationJob.cs:520-569`) and the junction is queryable by `sprk_entityrecordid`. An inverse read API is an ADR-034 amendment away, not greenfield. Also the transitive primitive IS a parent→child *bridge* (compute-model), just not a grant cascade. |
| C7 | §4 member-group examples (`sprk_event`, `sprk_matter` lookup inventories) | **NOT VERIFIED here** | Data-model claims; verify against live metadata / `docs/data-model/` in the schema investigation. Mechanism claims around them are correct. |
| C8 | "the two hard pieces already exist — a membership resolver … and a POA grant seam" | **CONFIRMED**, with the §4 caveat | The resolver's answer is not access-policy-filtered on the systemuser plane; the "which subset confers access" policy decision (§7.1 of the cascade doc) is genuinely open. |

### 10.4 Stale sources discovered along the way (not in either project doc)

1. **ADR-034 concise** (`.claude/adr/ADR-034-user-record-membership.md`): (a) Key Types block omits
   `ResolveByContactAsync` and `MembershipResponse.RelatedByRole` (both added post-R3 by teams-app-r1 / task 054);
   (b) the Identity Normalization Contract table documents only the `azureactivedirectoryobjectid` contact cross-ref —
   code now prefers `systemuser.sprk_primarycontact` (`IdentityNormalizationService.cs:139-150`).
2. **`MembershipEndpoints.cs:33, 113-114`**: "includeRelated … ACCEPTED-BUT-IGNORED — task 054 implements" — stale;
   task 054 landed and the parameter is live.
3. **`LookupUserMembershipNodeExecutor.cs:226-230`**: the "accepts-but-ignores" premise behind the `"*"` sentinel is
   stale and the sentinel now breaks the node when `includeRelated=true` (§5.3).
