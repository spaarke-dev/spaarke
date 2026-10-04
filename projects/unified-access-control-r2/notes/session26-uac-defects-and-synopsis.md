# Session 26 — UAC defect verification + six-case synopsis (2026-09-30)

> Captured at CONTEXT CHECKPOINT. Raw workflow outputs in `notes/raw/session26-*`.
> **12 defects CONFIRMED, 0 refuted** (3 adversarial skeptics each: code-trace / reachability / already-tracked). NONE are merge regressions — all predate 2026-09-30.

## Owner decisions needed (fixes blocked on these)
- **C4**: reconcile design-register B-14 with owner rule "No re-share at any level" (likely: /share-user requires Share right; /grant applies caller-rights intersection).
- **C7**: workforce-contact identity binding — bind-on-first-resolve / invite token / accept risk.
- **C9 interim**: drop `owningbusinessunit` from PlatformOwnershipColumns now, or wait for task 036.
- **C10**: named NON-default Secure owner team + re-own documents at provisioning.
- **Pending approval**: file GitHub issues for untracked defects + author a task per defect; re-open mis-marked tasks 037, 039, A-20, A-18 (see process finding).

## Cross-links
- **C4 + C1 compound**: a Write-holder can mint an org/denied-contact grant that takes FULL effect on CIAM (no read-time vetoes there).
- **Fix C3 with or before C2**: C2 presence gates currently mask C3; fixing C2 alone turns cache hits into full denials.
- **Process finding**: tasks 037/039 are ✅ and claim "every principal kind" but excluded CallerPrincipalResolver.cs (→C1). A-20 mis-closed (→C8). A-18 ✅ though notes say "do not mark closed" (→C7). Task 070 POML cites HasProjectAccess as the pattern to copy (would spread C2).

## Confirmed defects (ranked)

### [HIGH] C1 — CIAM plane applies none of the FR-21/22/23 vetoes (Restricted, deny list, Secure org-term suppression)
**Scenario:** Outside counsel C holds an active FullAccess grant on project P. The owner puts C (or C's firm) on the No Access List, or marks P Restricted. With a ciamlogin.com token, C still lists P and streams its documents via GET /api/v1/external/projects/{P}/documents/{d}/content, which the workforce plane would veto.

**Precise claim:** CiamContactPrincipalStrategy builds the whole CallerPrincipal from ExternalParticipationService.GetGrantSetAsync and never calls AccessibleRecordSetService. As a result, on the live /api/v1/external surface:
(a) the Restricted veto (FR-21) and the deny-list veto (FR-23) are missing for every CIAM grant, direct or org-inherited;
(b) Secure suppression (FR-22) is missing for ORG-INHERITED grants, which confer their full all-sources AccessLevel on sprk_issecure roots instead of None.
Narrowing agreed by all three skeptics: a DIRECT grant on a Secure (non-Restricted, non-denied) root legitimately confers its own level on every plane, so that part of the original claim is by design.

**Evidence:**
- CallerPrincipalResolver.cs:378-380 - the CIAM strategy calls only _participations.GetGrantSetAsync. There is no GetRootRecordFlagsAsync, deny-list read or ComposeAsync in :339-405 (spot-checked)
- CallerPrincipalResolver.cs:386-388 (FromLevel(p.ProjectId, p.AccessLevel)) and :417-429 (RightsFromGrants uses grant.AccessLevel) - the all-sources level, never DirectAccessLevel (spot-checked)
- ExternalParticipationService.cs:914-961 - org rows are unioned into AccessLevel (highest wins) with DirectAccessLevel=null. The filters are statecode + expiry only (:61-72, :108-109)
- AccessibleRecordSetService.cs:237-256 (Secure: isSecure ? DirectAccessLevel : AccessLevel) and :357-407 (ApplyVetoPipeline: deny list at :375-378, Restricted at :391-406). They are called only at :1090 and :1301 inside ComposeAsync
- The only consumer of ComposeAsync is WorkforcePrincipalStrategy (CallerPrincipalResolver.cs:519-524). GetRootRecordFlagsAsync is called only at AccessibleRecordSetService.cs:1041/:1257, and GetDeniedRecordsAsync only at :652
- LIVE wiring: Program.cs:69; ExternalAccessModule.cs:177; ExternalAccessEndpoints.cs:54-57 (group + CallerPrincipal filter); EndpointMappingExtensions.cs:439 unconditional; CallerPrincipalResolver.cs:293-313 routes any ciamlogin.com / Ciam:TenantId token to the CIAM strategy
- Handlers gate only on the principal: ExternalProjectDataEndpoints.cs:273, 290, 322 (app-only stream at :346-349), 571, 797, 903; ExternalModuleDataEndpoints.cs:268
- spec.md:84-86 - FR-21 'all contact principals', FR-22 'every principal kind', FR-23 applies to a No-Access contact 'even holding Full Access'
- The code's own false parity claim: CallerPrincipalResolver.cs:48-50 ('Both planes now source these from the ONE evaluator's answer')

**Tracking:** Untracked as a defect.
- No GitHub issue. Skeptic searches of #961-#1015 found only #998, which covers the deny-veto org axis on the workforce plane.
- Tasks 037 and 039 are marked done (TASK-INDEX.md:601, :603) and claim 'every principal kind'. Their file lists exclude CallerPrincipalResolver.cs.
- The only mentions are informal: current-task.md:40 ('CIAM ignores sprk_issecure', Secure part only, no id) and notes/investigation/01-spa-plane.md:18,77,190 (CIAM bypasses ComposeAsync; this predates the vetoes and was never filed).

**Fix direction:** Route the CIAM strategy through the same veto pipeline:
- read the root flags and deny set for the contact and the contact's orgs;
- apply Restricted removal and the deny list;
- use DirectAccessLevel on secure roots, mirroring GrantedRightsFor.
Alternatively, have the CIAM strategy call ComposeAsync with a contact principal. Add CIAM veto tests to CallerPrincipalResolverTests.

### [HIGH] C2 — External read gates test key presence, not rights - None-rights roots still pass reads including document content download
**Scenario:** A workforce contact whose firm holds an org grant on Secure project P composes {P: None}. HasProjectAccess(P) is still true, so GET /api/v1/external/projects/P/documents/{doc}/content streams the secure file app-only. Only writes and the to-do list return 403.

**Precise claim:** The /api/v1/external project read handlers and the module fetch/record Tier-2 scope treat 'id present in the principal's map' as authorization and never check AccessRights.Read. Two live paths insert ids with AccessRights.None.
(1) WORKFORCE plane (systemuser via linked contact, or contact-only): FR-22 Secure suppression of an org-only grant. AccumulateTerm keeps the key, and vetoes remove keys only for deny/Restricted. This is the reachable, high-impact path. It defeats FR-22 for reads, including project document content download.
(2) BOTH planes: matter/work-assignment grant rows with a null sprk_accesslevel, kept deliberately as None keys. This admits the module /fetch and /record reads for that matter/WA and its documents/invoices. It only happens with rows written outside the BFF (the BFF always writes a level), and there is no matter/WA content-download route, so download applies to projects only.
Rights-gated routes deny correctly: to-do list (Read), Create/Write routes. On the CIAM plane the Secure case does not produce None (see C1); there it is a full-level grant instead.

