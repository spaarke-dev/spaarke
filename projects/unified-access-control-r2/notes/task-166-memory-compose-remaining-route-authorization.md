# Task 166: Memory, Compose and remaining-surface route authorization

> **Task**: `tasks/166-memory-compose-and-remaining-route-authorization.poml` (GitHub #1105)
> **Branch**: `task/uac-r2-166` from `work/unified-access-control-r2` @ `e6dd48b43`
> **Rigor**: FULL (bff-api, auth, security; TEST-MODIFYING override applies)
> **Outcome**: ESCALATED. Every route in scope is fixed or deleted EXCEPT the reporting module, which is STOPPED
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

## 8. Escalations fired (CLAUDE.md §6) — awaiting the owner

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
| `GET /api/reporting/embed-token`, `POST /api/reporting/export`, `GET /api/reporting/reports/{reportId:guid}`, `GET /api/reporting/reports`, `POST /api/reporting/reports`, `PUT /api/reporting/reports/{reportId:guid}`, `DELETE /api/reporting/reports/{reportId:guid}` | UNCHANGED — STOPPED (trigger 5) | — (Pending waivers STAY, owner 166) |

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
9. (g) reporting: not applicable — STOPPED (trigger 5).
