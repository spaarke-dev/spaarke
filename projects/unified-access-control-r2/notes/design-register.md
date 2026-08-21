# UAC-r2 — Design Register

> **Purpose**: single consolidated list of everything surfaced during the 2026-08-20 investigation, so nothing
> is lost when `design.md` is written. Sources: `notes/investigation/01`–`09` + the design-alignment session.
> **Status legend**: ✅ verified in main session · 🟡 reported by one pass, not independently verified · 🔵 decision · ⚪ open

---

## A. Security findings — Phase 0 remediation candidates

### A.1 Verified directly (main-session code read)

| # | Finding | Evidence | Effect |
|---|---|---|---|
| A-1 | `GET /api/documents/{id}/download` has **no authorization filter** — group `RequireAuthorization()` only, then app-only SPE stream | `Api/FileAccessEndpoints.cs:101-109`, `:865` | Any authenticated user downloads any document by GUID. **This is R1's January 2026 attack scenario, still open** |
| A-2 | `AuthorizationService` always passes `userAccessToken: null` | `Spaarke.Core/Auth/AuthorizationService.cs:48-52` | The "user permission check" answers *can the app see it*. Never caller-scoped. `RetrievePrincipalAccess` never called |
| A-3 | `"read"` is not a key in `OperationAccessPolicy` | `OperationAccessPolicy.cs:161` (TryGetValue) | `FileAccessEndpoints.cs:118` (eml-render) + `DataverseDocumentsEndpoints.cs:443` are unconditionally 403 |
| A-4 | `PermissionsEndpoints` returns app-scoped capabilities | `Api/PermissionsEndpoints.cs:76` | Any authenticated user learns any document's capabilities |
| A-5 | `sprk_expiresdate` written, never read — absent from `$filter` and `$select` | `ExternalParticipationService.cs:405-407`; only write-side refs repo-wide | External grant expiry does not work. No sweep job |
| A-6 | Grant/revoke/invite/invite-and-grant/close-project/provision-project behind bare `RequireAuthorization()` | `Api/ExternalAccess/ExternalAccessEndpoints.cs:109-111` | Any authenticated user can mint external grants **and provision business units** |
| A-7 | `PATCH /api/v1/external/todos/{id}` has no record-scope check (self-acknowledged in comment) | `Api/ExternalAccess/ExternalProjectDataEndpoints.cs:328-345` | Any resolvable caller modifies any To Do by GUID |
| A-8 | SPA systemuser branch uses **raw** ADR-034 membership (no access-conferring allow-list) + blanket `Collaborate` stamp | `AccessibleRecordSetService.cs:192-194`; `CallerPrincipalResolver.cs:360`, `:429-431` | Over-inclusion at write level; **elevates a deliberate ViewOnly grant to Read\|Create\|Write** |
| A-9 | Only 3 accessible-root sets exist; no communication/event/todo sets anywhere | `CallerPrincipalResolver.cs:91-102`; grep = 0 hits | Child derivation cannot reach 8 of `sprk_todo`'s 11 parents |

### A.2 ✅ ALL CONFIRMED 2026-08-20 — independent verification pass

Full evidence + failure scenarios: [`notes/investigation/10-finding-confirmations.md`](investigation/10-finding-confirmations.md).
**13 of 13 confirmed, none refuted** (A-22 partial — real failure, different cause than reported).

| Severity | Findings |
|---|---|
| **High** | A-11 (grant survives revoke) · A-17 (caller-controlled FetchXML exfiltration) · A-14 (permanent anonymous links) · A-18 (email-fallback contact binding) · A-21 (AI trimming inert) |
| **Medium** | A-12 · A-19 · A-10 · A-13 · A-20 |
| **Low** | A-16 · A-15 · A-22 |