**Evidence:**
- CallerPrincipalResolver.cs:161 - HasProjectAccess => ProjectAccess.Any(p => p.ProjectId == projectId) (spot-checked); :145-152 id sets are Keys views
- CallerPrincipalResolver.cs:527-529, 545-546 - WorkforcePrincipalStrategy copies every evaluator key, None included; no Rights filter
- AccessibleRecordSetService.cs:232-234 ('The id still appears in the map with no rights'), :241-256, :308-317 (AccumulateTerm inserts even with None), :375-406 (vetoes remove only deny/Restricted keys)
- ExternalParticipationService.cs:920-927 (org rows get DirectAccessLevel=null), :884-907 (null-level matter/WA rows kept); ExternalCallerContext.cs:207-213 (null => None)
- ExternalProjectDataEndpoints.cs:273, 290, 322 then :346-363 (app-only stream), 694, 766, 823, 840 - presence-only gates. :256 lists all keys. Contrast :399-401 (to-do list checks Read)
- ExternalModuleRegistry.cs:129-144 IsRecordAccessible and :153-187 ScopeRows are presence-only; the dimensions are wired at ExternalAccessModule.cs:229, 250-252, 265-266, 279, 291
- ExternalUserContextEndpoint.cs:67-76 - /me returns the id to the client with level 'None', so the caller learns the GUID
- ExternalProjectDataEndpoints.cs:31-33 - the header claims read routes verify Read; the code does not
- Tests assert only RightsFor==None, never absence: AccessibleRecordSetServiceTests.cs:700,787; UnifiedEvaluatorSeamTests.cs:300,335

**Tracking:** Untracked.
- No GitHub issue and no task.
- current-task.md:40-42 mentions 'presence-only external read gates' informally, with no id.
- notes/task-037-restricted-secure-vetoes.md:90-92 names the hazard ('would still read as in the accessible set ... most of the read path') but applied key removal only to vetoes.
- notes/task-029-external-todo-parity.md:248-250 has a now-false premise.
- Task 070 POML:56 cites HasProjectAccess as the reference to copy (it would propagate the defect).

**Fix direction:** Make the gates rights-based: HasProjectAccess and AccessibleXIds should require Rights.HasFlag(Read), or prune None-valued keys in WorkforcePrincipalStrategy/RightsFromGrants before building the principal. Add seam tests that assert a read is denied (Contains==false), not just RightsFor==None.

### [HIGH] C9 — SPA/Teams plane grants flat Read|Write|Create on every root owned in the caller's business unit, ignoring role depth
**Scenario:** System user X in BU B has User-depth (or no) matter privileges. Via the Teams/SPA workforce plane, X gets Read|Write|Create on every project/matter/work assignment owned by anyone in B. X can then list them, download project documents, and create to-dos, events and uploads app-only.

**Precise claim:** On the LIVE workforce SystemUser plane, membership discovery admits owningbusinessunit as a structurally access-conferring column. The resolver adds `owningbusinessunit eq <caller's own businessunitid>`, which matches every user- or team-owned root in the caller's BU. ComposeForSystemUserAsync then stamps each hit with the constant MembershipTermRights = Read|Write|Create, which also survives the Restricted veto. The caller's security-role privileges and depth are never consulted.
The corrective seam (ImpersonatedRootSetSource, task 035) is registered but INERT; the swap is task 036, which is open.
How much broader than Dataverse:
- Write/Create: broader for everyone the rule catches.
- Read: broader only where the caller's read depth is below BU or absent. In current dev, Basic User holds Deep Read on sprk_project, so the Read over-grant is partly masked there.
Secure records owned by the Secure Record BU do not match unless the caller sits in that BU (see C10).
Anchor correction: MembershipResolverService is at Services/Ai/Membership/, not Infrastructure/ExternalAccess/.

**Evidence:**
- Services/Ai/Membership/MembershipResolverService.cs:595-596 - PlatformOwnershipColumns = {ownerid, owningteam, owningbusinessunit}; :688-692 admitted without a registry entry; :255-256 includePlatformOwnership: true on the systemuser plane
- MembershipResolverService.cs:911-916 - case BusinessUnit: AppendCondition(field, identity.BusinessUnitId); IdentityNormalizationService.cs:170 fills this from the systemuser row
- AccessibleRecordSetService.cs:288-289 - MembershipTermRights = Read|Write|Create; :997-1002 membership walk; :1048-1051 stamped on every id; :1044-1047 survives Restricted
- ExternalAccessModule.cs:195-198 - IImpersonatedRootSetSource 'NOT consumed by AccessibleRecordSetService yet' (registered, inert). ImpersonatedRootSetSource.cs:30-35 admits the BU over-grant
- CallerPrincipalResolver.cs:519-546 feeds /api/v1/external. AuthorizationModule.cs:351-359 accepts workforce tokens with authentication only. Gates at ExternalProjectDataEndpoints.cs:400, 434, 571, 798, 903. ExternalDataService.cs:13 is app-only (no Dataverse backstop)
- MembershipResolverServiceTests.cs:1171-1213 pins BU-owned records as conferring
- spec.md:83 - FR-20 acceptance names this exact over-grant

**Tracking:** Partially tracked. The set-level BU over-grant is spec FR-20 (spec.md:83) and task 036 (OPEN, TASK-INDEX.md:600, POML:95 'over-grant fixed' when the flag is ON). Task 036 is gated behind task 034 (BLOCKED). ADR-034 A1.1 (.claude/adr/ADR-034-user-record-membership.md:73) deliberately made owningbusinessunit conferring. NOT tracked: the flat Read|Write|Create stamp regardless of Write/Create privilege. Task 036 keeps that rights level (POML:23), and tasks 032/033 record it as a deliberate carry-over. No GitHub issue.

**Fix direction:** Interim: drop owningbusinessunit from PlatformOwnershipColumns (investigation 08 'Option A'). Proper fix: land task 036's impersonated read. Also derive per-record rights from the caller's actual privileges (RetrievePrincipalAccess or an impersonated read), not the Read|Write|Create constant.

### [HIGH] C4 — Grant/share endpoints gate only on Write: Collaborate sharees can re-share, and /grant mints levels above the caller's own, including org-wide grants
**Scenario:** Colleague B received Collaborate on a secure project (no ShareAccess). B uses '+ User' (/share-user) to share colleague C at Collaborate, and calls POST /grant to give an external contact FullAccess (including Delete, which B lacks). This defeats the owner's 'No re-share at any level' rule.

**Precise claim:** The /api/v1/external-access group (/grant, /invite-and-grant, /share-user and siblings) is gated by DelegationRuleFilter, whose only rights test is caller-OBO Write. AccessRights.Share is never consulted.
A Collaborate POA sharee (Read|Write|Append|AppendTo, deliberately without ShareAccess) therefore can:
(1) re-share internally via /share-user. The level is capped at the caller's own rights by RecordShareLevels.Intersect, but the re-share is not prevented.
(2) mint external grants on /grant and /invite-and-grant at ANY level, including FullAccess, because there is no caller-rights intersection.
(3) mint organization-wide grants (empty ContactId + OrganizationId).
There is no sprk_issecure or deny-list check at WRITE time, but read-time enforcement neutralizes two consequences:
- org grants on Secure records confer nothing on the workforce plane (though they DO on the CIAM plane, see C1);
- grants to deny-listed subjects are vetoed at read on the workforce plane (again not on CIAM, see C1).
'Write => may grant external access' is owner decision B-14. The defect is its unreconciled conflict with the later 'No re-share at any level' and 'grant only what you hold' owner decisions, and with UAT item U-3.

