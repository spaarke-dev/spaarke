# Task 166: Memory, Compose and remaining-surface route authorization

> **Task**: `tasks/166-memory-compose-and-remaining-route-authorization.poml` (GitHub #1105)
> **Branch**: `task/uac-r2-166` from `work/unified-access-control-r2` @ `e6dd48b43`; **r1**: `task/uac-r2-166-r1` (§18);
> **r2**: `task/uac-r2-166-r2` (§19); **f1**: `task/uac-r2-166-f1` (§20)
> **Rigor**: FULL (bff-api, auth, security; TEST-MODIFYING override applies)
> **Outcome (g, 2026-10-05)**: owner round 54 built and the f1-v2 verification closed (§23): a FRESH Write check for a
> post-move edit, a renewed relocation lock, post-move versions ordered by time (`history-undecidable` otherwise), a biting
> test for every fail-closed branch; §22's seeding claims corrected. Owner round 56 applied (last fix round; known limits §23.12).
> **Outcome (f1, 2026-10-04)**: round 21 item 1 (i)–(iii) and round 25 item 6 are BUILT (§20): the BFF pointer-attach
> route and every client pointer writer moved to it (7 PCF bundles rebuilt with `npm run build:prod`); the ONE
> `DocumentContainerRelocator` (round 26 item 3) and the legacy migration job + its dry-run / `-Apply` / `-Verify`
> driver; the strict derived-container rule behind `DocumentPointer:StrictDerivedContainer` (default = the round-23
> interim rule); the FLS script's `-ClientNoLongerWritesPointers` precondition made satisfiable (deployed-web-resource
> scan); the `sprk_report` pointer FLS target + the server-side allowed-workspace check; root-owned rows confined to the
> root's own containers; the real export request builder under test. What remains of the items is the live writes,
> each a script with a pending manual gate (§20.11). Status `completed-with-escalation` for ONE found consequence: a
> relocated file's RAG index entries keep the old item id, and the complete fix is new AI-facade surface (§20.12 🔔).
> **Outcome (r2, 2026-10-04)**: the 20-item verification of `582a4b42a` is closed and owner round 23 is implemented
> (§19): the interim document-pointer check verifies the ITEM and the owner's customer subtree; reporting export carries
> the RLS identity; the Save-As registration path is removed and DELETE is guarded; Create checks the caller's privilege
> before the clone; the field-mapping source is read as the caller. Status `completed-with-escalation`: round 21 item 1
> (i)–(iii) remain OWED by task 166 for the follow-up round round 23 names (§19.10).
> **Outcome (r1, 2026-10-04)**: COMPLETED. Owner round 21 decided the three escalations of §8 and r1 implements
> them (§18); r1 also closes the 25-item verification of `18a6aebdf` (§18.2). Not closed: publish size (harness skip,
> §18.10). Round 21 item 1 steps (i)–(iii) are owed by task 166 and run by the main session (§18.3).
> **Outcome (first run)**: ESCALATED. Every route in scope is fixed or deleted EXCEPT the reporting module, which is STOPPED
> on escalation trigger 5. Two more triggers fired and are reported below (trigger 1: the document-pointer
> second door; trigger 4, partly: the Matter → Invoice push profile).
> **Date**: 2026-10-03 / 2026-10-04

## 1. Binding inputs applied

| Source | What it decided here |
|---|---|
| Owner round 9 | The mandate and the fix pattern: decide on the caller's OWN rights; pre-check as the user before any app-only write; one answer for unknown and denied. |
| Owner round 10 item 1 | Delete a route with no caller in the repo AND in no published API description; fix it otherwise. Applied to six routes (§4). The POML body's older instructions for these routes (gate F7, refuse 13 fields on F0, keep F13 per AIR2-075) are superseded by this binding amendment. |
| Owner round 12 item 4 | `DELETE /api/memory/pins/{pinId}` answers ONE 404 for unknown and not-yours (OWNER-COMPARISON basis). Applied to `PUT` too (same oracle, same handler shape); recorded as a decision (§7). |
| Owner round 12 items 8 and 9 | The non-sweep findings in the amendments stay owned by 166: revoke `ContainerId`, office to-do Create, close-project internal users. All three are fixed. |
| Owner round 15 (standing) | "Fix it in the correct way; never defer or sideline." Escalation triggers remain legitimate stops; items found but not owned are raised in §11, not dropped. |
| POML amendments (a)–(f) | (a) healthz doc read deleted; (b) revoke container derived; (c) to-do Create privilege; (d) save session binding; (e) close-project grantee-scoped; (f) reporting CRUD — STOPPED with F14–F16 (§8, trigger 5). |

**Task 167 had NOT landed on this branch** (no Pending waivers owned by "166" in `RouteAuthorizationGuardTests.cs`), so
guard branch (b) applies: no GovernedFiles entries were added for files not governed today; the already-governed
edits were made; §9 and §10 carry the ledger input and the per-route table for the main session.

## 2. Live read-only facts (spaarkedev1)

Read 2026-10-03 and re-verified 2026-10-04, read-only (Dataverse MCP SQL). No live write was made.