| # | Finding | Reported location |
|---|---|---|
| A-10 | Membership resolver paging broken (no `<order>` + off-by-one); `ComposeAsync` passes `options:null` (limit 500) and ignores continuation → **silent truncation** for >500 memberships | `MembershipResolverService.cs:647-655`; `AccessibleRecordSetService.cs:193,277` |
| A-11 | `/grant` non-idempotent → duplicate active rows; `/revoke` deactivates one by id → **access survives revocation**; read-side `GroupBy` masks it | `GrantExternalAccessEndpoint.cs`, `RevokeExternalAccessEndpoint.cs` |
| A-12 | `close-project` cascade `$select`s stale `_sprk_contactid_value` → Dataverse 400 → unhandled 500; org-grant rows excluded from sweep | `ProjectClosureEndpoint.cs:181`, `:190` |
| A-13 | SPE revoke matcher searches contact GUID **inside the UPN** → effectively never matches | `RevokeExternalAccessEndpoint.cs:230-232` |
| A-14 | Anonymous non-expiring share links — untracked, unrevocable | `FileAccessEndpoints.cs:640-642` |
| A-15 | `AccessibleRecordSetAuthorizationFilter` defined but attached to no route (orphaned) | grep-verified by pass 01 |
| A-16 | Uncapped `in`-clause in scope injector (vs ~500 cap the membership service applies to itself) | `Tier2ScopeFilterInjector.cs:81-84` vs `MembershipResolverService.cs:1027` |
| A-17 | FetchXML guard rejects *other-entity* refs but permits **same-entity self-joins** | `ExternalModuleDataEndpoints.cs:160-161` |
| A-18 | Workforce contact-by-email fallback lacks CIAM's no-hijack `oid` check | `IdentityNormalizationService.cs:264-278` |
| A-19 | `CachedAccessDataSource` key ignores auth mode → SP-mode snapshot can poison OBO reads for 60s | `CachedAccessDataSource.cs:65` |
| A-20 | Always-403 operations beyond A-3: `finance.read`, `finance.confirm`, `entity.associate_document`; Read-ceiling kills `canwritefiles`/`canmanagecontainers` | pass 03 |
| A-21 | AI-search security trimming is a pass-through — `privilege_group_ids` never populated at index time | `RecordSyncJob.cs:557` |
| A-22 | `LookupUserMembershipNodeExecutor` passes `["*"]` under a stale comment → throws depth-exceeded | `LookupUserMembershipNodeExecutor.cs:234` |

---

## B. Settled design decisions

| # | Decision |
|---|---|
| B-1 | **Derived access is default-on; Secure is the veto.** Standing Grant becomes a per-subject standing arrangement, not the switch that enables derivation |
| B-2 | **Level precedence = highest wins** across additive terms |
| B-3 | **Surface split**: MDA = Dataverse-enforced (no code). SPA/Teams = BFF-enforced |
| B-4 | Type 1 (systemuser) + Type 2 (customer employee, no licence) authenticate via **workforce Entra**; Type 3 (external) via **CIAM**. Multitenant app registration + per-customer admin consent is the prerequisite for Type 2 |
| B-5 | **Type 1 record permission = Dataverse's real answer** (Option B, impersonated read) **∪ contact grants**. Types 2 & 3 = `sprk_assigned*` ∪ org. Types 2 & 3 have **no business unit** |
| B-6 | **Core records** (project, matter, work assignment, service request) require direct grants. **Child records** (invoice, communication, document, event, to-do, analysis) inherit from parent |
| B-7 | **1 hop only.** RegardingResolver denormalizes the **ultimate core-record ancestor** onto the child, so todo→email→matter is 1 hop by construction. Re-stamp on reparent |
| B-8 | Evaluator returns **(recordId → rights)**, not a set of ids. `AccessibleRecordSet` as a `HashSet<Guid>` structurally cannot carry level — this is why matters/WAs have none today |
| B-9 | **Vetoes evaluated after the max**: deny list (ethical wall + child-level revocation) → Restricted (no contacts at all) → Secure (suppress derived + org before the max) |
| B-10 | **"No Access" is NOT a level.** Under highest-wins, max() would ignore it and the wall would silently fail. It is a deny term |
| B-11 | Ethical-wall data model: "No Access List" subgrid on Contact matched against the record's Organization subgrid; extend to individual contacts. Same machinery serves child-level revocation (B-6) |
| B-12 | **Access-conferring allow-list must extend to org-typed lookups.** Today only contact-typed lookups are allow-listed — org expansion would otherwise hand access to any org on the record, including opposing counsel |
| B-13 | **Secure Project = Secure BU + service-account owner + share-only.** No one has access by ownership or BU; everyone by explicit share. Avoids matrix-data-access dependency and BU-per-project proliferation |
| B-14 | **Delegation rule**: you may grant access to a record if you have **Write** on that record (OBO check in the endpoint) |
| B-15 | **Attestation**: append-only log for grant/deny changes + evaluator replay over existing Dataverse field audit for derived access. **Do not materialize derived access into rows** |
| B-16 | Option B mechanism = app-only impersonated `RetrieveMultiple` (`MSCRMCallerID` + systemuserid). Already built and live: `DataverseImpersonation.cs`, `DataverseWebApiService.RetrieveMultipleImpersonatedAsync:953-989` (fail-closed on `Guid.Empty`). ~3 round trips/request — same as today |
| B-17 | Option B removes the need for a systemuser allow-list: with Dataverse's real answer there is no approximation to tame. **Keep the contact-grants union** — grants are not POA, Dataverse cannot see them |

---

## C. Open — need an answer

