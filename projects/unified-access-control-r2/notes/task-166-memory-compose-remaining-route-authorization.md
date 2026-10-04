# Task 166: Memory, Compose and remaining-surface route authorization

> **Task**: `tasks/166-memory-compose-and-remaining-route-authorization.poml` (GitHub #1105)
> **Branch**: `task/uac-r2-166` from `work/unified-access-control-r2` @ `e6dd48b43`; **r1**: `task/uac-r2-166-r1` (§18)
> **Rigor**: FULL (bff-api, auth, security; TEST-MODIFYING override applies)
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
15. Pointer check: a download of any existing dev document still works (0 of 530 refused, §18.4); a test row whose
    `sprk_graphdriveid` is set (by an administrator, in a scratch row) to a drive id no business unit owns → 409
    `document_storage_unverified`, no bytes.
16. Round 21 item 1 (a) — ONLY after the owed steps (i) and (ii) (§18.3):
    `pwsh scripts/Set-DocumentPointerFieldSecurity.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -ClientNoLongerWritesPointers -Apply`,
    then the same with `-Verify` (exit 0).

No `.claude` edit is needed for this round (§12's edit stands).