| Fact | Value |
|---|---|
| `privilege` rows (name → id) | `prvCreatesprk_Matter` c36cb486, `prvCreatesprk_Project` b3db8aff, `prvCreatesprk_Invoice` da03fe43, `prvCreatesprk_Todo` 14c94423 (all accessright 32 = Create). These exact strings are the code constants, each pinned by a test. |
| `fieldpermission` rows on `sprk_graphdriveid` / `sprk_graphitemid` | **none** → escalation trigger 1 fired (§8). |
| Active field-mapping profiles (`sprk_fieldmappingprofile` ⨝ `sprk_recordtype_ref`) | 4, all source `sprk_matter` (in EntityAccessFilter's map): → `sprk_workassignment`, → `sprk_event`, → `sprk_invoice`, → `sprk_reportcard`. |
| `sprk_regardingmatter` on each target | present on `sprk_workassignment`, `sprk_event`, `sprk_reportcard`; **ABSENT on `sprk_invoice`** (which carries `sprk_matter` instead) → trigger 4 fired for that one profile (§8). |
| `sprk_report` catalog rows | **0** (re-verified 2026-10-04). |
| Reporting client contract | `src/solutions/Reporting/src/services/reportingApi.ts` sends `reportId` = the `sprk_report` ROW id and NO `workspaceId`; the BFF requires `workspaceId` and treats `reportId` as the Power BI report id; the client updates with PATCH where the BFF maps PUT. The `businessunit`/`bu` claim the RLS code reads is produced by no Entra token or claims transformation. Module mapped in dev (PowerBi configured), recorded 2026-10-03. → trigger 5 (§8). |
| Impersonation | The field-mapping push impersonates through `IImpersonatedCommunicationQuery` / `UpdateRecordFieldsAsync(impersonateSystemUserId:)`, the same MSCRMCallerID path the Office and Workspace modules already use in production (task 104); no new impersonation path. |

## 3. Route-by-route (17 findings + amendments)

Every deny below is the route's ONE answer for unknown and forbidden. "Seeded" = the check was removed locally and the
named tests went red (§6).

| # | Route | Change | Mechanism | Tests |
|---|---|---|---|---|
| F0 (S-36) | `PUT /api/v1/documents/{id}` | **DELETED** (§4) | — | `AccessControl.DocumentDestroyAuthorizationTests.PutDataverseDocument_IsRetired_NoCallerReachesAWrite`; `Regression.RouteAuthorization.DeadRouteRetirementTests.RetiredDocumentPut_WhenRequested_Returns405_ThePathServesOnlyItsOtherVerbs` |
| F1 (S-76) | `POST /api/v1/work-assignments` | **DELETED**, file removed | — | `DeadRouteRetirementTests.RetiredRoute_WhenRequested_Returns404NotRouted` |
| F2 (S-63) | `POST /api/compose/documents/{documentSpeId}/promote` | **DELETED** with `PromoteComposeDocumentBody/Response`; `IComposeService.PromoteIfEphemeralAsync` kept (create-on-save) | — | same; `ComposeRouteSurfaceContractTests` row removed |
| F3 (S-64) | `POST /api/compose/upload` | Session-owner check under the CLAIM tenant BEFORE any `ITenantCache` read or `ProjectForMount`; not-owned / unknown / unowned / store fault = the existing 404 "Uploaded File Not Available" | handler, body-scoped session (`ComposeActiveDocumentEndpoints.ResolveOwnedSessionAsync`) | `Integration.Compose.ComposeSessionAndContainerAuthorizationContractTests.Upload_*` + the `ResolveOwned_*` decision table |
| F4 (S-65) | `POST /api/compose/document/{documentSpeId}/check-changes` | OBO metadata read of THIS item in THIS container (`ISpeFileOperations.ResolveDriveIdAsync` + `GetFileMetadataAsUserAsync`) before the app-only delta; deny = 404 "Document Not Found", `sdap.compose.deny.document_not_visible`; `.Produces(404)` added | handler, OBO | `Api.Ai.ComposeWordShuttlePollEndpointContractTests.Poll_WhenTheCallerCannotSeeTheItem_Returns404_AndNeverRunsTheDeltaOrTouchesState` (3 shapes, asserts no delta call and no orchestrator state), `Poll_InvisibleAndThrowingVisibilityRead_AreTheSameResponse` |
| F5 (S-66) | `GET /api/v1/documents?containerId=` | Gated by `.AddContainerDocumentAuthorizationFilter(queryParameter: "containerId")`; missing id = the filter's own 400 (same sentence as before), no resolver call; Permanent waiver DELETED | route filter | `Api.Documents.DocumentPointerAndContainerListAuthorizationContractTests` (real `MapDataverseDocumentsEndpoints`), `ContainerDocumentListAuthorizationTests` unchanged |
| F6 (S-80) | `POST /api/compose/sessions/{sessionId}/annotations` (+ pull / reanchor) | Tenant = CLAIM in all three handlers; body `tenantId` obsolete (kept on the DTOs so the client binds, documented, listed in `InboundBodyDtoMappingGuardTests.DeliberatelyUnread`) | `SessionOwnershipFilter` + claim tenant | `ComposeSessionAndContainerAuthorizationContractTests.SaveAnnotations_*` |
| F7 (S-42) | `GET /api/memory/records/{entityLogicalName}/{id:guid}` | **DELETED** with `IMemoryAccessAuthorizer.CanCallerReadRecordAsync` (its only caller) | — | `DeadRouteRetirementTests`; `MemoryGovernanceEndpointsTests` records cases removed |
| F8 (S-39) + (e) | `POST /api/v1/external-access/close-project` | `CloseProjectRequest.ContainerId` deleted; container DERIVED from the project (`RecordContainerResolver.ResolveForRecordAsync`): secure → its own container; non-secure → step skipped (never the shared BU container); undecidable → `container_not_cleared` 500 (grants still revoked). Removal is now EXACTLY the revoked grantees (contacts + active org members) through `RemoveMembershipsAsync`; `RemoveAllExternalMembersAsync` (which removed internal users too) and `SpeBulkRemovalResult` DELETED | `DelegationRuleFilter` (unchanged) + server derivation | `AccessControl.ProjectClosureCascadeTests.CloseProject_*` (7 container-step cases incl. client container ignored, non-secure untouched, exact grantee set) |
| (b) | `POST /api/v1/external-access/revoke` | `RevokeAccessRequest.ContainerId` deleted; container derived from the GRANT ROOT by the same helper; non-secure root → `NotAttempted`; undecidable → `Failed` (the M2 500) | `DelegationRuleFilter` + server derivation | `AccessControl.SpeRevokeMatcherTests.Revoke_OfANonSecureRoot_*`, `Revoke_WhenTheRootContainerCannotBeDetermined_*`, `Revoke_AClientSuppliedContainerIdIsIgnored_*` |
| F9 / F10 (S-43 / S-68) | `POST` / `PUT /api/memory/pins[/{pinId}]` | A non-blank `matterId` on ANY pinType must be a GUID (400 field-level otherwise, no probe) and the caller must hold **AppendTo** on `sprk_matters(matterId)` (new key `memory.pin_matter`) BEFORE the repository write; PUT checks the NEW matter after the ownership check; `.ProducesProblem(403)` on POST | handler, `CallerRecordAccessProbe` | `Api.Memory.MemoryRecordAndPinAuthorizationContractTests` (real `MapPinnedMemoryEndpoints`) |
| round 12 item 4 | `PUT` / `DELETE /api/memory/pins/{pinId}` | Not-yours = the SAME 404 as unknown; no matter probe for a pin the caller does not own | OWNER-COMPARISON | `MemoryRecordAndPinAuthorizationContractTests.UpdatePin_AnotherUsersPinAndAnUnknownPin_*`, `DeletePin_AnotherUsersPinAndAnUnknownPin_*`; `PinnedMemoryEndpointsContractTests.*_NotOwned_Returns404` (flipped) |
| F11 (S-67) | `POST /api/v1/field-mappings/push` | Read on the SOURCE as the caller + the caller's systemuserid (WhoAmI) before ANY profile/source/child read; children queried AS THE CALLER (`IImpersonatedCommunicationQuery`, filter `_sprk_regarding{base}_value` wrapped ONCE) and written AS THE CALLER (`impersonateSystemUserId`); the uniform 404 "Source record not found." (no id, no entity) | handler: probe + impersonation ("handler decision: query runs as the caller") | `Api.FieldMappings.FieldMappingPushAuthorizationContractTests` (real `MapFieldMappingEndpoints`; pins the query string) |
| F12 (S-69) | `POST /api/office/quickcreate/{entityType}` | After the unchanged source half, matter / project / invoice require the caller's live Create privilege (`CallerHoldsPrivilegeAsync`); invoice reuses `FinanceAuthorizationFilter.CreateInvoicePrivilege`; account / contact / unparseable pass through with no query | `QuickCreateSourceAccessFilter` | `Api.Office.OfficeQuickCreateContractTests.Post_QuickCreate_*`, `CreatePrivilegeFor_*` |
| (c) | `POST /api/office/todo` | After the source checks, `prvCreatesprk_Todo` as the caller, also for a standalone to-do; its own reason code `insufficient_privilege` (names a table, never a record) | `TodoSourceAccessFilter` | `Api.Office.OfficeTodoSourceAuthorizationContractTests.Post_Todo_*Privilege*`, `CreateTodoPrivilege_IsTheLiveDataversePrivilegeName` |
| (d) | `POST /api/compose/documents/{documentSpeId}/save`, `/documents/create-on-save` | Tenant = CLAIM; the body session is bound ONLY when the caller owns it, otherwise the save runs UNBOUND (no rebind, no session-derived container, no session state read) and the sent id is echoed | handler, body-scoped session | `ComposeSessionAndContainerAuthorizationContractTests.CreateOnSave_*` (both routes share `ResolveBindableSessionIdAsync`) |
| F13 (S-82) | `GET /api/workspace/state` | **DELETED** with `WorkspaceStateEndpoints.cs` / `WorkspaceStateResponse.cs` (round 10 supersedes AIR2-075's keep) | — | `DeadRouteRetirementTests` |
| (a) | `GET /healthz/dataverse/doc/{id}` | **DELETED**; smoke check moves to `/healthz/dataverse` (runbooks updated; `.claude` skill edit in §12) | — | `DeadRouteRetirementTests` |
| F14–F16 (S-71/S-70/S-81) + (f) | reporting embed-token, export, reports/{id} and the four CRUD routes | **NOT CHANGED — STOPPED** (trigger 5, §8) | — | — |

## 4. Deletions (owner round 10 item 1) — evidence

Searches (Grep over `src/**/*.{ts,tsx,js,cs,yaml,json,ps1}`, PCF `bundle.js` excluded per the POML; and the published API
descriptions `src/solutions/CopilotAgent/spaarke-bff-openapi.yaml`, `spaarke-api-plugin.json`, `declarativeAgent.json` —
the only OpenAPI / plugin manifests in the repo):

| Route | Callers in repo | Published? | Also deleted |
|---|---|---|---|
| `POST /api/compose/documents/{documentSpeId}/promote` | 0 (the only `/promote` calls are `/api/ai/analysis/promote`: `HistoryOverlay.tsx`, `useReviewedDocumentAnalysis.ts`) | no | `PromoteComposeDocumentBody`, `PromoteComposeDocumentResponse`, the `PostPromoteDocument_*` contract tests, the surface-snapshot row |
| `POST /api/v1/work-assignments` | 0 (other "work-assignments" strings are a LegalWorkspace section id and the external-module key `/api/v1/external/...`) | no | `Api/WorkAssignmentEndpoints.cs`, its mapping line, the `InlineNotificationIntegrationPointsTests` reference |
| `GET /api/memory/records/{entityLogicalName}/{id:guid}` | 0 | no | `IMemoryAccessAuthorizer.CanCallerReadRecordAsync` (+ impl; its privilege-checker dependency) |
| `PUT /api/v1/documents/{id}` | 0 (`DocumentOperations.js` declared the URL and never called it; its `updateDocument` now throws a retirement error) | no — the OpenAPI publishes **GET only** for `/api/v1/documents/{id}` | — (`UpdateDocumentRequest` unchanged; server code builds it) |
| `GET /api/workspace/state` | 0 (JSDoc only in `Spaarke.AI.Widgets/src/types/WorkspaceTab.ts`) | no | `Api/Workspace/WorkspaceStateEndpoints.cs`, `Models/Workspace/WorkspaceStateResponse.cs`, `WorkspaceStateEndpointsContractTests.cs` (its replacement is the absence guard). `IWorkspaceStateService` STAYS (SprkChatAgentFactory + AssistantSuggestionService read it); its stale comments were corrected. |
| `GET /healthz/dataverse/doc/{id}` | 0 code callers; the post-deploy smoke check (bff-deploy §9c, two dotnet-10 runbooks) | no | — |

Census: `ExpectedEndpointFileCount` 120 → 118 (two files), with its history line. Escalation trigger 3 did NOT fire (no
caller found for any of the four routes it names).

`SpeContainerMembershipService.ListExternalMembersAsync` now has no production caller (its only caller was the deleted
`RemoveAllExternalMembersAsync`). It is kept: it is a tested, honest read primitive, and deleting it is not required
by the route rule. Its doc says not to build a removal on it ("user identity" includes internal users). Candidate for
the test-diet / dead-code pass.

## 5. Behaviour changes a reviewer should know

1. **Manage Access revoke now actually cleans SPE on SECURE roots.** The shipped modal never sent `containerId`, so
   before this task every single-grant revoke reported `NotAttempted`. Now the container is derived, so a secure root's
   grantee permission is removed (or `Failed` → the M2 500 if it cannot be). Live gate (d).
2. **Close-project removes only the revoked grantees** (contacts + active org members, by email, one paged read), not
   every permission with a user identity. A non-secure project's container is never touched.
3. **check-changes for a document that was DELETED in Word** now answers the 404 (the caller can no longer see the
   item) instead of `{deleted: true}`. The client (`useComposeWordShuttle.ts`) never read `deleted`; a non-OK status is
   an error it already handles.
4. **Save with a session the caller does not own runs unbound** rather than failing (the Compose Load #863 rule).
5. **Pin updates** carrying a `matterId` re-check AppendTo every time (the POML's rule), including an unchanged matter.

## 6. Seeding (each new check removed locally, tests run, then restored from a byte copy and touched)

One seeded build, 2026-10-04, 13 seeds in disjoint handlers; 51 of 227 targeted tests went red, each naming its route:

| Seed (removed / inverted) | Red tests (examples) |
|---|---|
| upload owner check (`ownsSession` forced true) | `Upload_AnotherUsersSession_IsTheSame404AsExpiredBytes_AndReturnsNoneOfTheirBytes`, `Upload_AnUnknownSessionId_IsTheSameUniform404` |
| check-changes visibility (forced visible) | `Poll_WhenTheCallerCannotSeeTheItem_*` (not-visible, container-unresolvable), `Poll_InvisibleAndThrowingVisibilityRead_AreTheSameResponse` |
| list filter removed from `GET /api/v1/documents` | `List_ACallerWithNoReadOnTheOwningRecord_*(query)`, `List_AContainerNoSecureRecordOwns_*(query)`, `List_TheTwoRoutesAgree_*` |
| annotations write uses body tenant | `SaveAnnotations_AForeignBodyTenant_*`, `SaveAnnotations_ABodyWithNoTenantAtAll_IsAccepted` |
| save binds any session | `CreateOnSave_AnotherUsersSession_IsNotRebound_*` |
| pin matter AppendTo forced granted | `CreatePin_WithoutAppendToOnTheMatter_*` (4 rights), `CreatePin_OfAnyPinTypeNamingAMatter_*`, `UpdatePin_RepointingAnOwnedPinAtAMatterWithoutAppendTo_*`, `CreatePin_WithNoBearerToken_*`, `CreatePin_AnUnknownMatterAndAForbiddenOne_*` |
| delete not-yours answered 403 | `DeletePin_AnotherUsersPinAndAnUnknownPin_*`, `PinnedMemoryEndpointsContractTests.DeletePin_NotOwned_Returns404` |
| push source Read forced granted | `Push_WhenTheSourceCannotBeAuthorizedAsTheCaller_*` (no-read, rights-without-read, probe-throws, no-token, caller-unresolvable, unmapped) |
| push child write app-only (`impersonateSystemUserId: null`) | `Push_ForAReadableSource_QueriesAndWritesTheChildrenImpersonatingTheCaller` |
| quick-create privilege forced held | `Post_QuickCreate_WhenTheCallerLacksTheTablesCreatePrivilege_*` (matter, project, invoice), `Post_QuickCreate_WhenThePrivilegeCheckThrows_*` |
| to-do privilege forced held | `Post_Todo_WhenTheCallerLacksCreateOnSprkTodo_*`, `Post_StandaloneTodo_*`, `Post_Todo_WhenThePrivilegeCheckThrows_*` |
| non-secure root returns the shared BU container | `CloseProject_NonSecureProject_NeverTouchesTheSharedBusinessUnitContainer`, `SpeRevokeMatcherTests.Revoke_OfANonSecureRoot_*` |
| an extra (internal) email added to the closure removal | `CloseProject_SecureProject_RemovesExactlyTheRevokedGranteesFromItsOwnContainer` (+ 6 closure cases) |
| the six retired routes re-mapped as stubs | `DeadRouteRetirementTests` (4 HTTP cases + the route-table case), `PutDataverseDocument_IsRetired_*` (both rights) |

Restored: no `SEED-166` marker remains in `src/` (verified with Grep).

## 7. Decisions recorded (not escalated)

- **PUT pins get the uniform 404 too** (round 12 item 4 names DELETE; PUT had the identical 404/403 oracle).
- **Save: unbound rather than refused** for a session the caller does not own (Load #863 precedent; no saved work
  refused because a session aged out). Known minor degradation: an owned-but-evicted session's PDF-source marker is
  not read on that save.
- **Revoke on a non-secure root = `NotAttempted`** (one revoke must not sweep the shared BU container; the grantee may
  hold other grants it serves). Trigger 2 did NOT fire: the only BFF path that ADDS container members is
  `SpeContainerMembershipService.GrantMembershipAsync`, which has no caller; `DemoProvisioningService` adds INTERNAL demo
  users and the SPE admin console is operator-only — neither is grant-tied.
- **Pins: GUID validation on every pinType** (a non-GUID matter id is a field-level 400 before any probe).

## 8. Escalations fired (CLAUDE.md §6) — DECIDED by owner round 21 (2026-10-04), implemented in r1 (§18.1)

🔔 **Human Input Required — trigger 1: the document-pointer SECOND DOOR (F0)**
- Situation: deleting `PUT /api/v1/documents/{id}` closes the BFF door, but `sprk_graphdriveid` / `sprk_graphitemid` have
  NO field-level security (no `fieldpermission` rows), so any Write holder can still set them natively (MDA form,
  `Xrm.WebApi`); `GET /{id}/download`, `DocumentsBulkEndpoints`, `FileAccessEndpoints`, `ChatDocumentEndpoints`,
  `AppOnlyAnalysisService` and `DocumentTextSource` then follow a forged pointer app-only. **The chain is NOT closed.**
- Options: **(a) RECOMMENDED** — field-level security on the two pointer columns, writable only by the BFF identity, as
  task 150 does for `sprk_issecure` (a schema change → a live gate, dry run / apply / verify); (b) server-side pointer
  verification before every app-only download (the pointer's drive must equal the container `RecordContainerResolver`
  derives for the row).

🔔 **Human Input Required — trigger 5: REPORTING (F14, F15, F16 and amendment f) — STOPPED, no code changed**
- Situation: the catalog binding the POML prescribes assumes the client sends the Power BI report id + workspace id. Live
  facts contradict it: the client sends the `sprk_report` ROW id and no workspaceId (so every client call is a 400
  today), the client updates with PATCH while the BFF maps PUT, the catalog has 0 rows, and the `businessunit`/`bu` RLS
  claim is produced by nothing, so no embed token carries a BU role.
- Options: **(A) RECOMMENDED** — the catalog-row id becomes the contract: every reporting route takes the `sprk_report`
  id, reads that row AS THE CALLER (`IDataverseUserClient`; absent/unreadable → one 404) and derives the Power BI report
  and workspace ids from it; CRUD binds to rows the caller can read/write; RLS identity redesigned (owner decision);
  client fixed to match (PATCH/PUT). (B) keep the Power BI-id contract and fix the client to send report + workspace ids.
  (C) delete the module's clientless routes under round 10 item 1. All seven reporting Pending waivers stay owned by 166.

🔔 **Human Input Required — trigger 4 (partial): the Matter → Invoice push profile**
- Situation: the active profile "Matter to Invoice (Attorney Matrix)" targets `sprk_invoice`, which has NO
  `sprk_regardingmatter` (it carries `sprk_matter`). The corrected child filter `_sprk_regardingmatter_value` therefore
  fails for that target and the push returns the route's 500 (fail closed — no app-only fallback, no map entry). The
  three other profiles are served.
- Options: (a) RECOMMENDED — a per-target parent-lookup convention (`sprk_invoice` → `sprk_matter`) resolved from
  metadata, still impersonated; (b) add `sprk_regardingmatter` to `sprk_invoice` (schema + backfill; live gate);
  (c) deactivate the profile. Owner decides.

Not fired: trigger 2 (see §7), trigger 3 (no callers), trigger 6 (no ADR exception needed).

## 9. Route authorization ledger input (for task 167; route key as the guard spells it)

| Route key | Mechanism that now decides | Deny test (FQN) |
|---|---|---|
| `PUT /api/v1/documents/{id}` | DELETED | `Sprk.Bff.Api.Tests.Regression.RouteAuthorization.DeadRouteRetirementTests.RetiredDocumentPut_WhenRequested_Returns405_ThePathServesOnlyItsOtherVerbs` |
| `POST /api/v1/work-assignments` | DELETED | `Sprk.Bff.Api.Tests.Regression.RouteAuthorization.DeadRouteRetirementTests.RetiredRoute_WhenRequested_Returns404NotRouted` |
| `POST /api/compose/documents/{documentSpeId}/promote` | DELETED | same |
| `GET /api/memory/records/{entityLogicalName}/{id:guid}` | DELETED | same |
| `GET /api/workspace/state` | DELETED | same |
| `GET /healthz/dataverse/doc/{id}` | DELETED | same |
| `GET /api/v1/documents` | `ContainerDocumentAuthorizationFilter` (query `containerId`) | `Sprk.Bff.Api.Tests.Api.Documents.DocumentPointerAndContainerListAuthorizationContractTests.List_ACallerWithNoReadOnTheOwningRecord_Is403_AndTheListingNeverRuns` |
| `POST /api/v1/external-access/close-project` | `DelegationRuleFilter` (Write on ProjectId) + server-derived container, grantee-scoped removal | `Sprk.Bff.Api.Tests.AccessControl.ProjectClosureCascadeTests.CloseProject_AClientSuppliedContainerIdIsIgnored_TheDerivedContainerIsTheOnlyOneTouched` |
| `POST /api/v1/external-access/revoke` | `DelegationRuleFilter` + server-derived container | `Sprk.Bff.Api.Tests.AccessControl.SpeRevokeMatcherTests.Revoke_AClientSuppliedContainerIdIsIgnored_TheDerivedContainerIsTheOnlyOneTouched` |
| `POST /api/memory/pins` | handler: `CallerRecordAccessProbe` AppendTo on `sprk_matters` (`memory.pin_matter`) | `Sprk.Bff.Api.Tests.Api.Memory.MemoryRecordAndPinAuthorizationContractTests.CreatePin_WithoutAppendToOnTheMatter_Is403_AndWritesNothing` |
| `PUT /api/memory/pins/{pinId}` | OWNER-COMPARISON (uniform 404) + AppendTo on the new matter | `Sprk.Bff.Api.Tests.Api.Memory.MemoryRecordAndPinAuthorizationContractTests.UpdatePin_AnotherUsersPinAndAnUnknownPin_AreTheSame404_AndNoMatterIsAsked` |
| `DELETE /api/memory/pins/{pinId}` | OWNER-COMPARISON (uniform 404) | `Sprk.Bff.Api.Tests.Api.Memory.MemoryRecordAndPinAuthorizationContractTests.DeletePin_AnotherUsersPinAndAnUnknownPin_AreTheSame404_AndNothingIsDeleted` |
| `POST /api/compose/upload` | handler: body-scoped session owner (`ResolveOwnedSessionAsync`) | `Sprk.Bff.Api.Tests.Integration.Compose.ComposeSessionAndContainerAuthorizationContractTests.Upload_AnotherUsersSession_IsTheSame404AsExpiredBytes_AndReturnsNoneOfTheirBytes` |
| `POST /api/compose/document/{documentSpeId}/check-changes` | handler: OBO item visibility before the delta | `Sprk.Bff.Api.Tests.Api.Ai.ComposeWordShuttlePollEndpointContractTests.Poll_WhenTheCallerCannotSeeTheItem_Returns404_AndNeverRunsTheDeltaOrTouchesState` |
| `POST /api/compose/sessions/{sessionId}/annotations` | `SessionOwnershipFilter` + the write uses the same CLAIM tenant | `Sprk.Bff.Api.Tests.Integration.Compose.ComposeSessionAndContainerAuthorizationContractTests.SaveAnnotations_AForeignBodyTenant_IsIgnored_TheWriteLandsOnTheAuthorizedSession` |
| `POST /api/compose/documents/{documentSpeId}/save` | handler: claim tenant; session bound only if owned (OBO SPE write) | `Sprk.Bff.Api.Tests.Integration.Compose.ComposeSessionAndContainerAuthorizationContractTests.CreateOnSave_AnotherUsersSession_IsNotRebound_TheSaveRunsUnbound_AndEchoesTheSentId` (shared helper) |
| `POST /api/compose/documents/create-on-save` | same | same |
| `POST /api/v1/field-mappings/push` | handler: probe Read on source + WhoAmI; handler decision: query runs as the caller (children read and written impersonated) | `Sprk.Bff.Api.Tests.Api.FieldMappings.FieldMappingPushAuthorizationContractTests.Push_WhenTheSourceCannotBeAuthorizedAsTheCaller_IsTheUniform404_AndNothingIsReadOrWritten` |
| `POST /api/office/quickcreate/{entityType}` | `QuickCreateSourceAccessFilter` (source Read + table Create privilege) | `Sprk.Bff.Api.Tests.Api.Office.OfficeQuickCreateContractTests.Post_QuickCreate_WhenTheCallerLacksTheTablesCreatePrivilege_Returns403_AndCreatesNothing` |
| `POST /api/office/todo` | `TodoSourceAccessFilter` (source Read + `prvCreatesprk_Todo`) | `Sprk.Bff.Api.Tests.Api.Office.OfficeTodoSourceAuthorizationContractTests.Post_Todo_WhenTheCallerLacksCreateOnSprkTodo_IsRefusedWithInsufficientPrivilege_AndCreatesNoRow` |
| `GET /api/reporting/embed-token`, `POST /api/reporting/export`, `GET /api/reporting/reports/{reportId:guid}`, `GET /api/reporting/reports`, `POST /api/reporting/reports`, `PUT /api/reporting/reports/{reportId:guid}`, `DELETE /api/reporting/reports/{reportId:guid}` | SUPERSEDED by r1 — see §18.11 (`PUT` is now `PATCH`) | §18.11 |

## 10. Per-route guard table for 167 (branch b reconciliation)

- **Delete** 166's Pending waiver for every row of §9 except the seven reporting rows. For the six DELETED routes the
  waiver keys are now ABSENT routes, so `NoWaiverIsStale` would fail on them if kept.
- Classification hints: `ComposeMountEndpoints.cs`, `ComposeSaveEndpoints.cs` → handler-authorized, body-scoped session
  (both are in `SessionOwnershipGuardTests.BodyScopedSessionRoutes`); `ComposeSyncEndpoints.cs` check-changes →
  handler-authorized (OBO visibility); `ComposeAnnotationEndpoints.cs` POST → route-level `SessionOwnershipFilter`;
  `PinnedMemoryEndpoints.cs` → handler decision (probe) + the OWNER-COMPARISON basis of round 12 item 4;
  `FieldMappingEndpoints.cs` push → handler decision (probe + impersonation); `OfficeEndpoints.cs` quickcreate / todo →
  route-level (credited filters); close-project / revoke → `DelegationRuleFilter` (already credited) — the
  InsufficientDecision gap is resolved by server derivation; `DataverseDocumentsEndpoints.cs` list → route-level filter.
- Files deleted: `Api/WorkAssignmentEndpoints.cs`, `Api/Workspace/WorkspaceStateEndpoints.cs` — remove from 167's file
  inventory if it lists them.
- Already-governed edits made here (task 074's guard): the `GET /api/v1/documents` Permanent waiver deleted; the Compose
  HandlerAuthorized reasons rewritten (ComposeDocument without promote, Sync, Mount, Save, Annotation); OfficeEndpoints
  caveat (2) rewritten; census 120 → 118. `FilterMarker`, `DecisionServices` and `ExplicitlyCreditedFilterTypeNames`
  unchanged; Rule A, Rule B, `RuleBCoversEveryFilterRuleACredits` and `NoWaiverIsStale` pass.

## 11. Found, not owned by 166 — raised for the main session (round 15: never dropped)

> **r1**: all three items below are now FIXED in this task (verifier items 10 / 21; amendment d) — §18.2 row 10 / 21.

From the sweep note's noteworthy list (167's POML lists them for classification), confirmed still present:
- `POST /api/compose/documents/{documentRecordId:guid}/refresh-profile` takes `tenantId` from the BODY and a caller-chosen
  `documentRecordId`; the profile is downloaded OBO but persisted app-only to the row.
- Compose Load's `documentRecordId` query parameter is not bound to the OBO-checked item (origin read + profile
  re-trigger on an arbitrary record).
- `POST /api/compose/active-document` records a body `documentId` with no read check.
Recommend assigning them to a sweep task (same patterns: claim tenant; bind the record id to the OBO-checked item).

## 12. `.claude` edit for the main session (sub-agents cannot write `.claude/`)

`.claude/skills/bff-deploy/SKILL.md` line 50 — replace

    - §9c `/healthz/dataverse/doc/{id}` (proves MI → Dataverse)

with

    - §9c `/healthz/dataverse` (proves MI → Dataverse; the per-document probe was retired by unified-access-control-r2 task 166)

(The two `projects/dotnet-10-upgrade-r1` runbooks were updated in this diff. Historical baselines and closed-project
POMLs that mention the old path were left as history.)

## 13. Note to task 158

`POST /api/v1/work-assignments` (app-only `sprk_workassignment` create with `ownerid` = a caller-chosen user, plus an
app-authored notification) is DELETED by task 166. Remove it from 158's inventory of BFF paths that create work
assignments.

## 14. Placement (CLAUDE.md §10) and component justification (§11)

Placement: **in BFF, existing routes only** (`bff-extensions.md` decision criteria: these ARE the BFF's routes; nothing
new is added to its surface — six routes are removed). No new service, DI registration, package, option, job or column.
Publish size: not measured in this task (harness instruction); no package or registration change.

New surface, three-question justification:
- **`OperationAccessPolicy["memory.pin_matter"] = AppendTo`** — Existing: `entity.associate_document` (AppendTo) is the
  nearest key (grep: no pin-to-matter key; "memory.pin" exists only as telemetry counter names). Extension: reusing it
  would misdescribe the act (a pin is not a document) and couple two acts that may need different rights later.
  Cost of nothing: any caller can inject text into the prompt of every user on a matter they cannot even read.
- **`ProjectClosureEndpoint.GranteeRemoval`** (internal record struct) — Existing: `SpeBulkRemovalResult`, deleted with
  the whole-container sweep. Extension: the new removal is per-grantee, so the old type's `EnumerationComplete` no
  longer applies. Cost of nothing: a partial removal (one grantee unresolved) would read as a cleared container.
- Internal static helpers on existing classes (`ResolveOwnedSessionAsync`, `DeriveRecordOwnContainerAsync`,
  `RemoveRevokedGranteesAsync`, `BuildChildRecordQuery`, `UploadedFileNotAvailable`, `DocumentNotVisible`,
  `SourceRecordNotFound`, privilege constants) — modifications of existing components, each extracted so the one
  decision is shared (upload + save) or pinned by a test.
- **`tests/integration/Shared/Dataverse/TestRecordContainerResolver.cs`** (test-only) — Existing: `RecordContainerResolverTests`
  builds the real resolver privately; `ComposeServiceCollaborators.Resolver` is BU-fallback-shaped and unit-only.
  Extension: neither models a SECURE record owning its own container for the external-access suites. Cost of nothing:
  close-project and revoke would be tested against a constant instead of the resolver's real decision.

## 15. Tests

New files: `MemoryRecordAndPinAuthorizationContractTests` (18), `ComposeSessionAndContainerAuthorizationContractTests` (17),
`DocumentPointerAndContainerListAuthorizationContractTests` (10), `FieldMappingPushAuthorizationContractTests` (9),
`DeadRouteRetirementTests` (11), plus additions to `ComposeWordShuttlePollEndpointContractTests`,
`OfficeQuickCreateContractTests`, `OfficeTodoSourceAuthorizationContractTests`, `ProjectClosureCascadeTests`,
`SpeRevokeMatcherTests`, `SpeContainerPagingTests`. Not created: `ReportingCatalogBindingContractTests` (reporting
STOPPED). Test-diet notes: the tests of the deleted routes / methods were deleted or re-based in the same diff (each
re-based test says so in its doc comment); `RemoveMemberships_WhenTheReadFails_EveryMemberIsFailedAndNoneIsAbsent` already
covered the read-failure honesty, so no duplicate was added. ADR-038 bans respected (no `Mock<HttpMessageHandler>`, no
DI-registration or ctor-null tests).

Full runs (once, at the end, on the final code): see §16.

## 16. Gates

| Gate | Result |
|---|---|
| Affected tests | 397 run, 390 passed, 7 skipped (pre-existing skips), 0 failed |
| Full BFF unit suite (`tests/unit/Sprk.Bff.Api.Tests`, compiles contract/auth/regression/seam) | 14,348 total: 14,294 passed, 54 skipped, 0 failed (19 m 49 s) — no timing flake this run |
| NetArchTest (`tests/Spaarke.ArchTests`) | 346 passed, 0 failed |
| Sprk.Bff.Api.IntegrationTests (full) | 104 passed, 0 failed |
| Spe.Integration.Tests (full) | 428 total: 403 passed, 25 skipped (pre-existing environment-gated `SkippableFact`s), 0 failed |
| BFF build | succeeded |
| `dotnet list package --vulnerable --include-transitive` | no vulnerable packages |
| /conflict-check | 25 open PRs scanned against this diff: one overlap — #1120 `chore/remove-dead-shared-code` also touches `Models/Workspace/WorkspaceStateResponse.cs` (same direction: removal). Compose files: spaarkeai-compose-r8 is closed (wrap-up). No hard conflict. |

## 17. Manual live gates (main session, dev; non-admin test users, ids redacted to 8 chars)

1. Deploy the BFF from this branch (normal bff-deploy flow), then smoke `GET /healthz/dataverse` (200).
2. (a) user without access to matter M: `POST /api/memory/pins` `{title, content, pinType:"matter-fact", matterId:M}` → 403, no Cosmos pin row; a user with AppendTo on M → 201.
3. (b) Compose upload with another user's `sessionId` → 404; `check-changes` for an item the user cannot see → 404 (and no new Redis delta key for that container).
4. (c) `PUT /api/v1/documents/{id}` → 405; the row's pointers unchanged (read back).
5. (d) close-project on a SECURE test project: only that project's own container loses the revoked grantees (read back permissions on another container — unchanged; internal users on the project container — unchanged); on a NON-secure test project no container is touched. Revoke from Manage Access on a secure test project removes the grantee's permission on its own container.
6. (e) UpdateRelated push from a matter the user can open updates only children the user can write; a user without Read on the matter gets 404 and no child `modifiedon` changes. (The Matter → Invoice profile fails closed until trigger 4 is decided.)
7. (f) Office quick-create (matter, project) and Office to-do by a user without the Create privilege → 403, no row.
8. (h) `POST /api/v1/work-assignments`, `POST /api/compose/documents/{x}/promote`, `GET /api/memory/records/...`, `GET /api/workspace/state`, `GET /healthz/dataverse/doc/{id}` → 404.
9. (g) reporting: superseded — see §18.12 gate 11.

---

# r1 (2026-10-04): verifier findings closed, owner round 21 implemented

> **Branch**: `task/uac-r2-166-r1` from `task/uac-r2-166` @ `18a6aebdf`
> **Inputs**: the 25-item adversarial verification of 18a6aebdf; **owner round 21** (work branch `8166dd9ea`,
> BINDING, decides all three escalations of §8); the main session's answer to this round's round-21 item-1 question
> (2026-10-04, see §18.3). Standing directive (round 15): fix it the correct way; never defer, never sideline.
> **Outcome**: every finding is closed in code and tests, or proven and recorded, EXCEPT item 24 (publish size, skipped
> on the harness instruction) — §18.10. Round 21 is implemented; the three item-1 steps the main session assigned to
> its follow-up round are recorded as **owed by task 166** in §18.3 (not a deferral, not an owner question).

## 18.1 Round 21 as applied

| Round 21 item | Decision (binding) | Implemented here |
|---|---|---|
| 1 — F0 second door (trigger 1) | BOTH (a) FLS on `sprk_graphdriveid` / `sprk_graphitemid`, writable only by the BFF identity (task-150 pattern: dry run / `-Apply` / `-Verify`, rootcomponentbehavior-0 check) AND (b) a server-side check before every app-only download that the drive item belongs to the record's own container, fail closed | (a) `scripts/Set-DocumentPointerFieldSecurity.ps1` — dry run executed read-only against dev, all preconditions OK except the deliberate `-ClientNoLongerWritesPointers` gate (§18.4). (b) `RecordContainerResolver.IsDocumentPointerContainerAllowedAsync` / `EnsureDocumentPointerContainerAsync`, wired into EVERY app-only row-pointer read (§18.3). The legacy-compatible rule and the owed steps (i)–(iii) are the main session's 2026-10-04 answer. |
| 2 — Reporting (trigger 5) | Option (A) completed: catalog row id is the contract, read AS THE CALLER; PBI report/workspace ids derived server-side; client fixed; update verb aligned; RLS effective identity (business unit) computed server-side from the caller's systemuser; tests per route; the seven reporting Pending waivers resolve | Done (§18.5). |
| 3 — Field-mapping push (trigger 4) | Option (a): the target's parent lookup from relationship metadata; ambiguous fails closed with a clear error | Done (§18.6). |

## 18.2 The 25 findings — what changed per item

| # | Finding | Closure |
|---|---|---|
| 1 | Positive: independent re-runs match | No change. This round's runs: §18.9. |
| 2 | Positive: live facts re-verified read-only | Re-verified again 2026-10-04 (§18.4): still 0 `fieldpermission` rows on the two pointer columns, 0 `sprk_report` rows. |
| 3 | Positive: seeding run A bites | No change. This round's seeds: §18.8. |
| 4 | Replace-save route (`POST /api/compose/documents/{documentSpeId}/save`) had no route test for the session binding | NEW `Integration.Compose.ComposeBodySessionRouteDenyContractTests` (8 methods; the REAL `MapComposeMountEndpoints` / `MapComposeSaveEndpoints` over substituted module boundaries): `Save_AnotherUsersSession_RunsUnbound_TheServiceNeverSeesTheirSession(replace / create-on-save)`, `Save_TheCallersOwnSession_IsForwarded(…)`. Seed S2 (replace route forwards `body.SessionId`) turned the replace case red. |
| 5 / 19 | Upload deny cases tested only on the helper; no route assertion that `ITenantCache` is never read / `ProjectForMount` never called; a fault→500 seed went unnoticed | Same file, through `POST /api/compose/upload`: `Upload_AnotherUsersSession_UnderEverySpelling_IsTheUniform404_AndReadsNothing` (sent × stored in D / N spellings), `Upload_AnUnownedSession_IsTheUniform404_AndReadsNothing` (null and empty `OwnerOid`), `Upload_ASessionStoreFault_IsTheUniform404_NotA500_AndReadsNothing`, `Upload_AnUnknownSession_…`, positive control. Each asserts the response is byte-identical to the expired-bytes 404, no `ITenantCache` read, no `ProjectForMount`. Seed S3 (fault → rethrow) red. |
| 6 / 18 | pull / reanchor annotations: no test that a body without `tenantId` is accepted or that a missing `tid` is 401 with no download | `Api.Ai.ComposeWordShuttlePollEndpointContractTests.AnnotationRoute_ABodyWithNoTenantId_IsAccepted(pull / reanchor)` and `AnnotationRoute_ATokenWithNoTidClaim_Is401_AndDownloadsNothing(pull / reanchor)`. Seeds S4 / S4b red. |
| 7 / 20 | Save: a session-store FAULT degraded to an UNBOUND save (ADR-003; on create-on-save it could place secure-matter draft content in the shared BU container) | `ComposeSaveEndpoints.ResolveBindableSessionIdAsync` now returns `(Faulted, BoundSessionId)`; a fault REFUSES both save routes with **503** `compose_session_unavailable` (ProblemDetails, `correlationId`), before any write; telemetry outcome `StorageFailed`, cause `session-unavailable` (`ComposeSaveTelemetry.CauseSessionUnavailable`); both routes `.Produces(503)` (surface snapshot updated). Not-owned / unknown / unowned still run unbound (Load #863 precedent, §7). Test `Save_ASessionStoreFault_RefusesTheSave_503_AndWritesNothing(replace / create-on-save)`; seed S1 red on both. |
| 8 | `DeriveRecordOwnContainerAsync` used the CONTENT question, which answers a non-secure project / work assignment under a SECURE matter with the matter's container — closure / revoke swept the ancestor | New `RecordContainerResolver.ResolveOwnContainerAsync` (internal): the record's OWN container only — secure with a container → it; secure without → FailClosed; flag ABSENT → FailClosed; not secure / not securable → Unresolved (step skipped). `ProjectClosureEndpoint.DeriveRecordOwnContainerAsync` (shared by close-project and revoke) calls it. Tests: `AccessControl.RevokeGrantOverlapAndOwnContainerTests.DeriveRecordOwnContainer_*` (3 methods) + the hazard pin `ContentResolution_OfANonSecureRecordUnderASecureMatter_IsTheMattersContainer_WhichIsTheHazard`, `Revoke_OfAGrantOnAWorkAssignmentUnderASecureMatter_TouchesNoContainer`, `ProjectClosureCascadeTests.CloseProject_ANonSecureProjectUnderASecureMatter_NeverTouchesTheMattersContainer`, `CloseProject_WhenTheContainerCannotBeDetermined_…("secure-flag-absent")`; `TestRecordContainerResolver.ForNonSecureRecordUnderSecureMatter` / `ForRecordWithNoSecureFlag`. Seed S9 (back to the content question) red. |
| 9 | Revoke had no overlap check (another live grant on the same root still justifies the email-keyed permission); the M2 500 is newly reachable | `RevokeExternalAccessEndpoint.ResolveStillEntitledContactsAsync`: after the revoked grant is deactivated, reads the root's REMAINING active, unexpired grants (`ExternalGrantLifecycle.QueryActiveRowsForRootAsync`, at most `MaxRemainingGrantsPerOverlapCheck` = 200) and the active members of every still-granted organization; a contact they entitle KEEPS the permission (contact path → `NotAttempted`; org path counts `RetainedByOtherGrant` in `SpeOrgMemberCleanupSummary`; all retained → `NotAttempted`). If the remaining grants cannot be read, the removal proceeds for everyone (fail closed toward removing access). Tests: `Revoke_AContactGrant_WhoseContactIsAlsoInAnOrganizationStillGrantedTheRoot_KeepsTheirPermission`, `Revoke_AnOrganizationGrant_KeepsTheMemberWithTheirOwnLiveGrant_AndRemovesTheRest`, `…_WhenEveryMemberIsStillEntitled_AttemptsNothing`, `Revoke_WhenTheOtherGrantHasExpired_ItKeepsNobodysPermission`, `Revoke_WhenTheRemainingGrantsCannotBeRead_RemovesAnyway_FailClosed`, `Revoke_TheRevokedGrantsOwnRowsNeverCountAsAnotherGrant`. Seed S10 red. **M2 500 (decision)**: kept — it is the route's existing documented answer when the SPE cleanup of a SECURE root fails (the grant IS revoked in Dataverse; the 500 tells the caller the container still holds the permission). A 200 would claim a cleanup that did not happen. |
| 10 / 21 | Three Compose defects parked in §11 instead of fixed: refresh-profile body tenant, Load's unbound `documentRecordId`, active-document's unchecked `documentId` | All three FIXED (amendment d, "any other Compose session route with the same flaw"). **refresh-profile**: route is now `POST /api/compose/documents/{documentId:guid}/refresh-profile` (same URL) with `.AddDocumentAuthorizationFilter("write")` — the profile is persisted app-only onto that row, so the caller needs Write on it; tenant = CLAIM (body `tenantId` obsolete, never read, listed in `InboundBodyDtoMappingGuardTests.DeliberatelyUnread`); the "profiled eTag" stamp is written only when the body item IS the authorized row's `sprk_graphitemid` (`ResolveAuthorizedStampItemAsync`). **Load**: `ComposeService.ReadBoundDocumentRowAsync` — a `documentRecordId` is honoured (origin read, profile re-trigger, echo) only when that row's `sprk_graphitemid` is the item just read AS THE CALLER; mismatch / unreadable → dropped (Path B). The same binding guards the replace-save's `DocumentRecordId`. **active-document**: a STORED document is recorded only after its row is read AS THE CALLER (`IDataverseUserClient`, `ComposeActiveDocumentEndpoints.ReadCallerReadableDocumentAsync`); unknown / no Read / read fault / non-GUID are one 404 and the session is unchanged; the recorded SPE pointer is the ROW's (body `speDriveItemId` / `speDriveId` obsolete, never read, `DeliberatelyUnread`); a withdraw reads nothing. Tests: `Seam.Compose.ComposeRefreshProfileSeamTests` (rewritten: 202 through the wire, body without tenant accepted, `WithoutWriteOnTheDocument_IsTheSame403_AndStampsNothing(read-only / unknown)`, `ABodyItemThatIsNotTheRowsItem_IsNeverStamped`), `ComposeOriginRoutingSeamTests.Load_ARecordIdWhoseRowIsTheLoadedItem_IsBound_AndEchoed`, `Load_ARecordIdWhoseRowIsAnotherDocument_IsDropped_NotReadNotEchoed`, `Load_WhenTheRecordRowCannotBeRead_TheRecordIdIsDropped_FailClosed`, `Api.Compose.ComposeActiveDocumentContractTests.PostActiveDocument_AStoredDocument…` / `…AWithdrawOfAStoredDocument_ReadsNoRow` (4 methods, 7 cases). Seeds S5–S8 red. |
| 11 | Positive: deletions confirmed | No change. |
| 12 | PUT pins answers the uniform 404 where criterion F10 says "existing 403" | **Decision, kept** (§7): round 12 item 4's OWNER-COMPARISON basis and goal (3) ("one answer for unknown and denied") apply identically to PUT — the 403 is a pin-existence oracle on the same handler shape. Pinned by `MemoryRecordAndPinAuthorizationContractTests.UpdatePin_AnotherUsersPinAndAnUnknownPin_AreTheSame404_AndNoMatterIsAsked`. Recorded for the owner's reading; not reverted. |
| 13 | Round 21 decides all three escalations; none on the branch | Implemented: items 15, 16, 17 below; §18.1. |
| 14 | Positive: §11 surface small | This round's new surface + justifications: §18.7. |
| 15 | Reporting F14–F16 and amendment (f) unchanged | Implemented round 21 item 2 (§18.5). |
| 16 | Matter → Invoice push fails closed | Implemented round 21 item 3 (§18.6). |
| 17 | F0 second door open | Implemented round 21 item 1 (a)+(b) as the main session specified (§18.3, §18.4); owed (i)–(iii) recorded there. |
| 22 | F12: no route-level "no bearer token → 403", no "invalid entityType asks no privilege" | `Api.Office.OfficeQuickCreateContractTests.Post_QuickCreate_WithNoBearerToken_Returns403_AndCreatesNothing(matter / project / invoice)` — the REAL probe (`CallBase`) answers, proving the filter forwards the caller's (absent) token and the absence denies; `Post_QuickCreate_ForwardsTheCallersOwnBearerTokenToThePrivilegeQuestion`; `Post_QuickCreate_OfAnInvalidEntityType_AsksNoPrivilege_AndKeepsTheHandlers400`; `Post_QuickCreate_OfATypeThisRouteNeverCreates_AsksNoPrivilege(account / contact)`. Seeds S15 / S16 red. |
| 23 | Seeding gaps (save-replace binding, upload fault, pull/reanchor tenant) | Each now has a guard that a seed turned red (S2, S3, S4/S4b) — §18.8. |
| 24 | Publish size not measured | **NOT CLOSED** — §18.10. |
| 25 | /conflict-check not re-run | Re-run this round — §18.9. |

## 18.3 Round 21 item 1 (b): the document-pointer check, and what task 166 still owes

> **SUPERSEDED in r2 (§19.3)** by owner round 23 item 1: the check now also verifies the ITEM (Graph `createdBy`
> against the row's creator) and confines business-unit containers to the document owner's customer subtree; the
> archive is accepted only on the archive path. The site table below still lists every call site (each now passes the
> item id too). The rule text and "0 refused" figures below are r1's.

**The rule** (`RecordContainerResolver.IsDocumentPointerContainerAllowedAsync(documentId, pointerDriveId)`; the
`string` overload refuses an id that is not a GUID; `EnsureDocumentPointerContainerAsync` throws
`SdapProblemException` 409 `document_storage_unverified`):
1. a pointer into a SECURE record's OWN container (the reverse resolution names exactly one secure owner) is honoured
   only for a document that hangs off that record — one of its `DocumentLinkFields.All` lookups names the record or a
   record the resolver resolves to that container, or (one level) its `sprk_parentdocument` does;
2. any other pointer must name one of THIS environment's shared containers: a business unit's `sprk_containerid`, or
   `Communication:ArchiveContainerId`;
3. anything undecidable (ambiguous / indeterminate ownership, any read fault, blank pointer, empty or non-GUID id)
   refuses.

**Why not "equal to the derived container" today** (the main session's answer, 2026-10-04): live dev data — §18.4 —
has 447 of the 530 pointered documents in the UPLOADER's business-unit container (pre-task-076 uploads), not the one
derived for their record; the strict comparison would refuse ~85 % of legitimate downloads. Under the shipped rule,
**0 of the 530** dev documents are refused (both drives in use are business-unit containers; the one secure container
holds no document).

**Wired before every app-only read that follows a `sprk_document` row's pointer** (a refused pointer is never read):

| Site | Route / job | Refusal |
|---|---|---|
| `DataverseDocumentsEndpoints` download | `GET /api/v1/documents/{id}/download` | 409 `document_storage_unverified` |
| `FileAccessEndpoints.GetDownload` / `GetEmlRender` | `GET /api/documents/{documentId}/download`, `…/eml-render` | 409 (SdapProblemException) |
| `DocumentsBulkEndpoints` | `POST /api/documents/bulk-download` | per document: excluded, manifest reason |
| `ChatDocumentEndpoints.IngestArchiveDocumentAsync` | `POST /api/ai/chat/sessions/{sessionId}/documents/from-document` | 409 |
| `DocumentStorageResolver.GetSpePointersAsync` | `GET /api/v1/external/projects/{id:guid}/documents/{documentId:guid}/content` and `…/versions` | 409 (the routes surface the resolver's code) |
| `DocumentCheckoutService` edit / preview URL (an app-only preview link hands out content) | `POST /api/documents/{documentId:guid}/checkout`, `/checkin`, `/discard` | no URL minted (the step is already non-critical) |
| `CommunicationService` send attachments | communication send with `attachmentDocumentIds` | 409, nothing sent |
| `CommunicationService` .eml embed (`sprk_communicationattachment` pointer, verified against its `sprk_document`) | archive | that attachment skipped (embed is best-effort) |
| `AppOnlyAnalysisService.AnalyzeDocumentAsync` + `ExtractDocumentTextAsync` (email + attachments) | profile / email analysis jobs | `Failed` / empty text, summary status Failed |
| `DocumentContextService` app-only branch | chat document context without an HTTP context | no context |
| `FileIndexingService.IndexFileAppOnlyAsync` (a request naming a `DocumentId`) | RAG / bulk / Office indexing jobs | `Failed("Document storage could not be verified")` |
| `InvoiceExtractionJobHandler`, `AttachmentClassificationJobHandler` | finance jobs | Poisoned (permanent), invoice marked Failed |

NOT row-pointer reads, so not checked (recorded so the reviewer can confirm): `UploadFinalizationWorker` and
`OfficeStorageUploader` re-read an item the BFF itself just uploaded into a server-derived container;
`FileIndexingService` requests with NO `DocumentId` (an orphan file the BFF uploaded, or the API-key service route);
every `*AsUserAsync` read (bounded by the caller's own SPE permissions).

**OWED BY TASK 166 under round 21 — the main session runs these in the follow-up round (its 2026-10-04 answer):**
- (i) a BFF pointer-attach endpoint, and every client pointer writer moved to it — `Spaarke.UI.Components`
  `DocumentRecordService.ts` (×2) and `EntityCreationService.ts` (×1), the seven `Create*Wizard`s and the upload PCFs:
  the client creates the row WITHOUT pointers and the BFF stamps them after verifying the derived container
  (ADR-002 WP-3);
- (ii) a legacy migration script (dry run / `-Apply` / `-Verify`) that moves the 447 dev files into their derived
  containers and re-points them via the BFF identity;
- (iii) then `scripts/Set-DocumentPointerFieldSecurity.ps1 -ClientNoLongerWritesPointers -Apply` and `-Verify`, and the
  switch of this check to the strict derived-container comparison.

## 18.4 Live read-only facts (spaarkedev1, 2026-10-04; Dataverse MCP SQL and the script's dry run; no write)

| Fact | Value |
|---|---|
| Business-unit containers | `Spaarke` (root) and `Spaarke Business Unit 1` → `b!vzGD…` (shared); `Spaarke Demo` → `b!yLRd…`; `Secure Record`, `Spaarke Dev 1`, `Spaarke Test 1` → none |
| Active `sprk_document` by drive × owning BU | `b!yLRd…`: 416 (Spaarke) + 31 (BU 1) = **447**; `b!vzGD…`: 78 (Spaarke) + 5 (BU 1) = **83**; no pointer: 12. Total active 542, pointered 530. |
| Secure records with their own container | 1 project (`65a3fab2`, container `b!MVas…`); 0 secure matters / work assignments; **0** documents point into `b!MVas…` |
| Pointer-check outcome on today's data | all 530 pointered documents honoured (both drives are business-unit containers); 0 refused |
| `fieldpermission` on `sprk_graphdriveid` / `sprk_graphitemid` | **0** rows |
| FLS script dry run | p1 (both task-133 profiles exist, in SpaarkeCore), p2 (reader on all six BU default teams), p3 (writers = `# mi-bff-api-dev`, `SDAP-BFF-SPE-API`), p5 (no field-mapping rule / topic registry / email update field targets the columns), p6 (`sprk_document` root component, behavior 0): all **OK**; p4 (`-ClientNoLongerWritesPointers`) required for `-Apply`; WOULD secure both columns and grant reader / writer. Nothing written. |
| `sprk_report` rows | **0** |
| Parent lookups to `sprk_matter` (metadata, read earlier this round) | `sprk_workassignment`, `sprk_event`, `sprk_reportcard`: exactly one each (`sprk_regardingmatter`); `sprk_invoice`: exactly one (`sprk_matter`) |

## 18.5 Reporting (round 21 item 2): option A completed

- **Contract**: every route that names a report takes the `sprk_report` ROW id. The row is read AS THE CALLER
  (`IDataverseUserClient`, `ReportingEndpoints.ReadCatalogRowAsync` / `CatalogRowPath`); absent, unreadable, OBO
  failure, fault, or a row without a usable `sprk_pbi_reportid` + `sprk_workspaceid` are ONE 404
  (`sdap.reporting.deny.report_not_in_catalog`). Power BI report and workspace ids are derived from that row; the client
  never sends a workspace id. No app-only fallback.
- **Routes** (`/api/reporting`, group `RequireAuthorization()` + `ReportingAuthorizationFilter` unchanged):
  `GET /embed-token?reportId=` (row → PBI ids; RLS identity; 503 `ErrorCodeRlsIdentityUnavailable` when the caller's
  business unit cannot be read — never a token without it); `GET /reports` (the caller's own catalog read; a read
  failure is 502, never an empty list); `GET /reports/{reportId:guid}`; `POST /reports` (clone from a readable source
  row into the SOURCE's workspace, or register a Save-As `PbiReportId` only if it is in the source's workspace; the row
  is created AS THE CALLER; if the caller may not create it, the clone is deleted — compensating); **`PATCH
  /reports/{reportId:guid}`** (the verb the client sends; `PUT` is no longer mapped; the row is written as the caller);
  `DELETE /reports/{reportId:guid}` (row deleted as the caller FIRST, then the PBI report — a caller who may not delete
  the row deletes nothing); `POST /export` (row → derived report; returns the file).
- **RLS**: `EffectiveIdentity` username = the caller's business unit id (lowercase "D") from WhoAmI issued AS THE
  CALLER, role `BusinessUnitFilter` (design.md's DAX `LOOKUPVALUE(…, USERNAME())`). The `businessunit` / `bu` claim
  is no longer read (no token carries it).
- **Client** (`src/solutions/Reporting`): `reportingApi.ts` (row ids only; create `{name, sourceReportId}`, save-as
  `{name, sourceReportId, pbiReportId}`; export returns the file; `expiry`), `useEmbedToken.ts`, `ExportButton.tsx`
  (no polling; downloads the returned file), `NewReportButton.tsx` (clones the selected report), `ReportingToolbar.tsx`,
  `SaveControls.tsx` (captures the saved report's PBI id). `npm run build` (vite) green; prettier clean; the package has
  no test runner. `tsc` reports 8 pre-existing errors in untouched files (`ReportDropdown`, `ReportViewer`,
  `globalStyles`).
- **Seam**: `ReportingEmbedService` is unsealed with virtual members (the only seam; no `Mock<HttpMessageHandler>`).
- **Tests**: NEW `Api.Reporting.ReportingCatalogBindingContractTests` (21 methods: per route — unbindable row is the
  uniform 404 and Power BI is never asked; unreadable ≡ absent byte-for-byte; readable row → derived ids + BU RLS
  identity; no BU → no token; list = the caller's read, failure ≠ empty; create / save-as / compensating delete;
  viewer refused before any read; PATCH; delete order). Re-based: `ReportingEndpointsTests` (DTO shapes),
  `ReportingProfileManagerTests` (sealed-class test → virtual-seam theory), Spe `ReportingEndpointTests` (five
  workspaceId tests removed with the parameter; PATCH). **Deleted**: Spe `ReportingRlsTests.cs` — it tested the
  claim-based BU extraction that round 21 replaced (no token carries that claim); its replacement is the server-computed
  identity tests above. Seeds S12–S14 red.
- **The seven reporting Pending waivers resolve** — ledger rows §18.11.

## 18.6 Field-mapping push (round 21 item 3)

`FieldMappingEndpoints.ResolveParentLookupAsync`: reads `EntityDefinitions(LogicalName='{target}')/ManyToOneRelationships`
(`$select=ReferencingAttribute,ReferencedEntity`) AS THE CALLER (impersonated query, the route's existing mechanism),
keeps the lookups whose `ReferencedEntity` is the source, and requires EXACTLY one: none → 409
`field_mapping_parent_lookup_missing`, several → 409 `field_mapping_parent_lookup_ambiguous`, both with a clear
detail and NO child read or write. The child filter is `_{attribute}_value eq {id}` (one `_value` wrap). The
`sprk_regarding{base}` convention (`DetermineParentLookupField`) is deleted. Entity names are validated as logical
names (400 otherwise, nothing asked). Matter → Invoice resolves to `sprk_invoice.sprk_matter`; the other three
profiles resolve to `sprk_regardingmatter`. Tests: `Push_ToATargetWhoseLookupIsNotTheRegardingConvention_FindsItFromMetadata_MatterToInvoice`,
`Push_ToTheConventionalTarget_ResolvesTheSameLookupFromMetadata`, `Push_WhenMetadataNamesNoneOrSeveralLookupsToTheSource_Is409_AndReadsAndWritesNoChild(0 / 2)`,
`Push_WithANonLogicalNameEntity_Is400_AndNothingIsAsked`. Route `.Produces(409)`. Seed S11 red.

## 18.7 Placement (CLAUDE.md §10) and component justification (§11) — this round

Placement: **in BFF, existing routes only** — no new endpoint, service, DI registration, package, option, job or
column. The changes are checks inside existing routes and services, plus new constructor dependencies on the
already-registered (unconditional, Program.cs) `RecordContainerResolver` and `IDataverseUserClient`. One route
changed verb (reporting `PUT` → `PATCH`, the verb the shipped client sends). Publish size: §18.10.

New surface, three questions each:
- **`RecordContainerResolver.ResolveOwnContainerAsync`** (internal) — Existing: `ResolveForRecordAsync` (grep: no
  other own-container question). Extension: changing `ResolveForRecordAsync` would break content placement (task 155
  needs the ancestor answer there). Cost of nothing: closure / revoke strip grantees from a secure ancestor's
  container (item 8).
- **`IsDocumentPointerContainerAllowedAsync` (+ `string` overload) / `EnsureDocumentPointerContainerAsync` /
  `DocumentStorageUnverifiedCode`** — Existing: `ResolveOwningRecordAsync` (reverse) and the business-unit read; the
  new methods compose them; the link vocabulary is the canonical `DocumentLinkFields` (a private copy was caught by
  `DocumentLinkVocabularyGuardTests` and removed). Extension: they ARE the extension of the resolver, the one owner of
  container decisions (WP-1). Cost of nothing: any Write holder re-points a row at another container and downloads it
  as the managed identity (round 21 item 1).
- **`ComposeSaveTelemetry.CauseSessionUnavailable`**, **`ComposeSaveEndpoints.SessionUnavailableCode`** — Existing:
  `StorageFailed` outcome (reused); no cause named the session store. Cost of nothing: an unobservable 503 class.
- **`ComposeActiveDocumentEndpoints.CallerDocumentReadPath` / `ReadCallerReadableDocumentAsync`**,
  **`ComposeDocumentEndpoints.ResolveAuthorizedStampItemAsync`**, **`ComposeService.ReadBoundDocumentRowAsync`** —
  modifications of existing handlers, extracted so each decision is pinned by a test.
- **`RevokeExternalAccessEndpoint.ResolveStillEntitledContactsAsync`**, `MaxRemainingGrantsPerOverlapCheck`,
  **`SpeOrgMemberCleanupSummary.RetainedByOtherGrant`** (additive, default 0) — Existing: `QueryActiveRowsForRootAsync`
  and `QueryActiveMembersAsync` (reused). Cost of nothing: item 9.
- **Field-mapping `ParentLookupMetadataPath` / `ParentLookupMetadataQuery` / `IsLogicalName` / two reason codes** —
  replace the deleted convention. Cost of nothing: item 16.
- **Reporting `CatalogRow`, `ReportCatalogItem`, `CreateReportResponse`, `RlsRoleName`, `ReportNotInCatalogReasonCode`**
  — the catalog-row contract; the old DTOs were reshaped, not duplicated. Cost of nothing: item 15.
- **`scripts/Set-DocumentPointerFieldSecurity.ps1`** — Existing: task 150's `Set-SecureFlagFieldSecurity.ps1` (the model;
  it secures a different column and has no client-writer precondition). Extension: parameterizing 150's script would
  change a shipped, verified operator tool. Cost of nothing: round 21 item 1 (a) has no apply path.
- Test-only: `TestRecordContainerResolver.ForBusinessUnitContainers` / `ForDocumentPointerWorld` /
  `ForNonSecureRecordUnderSecureMatter` / `ForRecordWithNoSecureFlag`, `RefreshProfileSeamFixture` — the REAL resolver
  over its two interface seams (it is sealed, ADR-010), shared by the three test assemblies.

ADR-002: no plugin. ADR-003: every new check fails closed (§18.2). ADR-038: no `Mock<HttpMessageHandler>`, no
DI-registration or ctor-null tests.

## 18.8 Seeding (one seeded build, 2026-10-04; restored from byte copies, files touched)

19 seeds in disjoint code (`scratchpad/seed166r1.py`); **62 of 369** targeted tests went red, each naming its guard:

| Seed | Red tests (examples) |
|---|---|
| S1 save fault → unbound | `Save_ASessionStoreFault_RefusesTheSave_503_AndWritesNothing` (replace, create-on-save) |
| S2 replace save forwards the body session | `Save_AnotherUsersSession_RunsUnbound_TheServiceNeverSeesTheirSession(replace)` |
| S3 upload fault → rethrow | `Upload_ASessionStoreFault_IsTheUniform404_NotA500_AndReadsNothing` |
| S4 / S4b pull / reanchor `tid` 401 removed | `AnnotationRoute_ATokenWithNoTidClaim_Is401_AndDownloadsNothing(pull / reanchor)` |
| S5 refresh-profile filter removed | `RefreshProfile_WithoutWriteOnTheDocument_IsTheSame403_AndStampsNothing(read-only / unknown)` |
| S6 refresh-profile stamps any item | `RefreshProfile_ABodyItemThatIsNotTheRowsItem_IsNeverStamped` |
| S7 Load binds any record id | `Load_ARecordIdWhoseRowIsAnotherDocument_IsDropped_NotReadNotEchoed` |
| S8 active-document records the body pointer unread | `PostActiveDocument_AStoredDocumentTheCallerCanRead_RecordsTheRowsPointer_NotTheBodys`, `…CannotRead_IsTheUniform404…(4 shapes)` |
| S9 own container → content (ancestor) container | `CloseProject_ANonSecureProjectUnderASecureMatter_*`, `CloseProject_…("secure-flag-absent")`, `DeriveRecordOwnContainer_*` (3), `Revoke_OfAGrantOnAWorkAssignmentUnderASecureMatter_*` |
| S10 overlap check removed | `Revoke_AContactGrant_WhoseContactIsAlsoInAnOrganization…`, `Revoke_AnOrganizationGrant_KeepsTheMember…`, `…WhenEveryMemberIsStillEntitled_AttemptsNothing` |
| S11 parent lookup by naming convention | `Push_ToATargetWhoseLookupIsNotTheRegardingConvention_*`, `Push_WhenMetadataNamesNoneOrSeveralLookups…(0 / 2)` |
| S12 unreadable catalog row → client id used as the PBI id | `EmbedToken_/Export_/GetReport_ForARowTheCallerCannotBindTo_*` (3 shapes each), `Update_OfAnUnreadableRow_*`, `Delete_OfAnUnreadableRow_*` |
| S13 RLS identity not the business unit | `EmbedToken_ForAReadableRow_UsesTheRowsPowerBiIds_AndTheCallersBusinessUnitAsTheRlsIdentity` |
| S14 update back to PUT | `Update_IsPatch_TheVerbTheClientSends_AndWritesTheRowAsTheCaller` |
| S15 a stand-in token for a caller with none | `Post_QuickCreate_WithNoBearerToken_Returns403_AndCreatesNothing(matter / project / invoice)` |
| S16 invalid entity type asks a privilege | `Post_QuickCreate_OfAnInvalidEntityType_AsksNoPrivilege_*`, `…OfATypeThisRouteNeverCreates_AsksNoPrivilege(account / contact)` |
| S17 pointer check always allows | `DocumentPointerContainerCheckTests` refusal cases (8), `IngestFromDocument_WhenArchivePointerNamesAContainerTheDocumentMayNotUse_Returns409AndNeverDownloads`, `IndexFileAppOnlyAsync_PointerIntoAContainerTheDocumentMayNotUse_*`, `ArchiveExistingAsync_WhenAnAttachmentPointerCannotBeVerified_EmbedsOnlyTheVerifiedOnes` |
| S18 non-GUID text id followed (with S17) | `ATextDocumentIdThatIsNotAGuid_IsRefused(null / "not-a-document-id")`, `IndexFileAppOnlyAsync_DocumentIdThatIsNotADocument_*` |

Restored: no `SEED-166` marker remains in `src/` (Grep).

## 18.9 Gates (this round, final code)

| Gate | Result |
|---|---|
| Affected tests (before the final runs) | Download / AI / Communication / Office / Checkout set: 2,443 passed, 13 skipped, 0 failed (then the BulkDownload fixture was given the resolver); active-document + pointer-check + fixture set: 86 passed, 0 failed; the Compose / ExternalAccess / FieldMapping / Reporting sets earlier this round: green |
| Full BFF unit suite (`tests/unit/Sprk.Bff.Api.Tests`) | **14,464 total: 14,410 passed, 54 skipped, 0 failed** (13 m 51 s). An earlier full run on interim code had one failure, `PinnedMemoryEndpointsContractTests.DeletePin_Authenticated_Returns204AndEmitsCounter` (27 s, a metric-counter wait under suite contention); it passed on an isolated re-run (1/1) and in this final run. |
| NetArchTest (`tests/Spaarke.ArchTests`) | **346 passed, 0 failed** (after replacing a private `sprk_document` link list with the canonical `DocumentLinkFields` — `DocumentLinkVocabularyGuardTests` caught it) |
| Sprk.Bff.Api.IntegrationTests (full) | **104 passed, 0 failed** |
| Spe.Integration.Tests (full) | **405 total: 380 passed, 25 skipped (pre-existing environment-gated `SkippableFact`s), 0 failed** — 23 fewer cases than 18a6aebdf: the deleted `ReportingRlsTests` and the removed workspaceId cases of `ReportingEndpointTests` (§18.5) |
| BFF build | succeeded (warnings as errors) |
| `dotnet list package --vulnerable --include-transitive` | `Sprk.Bff.Api` has no vulnerable packages |
| Reporting client | `npm run build` green; prettier clean; no test runner in the package |
| /conflict-check (re-run 2026-10-04) | 26 open PRs × 64 changed files: ONE overlap — #1120 `chore/remove-dead-shared-code` edits doc comments in `SprkChatAgentFactory.cs` around lines 1502–1751; this branch changes one call at ~2280 (no hunk overlap). Master since the merge base (`origin/master` @ `62277d50a`): ONE overlap — `tests/integration/contract/Api/Office/OfficeQuickCreateContractTests.cs` (this branch appends tests; a textual merge at integration). Hot path BFF is shared with other active worktrees (`projects/INDEX.md`): soft warn, no hard conflict. |

## 18.10 Not closed

- **Item 24 — publish size not measured.** The harness for this round says "Skip publish-size measurement". No
  master-vs-branch comparison was made, so there is no delta or file count. Risk: no package and no DI registration was
  added; the diff adds code inside existing types and one script. The main session measures it at integration with the
  CLAUDE.md §10 fresh-worktree procedure.
- **Round 21 item 1 steps (i)–(iii)** are OWED BY TASK 166 and are run by the main session in the follow-up round, per
  its 2026-10-04 answer (§18.3). They are listed here so nothing reads this round as having applied the FLS lock or the
  strict check.

## 18.11 Route authorization ledger input — r1 rows (route key as the guard spells it)

| Route key | Mechanism that now decides | Deny test (FQN) |
|---|---|---|
| `GET /api/reporting/embed-token` | handler: `sprk_report` row read AS THE CALLER (uniform 404) + server-computed BU RLS identity | `Sprk.Bff.Api.Tests.Api.Reporting.ReportingCatalogBindingContractTests.EmbedToken_ForARowTheCallerCannotBindTo_IsTheUniform404_AndPowerBiIsNeverAsked` |
| `POST /api/reporting/export` | handler: row read as the caller | `…ReportingCatalogBindingContractTests.Export_ForARowTheCallerCannotBindTo_IsTheUniform404_AndPowerBiIsNeverAsked` |
| `GET /api/reporting/reports/{reportId:guid}` | handler: row read as the caller | `…ReportingCatalogBindingContractTests.GetReport_ForARowTheCallerCannotBindTo_IsTheUniform404` |
| `GET /api/reporting/reports` | handler decision: query runs as the caller | `…ReportingCatalogBindingContractTests.GetReports_ListsOnlyWhatTheCallersOwnReadReturns` |
| `POST /api/reporting/reports` | handler: source row read as the caller; row created as the caller (compensating delete) | `…ReportingCatalogBindingContractTests.Create_FromAnUnreadableSource_IsTheUniform404_AndCreatesNothing` |
| `PATCH /api/reporting/reports/{reportId:guid}` (REPLACES `PUT /api/reporting/reports/{reportId:guid}`, which is no longer mapped) | handler: row read and written as the caller | `…ReportingCatalogBindingContractTests.Update_OfAnUnreadableRow_IsTheUniform404_AndWritesNothing` |
| `DELETE /api/reporting/reports/{reportId:guid}` | handler: row deleted as the caller first | `…ReportingCatalogBindingContractTests.Delete_WhenTheCallerMayNotDeleteTheRow_TheReportIsNeverDeleted` |
| `POST /api/compose/documents/{documentId:guid}/refresh-profile` (route parameter renamed from `{documentRecordId:guid}`; same URL) | `DocumentAuthorizationFilter("write")` + claim tenant + stamp bound to the row's item | `Sprk.Bff.Api.Tests.Seam.Compose.ComposeRefreshProfileSeamTests.RefreshProfile_WithoutWriteOnTheDocument_IsTheSame403_AndStampsNothing` |
| `GET /api/compose/documents/{documentSpeId}` (Load) | handler: OBO item read; `documentRecordId` bound only to that item's row | `Sprk.Bff.Api.Tests.Seam.Compose.ComposeOriginRoutingSeamTests.Load_ARecordIdWhoseRowIsAnotherDocument_IsDropped_NotReadNotEchoed` |
| `POST /api/compose/active-document` | handler: body session owner + stored document read AS THE CALLER | `Sprk.Bff.Api.Tests.Api.Compose.ComposeActiveDocumentContractTests.PostActiveDocument_AStoredDocumentTheCallerCannotRead_IsTheUniform404_AndTheSessionIsUnchanged` |
| `POST /api/compose/document/{documentSpeId}/pull-annotations`, `…/reanchor-annotations` | handler: claim tenant (401 without `tid`), OBO read | `Sprk.Bff.Api.Tests.Api.Ai.ComposeWordShuttlePollEndpointContractTests.AnnotationRoute_ATokenWithNoTidClaim_Is401_AndDownloadsNothing` |
| `POST /api/compose/documents/{documentSpeId}/save`, `POST /api/compose/documents/create-on-save` | as §9, plus: a session-store fault refuses (503) | `Sprk.Bff.Api.Tests.Integration.Compose.ComposeBodySessionRouteDenyContractTests.Save_AnotherUsersSession_RunsUnbound_TheServiceNeverSeesTheirSession` |
| `POST /api/compose/upload` | as §9 (route-level tests added) | `Sprk.Bff.Api.Tests.Integration.Compose.ComposeBodySessionRouteDenyContractTests.Upload_AnotherUsersSession_UnderEverySpelling_IsTheUniform404_AndReadsNothing` |
| `POST /api/v1/field-mappings/push` | as §9 + parent lookup from metadata (409 when none / several) | `Sprk.Bff.Api.Tests.Api.FieldMappings.FieldMappingPushAuthorizationContractTests.Push_WhenMetadataNamesNoneOrSeveralLookupsToTheSource_Is409_AndReadsAndWritesNoChild` |
| `POST /api/v1/external-access/revoke`, `POST /api/v1/external-access/close-project` | as §9; container = the record's OWN only; revoke keeps grantees another live grant entitles | `Sprk.Bff.Api.Tests.AccessControl.RevokeGrantOverlapAndOwnContainerTests.Revoke_OfAGrantOnAWorkAssignmentUnderASecureMatter_TouchesNoContainer` |
| download routes of §18.3 | existing authorization unchanged + the document-pointer check (server invariant) | `Sprk.Bff.Api.Tests.Api.Ai.ChatDocumentEndpointsContractTests.IngestFromDocument_WhenArchivePointerNamesAContainerTheDocumentMayNotUse_Returns409AndNeverDownloads`; `Sprk.Bff.Api.Tests.AccessControl.DocumentPointerContainerCheckTests` |

The §9 reporting row ("UNCHANGED — STOPPED") is superseded by the rows above: **delete all seven reporting Pending
waivers owned by 166**, the `PUT` key becoming the `PATCH` key. `RouteAuthorizationGuardTests` GovernedFile reasons for
`ComposeDocumentEndpoints.cs` and `ComposeActiveDocumentEndpoints.cs` were updated in this diff (already governed);
`InboundBodyDtoMappingGuardTests.DeliberatelyUnread` gained `RefreshProfileBody.TenantId` and
`ComposeActiveDocumentRequest.SpeDriveItemId` / `.SpeDriveId`, each with its reason.

## 18.12 Manual live gates — r1 (main session, dev; non-admin test users; ids redacted to 8 chars)

10. Deploy the BFF and the Reporting web resource from this branch.
11. Reporting (round 21 item 2): seed one `sprk_report` row (catalog is empty) with a real `sprk_pbi_reportid` /
    `sprk_workspaceid`; as a user who can read it, open it → embed token issued, data filtered to the user's business
    unit (the model's `BusinessUnitFilter` role must exist with the design.md DAX — confirm in the PBI service); as a
    user who cannot read the row → 404, no token. Rename a report (PATCH) and delete one as a user without Delete on
    the row → 403/404 and the PBI report still exists.
12. Field-mapping push from a matter on the "Matter to Invoice (Attorney Matrix)" profile → child invoices updated
    (impersonated); the other three profiles unchanged.
13. Compose: a save while Redis is unavailable → 503, nothing written; refresh-profile by a user without Write on the
    row → 403; active-document with a document the user cannot read → 404.
14. Revoke a contact grant on a secure test project where the same contact is also a member of an organization still
    granted that project → the container permission is kept (`RetainedByOtherGrant` / `NotAttempted`).
15. ~~Pointer check: a download of any existing dev document still works (0 of 530 refused, §18.4);~~ **STALE —
    corrected in f1 (§20.2 item 11).** "0 of 530 refused" was true of the r1 rule only; r2's round-23 rule refuses
    whole classes of dev documents by design (§19.3 "Expected effect"), and f1's root-owned rule (round 25 item 6)
    refuses more (§20.5). The expected counts are now gate 17 (§19.12) and gate 22 (§20.11), not zero. The second half
    stands: a test row whose `sprk_graphdriveid` is set (by an administrator, in a scratch row) to a drive id no business
    unit owns → 409 `document_storage_unverified`, no bytes.
16. Round 21 item 1 (a) — ONLY after the owed steps (i) and (ii) (§18.3):
    `pwsh scripts/Set-DocumentPointerFieldSecurity.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -ClientNoLongerWritesPointers -Apply`,
    then the same with `-Verify` (exit 0).

No `.claude` edit is needed for this round (§12's edit stands).

# r2 (2026-10-04): verifier findings on `582a4b42a` closed, owner round 23 implemented

> **Branch**: `task/uac-r2-166-r2` from `task/uac-r2-166-r1` @ `582a4b42a`
> **Inputs**: the 20-item adversarial verification of `582a4b42a`; **owner round 23** (work branch `bfafcc04e`,
> BINDING — the main session's decisions on r1's two open questions, under the round-15 standing directive; also
> delivered as an uncommitted `NOTE-FROM-MAIN.md`, deleted before commit); rounds 1–12 and 21 as before.
> **Outcome**: every finding is closed in code and tests, or proven and recorded (§19.2), EXCEPT the round 21 item 1
> steps (i)–(iii), which round 23 assigns to the follow-up round (§19.10). Publish size: the verifier's own measurement
> is recorded (§19.9); this run was told to skip it.

## 19.1 Round 23 as applied

| Round 23 item | Decision (binding) | Implemented here |
|---|---|---|
| 1 — interim document-pointer check | Verify the ITEM (driveItem `createdBy` = the row's creator — `createdby` when human, else `sprk_createdbyperson` — or the BFF identity for BFF-created rows) AND accept only containers in the document owner's business unit or its customer's subtree (archive only on the archive path); unverifiable fails closed; residual exposure stated | §19.3 |
| 2 — reporting Save-As | Remove the `pbiReportId` registration path (view-only tokens cannot create a report; it only aliased); keep the server-side clone; DELETE removes the Power BI report only for an `iscustom` row no other catalog row references | §19.4 |

## 19.2 The 20 findings — what changed per item

| # | Finding | Closure |
|---|---|---|
| 1 | Positive: independent runs match | No change. This round's runs: §19.9. (The lone `PlaybookByNameDeprecationTests` failure the verifier saw is in a file this task does not touch; see §19.9 for this round's integration runs.) |
| 2 (a) / 19 | `ContainerDocumentAuthorizationFilter` calling `next()` for a missing query `containerId` turned nothing red (the handler answers the same 400) | NEW `AccessControl.ContainerDocumentListAuthorizationTests.QuerySource_AMissingContainerId_IsTheFiltersOwn400_AndNeverReachesTheHandler(null / "" / "   ")` drives the REAL filter with a recording `next` and recording Dataverse doubles: the answer is the filter's own 400 with the exact detail, the handler is never reached, no resolver / Dataverse / access-data call. The route-level test cannot see the difference by design (identical 400), so the filter contract is pinned at the filter. Seed S8 (`return await next(context)`) → 3 red. |
| 2 (b) | Removing `ReadCatalogRowAsync`'s `RowId == requested id` check turned nothing red | NEW shape `row-of-another-id` in `ReportingCatalogBindingContractTests.UnboundRowShapes` (a well-formed row with usable Power BI ids but another `sprk_reportid`): embed-token, export and GET each answer the uniform 404 and Power BI is never asked. Seed S7 → 3 red. |
| 3 / 20 | Publish size not measured by the task | Recorded from the verifier's independent measurement (§19.9): `e6dd48b43` 45.657 MB vs `582a4b42a` 45.675 MB = **+0.018 MB**, Compress-Archive Optimal, PDBs included, 212 / 212 files, short-path fresh worktrees, zips in the scratchpad (CLAUDE.md §10 hazard four reproduced). This run's own measurement was skipped on the harness instruction; r2 adds no package and no DI registration. |
| 4 / 15 | HIGH: `POST /api/reporting/export` ran `ExportToFileInGroupAsync` with no effective identity | `ExportReport` now reads the caller's business unit AS THE CALLER (WhoAmI, the embed path's `ReadCallerBusinessUnitAsync`) and refuses with the embed path's 503 `sdap.reporting.rls.identity_unavailable` when it cannot (the route now `.ProducesProblem(503)`); `ReportingEmbedService.ExportReportAsync(…, string username, IList<string> roles, …)` requires the identity (throws without it), reads the report's dataset from Power BI (as the embed path does) and builds the request through `BuildExportRequest`, which puts `PowerBIReportConfiguration.Identities = [EffectiveIdentity{Username = business unit, Roles = [BusinessUnitFilter], Datasets = [dataset]}]` in the request and refuses a blank username, no roles or no dataset. Tests: `Export_ForAReadableRow_ExportsTheDerivedReport_UnderTheCallersBusinessUnitRlsIdentity`, `Export_WhenTheCallersBusinessUnitCannotBeRead_ExportsNothing`, `ExportRequest_CarriesTheEffectiveIdentity_OnTheReportsDataset`, `ExportRequest_WithoutACompleteIdentity_IsNeverBuilt` (3 cases). Seeds S1 / S1b red. |
| 5 / 16 | MEDIUM: Save-As registration aliased any report in the source's workspace; DELETE then destroyed it app-only | Round 23 item 2: `CreateReportRequest.PbiReportId` is DELETED (a client that still sends it is ignored by System.Text.Json and gets a clone); `CreateReport` ALWAYS clones the SOURCE row's report; `GetReportAsync` is no longer called by any route. `DeleteReport` deletes the catalog row as the caller first (unchanged), then deletes the Power BI report ONLY when the row is `sprk_iscustom` AND no other catalog row (active or inactive) references its `sprk_pbi_reportid` — an APP-ONLY existence read (`IsReferencedByAnotherCatalogRowAsync`, a server invariant: a caller-scoped read would miss rows the caller cannot see); a standard report, a still-referenced report, or an unanswerable reference question KEEPS the report (204, logged). Client: `SaveControls` Save As now calls `createReport({name, sourceReportId})` (a server clone); `saveAsReport` / `SaveAsReportRequest` are deleted from `reportingApi.ts`; the unused `workspaceId` prop is removed from `SaveControls` / `ReportingToolbar` / `App`. Tests: `Create_WithAClientNamedPowerBiReport_ClonesTheSource_AndNeverRegistersTheNamedReport`, `Delete_OfACustomRowNoOtherRowReferences_DeletesTheRowAsTheCallerFirst_ThenTheDerivedReport` (also pins the app-only query's two conditions), `Delete_KeepsThePowerBiReport_UnlessItIsACustomRowsUnreferencedReport(standard-row / referenced-by-another-row / reference-check-throws)`, `ReportingEndpointsTests.CreateReportRequest_HasExpectedProperties` (`PbiReportId` absent). Seeds S2, S4, S5, S6 red. |
| 6 | LOW-MEDIUM: Create cloned app-only before knowing the caller may create the row | Round 9 write pattern: `CreateReport` asks `CallerRecordAccessProbe.CallerHoldsPrivilegeAsync(caller token, "prvCreatesprk_Report")` AS THE CALLER after the input 400s and BEFORE the source read and the clone; false / throw / no token → 403 (`sdap.reporting.deny.insufficient_privilege`), nothing read, nothing cloned. The constant is the live-verified name (§19.6) and is pinned by `CreateReportPrivilege_IsTheLiveVerifiedName`. The compensating delete stays for a row create that fails for another reason (it now always runs, since every create is a clone). Test: `Create_WithoutTheCallersCreatePrivilege_IsRefused_BeforeAnyReadOrClone(denied / throws)` (asserts the caller's own token is forwarded). Seed S3 red. |
| 7 / 17 | HIGH: the pointer check verified the drive only and accepted any business-unit or the archive container; the deviation rested on an unrecorded answer | Round 23 item 1 implemented (§19.3), and the decision IS now recorded (round 23, `bfafcc04e`). The check verifies the ITEM (Graph `createdBy`) against the row's creator and confines business-unit containers to the document OWNER's customer subtree (Model 1: another customer's container is refused); the archive is accepted only on the archive path. The misleading r1 comment is replaced. Every call site now passes the item id it is about to read. 33 test methods (39 cases) in `DocumentPointerContainerCheckTests` (rewritten) + a route-level item test. Seeds S10–S15 red. |
| 8 / 18 | FLS on the pointer columns not applied; prerequisites (i) pointer-attach endpoint, (ii) legacy migration not built; POML said `completed` | NOT built in this round, BY BINDING DECISION: round 23 makes the item-verifying check the interim rule "until round 21 item 1's BFF pointer-attach path, legacy migration of the 447 dev files, FLS apply and strict derived-container check land in the follow-up round". The record is corrected: the POML status is now `completed-with-escalation` (the project's existing value for "code done, an owner-decided item still open"), and §19.10 lists (i)–(iii) as owed by task 166. |
| 9, 10, 11 | Positive: deletions, forbidden-change criteria, covered deviations verified | No change. r2 again leaves `src/server/shared/Spaarke.Dataverse/**`, `EntityAccessFilter`, `SemanticSearchAuthorizationFilter` and `OperationAccessPolicy` untouched (diff). |
| 12 | LOW: `scripts/Seed-TypedHandlers.ps1:220` still names the deleted `GET /api/workspace/state` | Comment corrected: the kept read path is the `SprkChatAgentFactory` workspace-state prompt block; the route was deleted by task 166 (round 10 item 1). |
| 13 | LOW: the push read the source's mapped fields app-only after a row-level Read check (FLS bypass) | The source's mapped fields are now read AS THE CALLER through the route's existing impersonated seam (`IImpersonatedCommunicationQuery.QueryAsync(entity set, "$select={fields},{source}id&$filter={source}id eq {id}&$top=1", caller)`), converted exactly as the app-only read converted them. A field-secured column the caller cannot read comes back null, so its rule is skipped and nothing is copied; no row → the uniform 404. A rule field that is not a logical name is never interpolated. `IFieldMappingDataverseService.RetrieveRecordFieldsAsync` is no longer called by the handler; `DataverseWebApiService.cs` / `IFieldMappingDataverseService.cs` are unchanged. Tests: `BuildSourceRecordQuery_SelectsTheMappedFieldsOfExactlyTheAuthorizedRow`, `Push_ReadsTheSourceFieldsAsTheCaller_NeverAppOnly`, `Push_ASourceColumnTheCallerCannotReadUnderFieldSecurity_IsNeverCopiedIntoAChild`, `Push_WhenTheCallersSourceReadReturnsNoRow_IsTheUniform404_AndNoChildIsReadOrWritten`. Seed S9 red. |
| 14 | Integration note (OfficeQuickCreateContractTests textual merge; 167 not landed) | No change; r2 does not touch that file. /conflict-check re-run (§19.9). |

## 19.3 Round 23 item 1: the interim document-pointer check

`RecordContainerResolver.IsDocumentPointerContainerAllowedAsync(documentId, driveId, itemId)` (+ the `string` id
overload and `EnsureDocumentPointerContainerAsync(…, itemId)`, 409 `document_storage_unverified`), now in
`Infrastructure/Dataverse/RecordContainerResolver.DocumentPointer.cs` — the SAME sealed type split into a `partial`
file by reason-to-change (CLAUDE.md §11.5); no new type, no registration change. Both halves must hold; anything
undecidable refuses:

1. **The CONTAINER.**
   - (a) a SECURE record's own container: only for a document hanging off that record (unchanged from r1);
   - (b) the communication archive (`Communication:ArchiveContainerId`): only on the ARCHIVE PATH — the row carries
     `sprk_relatedcommunication` = C (`CrossPathLink.LinkedCommunicationAttribute`, stamped by every archive writer)
     AND the item's name is `{C:N}_…` (every archive upload — `CommunicationService`, `IncomingCommunicationProcessor`,
     `MessageAttachmentMaterializer` — is named so): the item is the archive's record of THAT communication;
   - (c) otherwise a business unit's container, and only one stamped by a unit in the DOCUMENT OWNER's customer
     subtree: walk the owner's `owningbusinessunit` up to the unit directly under the root (the customer), then that
     unit and every descendant (`SpeAdminTenantScope.CollectSelfAndDescendants`, reused). A root-owned row's subtree
     is the whole environment (the operator level — the `SpeAdminTenantScope` precedent; root ownership is the dev
     artifact of owner #1081). Unknown unit, broken chain, cycle, a hierarchy larger than one read → refuse.
   - Because the archive can ALSO be a business unit's container (on dev it IS "Spaarke Demo"'s, §19.6), (b) and (c)
     are alternatives: either may admit a pointer, neither widens the other.
2. **The ITEM.** `ISpeFileOperations.GetItemCreatorAsync(drive, item)` — an app-only, UNCACHED Graph read of
   `id,name,createdBy` (null when the item is not in that drive) — then:
   - the row's creator is `createdby` when that systemuser has no `applicationid` (a person), else the person in
     `sprk_createdbyperson` (read in its OWN query; a missing column, as on dev today, makes only this answer
     unverifiable — owner round 17);
   - an item uploaded BY A PERSON (`createdBy.user.id`) must be that person (compared by Entra object id against
     `azureactivedirectoryobjectid`);
   - an item uploaded APP-ONLY (no user id) is accepted only for a row the BFF itself created (its `applicationid` is
     the BFF identity) and only when `createdBy.application.id` is the BFF identity;
   - "the BFF identity" = every Entra application id the BFF authenticates as: `AzureAd:ClientId`, `API_APP_ID`,
     `Graph:ManagedIdentity:ClientId`, `ManagedIdentity:ClientId`, `Dataverse:ClientId` (existing settings; no new
     option). On dev these are `1e40baad…` (SDAP-BFF-SPE-API) and `5967251e…` (mi-bff-api-dev) — exactly the two
     Dataverse application users that created 412 of the 530 pointered documents.

**Residual exposure until the strict check (stated by round 23):** a pointer to ANOTHER legitimately uploaded item of
the same creator inside the owner's own customer subtree; for a BFF-created row "the same creator" is the BFF identity.

**Call sites** (§18.3 table, unchanged list): each now passes exactly the item it reads — `DataverseDocumentsEndpoints`
download, `FileAccessEndpoints` download + eml-render, `DocumentsBulkEndpoints`, `ChatDocumentEndpoints` from-document,
`DocumentStorageResolver`, `DocumentCheckoutService` edit + preview URL, `CommunicationService` send attachments + .eml
embed (the attachment row's own item, verified against its document), `AppOnlyAnalysisService` (×2),
`DocumentContextService`, `FileIndexingService`, `InvoiceExtractionJobHandler`, `AttachmentClassificationJobHandler`.

**Expected effect on today's dev data** (read-only, §19.6; the item half needs a Graph census, so it is a live gate):
the container half refuses the **21** Business-Unit-1-owned documents in "Spaarke Demo"'s container that are not
communication-linked (another top-level unit's container), plus any of the further **10** communication-linked ones
whose item is not named for its communication; the 494 root-owned and 5 BU-1-in-own-container documents pass it. The
item half additionally refuses any document whose item was uploaded by someone other than its creator — notably BFF-created
rows whose item a PERSON uploaded (OBO), while `sprk_createdbyperson` is absent on `sprk_document` (task 146's child
schema is not yet applied on dev). These refusals are the decided behaviour until the owed migration (§19.10).
**This supersedes §18.12 gate 15's "0 of 530 refused"** (that figure described the r1 rule). **f1 update:** round 25
item 6 narrows a ROOT-owned row's subtree to the root unit's own containers, so the "494 root-owned … pass it" above
no longer holds for root-owned rows in another unit's container — the full census by class is §20.5.

Tests: `AccessControl.DocumentPointerContainerCheckTests` (rewritten, 33 methods / 39 cases: container — own unit,
same customer, another customer refused, root-owned, foreign, unknown unit, Ensure 409, archive path / unlinked /
another communication's item, archive-that-is-also-a-unit, the four secure-container cases; item — another person's
upload, not in the drive, Graph fault, no reader, person-row + BFF upload refused, BFF row + BFF upload, BFF row +
another application, another application's row, BFF row + recorded person, BFF row + no person, missing column;
undecidable — query fault, blank drive / item (6), empty / non-GUID / GUID-text ids; pure — `CustomerSubtree`,
`BffApplicationIdsFrom`), and the route-level
`Api.Ai.ChatDocumentEndpointsContractTests.IngestFromDocument_WhenTheRowsItemWasUploadedBySomeoneElse_Returns409AndNeverDownloads`
(the item asked about is the exact one downloaded). The shared `TestRecordContainerResolver` world now models business
units with parents, document creators, item creators and the BFF identity; its `ForBusinessUnitContainers` default
(root-owned documents, created and uploaded by one person) keeps every consumer test's subject unchanged.

## 19.4 Reporting (round 23 item 2 + findings 4, 5, 6)

| Route | r2 change |
|---|---|
| `POST /api/reporting/export` | the server-computed business-unit RLS identity is in the export request (§19.2 item 4); 503 without it |
| `POST /api/reporting/reports` | caller's `prvCreatesprk_Report` asked AS THE CALLER before any read or clone; always a server-side clone of the source row; no client-named Power BI report |
| `DELETE /api/reporting/reports/{reportId:guid}` | row deleted as the caller first (unchanged); Power BI report deleted only for a custom row no other row references (app-only existence read); otherwise kept |
| `GET /embed-token`, `GET /reports/{id}`, export | the catalog read now has a pinned "row of another id" refusal |

Client (`src/solutions/Reporting`): `SaveControls.tsx` (Save As = server clone via `createReport`; the SDK `saveAs`
and the "saved"-event id capture are gone), `reportingApi.ts` (`saveAsReport` / `SaveAsReportRequest` deleted),
`ReportingToolbar.tsx` / `App.tsx` (`workspaceId` prop removed), `ReportViewer.tsx` (doc comment). Build: §19.9.

## 19.5 Field-mapping push (finding 13)

See §19.2 item 13. The impersonated read is the route's existing mechanism (round 9: "reads — the caller's own rights
decide"); Dataverse applies row AND field security to it.

## 19.6 Live read-only facts (2026-10-04; Dataverse MCP SQL and `az webapp config appsettings list`; no write)

| Fact | Value |
|---|---|
| `privilege` `prvCreatesprk_Report` | id `4ea28bbd…`, accessright 32 (Create) — the constant |
| Business units | root `Spaarke` (`06fbf21c…`, container `b!vzGD…`); children `Spaarke Business Unit 1` (`cb15f587…`, `b!vzGD…` — the same container as the root), `Spaarke Demo` (`9271b764…`, `b!yLRd…`), `Secure Record`, `Spaarke Dev 1`, `Spaarke Test 1` (no container) |
| dev BFF settings (ids only) | `API_APP_ID` = `AzureAd__ClientId` = `Dataverse__ClientId` = `1e40baad…`; `Graph__ManagedIdentity__ClientId` = `ManagedIdentity__ClientId` = `5967251e…`; `Communication__ArchiveContainerId` = `b!yLRd…` (= Spaarke Demo's unit container) |
| Pointered active documents by owner unit × drive × creator | root: `b!vzGD…` 44 person + 34 BFF; `b!yLRd…` 74 person + 342 BFF. BU 1: `b!vzGD…` 5 BFF; `b!yLRd…` 31 BFF (10 of them communication-linked). Total 530 (118 person-created, 412 BFF-created) |
| `sprk_document.sprk_createdbyperson` | does not exist on dev (the read fails: "entity doesn't contain attribute") — task 146's child schema not yet applied |

## 19.7 Placement (CLAUDE.md §10) and component justification (§11) — this round

Placement: **in BFF, existing routes only.** No new endpoint, service, DI registration, package, option, job or column.
`RecordContainerResolver` gains two OPTIONAL constructor dependencies that are already registered unconditionally
(`ISpeFileOperations` → `SpeFileStore`, Scoped like the resolver; `IConfiguration`); no cycle (Graph types do not depend
on the resolver). Reporting handlers gain `[FromServices] CallerRecordAccessProbe` (registered unconditionally,
`ExternalAccessModule`) and `IGenericEntityService` (registered). Publish size: §19.9.

New members, three questions each:
- **`ISpeFileOperations.GetItemCreatorAsync`** (+ `SpeFileStore` delegate, `DriveItemOperations` implementation) and the
  **`SpeItemCreator`** record — Existing: `GetFileMetadataAsync` (app-only metadata, `FileHandleDto`, Redis-cached) has no
  creator, and `SpeAdminGraphService` exposes only a display name. Extension: adding creator fields to the cached
  `FileHandleDto` would put an authorization input behind a 5-minute cache and break old cache entries; the facade
  (ADR-007) gains one uncached method instead, returning a DTO, not an SDK type. Cost of nothing: round 23's ITEM
  verification is impossible — a re-pointed `sprk_graphitemid` cannot be told from the real one.
- **`RecordContainerResolver.DocumentPointer.cs` (partial)**, `CreatedByPersonColumn`, `BffApplicationIdKeys` /
  `BffApplicationIdsFrom`, `CustomerSubtree` and private helpers — Existing: the r1 check in the same type; the
  hierarchy math reuses `SpeAdminTenantScope.CollectSelfAndDescendants`; the archive link name reuses
  `CrossPathLink.LinkedCommunicationAttribute`. Extension: they extend the one owner of container decisions; the split
  file keeps the 2,200-line resolver from growing by another cohesive-but-distinct concern. Cost of nothing: verifier
  items 7 / 17 (cross-customer and any-item reads).
- **Reporting `CreateReportPrivilege`, `CallerMayCreateCatalogRowAsync`, `IsReferencedByAnotherCatalogRowAsync`,
  `RlsIdentityUnavailable`; `ReportingEmbedService.BuildExportRequest`; `ExportReportAsync`'s two required
  parameters** — Existing: the embed path's identity read and `BuildGenerateTokenRequest` (the export builder is its
  twin); the probe's G5 privilege question. Cost of nothing: items 4, 5, 6.
- **Field-mapping `BuildSourceRecordQuery`, `RetrieveSourceRecordValuesAsCallerAsync`, `ToClrValue`** — Existing: the
  app-only `RetrieveRecordFieldsAsync` (shared lib, not editable here) and the route's impersonated seam (reused). Cost
  of nothing: item 13.
- Removed surface: `CreateReportRequest.PbiReportId`, the Save-As branch, the client's `saveAsReport`.
- Test-only: `TestRecordContainerResolver.DocumentPointerWorld` (replaces r1's `ForDocumentPointerWorld`; the real
  resolver over its seams, ADR-010), `ChatDocumentEndpointsTestFixture.PointerWorld`.

ADR-002: no plugin. ADR-003: every new check fails closed (an unreadable item, creator, person column, hierarchy or
reference answer refuses — or, for DELETE, keeps the shared report). ADR-007: the new Graph read returns a DTO. ADR-038:
no `Mock<HttpMessageHandler>`, no DI-registration or ctor-null tests.

## 19.8 Seeding (two seeded builds, 2026-10-04; restored from byte copies, files touched)

16 seeds (`scratchpad/seed166r2.py`); build A: 13 seeds in disjoint code; build B: 3 seeds whose code overlaps build A's
(the item check as a whole vs its branches; the two DELETE guards). **43 of 247** targeted tests went red, each naming
its guard (A: 34 of 144; B: 9 of 103):

| Seed | Red tests (examples) |
|---|---|
| S1 export identity without the business unit | `ExportRequest_CarriesTheEffectiveIdentity_OnTheReportsDataset` |
| S1b export proceeds without a business unit | `Export_WhenTheCallersBusinessUnitCannotBeRead_ExportsNothing` |
| S2 Save-As registration restored (DTO + branch) | `Create_WithAClientNamedPowerBiReport_ClonesTheSource_AndNeverRegistersTheNamedReport`, `CreateReportRequest_HasExpectedProperties` |
| S3 Create privilege pre-check skipped | `Create_WithoutTheCallersCreatePrivilege_IsRefused_BeforeAnyReadOrClone(denied / throws)` |
| S4 standard reports deleted too (B) | `Delete_KeepsThePowerBiReport_…(standard-row)` |
| S5 unknown reference answer = unreferenced (B) | `Delete_KeepsThePowerBiReport_…(reference-check-throws)` |
| S6 reference check removed | `Delete_KeepsThePowerBiReport_…(referenced-by-another-row / reference-check-throws)`, `Delete_OfACustomRowNoOtherRowReferences_…` |
| S7 catalog row id not compared | `EmbedToken_/Export_/GetReport_ForARowTheCallerCannotBindTo_…(row-of-another-id)` |
| S8 missing query id calls `next()` | `QuerySource_AMissingContainerId_IsTheFiltersOwn400_AndNeverReachesTheHandler` (3) |
| S9 source fields read app-only | `Push_ReadsTheSourceFieldsAsTheCaller_NeverAppOnly`, `Push_ASourceColumnTheCallerCannotReadUnderFieldSecurity_…` (+ the push tests whose source the app-only double does not model) |
| S10 item check removed (B) | `AnItemUploadedByAnotherPerson_IsRefused_…`, `APersonsRow_WhoseItemTheBffUploadedAppOnly_IsRefused`, `ABffRow_WhoseItemAnotherApplicationUploaded_IsRefused`, `ARowAnotherApplicationCreated_…`, `ABffRow_WithNoRecordedPerson_…`, the missing-column case, `IngestFromDocument_WhenTheRowsItemWasUploadedBySomeoneElse_…` |
| S11 any business unit's container | `PointerIntoAnotherCustomersBusinessUnitContainer_IsRefused`, `ADocumentWhoseOwningBusinessUnitIsNotInTheHierarchy_IsRefused`, `Ensure_OnARefusedPointer_…`, `WhenTheArchiveIsAlsoABusinessUnitsContainer_…` |
| S12 archive accepted unconditionally | `PointerIntoTheArchive_FromARowNotLinkedToACommunication_IsRefused`, `…_AtAnotherCommunicationsItem_IsRefused` |
| S13 BFF identity accepted for a person's row | `APersonsRow_WhoseItemTheBffUploadedAppOnly_IsRefused` |
| S14 unknown person accepts any uploader | `ABffRow_WithNoRecordedPerson_WhoseItemAPersonUploaded_IsRefused`, `WhereTheCreatedByPersonColumnDoesNotExist_…` |
| S15 a call site verifies another item | `IngestFromDocument_WhenTheRowsItemWasUploadedBySomeoneElse_Returns409AndNeverDownloads` |

Restored: no `SEED-166R2` marker remains in `src/` or `tests/` (Grep).

## 19.9 Gates (this round, final code)

| Gate | Result |
|---|---|
| Affected tests (before the final runs) | Reporting 108 passed; FieldMapping 114 passed / 1 skipped; pointer-check consumers (download / AI / communication / checkout / jobs / version / destroy / container list) 1,429 passed / 13 skipped; pointer + chat-document 56 passed; all 0 failed |
| Full BFF unit suite (`tests/unit/Sprk.Bff.Api.Tests`) | **14,509 total: 14,455 passed, 54 skipped, 0 failed** (12 m 43 s; +45 cases vs r1's 14,464) |
| NetArchTest (`tests/Spaarke.ArchTests`) | **346 passed, 0 failed** |
| Sprk.Bff.Api.IntegrationTests (full) | **104 passed, 0 failed** (run alone — the verifier's `PlaybookByNameDeprecationTests` contention flake did not recur) |
| Spe.Integration.Tests (full) | **405 total: 380 passed, 25 skipped (environment-gated `SkippableFact`s), 0 failed** |
| BFF build | succeeded (warnings as errors) |
| `dotnet list package --vulnerable --include-transitive` | `Sprk.Bff.Api` has no vulnerable packages |
| Reporting client | `npm run build` (vite) **green** after `npm install --legacy-peer-deps --no-audit --no-fund` here and in `Spaarke.UI.Components`, and `npm run build` of `Spaarke.SdapClient` / `Spaarke.Auth` (the worktree had no node_modules / dist). `tsc --noEmit`: 0 errors in the files this round changed (12 pre-existing in `ReportDropdown`, `ReportViewer` — untouched code —, `ThemeProvider`, `globalStyles`). `prettier --list-different` flags the five changed files AND their HEAD versions (pre-existing drift from the root config); not reformatted, to keep the diff to the change. The package has no test runner. |
| /conflict-check (2026-10-04, r2 files) | 27 open PRs × the 37 files this round changes: **no overlap**. Master since the merge base (`b8026dfa8`, master @ `62277d50a`): **no overlap** with this round's files (r1's `OfficeQuickCreateContractTests.cs` textual merge stands). BFF hot path shared with other active worktrees: soft warn, no hard conflict. |
| Publish size | Not measured by this run (harness: skip). The verifier's independent measurement of r1: **45.657 MB** (`e6dd48b43`) vs **45.675 MB** (`582a4b42a`) = **+0.018 MB**, Compress-Archive Optimal, PDBs included, 212 / 212 files, fresh short-path worktrees. r2 adds no package and no DI registration; the main session re-measures at integration. Ceiling 60 MB. |

## 19.10 Not closed

- **Round 21 item 1 (i)–(iii) — OWED BY TASK 166, assigned by round 23 to the follow-up round:** (i) the BFF
  pointer-attach endpoint with every client pointer writer moved to it; (ii) the legacy migration script (dry run /
  `-Apply` / `-Verify`) for the dev files not in their derived container; (iii) then
  `scripts/Set-DocumentPointerFieldSecurity.ps1 -ClientNoLongerWritesPointers -Apply` / `-Verify` and the switch of
  this check to the strict derived-container comparison. Until then the F0 write door is narrowed by §19.3, not closed;
  the POML status says so (`completed-with-escalation`).
- **Publish size** — the verifier's figure is recorded; this run did not re-measure (harness instruction).

## 19.11 Route authorization ledger input — r2 rows (route key as the guard spells it)

| Route key | Mechanism that now decides | Deny test (FQN) |
|---|---|---|
| `POST /api/reporting/export` | handler: row read as the caller + server-computed BU RLS identity in the export (503 without it) | `Sprk.Bff.Api.Tests.Api.Reporting.ReportingCatalogBindingContractTests.Export_WhenTheCallersBusinessUnitCannotBeRead_ExportsNothing` |
| `POST /api/reporting/reports` | handler: `prvCreatesprk_Report` AS THE CALLER before any read or clone; source row read as the caller; clone only | `…ReportingCatalogBindingContractTests.Create_WithoutTheCallersCreatePrivilege_IsRefused_BeforeAnyReadOrClone` |
| `DELETE /api/reporting/reports/{reportId:guid}` | handler: row deleted as the caller first; Power BI report only for an unreferenced custom row | `…ReportingCatalogBindingContractTests.Delete_KeepsThePowerBiReport_UnlessItIsACustomRowsUnreferencedReport` |
| download routes of §18.3 | existing authorization + the round-23 pointer check (item + customer subtree) | `Sprk.Bff.Api.Tests.Api.Ai.ChatDocumentEndpointsContractTests.IngestFromDocument_WhenTheRowsItemWasUploadedBySomeoneElse_Returns409AndNeverDownloads`; `Sprk.Bff.Api.Tests.AccessControl.DocumentPointerContainerCheckTests` |
| `GET /api/v1/documents` (query `containerId`) | `ContainerDocumentAuthorizationFilter(queryParameter: "containerId")` — unchanged; the missing-id 400 is now pinned at the filter | `Sprk.Bff.Api.Tests.AccessControl.ContainerDocumentListAuthorizationTests.QuerySource_AMissingContainerId_IsTheFiltersOwn400_AndNeverReachesTheHandler` |

## 19.12 Manual live gates — r2 (main session, dev; non-admin test users; ids redacted to 8 chars)

17. **Before or right after the deploy — pointer-check census.** The item half needs Graph `createdBy`, which this run
    cannot read. After deploying to dev, download one document of each class and read the BFF log for
    `[DOCUMENT-POINTER] REFUSED`: a person-uploaded document (`createdby` a person), an Office-saved document (BFF row,
    BFF upload), an archived email `.eml` and one of its attachments (archive path), a Compose / chat-saved document
    (BFF row, OBO upload — EXPECTED refused until task 146's child schema and stamp populate `sprk_createdbyperson`),
    and one of the 21 BU-1-owned documents in "Spaarke Demo"'s container (EXPECTED refused, §19.3). Record the counts;
    any refusal outside the expected classes is a defect to report, not to widen.
18. **Reporting** (with gate 11's seeded `sprk_report` row): export as a user of business unit X → the PDF contains only
    X's rows; a user whose business unit cannot be read → 503, no file. An Author without `prvCreatesprk_Report` → 403
    and no new report in the workspace. Deleting a STANDARD row (or one another row references) as an Admin → the row
    is gone and the Power BI report still exists.
19. Field-mapping push from a matter whose mapped source column is field-secured from the test user → the children are
    unchanged for that column (if no profile maps an FLS column, record "no FLS-mapped profile; proven by tests").

## 19.13 Integration notes

- `RecordContainerResolver.CreatedByPersonColumn` spells `sprk_createdbyperson`; switch it to task 146's
  `Spaarke.Dataverse.RecordCreatorPersonColumn.LogicalName` when 146 is merged (the integration checklist's
  "creator column constant" item covers the same switch for 146's helper).
- Task 146's child-table `sprk_createdbyperson` schema (a deploy prerequisite already) is also what lets BFF-created
  rows with person-uploaded items pass this check; until it is applied and stamped, those rows are refused (decided).
- `NOTE-FROM-MAIN.md` (round 23) was deleted, not committed.
- No `.claude` edit is needed for this round (§12's edit stands).

# f1 (2026-10-04): round 21 item 1 (i)–(iii) and round 25 item 6 built; the follow-up list closed

> **Branch**: `task/uac-r2-166-f1` from `task/uac-r2-166-r2` @ `8e0c89f33`
> **Inputs (BINDING)**: owner/main-session rounds 1–27 (`work/unified-access-control-r2`,
> `notes/session27-owner-decisions-and-research.md`) — chiefly round 21 item 1 (i)–(iii), round 23 item 1 (the interim
> rule, kept as the default), round 25 item 6 (report-catalog FLS + allowed-workspace check; root-owned rows; the real
> export builder under test) and round 26 item 3 (ONE `DocumentContainerRelocator`, two callers); the main session's
> `NOTE-FROM-MAIN.md` (round 26 item 3 restated; read first, deleted, NOT committed); the 21-item follow-up list for this
> round (items 1–21 below).
> **Outcome**: every item is closed in code, tests, scripts and docs (§20.2). What is left of them is LIVE writes only —
> each a dry-run / `-Apply` / `-Verify` script or a deploy, with its exact command in §20.11 for the main session. One
> consequence FOUND while building item 1 (ii) is escalated, not silently fixed (§20.12 🔔: a relocated file's RAG index
> entries keep the old item id; the complete fix is new AI-facade surface, CLAUDE.md §10 bullet 3 / §6). Status
> `completed-with-escalation` for that one decision.

## 20.1 Binding inputs as applied

| Decision | Implemented |
|---|---|
| Round 21 item 1 (i) — BFF pointer-attach endpoint; every client pointer writer moved to it; the client creates the row WITHOUT the pointer and the BFF stamps it after verifying the derived container (ADR-002 WP-3) | §20.3 |
| Round 21 item 1 (ii) + round 26 item 3 — the legacy migration as ONE BFF service (`DocumentContainerRelocator`), copy → verify → re-point through the attach path → delete source; the script only triggers the BFF and reads its reports | §20.4 |
| Round 21 item 1 (iii) — the FLS script's `-ClientNoLongerWritesPointers` precondition made satisfiable; the strict derived-container rule behind a flag, default = the round-23 interim rule | §20.5 |
| Round 25 item 6 — `sprk_report` pointer columns FLS (BFF-only writable) + a server-side allowed-workspace check before embed, export and delete; a ROOT-owned document accepts only containers the root unit itself stamps; a test that executes the REAL export request builder | §20.6 |
| Round 26 item 3 "do not build the Make Secure caller" | Not built. Task 150's lane calls `DocumentContainerRelocator.RelocateDocumentsAsync(ids, recordContainer, RelocationPurpose.MakeSecure, apply: true)` (§20.12) |

## 20.2 The 21 items — what changed per item

| # | Item | Closure |
|---|---|---|
| 1 (i) | BFF pointer-attach endpoint; client writers (`DocumentRecordService.ts` ×2, `EntityCreationService.ts` ×1, the seven Create*Wizards, the upload PCFs) moved to it; PCF builds with `npm run build:prod` | NEW `POST /api/v1/documents/{id}/file` (§20.3). Every client writer now creates the row with no `sprk_graphdriveid` / `sprk_graphitemid` / `sprk_hasfile` / `sprk_filepath` and calls `SdapApiClient.attachDocumentFile`; a refused attach deletes the just-created row (best effort) so no file-less "document" is left. The seven Create*Wizards write through `EntityCreationService`, so they are covered by its change; the form-script `DocumentOperations.js` pointer writes are removed. NEW arch guard `ClientDocumentPointerWriteGuardTests` scans every `.ts/.tsx/.js` under `src/client`, `src/solutions`, `src/dataverse` AND the committed PCF `Solution/Controls/**/bundle.js` (what a PCF solution import deploys); it went red on the seven stale committed bundles until they were rebuilt (`npm run build:prod`, all seven `Succeeded`) and copied into `Solution/Controls/sprk_Spaarke.Controls.<X>/bundle.js` |
| 1 (ii) | Legacy migration (dry run / `-Apply` / `-Verify`, NOT run) moving misplaced dev files via the BFF identity; ONE BFF `DocumentContainerRelocator` | §20.4: the relocator (copy → verify size + quickXorHash → re-point through the attach path's one writer → delete source only when no other row points at it; every step logged with before/after ids); the ADR-036 job `document-container-migration` (registered DISABLED, report-only unless `DocumentContainerMigration:WritesEnabled`); `scripts/Invoke-DocumentContainerMigration.ps1` drives it through the existing `/api/admin/jobs` trigger/history routes. Not run (§20.11 gate 24) |
| 1 (iii) | Make the FLS script's `-ClientNoLongerWritesPointers` precondition satisfiable; strict rule behind a flag (default interim) | `Set-DocumentPointerFieldSecurity.ps1` p4 is now EVIDENCE + confirmation: (p4a) scans every deployed JavaScript / HTML web resource for a client pointer write (it found ~30 on dev 2026-10-04 — the bundles predating this round, incl. the retired `UniversalDatasetGrid` PCF still deployed); (p4b) the operator's `-ClientNoLongerWritesPointers`. Strict rule: `DocumentPointer:StrictDerivedContainer` (§20.5) |
| 2 | `sprk_report` FLS (`sprk_pbi_reportid`, `sprk_workspaceid`, `sprk_datasetid`, `sprk_iscustom`) + allowed-workspace check before embed, export, delete; root-owned rule; real `ExportReportAsync` test | §20.6 |
| 3–6, 15 | Verifier context / positives | No action (recorded). |
| 7 / 16 | Export RLS: no test executed the real request builder | `ExportReportAsync_TheRealRequestItSends_CarriesTheCallersRlsIdentity_OnTheReportsDataset` runs the REAL `ReportingEmbedService.ExportReportAsync` (a `CallBase` mock whose only override is the new `protected internal virtual GetPowerBIClientAsync` seam) against a strict `IPowerBIClient` / `IReportsOperations` double and captures the `ExportReportRequest` it sends: `Identities` = one `EffectiveIdentity { Username = caller's business unit, Roles = [BusinessUnitFilter], Datasets = [the report's dataset read from Power BI] }`. `ExportReportAsync_WithoutAnRlsUsername_RefusesBeforeAnyPowerBiCall` ("" / "   "). Seed S20 (identity removed from the real request) → red |
| 8 | `sprk_report` native-write second door (any Write holder could re-point a catalog row) | Both controls of round 25 item 6: FLS target `-Target ReportCatalog` (`Set-ReportCatalogFieldSecurity.ps1` wrapper) AND the allowed-workspace check, which also catches rows forged before the lock. `POST /api/reporting/reports` now creates the row AS THE CALLER with only `sprk_name` / `sprk_embedurl` / `sprk_category` and stamps the four pointer columns APP-ONLY (the identity the FLS admits); a failed stamp deletes the half-made row and the clone (502) |
| 9 | Root-owned widening (a root-owned row's subtree was the whole environment) | `RecordContainerResolver.CustomerSubtree`: a root-owned row's subtree is `[root]` only — the root's OWN stamped containers. Test renamed `ARootOwnedDocument_MayUseOnlyAContainerTheRootItselfStamps`; `CustomerSubtree_IsTheOwnersTopLevelUnitAndEverythingBeneathIt` pins `CustomerSubtree(root) = {root}`. Seed S1 → red. Census effect §20.5 |
| 10 | Census: Secure Record owner-team documents and BFF-created rows with person-uploaded items (Compose / chat saves, outbound attachments) are EXPECTED refusals; add them | §20.5 census table (both classes + the root-owned class item 9 adds). The migration job's dry-run report is the census instrument: per document, its state and the interim / strict answers |
| 11 | §18.12 gate 15 "0 of 530 refused" is stale (contradicts §19.3) | §18.12 gate 15 struck through and corrected in place; §19.3 now names the supersession |
| 12 | The BFF identity config keys are documented nowhere an operator reads | `docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` §6.5: one line naming the five keys (`AzureAd__ClientId`, `API_APP_ID`, `Graph__ManagedIdentity__ClientId`, `ManagedIdentity__ClientId`, `Dataverse__ClientId`) and the fail-closed consequence; plus `DocumentPointer__StrictDerivedContainer` and `PowerBi__AllowedWorkspaces__{n}__*` |
| 13 | Performance: uncached repeated reads; bulk download | Per-SCOPE memo in `RecordContainerResolver` (Scoped = one request or one job run): the business-unit hierarchy is read once, each container's claimants once; only SUCCESSFUL answers are kept (a faulted read is dropped, so the next check asks again); nothing crosses requests. A bulk download of N documents now makes 1 hierarchy read instead of N. The Graph item read stays uncached by design (an authorization input — r2 §19.7). Tests `OneScope_ReadsTheHierarchyAndEachContainersClaimantsOnce_ForManyChecks`, `AFailedHierarchyRead_IsNotRemembered_TheNextCheckAsksAgain`. Seeds S22a / S22b → red |
| 14 | Dead code / stale docs | DELETED `ReportingEmbedService.GetReportAsync` (no caller since r2) and its `ReportingProfileManagerTests` InlineData; DELETED `SpeContainerMembershipService.ListExternalMembersAsync` (+ `ReadExternalMembersAsync`, `ToContainerMember`, `SpeContainerMember`; no caller) with its two tests retargeted to the live `RemoveMembershipsAsync` / `RevokeMembershipAsync` paths and `ExternalAccessQueryIntegrityGuardTests` reduced to the surviving signature; `reporting-admin.md` endpoint table (PUT → PATCH, the verb the client sends), catalog contract + FLS paragraph, token flow, env vars, and the RLS section (the stale "client sends bu" line ~387); `reporting-module.md` Save As (= server clone); `Spaarke.AI.Widgets/src/types/WorkspaceTab.ts` stale doc (lines 5 and 337). `.claude/skills/bff-deploy/SKILL.md:50` is main-session-only (§20.12) |
| 17–21 | Record criteria status | Publish size: the verifier's r2 measurement is recorded (`e6dd48b43` 45.657 MB vs `8e0c89f33` 45.684 MB = +0.027 MB, 212 / 212 files) and f1 is measured here (§20.9). F0 / F7 / F13 superseded by deletion (their routes were deleted in the first run; the rows stay struck in §9). F10 uniform 404 accepted (round 12 item 4). Manual gates pending: §17, §18.12 (gate 15 corrected), §19.12, §20.11 |

## 20.3 Round 21 item 1 (i): the pointer-attach route and the client writers

**Route.** `POST /api/v1/documents/{id}/file` with body `{ driveId, itemId }` (`AttachDocumentFileRequest`), on the
existing documents group: `.AddDocumentAuthorizationFilter("write")` (WRITE on `{id}` as the caller, the same filter as
the group's other writes) + `.RequireAuthorization()`. Handler → `DocumentContainerRelocator.AttachFileAsync`, which, in
order and with each failure leaving the row untouched:

1. **first file only** — a row that already carries a pointer accepts the SAME file again (200, `alreadyAttached`) and
   refuses any other (409 `AlreadyAttached`): re-pointing a file is the relocator's job, never a client's;
2. **the caller is the row's creator** — `createdby` when a person, else the person the BFF recorded in
   `sprk_createdbyperson`, compared by Entra object id through the resolver's ONE definition
   (`RecordContainerResolver.ReadDocumentCreatorObjectIdAsync`, the same reader the round-23 item check uses) — else
   403 `NotTheCreator`;
3. **the drive is the row's DERIVED container** (`DeriveDocumentContainersAsync`, §20.5) — undecidable → 409
   `ContainerUndetermined`; another container → 409 `WrongContainer`;
4. **the item exists there and the CALLER uploaded it** (Graph `createdBy.user.id`) — else 403 `NotTheUploader`;
5. stamp APP-ONLY: `sprk_graphdriveid`, `sprk_graphitemid`, `sprk_hasfile = true`, `sprk_filepath` = the item's
   `webUrl` read from Graph (never the client's).

Problem details carry `errorCode = document_file_attach_refused` and `reasonCode` = the outcome; 400 for a missing id.
Because the attach admits exactly the rows the interim check then honours, a freshly attached pointer passes both rules.

**Clients.** `Spaarke.SdapClient`: `SdapApiClient.attachDocumentFile(documentId, { id, driveId? })` (+
`AttachDocumentFileResult`, exported). `Spaarke.UI.Components`: `EntityCreationService` (payload without pointer
columns; `attachUploadedFile`; `_deleteUnattachedDocument` on a refused attach), `DocumentRecordService`
(`attachFile: DocumentFileAttacher` REQUIRED option; `createAndAttach` for both create paths),
`createXrmEmailComposeHandlers` (attach, delete on failure), `IDataverseClient` / `WebApiLike` optional `deleteRecord`
(+ `ODataDataverseClient`; `PcfDataverseClient` deliberately untouched — zero instantiation sites, and master deletes it, reuse audit C-26). `DocumentUploadWizard/uploadOrchestrator.ts` passes
`attachFile: (documentId, file) => sdapClient.attachDocumentFile(documentId, file)` (the only `new DocumentRecordService`
in `src/`). `spaarke_documents/DocumentOperations.js`: `updateDocumentAfterUpload` throws (the form script can no longer
stamp a pointer); `processFileDelete` no longer clears the pointer columns (the BFF owns them).

**PCF bundles rebuilt** (`npm install --legacy-peer-deps --no-audit --no-fund` then `npm run build:prod`; every log
`[build] Succeeded`; lock-file churn reverted): CommunicationActions, CommunicationConnections,
CommunicationConversationPanel, CommunicationMessageActions, CommunicationTimeline, CommunicationTimelineRegarding,
TrackingFieldTrio. **Pre-existing build break fixed to make this possible:** since the 2026-08-14 `pdfjs-dist` 6.x CVE
bump, pcf-scripts' babel-loader (no `node_modules` exclude) crashes on `pdfjs-dist/build/pdf.mjs` ("Cannot read
properties of null (reading 'declarations')") for every single-chunk PCF whose bundle reaches SprkChat's
`import('pdfjs-dist')` through the `@spaarke/ui-components` barrel — and pcf-scripts still exits 0, so the failure was
silent. **Open PR #1123 (task 092, master build/test baseline repair) fixes the same six Communication* configs**, so
this branch adopts its files BYTE-IDENTICALLY (`git show ec247e9dc:…` — the six `webpack.config.js` and the two LOUD
stubs `src/client/pcf/shared/stubs/pdfjsDistUnreachable.js` / `mammothUnreachable.js`, which throw if ever reached):
identical changes on both sides merge without conflict, whichever lands first. TrackingFieldTrio (not in #1123) gets a
`webpack.config.js` aliasing the same two stubs (its `featureconfig.json` already allowed a custom config). The
Communication.Components shared package needed `npm install` in this worktree for CommunicationConnections to resolve.
`ControlManifest.xml` versions are NOT bumped here (the `pcf-deploy` skill bumps at deploy, §20.11 gate 21).

## 20.4 Round 21 item 1 (ii) / round 26 item 3: `DocumentContainerRelocator` and the legacy migration

`Services/Documents/DocumentContainerRelocator.cs` (sealed, Scoped, `DocumentsModule`). The ONE writer of a document's
pointer outside a path that uploads the bytes itself; both of its writes go through one private `WritePointerAsync`.

- **`RelocateIfMisplacedAsync(documentId, apply, purpose, ct, expectedTargetContainer)`** → `DocumentRelocationOutcome`
  (`NoFile`, `InPlace`, `WouldRelocate`, `Relocated`, `RelocatedSourceKept`, `Undecidable`, `FileMissing`,
  `SourceUnverified`, `Failed`). Derivation undecided → `Undecidable`, nothing moves. An `expectedTargetContainer` (Make
  Secure) must be one the derivation ALLOWS, else `Undecidable`. **Legitimacy:** `LegacyMigration` moves only a file
  that is verifiably the row's own (`RecordContainerResolver.IsRelocationSourceVerifiedAsync`: the item exists, was
  uploaded by the row's creator under the round-23 item rule, and sits in an environment container — a unit's, the
  archive, or the own container of a secure record the document hangs off); a forged pointer is reported
  `SourceUnverified` and NEVER copied into the document's container. `MakeSecure` skips that check only when the derived
  container IS the secure one (the move only narrows who can reach the bytes). Files over 250 MB → `Failed`, reported,
  never half-copied.
- **Order (NOTE-FROM-MAIN, round 26 item 3):** (1) download → `UploadSmallAsync(target, sanitized name, bytes,
  ConflictBehavior.Rename)` as the BFF identity; (2) VERIFY the copy against the source — size must match, and
  `quickXorHash` when Graph returns both (`CopyMismatch`); a mismatch deletes the copy, the row and source untouched;
  (3) RE-POINT through the attach path's writer; a failure deletes the copy, the row still names the source; (4) DELETE
  the source only when no OTHER `sprk_document` points at it (an unreadable answer keeps it → `RelocatedSourceKept`).
  Every step logs `[DOCUMENT-RELOCATE] copied: / re-pointed: / source deleted: / source KEPT:` with both id pairs.
- **`RelocateDocumentsAsync(ids, targetContainerId, purpose, apply)`** → `DocumentRelocationBatchResult(Outcomes,
  Counts, Incomplete, Complete)`: the reusable entry point for task 150 (inputs: document ids + the target container;
  outputs: per-file outcomes, counts, and the incomplete ids). Resumable: a moved file is `InPlace` next time.
  `RelocatedSourceKept` is settled for the migration and NOT for Make Secure (a secure record's bytes must not stay in
  the shared container). **Superseded by f1-v1 §21.4 (owner round 37 item 2):** a source kept for rows of OTHER records
  is complete for both purposes (`RelocatedSourceKeptForOtherRecords`); anything still owed is `RelocationPending`,
  recorded in the row's relocation ledger and settled by the repeat call.

**The job** `DocumentContainerMigrationJob` (`IScheduledJob` `document-container-migration`, ADR-036; ADR-052: a
bounded batch inside the BFF that composes BFF-only services). Registered DISABLED (`AddScheduledJob(…, enabled:
false)`); runs only on `POST /api/admin/jobs/document-container-migration/trigger` (SystemAdmin). Report-only unless
`DocumentContainerMigration:WritesEnabled` is true; `DocumentContainerMigration:MaxDocumentsPerRun` (default 100). Each
run reads a keyset batch of pointered documents after its cursor, relocates (or plans), then evaluates BOTH rules on the
final pointer; its `ResultJson` report: mode, startAfter / endAt, passComplete, examined, `wouldNewlyRefuse` (interim
serves, strict would refuse — exactly what flipping the flag would break), `refusedByBoth`, counts by state, and up to
200 listed rows. Success = no `wouldNewlyRefuse`, no `Failed`, no `WouldRelocate`. An enumeration fault throws (the
attempt fails; the scheduler's history shows it).

**The driver** `scripts/Invoke-DocumentContainerMigration.ps1` (148's `Invoke-SecureChildBackfill.ps1` precedent):
triggers, polls `/history`, repeats until `passComplete`, proves the pass contiguous (each run starts where the previous
ended, from the first document), saves every report, prints totals. `-Apply` turns `DocumentContainerMigration__WritesEnabled`
on for the pass and OFF in a `finally`. `-Verify` exits 0 only on a full pass with `WouldRelocate = 0`, `Failed = 0`,
`wouldNewlyRefuse = 0`. No Graph or Dataverse logic in PowerShell.

## 20.5 Round 21 item 1 (iii): the strict rule, the flag, and the census (item 10)

**Derivation** (`RecordContainerResolver.DeriveDocumentContainersAsync` → `DocumentContainerDerivation(Decided,
AllowedContainers, PrimaryContainer, IsSecure, Reason)`), over the document's 17 canonical link columns
(`DocumentLinkFields`): a `sprk_communication` link → where that communication archives
(`ResolveForRecordWithFixedFallbackAsync`, the archive container); a Root / Intermediate link → `ResolveForRecordAsync`
(a secure record's own container, else its business unit's); Party and email links own no content and are skipped; an
attachment (`sprk_parentdocument`) → its parent's answer, one level; unfiled → the owner's business-unit container, else
`EmailProcessing:DefaultContainerId` (exactly the Office save's choice), else undecided. A secure answer DOMINATES (only
that container); two different secure records → undecided; any fault → undecided.

**Strict rule** (`DocumentPointer:StrictDerivedContainer = true`): the pointer's drive is in the derived set AND the item
exists in that drive. Container-only, as round 21 decided (the uploader no longer matters once the container is the
record's own). **Default `false` = the round-23 interim rule, unchanged.** The flag is flipped only after the
migration's `-Verify` (§20.11 gate 25).

**Census — what the INTERIM rule (in force after deploy) refuses by design, and what the strict rule does** (item 10;
dev counts from §19.6 where known; the exact per-class numbers are the migration dry run's report, gate 24):

| Class | Interim (round 23 + round 25 item 6) | Strict (after the flag) |
|---|---|---|
| Person-created row, that person's upload, in a container of the owner's customer subtree | served | served only in its derived container; the migration moves the rest |
| BFF-created row, BFF (app-only) upload — Office saves, archive writers | served | served in its derived container |
| **BFF-created row whose item a PERSON uploaded (OBO): Compose saves, chat "save to document", outbound communication attachments** | **REFUSED (expected)** until task 146's `sprk_createdbyperson` is applied and stamped on `sprk_document` (absent on dev, §19.6) | served in its derived container |
| **Secure Record owner-team document (149's isolation) whose file is still in a shared business-unit container** | **REFUSED (expected)**: the "Secure Record" unit stamps no container, so only the secure record's own container passes | refused until moved: task 150's Make Secure relocation moves it (`RelocationPurpose.MakeSecure`), and the legacy migration moves it when the file is verifiably the row's own |
| **Root-owned row in another unit's container (round 25 item 6)** — dev: up to 416 root-owned rows in `b!yLRd…` (Spaarke Demo's, which is also the archive) | **REFUSED (expected)** unless on the archive path (the row's communication's own `{C:N}_…` item) | served in its derived container; the migration moves the rest |
| BU-1-owned row in Spaarke Demo's container (21, + some of 10, §19.3) | refused (as r2) | as its derivation says; moved if verifiable |
| Another person's upload / a container of no unit (forged) | refused | refused; the migration reports `SourceUnverified`, never copies |

Tests: `DocumentContainerStrictRuleTests` (23 methods, 29 cases: derivation per link kind, secure dominance, undecidable
cases, unfiled default, party links, strict allow / refuse incl. "another container of the same customer the interim
rule allows", the flag default, relocation-source verification, the memo, `IsBusinessUnitInSubtreeAsync`).

## 20.6 Round 25 item 6: the report catalog and root-owned rows

- **FLS:** `scripts/Set-DocumentPointerFieldSecurity.ps1 -Target ReportCatalog` (same two task-133 profiles, same
  p1–p6 preconditions; p4 = `-BffWritesCatalogPointers`, the operator's confirmation that the f1 BFF — which stamps the
  four columns app-only — is deployed), with `scripts/Set-ReportCatalogFieldSecurity.ps1` as the named entry point.
- **Allowed workspaces:** `PowerBiOptions.AllowedWorkspaces` (`PowerBi:AllowedWorkspaces:{n}:WorkspaceId` and optional
  `…:CustomerBusinessUnitId`). Every catalog route answers 503 `sdap.reporting.config.workspaces_unconfigured` BEFORE any
  read when the list is empty (fail closed: an empty allow-list is never "any workspace"). Embed-token, export, GET,
  PATCH, DELETE and create-from-source treat a row whose workspace is not listed as "not in your catalog" (the uniform
  404; Power BI never asked; nothing written); `GET /reports` omits such rows. A workspace bound to a customer admits
  only callers whose business unit is in that customer's subtree, read through the ONE hierarchy reader
  (`RecordContainerResolver.IsBusinessUnitInSubtreeAsync`); no reader → refused.
- **Create:** the caller's create carries no pointer column; the BFF stamps them app-only; a failed stamp removes the
  row and the clone (502).
- **Root-owned documents:** §20.2 item 9.
- **Tests** (`ReportingCatalogBindingContractTests`, +9 methods / 21 cases): `AnAction_OnARowWhoseWorkspaceThisDeploymentDoesNotAllow_IsTheUniform404_AndActsOnNothing` (6 routes),
  `WithNoAllowedWorkspaceConfigured_EveryActionIs503_AndReadsNothing` (6), `GetReports_WithNoAllowedWorkspaceConfigured_Is503`,
  `GetReports_OmitsARowWhoseWorkspaceThisDeploymentDoesNotAllow`, `EmbedToken_ForACustomerBoundWorkspace_AdmitsOnlyThatCustomersBusinessUnits` (2),
  `ACustomerBoundWorkspace_WithNoHierarchyReader_IsRefused_FailClosed`, `Create_WhenThePointerStampFails_TheRowAndTheCloneAreRemoved_AndNothingIsLeftHalfMade`,
  the two real-export tests (§20.2 item 7); `Create_FromAReadableSource_…` now asserts the caller payload has no pointer
  column and the app-only stamp carries all four.

## 20.7 Placement (CLAUDE.md §10) and component justification (§11)

**Placement: in BFF.** Every new piece composes BFF-only things — `RecordContainerResolver`'s container decisions, the
app-only SPE facade (`SpeFileStore`), the BFF's Dataverse application identity (the only identity the pointer FLS
admits) — so no other host could own it (ADR-052: the job is a bounded, operator-triggered batch over those services;
in-BFF `IScheduledJob` per ADR-036, no new hand-rolled timer). ADR-002: no plugin — the invariant (a pointer names the
record's own container, written by the BFF) has ONE server owner (WP-1) and clients only request it (WP-3). ADR-003: every
new decision fails closed (undecidable derivation, unreadable creator, unverified copy, unknown reference, empty
allow-list, missing hierarchy reader). ADR-007: Graph returns DTOs (`SpeItemCreator` gains `Size`, `QuickXorHash`,
`WebUrl`). ADR-038: no `Mock<HttpMessageHandler>`, no DI-registration or ctor-null tests. Publish size §20.9.

Three questions per NEW surface:
- **`POST /api/v1/documents/{id}/file` + `AttachDocumentFileRequest` / `Response`** — Existing: the record-keyed upload
  route places bytes but stamps no row; the deleted `PUT /api/v1/documents/{id}` was the old client write door.
  Extension: the upload route cannot stamp — the row is created by the client AFTER (or independently of) the upload,
  and record-less uploads exist; one attach call is the smallest server write. Cost of nothing: under the FLS the
  client cannot write the pointer at all, so every client upload would produce a file-less document (round 21 item 1 (i)).
- **`DocumentContainerRelocator` (Scoped, `DocumentsModule`, unconditional — its route maps unconditionally, §F.1)** —
  Existing: `RecordContainerResolver` decides containers but writes nothing; `SpeFileStore` moves no bytes between
  containers; 148's backfill re-owns rows, not files. Extension: putting byte copies and pointer writes into the
  resolver would mix a write service into a read-only decision type (§11.5); round 26 item 3 names this one service.
  Cost of nothing: 447 dev files stay outside their derived container, so the strict rule can never be switched on, and
  Make Secure leaves secure bytes in a container whose access cannot be narrowed.
- **`DocumentContainerMigrationJob` + `DocumentContainerMigration:WritesEnabled` / `:MaxDocumentsPerRun`** — Existing:
  ADR-036's job host + `/api/admin/jobs` trigger/history (reused, no new route). Extension: it IS the extension (one job
  class). Cost of nothing: no auditable, resumable, BFF-identity way to run (ii); the -Verify gate would have no report.
- **`DocumentPointer:StrictDerivedContainer`** — Existing: the interim rule. Extension: a flag on the same check, not a
  second check. Cost of nothing: the strict rule could only ship by refusing every unmigrated file at deploy.
- **`PowerBi:AllowedWorkspaces` (+ `AllowedPowerBiWorkspace`) and `ReportingEndpoints.IsWorkspaceAllowedAsync` /
  `ReadActionableRowAsync` / `CatalogPointerColumns` / `CatalogPointerFields` / `WorkspacesUnconfiguredCode`** —
  Existing: `PowerBiOptions` (extended, not a new options class); the catalog read as the caller. Cost of nothing: a
  forged or seeded row could aim embed / export / delete at any workspace the service principal can reach (item 8).
- **`RecordContainerResolver` members** — `DeriveDocumentContainersAsync` + `DocumentContainerDerivation`,
  `IsAllowedUnderStrictRuleAsync`, `IsRelocationSourceVerifiedAsync`, `ReadDocumentCreatorObjectIdAsync` (the one
  "row's creator" reader the attach and the item check share), `IsBusinessUnitInSubtreeAsync`, `IsSameContainerId`,
  the per-scope memo: extensions of the one owner of container decisions in its existing partial file; no new type.
- **`ReportingEmbedService.GetPowerBIClientAsync` made `protected internal virtual`** — the test seam item 7 requires
  (the codebase's virtual-facade idiom); no behaviour change.
- **Scripts** `Invoke-DocumentContainerMigration.ps1` (NEW: the round-26 driver), `Set-ReportCatalogFieldSecurity.ps1`
  (NEW: a 20-line named entry point over the extended `Set-DocumentPointerFieldSecurity.ps1`; one mechanism).
- **`ClientDocumentPointerWriteGuardTests`** (NEW arch guard) — Existing: none scans client code or committed bundles for
  pointer writes; the FLS script's p4a scans only what is DEPLOYED. Cost of nothing: a reintroduced client write (or a
  stale committed bundle) ships unnoticed until FLS refuses it in production.
- **PCF `webpack.config.js` aliases (6 edited byte-identical to open PR #1123, TrackingFieldTrio NEW) and #1123's two
  stub files** — Existing: #1123 is the same fix, adopted rather than re-invented (one mechanism); the stubs are its
  files. Cost of nothing: the seven controls cannot be rebuilt, so their committed bundles keep writing the pointer.
- No new package, column, PCF, plugin or Dataverse schema.

## 20.8 Seeding (three seeded builds, 2026-10-04; restored from byte copies, files touched)

Build A: 17 seeds in disjoint code → **29 of 253** targeted tests red. Build B: 7 seeds whose code overlaps A's →
**24 of 253** red. Build C (after the attach creator change): 2 seeds → **5 of 111** red. Every seed bit; S16 (A) was
too weak (its fallback also refused) and was replaced by S16b in B. No `SEED-166F1` marker remains (Grep).

| Seed | Red tests (examples) |
|---|---|
| S1 root-owned subtree = whole environment | `ARootOwnedDocument_MayUseOnlyAContainerTheRootItselfStamps`, `CustomerSubtree_IsTheOwnersTopLevelUnitAndEverythingBeneathIt` |
| S2 (B) strict flag ignored | `Strict_RefusesAnotherContainerOfTheSameCustomer_ThatTheInterimRuleAllows`, `Strict_AnUndecidableDocument_IsRefused`, `Strict_DecidesByContainer_NotByUploader_…` |
| S3 secure answer does not dominate | `ASecureAnswer_Dominates_ANonSecureLinkAddsNoContainer` |
| S4 party links consulted | `APartyLink_OwnsNoContent_TheDocumentIsPlacedAsUnfiled` |
| S5 strict: item existence not checked | `Strict_AnItemNotInTheDerivedDrive_IsRefused` |
| S6 attach: creator not checked | `Attach_ByAWriterWhoDidNotCreateTheRow_IsRefused`, `Attach_ToARowTheBffCreated_…` |
| S7 (B) attach: derived container not checked | `Attach_AFileOutsideTheDerivedContainer_IsRefused` (3), `…_Is409_WithTheAttachReasonCode` |
| S8 attach: uploader not checked | `Attach_AFileSomeoneElseUploaded_IsRefused`, `…_Is403` |
| S9 attach: an existing file is replaced | `Attach_ToARowThatAlreadyHasAnotherFile_IsRefused_…`, `Attach_TheSameFileAgain_IsIdempotent_…` |
| S10 relocate: copy not verified | `Relocate_WhenTheCopyDoesNotVerify_…`, `Relocate_WhenTheHashDiffers_…` |
| S11 (B) source deleted before the re-point | `Relocate_CopiesThenVerifiesThenRepoints_AndOnlyThenDeletesTheSource`, `Relocate_WhenTheRepointFails_…` |
| S12 other references ignored | `Relocate_WhenAnotherDocumentStillPointsAtTheSource_KeepsTheSource`, `RelocateDocuments_CountsEveryOutcome_…` |
| S13 (B) legacy legitimacy not checked | `Relocate_AFileThatIsNotVerifiablyTheRowsOwn_IsNeverCopied`, `AFileBothRulesRefuse_IsListedForAnAdministrator_…` |
| S14 Make Secure exemption for any target | `Relocate_MakeSecure_DoesNotWidenToANonSecureTarget` |
| S15 migration: newly-refused not counted | `AReportOnlyRun_PlansTheMoves_CountsTheFlipBlockers_AndWritesNothing` |
| S16b (B) an unlisted workspace is allowed | `AnAction_OnARowWhoseWorkspaceThisDeploymentDoesNotAllow_…` (6), `GetReports_OmitsARowWhose…` |
| S17 unconfigured allow-list not refused | `WithNoAllowedWorkspaceConfigured_EveryActionIs503_AndReadsNothing` (6), `GetReports_WithNoAllowedWorkspaceConfigured_Is503` |
| S18 (B) customer binding ignored | `EmbedToken_ForACustomerBoundWorkspace_…(False)`, `ACustomerBoundWorkspace_WithNoHierarchyReader_…` |
| S19 create writes the pointer as the caller | `Create_FromAReadableSource_ClonesInTheSourcesWorkspace_AndRegistersTheRowAsTheCaller` |
| S20 no RLS identity in the real export request | `ExportReportAsync_TheRealRequestItSends_CarriesTheCallersRlsIdentity_OnTheReportsDataset` |
| S21 attach route without the Write filter | `Attach_WithoutWriteOnTheDocument_Is403_AndNothingIsReadOrStamped` (None / Read) |
| S22a memo off / S22b (B) memo remembers a failure | `OneScope_ReadsTheHierarchyAndEachContainersClaimantsOnce_ForManyChecks` / `AFailedHierarchyRead_IsNotRemembered_…` |
| S23 (C) the row's creator ignores `sprk_createdbyperson` | `Attach_ToARowTheBffCreated_FollowsTheRecordedPerson_…(True)`, `ABffRow_WhoseItemItsRecordedPersonUploaded_IsAllowed` |
| S24 (C) attach skips the creator check | `Attach_ByAWriterWhoDidNotCreateTheRow_IsRefused`, `Attach_ToARowTheBffCreated_WithNoRecordedPerson_IsRefused`, `…FollowsTheRecordedPerson_…(False)` |
| natural: stale committed PCF bundles | `ClientDocumentPointerWriteGuardTests.NoClientWritesADocumentPointer` (7 bundles) until rebuilt |

## 20.9 Gates (this round, final code)

| Gate | Result |
|---|---|
| Affected tests (before the final runs) | relocator + migration + strict + pointer-check + attach contract: 117 passed, 0 failed (after the row-creator change); reporting set: Spe.Integration reporting 40 passed; arch guards (provenance, flat upload path, client pointer writes): 30 passed |
| Full BFF unit suite (`tests/unit/Sprk.Bff.Api.Tests`) | **Run 1: ABORTED** — the test host crashed with `Internal CLR error (0x80131506)` inside `DocumentFormat.OpenXml … WalkRelationships` during a Compose create-on-save test (code this round does not touch); the partial tally was 14,368 total / 14,321 passed / 47 skipped / 0 failed. **Run 2 (same build, `--no-build`): 14,606 total: 14,552 passed, 54 skipped, 0 failed** (25 m 27 s; +97 cases vs r2's 14,509). Both results reported per the rule. |
| NetArchTest (`tests/Spaarke.ArchTests`) | **359 passed, 0 failed** (r2: 346; + `ClientDocumentPointerWriteGuardTests`) |
| Sprk.Bff.Api.IntegrationTests (full) | **104 passed, 0 failed** |
| Spe.Integration.Tests (full) | **405 total: 380 passed, 25 skipped (environment-gated `SkippableFact`s), 0 failed** |
| BFF build | Debug (tests) and **Release** succeeded, 0 errors (warnings as errors) |
| `dotnet list package --vulnerable --include-transitive` | `Sprk.Bff.Api` has no vulnerable packages |
| `dotnet format whitespace --verify-no-changes` on the changed C# files | only end-of-line findings on files written with LF; normalized to CRLF (`.gitattributes` `*.cs eol=crlf`; storage stays LF) — no content change |
| Client: `Spaarke.SdapClient` jest | **30 passed** (2 suites) |
| Client: `Spaarke.UI.Components` jest (full) | 236 suites: 227 passed, 9 failed; 3,376 tests: 3,362 passed, 14 failed. **8 of the 9 failing suites (13 tests) fail identically on the base `8e0c89f33`** (WorkspaceShell `buildDynamicWorkspaceConfig`, `todoScoreMappings`, `surfaceLaunchRegistry`, RecordHeader `configResolution`, `RichFilePreview`, `TimelineComposeBox`, ConversationView `emailInFlow` / `forward`; pre-existing — open PR #1123 is the master-side repair of UI.Components suite failures); the 9th, `AccessGrantModal.userShare` (untouched), is a load-timing flake: 77 / 77 pass when the `AccessGrantModal` folder is re-run. Every suite this round changed passes (`DocumentRecordService.payload` 15, `EntityCreationService.multibind` 10, `todoService.upload` + `invoiceService.resolver` 22) |
| Client: `DocumentUploadWizard` | `npm run build` (vite) **green**; jest **11 passed**; `tsc --noEmit` 15 errors, none in `uploadOrchestrator.ts` (all in untouched wizard files, plus the pre-existing `ComponentFramework` namespace error on `PcfDataverseClient.ts` line 25, which this round no longer changes) |
| PCF `npm run build:prod` (7 controls) | all seven **`[build] Succeeded`**, 0 `ERROR in` (final build with #1123's loud stubs, after a fresh `Spaarke.UI.Components` `tsc` build of `dist`); each new bundle has 0 `sprk_graphitemid` occurrences and is copied to `Solution/Controls/sprk_Spaarke.Controls.<X>/bundle.js`; NetArchTest re-run on the rebuilt bundles: 359 passed. (The first rebuild attempt showed the trap: CommunicationActions / CommunicationConnections logged `[build] Failed` while `npm run build:prod` exited 0.) |
| Publish size (CLAUDE.md §10; fresh short-path worktrees, `dotnet publish -c Release`, PowerShell `Compress-Archive -CompressionLevel Optimal`, PDBs included) | base `8e0c89f33` **45.684 MB** (47,903,114 bytes, 212 files — equal to the verifier's r2 figure) vs branch `6d6b10def` **45.714 MB** (47,934,610 bytes, 212 files) = **+0.030 MB** (+31,496 bytes; one service, one job, one route, no package). Both sides from FRESH short-path worktrees (`C:\wt6m`, `C:\wt6f`; CLAUDE.md §10 hazards three and four), equal file counts. Ceiling 60 MB; far below the +5 MB escalation threshold |
| /conflict-check (2026-10-04) | 20 open PRs: one overlap, **#1123** (task 092, master build/test baseline repair) on the six Communication* `webpack.config.js` — resolved by adopting #1123's files byte-identically (+ its two stub files). Master since the merge base (`b8026dfa8`, master @ `6932582b1`): **11 overlapping files**, textual merges at integration — `cleanGuid` refactors in `DocumentRecordService.ts`, `ODataDataverseClient.ts`, `EntityCreationService.ts`, `createXrmEmailComposeHandlers.ts`, `document-upload/index.ts` / `types.ts`, `uploadOrchestrator.ts`; `DriveItemOperations.cs`; `WorkspaceTab.ts`; the deployment guide; and master DELETES `PcfDataverseClient.ts` (reuse audit C-26), so this round leaves that file unchanged (no modify/delete conflict). BFF hot path shared with other active worktrees: soft warn, no hard conflict. |

## 20.10 Route authorization ledger input — f1 rows (route key as the guard spells it)

| Route key | Mechanism that now decides | Deny test (FQN) |
|---|---|---|
| `POST /api/v1/documents/{id}/file` (NEW) | `DocumentAuthorizationFilter("write")` on `{id}` + `DocumentContainerRelocator.AttachFileAsync` (first file only; the row's creator; derived container; the caller's own upload) | `Sprk.Bff.Api.Tests.Api.Documents.DocumentFileAttachContractTests.Attach_WithoutWriteOnTheDocument_Is403_AndNothingIsReadOrStamped` |
| `GET /api/reporting/embed-token`, `POST /api/reporting/export`, `GET /api/reporting/reports/{reportId:guid}`, `PATCH /api/reporting/reports/{reportId:guid}`, `DELETE /api/reporting/reports/{reportId:guid}`, `POST /api/reporting/reports` | r1/r2 mechanisms + the allowed-workspace check (503 unconfigured; uniform 404 for an unlisted / customer-foreign workspace) | `Sprk.Bff.Api.Tests.Api.Reporting.ReportingCatalogBindingContractTests.AnAction_OnARowWhoseWorkspaceThisDeploymentDoesNotAllow_IsTheUniform404_AndActsOnNothing` |
| `GET /api/reporting/reports` | rows of unlisted workspaces omitted; 503 unconfigured | `…ReportingCatalogBindingContractTests.GetReports_OmitsARowWhoseWorkspaceThisDeploymentDoesNotAllow` |
| `POST /api/admin/jobs/{jobId}/trigger`, `GET /api/admin/jobs/{jobId}/history` | unchanged (`SystemAdmin` group policy); f1 adds only a job id | existing jobs-endpoint tests |
| download routes of §18.3 | unchanged call sites; the check is now interim OR strict per the flag, root-owned rows confined | `Sprk.Bff.Api.Tests.AccessControl.DocumentContainerStrictRuleTests.Strict_RefusesAnotherContainerOfTheSameCustomer_ThatTheInterimRuleAllows`; `…DocumentPointerContainerCheckTests.ARootOwnedDocument_MayUseOnlyAContainerTheRootItselfStamps` |

## 20.11 Manual live gates — f1 (main session, dev; in THIS order; ids redacted to 8 chars where not config)

20. **Deploy the BFF** from the integrated branch (`bff-deploy`). Then set the allowed workspaces (an app-setting change
    restarts the app): `az webapp config appsettings set -g spe-infrastructure-westus2 -n spe-api-dev-67e2xz --settings
    PowerBi__AllowedWorkspaces__0__WorkspaceId=<the dev Power BI workspace id>` (add `__CustomerBusinessUnitId` only for
    a customer-bound workspace). Until set, every reporting route answers 503 `sdap.reporting.config.workspaces_unconfigured` — by design.
21. **Redeploy every client** that bundles the shared library, from the same branch: the code pages (`sprk_*wizard`,
    `sprk_spaarkeai`, `sprk_emailpage`, `sprk_externalworkspace`, `sprk_documentuploadwizard`, … — the FLS dry run below
    prints the exact list), the form script `spaarke_documents/DocumentOperations.js`, and the seven PCFs of §20.3 via
    the `pcf-deploy` skill (it bumps `ControlManifest.xml` versions). **Remove the retired `UniversalDatasetGrid` PCF**
    still deployed on dev (its source is gone from the repo; its bundle writes the pointer).
22. **Pointer-check census** (supersedes §18.12 gate 15; extends gate 17): download one document of each §20.5 class and
    read `[DOCUMENT-POINTER] REFUSED` in the BFF log; the refusals must be exactly the classes marked expected.
23. **Document pointers FLS**: `pwsh scripts/Set-DocumentPointerFieldSecurity.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c`
    (dry run: p4a must list NO web resource) → the same with `-ClientNoLongerWritesPointers -Apply` → the same with
    `-Verify` (exit 0).
24. **Legacy migration**: `pwsh scripts/Invoke-DocumentContainerMigration.ps1 -BffBaseUrl https://spe-api-dev-67e2xz.azurewebsites.net -ApiScope api://1e40baad-e065-4aea-a8d4-4b7ab273458c/.default`
    (dry run; the reports are the census of gate 22 — review `SourceUnverified` / `Undecidable` rows) → the same with
    `-Apply -ResourceGroup spe-infrastructure-westus2 -AppName spe-api-dev-67e2xz` (~~FIRST decide §20.12's RAG item —
    or re-index the relocated ids afterwards~~ decided by round 37 item 1 and built in f1-v1, §21.5.2: the relocation
    re-indexes itself; run gate 23a of §21.11 first) → the same with `-Verify` (exit 0 only when nothing would move,
    nothing failed and the strict rule would newly refuse nothing — f1-v1 adds: nothing owed, no relocated file refused).
    Single instance only.
25. **Strict rule ON** (only after gate 24's `-Verify` exits 0): `az webapp config appsettings set -g spe-infrastructure-westus2 -n spe-api-dev-67e2xz --settings DocumentPointer__StrictDerivedContainer=true`;
    re-download one document per class (all served from their derived containers).
26. **Report catalog FLS** (after gate 20): `pwsh scripts/Set-ReportCatalogFieldSecurity.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c`
    (dry run) → `-BffWritesCatalogPointers -Apply` → `-Verify` (exit 0). Then a Save As from the Reporting page creates
    a row whose four pointer columns the BFF stamped, and an MDA edit of `sprk_workspaceid` by a non-admin is refused.
27. **Attach**: upload through the DocumentUploadWizard and one Create*Wizard as a non-admin → the row gets its pointer
    via `POST /api/v1/documents/{id}/file` (BFF log `[DOCUMENT-ATTACH]`); a scratch attempt to attach another person's
    item → 403, row unchanged.

## 20.12 Found gaps, `.claude` edit, integration notes

**Found, not owned by 166 (round 15: never dropped):**
- **🔔 RAG index entries of a relocated file keep the OLD item id — needs a main-session decision before gate 24's
  `-Apply`.** *(Decided by owner round 37 item 1 and BUILT in f1-v1 — §21.5.2; kept below as found.)* `FileIndexingService` keys chunks `{speFileId}_{index}` and stores `SpeFileId`; after a move the
  document's chunks still name the deleted source item, and `SemanticSearchService` (line ~591) returns
  `SpeFileId = result.SpeFileId ?? doc.GraphItemId` — the stale index value — next to the row's NEW `DriveId`, so a
  per-result AI action on a relocated document asks for an item that is not in that drive (the pointer check refuses
  it, 409) until the document is re-indexed; a plain re-index would ADD `{newItem}_{i}` chunks beside the stale
  `{oldItem}_{i}` ones (duplicates). NOT fixed here: the complete fix reaches into the AI indexing pipeline, and CRUD code
  may only reach AI capability through a `Services/Ai/PublicContracts/` facade (CLAUDE.md §10 bullet 3) — there is no
  indexing facade yet, so the fix is NEW AI-hot-path surface (CLAUDE.md §6 scope expansion). **Proposed complete fix:**
  a PublicContracts facade (e.g. `IDocumentIndexRelocation.ReindexMovedFileAsync(documentId, oldItemId, newDriveId,
  newItemId)`) that enqueues the existing app-only `RagIndexing` job for the new item (`ReplaceStaleChunks = true`; the
  copy IS BFF-written, which is that path's precondition) and deletes the old item's chunks
  (`IRagService.DeleteChunksBeyondCountAsync(tenant, oldItemId, 0, index)` in the document's index, and the discovery
  index); `DocumentContainerRelocator.RelocateAsync` calls it after a successful re-point (best effort, logged). Interim
  alternative if the decision lags: run gate 24's `-Apply`, then re-index every `Relocated` / `RelocatedSourceKept` id the
  run reports through the existing "send to index" path. The same stale-chunk gap already exists for a document DELETE
  (pre-existing, not 166's).
- **SPE version history is not carried by a relocation copy** (Graph has no cross-container move for SPE; the copy is
  the current version). Recorded as the documented residual of round 26 item 3. *(f1-v1 §21.12 raises it as a decision
  request with the complete fix proposed.)*
- **The Communication* PCFs stub `@spaarke/sdap-client`** (pre-existing `false` alias), so any local-upload path inside
  those controls cannot work; they never call `attachDocumentFile`. No pointer write remains in them.

**`.claude` edit (main session only):** §12's `bff-deploy/SKILL.md:50` edit still stands; none new.

**Integration notes:**
- Task 150's lane: Make Secure calls `DocumentContainerRelocator.RelocateDocumentsAsync(documentIds, recordContainerId,
  RelocationPurpose.MakeSecure, apply: true)`; `Complete == false` → 500 `sdap.provision.files_incomplete` with
  `Counts` / `Incomplete` (round 26 item 3).
- `RecordContainerResolver.CreatedByPersonColumn` → task 146's `Spaarke.Dataverse.RecordCreatorPersonColumn.LogicalName`
  at integration (unchanged note, §19.13). Round 28 (E1: browser-initiated child creates through G5 with
  `sprk_createdbyperson`) is compatible with the attach route: the row's creator is that recorded person.
- Task 167's guard: add the §20.10 rows.
- **PR #1123 (task 092):** the six Communication* `webpack.config.js` and `src/client/pcf/shared/stubs/*` are
  byte-identical to its head `ec247e9dc`; if #1123 changes before it merges, take #1123's version and rebuild the seven
  bundles (`npm run build:prod`). Its stub comments say "six Communication* PCFs"; TrackingFieldTrio is a seventh user.
- **Master overlap:** 11 files, textual merges (§20.9 conflict-check row); `PcfDataverseClient.ts` is deliberately left
  unchanged here because master deletes it.
- `NOTE-FROM-MAIN.md` (round 26 item 3) was read first and deleted, not committed.

## 20.13 Not closed

- **Live writes (by design, main session):** gates 20–27 (§20.11) — deploys, app settings, the two FLS `-Apply` /
  `-Verify` runs, the migration `-Apply` / `-Verify`, the strict-rule flag. Nothing was written live; the only live
  access this round was the read-only FLS dry run against dev (p4a evidence, §20.2 item 1 (iii)).
- ~~**🔔 Escalated (found, not one of the 21 items):** the RAG re-key of a relocated file (§20.12)~~ — decided by owner
  round 37 item 1 and built in f1-v1 (§21.5.2).
- Nothing else is owed by task 166.

# f1-v1 (2026-10-05): the f1 verification's findings F1–F4 closed; owner round 37 implemented

> **Branch**: `task/uac-r2-166-f1-v1` from `task/uac-r2-166-f1` @ `ca8291442`
> **Inputs (BINDING)**: rounds 1–39 on `work/unified-access-control-r2` (`notes/session27-owner-decisions-and-research.md`).
> **Round 37 is this verification's answer**: item 1 (a relocated file is re-indexed through ONE `Services/Ai/PublicContracts`
> facade and every reference to the old item is re-keyed — F4), item 2 (several rows naming the moved file; a source kept
> for another record is complete; re-entry settles — F2), item 3 (the interim rule also serves a BFF-identity item that
> passes the strict derived-container test — F1). The verifier's 11-item list is §21.2.
> **Outcome**: every item is closed in code, tests, scripts and docs. What is left is LIVE writes only — a schema script, the
> FLS script (now also covering the ledger column) and the migration, each dry run / `-Apply` / `-Verify` with its exact
> command in §21.11. Two things FOUND while doing it are recorded for the main session in §21.12, one with a 🔔 decision
> request (SPE version history of a relocated file) and its complete fix proposed. Status `completed-with-escalation`
> for that one decision; every listed item is closed.
> `NOTE-FROM-MAIN.md`: none in the worktree.

## 21.1 Binding inputs as applied

| Decision | Implemented |
|---|---|
| Round 37 item 3 — the interim rule ALSO accepts an item uploaded by the BFF identity, only when its pointer passes the strict derived-container test; tests: a relocated file is served under the interim rule, a BFF-identity item in the wrong container is refused; seeded | §21.3 |
| Round 37 item 2 — every referencing row inside the secure record's subtree re-pointed to the copy through the pointer-attach path; a row outside keeps the source and the transition is COMPLETE; `SourceKeptForOtherRecords` with the row ids; the source deleted only when no row references it; re-entry recognises an already re-pointed row and settles it, never loops on and never reports incomplete for a source kept for another record | §21.4 (each moved row gets its OWN copy — the live unique key `sprk_graphitemid_uk` forbids a shared one; §21.4.3) |
| Round 37 item 1 — after each re-point, ONE PublicContracts facade method enqueues RAG indexing for the new item and deletes the old item's chunks; failure = `index-pending`, incomplete, retried by the repeat call; every reference to the old item re-keyed in the same step; the note lists each one | §21.5 (the inventory is §21.5.1) |
| The verifier's F3 (client guard completeness) | §21.6 |

## 21.2 The 11 items — what changed per item

| # | Item | Closure |
|---|---|---|
| 1 | **F1** — the interim rule refused every relocated file (BFF-identity copy on a person's row) | `RecordContainerResolver.IsAllowedUnderInterimRuleAsync`: when the round-23 halves refuse an item the BFF identity uploaded app-only, it is served only if `StrictRefusalAsync` (the derived-container test) passes. Effects (a)–(c) are gone: a migrated file, a Make Secure move and the -Verify gate are tested (§21.3). The job report no longer hides an interim/strict disagreement: `servedOnlyAfterFlip` (interim refuses, strict serves) is counted and listed, and `relocatedButRefused` (a moved file the rule IN FORCE refuses) makes the run unclean and fails `-Verify`. `Strict_DecidesByContainer_NotByUploader_…` now pins the round-37 behaviour (interim serves); the §20.5 census and the §21.11 gates say so |
| 2 | **F2** — re-entry reported Complete with the secure file's source still in the shared container | The row's relocation ledger `sprk_relocationpending`, written in the SAME update as the re-point; every call settles it first (§21.4). Probes P2 and P3 are now tests: `MakeSecure_ASourceKeptForAnotherRecord_IsComplete_AndARepeatCallDeletesItOnceThatRowIsGone`, `MakeSecure_WhenTheSourceDeleteFails_IsIncomplete_AndARepeatCallDeletesIt` — each makes the repeat call its name claims. `RelocateDocuments_CountsEveryOutcome_…` (which never made its repeat call) is replaced by them. The migration-purpose untracked duplicate is tracked the same way (the next pass deletes it) |
| 3 | **F3** — the guard missed bracket, dotted, computed-key and constant-through writes; InlineData #3 proved less than it appeared | `ClientDocumentPointerWriteGuardTests` and the FLS script's p4a (`Find-PointerWrite`) detect every static shape, directly or through a constant (§21.6). Case #3 split into single-shape cases; 20 write cases, 14 read cases. Seeded six ways (the finding's own `EntityCreationService.ts` bracket write first) — each red |
| 4 | **F4** — a relocation re-keyed only the four pointer columns | Every reference re-keyed in the same step (§21.5.1 lists each one, with its live count), and the index re-keyed through `IRelocatedFileIndexing` (§21.5.2). SPE version history: §21.12 🔔 |
| 5 | Verified MET (attach route, relocator order, job, strict flag, allowed workspaces, export builder, dead code) | Unchanged and still MET. The relocator's order is copy → verify → re-point (now with the row's re-keyed own columns and its ledger entry in that one update) → settle (re-key, source, index). It still never deletes before verifying, never copies a forged pointer, and keeps the MakeSecure exemption bounded to a secure derivation |
| 6 | The verifier's seeding (15 seeds) | Recorded. This round's own seeding: §21.8 |
| 7 | The verifier's full runs | Recorded. This round's: §21.9 |
| 8 | Hygiene | Holds: the POML parses as XML; no `NOTE-FROM-MAIN.md`; no `.claude/` file in the diff; the TASK-INDEX row is untouched; no script was run against a live environment (read-only metadata/SQL only, §21.5.1) |
| 9 | Criterion — round 26 item 3 "a repeat call completes it" | **Met.** §21.4 |
| 10 | Criterion — round 21 item 1 (ii)/(iii) + round 26 item 3 "moved files remain servable" | **Met.** §21.3 |
| 11 | Criterion — client pointer-write guard completeness | **Met for every statically writable shape** (§21.6). A key computed at run time (string concatenation, a loop over names, a value from another module's function) cannot be detected statically; the field-level-security lock is the control for it, and the guard's remarks say so |

## 21.3 F1 / round 37 item 3 — the interim rule serves what the BFF placed

- **The rule.** `IsAllowedUnderInterimRuleAsync` = (the round-23 container half AND item half) OR (the item was uploaded
  app-only by the BFF identity — no user, an application id under `BffApplicationIdKeys` — AND `StrictRefusalAsync` passes:
  the drive is a container derived for this document and the item is in it). The second disjunct admits nothing the strict
  rule refuses, so the interim rule is never weaker than the strict rule; its residual is the strict rule's (a pre-lock
  forged pointer to another BFF-placed item of the SAME derived container). The halves were refactored into
  `InterimRefusalAsync` / `StrictRefusalAsync` (reason-returning, no logging), so a served BFF item logs one INFORMATION
  line and a refused one exactly one REFUSED line naming both reasons. `IsAllowedUnderStrictRuleAsync` is unchanged in
  behaviour (it now calls `StrictRefusalAsync`).
- **Tests** (all on the REAL resolver): `ARelocatedFile_IsServedUnderTheInterimRule_BeforeTheStrictFlip` (probe P1 —
  precondition: the interim rule serves the file before the move; after it, the copy), `AMakeSecureRelocation_IsServedUnderTheInterimRule`,
  `Strict_DecidesByContainer_NotByUploader_…` (interim now serves), `Interim_ABffIdentityItemOutsideTheDerivedContainer_IsRefused`
  (the owner's own customer container AND another customer's), `Interim_AnAppOnlyItemOfAnotherApplication_InTheDerivedContainer_IsRefused`,
  `Interim_ABffIdentityItemOfAnUndecidableDocument_IsRefused`, `APersonsRow_WhoseItemTheBffUploadedAppOnly_IsServedOnlyInItsDerivedContainer`
  (2 cases; replaces r2's `…_IsRefused`, which pinned the pre-round-37 answer); job: `AWriteRun_RelocatesTheMisplacedFile_AndTheNextRunIsClean`
  asserts `interim = true`, `relocatedButRefused = 0`; `ARelocatedFileTheRuleInForceRefuses_MakesTheRunUnclean`;
  `TheReport_CountsAndListsADocumentOnlyTheStrictRuleWouldServe`.
- **Census (§20.5) as it now reads.** Row 2 of the census ("BFF-created row, BFF upload") is unchanged; a NEW served class:
  **a person's row whose file the BFF placed in its derived container (every relocation copy) — interim: served (round 37
  item 3); strict: served.** The other expected-refusal classes are unchanged, and each is now counted in the report as
  `servedOnlyAfterFlip` when the strict rule would serve it.

## 21.4 F2 / round 37 item 2 — the relocation ledger, re-entry, and several rows

### 21.4.1 The ledger

`sprk_document.sprk_relocationpending` (Multiple lines of text, 4000; created by `scripts/Set-DocumentRelocationSchema.ps1`;
field-secured with the pointer columns). JSON `{ "v": 1, "entries": [ { sourceDrive, sourceItem, source: pending |
keptForOtherRecords | removed | delegated, rekeyPending, indexed: none | own | all, at } ] }` — one entry per OLD item
whose move still owes something. It is written by the re-point itself (`WritePointerAsync`, the one pointer writer, now
carrying the ledger and the re-keyed own columns in the SAME Dataverse update), so no crash can leave a re-pointed row that
forgot its debt. An entry is dropped when it is complete (source removed or delegated, re-key done, index re-keyed to the
scope the source state requires); a `keptForOtherRecords` entry stays — settled, never incomplete — so a later pass can
delete the source once nothing references it (round 37: "the source is deleted only when no row references it any more").

### 21.4.2 Settling (every call, before anything else)

`RelocateIfMisplacedAsync` reads the row (pointer + ledger), SETTLES the ledger against the row's current file, then
derives and moves as before. Per entry: (a) **re-key** the rows that hold the old item for this document (§21.5.1);
(b) **source**: gone → removed; still used by a row whose derived container is NOT where this document's file now is →
kept for that record (listed); used by a row that belongs WITH the moved file → moved along (§21.4.3); unreadable /
undecidable / a move-along that failed → pending; unreferenced → deleted — on a REPEAT call only when the source is
byte-identical to the document's current file (size AND `quickXorHash`, both present), because the ledger is then the only
witness and a ledger entry must never become a delete of an unrelated file (`ARepeatCall_NeverDeletesASourceThatIsNotByteIdenticalToTheDocumentsFile`,
`ARepeatCall_WhenGraphReturnsNoHash_DoesNotDeleteTheSource`); (c) **index** (§21.5.2). The ledger is written back only
when it changed; a failed write is reported pending (every step is idempotent, the next call redoes only what it finds owed).
An UNREADABLE ledger is never acted on, and the file is not moved (moving would overwrite it): `ledger-unreadable`, Failed.
Report-only reports the ledger's debt without settling it. Without the column the row read fails, so nothing moves (fail
closed; `WithoutTheLedgerColumn_NothingIsMoved_FailClosed`).

**Outcomes.** `RelocatedSourceKept` is replaced by **`RelocatedSourceKeptForOtherRecords`** (settled, for BOTH purposes —
round 37) and a new **`RelocationPending`** (re-pointed now or earlier, something still owed; incomplete). Every outcome
carries `Pending` (`source-pending: …`, `rekey-pending: …`, `index-pending: …`, `ledger-unreadable: …`),
`KeptForOtherRecords` and `MovedAlong`. `DocumentRelocationBatchResult` gains `SourceKeptForOtherRecords`; an outcome is
settled only when its state is settled AND it owes nothing; a moved-along row that owes something is listed in
`Incomplete` too.

### 21.4.3 Several rows naming the moved file — one copy per row

Round 37 item 2 says the in-subtree rows are "re-pointed to the copy". **`sprk_graphitemid_uk` is a UNIQUE key on
`sprk_graphitemid` alone, `Active` on spaarkedev1 (read 2026-10-05: `EntityDefinitions(…)/Keys`), and no two dev rows share
an item (SQL `GROUP BY sprk_graphitemid`, max 1).** So while the key is Active the multi-row case cannot occur at all, and
where it does occur (a degraded environment whose key build failed over duplicates) re-pointing several rows to ONE copy
would recreate exactly the duplicate set that blocks the key — and `scripts/Repair-ComposeIdentityKey.ps1` would then clear
the pointer on all but the oldest row. Each in-subtree row is therefore moved with its OWN copy (`MoveAlongAsync`: the same
legitimacy rules, its ledger entry `delegated` — one owner of the source delete). The intent of the decision is unchanged:
every in-subtree row ends in the secure container, through the pointer-attach path; out-of-subtree rows keep the source.
Recorded here as the decision the live key forces, not a deviation of intent.

### 21.4.4 One writer per document

Two concurrent relocations of the same row (a double-clicked Make Secure, or Make Secure meeting the migration pass) would
each copy and re-point, and the loser's copy and ledger entry would be lost. `RelocateIfMisplacedAsync` (write mode) and the
move-along take the EXISTING ADR-004 processing lock (`IIdempotencyService`, key `document-relocate-{id:N}`, 10 minutes,
released in `finally`); a held lock is reported (Failed, incomplete — the repeat call settles), never waited on; a lock that
cannot be taken is treated as held (fail closed). Tests: `ARelocationOfADocumentAnotherRelocationHolds_MovesNothing_AndIsIncomplete`,
`TheRelocationLock_IsReleased_EvenWhenTheMoveFails`, `ARowThatCannotBeMovedAlong_BecauseAnotherRelocationHoldsIt_KeepsTheSourcePending`.

### 21.4.5 The job and the driver

`DocumentContainerMigrationJob` is a repeat caller too (each document's ledger is settled when the pass reaches it, write
mode). Report: `servedOnlyAfterFlip`, `relocatedButRefused`, `pending`, `movedAlong`, `sourceKeptForOtherRecords`; listed
rows carry `pending` / `movedAlong`. Clean = no `wouldNewlyRefuse`, no `relocatedButRefused`, no `pending`, no `Failed`, no
`WouldRelocate`. `scripts/Invoke-DocumentContainerMigration.ps1` totals and prints them and `-Verify` exits 0 only when
`WouldRelocate`, `Failed`, `RelocationPending`, `pending`, `relocatedButRefused` and `wouldNewlyRefuse` are all 0.

## 21.5 F4 / round 37 item 1 — every reference re-keyed; the index follows the move

### 21.5.1 The inventory (read-only on spaarkedev1, 2026-10-05: Dataverse metadata of every entity's string columns whose name holds an SPE id, SQL counts; and the code that writes or reads each)

| Where | Holds | Live (dev) | Writer / reader in code | Re-keyed |
|---|---|---|---|---|
| `sprk_document.sprk_graphdriveid` / `sprk_graphitemid` / `sprk_filepath` / `sprk_hasfile` | the pointer | 530 pointered | the BFF (FLS) | yes — the re-point (f1) |
| `sprk_document.sprk_driveitemid` | the item id (legacy) | 9 | no writer; READ by the Insights observation mirror (`DataverseObservationMirror`, lookup by item) | yes, when it holds the old item → the copy, in the re-point update |
| `sprk_document.spk_fileviewerid` | an item-keyed viewer id | 0 | none (listed in `ExternalModuleRegistry.PointerColumns`) | yes, when it holds the old item → the copy |
| `sprk_document.sprk_containerid` | the drive id (legacy; design keeps it null) | 28 | read by `DataverseServiceClientImpl` (document DTO, container list) | yes, when it holds the old drive → the target |
| `sprk_document.sprk_parentfolderid` | the folder the item sat in | 0 | none | yes, when set → the copy's parent (cleared if Graph returns none) |
| `sprk_document.sprk_etag` | the item's eTag | 1 | none | yes, when set → the copy's eTag (cleared if none) |
| CHILD `sprk_document.sprk_parentgraphitemid` (`sprk_parentdocument` = the moved row) | the parent's item id | 102 | `UploadFinalizationWorker` (email attachments) | yes — rows with `sprk_parentdocument` = the document AND the old item → the copy (another parent's child is untouched) |
| `sprk_communicationattachment.sprk_graphdriveid` / `sprk_graphitemid` (`sprk_document` = the moved row) | the attachment's own pointer (mirrors its document's) | 78 with an item, 77 equal to a document's | written by the communication pipeline; READ app-only by the `.eml` embed (`CommunicationService.FetchEmlAttachmentsForEmbedAsync`) and by archive (`ArchiveExistingAttachmentsAsync`) | yes — rows linked to the document AND naming the old item → (target, copy). An UNLINKED attachment row naming the source is a communication's own record of that file: it keeps the source (listed `sprk_communicationattachment:{id}`) |
| RAG knowledge index (chunk id `{speFileId}_{i}`, field `speFileId`, `documentId`; no drive field — `KnowledgeDocument`) | the item id | — | `FileIndexingService` | yes — §21.5.2 |
| Insights observations (`spe://drive/{d}/item/{i}` evidence) | drive + item, IF the files-index chunk carries `driveId`/`itemId` | — | `FilesIndexIngestDocumentSource` | not stored by the file pipeline (`KnowledgeDocument` has no such fields), so production refs are `file://{documentId}` — stable across a move. Nothing to re-key |
| Session-files index (`{documentId}_s_{i}`) | the chat-document GUID as `speFileId` | — | `ChatDocumentEndpoints` | not an SPE item id; not a `sprk_document`'s file. Nothing to re-key |
| `sprk_fileversion` | version metadata (number, comment, dates) | — | `DocumentCheckoutService` | holds no SPE id (live schema). Nothing to re-key |
| `sprk_analysisworkingversion.sprk_driveid` / `sprk_itemid` | its own working files | 0 equal to a document's item | none in `src` | not a document's file |
| `sprk_analysis.sprk_containerid` (and other tables' `sprk_containerid`) | a RECORD's container (business unit / secure record) | — | wizards / provisioning | record-level, not a file reference |
| `sprk_document.sprk_attachments` | email attachment metadata | 0 | none | empty; holds no item id |
| Compose sessions (`documentSpeId`, Redis) | the open item | transient | `ComposeService` | an open session on the old item resolves its drive through the `sprk_document` row by item (`TryResolveRecordedDriveIdAsync`): no row names the old item after the move, so the save is REFUSED (fail closed) and the user reopens the document. No write can reach the old item |
| RAG idempotency keys `rag-index-{drive}-{item}` (Redis, 7 days) | drive + item | transient | `RagIndexingJobHandler` | keyed by the NEW item for the copy; the old key expires. Nothing to re-key |
| SPE version history of the item | content of older versions | — | `GET /api/documents/{id}/versions` | **not carried by a copy — §21.12 🔔** |

Tests: `Relocate_ReKeysTheRowsOwnColumnsThatHeldTheOldIds_InTheSameUpdateAsThePointer`, `Relocate_LeavesAnOwnColumnThatHeldSomethingElse`,
`Relocate_ReKeysAChildAttachmentsParentItem_AndTheCommunicationAttachmentRow` (another parent's child untouched),
`ACommunicationsOwnAttachmentRecord_KeepsTheSource_ForThatCommunication`, `Relocate_WhenAReKeyFails_IsPending_AndARepeatCallCompletesIt`.
The legacy own columns are read BEFORE any byte moves; an environment without one of them ("doesn't contain attribute")
simply has no reference there.

### 21.5.2 The index — ONE PublicContracts facade method

No existing `Services/Ai/PublicContracts` facade indexes or un-indexes a file (checked: none of the 63 files declares an
indexing method), so — as round 37 allows — ONE new interface with ONE method: `IRelocatedFileIndexing.ReindexRelocatedFileAsync(RelocatedFileIndexRequest)`
(`Services/Ai/PublicContracts/IRelocatedFileIndexing.cs`), implemented by `RelocatedFileIndexing` over the EXISTING seams:
(1) `IPostUploadIndexingEnqueuer.EnqueueAppOnlyIfApplicableAsync` for the new item (the app-only `RagIndexing` job — the
copy is BFF-written, that path's precondition; tenant from `TENANT_ID` / `AzureAd:TenantId` like every app-only producer);
then (2) the old item's chunks removed by the new `IRagService.DeleteSupersededFileChunksAsync` (all of them once the old
item is gone; only those attributed to THIS document while it is another record's file), from the index the document was
last stamped into (`sprk_searchindexname`) and the tenant default. The old chunks are removed only after the enqueue
succeeded; a failed or switched-off enqueue (`FeatureFlagDisabled`, `MissingTenantId`, `MissingSpeIdentifiers`) leaves
them and answers pending, so a document is never made unsearchable by a re-index that will not come; a non-indexable file
still has its old chunks removed; an index the allow-list no longer admits is skipped (unreachable for search too); AI off
(`NullRagService`) = nothing was ever indexed = settled. `DeleteSupersededFileChunksAsync` shares ONE deletion core with
`DeleteChunksBeyondCountAsync` (the trim's structural rule — never below chunk 1 — is unchanged; its message is unchanged).
The relocator (CRUD code) injects only the facade (ADR-013). Failure → `index-pending`, incomplete, retried by the repeat
call (round 37). Tests: `RelocatedFileIndexingTests` (8 methods, 10 cases), `RagServiceChunkTrimTests` (+3), `Relocate_ReindexesTheNewItem_AndRemovesAllTheOldItemsChunks_OnceTheSourceIsGone`,
`Relocate_WhenTheIndexStepFails_IsIndexPending_Incomplete_AndARepeatCallRetries`; the P2 test pins the scope sequence
(this document's chunks while the source is kept, all once it is deleted).

## 21.6 F3 — the client pointer-write guard

The columns: `sprk_graphdriveid`, `sprk_graphitemid` and now `sprk_relocationpending`. Shapes (C# guard and the FLS
script's `Find-PointerWrite`, kept identical — a 34-case harness ran both): object key; computed literal key
`{ ["…"]: v }`; bracket assignment `x['…'] = v` (incl. `??=`, `||=`, `&&=`); dotted assignment `e.sprk_graphdriveid=t`;
`getAttribute(…)` / `attributes.get(…)` `.setValue` / `?.setValue`; and the computed-key, bracket and setValue shapes
THROUGH a name bound to a column string (`const F = "…"` / `{ ITEM: '…' }`) — bound in the same file, or (source files)
in any other client source file (an exported constant). A minified bundle's one-letter names count only inside that
bundle. Reads never match: `$select`, property access, bracket lookups incl. a ternary arm, `===`/`==`/`!==`, a live
minified read (`(t=e.sprk_graphitemid)`), a read through a constant. The repo scan stays green (no client write exists).
The FLS script also gains (p7): every column to lock exists (the ledger column after the schema script), and the
DocumentPointers target locks the ledger column with the pointer.

## 21.7 Placement (CLAUDE.md §10) and component justification (§11)

**Placement: in BFF**, and in the existing types except where round 37 names a facade. ADR-002: no plugin. ADR-003: every
new decision fails closed (an unreadable ledger is never acted on and blocks the move; an unreadable reference keeps the
source; a repeat-call delete needs byte identity; a lock that cannot be taken is held; no tenant / a failed enqueue keeps
the old chunks). ADR-007: no new Graph surface. ADR-010: one new 1:1 interface (below; `ADR010_DITests` documents it).
ADR-013: CRUD code reaches AI only through the PublicContracts facade. ADR-038: no `Mock<HttpMessageHandler>`, no
DI-registration or ctor-null tests. No new package, endpoint, option, job, PCF or plugin.

- **`IRelocatedFileIndexing` + `RelocatedFileIndexing` (Scoped, `AnalysisServicesModule`, UNCONDITIONAL next to the enqueuer)** —
  Existing: no PublicContracts facade indexes a file; `IPostUploadIndexingEnqueuer` and `IRagService` are AI internals.
  Extension: round 37 says extend a facade if one fits — none does; the implementation EXTENDS the existing seams (no new
  pipeline). Cost of nothing: a relocated file stays searchable only under the deleted item id (§20.12), and per-result AI
  actions on it fail.
- **`IRagService.DeleteSupersededFileChunksAsync`** (+ `RagService`, `NullRagService`, the seam test's in-memory index) —
  Existing: `DeleteChunksBeyondCountAsync` refuses to remove chunk 0 by design (a version re-index must never leave a file
  without chunks); `DeleteBySourceDocumentAsync` targets only the tenant default and keys by document. Extension: ONE shared
  private deletion core; the trim's rule and message are unchanged. Cost of nothing: the old item's chunks cannot be removed.
- **Column `sprk_document.sprk_relocationpending` + `scripts/Set-DocumentRelocationSchema.ps1`** — Existing: no column
  records what a move owes; `sprk_processingjob` is the Office job ledger (a separate write, not atomic with the re-point; a
  job type option it lacks). Extension: nothing on the row can hold it without misusing a column of another meaning. Cost
  of nothing: F2 — a re-pointed row forgets its source, its re-key and its index step, and a repeat call reports Complete.
- **Relocator members** — `RelocationLedgerColumn`, `ReKeyedOwnColumns`, the ledger types, `SettleOrReportAsync`,
  `ReKeyReferencesAsync`, `SettleSourceAsync`, `MoveAlongAsync`, `IsByteIdentical`, `RelocationLockKey`: extensions of the
  ONE relocation service (round 26 item 3: never two mechanisms). New constructor dependencies: `IRelocatedFileIndexing`
  (above) and `IIdempotencyService` (EXISTING, unconditional Scoped — the ADR-004 lock; §21.4.4).
- **Job report fields** and **driver script totals** — extensions of the existing report and `-Verify`.
- **Guard / FLS script patterns** — extensions of the existing guard and p4a; (p7) is one existence check.
- **`RelocationState.RelocationPending`**, **`RelocatedSourceKeptForOtherRecords`** (renamed from `RelocatedSourceKept`;
  no other branch references it — checked `task/uac-r2-150-integ`, `-integ-c`, `wip/uac-r2-150-integ-restart`,
  `work/unified-access-control-r2`), **`KeptSource`**, **`DocumentRelocationBatchResult.SourceKeptForOtherRecords`** —
  the round-37 report vocabulary.

## 21.8 Seeding (five seeded builds, 2026-10-05; restored from byte copies, files touched)

Targeted set: the relocator, migration job, facade, RAG trim, pointer-check and attach tests (167). Build A (10 seeds):
**26 of 167** red. Build B (7): **22 of 167**. Build C (2): **13 of 167**. Build D (S9 alone): **16 of 167**. Build E (S18
alone): **1 of 167**. S4 and S9 also sat in build A; their bite is proven alone in B / D. Every seed bit, each with a test
no other seed of its build turns red. No `SEED-166V1` marker remains (Grep, `src` and `scripts`).

| Seed | What it breaks | Red (examples) |
|---|---|---|
| S1 (A) | interim: the BFF-identity disjunct removed (F1 as found) | `ARelocatedFile_IsServedUnderTheInterimRule_BeforeTheStrictFlip`, `AMakeSecureRelocation_IsServed…`, `Strict_DecidesByContainer…`, `APersonsRow_…(A, True)`, `AWriteRun_Relocates…` |
| S2 (B) | interim: the disjunct without the strict test (too wide) | `Interim_ABffIdentityItemOutsideTheDerivedContainer_IsRefused` (2), `APersonsRow_…(A1, False)`, `Interim_ABffIdentityItemOfAnUndecidableDocument…`, `PointerIntoTheArchive_…` (2) |
| S3 (B) | re-entry ignores the ledger (F2 as found) | `MakeSecure_WhenTheSourceDeleteFails_…`, `AWriteRun_WhoseMoveStillOwesAStep_…`, `ReportOnly_WithALedgerStillOwing_…`, `Relocate_WhenTheIndexStepFails_…` |
| S4 (B) | a source kept for another record reported pending | `Relocate_WhenARowOfAnotherRecordStillUsesTheSource_…_IsComplete`, `ASourceKeptForAnotherRecord_IsNeverReportedIncomplete…`, `ACommunicationsOwnAttachmentRecord_…` |
| S5 (A) | repeat-call delete without the byte-identity test | `ARepeatCall_NeverDeletesASourceThatIsNotByteIdentical…`, `ARepeatCall_WhenGraphReturnsNoHash…` |
| S6 (B) | in-subtree rows not moved along | `MakeSecure_ARowInTheSecureSubtreeNamingTheSameFile_IsMovedAlong_WithItsOwnCopy` |
| S7 (A) | the row's own columns not re-keyed | `Relocate_ReKeysTheRowsOwnColumnsThatHeldTheOldIds_…` |
| S8 (B) | children / attachment rows not re-keyed | `Relocate_ReKeysAChildAttachmentsParentItem_AndTheCommunicationAttachmentRow` |
| S9 (D) | the index step skipped | `Relocate_ReindexesTheNewItem_…`, `Relocate_WhenTheIndexStepFails_…` |
| S10 (A) | facade: old chunks removed although the enqueue failed | `WhenTheNewItemIsNotEnqueued_NoOldChunkIsRemoved_AndItIsPending(failed)` |
| S11 (B) | facade: all chunks removed while the source is another record's | `WhileTheOldItemIsAnotherRecordsFile_OnlyThisDocumentsChunksOfItAreRemoved` |
| S12 (A) | RAG: the document filter dropped | `DeleteSupersededFileChunks_ForOneDocument_FiltersOnThatDocument_InLowerCase` |
| S13 (A) | job: `relocatedButRefused` not counted | `ARelocatedFileTheRuleInForceRefuses_MakesTheRunUnclean` |
| S14 (B) | job: `servedOnlyAfterFlip` not counted | `TheReport_CountsAndListsADocumentOnlyTheStrictRuleWouldServe` |
| S15 (A) | the per-document lock ignored | `ARelocationOfADocumentAnotherRelocationHolds_MovesNothing_AndIsIncomplete` |
| S16 (A) | an unreadable ledger overwritten by a move | `AnUnreadableLedger_IsNeverActedOn_AndTheFileIsNotMoved` |
| S17 (C) | the ledger not written with the re-point | `Relocate_CopiesThenVerifiesThenRepoints_…` (+ every re-entry test) |
| S18 (E) | job: a pending relocation counted clean | `AWriteRun_WhoseMoveStillOwesAStep_IsNotClean_…` |
| G1–G6 | client guard, one seed per run (`NoClientWritesADocumentPointer`): G1 the finding's bracket write in `EntityCreationService.ts`; G2 a dotted write in a committed bundle; G3 a computed key through a same-file constant; G4 a bracket write through a constant bound in ANOTHER file; G5 a form `setValue` through a constant; G6 a client write of the ledger | each **red**, naming the shape (`['sprk_graphitemid'] =`, `.sprk_graphdriveid=`, `{ [SEED_G3_FIELD]:`, `[F.SEED_G4_ITEM] =`, `getAttribute(SEED_G5).setValue`, `sprk_relocationpending:`); restored |
| P1 | FLS script `Find-PointerWrite`: the bracket pattern removed and the through-a-constant pass disabled | the 34-case harness: **7 missed writes**; restored, harness 0 failures |

## 21.9 Gates (this round, final code)

| Gate | Result |
|---|---|
| Affected tests (before the final runs) | relocator + migration job + facade + RAG trim + pointer check + attach: **167 passed, 0 failed**; every test class that runs on the pointer world (21 classes incl. FileIndexing, ChatDocument, Revoke, Reporting, VersionSave seam): **500 passed, 5 skipped, 0 failed** (before the lock tests were added; the 167 set re-ran green after them); `ClientDocumentPointerWriteGuardTests` **37 passed** |
| Full BFF unit suite (`tests/unit/Sprk.Bff.Api.Tests`) | **14,651 total: 14,597 passed, 54 skipped, 0 failed** (20 m 27 s; +45 cases vs f1's 14,606) |
| NetArchTest (`tests/Spaarke.ArchTests`) | **383 passed, 0 failed** (f1: 359; +24 guard cases) |
| Sprk.Bff.Api.IntegrationTests (full) | **104 passed, 0 failed** |
| Spe.Integration.Tests (full) | **405 total: 380 passed, 25 skipped (environment-gated `SkippableFact`s), 0 failed** |
| BFF build | Debug (tests) and Release succeeded, 0 errors (warnings as errors) |
| `dotnet format whitespace --verify-no-changes` (the changed BFF files) | clean; every changed `.cs` file is CRLF (`.gitattributes *.cs eol=crlf`) |
| `dotnet list package --vulnerable --include-transitive` | `Sprk.Bff.Api` has no vulnerable packages |
| PowerShell | `Set-DocumentRelocationSchema.ps1`, `Set-DocumentPointerFieldSecurity.ps1`, `Invoke-DocumentContainerMigration.ps1` parse with 0 errors; the C# guard and the script's `Find-PointerWrite` agree on all 34 harness cases |
| Publish size (CLAUDE.md §10) | Base ca8291442 45.710 MB (47,930,449 bytes) vs branch ba81ea003 45.737 MB (47,959,058 bytes) = +0.027 MB (+28,609 bytes); 212/212 files; both from FRESH short-path trees (git archive into C:\wt166vm / C:\wt166vb, removed afterwards), dotnet publish -c Release, PowerShell Compress-Archive Optimal, PDBs included. No package; one interface, one service, one IRagService method. Ceiling 60 MB; far below the +5 MB escalation threshold. |
| Client builds | none needed: no client source changed (the guard and the FLS script scan clients; no `.ts`/`.tsx`/`.js` or PCF bundle is edited) |

## 21.10 Route authorization ledger input — f1-v1

No route is added, deleted or re-gated in this round. The §20.10 rows stand.

## 21.11 Manual live gates — f1-v1 (main session, dev; in THIS order; supersedes §20.11 gates 23–25 where they differ)

20–22 as §20.11 (deploy the BFF — it carries this round; set the allowed workspaces; redeploy the clients; census).
**23a (NEW) Relocation ledger column**: `pwsh scripts/Set-DocumentRelocationSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com`
(dry run) → the same with `-Apply` → the same with `-Verify` (exit 0). Before gate 24 and before task 150's Make Secure is
used: every relocation fails closed until the column exists.
**23 Document pointers FLS** — as §20.11, now ALSO locking `sprk_relocationpending` (p7 refuses until 23a ran):
`pwsh scripts/Set-DocumentPointerFieldSecurity.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c`
(dry run: p4a and p7 OK) → `-ClientNoLongerWritesPointers -Apply` → `-Verify` (exit 0).
**24 Legacy migration** — as §20.11; the RAG decision §20.12 asked for is made (round 37 item 1) and built: every
relocated file is re-indexed by the relocation itself (one app-only `RagIndexing` job per moved file — up to 447 on dev;
the BFF's Service Bus job processor must be running). `-Verify` now also requires `RelocationPending 0`, `pending 0`,
`relocatedButRefused 0`. A run that ends with `pending > 0` is re-run with `-Apply` (it settles what is owed).
Read-only cross-check after `-Verify`: `SELECT COUNT(sprk_documentid) FROM sprk_document WHERE sprk_relocationpending IS NOT NULL`
— only rows whose source is kept for another record may remain (each listed under `sourceKeptForOtherRecords`).
**25 Strict rule ON** — as §20.11 (only after gate 24's `-Verify`). Before AND after it, a relocated file downloads
(round 37 item 3; gate 22's census class "a person's row whose file the BFF placed" is served by both rules).
26–27 as §20.11.

## 21.12 Found, decisions recorded, integration notes, `.claude`

- **🔔 Decision requested — SPE version history of a relocated file.** A relocation copies the CURRENT version (round 26
  item 3: "copy … verify … delete the source"); deleting the source deletes its older versions, and
  `GET /api/documents/{documentId}/versions` (re-keyed by document id; it lists the item's SPE versions) then shows one
  version for every relocated document (up to 447 on dev, and every Make Secure move). f1 recorded this as a documented
  residual; it is not one of the round-37 references (it is content, not an id) and no round decides it, while it changes
  what round 26 item 3's "copy" means — so it is asked, not assumed. **Proposed complete fix:** the relocator replays the
  source's history into the copy through the BFF identity: list the versions app-only (`SpeFileStore.ListFileVersionsAsync`
  EXISTS), upload the oldest prior version with `ConflictBehavior.Rename`, every later prior version to the same path with
  `Replace` (each becomes a new version of the same item), and the current content last; each upload's size is checked
  against the listed version, and the final one is verified against the source exactly as today; a failed replay deletes
  the copy and leaves the row and the source untouched (Failed, retried). It needs ONE new app-only Graph facade method
  (`DownloadFileVersionAsync`; its OBO twin `DownloadFileVersionAsUserAsync` exists). Facts the decision needs: Graph cannot
  set a version's author or date, so replayed versions show the BFF identity and the replay time; each version is one more
  download and upload (a Make Secure request's duration grows with the history it carries); the target container's
  version limit applies.
- **Found, not owned (round 15: never dropped):** `AnalysisChatContextResolver` retrieves `sprk_analysisoutput.sprk_analysisplaybookid`,
  `sprk_analysistype`, `sprk_spefileid`, `sprk_containerid`; none exists on spaarkedev1 (live metadata 2026-10-05:
  `sprk_analysisid`, `sprk_name`, `sprk_outputcode`, `sprk_outputtypeid`, `sprk_tags`, `sprk_value`, `sprk_availableadhoc`),
  so the retrieve throws, is caught, and the resolver returns null — analysis chat context never resolves from Dataverse
  on dev. Unrelated to access control; for the main session to file.
- **Decision recorded (forced by the live key):** one copy per moved row (§21.4.3).
- **Integration notes — task 150's lane:** `RelocateDocumentsAsync(ids, recordContainerId, RelocationPurpose.MakeSecure, apply: true)`
  unchanged; `Complete == false` → 500 `sdap.provision.files_incomplete` with `Counts` / `Incomplete` — now also for
  `RelocationPending` (`index-pending`, `rekey-pending`, `source-pending`) and a held lock; add `SourceKeptForOtherRecords`
  to that report (round 37). A repeat call settles. **Pass the record's CHILD attachment documents too** (`sprk_parentdocument`
  of the record's documents): their derived container follows the parent, they are separate files, and the relocator moves
  only the ids it is given (plus rows naming the SAME file). The relocator's new constructor dependencies are DI-registered
  (no change for a caller that resolves it).
- **Task 167's guard:** no new route.
- **`.claude` edit (main session only):** none new; §12's `bff-deploy/SKILL.md:50` edit still stands.

## 21.13 Not closed

- **Live writes (by design, main session):** gates 23a, 23, 24, 25 (§21.11) and §20.11's others. Nothing was written live;
  this round's live access was read-only (Dataverse metadata and SQL counts, §21.4.3 / §21.5.1).
- ~~**🔔 One decision requested** (found, not one of the 11 items): SPE version history of a relocated file — complete fix
  proposed in §21.12.~~ — decided by owner round 45 item 1 and built in f1-v2 (§22.3).
- Nothing else is owed by task 166.

# f1-v2 (2026-10-05): owner round 45 built; the f1-v1 verification's F-A – F-E closed

> **Branch**: `task/uac-r2-166-f1-v2` from `task/uac-r2-166-f1-v1` @ `992a242a1` (baseSha).
> **Inputs (BINDING)**: rounds 1–48 on `work/unified-access-control-r2` (`notes/session27-owner-decisions-and-research.md`).
> **Round 45 is this verification's answer** (and the decision §21.12 asked for): item 1 (a moved file keeps its version
> history — replay, original authorship recorded, `versions-truncated` stated), item 2 (one copy per in-subtree row,
> ratified), item 3 (an unlinked `sprk_communicationattachment` row is classified by its communication's subtree — F-B),
> item 4 (a repeat call deletes the source by the WITNESS recorded at verification; a source edited after the move is
> `source-changed-after-move` and closed by the relocator's own re-entry — F-A), item 5 (F-C guard tests, the two F-D
> client shapes, the F-E gate name; the ADR-010 integration note).
> **Outcome**: every item is closed in code, tests, scripts and docs. ~~each new check seeded~~ — **CORRECTED by g
> (owner round 54 item 4, §23.6):** that was NOT true. Six fail-closed branches added here (Sk, Sq, Si, Sm, Sj, Sw), the
> version-list paging (Sd) and the Unknown → never-delete witness branch (Sk) had no test: the f1-v2 verification seeded
> each away and all 273 targeted tests stayed green. g adds a test for each and seeds it red (§23.8). What is left is LIVE
> writes only — the schema script (now two columns, secured from creation), the FLS script (now four columns) and the
> migration, each dry run / `-Apply` / `-Verify`, exact commands in §22.11. No decision is requested.
> `NOTE-FROM-MAIN.md`: none in the worktree.

## 22.1 Binding inputs as applied

| Decision | Implemented |
|---|---|
| Round 45 item 1 — replay the source's prior versions into the copy through the BFF identity, oldest first, current content last; list with the existing `SpeFileStore.ListFileVersionsAsync`; ONE new app-only facade method `DownloadFileVersionAsync`; each upload's size checked; a failed replay deletes the copy and leaves the row and the source untouched; the ORIGINAL author, date and size of each replayed version recorded against the new version id; `GET /api/documents/{id}/versions` reports them; `versions-truncated` with counts when the target's version limit is lower; tests and seeds (order, failure, mapping, truncation) | §22.3 |
| Round 45 item 2 — each in-subtree row gets its OWN copy (as built; ratified) | unchanged (§21.4.3); the note states it |
| Round 45 item 3 — an unlinked attachment row: inside the secure record's subtree → re-key it to the copy or report it pending; outside → keep the source; undecidable → pending | §22.5 |
| Round 45 item 4 — the ledger records the source's size and quickXorHash at verification; a repeat call deletes the source only if it still matches; edited after the move → `source-changed-after-move` with the row id, not deleted, closed by the relocator's own re-entry (re-copy, verify, re-point), never by a manual step | §22.4 |
| Round 45 item 5 — V6 / F-C real tests; the two F-D shapes; F-E (gate 23a); the ADR010 integration note | §22.6, §22.7, §22.2 item 5, §22.12 |

## 22.2 The verifier's 16 items — what changed per item

| # | Item | Closure |
|---|---|---|
| 1 | **F-A** — a source-pending entry whose document was edited after the move never settled (probe Q1) | The ledger entry records the source's WITNESS when the copy is verified (`RelocationWitness`: size, quickXorHash, current version id); a repeat call compares the SOURCE with that witness — never with the document's current file — and deletes it when it matches (§22.4). Probe Q1 is the test `ARepeatCall_DeletesTheSourceByItsWitness_ThoughTheDocumentWasEditedSinceTheMove` (first delete fails, the copy is edited to a new size and hash, then two more calls: complete, source gone); case (2) is `ASourceKeptForAnotherRecord_ThenReleased_IsDeletedByItsWitness_ThoughTheDocumentWasEdited`. A source that WAS edited is re-copied (round 45 item 4). Seeded (S1, S2, S2b, S15) |
| 2 | **F-B** — every unlinked attachment row kept the source whatever its communication's subtree | `ReadSourceReferencesAsync` reads each attachment row's `sprk_communication`; `RecordContainerResolver.DeriveCommunicationContainersAsync` (the communication pipeline's own answer — the same one a document linked to that communication derives) classifies it: inside → re-keyed to the current file (`sprk_graphdriveid` / `sprk_graphitemid`), a failed re-key pending; outside → kept; no communication / undecidable → pending. f1-v1's `ACommunicationsOwnAttachmentRecord_KeepsTheSource_ForThatCommunication` (which pinned the defect) is replaced by three tests (§22.5). Seeded (S10, S11, S11b) |
| 3 | **F-C** — V6 / V8 / V5 had no test | `MoveAlong_UnderTheLegacyMigration_NeverCopiesARowWhoseFileIsNotVerifiablyItsOwn` (V6), `WhenTheRelocationLockCannotBeTaken_NothingMoves_FailClosed` (V8: the lock store faults), `UnderTheStrictRuleInForce_ARelocatedFileOnlyTheInterimRuleWouldRefuse_IsServed_AndTheRunIsClean` (V5). Each seeded alone in its build: red (S12, S13, S14) |
| 4 | **F-D** — the getControl(...).getAttribute().setValue shape and an aliased-import constant evaded the guard and p4a | Both detectors gain: setValue through `getControl` / `controls.get` (literal and through a name); `Reflect.set` / `defineProperty`; and ALIASES of a column-holding name, to a fixpoint — import / export rename (`F as G`), re-binding (`const G = F`, `const G = cols.F`), destructuring rename (`const { F: G } = …`) — across client source files for the guard. The FLS script's detector moved to `scripts/common/Find-ClientPointerWrite.ps1` (dot-sourced by p4a) and the guard's NEW test `TheFlsScriptsDetector_AgreesWithTheGuard_OnEveryCase` runs THAT file through pwsh over all 55 cases (34 write, 21 read) — the committed form of the f1-v1 harness, which had not been committed. Seeded: the verifier's two shapes as client files (G1 control, G2 aliased import: both red), the PS detector (P1 control template, P2 aliases: parity red), the C# detector (C1, C2: red) (§22.7) |
| 5 | **F-E** — the DEPLOY ORDER text said gate 23b | `Set-DocumentRelocationSchema.ps1` names gate 23a (task note §21.11 / §22.11) |
| 6 | Verified MET — F1 / round 37 item 3 | Unchanged |
| 7 | Verified MET — F4 / round 37 item 1 | Unchanged; the re-copy (§22.4) re-keys and re-indexes through the same ledger steps (the replaced copy is an old item of the ledger) |
| 8 | Verified MET — F2 (except F-A, F-B) | F-A and F-B closed (items 1, 2) |
| 9 | Seed V3 (no behavioural effect) | Not a finding; unchanged |
| 10 | The verifier's integration runs; the load flake `DeletePin_Authenticated_Returns204AndEmitsCounter` | Recorded. This round's runs: §22.10 |
| 11 | Hygiene | Holds: the POML parses as XML; no `NOTE-FROM-MAIN.md`; no `.claude/`, `TASK-INDEX.md` or `current-task.md` change; no live write; no live access at all this round |
| 12 | ADR010 1:1-interface ceiling (informational) | Round 45 item 5's integration note, §22.12 |
| 13 | Criterion — round 26 item 3 / round 40 item 1, every post-flag failure closable by retry or job | **Met.** A source-pending entry now settles on the repeat call whatever the document became (witness), and an edited source is closed by the relocator's own re-copy. What still needs a person is unchanged and stated: a ledger the BFF cannot read, a document whose container cannot be derived, a file not verifiably the row's own, an attachment row whose communication's filing cannot be derived (round 45 item 3: pending), and an entry without a witness (only a hand-written ledger lacks one; the column is BFF-written and secured from creation) |
| 14 | Criterion — round 37 item 2 split for attachment rows | **Met** (item 2) |
| 15 | Criterion — seeding | ~~**Met**~~ **Not met as claimed — corrected by g (§23.6)**: the 23 server seeds in 12 builds and 6 client/script seeds below were each red, but six new fail-closed branches (Sk, Sq, Si, Sm, Sj, Sw) and the version-list paging (Sd) had NO seed and NO test; the f1-v2 verification seeded each away with every targeted test green (§22.9) |
| 16 | Criterion — §21.2 item 11 "every statically writable shape" | **Met for the enumerated shapes, and the claim is corrected**: the guard's remarks now LIST the shapes it detects; a key built at run time (concatenation, a loop over names, a value returned by another module's function) is not statically detectable and the field-level security lock is the control for it (§22.7) |

## 22.3 Round 45 item 1 — a moved file keeps its version history

- **The replay.** `ReadHistoryAsync` lists the source's versions app-only (`SpeFileStore.ListFileVersionsAsync`, now
  `virtual`, and now following `@odata.nextLink` so a long history is never cut at one page — **untested here; g adds the
  test, §23.6**), orders them oldest first
  (by version number, else by date), and makes one step per version: every PRIOR version through the ONE new app-only
  facade method `SpeFileStore.DownloadFileVersionAsync` (→ `DriveItemOperations.DownloadFileVersionAsync`, the app-only
  twin of `DownloadFileVersionAsUserAsync`; not on `ISpeFileOperations` — no route reads a version app-only), the current
  content last through the existing current download. `MoveAsync` writes the first step with `Rename` (creating the copy)
  and every later one with `Replace` to the name Graph gave the copy (each a new version of THE SAME item); every upload
  must land on the copy's own item id and with the listed size, and the final content is verified against the source as
  before. Any failure — a version that cannot be downloaded, a size mismatch, an upload on another item, a fault
  mid-replay — deletes the partial copy (and a stray item) and leaves the row and the source untouched (Failed / FileMissing,
  retried).
- **Original authorship.** After the replay the copy's versions are listed and matched to the replayed steps newest to
  newest; the record (`RelocatedVersionHistory.Map`: the copy's item id and, per NEW version id, the original author's
  display name, Entra object id or application id, date, size, source item and source version) is written to the NEW
  column `sprk_document.sprk_relocatedversions` in the SAME Dataverse update as the re-point, the re-keyed own columns and
  the ledger. A second move reads the record of the item it moves, so a version replayed twice keeps its FIRST original
  author (`ASecondMove_KeepsTheFirstOriginalAuthorship`).
- **The record is not the ledger column, deliberately.** "The relocation ledger records each replayed version's original
  author …" (round 45) is implemented as the relocation's record on the row in TWO columns: `sprk_relocationpending` holds
  what a move still OWES and is cleared when settled (gate 24's read-only cross-check counts rows where it is not null) and
  is 4000 characters; the version record is permanent and as long as the history (Multiple lines of text, 1,048,576; a
  longer one keeps its newest versions and states the rest in `versions-truncated`). Folding it into the ledger would break
  both properties.
- **The routes.** `GET /api/documents/{id}/versions` reports each replayed version's ORIGINAL author and date
  (`RelocatedVersionHistory.WithOriginalAuthorshipAsync`, after the per-document gate, presentation only: an unreadable
  record leaves Graph's values and is logged). `VersionInfoDto` gains `LastModifiedBy` (Graph's display name for every
  version; the ids stay server-side, `[JsonIgnore]`). The external version list
  (`GET /api/v1/external/projects/{id}/documents/{documentId}/versions`) reports the original DATES too; its
  `createdByName` stays null — an external participant has never been shown who wrote a version, and showing internal
  authors to external contacts is a disclosure decision of its own, not a side effect of a move ("the history a user sees
  is unchanged" holds on both surfaces). Version LABELS are the copy's (`1.0` … `N.0`): a source with gaps in its labels
  shows contiguous ones after a move; the count, order, dates, authors and sizes are the source's.
- **`versions-truncated`.** When the copy keeps fewer versions than were replayed (the target container's version limit
  dropped the oldest), the outcome carries `VersionsTruncated(documentId, item, replayed, kept, unrecordedAuthors)` — a
  STATED outcome: the move is complete (no retry could change the limit), the batch result and the migration report
  (`versionsTruncated`, `versionsTruncatedRows`) list it with counts, the driver script prints it.
- **Tests**: `Relocate_ReplaysTheSourcesHistory_OldestFirst_TheCurrentContentLast` (exact step order, one copy, sizes),
  `Relocate_RecordsEachReplayedVersionsOriginalAuthorAndDate_InTheSameUpdateAsThePointer`,
  `Relocate_WhenAReplayedVersionCannotBeRead_DeletesTheCopy_AndLeavesTheRowAndTheSource`,
  `Relocate_WhenAReplayedVersionIsWrittenWithTheWrongSize_DeletesTheCopy_AndFails`,
  `Relocate_WhenTheTargetKeepsFewerVersions_StatesVersionsTruncated_WithCounts_AndIsComplete`,
  `ASecondMove_KeepsTheFirstOriginalAuthorship`, `TheVersionRecord_IsReportedOnlyForTheItemItDescribes_AndAnUnreadableOneChangesNothing`,
  `WithoutTheVersionRecordColumn_NothingIsMoved_FailClosed`, the job's `TheReport_StatesATruncatedHistory_WithCounts_WithoutBlockingTheRun`,
  the route's `ListVersions_OfAMovedFile_ReportsTheOriginalAuthorAndDate_OfEachReplayedVersion` (auth suite) and the external
  `DocumentVersions_OfAMovedFile_ReportTheOriginalDates_AndNoAuthor_ToAnExternalParticipant`.
- **Costs (as round 45 accepted them):** each prior version is one more download and upload, so a Make Secure request's
  duration grows with the history it carries; each version obeys the existing 250 MB single-request bound (a larger one
  fails the move, reported).

## 22.4 Round 45 item 4 / F-A — the witness, and the re-copy of an edited source

- **The witness.** `MoveAsync` records, in the ledger entry it writes with the re-point, `witness = { size, quickXorHash,
  version }` of the item the row moves away from — exactly the facts its copy was verified against, plus the current
  version id from the replay's listing. A witness must be PROVABLE (a size and a hash or a version), else the move fails
  before a byte moves.
- **The rule** (`CompareWithWitnessAsync` / `Compare`): the same size AND — when both carry one — the same quickXorHash;
  otherwise the same current version id (SharePoint mints a new version for every content change; the versions are listed
  only when a hash is missing). Matches → the source is deleted (when no row references it). Changed → `source-changed-after-move`.
  Unknown (the version list cannot be read) → pending, retried, never deleted. No witness → `source-unverifiable`, never
  deleted or copied (only a hand-written ledger lacks one). The first settle after a move uses the same rule (the
  just-recorded witness), so "verified moments ago" is no longer a special case.
- **The re-copy** (`RecopyChangedSourcesAsync`, the relocator's own re-entry; on the call that finds the change — the
  repeat call, or the move's own settle when a save raced the move): a NEW copy in the document's CURRENT container of the
  document's current file WITH its history (its recorded originals), plus each edited source's versions written after its
  witness version, oldest first; verified; the row re-pointed (the replaced copy becomes an old item of the ledger with its
  own witness; each edited source's witness becomes its current state); settled at once — the edited source and the
  replaced copy are deleted against their new witnesses, references re-keyed, the index re-keyed. Nothing either file held
  is lost. Reported `SourceChangedAfterMove(documentId, source, carriedVersions, newItem, editIsCurrent)` — stated, with the
  row id; one the re-copy could not close yet (a fault) is ALSO a `source-changed-after-move: …` pending line, retried.
- **Whose edit becomes current.** The old file stayed writable by the OLD container's audience (SPE permissions are
  container-wide), so carrying its post-move edits blindly would let a person who lost access with the move change the
  (now secure) document. Every edit is carried (nothing is lost; its author is in the version record), but the last one
  becomes the CURRENT content only when its author may write the document NOW — Dataverse's own answer for that person,
  `IAccessDataSource.GetUserAccessAsync(authorObjectId, documentId)` (RetrievePrincipalAccess, app-only for the named
  principal; the EXISTING registered service — **superseded by g, owner round 54 item 1 (§23.3): that registration is
  the 60-second `CachedAccessDataSource`; the answer is now read FRESH**). Otherwise — no Write, an app-only write, an answer that cannot be had
  (ADR-003) — the edits go into the history before the document's own current content, which stays current
  (`EditIsCurrent = false`). This is the round-45 re-copy made safe, not a new path: the re-copy, verify and re-point are
  as decided; only which content ends current is decided fail-closed.
- **Tests**: probe Q1 and case (2) above; `ASourceEditedAfterTheMove_IsReportedWithTheRowId_AndReCopiedWithItsEdits_ThenDeleted`
  (editor may write: the edit is current, the history shows the edit and the original with their original authors),
  `AnEditAtTheOldLocation_ByAPersonWhoMayNotWriteTheDocument_IsKeptInItsHistory_ButNeverBecomesCurrent`,
  `AnEditAtTheOldLocation_WhoseAuthorsRightCannotBeRead_IsNeverMadeCurrent`, `ASourceEditedWhileItIsBeingMoved_IsReCopiedOnTheSameCall`,
  `ASourceEditedAfterTheMove_WhoseReCopyFails_IsPending_NeverDeleted_AndARepeatCallClosesIt`,
  `ALedgerEntryWithoutAWitness_NeverDeletesOrCopiesItsSource` (replaces the byte-identity test of f1-v1),
  `AWitnessWithoutAHash_IsMatchedByTheSourcesVersion_AndTheSourceIsDeleted`, `TheWitnessRule_SizeAndHash_ElseSizeAndVersion`
  (replaces `TheRepeatCallsDeletionTest_NeedsTheSameSizeAndHash_BothPresent`; `IsByteIdentical` is deleted — the current
  file is no longer a delete criterion).

## 22.5 Round 45 item 3 / F-B — attachment rows by their communication's subtree

`SettleSourceAsync` now treats an unlinked `sprk_communicationattachment` row as the document rows are treated (round 37
item 2): `DeriveCommunicationContainersAsync(communication)` → inside (the derivation allows the drive the document's file
now lives in) → the row is re-keyed to that file in place, so no secure bytes stay in the shared container; outside → it
keeps the source (listed `sprk_communicationattachment:{id}`); no communication or an undecidable derivation (an
unreadable communication, an ambiguous or refused resolution) → pending, never "outside". A row linked to ANOTHER
document goes the same way (round 45: "not linked to the document"). Tests:
`MakeSecure_AnUnlinkedAttachmentRowOfACommunicationInsideTheSubtree_IsReKeyedToTheCopy_AndTheSourceIsDeleted`,
`ACommunicationsOwnAttachmentRecord_OutsideTheSubtree_KeepsTheSource_ForThatCommunication`,
`AnUnlinkedAttachmentRowWhoseCommunicationsSubtreeIsUndecidable_IsPending_AndKeepsTheSource` (2 cases). The f1-v1 dev
read (1 unlinked row with an item, 0 sharing a document's item) means no live row changes behaviour today.

## 22.6 F-C — the three guards now proven

| Guard | Test | Seed |
|---|---|---|
| V6 — a move-along applies the relocation's legitimacy rule | `MoveAlong_UnderTheLegacyMigration_NeverCopiesARowWhoseFileIsNotVerifiablyItsOwn` (the other row's creator did not upload the file: SourceUnverified, one copy only, the row and the source untouched, listed for an administrator) | S12 `verified = true` → red |
| V8 — a lock that cannot be taken is held | `WhenTheRelocationLockCannotBeTaken_NothingMoves_FailClosed` (the lock store faults: Failed, nothing downloaded, uploaded or written) | S13 catch returns `true` → red |
| V5 — `relocatedButRefused` asks the rule IN FORCE | `UnderTheStrictRuleInForce_ARelocatedFileOnlyTheInterimRuleWouldRefuse_IsServed_AndTheRunIsClean` (strict in force; a copy written by another application: interim refuses, strict serves → 0, clean) beside the existing interim-in-force test | S14 "always interim" → red |

## 22.7 F-D — the client pointer-write guard: two more shapes, aliases, and one detector run by both

- **Columns**: `sprk_graphdriveid`, `sprk_graphitemid`, `sprk_relocationpending` and now `sprk_relocatedversions`.
- **New shapes**: form setValue through `getControl(…)` / `controls.get(…)` `.getAttribute().setValue` (also `?.`);
  `Reflect.set(obj, "…", v)` / `defineProperty(obj, "…", …)`; both literal and through a name.
- **Aliases** of a column-holding name, to a fixpoint: `import { F as G }` / `export { F as G }`, `const G = F` /
  `const G = cols.F`, `const { F: G } = …`. Across client SOURCE files the guard computes the closure globally (an exported
  constant renamed on import in another file is caught: the verifier's `import { POINTER_ITEM_COL as COL } from './cols';
  p[COL] = v`); a committed bundle's names count only inside that bundle, as before.
- **One detector, two runners.** The templates are identical in `ClientDocumentPointerWriteGuardTests` and the new
  `scripts/common/Find-ClientPointerWrite.ps1`, which `Set-DocumentPointerFieldSecurity.ps1` dot-sources for p4a.
  `TheFlsScriptsDetector_AgreesWithTheGuard_OnEveryCase` runs the PowerShell file through `pwsh` over every write and read
  case (55) and fails on any disagreement — f1-v1's 34-case harness was a manual run, never committed, which is how the
  two shapes were missed by both. (`pwsh` is on every CI runner; a machine without it fails the test with an install hint.)
- **The claim, corrected.** The guard's remarks now enumerate the shapes; "every statically writable shape" is withdrawn:
  a key computed at run time is not statically detectable, and the field-level security lock (gate 23) is the control for
  it. The repo scan is green.

## 22.8 Placement (CLAUDE.md §10) and component justification (§11)

**Placement: in BFF**, in the existing types. ADR-002: no plugin. ADR-003: every new decision fails closed (a witness that
cannot be compared keeps the source; an entry without a witness never deletes or copies; an undecidable attachment row is
pending; an edit's author whose right cannot be read does not make it current; a partial replay is deleted; the version
record must exist for a move). ADR-007: one new app-only Graph facade method, DTO-only. ADR-010: no new interface (the
ceiling is unchanged; §22.12). ADR-013: no AI type in the relocator. ADR-038: no `Mock<HttpMessageHandler>`, no DI or
ctor-null tests. No package, endpoint, option, job, PCF or plugin.

- **`SpeFileStore.DownloadFileVersionAsync` + `DriveItemOperations.DownloadFileVersionAsync`** — Existing: the OBO twin
  `DownloadFileVersionAsUserAsync` (needs a user; a relocation runs as the BFF identity, also from the job); Extension:
  round 45 names exactly this ONE facade method; Cost of nothing: no prior version can be copied, the history is lost.
- **Column `sprk_document.sprk_relocatedversions` + `RelocatedVersionHistory`** (a static parser/projection class, no DI
  registration) — Existing: `sprk_relocationpending` (cleared when settled, 4000 chars, counted by gate 24's check),
  `sprk_fileversion` (check-out tracking keyed `vN`, a separate non-atomic write); Extension: neither can hold a permanent
  per-version record written atomically with the re-point; Cost of nothing: every replayed version reads "the BFF, at the
  move" — round 45 item 1 forbids it.
- **`RecordContainerResolver.DeriveCommunicationContainersAsync`** — Existing: `DeriveDocumentContainersAsync` resolves a
  communication only as a document link; Extension: it reuses the same `ResolveForRecordWithFixedFallbackAsync` answer;
  Cost of nothing: round 45 item 3 cannot be decided (F-B stays open).
- **Relocator members** (`ReadHistoryAsync`, `RecordReplayAsync`, `RecopyChangedSourcesAsync`, `MayWriteAsync`,
  `CompareWithWitnessAsync` / `Compare`, `RelocationWitness`, `OldestFirst`) and one constructor dependency — the EXISTING,
  unconditionally registered Scoped `IAccessDataSource` (type activation; no registration change). Extensions of the ONE
  relocation service (round 26 item 3). **Superseded by g (§23.3, §23.7):** the registration now passes the UNCACHED
  `DataverseAccessDataSource`.
- **Outcome / report vocabulary**: `SourceChangedAfterMove`, `VersionsTruncated` (records; on the outcome, the batch
  result, the job report and the driver script) — round 45's stated outcomes.
- **`VersionInfoDto.LastModifiedBy`** (+ two `[JsonIgnore]` ids) — the projection round 45 says the route reports; an
  optional positional parameter, so every existing construction compiles unchanged.
- **`scripts/common/Find-ClientPointerWrite.ps1`** — Existing: the detector inline in the FLS script; Extension: moved
  verbatim-plus-shapes so CI can run the very function p4a runs; Cost of nothing: the two detectors drift unseen (F-D).

## 22.9 Seeding (2026-10-05; restored from byte copies, files touched; no `SEED-166V2` marker remains)

Targeted set: relocator, migration job, facade, RAG trim, pointer check, attach, version-route auth, external contract
(270–271 tests). Every seed LISTED BELOW bit; within each build every seed turned red at least one test no other seed of
that build did. **Corrected by g (owner round 54 item 4, §23.6):** "every seed bit" was true only of the seeds below. The
six fail-closed branches f1-v2 added in the replay and the witness (Sk: Unknown → never delete; Sq: a faulted version
list; Si: an unprovable witness; Sm: a replay upload on another item; Sj: the per-version size bound; Sw: a copy whose
versions cannot be listed) and the version-list paging (Sd) were never seeded and had no test; the f1-v2 verification
seeded each away with the whole targeted set green. §23.8 seeds each against the test g adds for it.

| Build | Seed | What it breaks | Red |
|---|---|---|---|
| A (19 red) | S1 | the witness replaced by the document's CURRENT file (f1-v1's rule) | `ARepeatCall_DeletesTheSourceByItsWitness_…`, `ASourceKeptForAnotherRecord_ThenReleased_…`, `AWitnessWithoutAHash_…`, `ALedgerEntryWithoutAWitness_…` |
| A | S3 | no replay (the current content only) | `Relocate_ReplaysTheSourcesHistory_…`, `…RecordsEachReplayedVersions…`, both truncation tests, `ASecondMove_…`, `…CannotBeRead…`, `…WrongSize…` |
| A | S10 | attachment rows always kept (F-B as found) | `MakeSecure_AnUnlinkedAttachmentRowOfACommunicationInsideTheSubtree_…`, the undecidable theory (2) |
| A | S12 / S13 / S14 | V6 / V8 / V5 (§22.6) | their three tests |
| A | S9 | the internal route reports Graph's authorship | `ListVersions_OfAMovedFile_ReportsTheOriginalAuthorAndDate_…` |
| B (8 red) | S4 | the version record not written | `…RecordsEachReplayedVersions…`, `ASecondMove_…`, `ASourceEditedAfterTheMove_IsReported…`, truncation |
| B | S5 | a partial copy left behind | `…CannotBeRead_DeletesTheCopy…` |
| B | S6 | upload size not checked | `…WrittenWithTheWrongSize…` |
| B | S11 / S11b | no communication / undecidable treated as outside | the undecidable theory (False / True) |
| C (3 red) | S2 (+S2b) | no re-copy of an edited source | `ASourceEditedAfterTheMove_IsReported…` |
| C | S7 | truncation not stated | both truncation tests |
| D (3 red) | S8 | a second move ignores the first record | `ASecondMove_KeepsTheFirstOriginalAuthorship` |
| D | S2b | no re-copy after the move's own settle | `ASourceEditedWhileItIsBeingMoved_IsReCopiedOnTheSameCall` |
| E (25 red) | S15 | no witness recorded | every move that deletes a source (25) |
| F (2 red) | S16 | a closed edit not reported | `ASourceEditedAfterTheMove_IsReported…`, `…WhileItIsBeingMoved…` |
| G (2 red) | S17 | an edit always current | `AnEditAtTheOldLocation_ByAPersonWhoMayNotWrite…`, `…WhoseAuthorsRightCannotBeRead…` |
| H (2 red) | S18 | an edit never current | `ASourceEditedAfterTheMove_IsReported…`, `…WhileItIsBeingMoved…` |
| I (1 red) | S19 | an access fault read as a right | `AnEditAtTheOldLocation_WhoseAuthorsRightCannotBeRead_IsNeverMadeCurrent` |
| J/K/L (1 red each) | S20 / S21 | the external route: Graph's dates / authors shown | `DocumentVersions_OfAMovedFile_ReportTheOriginalDates_AndNoAuthor_…` (each alone) |
| G1 / G2 | — | the verifier's two shapes as client files (`src/client/zzseed166v2/seed1.js` control setValue; `cols.ts` + `use.ts` aliased import) | `NoClientWritesADocumentPointer` red, naming `getControl("sprk_graphitemid").getAttribute().setValue` / `[COL] =` |
| P1 / P2 | — | `Find-ClientPointerWrite.ps1`: control template removed / aliases not followed | `TheFlsScriptsDetector_AgreesWithTheGuard_OnEveryCase` red, listing 3 / 6 disagreeing write cases |
| C1 / C2 | — | the C# detector: control template removed / aliases not followed | 3 + 6 write cases, the alias fact, the parity test |

## 22.10 Gates (this round, final code)

| Gate | Result |
|---|---|
| Affected tests | the targeted set above: **271 passed, 0 failed**; `ClientDocumentPointerWriteGuardTests` **60 passed** (34 write + 21 read cases, 5 facts) |
| Full BFF unit suite (`tests/unit/Sprk.Bff.Api.Tests`) | **14,675 total: 14,621 passed, 54 skipped, 0 failed** (17 m 48 s; +24 cases vs f1-v1's 14,651) |
| NetArchTest (`tests/Spaarke.ArchTests`) | **406 passed, 0 failed** (f1-v1: 383; +23 guard cases) |
| Sprk.Bff.Api.IntegrationTests (full) | **104 passed, 0 failed** |
| Spe.Integration.Tests (full) | **405 total: 380 passed, 25 skipped (environment-gated `SkippableFact`s), 0 failed** |
| `dotnet list package --vulnerable --include-transitive` | `Sprk.Bff.Api` has no vulnerable packages |
| Formatting | the pre-commit `dotnet format` ran on every staged `.cs` file; every changed `.cs` file is CRLF |
| Publish size (CLAUDE.md §10) | base `992a242a1` **45.737 MB** (47,959,073 bytes) vs branch `dad6ade5c` **45.765 MB** (47,988,510 bytes) = **+0.028 MB** (+29,437 bytes); 212/212 files; both from FRESH short-path trees (`git archive` into `C:\wt166v2m` / `C:\wt166v2b`, removed afterwards), `dotnet publish -c Release`, PowerShell `Compress-Archive -CompressionLevel Optimal`, PDBs included. No package. |
| PowerShell | the three changed scripts and `common/Find-ClientPointerWrite.ps1` parse with 0 errors; the parity test runs the detector |
| Client builds | none needed: no client source changed (the guard and the FLS script scan clients) |

## 22.11 Manual live gates — f1-v2 (main session, dev; supersedes §21.11 where they differ)

20–22 as §20.11 (deploy the BFF — it carries this round; allowed workspaces; redeploy the clients; census).
**23a Relocation record (two columns, secured from creation)**: `pwsh scripts/Set-DocumentRelocationSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com`
(dry run: WOULD create `sprk_relocationpending` 4000 and `sprk_relocatedversions` 1,048,576, both field-secured) → the same
with `-Apply` → the same with `-Verify` (exit 0: both exist, Memo, secured, travel with SpaarkeCore).
**23 Field security — run IMMEDIATELY after 23a**: `pwsh scripts/Set-DocumentPointerFieldSecurity.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c`
(dry run: p4a OK — the scan now uses `scripts/common/Find-ClientPointerWrite.ps1`; p7 OK for four columns) →
`-ClientNoLongerWritesPointers -Apply` (the two relocation columns are already secured: it grants the reader / writer
profiles on them; it secures and grants the two pointer columns) → `-Verify` (exit 0). Until 23 has granted the writer
profile, a BFF application user WITHOUT System Administrator cannot write the relocation columns, so every relocation fails
closed (reported Failed, retried) — Make Secure and gate 24 come after 23.
**24 Legacy migration** — as §21.11; each move now also replays the file's versions (one download and upload per version)
and records their original authorship; the report also counts `sourceChangedAfterMove` and `versionsTruncated` (stated;
neither blocks `-Verify`). Read-only cross-checks after `-Verify`: `SELECT COUNT(sprk_documentid) FROM sprk_document WHERE
sprk_relocationpending IS NOT NULL` (only sources kept for another record may remain) and, on two relocated documents,
`GET /api/documents/{id}/versions` lists the same count, dates and authors as before the move.
**25 Strict rule ON** — as §21.11. 26–27 as §20.11.

## 22.12 Found, decisions recorded, integration notes, `.claude`

- **Decision recorded (made within round 45 item 4, fail closed):** which content ends CURRENT after a re-copy — the last
  post-move edit only when its author may write the document now; otherwise the document's own content (§22.4). Every
  edit is carried either way. Reason: the old file stayed writable by the old container's audience.
- **Decision recorded:** the external version list keeps `createdByName` null (§22.3).
- **Residual (degraded environments only):** where two `sprk_document` rows of different records share ONE item (possible
  only while the unique key `sprk_graphitemid_uk` is not Active; it is Active on spaarkedev1), edits the OTHER record's
  users make to the kept source after this document's move, once that record releases the source, are carried into this
  document's HISTORY (never current unless their author may write this document). Before the move both records already
  served that one item to both audiences.
- **Residual (pre-existing to field security, unchanged):** if the BFF application user ever loses the writer profile on
  the relocation columns, Dataverse returns them MASKED (empty) rather than refusing the read, so an owed ledger entry would
  read as none. Controls: the FLS script's `-Verify` (p3) and gate 24's read-only cross-check above.
- **Integration notes — task 150's lane:** `DocumentContainerRelocator` gains a constructor dependency, the existing
  Scoped `IAccessDataSource` (type activation — nothing to change for a DI caller; a test that `new`s the relocator passes
  one). **Updated by g (§23.11):** `DocumentsModule` now constructs it with the uncached `DataverseAccessDataSource`
  (still nothing to change for a DI caller). `DocumentRelocationBatchResult` gains `SourceChangedAfterMove` and `VersionsTruncated` (stated; `Complete` is
  unchanged in meaning); add them to the Make Secure `files_incomplete` / success report. A Make Secure request now replays
  each file's history — its duration grows with the history it carries.
- **Integration note — round 45 item 5:** `IRelocatedFileIndexing` (f1-v1) holds the last free slot under
  `ADR010_DITests`' 1:1-interface ceiling (158). This round adds NO interface. At integration, register it without a 1:1
  interface if ADR-010 allows; otherwise record the ceiling change in the PR's ADR block.
- **Integration note — shared test fixture:** `ExternalAccessContractFixture` now exposes its `IDataverseService` mock as
  `DataverseServiceMock` (was a local); a branch that edits the same lines merges textually.
- **Task 167's guard:** no route added, deleted or re-gated; the two version routes changed only their projection.
- **`.claude` edit (main session only):** none new; §12's `bff-deploy/SKILL.md:50` edit still stands.

## 22.13 Not closed

- **Live writes (by design, main session):** gates 23a, 23, 24, 25 (§22.11) and §20.11's others. Nothing was written
  live, and this round made no live access at all.
- Nothing else is owed by task 166.

# g (2026-10-05): owner round 54 built; the f1-v2 verification's F-1 – F-5 closed; owner round 56 applied

> **Branch**: `task/uac-r2-166-g` from `task/uac-r2-166-f1-v2` @ `966f88a21` (baseSha).
> **Inputs (BINDING)**: rounds 1–58 on `work/unified-access-control-r2` (read at `0caca7e3e`, re-read at `6532bb494` when
> the main session's `NOTE-FROM-MAIN.md` arrived); rounds 37, 45 and 54 re-read in full. **Round 54 is this verification's
> answer**: item 1 (a post-move edit becomes current only on a FRESH Write check), item 2 (the relocation lock lasts as long
> as the move), item 3 (no intermediate edit is dropped), item 4 (every fail-closed branch tested; §22's claims corrected;
> the F-3 header). **Owner round 56** (relayed by `NOTE-FROM-MAIN.md`, read and NOT committed) sets the bar from now on:
> classify every finding (a)–(f), fix (a)–(c), record (d)–(f) as known limits, build nothing new for (d)–(f), and this is
> the LAST fix round for this lane. Round 54 was decided before round 56, so it stands and is built (round 56: "rounds
> 1–55 stand"); the class of each item is in §23.2 and the known limits are in §23.12.
> **Outcome**: every (a)–(c) item is closed in code and tests; every new check, and each of the seven branches the
> verifier found untested, has a test that its seed turns red (28 seeds, each alone in its own build, §23.8). §22's two
> seeding claims are corrected in place. No live access was made; nothing new is owed live (the §22.11 gates stand
> unchanged). No decision is requested.

## 23.1 Binding inputs as applied

| Decision | Implemented |
|---|---|
| Round 54 item 1 — the LAST post-move edit becomes current only when its author holds Write NOW; that check is read FRESH, never from the 60-second `CachedAccessDataSource`; app-only / unknown / faulted = not current; test: a stale cached Write answer cannot make an edit current, seeded | §23.3 |
| Round 54 item 2 — the relocation lock is RENEWED while the relocation runs (heartbeat, bounded interval); a move whose renewal fails stops before its next destructive step (re-point or source delete) and reports `lock-lost`, never continuing unlocked; test: a second relocator cannot start a move while the first holds a renewed lock, seeded | §23.4 |
| Round 54 item 3 — a witness with no version id, or a non-numeric id: order the post-move versions by their timestamps; undecidable → `history-undecidable`, row and source untouched (retried); never silently only the latest | §23.5 |
| Round 54 item 4 — a biting test for Sk, Sq, Si, Sm, Sj, Sw, Sd and the Unknown → never-delete witness branch; §22's "each new check seeded" / "every seed bit" corrected; the F-3 header corrected | §23.6, §23.8, corrections in §22 |
| Round 45 items 1 and 4 (the parts the verifier found untested: paging, the failure branches, Unknown → never delete) | §23.6 |
| Round 45 items 2, 3, 5 and round 37 items 1–3 | Verified MET by the f1-v2 verification; unchanged (the lock and settle changes keep every round-37 behaviour: the full targeted set is green) |
| Owner round 56 — classify, fix (a)–(c), record (d)–(f), no new machinery for (d)–(f), last round | §23.2 (class per item), §23.12 (known limits) |

## 23.2 The verifier's 14 items — class (owner round 56) and what changed

| # | Item | Class | Closure |
|---|---|---|---|
| 1 | Rounds 37, 45, 54 owed | — | §23.1 |
| 2 | **F-1** — Sk, Sq, Si, Sm, Sj, Sw untested | (b) important fail-closed behaviour (no data loss, no unprovable delete) with no behaviour test | One test or theory each (§23.6); each seeded red alone (§23.8) |
| 3 | **F-2** — the version-list paging untested; a cut listing would lose history silently | (b), guarding an (a) data-loss path | `DocumentVersionListPagingTests` drives the REAL `DriveItemOperations.ListFileVersionsAsync` through a scripted Kiota `IRequestAdapter` (the `SpeContainerPagingTests` precedent; no `Mock<HttpMessageHandler>`): two pages, three pages, one page = one request, a listing that never ends. The listing is BOUNDED (`MaxVersionPages` = 500): past it, it throws "could not be fully enumerated" and the relocation that asked fails, reported — never returned cut (and never an endless loop holding a renewed lock). Seeds Sd, Sd2 red |
| 4 | **F-3** — the KEEP-path header said "byte-identical" | (b) comment contradicts code | Rewritten: "unless it still matches the WITNESS recorded when its copy was verified … never compared with the document's current file", plus the lock rule |
| 5 | **F-4** — a fixed 10-minute lock with no renewal | (a) two moves of one document could race on a real path (a long history + a Make Secure retry or the migration pass) | Round 54 item 2, §23.4 |
| 6 | **F-5** — `History.After` dropped intermediate post-move edits without a numeric witness | (a) data loss, rare (Graph listed no version at the move, or non-numeric ids) | Round 54 item 3, §23.5 |
| 7 | Verified MET by the verifier's seeds | — | Unchanged; the full targeted set (309) is green |
| 8 | The verifier's runs on `966f88a21`; its worktree `C:\wvh166` | — | Recorded. This round's runs §23.9. `C:\wvh166` is the verifier's; this lane did not touch it |
| 9 | Hygiene | — | Holds: POML parses as XML; `NOTE-FROM-MAIN.md` read, never staged; no `.claude/`, `TASK-INDEX.md` or `current-task.md` change; no live write, no live access; no script changed (they default to dry run) |
| 10 | Round 45 items 2, 3, 5 met; 1 and 4 met except the untested branches | — | The untested branches are tested (§23.6) |
| 11 | Criterion — seeding | (b) | **Met** (§23.8): 28 seeds, each alone in its own build; every one turns red the test written for it, none missing |
| 12 | Criterion — round 45 item 4 as tested | (b) | `ASourceThatCannotBeComparedWithItsWitness_IsNeverDeleted` (faulted / not-found / empty version list) — seed Sk turns all three red |
| 13 | Criterion — round 45 item 1 "never silent" | (b) | The paging tests — seeds Sd / Sd2 red |
| 14 | Criterion — §22's claims | (b) | Corrected in place in §22 (header Outcome, §22.2 item 15, §22.3, §22.4, §22.8, §22.9, §22.12), each marked "corrected by g" / "superseded by g" |

## 23.3 Round 54 item 1 — the Write check that makes a post-move edit current is read FRESH

- **Where the stale answer came from.** `IAccessDataSource` resolves to `CachedAccessDataSource` (`SpaarkeCore`), which keeps
  each `(user, resource)` answer for 60 seconds; the relocator took `IAccessDataSource` by type activation. A Write answer
  read just before Make Secure could therefore be served after it and make a non-writer's edit current.
- **The wiring.** `DocumentsModule` constructs the relocator with the UNCACHED `DataverseAccessDataSource` (the typed-client
  registration `AddSpaarkeCore` already makes, and that `CachedAccessDataSource` wraps):
  `services.AddScoped(sp => ActivatorUtilities.CreateInstance<DocumentContainerRelocator>(sp, sp.GetRequiredService<DataverseAccessDataSource>()))`.
  No service is added; the relocator's own registration changes from type activation to this factory.
- **The rule, in the relocator itself** (`MayWriteAsync`): whatever source it is given, an answer PRODUCED BEFORE THE
  QUESTION was asked is not a right. `AccessSnapshot.CachedAt` is when the answer was read — `DataverseAccessDataSource`
  stamps its Dataverse read, `CachedAccessDataSource` returns the time it CACHED the answer — so `CachedAt < asked` is a
  cached answer → not current (fail closed). Compared on the system clock, the clock both stamp with. Not a person /
  app-only / no answer / a fault → not current, as before.
- **Test** `AStaleCachedWriteAnswer_CannotMakeAPostMoveEditCurrent`: the REAL `CachedAccessDataSource` over an in-memory
  distributed cache answers Write for the editor; the underlying answer is then revoked (Make Secure); a precondition
  asserts the cache still answers Write; the repeat call must keep the edit in the history (`EditIsCurrent = false`, the
  document's own content current). Seed R1 → red.
- **Why the wiring has no test of its own**: asserting what `DocumentsModule` resolves is a DI-registration assertion
  (ADR-038 B3, banned). The binding behaviour — a cached answer never makes an edit current — is the relocator's own,
  tested rule; the wiring makes production answers fresh, so a writer's edit is not needlessly kept as history.

## 23.4 Round 54 item 2 — the relocation lock lasts as long as the move

- **The lease** (`DocumentContainerRelocator.RelocationLease`, private): each relocation call takes the ADR-004 processing
  lock under an owner id of its OWN (a new GUID per call), READS IT BACK as its own before anything runs, renews it every
  `RelocationLockRenewInterval` (2 minutes; each renewal lasts `RelocationLockDuration`, 10 minutes) on the relocator's
  `TimeProvider`, and releases it only while it is still its own.
- **Confirmed before every destructive step**: the re-point (else the copy is removed, the row and the source untouched,
  `Failed` with `lock-lost: …`); each ledger entry's settle (its re-keys and move-alongs); the source delete (else the
  source is kept, pending); the ledger write-back (else the stored ledger is left as it was — writing it unlocked could
  overwrite what another relocation now records). Between replay steps the move checks the heartbeat's verdict without a
  round trip and stops at the next version (the partial copy removed). A lost lease never counts as held again, and every
  later step of that call reports `lock-lost` and writes nothing. Every step is idempotent: the repeat call redoes what is
  owed.
- **The read-back closes two lock-store gaps for the relocator (#984; not widened to other callers):** `IdempotencyService`'s
  acquire is check-then-set and fails OPEN on a cache fault. A fail-open take does not read back as ours → nothing runs;
  two takers that both passed the check-then-set are told apart (the last writer's owner is what reads back; the other
  stops before its first destructive step).
- **Owner-checked release**: a relocation that lost its lock never removes the lock another relocation now holds.
- **One instance, one move per document**: the relocator records its leases by document; a second move of the same
  document on the same instance is refused even if the lock store stops excluding.
- **The lock store, extended — no new interface** (`IIdempotencyService`): `RenewProcessingLockAsync(eventId, ownerId,
  duration)` (a default method answering `false`: a store that cannot renew can never confirm a lock, so the caller stops)
  and `ReleaseProcessingLockAsync(eventId, ownerId)` (default: the ownerless release). `IdempotencyService` implements both:
  a renewal rewrites the lock only when it reads back as the owner's and FAILS CLOSED on a cache fault (unlike the
  acquire); the owner release removes only the owner's lock.
- **Tests**: `ASecondRelocator_CannotStartAMove_WhileTheFirstStillHoldsItsRenewedLock` (two relocators over the REAL
  `IdempotencyService` and ONE expiring cache on a `FakeTimeProvider`: the first pauses in its replay for 12 minutes —
  past one lock — while its heartbeat renews; the second is refused; the first completes and releases),
  `AMoveWhoseHeartbeatFindsTheLockLost_StopsItsCopyAtTheNextVersion`,
  `AMoveThatLosesItsLock_StopsBeforeTheRepoint_RemovesItsCopy_AndTouchesNothingElse`,
  `ARepeatCallThatLosesItsLock_ReKeysNothing_DeletesNothing_AndLeavesTheLedger`,
  `ALockLostRightBeforeTheSourceDelete_KeepsTheSource`, `ALockLostBeforeTheSettledLedgerIsWritten_LeavesTheStoredLedger`,
  `ARelocationWhoseLockWasTakenOver_NeverReleasesTheNewHoldersLock`, `ALockStoreThatAnswersTakenWithoutHoldingTheLock_RunsNothing`
  (the real store over a faulting cache), `OneRelocator_NeverRunsTwoMovesOfOneDocumentAtOnce_EvenWhenItsLockStoreStopsExcluding`;
  and the store's `ProcessingLockRenewalTests` (5: a renewal extends past the first expiry; without one the lock expires
  and another owner takes it; a renewal of another owner's / nobody's / an ownerless lock is refused and writes nothing; a
  renewal on a faulting cache is refused; an owner release leaves another owner's lock).

## 23.5 Round 54 item 3 — no intermediate edit is dropped

- **The witness records a time.** `RelocationWitness` gains `Modified`: the time of the content it witnessed — the current
  version's `lastModifiedDateTime`, or, when Graph lists no version, the item's own `lastModifiedDateTime`
  (`SpeItemCreator.LastModified`, read in the same uncached `GetItemCreatorAsync` call). A ledger without it still parses.
- **`History.After(witness)`**: by version NUMBER when the witness's version and every listed id are numbers (every version
  above the witnessed one; none above it = the witnessed version was changed in place, so its current content is the only
  content after the witness and nothing exists to drop); otherwise by TIME — every version written after the witness's
  time, in time order. The f1-v2 fallback to "the current content alone" is gone.
- **`history-undecidable`** when that order cannot be decided — the witness has neither a numbered version nor a time; a
  version carries no time; two versions written after the move carry the same time; the content changed yet no version is
  later than the witness; or the current content is not the latest version by time: the re-copy does not run, the row and
  the source are untouched, the ledger entry stays owed, the outcome is `RelocationPending` with a
  `history-undecidable: …` line naming the reason, and a repeat call retries.
- **Tests**: `AnEditAtTheOldLocation_WhoseVersionIdsAreNotNumbers_CarriesEveryVersionWrittenAfterTheMove_InTimeOrder` (two
  post-move versions, both carried; the reported history in time order with original authorship),
  `AWitnessRecordedWithoutAVersion_StillCarriesEveryVersionWrittenAfterTheMove_ByTheirTimes`, and the theory
  `APostMoveHistoryWhoseOrderCannotBeDecided_IsHistoryUndecidable_AndTheRowAndTheSourceAreUntouched` (5 cases, one per
  undecidable condition).

## 23.6 Round 54 item 4 — every fail-closed branch tested; §22 corrected

| Verifier seed | Branch | Test (all in `DocumentContainerRelocatorTests` unless named) |
|---|---|---|
| Sk | the witness cannot be compared (hashless witness; the source's version list faults, is not found, or is empty) → never deleted | `ASourceThatCannotBeComparedWithItsWitness_IsNeverDeleted` (3 cases) |
| Sq | the source's version list faults / is not found → the move fails before a byte moves (never the current content alone) | `ASourceWhoseVersionsCannotBeListed_IsNeverMoved_SoNoHistoryIsSilentlyLost` (2) |
| Si | an unprovable witness (no hash, no version listed) → the move never starts | `ASourceThatCouldNeverBeProvedUnchanged_IsNeverMoved` |
| Sj | a PRIOR version above the single-request bound → fails before a byte moves | `APriorVersionLargerThanASingleRequestCopy_FailsTheMove_BeforeAByteMoves` |
| Sm | a replay upload landing on another item → fails; the stray item and the partial copy removed | `AReplayedVersionWrittenToAnotherItem_FailsTheMove_AndRemovesBothItems` |
| Sw | the copy's versions cannot be listed (fault / none) → the copy removed, the row never re-pointed | `ACopyWhoseVersionsCannotBeListed_IsRemoved_AndTheRowIsNeverRepointed` (2) |
| Sd | the version listing follows every page | `DocumentVersionListPagingTests` (4) |

§22 is corrected in place: the header's "each new check seeded", §22.2 item 15's "Met", §22.9's "every seed bit" (true only
of the seeds listed there), §22.3's paging line, §22.4 / §22.8 / §22.12's "the EXISTING registered `IAccessDataSource`".
The relocator test file's KEEP-path header (F-3) now states the witness rule and the lock rule.

## 23.7 Placement (CLAUDE.md §10) and component justification (§11; owner round 56 item 2's over-engineering check)

**Placement: in BFF**, in the existing types (the relocation is the BFF's; the lock store is the BFF's ADR-004 store).
ADR-002: no plugin. ADR-003: every new decision fails closed (a lost or unconfirmed lock writes nothing; a cached answer is
not a right; an undecidable history moves nothing; an unending listing fails the move). ADR-004: the existing processing
lock, extended. ADR-007: no Graph type leaks (`SpeItemCreator` gains a `DateTimeOffset?`). ADR-010: no new interface
(the 1:1 ceiling is unchanged); one registration changes from type activation to a factory. ADR-013: no AI type.
ADR-038: no `Mock<HttpMessageHandler>` (the Kiota adapter is the SDK's own boundary), no DI or ctor-null assertion,
`TimeProvider` (`FakeTimeProvider`) for time. No package, endpoint, option, job, column, PCF or plugin.

- **`IIdempotencyService.RenewProcessingLockAsync` / `ReleaseProcessingLockAsync(eventId, ownerId)`** + their
  `IdempotencyService` implementations — Existing: the acquire / ownerless release of the same store (no renewal, no owner
  check on release); Extension: extended in place (default methods, so every other implementer compiles unchanged);
  Cost of nothing: a move longer than 10 minutes runs unlocked and a second relocation copies and re-points the same
  document (F-4), or a relocation that lost its lock deletes the new holder's lock.
- **`RelocationLease`** (private nested class of the relocator, no registration) — Existing: none in the relocator (a bare
  acquire/release pair); Extension: it IS the relocator's acquire/release, given an owner, a read-back, a heartbeat and a
  verdict; Cost of nothing: no renewal and no "stop before the next destructive step" (round 54 item 2).
- **`RelocationWitness.Modified` + `SpeItemCreator.LastModified`** (+ `lastModifiedDateTime` in the existing uncached item
  read) — Existing: the witness's version id; Extension: one field each, one more selected property on the same call;
  Cost of nothing: with no numbered version the post-move edits cannot be ordered, so every such source stays
  `history-undecidable` forever instead of being carried (round 54 item 3).
- **`DriveItemOperations.MaxVersionPages`** (a constant bound) — Existing: the unbounded paging loop; Extension: one
  bound in that loop; Cost of nothing: a listing that never ends loops forever while the heartbeat keeps the document
  locked.
- **`LockLostPrefix`, `HistoryUndecidablePrefix`** — report codes beside the existing `source-changed-after-move` /
  `index-pending` (round 54 names them).
- **`DocumentsModule`** — the relocator's existing registration, now a factory passing the uncached source (no new
  registration).
- Nothing was built for a (d)–(f) item.

## 23.8 Seeding (2026-10-05; one seed per build, 28 builds; byte copies restored and touched; no `SEED166G` marker remains)

Harness: each seed edits one or more lines, builds `tests/unit/Sprk.Bff.Api.Tests`, runs the targeted set (309 tests:
relocator, migration job, facade, RAG trim, pointer check, attach, version-route auth, external contract, lock renewal,
version paging), records the red tests, restores. Every seed turned red the test(s) written for it; none missing.

| Seed | What it breaks | Red |
|---|---|---|
| R1 | the CachedAt freshness rule off | `AStaleCachedWriteAnswer_CannotMakeAPostMoveEditCurrent` |
| R2a | no heartbeat | `ASecondRelocator_CannotStartAMove_…`, `AMoveWhoseHeartbeatFindsTheLockLost_…` |
| R2b | no confirmation before the re-point | `AMoveThatLosesItsLock_StopsBeforeTheRepoint_…`, `ARelocationWhoseLockWasTakenOver_…` |
| R2c | no confirmation per ledger entry | `ARepeatCallThatLosesItsLock_…`, `ALockLostRightBeforeTheSourceDelete_…`, `ALockLostBeforeTheSettledLedgerIsWritten_…` |
| R2d | no confirmation before the source delete | `ALockLostRightBeforeTheSourceDelete_KeepsTheSource` |
| R2e | no confirmation before the ledger write | `ALockLostBeforeTheSettledLedgerIsWritten_LeavesTheStoredLedger` |
| R2f | the lease releases ownerless | `ARelocationWhoseLockWasTakenOver_NeverReleasesTheNewHoldersLock` |
| R2g | no read-back at the take | `ALockStoreThatAnswersTakenWithoutHoldingTheLock_RunsNothing` (+5 lock tests) |
| R2h | the settle continues after a lost lock (index step) | `ALockLostRightBeforeTheSourceDelete_KeepsTheSource` (index assertion) |
| R2i | the replay continues after the heartbeat lost the lock | `AMoveWhoseHeartbeatFindsTheLockLost_StopsItsCopyAtTheNextVersion` |
| R2k | a second lease of one document on one instance accepted | `OneRelocator_NeverRunsTwoMovesOfOneDocumentAtOnce_…` |
| R3a | the f1-v2 fallback (current content only) | both carry-every-version tests + 4 undecidable cases |
| R3b / R3c / R3d / R3e / R3f | each undecidable condition allowed | its own theory case (same-time / times-contradict-numbers / no-witness-time / a-version-without-time / no-later-version) |
| Sk | `Unknown` falls through to the delete | `ASourceThatCannotBeComparedWithItsWitness_IsNeverDeleted` (3) |
| Sq | a faulted source version list read as empty | `ASourceWhoseVersionsCannotBeListed_…(faults: True)` |
| Si | an unprovable witness accepted | `ASourceThatCouldNeverBeProvedUnchanged_IsNeverMoved` |
| Sj | the per-version size bound off | `APriorVersionLargerThanASingleRequestCopy_…` |
| Sm | an upload on another item accepted | `AReplayedVersionWrittenToAnotherItem_…` |
| Sw | an unlistable copy accepted | `ACopyWhoseVersionsCannotBeListed_…` (2) |
| Sd | version-list paging off (`while (false && …)`) | `AHistoryOnTwoPages_…`, `AHistoryOnThreePages_…`, `AListingThatDoesNotEnd_…` |
| Sd2 | the page bound off | `AListingThatDoesNotEnd_IsNeverReturnedAsTheWholeHistory` |
| I1 | a faulted renewal read as renewed | `ARenewal_WhenTheCacheFaults_IsRefused_NotAssumed`, `ALockStoreThatAnswersTaken…` |
| I2 | a renewal ignores the owner | `ARenewal_OfALockAnotherOwnerHolds_…`, `WithoutARenewal_TheLockExpires_…` |
| I3 | a release ignores the owner | `AnOwnersRelease_RemovesOnlyItsOwnLock` |

The verifier's earlier seeds (S1–S21, V5/V6/V8, the client and script seeds) are unaffected: their tests are unchanged
and green.

## 23.9 Gates (this round, final code `53325bc6a`)

| Gate | Result |
|---|---|
| Affected tests | the targeted set (filter `DocumentContainer`, `DocumentVersionAuthorization`, `ExternalAccessContract`, `DocumentPointer`, `DocumentFileAttach`): **309 passed, 0 failed** (273 at base; +36: relocator +27 cases, `ProcessingLockRenewalTests` 5, `DocumentVersionListPagingTests` 4); the relocator class run 5 times in a row, green each time (the two time-driven tests are stable) |
| Full BFF unit suite (`tests/unit/Sprk.Bff.Api.Tests`) | **14,711 total: 14,657 passed, 54 skipped, 0 failed** (27 m 47 s; +36 vs f1-v2's 14,675) |
| NetArchTest (`tests/Spaarke.ArchTests`) | **406 passed, 0 failed** |
| Sprk.Bff.Api.IntegrationTests (full) | **104 passed, 0 failed** |
| Spe.Integration.Tests (full) | **405 total: 380 passed, 25 skipped (environment-gated `SkippableFact`s), 0 failed** |
| `dotnet list package --vulnerable --include-transitive` | `Sprk.Bff.Api` has no vulnerable packages |
| Formatting | the pre-commit `dotnet format` ran on every staged `.cs` file; every changed `.cs` file is CRLF |
| Publish size (CLAUDE.md §10) | base `966f88a21` **45.765 MB** (47,988,516 bytes) vs branch `53325bc6a` **45.772 MB** (47,995,924 bytes) = **+0.007 MB** (+7,408 bytes); 212/212 files; both from FRESH short-path trees (an archive of each commit extracted into `C:\wt166gm` / `C:\wt166gb`, removed afterwards), `dotnet publish -c Release`, PowerShell `Compress-Archive -CompressionLevel Optimal`, PDBs included. No package |
| Client builds | none needed: no client source changed |

## 23.10 Manual live gates

None new. §22.11's gates 23a (schema), 23 (FLS), 24 (migration), 25 (strict rule) stand unchanged and pending (main
session). No script changed. Nothing was read or written live this round.

## 23.11 Found, decisions recorded, integration notes, `.claude`

- **Decisions recorded (within round 54, no owner question):** freshness is enforced by the relocator's own rule as well
  as by the wiring (the rule is the testable guarantee; ADR-038 B3 bans asserting the wiring). In the version-number
  path, "no version above the witnessed one" means the witnessed version was changed in place, so its current content is
  carried (nothing else exists after the witness). A version listing past 500 pages fails the move (reported, retried).
- **Integration — the lock store:** `IIdempotencyService` gains two DEFAULT methods; only the relocator calls them, so
  every other implementer and every Moq mock of the interface is unaffected. A branch that edits `IdempotencyService.cs`
  merges textually (the new members sit before `IsStaleLockOf`).
- **Integration — task 150's lane and 147's `SecureChildReconciliationJob` (round 46 item 2):** they resolve the
  relocator from DI unchanged (the registration is now a factory). A Make Secure request that replays a long history now
  holds its lock for the whole move; a retry meanwhile gets `Failed` "another relocation of this document is running, or
  its lock could not be confirmed" (incomplete, retried) — never a second copy. Pending lines may now also read
  `lock-lost: …` and `history-undecidable: …` (both incomplete, both settled by a repeat call); the batch result's shape is
  unchanged.
- **Integration — round 45 item 5:** this round adds NO interface; `IRelocatedFileIndexing` still holds the last slot
  under the ADR010 1:1-interface ceiling (§22.12).
- **Task 167's guard:** no route added, deleted or re-gated.
- **`.claude` edit (main session only):** none new; §12's `bff-deploy/SKILL.md:50` edit still stands.

## 23.12 Known limits (owner round 56: one line each; no fix round)

- **(e)** The `DocumentsModule` wiring (uncached access source) has no test of its own — a DI-registration assertion is
  banned (ADR-038 B3); reverting it only degrades toward fail-closed (a writer's post-move edit kept as history), never
  toward exposure, because the relocator's own tested rule refuses a cached answer.
- **(e)** The CachedAt rule compares clocks: with a CACHED source shared across instances, an answer cached on an instance
  whose clock runs ahead by more than the entry's age could pass it; production passes the uncached source, so this needs
  a misconfiguration.
- **(e)** `IdempotencyService`'s renewal and owner release are read-then-write, like its acquire (#984): a lock that
  expires exactly between a renewal's read and write could overwrite a new taker's lock; the other taker then fails its
  next confirmation and stops before its next destructive step (last writer wins) — fail closed. The store's fail-open
  acquire (#984) is unchanged for its other callers.
- **(e)** A version history longer than 500 pages fails its move (reported, retried, never cut); no SPE history comes near.
- **(e)** A `history-undecidable` source stays owed for as long as Graph keeps returning version metadata whose order
  cannot be decided; each repeat call reports it with the reason, and nothing is lost meanwhile.

## 23.13 Not closed

- **Live writes (by design, main session):** §22.11's gates 23a, 23, 24, 25 and §20.11's others. Nothing was written live,
  and this round made no live access at all.
- Nothing else is owed by task 166. Per owner round 56 this was the lane's last fix round; no (a)–(c) item remains.