| # | Item |
|---|---|
| C-1 | Truncation semantics when a Type 1 user's real accessible set exceeds FetchXML `in`-clause bounds (~5000). Applies under both Option A and B |
| C-2 | Standing grant currently sits in `ComposeForContactAsync`, which serves **ContactOnly workforce** principals. Verify whether it applies to CIAM partners at all — the UI implies it does |
| C-3 | `sprk_accesspermissiongrant` (level for standing grants) has **zero references repo-wide**. Org-level `sprk_standinggrant` / `sprk_accesspermissions` are not set. What level does a standing grant confer? |
| C-4 | Contact changes firms: org grants drop (junction statecode-filtered) but **individual grants and person-derived access persist**. Auto-revoke, or flag for review? |
| C-5 | `sprk_contactorganization` supports multiple simultaneous orgs. "Changing primary firm" may not deactivate the prior junction row — who does? |
| C-6 | Organization hierarchy — do org grants cascade to sub-orgs / other offices of the same firm? |
| C-7 | Break-glass / emergency access (e.g. GC during an investigation) |
| C-8 | GDPR / right-to-be-forgotten applied to grant rows + the attestation log |
| C-9 | Dual identity level interaction: a Type 1 who is also a contact on the record — Dataverse answer ∪ contact grants, highest wins. Confirm this cannot exceed their MDA rights in a way that surprises |
| C-10 | Does Secure suppress the contact-derived term for **Type 1** as well? (It must, or Secure leaks via the contact path.) On a Secure project, Type 1 access = Dataverse answer only |

---

## D. Deferred by explicit decision

| # | Item | Note |
|---|---|---|
| D-1 | Expiry enforcement | **But**: the UI accepts an expiry date the system ignores. Either enforce (small — add to the read filter) or hide the input |
| D-2 | Project/matter closure + archival semantics | A-12 suggests the existing cascade is broken anyway |
| D-3 | Field-level visibility (show/hide by permission) | SPA simply will not expose sensitive fields for now |
| D-4 | AI plane for contacts | No CIAM route reaches AI search today; A-21 means index-time trimming is unbuilt. Needs an explicit guard before the SPA gains an assistant |
| D-5 | Notification | Partially working: fires only on **first CIAM provisioning**; a subsequent grant is silent and removal sends nothing (R3 gap 6A). Requirement was for add **and** remove |

---

## E. Operational / environmental prerequisites

| # | Item |
|---|---|
| E-1 | **Security-role depth audit**: no standing role may grant Organization-depth Read on `sprk_project` / `sprk_matter`. One hole defeats every secure project silently. Should be a recurring assertion, not a one-time review |
| E-2 | `prvActOnBehalfOfAnotherUser` on the BFF application user. Works in dev only incidentally (app user is System Administrator). **No runbook records the grant** |
| E-3 | BFF app user must stay **Org-scoped** — impersonated privileges are the intersection of app user × impersonated user; tightening the app user silently under-grants |
| E-4 | **Negative canary test** for impersonation: impersonated low-privilege read must return a strict subset AND strictly fewer rows than app-only. Equality = inert impersonation. Must exist before Option B ships |
| E-5 | Size the primary-contact linking backlog from the existing `member_skipped` telemetry (App Insights) — the defect is a missing systemuser→contact link, not a missing contact |
| E-6 | Multitenant app registration + per-customer admin consent for Type 2 workforce sign-in |
| E-7 | Field Security Profile membership for `contact.sprk_standinggrant` — per-environment operator step, reportedly not applied even in dev |
| E-8 | **Phase 0 spike**: verify secure-BU behaviour in dev — record in Secure BU owned by service account, shared with a Spaarke user; confirm the shared user reads it and a non-shared Spaarke user cannot |

---

## F. UI surfaces to rework

| # | Surface | Work |
|---|---|---|
| F-1 | **Access Permission PCF / `AccessGrantModal`** — becomes the single Manage Access surface. Add: **"+ User"** system-user picker (Dataverse share via POA), service-user assignment + BU alignment, contact access, organization access, level per principal, standing-grant visibility, deny-list entries, Secure/Restricted state rendering. Current Access list shows **provenance** per row (share · grant · org grant · standing · derived-from-field) and renders suppressed rows struck through under Secure. Writes to two stores (POA / `sprk_externalrecordaccess`) behind one uniform UI |
| F-2 | **Create Project wizard — "Secure Project" step.** Current copy promises a *dedicated Business Unit*, an SPE container, and *"a Power Pages workspace is activated"*. **Power Pages is retired** (SWA + CIAM). Rework to the B-13 model; the permanence warning ("designation cannot be removed") should be re-evaluated — under B-13 the designation is reversible |
| F-3 | Effective-access view: "what can this person do here, and why" for a single principal. Same query as attestation; support will need it on day one |
| F-4 | Restricted banner currently states "only system users may have access" while the modal offers no way to grant a system user — resolved by F-1 |

