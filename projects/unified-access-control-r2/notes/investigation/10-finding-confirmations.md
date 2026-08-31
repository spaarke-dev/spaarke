# UAC-r2 — Finding Confirmations (independent verification of design-register A.2)

> **Pass**: 10 — read-only independent re-verification of the 13 single-pass findings A-10…A-22.
> **Method**: each finding re-read from source; verdict = CONFIRMED / REFUTED / PARTIALLY CONFIRMED, with corrected
> detail where the original was imprecise, a concrete failure scenario, and a severity.
> **Date**: 2026-08-20.

---

## A-11 — /grant non-idempotent; /revoke deactivates ONE row by id; read-side GroupBy masks duplicates

**Verdict: CONFIRMED — Critical/High (silent privilege retention after revoke).**

Evidence:
- `/grant` unconditionally CREATES a new row every call — `GrantExternalAccessEndpoint.cs:147` (`dataverseClient.CreateAsync(EntitySet, payload)`), reached via `CreateGrantAsync` (`:130-147`). There is no pre-existence / upsert check anywhere in `GrantAccessAsync` (`:62-118`) or `CreateGrantAsync`. Two identical grants → two active rows.
- `/revoke` deactivates exactly ONE row identified by `request.AccessRecordId` — `RevokeExternalAccessEndpoint.cs:96-97` (`UpdateAsync(AccessEntitySet, request.AccessRecordId, {statecode=1,statuscode=2})`). It is root- and grantee-agnostic and never queries for sibling active rows for the same (contact, root, level). If two active rows exist, revoking one leaves the other active → **access survives revocation**.
- Read-side masking: `ExternalParticipationService.QueryGrantSetAsync` dedupes projects by id keeping the max level — `:469-472` (`GroupBy(p => p.ProjectId).Select(... AccessLevel = g.Max(...))`). Matter/WA sets are `HashSet` (`:439,:443`) which also collapse duplicates. The participation read path returns `ProjectId + level`, never the underlying `AccessRecordId`s, so effective-access views cannot reveal that N active rows back one logical grant.

Failure scenario: admin grants Contact X FullAccess on Project P (row R1), later re-grants (double-submit, or grant→revoke→re-grant flows) creating row R2. Admin later "revokes" via one `AccessRecordId` (R1). R2 stays `statecode=0`; `GetGrantSetAsync` still returns P at FullAccess. The contact retains access, and the effective-access read shows access-present with no indication a second row remains. Undetectable through the participation surface.

Correction to the register wording: accurate. Add that the org-grant path (empty ContactId) increases duplicate likelihood — an org grant plus a per-contact grant for the same root already both flow through the same GroupBy.

---

## A-10 — Membership resolver paging (no `<order>`, off-by-one); ComposeAsync passes options:null and ignores continuation

**Verdict: CONFIRMED — Medium (silent truncation; fail-closed under-grant + unstable paging).**

Evidence:
- No `<order>` element in the built FetchXml — `MembershipResolverService.BuildFetchXml:637-755`. The `<fetch>` opens at `:647`, projects descriptor fields, adds a `<filter type='or'>`, closes at `:751` — no `<order>` is ever appended. Dataverse `page`/`count` paging without a stable sort order is not guaranteed consistent across pages.
- Off-by-one at page boundaries: `top='{limit+1}'` (`:647`) is used as the has-more sentinel; when `skip>0` it ALSO emits `page='{(skip/limit)+1}' count='{limit+1}'` (`:648-654`). `MaterializeResults` keeps `limit` sorted ids and reports `hasMore = allIds.Count > limit` (`:837`), truncating the (limit+1)th row (`:840`). Next token = `skip + effectiveLimit` (`:305`) → page 2 = `count=limit+1` starting at row `limit+2`, so the (limit+1)th row that page 1 dropped is never returned by page 2 → one row silently lost per page boundary. (Also, mixing `top` with `page`/`count` is itself malformed FetchXml paging.)
- Truncation via `options:null`: `AccessibleRecordSetService.ComposeForSystemUserAsync:192-196` calls `_membership.ResolveAsync(systemUserId, entityType, options: null, ct)` and takes only `membership.Ids` — never following `ContinuationToken`. `null` options → `ClampLimit(0)` → `DefaultLimit = 500` (`MembershipResolverService.cs:1180-1184`, `IMembershipResolverService.cs:149`). `ComposeForContactAsync:276-278` does the same for the standing-grant membership term. Continuation tokens are computed (`:302-306`) but no composer ever consumes them.