**Evidence:**
- DelegationRuleFilter.cs:86 `private const AccessRights RequiredRight = AccessRights.Write;` (spot-checked); :170 is the only rights test
- ExternalAccessEndpoints.cs:123-126 - group = RequireAuthorization() + AddDelegationRuleFilter(); :129 /grant, :148 share routes, :155 /invite-and-grant; mapped unconditionally (EndpointMappingExtensions.cs:439)
- DelegationRuleCharacterizationTests.cs:43-51 - pins that ShareAccess is irrelevant and Read+Write delegates
- RecordShareLevels.cs:52 - CollaborateMask includes Write; :20-22 'No re-share at any level ... cannot pass it on'
- ProvisionProjectEndpoint.cs:172-181 - colleagues get Collaborate without ShareAccess 'so the access list cannot widen through a chain nobody reviewed'
- InternalShareEndpoints.cs:290-303 - an intersection only, with no Share requirement; :344-346 app-only GrantAccess (the platform never checks the sharer's Share right)
- GrantExternalAccessEndpoint.cs:69-150 - no caller-rights cap (AccessLevel only Enum.IsDefined at :95); :84-87, :679-682 org grant. ExternalCallerContext.cs:211 FullAccess includes Delete
- RecordAccessGateEndpoint.cs:19-24 + TrackingFieldTrio/index.ts:609 - the UI exposes Manage Access to any Write holder
- Mitigations (workforce only): AccessibleRecordSetService.cs:237-256, 1279-1290 (Secure suppresses org terms), :363-378 (deny veto)

**Tracking:** Untracked as a defect. B-14 is at notes/design-register.md:84. The conflicting decision 'No re-share' is at notes/task-063-internal-user-share-endpoints.md:94-105. 'Grant only what you hold' was applied to /share-user only (:340-350). UAT U-3 (notes/phase4-uat-acceptance.md:24) expects 'cannot re-share' and has not been run. investigation/06-adversarial-critique.md:107 left least-privilege on grant writes unanalyzed. No GitHub issue. Nearest are #994 and #1010, which are unrelated.

**Fix direction:** Owner decision needed (§6.5): reconcile B-14 with 'No re-share'. Likely: require the Share right (or ownership) for /share-user. Apply the caller-rights intersection to /grant and /invite-and-grant. Optionally refuse org-grant and deny-listed/Restricted targets at write time.

### [HIGH] C8 — Finance recalculate is an auth-only IDOR (app-only read+write of any matter/project); finance summary always 403s
**Scenario:** An authenticated user walled off from matter M POSTs /api/finance/matters/{M}/recalculate. The BFF reads M's invoices and budgets app-only, PATCHes nine rollup fields, and returns M's spend, budget, utilization and a 12-month timeline with a 200.

**Precise claim:** Recalculate half:
- POST /api/finance/{matters|projects}/{id}/recalculate is on its own MapGroup with only rate limiting and a bare RequireAuthorization(). No DefaultPolicy/FallbackPolicy exists, so that means 'authenticated'. No per-record check is made.
- FinanceRollupService reads sprk_invoice/sprk_budget through the app-identity ServiceClient, PATCHes nine derived fields onto the parent app-only, and returns the aggregates. This is cross-matter financial disclosure, plus a forced overwrite with server-derived values (not arbitrary tampering).
- Plausible, unverified: an unknown GUID may upsert-create a record via PATCH without If-Match.
Summary half:
- GET /api/finance/matters/{matterId}/summary runs FinanceAuthorizationFilter('finance.read') with the matter id as ResourceId.
- That id reaches the document-only DataverseAccessDataSource (RetrievePrincipalAccess and a fallback probe both hard-coded to sprk_documents), so the result is None and every honest caller gets 403. This half fails closed: availability, not disclosure.

**Evidence:**
- FinanceRollupEndpoints.cs:28-31, 47-50 - groups carry only RequireRateLimiting + RequireAuthorization() (spot-checked); :65-140 handler has no record check
- EndpointMappingExtensions.cs:361-362 - both mappers unconditional; FinanceModule.cs:210 registration unconditional
- AuthorizationModule.cs:231 - named policies only (no DefaultPolicy/FallbackPolicy); ContainerItemEndpoints.cs:37-38 corroborates
- FinanceRollupService.cs:130-136, 236, 256 (app-only reads); :193 app-only PATCH via DataverseWebApiService.cs:1387-1410; :200-211 aggregates returned
- sprk_subgrid_parent_rollup.js:452-467 - live client acquires a delegated BFF token by MSAL SSO
- FinanceRollupEndpointsContractTests.cs:53-76 - only unauthenticated 401 is tested
- FinanceEndpoints.cs:18, 64-65; FinanceAuthorizationFilter.cs:122-123 (matterId becomes ResourceId), :77-93
- AuthorizationService.cs:76-80, 225 -> DataverseAccessDataSource.cs:373, 662-663 (sprk_documents), :793, :813-829, :947-949 -> None; OperationAccessPolicy.cs:167 + OperationAccessRule.cs:49-78 -> 403
- RouteAuthorizationGuardTests.cs:99-101 - Api/Finance/* is NotGoverned by omission

**Tracking:** Recalculate IDOR: untracked. code-quality-and-assurance-r3 task 023 only removed AllowAnonymous (notes/task-023-notes.md:10). No issue. Summary always-403: mis-closed. A-20 was attributed only to the missing finance.read key, and task 003 marked the filter fixed (notes/task-003-operation-rights-decisions.md:88). The generic document-only data source hazard is noted at notes/task-005-rights-mapping.md:215, routed to task 032 (done), and never names FinanceAuthorizationFilter.

**Fix direction:** Recalculate: add a record-scoped Write (or Read) check as the caller, e.g. GetCallerRecordAccessAsync(sprk_matters/sprk_projects, id) or an OBO probe, before recomputing. Return 404 for unknown ids. Summary: move FinanceAuthorizationFilter to the entity-generic GetCallerRecordAccessAsync (AuthorizationService.cs:255-283) with the correct entity set. Add Api/Finance to RouteAuthorizationGuardTests.

### [MEDIUM] C7 — Workforce non-systemuser identity rests on an unverified email claim with no oid binding (conditional); in the connected env the path fails closed for everyone
**Scenario:** Where contact.azureactivedirectoryobjectid exists but is empty (it is never written), a home-tenant non-systemuser whose email/preferred_username/upn equals a granted contact's emailaddress1 inherits that contact's grants. In the connected dev env, which has no such column, every contact-only workforce caller is denied instead.

**Precise claim:** The mechanism is LIVE:
- ExtractVerifiedEmail verifies nothing and returns email ?? preferred_username ?? upn.
- The contact-only branch matches it to contact.emailaddress1 (TopCount=2) after an oid lookup on contact.azureactivedirectoryobjectid.
- Nothing in src writes that contact column (the only write is systemuser, RegistrationDataverseService.cs:372). So the no-hijack guard can only deny a contact bound to a DIFFERENT oid, which never exists, and is inert.
- Duplicate emails produce an ambiguity deny.
The impact depends on environment. One skeptic refuted on reach, and one trace skeptic concurred:
- In the connected environment (spaarkedev1), contact has NO azureactivedirectoryobjectid attribute. The oid query and the email query (which selects that column) both throw and are caught as null, so every non-systemuser workforce caller is denied (403 principal_not_resolved). That is an availability defect: Type-2 contact-only users can never resolve. The GuardInertNoBindingColumn warning cannot fire in this case, and its comment is wrong.
- The email-only hijack is real only where the column exists but is empty. Because it has no publisher prefix it is likely a platform column absent from this org; not verified elsewhere.
- Attackers are limited to home-tenant identities while AzureAd:TenantId is single-tenant (E-6 multitenant is open).

**Evidence:**
- WorkforcePrincipalResolver.cs:220-228 - claim chain, no verification (no xms_edov check in src); :160-166 passes it to the contact branch; :204-212 deny
- IdentityNormalizationService.cs:252-262 oid lookup; :596-605 email query (TopCount=2, ColumnSet includes azureactivedirectoryobjectid); :496-499 ambiguity deny; :525-530 unbound contact resolves; :478-480 'Nothing here writes a binding'
- IdentityNormalizationService.cs:568-576, :646-653 catch -> null; :273-281 contact_binding_unreadable -> null (the fail-closed path when the column is absent)
- Live metadata (reach skeptic): 'Contact entity doesn't contain attribute with Name = azureactivedirectoryobjectid'; corroborated by projects/spaarke-daily-update-service-r5/notes/lessons-learned.md:37-39
- LIVE wiring: ExternalAccessModule.cs:132,178; CallerPrincipalResolver.cs:293-313, 483-487; AuthorizationModule.cs:351-359

**Tracking:** Partially tracked, with no work item. A-18 / FR-12 / task 013 is marked done (TASK-INDEX.md:106), but notes/wave2-parallel-merge-plan.md:1077-1162 says 'Do not mark A-18 closed', and IIdentityNormalizationService.cs:50-75 and WorkforceEmailNoHijackTests.cs:795-814 record the residual. No GitHub issue, no ISS entry, no owner decision. The absent-column fail-closed behaviour is not tracked anywhere.

**Fix direction:** Owner decision: bind-on-first-resolve to a Spaarke-prefixed contact column (or reuse sprk_externalobjectid), an invite-token binding, or accept the risk. Also make the email query not select a column that may not exist, so contact-only users can resolve. Fix the misleading inert-guard warning.

### [MEDIUM] C10 — Secure isolation relies on the Secure BU's DEFAULT owner team staying memberless (unenforced); secure projects' documents are never moved to the Secure BU
**Scenario:** An admin moves user U into the 'Secure Record' BU, perhaps during a Fix-A relocation. Dataverse auto-adds U to the default owner team that owns every secure project, and U silently reads all of them in MDA. Separately, and with no misconfiguration, documents on secure projects stay owned in ordinary BUs.

**Precise claim:** Half 1 (DEFAULT owner team):
- Provisioning assigns each secure sprk_project to the Secure Record BU's DEFAULT owner team (isdefault eq true) and never checks membership.
- Dataverse maintains default-team membership automatically from each user's businessunitid, so placing any user in that BU grants Secure Record Owner read on every secure project/matter/WA the team owns, with no share, role grant or error.
- The only automated detector is NFR-05 clause 2. Its live half is opt-in (env var) and never runs in CI; the runbook also has a manual 'MUST be zero' check.
- Triggering it needs an admin action (Change BU, or an operator-configured BusinessUnitName in registration), not an ordinary user.
Half 2 (documents):
- Only the sprk_project row is re-owned. sprk_document (and the other child entities) keep creator/app-user ownership in ordinary BUs, so they are not isolated. This half needs no misconfiguration.
- RecordOwnershipResolver, which would place target-filed documents on the default team, is registered but INERT (no callers).

**Evidence:**
- ProvisionProjectEndpoint.cs:373-379 - `_businessunitid_value eq {secureBuId} and isdefault eq true and teamtype eq 0` (spot-checked :375); :392-408 checks only team count; :609-616 re-owns only sprk_projects; :669-674 asserts 'has no members'
- SecureBuRoleDepthAssertion.cs:191-193 and SecureBuRoleDepthAssertionTests.cs:515-516 - default team 'holds every user in that BU automatically'; notes/task-062-nfr05-role-depth.md:243-249 (168 members observed on the root default team)
- SecureBuRoleDepthAssertion.cs:243-258 clause 2; SecureBuRoleDepthAssertionTests.cs:426-452 opt-in, returns 'NOT RUN'; no .github workflow references NFR05/SPAARKE_CANARY; tests/integration/auth/README.md:138
- RegistrationDataverseService.cs:365-376 + RegistrationEndpoints.cs:319 - an operator-configured BU with no guard against the Secure BU
- design.md:581-589 (§5.1d) 'Children are not assigned ... not isolated at all'; OfficeService.cs:866-878 passes no owningTeamId -> DataverseServiceClientImpl.cs:303-306
- RecordOwnershipResolver.cs:144 registered at MetadataServiceExtensions.cs:103, with zero callers (inert)
- docs/guides/SECURE-PROJECT-ENVIRONMENT-SETUP.md:386 warns only against adding a human to the team, which is impossible manually on a default team, and never against BU placement

**Tracking:** Half 1: GitHub #967 (OPEN; the owner downgraded it to low on 2026-09-09 as 'procedural'). CI credential gap: task 034/062 open decision (current-task.md:1374). Half 2: design.md §5.1d, SECURE-DOCUMENTS-BUILD-PLAN.md:145 ('separate task, post-MVP'), and DATAVERSE-WRITE-PATH-ARCHITECTURE.md:124 (I-2 'Not implemented ... fail OPEN ... Unowned'). No issue or task.

**Fix direction:** Use a named, non-default owner team in the Secure BU, and add a runtime/provisioning check that it has zero members. Wire the live NFR-05 clause 2 into a credentialed pipeline. Take ownership of invariant I-2: re-own child records (documents first) to the secure team at provision/create time, which requires prvRead/Assign on sprk_document for that role.

### [MEDIUM] C5 — Soft revocation: contact/org/root deactivation and ended memberships do not revoke; revoke-time cache invalidation keyed on the admin's workforce tid misses the CIAM entry
**Scenario:** Firm X is disengaged. Ops deactivates the sprk_organization, contact Jane and the matter. Jane's CIAM token still resolves the inactive contact and its active grant rows (direct and org-inherited), so she keeps reading matter documents until each grant's expiry date, 90 days by default.

**Precise claim:** The grant read path filters only on the grant row's statecode + sprk_expiresdate and the junction row's statecode. It never checks:
- the contact's statecode (the CIAM oid/email lookup also has no statecode filter);
- sprk_organization.statecode;
- the membership sprk_enddate;
- the root record's statecode.
So deactivating a contact, org or matter/project/WA, or passing a membership end date, does not revoke. There are no plugins; /close-project is an explicit project-only cascade.
Still revoking: deactivating the grant row, grant expiry (default +90 days, unbounded if set explicitly), deactivating the junction row, and the deny list (workforce plane only, see C1).
The reconciliation job is registered enabled:false with writes report-only by default. Even enabled, it covers only R1 undated grants, R2 inactive org and R3 ended membership, never contact or root deactivation.
Revoke (and grant, close-project, set-expiry) invalidate the grant cache under the ADMIN's workforce tid, but CIAM entries are keyed on the CIAM tid, so a revoked CIAM user keeps cached grants for up to the 60 s TTL. Org-grant revokes do no invalidation at all.

**Evidence:**
- ExternalParticipationService.cs:61-62 (contact grant filter: grant statecode + expiry), :68-72 (org grant filter), :1101-1103 (junction statecode only, no sprk_enddate / org statecode), :375 and :423 (contact lookup, no statecode), :572-574 (root flags select no statecode)
- ExternalDataService.cs:181-201 - selects statecode, does not filter on it
- ExternalAccessModule.cs:360-372 `enabled: false` ('IS THE SHIPPING STATE'); ExternalAccessReconciliationJob.cs:29-38 (R1-R3 only), :109, :208-209 (WritesEnabled defaults false; key absent per DEPLOY-CHECKLIST.md:212-213)
- RevokeExternalAccessEndpoint.cs:197, 208-210, 315-317 - RemoveAsync(admin tid); :198-205 org-grant revoke skips invalidation; admin group on the default workforce scheme (ExternalAccessEndpoints.cs:123-126; AuthorizationModule.cs:46-49)
- ExternalParticipationService.cs:193-202, 223, 290-296 - CIAM cache keyed on the CIAM request tid; TenantCache.cs:257-258 raw tid in key; CallerPrincipalResolver.cs:302-309 tid == Ciam:TenantId marks the CIAM plane; ExternalParticipationService.cs:18 60 s TTL
- Same invalidation pattern: GrantExternalAccessEndpoint.cs:500-513, ProjectClosureEndpoint.cs:484-506, SetRecordShareExpiryEndpoint.cs:343-344
- RevokeExternalAccessEndpoint.cs:477-483 and ExternalAccessReconciliationJob.cs:23-27 admit the membership end date and org gaps

**Tracking:** Partial.
- Org deactivation: #1006 (ISS-026, OPEN). Read guard in task 109 (pending); writer in task 117 R2 (shipped disabled).
- Membership end date: #999 (ISS-020, OPEN), tasks 109/110 (open), task 117 R3 (disabled).
- Job disabled/report-only: a deliberate owner-gated state, with no issue tracking when it gets enabled.
- NOT tracked: contact deactivation, root-record deactivation, and the admin-tid cache-invalidation miss. notes/investigation/04-grant-lifecycle.md:116 wrongly claims 0 s revoke latency.

**Fix direction:** Add read-side guards on the grant path: active contact, active org, junction sprk_enddate/startdate, and active root (join or flag read). Invalidate the grant cache under the CIAM tenant id (Ciam:TenantId), or key the grant cache tenant-agnostically by contact id. Invalidate on org-grant revoke by fanning out to members. Decide the enable and write posture of the reconciliation job.

### [MEDIUM] C6 — External module /fetch has no column allow-list: CIAM callers can project SPE pointers and any internal/FLS column on in-scope rows
**Scenario:** A CIAM contact granted on project P POSTs /api/v1/external/api/dataverse/fetch for sprk_document with attributes sprk_project, sprk_graphdriveid, sprk_graphitemid and sprk_filepath (or all-attributes). The response returns P's SPE drive id, item id and webUrl for every document.

**Precise claim:** POST /api/v1/external/api/dataverse/fetch (the claim says GET; it is a POST) is guarded only on entity identity and no link-entity. It never inspects attribute/all-attributes, and the Tier-2 injector and ScopeRows filter rows, not columns. FetchService executes app-only and serializes every returned attribute.
So any CIAM or workforce caller can read sprk_graphdriveid, sprk_graphitemid and sprk_filepath (the Graph webUrl), plus any other internal or FLS-secured column (app-only bypasses field security), on sprk_document/sprk_invoice/root rows already in scope. The caller must project the scope column, which is trivial.
Narrowing for GET /record: $select passes through unchecked, but child modules such as sprk_document always fail the Tier-2 gate. So the three named document columns leak via /fetch ONLY; /record exposes arbitrary columns of granted ROOT records (e.g. sprk_project.sprk_containerid).
Impact: identifier and internal-field disclosure within already-granted scope, with no cross-scope rows. It contradicts the file's own invariant 'no Graph pointer ever reaches the client'.

**Evidence:**
- ExternalModuleDataEndpoints.cs:84, 169-173, 502-548 - the guard checks entity names (:526-530) and link-entity (:545-547) only; :26-27 claims 'no Graph pointer ever reaches the client'
- Tier2ScopeFilterInjector.cs:111-146 - appends a filter only; ExternalModuleRegistry.cs:153-187 ScopeRows keeps whole rows (:178 needs the scope attribute projected)
- FetchService.cs:95-96, 145-150 - app-only execution; every attribute copied to the response
- ExternalAccessModule.cs:244-254 - sprk_document is a live module; ExternalDataService.cs:252-260 shows the pointer columns on sprk_document; the curated read at :211 deliberately omits them
- ExternalModuleDataEndpoints.cs:246, 278-281, 632-645 + RecordService.cs:68-74 - /record $select passes through; ExternalModuleRegistry.cs:125-144 child single-record reads fail closed
- Unconditional wiring: EndpointMappingExtensions.cs:439; ExternalAccessEndpoints.cs:54-57, 100; AuthorizationModule.cs:351-359

**Tracking:** Known but not tracked. Investigation gap G-10 (Medium) at notes/investigation/05-three-plane-completeness.md:36, :127 was never promoted to the design register, spec, a task or an issue. It sits only under the generic 'field-level visibility out of scope' deferral (spec.md:27; design-register.md:114 D-3), whose premise ('SPA won't ask') fails when the caller writes the FetchXML or $select. Tasks 011, 022 and 070 cover other surfaces.

**Fix direction:** Add a per-module column allow-list to ExternalModuleDefinition. Rewrite or reject FetchXML with all-attributes or non-allow-listed attributes, and filter /record $select, before execution. Strip non-allow-listed keys from results as defence in depth.

### [MEDIUM] C12 — Access caches store fault results (empty grant set / None snapshot / blank identity) and have no BU/team/owner invalidation
**Scenario:** A 429 on the grant query makes QueryGrantSetAsync return Empty, which is cached for 60 s, so a CIAM partner sees zero projects for a minute and retries hit the cache. A teammembership read fault caches an identity with no teams for 10 min, plus 5 min of membership cache, hiding all team-owned matters.

**Precise claim:** Three LIVE caches store results produced by a failed read:
(a) ExternalParticipationService caches ExternalGrantSet.Empty (non-2xx or any exception, including client-abort cancellation) and partial sets missing org grants (org-grant or junction sub-read faults) for 60 s.
(b) IdentityNormalizationService caches a merged PersonIdentity with null BU/contact or empty TeamIds after lookup faults for 10 min. MembershipResolverService builds on it and caches its response, including empty ones, for another 5 min.
(c) CachedAccessDataSource caches the inner AccessRights.None / Denied('exception') snapshots for 60 s. It skips the cache only for a missing token or empty id.
Nothing invalidates the identity cache. The membership invalidator only handles lookup-junction writes and is off by default. The grant cache is invalidated only on grant-write paths. With no Dataverse plugins, BU, team and owner changes are bounded only by the TTLs: roughly 10-15 min identity+membership, 60 s grants and snapshots.
Corrections to the claim:
- No access cache has a 15-min TTL; the '10-15' figure is the 10+5 stacking.
- The CachedAccessDataSource roles/teams 2-min keys are write-only (dead).
- ImpersonatedRootSetSource and MembershipEndpoints do NOT cache faults.
Impact: fault caching fails closed (an availability issue). The missing invalidation is the over-grant direction after team/owner removal.
Plausible, not traced end to end: a cached null contact plus a failed email fallback yields an empty deny-veto subject (AccessibleRecordSetService.cs:608-612), skipping the ethical wall for membership-term access for up to 10 min.

**Evidence:**
- ExternalParticipationService.cs:18 (60 s), :857-862 and :989-993 return ExternalGrantSet.Empty (spot-checked :861, :992), :218-224 caches the result with no fault check (:223), :1044-1059 and :1112-1131 partial-set faults
- IdentityNormalizationService.cs:55 (10 min), :174 caches the merged identity, :230-238 systemuser fault -> Empty, :699-707 team fault -> empty; :47-49 invalidation 'future Phase 2' only
- MembershipResolverService.cs:112 (5 min), :263, :289/:325/:368 cache empty responses; :579-592 admits a team fault is 'indistinguishable from no access'
- CachedAccessDataSource.cs:59, :131-134, :158-161, :208-210; DataverseAccessDataSource.cs:351-369, 465-474; registered at SpaarkeCore.cs:106-114
- MembershipCacheInvalidationSubscriber.cs:275 + MembershipModule.cs:193-215 (flagged off, junction-only)
- Counter-examples: ImpersonatedRootSetSource.cs:158-190 (no catch); MembershipEndpoints.cs:453-463 (positive-only caching); CachedAccessDataSource.cs:253, 280 (write-only keys)

**Tracking:** Partial, in notes only. CachedAccessDataSource negative caching: investigation 03 F5 (03-native-uac-core.md:207) and task 070 W-7 (task-070-gate-semantic-search.md:159, 'not fixed here'). Identity/membership staleness with the invalidator off: investigation 02 (02-membership-spine.md:26-27, 188-193) says it must be an 'explicitly accepted risk', and none is recorded. Task 043 §4.2 declined per-member invalidation. Grant-set and identity fault caching are untracked. No GitHub issue; #998, #1010 and #1001 are adjacent but different.

**Fix direction:** Stop caching fault-derived results. Have QueryGrantSetAsync and the identity lookups signal a fault (a Faulted flag or exception) and skip the cache write, or use a very short negative TTL. Rethrow OperationCanceledException. Add an invalidation path, or shorter TTLs, for identity/team changes, and document the accepted staleness.

### [MEDIUM] C3 — Grant cache drops DirectAccessLevel: on a cache hit a direct grant on a Secure root reads as None (intermittent under-grant, workforce plane)
**Scenario:** A contact-only Teams user with a direct Collaborate grant on secure matter M can create to-dos on a cache miss. Within 60 s, on a cache hit, POST/PATCH/GET to-dos on M return 403 insufficient_rights, while presence-gated reads still pass.

**Precise claim:** CacheGrantSetAsync persists only ProjectId/RecordId + AccessLevel. CachedParticipation and CachedRootGrant have no DirectAccessLevel field, so every cache HIT yields DirectAccessLevel=null. CacheVersion was left at 4 when task 037 added the field, contrary to the rule at :1281.
On the WORKFORCE plane, GrantedRightsFor uses DirectAccessLevel for sprk_issecure roots, so a legitimate DIRECT grant on a secure project/matter/WA becomes AccessRights.None on a hit (correct on a miss). The key stays in the map (AccumulateTerm), so presence-gated reads still pass (C2 mechanism), while rights-gated routes return 403:
- to-do list/create/PATCH
- document upload
- event create
This can flip within one request, because the project/matter/WA compositions run sequentially after a fire-and-forget fill.
Narrowings:
- It fails closed (no over-grant).
- The CIAM plane is unaffected (it reads only AccessLevel).
- It is masked for systemusers who also hold ADR-034 membership on the record.
- The cache is live whenever a tid claim is present; Redis is mandatory in deployed environments.

**Evidence:**
- ExternalParticipationService.cs:1141-1154 - the cache write projects AccessLevel only (spot-checked :1144, :1149, :1152)
- ExternalParticipationService.cs:1242-1268 - CachedParticipation/CachedRootGrant lack DirectAccessLevel; ToParticipation()/ToGrant() leave it null (spot-checked)
- ExternalParticipationService.cs:877, 896, 905 (miss path sets it); :201-208 (hit returns cached.ToGrantSet()); :39 CacheVersion = 4; :1273-1284 documents the identical bug class fixed for AccessLevel in task 032
- AccessibleRecordSetService.cs:244, 250, 256 (isSecure ? DirectAccessLevel : AccessLevel); :308-317 key kept with None; called at :1061, :1264
- CallerPrincipalResolver.cs:519-546 (three compositions per request); :179-193 rights getters vs :161 presence
- ExternalProjectDataEndpoints.cs:399-401, 433-440, 570-582, 797-804, 897-909 (403 on hit)
- git: introduced by 392fc251b1 (task 037), which is already on origin/master; UnifiedEvaluatorSeamTests.cs:892 overrides GetGrantSetAsync, so no test covers the cache round-trip

**Tracking:** Untracked. No task, no ISS entry, no GitHub issue (DirectAccessLevel/CachedGrantSet/CacheVersion searches came up empty). Task 037 notes only mention flags being read live (task-037-restricted-secure-vetoes.md:137). Task 032 notes :82-98 document the same bug class for AccessLevel.

**Fix direction:** Add DirectAccessLevel (int?) to CachedParticipation and CachedRootGrant, write and restore it, and bump CacheVersion 4->5. Add a cache round-trip test through a real ITenantCache (in-memory IDistributedCache) that asserts a secure direct grant's rights are equal on miss and hit.

### [MEDIUM] C11 — Secure provisioning: creator-share failure after the owner move locks the creator out, and the claimed retry is impossible
**Scenario:** A creator provisions a Secure project. Step 5 moves ownership to the memberless Secure team, then the app-only GrantAccess hits a transient 429/5xx, giving 500 creator_share_failed ('retry once the share path is healthy'). The creator's retry is refused 403 by the delegation filter (no Write left), and any Write-holder's retry gets 409 already_provisioned.

**Precise claim:** ProvisionProjectAsync commits step 5 (re-own to the Secure Record default owner team, verified by read-back) before step 5.5 (WhoAmI + the creator's POA share). If step 5.5 fails, the endpoint returns 500 creator_share_failed or 403 creator_unresolved, with no ownership rollback (deliberately).
The server comments (:512-516) and error detail (:742-743) wrongly say a retry resolves it:
- DelegationRuleFilter requires caller-OBO Write, which the creator no longer has, so the creator gets 403 delegation_write_required.
- A caller who passes the filter hits Step 4's ownership-based idempotency marker: 409 already_provisioned.
- Even without that, step 5.5 shares to the CURRENT caller (WhoAmI), not the original creator.
The wizard does not retry. Recovery is manual by an administrator: share in Dataverse, or reassign the owner and re-run. So 'no retry path' is exact for self-service and automatic recovery only.
On today's root-BU dev topology, Deep Read may still let the creator read the project.
Sibling trigger: if the read-back throws after the PATCH committed, the response says 'Nothing has been provisioned' although ownership moved (:628-656, :500-503).
This fails closed: availability/integrity, no disclosure.

**Evidence:**
- ProvisionProjectEndpoint.cs:485-508 (step 5 before share), :517-521, :707-722 (creator_unresolved), :724-745 (creator_share_failed; 'retry' detail at :742-743), :29 and :785-788 (no rollback)
- ProvisionProjectEndpoint.cs:445-472 (409 already_provisioned via IsOwnedBy, :1017-1018); :705-707, :729 (share target = current caller)
- ExternalAccessEndpoints.cs:123-126, 172; DelegationRuleFilter.cs:86, 170-179, 240-241 (Write on the project as caller)
- ProvisionProjectEndpoint.cs:512-516 - the false 'a retry resolves cleanly' comment
- provisioningService.ts:188-191, 206-215 and notes/task-068-secure-step-copy.md:161-163 - the client acknowledges the locked-out state; copy-only mitigation
- CreateProjectWizard.tsx:690-713 - one-shot call, no retry; :708-709 comment 'a refusal means nothing was moved' is false here
- SecureProjectShareTests.cs:144-157 - no retry-after-failure test

**Tracking:** Partial. Task 068 found it and mitigated it with client copy only (sends the user to an administrator). No server fix, task, ISS entry or GitHub issue (#967 is a different defect). Task 061 closed while claiming share failure 'orphans nothing' (061 POML:107-109).

**Fix direction:** Either:
- resolve the creator (WhoAmI) and pre-validate before step 5, and compensate on share failure by reassigning the owner back to the creator; or
- make the endpoint resumable, so that when owned by the team but the creator lacks a share it issues the share to a persisted creator id instead of returning 409, allowing an admin or app retry.
Fix the misleading comments and error detail.

## Consolidator notes

All 12 claims survive. Only C7 drew a refutation (the reach skeptic), 1 of 3. The main code anchors were spot-checked in the working tree and match the quoted lines:
- CallerPrincipalResolver.cs:161, 378-429
- ExternalParticipationService.cs:861, 992, 223, 1141-1154, 1242-1268
- DelegationRuleFilter.cs:86
- FinanceRollupEndpoints.cs:28-52
- ProvisionProjectEndpoint.cs:375

No two claims are the same defect, so nothing was merged. Related pairs, kept separate on purpose:
- C1 vs C2: on the CIAM plane a Secure org grant yields the FULL level (C1), not a None key (C2). C2's Secure path is workforce-only. Its null-level matter/WA path applies to both planes.
- C3 vs C2: in C3's cache-hit under-grant, reads still pass only because of C2's presence gates. Fixing C2 turns C3 into a full denial on hits, so fix C3 with or before C2.
- C12 skeptics re-found the C3 DirectAccessLevel cache drop and the C5 admin-tid invalidation miss as "adjacent" defects. Both are attributed to C3 and C5, not C12.
- C4's read-time mitigations (Secure org suppression, deny veto) do not exist on the CIAM plane (C1). C4 plus C1 together means a Write holder can mint an org or denied-contact grant that DOES take effect for CIAM callers.

Why C7 is medium, not high: the connected Dataverse org has no contact.azureactivedirectoryobjectid column. Both contact queries therefore throw, and the branch fails closed for every non-systemuser workforce caller. The email hijack is conditional on an environment where the column exists but is empty.

Severity changes from the skeptics' ratings:
- C8 kept high. The recalculate IDOR is cross-matter financial disclosure by any authenticated user; the summary half is availability only.
- C11 kept medium, with no disclosure. It could reasonably be low.

New leads the skeptics raised, outside the twelve claims and NOT verified here:
- Live and email-only (the other two are unverified): a systemuser with no sprk_primarycontact resolves a contact by email alone. See AccessibleRecordSetService.cs:1022-1026 -> ExternalParticipationService.ResolveExternalContactAsync(oid:null). It uses $top=1 with no ambiguity deny, which bypasses the task 013 guard. Recorded only in wave2-parallel-merge-plan.md §A16.
- FinanceAuthorizationFilter accepts a query-string documentId. /invoices/search?documentId=<own doc> may pass and search tenant-wide (FinanceAuthorizationFilter.cs:137-141; InvoiceSearchService.cs:171-180). Confirm/reject routes authorize one id and act on another.
- ScorecardCalculatorEndpoints.cs:25-46 may have the same auth-only shape as finance recalculate.
- Finance recalculate PATCH without If-Match may upsert-create records for unknown GUIDs.

Priority for fixes: C1 and C2 together (external read and veto parity), then C9 (interim: drop owningbusinessunit), C8 recalculate, and the C4 owner decision.

## Six-case UAC synopsis (verified)

### UC1
- **Headline:** Two deciders. On MDA/Office per-record gates, Dataverse decides (OBO RetrievePrincipalAccess). On SPA/Teams, the BFF matches ownership and assignment columns app-only and grants a flat R|W|C.
- **Plane/identity:** oid maps to systemuser.azureactivedirectoryobjectid. Internal: OBO, caller found by WhoAmI or an OBO oid lookup, then RPA. Teams: app-only systemuser lookup (isdisabled=false) and a BU/team/primary-contact read, each cached 10 min.
- **Who decides:** Dataverse on internal per-record gates. The BFF decides on SPA/Teams and on internal aggregates (Daily Briefing, Workspace briefing, LookupUserMembership node, /api/users/me/memberships). The finance/scorecard recalculate routes check nothing beyond sign-in.
- **What grants access:** Internal: RPA on the row being checked. For documents that is the sprk_document row's own ownership, not the matter's. Teams: exact match on ownerid, owningteam, owningbusinessunit, or a registry assignment column naming the linked contact/org, plus the linked contact's grants.
- **Level:** Internal: RPA rights (Assign dropped); download needs Write. If RPA fails, the DataverseAccessDataSource read probe gives Read or None and CallerRecordAccessProbe denies; both results cached 60 s. Teams: R|W|C on membership hits, a grant's own level on grant-only hits; the deny list can remove either.
- **Caveats:**
  - OVER-GRANT: on Teams, every BU-owned record gets R|W|C whatever the role depth. Aggregates disclose even more: all discovered lookups, no vetoes. Recalculate routes read/write finance fields on any id, app-only.
  - UNDER-GRANT/GAPS: Teams ignores POA shares and BU depth and cuts at 5,000 rows. Finance summary denies everyone (matter id checked as a document). The ethical wall is enforced only on Teams, and only when a contact resolves.
  - STALE/DIVERGENT: nothing invalidates caches on BU, team or owner change (up to ~15 min on Teams, 60 s internal; faulted reads also cached). The A5 impersonated swap is registered but unused, so the two planes can disagree.

### UC2
- **Headline:** Supported only on SPA/Teams /api/v1/external, where the user is treated as a contact. Access comes from grant rows plus standing and org-expansion membership, then deny/Restricted vetoes. Every internal route checked denies non-systemusers (not audited exhaustively).
- **Plane/identity:** Workforce token, B2B guests included; the code enforces no tenant restriction. With no enabled systemuser: contact.azureactivedirectoryobjectid = oid, else an unverified email claim (email > preferred_username > upn; in practice preferred_username) matched to emailaddress1. All reads app-only.
- **Who decides:** BFF (AccessibleRecordSetService.ComposeForContactAsync). Dataverse row security is never consulted.
- **What grants access:** Active, dated grant rows for the contact or its active orgs. Standing term needs the contact's sprk_standinggrant and baseline. Org expansion needs only the ORG's own flag and baseline, via registry org lookups. Then deny list; Restricted is absolute (unreadable flags count as Restricted).
- **Level:** The highest term: ViewOnly=Read, Collaborate=R|C|W, FullAccess adds Delete (no route uses Delete). Most read routes check presence in the set, not Read.
- **Caveats:**
  - IDENTITY: the email fallback matches an unbound contact on a mutable, unverified claim and never binds the oid; the no-hijack guard is inert. Duplicate emails (even inactive) cause an ambiguity deny; with the oid column unprovisioned, everyone is denied.
  - REVOCATION/FAIL-SOFT: deactivating the contact or org, or passing the end date, revokes nothing (caches 60 s/5 min/10 min). A junction-read fault drops the org axis of the wall (fail-open); a grant fault caches an empty set for 60 s. Over 5,000 rows are silently truncated.
  - PLANE-SPECIFIC: the vetoes follow the workforce sign-in, not the contact; the same person via CIAM bypasses them. Tier-1 entitlement is advisory. Content download exists only for projects.

### UC3
- **Headline:** Explicit grants only. The BFF reads the CIAM contact's direct and org-inherited grant rows app-only; it runs no deny, Restricted or Secure veto and no standing or org-expansion membership.
- **Plane/identity:** CIAM token, accepted only on /api/v1/external (the only ExternalCollaboration binding). oid maps to contact.sprk_externalobjectid. The first-login email fallback ($top=1, no statecode) binds an unbound contact and refuses one bound to another oid.
- **Who decides:** BFF (CiamContactPrincipalStrategy). Dataverse roles, BUs, teams and shares play no part.
- **What grants access:** Active, unexpired grant rows for the contact or an org it actively belongs to, on a project, matter or WA, minted via /grant by anyone with Write. Children are listable through any root; content, versions, upload and events exist only under projects.
- **Level:** The highest across rows; FullAccess acts as Collaborate. Reads check set membership, to-dos and creates check rights. Null-level project rows are dropped; null-level matter/WA rows are kept at None but still confer module reads.
- **Caveats:**
  - DESIGNED, NOT LIVE: FR-21 Restricted, FR-22 Secure (org grants reach secure roots), FR-23 deny and FR-24/25 standing/org-expansion do not run here. The grant endpoints check none of them either.
  - REVOCATION: invalidation is keyed on the admin's tid, so changes wait for the 60 s TTL. Only grant rows or membership-row deactivation revoke (not contact, org, root or end date); the reconciliation job is disabled. A grant-read fault caches an empty 200 for 60 s.
  - OVER-EXPOSURE: /api/v1/external/api/dataverse/fetch (and /record) has no column filter, so sprk_graphdriveid, sprk_graphitemid and sprk_filepath leak. The email bind is arbitrary with duplicate emails. CIAM-created records are owned by the app user with no ancestor stamp.

### UC4
- **Headline:** For PROVISIONED secure projects, Dataverse isolates the project row: it is owned by the Secure Record BU's default team, and people enter via POA shares or reaching roles. SPA/Teams ignores those shares and never suppresses membership.
- **Plane/identity:** Internal: OBO RPA for the caller's systemuser, on the project row only. Document-id gates check the sprk_document row, which is not in the Secure BU. SPA/Teams: UC1 matching; Secure only cuts the linked contact's org-inherited grants to None (key kept).
- **Who decides:** Mixed. Dataverse decides on MDA and on the Office/BFF per-record gates; the BFF decides on SPA/Teams.
- **What grants access:** POA share: the creator at provisioning (the wizard sends no colleagues), others later via /share-user. Also: users in the Secure Record BU (automatic default-team members), the Secure Record Owner role, and Global/ancestor-Deep/team-held roles. 'Memberless' is environment config, checked only by the opt-in NFR-05 test.
- **Level:** Internal: RPA. Creator: R/W/Append/AppendTo/Share. /share-user gives ViewOnly, Collaborate or FullAccess, narrowed to the sharer's rights (FullAccess only if the sharer holds Delete); never Share/Assign. Teams: flat R|W|C on a membership hit.
- **Caveats:**
  - PARTIAL ISOLATION: the flag alone isolates nothing, only a completed provision does (sprk_project only). A failed provision leaves the project in the creator's BU and uploads 409. Documents sit outside the Secure BU, so sharees fail the document gates. A null or FLS-masked flag routes to the shared container.
  - WRITE-ONLY ESCALATION: management routes check only Write, so sharees can re-share; /unshare-user removes anyone and /unsecure-project reassigns. A creator-share failure after the ownership move locks the creator out with no retry. MDA, Office and /share-user never consult the deny list or Restricted.
  - PLANE DIVERGENCE: SPA/Teams cannot see POA shares, so the creator can be missing there. A registry contact lookup or the linked contact's direct grant admits a user the MDA denies. None-rights suppressed keys stay readable (A5 not live).

### UC5
- **Headline:** SPA/Teams only. Rights come only from a DIRECT personal grant row: standing and org-expansion terms are dropped entirely. Org-inherited grant rows drop to None but keep the key, so presence-gated reads still pass.
- **Plane/identity:** Same as UC2. Nothing writes contact.azureactivedirectoryobjectid, so in practice access rests on an email/preferred_username match. Dataverse Secure BU and POA shares cannot apply, because a contact is not a principal.
- **Who decides:** BFF (ComposeForContactAsync, with pre-max Secure suppression and then the deny and Restricted vetoes).
- **What grants access:** An active, dated row keyed to this contact on the secure root: /grant (Write on root), or /invite-and-grant (also creates a CIAM account and sends an invite), or a direct Dataverse write. Deny and Restricted remove even FullAccess, but a junction-read fault drops the org-keyed walls.
- **Level:** The direct row's own level. But the grant cache drops DirectAccessLevel, so on cache hits a direct grant on a secure root yields None.
- **Caveats:**
  - LIKELY DEFECT, OVER-GRANT: an org-only secure root keeps its key at None, so project, documents, content, versions, events and grids all pass (only to-dos and mutations deny). /grant has no issecure check, so an org grant exposes every firm member, ended memberships included; null-level matter/WA rows behave the same.
  - LIKELY DEFECT, UNDER-GRANT: CachedParticipation and CachedRootGrant do not store DirectAccessLevel. While the cache is warm, direct grants on secure roots read as None: creates, PATCH and to-dos return 403 while presence reads pass.
  - PLANE/IDENTITY: the vetoes follow the workforce sign-in, not the contact; the same contact via CIAM bypasses all three. UC2 identity gaps apply (email match; a deactivated contact keeps access). The hard-coded three-root flag list is a latent risk only.

### UC6
- **Headline:** Supported, but sprk_issecure has no effect on CIAM authorization. Any direct or org grant admits at its full level, with no Secure, Restricted or deny veto. This does not conform to FR-21/22/23.
- **Plane/identity:** Same as UC3. The first-login email match binds an unbound contact and inherits all its grants, secure ones included. sprk_issecure is read only for display and for choosing the upload container.
- **Who decides:** BFF (CiamContactPrincipalStrategy uses the effective AccessLevel, never DirectAccessLevel).
- **What grants access:** Any active, dated direct or org grant row on the secure root. /grant has no issecure check and needs only Write, so Collaborate sharees can mint org-wide grants. Direct Dataverse rows and maker-added membership rows also count.
- **Level:** The highest direct/org level, unsuppressed. Matter/WA rows with a null level still give read access through the module host.
- **Caveats:**
  - DESIGNED, NOT LIVE: FR-21/22/23 run on the workforce plane only; task 037's 'both planes' meant systemuser/contact, and it never touched CallerPrincipalResolver. An org grant admits every active firm member. A deactivated firm, contact or past-end-date membership keeps access (reconciliation job disabled, report-only).
  - SAME PERSON, DIFFERENT ANSWERS: the vetoes apply on a workforce token but not on CIAM (CIAM also gets no standing/org-expansion). No test pins CIAM secure behavior. Fail-closed, but faults are cached 60 s, and revocation waits for the TTL (wrong-tid invalidation).
  - Only PROVISIONED secure projects are isolated. An unprovisioned secure project returns 409 on CIAM upload. Matters and WAs have no CIAM content, version or upload routes at all, secure or not.

## Owner mental-model verdicts
- **(a) System users get access when their team assignment matches the record's assigned team** → partially correct: On MDA/Office the BFF compares no teams; Dataverse decides through the owning team's roles, depth and shares (for documents, the document row's own owner). On SPA/Teams an owningteam match gives R|W|C, but so do BU, ownerid and registry assignment columns; a custom 'assigned team' lookup confers nothing.
- **(b) Contacts get access when associated to the record** → mostly incorrect: Association alone grants nothing; access comes from explicit, dated grant rows (direct or via the org). On the workforce plane, association adds access only through registry lookups plus a standing grant (contact's or org's), never on secure roots. On CIAM it never counts.
- **(c) Secure records work by adding the user's/contact's oid to the record** → incorrect: No oid is written to the record; the oid only identifies the caller. System users: provisioning moves the project row (not its documents) to the Secure BU's default team, and people enter via POA shares. Contacts: a grant row keyed by contact id; Secure is BFF suppression on the workforce plane only, which CIAM ignores.

## Cross-cutting
- Two deciders for system users: Dataverse RPA on the MDA/Office per-record gates (document gates check the sprk_document row) versus app-only BFF matching on SPA/Teams and aggregates. The A5 fix (ImpersonatedRootSetSource) is registered but inert (ExternalAccessModule.cs:195-198).
- /api/v1/external read gates check presence only (CallerPrincipalResolver.cs:161, module id sets). So None-rights keys (Secure-suppressed org grants, null-level matter/WA rows) can still be read and their content downloaded.
- The Restricted, Secure and deny-list vetoes run only in AccessibleRecordSetService, i.e. for workforce callers on /api/v1/external. The internal plane and the CIAM plane have none, so a wall on a contact holds only for that person's workforce sign-in.
- Anyone with Write can mint access (DelegationRuleFilter.cs:86): /grant and /share-user do no ShareAccess, issecure or deny check. Grant, membership and share rows written directly in Dataverse bypass the BFF, and the reconciliation job ships disabled and report-only.
- Revocation is soft: deactivating a contact, org or root, or passing an end date, revokes nothing. Caches (60 s to 10 min; ~15 min for BU/team changes) have no invalidation hooks and store fault results. The grant cache drops DirectAccessLevel (ExternalParticipationService.cs:1242-1268), and CIAM invalidation uses the wrong tid.