---

## G. Documentation drift to fix

| Doc | Drift |
|---|---|
| `docs/architecture/uac-access-control.md` | Three-plane table + "single grant orchestrates all three" (false); dual-mode `RetrievePrincipalAccess` (never called); cache keys/`ITenantCache` (actual `sdap:auth:*`); "all filters call AuthorizeAsync" (false); "Power Pages web role"; External Access Levels SPE-role column (dead); "no participation → Deny" (zero-grant contact gets 200-empty); "70+ ops" (is 66) |
| `.claude/adr/ADR-003-authorization-seams.md` | "two seams" stale; per-request-caching MUST contradicted by `CachedAccessDataSource`; dead link |
| `.claude/adr/ADR-034-user-record-membership.md` (concise) | Omits `ResolveByContactAsync` / `RelatedByRole`; the `*teammember` ban is narrower than both project docs claim (real `teammembership` **is** used) |
| `.claude/constraints/auth.md` | `RetrievePrincipalAccess` claim wrong |
| `.claude/patterns/auth/uac-access-control.md` | `RequestCache` claim wrong (zero consumers) |
| `src/solutions/SpaarkeCore/entities/sprk_externalrecordaccess/entity-schema.md` | 10+ stale points: field names, `sprk_accountid`→`sprk_organization`, phantom expiry worker, retired Power Pages model |
| `docs/architecture/DATAVERSE-ACCESS-LAYER-ROUTING.md` | Auth column stale (#3b landed); attributes impersonation/POA registrations to the wrong module |
| `projects/.../access-model-decision.md` | Pairs `MSCRMCallerID` with the AAD oid — incorrect; the header takes the **systemuserid** (code is right, note is wrong) |

---

## H. Project setup + coordination

| # | Item |
|---|---|
| H-1 | Register `unified-access-control-r2` in `projects/INDEX.md` with hot-path flags. **BFF=Y** (the cascade doc's "BFF=N" is carried over from smart-todo-r5 and is wrong) |
| H-2 | `design.md` needs the `<hot-path-declaration>` block + **Placement Justification** section (CLAUDE.md §10) |
| H-3 | Project naming: "unified access control" already names the shipped subsystem (`docs/architecture/uac-access-control.md`, ADR-003). Decide whether to rename the project or explicitly scope it as the unification of that subsystem |
| H-4 | Write `notes/r1-reconciliation.md` — R1's intents largely shipped under other names; its two docs should be treated as history, not design input |
| H-5 | **ADR work**: ADR-003 amendment (single evaluator; the "rules only / no new auth service layers / per-request cache only" rules no longer describe reality). ADR-034 gains the allow-list as a first-class concept. ADR-028 A2 amendment if the workforce composition rule changes. All CLAUDE.md §6.5 **path B** |
| H-6 | Coordination: `teams-app-r1` and `spaarke-SPA-external-access-platform-r2` have **shipped** (amending settled code, not colliding). `SPA-r3` is a draft assuming dual-plane. `/conflict-check` before every BFF PR; `parallel-safe:false` on shared external-access files |
| H-7 | Publish-size verification per CLAUDE.md §10 (ceiling 60 MB; baseline ~44.96 MB incl. PDBs) |

---

## H.8 Residual code cleanup (found 2026-08-21 during the doc-drift pass — small, unscheduled)

| # | Item |
|---|---|
| H-8a | Stale code comments naming the deleted `ExternalCallerAuthorizationFilter` / `ExternalCallerContext` — `Api/ExternalAccess/ExternalProjectDataEndpoints.cs:13,24,339` |
| H-8b | `RevokeExternalAccessEndpoint.cs:182` returns a vestigial `WebRoleRemoved: false` (Power Pages relic) and retains a best-effort SPE-permission-removal path (`:120-145,197-261`) even though grants never add container members. Related to A-13 — fix together |

## I. Watch items

| # | Item |
|---|---|
| I-1 | Microsoft's 2025-08-07 Power Platform blog describes a **filtered-view predicate RLS** model live in D365 F&O / Power Pages and "expanding to other workloads". No Learn doc, no admin switch, no release-plan entry for custom Dataverse tables — not designable-against, but it would be the first genuine restriction primitive. Re-check next release wave |
| I-2 | Dataverse has **no per-record deny** as of 2026-08-20 (2025 wave 2 + 2026 wave 1 checked). Column-level security is the only restrictive primitive |
| I-3 | POA growth: no documented row limit; mitigate via team ownership / share-to-team; direct POA deletion unsupported |