Failure scenario: a Type-1 systemuser who owns/participates in >500 records of one entity type (e.g., 900 matters) gets an accessible set capped at the first 500 by ascending GUID. `IsRecordAccessibleAsync` DENIES the other 400 even though membership exists — a fail-closed under-grant (correctness/availability), not a leak. C-1 in the register already flags the analogous >5000 concern.

Correction: register cites `:647-655` and `AccessibleRecordSetService.cs:193,277` — accurate. Impact direction is under-grant (fail-closed), so this is a correctness bug, not a confidentiality hole.

---

## A-12 — close-project cascade $selects stale `_sprk_contactid_value` → 400 → unhandled 500; org rows excluded

**Verdict: CONFIRMED (with a schema-doc caveat) — Medium (broken closure cascade → privilege retention on project close).**

Evidence:
- `$select = "sprk_externalrecordaccessid,_sprk_contactid_value"` — `ProjectClosureEndpoint.cs:181`. The live contact FK is `_sprk_contact_value`, per the runtime read path which explicitly claims live verification: `ExternalParticipationService.cs:406` (`_sprk_contact_value eq {contactId}`) and `:403` ("the contact FK is `_sprk_contact_value` — verified against live Dataverse"). Task-070 already corrected the sibling project field from `_sprk_projectid_value` → `_sprk_project_value` in this very file (`:158-162`) — the contact field was left on the stale `*id_value` form. A `$select` on a non-existent column returns Dataverse 400.
- Unhandled → 500: `QueryActiveAccessRecordsAsync` catches and RETHROWS (`:198-204`); `Handle` calls it at `:95` with no surrounding try/catch, so the exception propagates as an unhandled 500. The cascade deactivates nothing.
- Org rows excluded: `.Where(r => r.sprk_externalrecordaccessid.HasValue && r._sprk_contactid_value.HasValue)` (`:190`) drops any row with a null contact — i.e. organization grants — from the sweep even if the query succeeded.

Caveat: `src/solutions/.../sprk_externalrecordaccess/views-schema.md` references an attribute `sprk_contactid`, so the schema docs are internally contradictory. The authoritative signal is the runtime read code (explicit live-verification comment) using `_sprk_contact_value`; treat the closure endpoint's `_sprk_contactid_value` as the outlier bug.

Failure scenario: admin closes a Secure Project; the endpoint 500s; zero grants deactivated, zero SPE members removed (Step 2/3 never reached). Every external participant retains access post-closure. Loud (500) but functionally inert — and even if the field were fixed, `:190` would still skip org grants.

---

## A-13 — SPE revoke matcher searches the contact GUID inside the UPN → never matches

**Verdict: CONFIRMED — Medium (real logic defect; impact bounded by broker-only model).**

Evidence:
- `RevokeExternalAccessEndpoint.cs:222-235`: `contactIdStr = contactId.ToString()`, then matches a permission whose `AdditionalData["userPrincipalName"]` `.Contains(contactIdStr)` (`:230-231`). SPE permissions are written with `userPrincipalName = contactEmail` — `SpeContainerMembershipService.cs:75` (`["userPrincipalName"] = contactEmail`). An email never contains the contact GUID, so the predicate never matches.
- On no-match, `contactPermission?.Id == null` returns `true` ("may have already been removed", `:237-243`) — the endpoint reports SPE-revoke success while leaving the permission in place.

Failure scenario: revoke of a contact who has a real SPE container permission (keyed by email) leaves the ACL entry intact and reports success. Impact is bounded because the current grant path is broker-only — `GrantExternalAccessEndpoint.cs:116-117` writes NO SPE permission on grant (`SpeContainerMembershipGranted: false`), and external users don't authenticate to SPE directly (ADR-028 A1). So residual ACLs are mostly legacy/invite-path data with limited direct exploitability. Still a defect: the matcher can never satisfy its purpose.

---

## A-14 — Anonymous, non-expiring, untracked, unrevocable share links

**Verdict: CONFIRMED — High (unrevocable anonymous exfiltration link).**

Evidence:
- `FileAccessEndpoints.cs:640-642`: `CreateSharingLinkAsUserAsync(..., linkType: "view", scope: "anonymous", expiration: null, ...)`. Scope `anonymous` = openable with no auth; `expiration: null` = non-expiring (comment at `:637-639` "Non-expiring for now"). The returned URL is handed back (`:653`) and not persisted to any Spaarke store — grep shows no tracking table and no revoke path for these links. Route: `POST /api/documents/{documentId}/share-link` (`:81`).

Failure scenario: any caller with OBO SPE access to a document mints a permanent "Anyone-with-the-link" URL; the link then circulates outside all Spaarke controls, cannot be found, expired, or revoked, and survives any subsequent access-revocation on the document. Requires the tenant SPE/SharePoint external-sharing policy to permit "Anyone" links (else Graph 502s, `:660-668`).

Correction: the link is OBO-authorized (caller must already have SPE access), so it is not an unauth-mint; the risk is the resulting artifact's permanence + anonymity + untracked/unrevocable nature.

---

## A-15 — AccessibleRecordSetAuthorizationFilter defined but attached to no route

**Verdict: CONFIRMED — Low (orphaned/dead enforcement point; live enforcement runs elsewhere).**

Evidence:
- `AddAccessibleRecordSetAuthorizationFilter<T>()` is defined at `AccessibleRecordSetAuthorizationFilter.cs:45`; grep across `src` finds only the definition and the doc-comment example (`:15`) — no route/endpoint calls it.
- The filter reads `WorkforcePrincipal.HttpContextItemsKey` (`:102`), and grep shows that key is READ only there and SET nowhere — no filter places a `WorkforcePrincipal` on `HttpContext.Items`. The file header itself says the record-scoped endpoints that attach the gate "are added by task 030" — task 030 never wired it.

Failure scenario: no direct vulnerability — the external-module read seam enforces Tier-2 via the CallerPrincipal path instead (`ExternalModuleDataEndpoints.GetScopedRecordAsync:266-273`, `ExecuteScopedFetchAsync` + `ExternalModuleRegistry`). The finding is real (dead code / an intended workforce record∈set gate that was never attached). Risk only materializes if a future broker/document-stream route assumes this gate is active. Confirmed as orphaned.

---

## A-16 — Uncapped `in`-clause in the scope injector vs the ~500 cap the membership service applies to itself

**Verdict: CONFIRMED — Low (robustness/availability; not confidentiality).**

Evidence:
- `Tier2ScopeFilterInjector.Inject:76-86` adds one `<condition operator='in'>` per dimension with one `<value>` per id (`:81-84`) — no cap on `dim.AccessibleIds`.
- The membership service caps itself: `MembershipResolverService.BuildTransitiveFetchXml:1030-1040` truncates to `MembershipResolveOptions.MaxLimit` (5000) and its comment (`:1022-1028`) documents the ~500-values-per-`in`-condition Dataverse guidance.

Failure scenario: a workforce principal whose composed accessible set (project/matter/WA ids from `CallerPrincipal`) is very large produces a FetchXml `in`-clause that can exceed Dataverse limits → the module fetch fails/500s or returns empty. Fail-closed (availability), not a leak. Realistically bounded by how many roots a single external/workforce caller can accumulate; the inconsistency with the self-capping membership path is real.

---

## A-17 — FetchXML guard rejects other-entity refs but permits same-entity self-joins

**Verdict: CONFIRMED — High (same-entity data exfiltration on the external module read seam).**

Evidence:
- `ExternalModuleDataEndpoints.ExecuteScopedFetchAsync:160-172` rejects the fetch iff `referenced.Count == 0 || referenced.Any(e => e != module.RecordEntity)`. A `<link-entity name='{module.RecordEntity}'>` self-join adds only `module.RecordEntity` to the referenced set, so the guard passes.
- `FetchXmlEntityExtractor.ExtractEntities:94-110` collects all `<link-entity>` names via `Descendants` — a self-join surfaces as `{module.RecordEntity}` (single element), confirming the guard cannot distinguish a self-join from a plain single-entity query.
- Tier-2 scoping filters only PRIMARY rows: `Tier2ScopeFilterInjector.Inject` adds the `in`-filter on the entity's own scope attribute; `ExternalModuleDescriptor.ScopeRows:153-187` keeps a primary row when its own attribute ∈ accessible set. Aliased columns pulled from a self-joined `<link-entity>` are additional attributes on those in-scope primary rows and are never scope-checked. `FetchService.ProjectEntity` serializes `AliasedValue` through to the client (`FetchService.cs:136`).

Failure scenario (exploitable, caller-controlled FetchXml): an external caller submits a fetch for `sprk_document` (an accessible project's docs) with a self-join `<link-entity name='sprk_document' from='statecode' to='statecode' alias='x'>` selecting sensitive columns. The join is broad (any active doc), so each in-scope primary row carries aliased columns sourced from OUT-OF-SCOPE `sprk_document` rows. The primary rows pass Tier-2 scoping; the aliased out-of-scope column values ride out to the client. Real cross-tenant/cross-matter document-field disclosure.

---

## A-18 — Workforce contact-by-email fallback lacks the CIAM no-hijack oid check

**Verdict: CONFIRMED — High (identity binding without a no-hijack guard; acute under the multitenant workforce model).**

Evidence:
- Workforce contact-only branch: `WorkforcePrincipalResolver.cs:148-160` calls `_identity.TryResolveContactByWorkforceIdentityAsync(callerOid, verifiedEmail, ct)`.
- That method (`IdentityNormalizationService.cs:242-281`) tries oid cross-ref first (`:252-262`), then falls back to a raw `emailaddress1` match (`:268-278` → `TryResolveContactIdByEmailAsync:331-372`) that returns the contact id with NO check of whether that contact is already bound to a DIFFERENT oid, and no oid-binding write.
- Contrast the CIAM path `ExternalParticipationService.ResolveExternalContactAsync:211-258`, which enforces the guard: an email match on a contact already bound to a different oid is DENIED (`:247-257`, "no email hijack of a bound Contact").

Failure scenario: under B-4 (Type-2 customer employees authenticate via workforce Entra, multitenant), the `email`/`upn`/`preferred_username` claim (`WorkforcePrincipalResolver.ExtractVerifiedEmail:178-186`) is not guaranteed domain-verified across a foreign/compromised tenant. A caller whose token carries `email = victim@firm.com` (matching a Spaarke contact's `emailaddress1`) resolves to a contact-only principal bound to the victim's contact — inheriting the victim's grants — even when that contact is firmly oid-bound to the real person. The CIAM plane blocks exactly this; the workforce plane does not.

Correction: register cites `IdentityNormalizationService.cs:264-278` — precise (the fallback + its helper). The trust hinge is the cross-tenant verifiability of the email claim.

---

## A-19 — CachedAccessDataSource key omits auth mode → SP-mode snapshot served to OBO caller for 60s

**Verdict: CONFIRMED — Medium (60s cache poisoning that defeats the only caller-scoped check, the AI OBO path).**

Evidence:
- Cache key `sdap:auth:access:{userId}:{resourceId}` — `CachedAccessDataSource.cs:65` — omits `userAccessToken` even though the method takes it (`:55-59`). TTL 60s (`:37`).
- Two callers share this single `IAccessDataSource` (registered as the cached decorator, `SpaarkeCore.cs:59-67`): `AuthorizationService` always passes `userAccessToken: null` → service-principal/app-only mode (`AuthorizationService.cs:48-52`); `AiAuthorizationService` passes the caller bearer → OBO mode (`AiAuthorizationService.cs:176-180`).
- In `DataverseAccessDataSource.GetUserAccessAsync:159-227`, SP mode queries the document app-only (app sees everything → Read granted), OBO mode queries AS the user. Same cache key → an SP-mode "Read" snapshot is served to a subsequent OBO caller.

Failure scenario: `AuthorizationService` (SP mode) evaluates (user, doc) and caches AccessRights=Read because the app can see the doc. Within 60s `AiAuthorizationService` (OBO) checks the same (user, doc) for an AI operation, gets the cache HIT, and grants AI/RAG access based on app-visibility rather than the user's actual Dataverse access — defeating the OBO check that A-2 already noted is the only truly caller-scoped path. Bounded to a 60s window and requires the SP-mode call to populate first.

---

## A-20 — Operations absent from OperationAccessPolicy → unconditional 403; plus a Read-ceiling defeating write policies

**Verdict: CONFIRMED — Medium (fail-closed over-restriction that breaks features; not a confidentiality hole).**

Method: enumerated every operation string reaching `AuthorizationService`/`OperationAccessRule` and cross-checked against `OperationAccessPolicy._operationRequirements` keys (`OperationAccessPolicy.cs:25-149`). `OperationAccessRule.EvaluateAsync:35-46` DENIES ("unknown_operation") any operation not in that dictionary.

Complete list of always-deny sites (operation string absent from the policy):
- `"finance.read"` → `FinanceEndpoints.cs:18`, `:51`, `:65` (list/read invoice endpoints).
- `"finance.confirm"` → `FinanceEndpoints.cs:23`, `:37` (confirm/reject).
- `"entity.associate_document"` → `EntityAccessFilter.cs:64`, attached at `OfficeEndpoints.cs:173` (Office save → association authz).
- `"read"` → `DataverseDocumentsEndpoints.cs:443`, `FileAccessEndpoints.cs:118` (eml-render), `ChatDocumentEndpoints.cs:915` (archive ingest). (This is the A-3 key; it hits three enforcement sites.)

All `ResourceAccessRequirement` operations wired in `AuthorizationModule.cs:182-247` (`driveitem.*`, `container.*`, `preview_file`, `upload_file`, `create_container`) ARE present in the policy — those pass the key check.

Read-ceiling (defeats `canwritefiles` / `canmanagecontainers` and every Write+ policy): `DataverseAccessDataSource.QueryUserPermissionsAsync:305-379` returns at most a single `PermissionRecord(..., AccessRights.Read)` on success (`:368-372`, comment "Grant Read access… Dataverse will enforce Write/Delete separately"); it never returns Write/Create/Delete/Share. So the snapshot's rights ceiling is Read. `canwritefiles` → `upload_file` = Write|Create (`AuthorizationModule.cs:244-245`, `OperationAccessPolicy.cs:141`) and `canmanagecontainers` → `create_container` = Create|Write (`:246-247`, `:147`) — `(Read & required) != required` → unconditional Deny. This also defeats `candownloadfiles`/`cansharefiles` etc. (any policy requiring >Read). Affected write-gated endpoints: `UploadEndpoints.cs:108,159,214`; `DocumentsEndpoints.cs:61,106,149,192,245,304,365,418`.

Failure scenario: every finance endpoint, the Office document-association save, and every upload / container-management endpoint permanently return 403 whenever these authz paths are actually exercised. Fail-closed (no confidentiality risk) but breaks core functionality — the auth model cannot express any right above Read.

Correction: the register's framing ("always-deny operations beyond A-3") is accurate; the Read-ceiling is broader than just `canwritefiles`/`canmanagecontainers` — it neutralises ALL Write+ policies.

---

## A-21 — AI-search security trimming is a pass-through (privilege_group_ids never populated at index time)

**Verdict: CONFIRMED — High (cross-matter RAG retrieval isolation is inert).**

Evidence:
- Record sync writes an empty list: `RecordSyncJob.MapToSearchDocument:557` (`PrivilegeGroupIds = new List<string>()`), alongside empty `Organizations`/`People` with "TODO: expand lookup joins" (`:550-551`).
- The document/knowledge indexer never populates real group ids either: `RagIndexingPipeline.cs:475` only NULLs the field for session-files; customer-corpus docs keep the model default empty list (`KnowledgeDocument.cs:257-260`). Grep finds no code path that assigns real group ids to `PrivilegeGroupIds`.
- The retrieval filter is ALWAYS applied: `RagService.cs:1238-1242` appends `PrivilegeFilterBuilder.BuildFilter(...)`, whose expression includes the public escape clause `not privilege_group_ids/any()` (`PrivilegeFilterBuilder.cs:8-9,63-64`). With every indexed doc carrying an empty `privilege_group_ids`, that clause is TRUE for every document → the filter matches everything → the AIPU2-027 trimming is a no-op.

Failure scenario: cross-matter retrieval isolation (the stated purpose of AIPU2-027) never engages. Any RAG search returns hits from documents/records the caller's groups should exclude, because all content is classified "public" at the index. Mitigated today only by D-4 ("no CIAM route reaches AI search yet") — the moment a scoped surface (or a cross-matter internal user) hits RAG, the control is absent.

---

## A-22 — LookupUserMembershipNodeExecutor passes `["*"]` under a stale "accepts but ignores" comment → now throws

**Verdict: PARTIALLY CONFIRMED — Low (node fails at runtime; the thrown reason is "unknown-entity", not literally "depth exceeded").**

Evidence:
- `LookupUserMembershipNodeExecutor.cs:231-234` sets `IncludeRelated = (config.IncludeRelated ?? false) ? new[] { "*" } : null`, under the comment (`:226-230`) "the resolver accepts-but-ignores in Phase 1A."
- The resolver no longer ignores it: `MembershipResolverService.ResolveAsync:164-185` pre-validates each `IncludeRelated` entry and throws `MembershipDepthExceededException` only for chain syntax (contains `.` or `/`). `"*"` contains neither, so it PASSES pre-validation and flows into `ResolveTransitiveAsync:957-978`, which calls `DiscoverLookupsTargetingAsync(relatedEntity: "*", ...)`; the metadata fetch for entity `"*"` fails → `InvalidOperationException` → rethrown as `MembershipDepthExceededException` with `reasonTag: "unknown-entity"` (`:969-979`).
- The executor's `catch` chain (`:287-332`) has no case for `MembershipDepthExceededException`, so it lands in `catch (Exception)` (`:318`) → `NodeOutput.Error(..., InternalError)`.

Correction: the register says "throws depth-exceeded." It DOES throw `MembershipDepthExceededException`, but the tag is `unknown-entity` (entity `"*"` not resolvable), not a depth violation — the class name matches, the reason does not. Net effect confirmed: any playbook node with `IncludeRelated = true` fails at runtime instead of silently ignoring the flag.

Failure scenario: an AI playbook node configured with `includeRelated: true` errors on every execution (InternalError), breaking that playbook branch. Functionality bug, no security impact; requires the node config flag set.

---

## Ranked severity table (CONFIRMED / PARTIAL findings)

| Rank | # | Verdict | Severity | One-line |
|---|---|---|---|---|
| 1 | A-11 | CONFIRMED | High | Non-idempotent grant + single-row revoke + read GroupBy → access silently survives revocation |
| 2 | A-17 | CONFIRMED | High | Self-join passes the same-entity guard → aliased out-of-scope rows exfiltrated on the module read seam |
| 3 | A-14 | CONFIRMED | High | Anonymous, non-expiring, untracked, unrevocable SPE share links |
| 4 | A-18 | CONFIRMED | High | Workforce contact-by-email binding lacks the CIAM no-hijack oid check (multitenant) |
| 5 | A-21 | CONFIRMED | High | privilege_group_ids never indexed → AI-search trimming is a pass-through |
| 6 | A-12 | CONFIRMED | Medium | Closure cascade $selects stale `_sprk_contactid_value` → 500, revokes nothing; org rows also skipped |
| 7 | A-19 | CONFIRMED | Medium | Auth cache key omits auth mode → SP-mode snapshot served to OBO caller 60s |
| 8 | A-10 | CONFIRMED | Medium | No `<order>` + off-by-one paging; ComposeAsync options:null (500) ignores continuation → truncation |
| 9 | A-13 | CONFIRMED | Medium | SPE revoke matches contact GUID inside UPN → never matches (bounded by broker-only) |
| 10 | A-20 | CONFIRMED | Medium | finance.read/confirm, entity.associate_document, "read" always-deny; Read-ceiling defeats all Write+ policies |
| 11 | A-16 | CONFIRMED | Low | Uncapped `in`-clause in scope injector vs the ~500 self-cap in the membership service |
| 12 | A-15 | CONFIRMED | Low | AccessibleRecordSetAuthorizationFilter orphaned (dead); enforcement runs via CallerPrincipal path |
| 13 | A-22 | PARTIAL | Low | `["*"]` no longer ignored → node throws MembershipDepthExceededException (reason "unknown-entity", not depth) |

All 13 findings substantively hold. A-22 is the only partial (correct mechanism, imprecise exception-reason label). No finding was refuted.
