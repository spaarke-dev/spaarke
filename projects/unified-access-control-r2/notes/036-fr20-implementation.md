# Task 036 — FR-20 flag-gated swap: implementation notes

> **Branch**: `feat/uac-036-fr20-flag-gated-swap`, cut from R3's tip `origin/work/spaarke-SPA-external-access-platform-r3`
> @ `5b2375f6f` (like #2061). Merges after R3's interim → master PR and after #2061.
> **Binding**: the task POML (`tasks/036-fr20-flag-gated-swap.poml`) — its 2026-09-30 amendments, the owner round 59 block
> (2026-10-05) that sets the rights shape, and the round 75 approval (2026-10-06) of ADR-034 path B.
> **Live**: nothing was run against dev and nothing was written to dev. The NFR-04 canary is a manual gate (below).

## 1. What changed, in one paragraph

`ExternalAccess:ImpersonatedRootSets:Enabled` (default `false`) chooses, once per composition, who decides a systemuser's
SPA/Teams access. **Off**: the ADR-034 membership walk, byte-for-byte as before. **On**: the walk is not run at all; the
systemuser's own term is Dataverse's answer from `IImpersonatedRootSetSource` at **Read**, and every write route on
`/api/v1/external` that a systemuser can reach asks Dataverse, as the caller, through the existing
`CallerRecordAccessProbe` for the caller's rights on the target record before writing. The contact-grants union, the deny
veto and the Restricted veto run unchanged in both states. A capped root set (either state) now reaches the module data
response as `"truncated": true`.

## 2. Design

### 2.1 Caller kinds per plane

| Caller | Plane | Flag off | Flag on |
|---|---|---|---|
| CIAM contact | `CiamContact` | grants (unchanged) | grants (unchanged). `DataverseConfirmsWrites` is never set — no probe call |
| Workforce contact (no licence) | `Workforce`, no `SystemUserId` | grants (unchanged) | unchanged — no probe call |
| Systemuser | `Workforce` + `SystemUserId` | membership walk at `MembershipTermRights` ∪ linked-contact grants | impersonated set at Read ∪ linked-contact grants; writes confirmed by the probe |
| WorkforceHome-linked systemuser (ADR-028 A9) | `Workforce` (scheme decides first — the WorkforceHome guard in `DeterminePlane` is untouched) | as systemuser | as systemuser for the read set; **the probe cannot exchange a customer-directory token**, so its writes deny unless grants allow (open question a) |

`WorkforcePrincipalStrategy` sets `CallerPrincipal.DataverseConfirmsWrites` from `Sources.ImpersonatedRootSet` (any of
the three root types; the flag is read per composition, so any = all, and "any" is the fail-closed reading) and
`CallerPrincipal.TruncatedRootEntityTypes` from `AccessibleRecordSet.Capped`.

### 2.2 Composition (`AccessibleRecordSetService.ComposeForSystemUserAsync`)

- `:1883` reads the flag; `:1884` logs `[WF-AUTHZ] Access source for systemuser {id} on {type}: ImpersonatedRootSet|Membership`.
- `:1895-1903` flag on: `ReadDataverseRootSetAsync` → ids at `AccessRights.Read`, `Capped = answer.Truncated`,
  `CapLimit = ImpersonatedRootSetSource.RowCap` (5,000) — the existing Capped/CapLimit fields (round 59 item 3).
- `:1904-1917` flag off: the unchanged walk at `MembershipTermRights`.
- `ReadDataverseRootSetAsync` throws on: no source registered, `Guid.Empty`, any source exception (logged `[WF-AUTHZ]`,
  rethrown). `SystemUserId == null` already threw (`:1857`, task 020 invariant). No catch above it: the
  `CallerPrincipalAuthorizationFilter` lets the exception fail the request — no handler runs (NFR-01).
- The own term replaces `membershipTerm` everywhere after, including the 4th argument of `ApplyVetoPipeline` — under the
  flag the Restricted survivor is the Dataverse term at Read, never `MembershipTermRights` (amendment 3).
- Flag on returns `Sources = (SystemUserMembership: false, ContactGrants, StandingGrantMembership: false, …,
  ImpersonatedRootSet: true)`.

### 2.3 Writes (`ExternalProjectDataEndpoints.WithDataverseRightsAsync`, `:855`)

`WithDataverseRightsAsync(caller, composed, entitySet, recordId, readCallerRights, logger, ct)`:

- returns `composed` unchanged, asking nothing, unless `caller.DataverseConfirmsWrites` **and** `composed` has Read — so a
  record outside the set, or vetoed off it, is refused by the route with no Dataverse call;
- otherwise returns `composed | SurfaceRightsFromDataverse(probe answer)`. Widening, not replacing, because the only way
  `composed` carries more than Read under the flag is the contact-grants union (B-17).
- a probe exception (other than the caller's own cancellation) or a null reader adds `None` (NFR-01). The real probe
  already answers `None` on a missing token, a failed exchange or a non-2xx.

`SurfaceRightsFromDataverse` (`:911`): Read, Write and Delete as Dataverse answers them; **AppendTo → Create** ("may add a
child under this record"). Dataverse's own `CreateAccess` from RetrievePrincipalAccess is table-level (the caller's create
privilege on the record's table), not "may attach to this record", so it carries nothing. Precedent: `OperationAccessPolicy`
`entity.associate_document` / `event.attach_regarding` = AppendTo.

`CallerRightsReader(probe, httpContext)` (`:922`) binds the probe to this request's bearer token
(`TokenHelper.ExtractBearerTokenOrNull`).

### 2.4 Write-route inventory — every write under `/api/v1/external`

Route map lines are `ExternalProjectDataEndpoints.cs` unless named. "Confirmed" = `WithDataverseRightsAsync` runs before
the write for a Dataverse-decided caller.

| Route | Map | Target record asked about | Right needed | Confirmation site |
|---|---|---|---|---|
| `POST /projects/{id}/todos` | `:153` | the project (`sprk_projects`) | AppendTo → Create | `CreateTodoForRoot` → `RightsForRootWriteAsync` `:729`, `:930` |
| `POST /matters/{id}/todos` | `:183` | the matter (`sprk_matters`) | AppendTo → Create | same |
| `POST /workassignments/{id}/todos` | `:199` | the work assignment (`sprk_workassignments`) | AppendTo → Create | same |
| `POST /projects/{id}/documents` | `:211` | the project | AppendTo → Create | `UploadDocumentForParent` `:1448` → `RightsForDocumentParentAsync` `RootRightsForParentAsync` |
| `POST /matters/{id}/documents` | `:259` | the matter | AppendTo → Create | same |
| `POST /workassignments/{id}/documents` | `:295` | the work assignment | AppendTo → Create | same |
| `POST /invoices/{id}/documents` | `:372` | **the invoice** (`sprk_invoices`), before the secure-roots gate | AppendTo → Create | `RightsForInvoiceAsync` `:1093` |
| `POST /servicerequests/{id}/documents` | `:331` | — not confirmed | requester rule | open question (e) |
| `POST /projects/{id}/events` | `:419` | the project | AppendTo → Create | `CreateEvent` `:1895` |
| `PATCH /todos/{id}` | `:445` | **the to-do** (`sprk_todos`); the root's Read stays the scope check | Write | `UpdateTodo` `:2018` |
| `DELETE /documents/{id}` | `:464` | the row (`sprk_documents`) | Delete (+ creator stamp + parent Read, unchanged) | `AuthorizeDeleteOwnAsync` step (2b) `:2167` |
| `DELETE /todos/{id}` | `:474` | the row (`sprk_todos`) | Delete (+ stamp + Read) | same |
| `DELETE /events/{id}` | `:482` | the row (`sprk_events`) | Delete (+ stamp + Read) | same |
| `DELETE /servicerequests/{id}` | `:494` | — not confirmed | requester rule | open question (e) |
| `POST /{root}/{id}/messages` | `ExternalMessageEndpoints.cs:107` | the project / matter / work assignment / invoice itself; a service request is not confirmed | AppendTo → Create | `PostMessageCoreAsync` `:251-253` |
| `POST /servicerequests` (intake) | `ExternalIntakeEndpoints.cs:110` | no existing target — a new request filed by the caller's own contact | module entitlement (never membership) | not applicable |
| `POST /notifications/seen` | `ExternalNotificationFeedEndpoints.cs:109` | the caller's own last-seen marker | — | not applicable |
| `POST /contact-grants`, `POST /contact-grants/revoke` | `ContactGrantEndpoints.cs:50`, `:77` | — | — | **unreachable by a systemuser**: `ContactGrantorAuthorizationFilter` refuses one (`ContactGrantorAuthorizationFilter.cs:339`) |
| `POST /registration`, `POST /workforce/link` | `ExternalRegistrationEndpoints.cs:78`, `ExternalWorkforceLinkEndpoints.cs:75` | the caller's own identity link | — | not applicable (no core record) |
| `POST /api/dataverse/fetch` | `ExternalModuleDataEndpoints.cs:128` | read, not a write | — | — |

No write on the surface rests on `MembershipTermRights` under the flag: the only writes not confirmed are the requester's
own service request (requester rule), self-identity and self-state writes, none of which consult membership.

### 2.5 Truncation (NFR-03)

- `FetchResponseDto.Truncated` — `[JsonPropertyName("truncated")]`, `[JsonIgnore(WhenWritingDefault)]`: omitted when
  false, so an uncapped response is byte-identical to before (the `ExternalCollectionResponse.Truncated` convention).
- `ExternalModuleDataEndpoints.ExecuteScopedFetchCoreAsync` sets it when `principal.TruncatedRootEntityTypes` is
  non-empty, on both Ok returns (including the empty-dimension 0-rows path), and logs a warning.
- **Both flag states.** Before this task the membership walk's `Capped` was never propagated to any client (pre-existing,
  fixed in scope: the AC "when the cap is hit" is not qualified by flag).
- **Over-reporting, deliberately.** A scope dimension is a lambda over the principal, so which root types a module reads
  is not knowable at the seam; ANY truncated root set marks every module fetch for that caller ("may be incomplete" is the
  safe direction).
- Client: `FetchMultipleResult.truncated?: boolean` (`IDataverseClient.ts`); `BffDataverseClient` passes the JSON through
  unchanged. Rendering the "Only 5,000 records displayed" banner is not in this task (known limit).

## 3. Decisions

| # | Decision | Why |
|---|---|---|
| D1 | Round 59 shape: the impersonated set confers Read; writes confirmed per route, per record. No batched RPA over the set | Owner round 59 item 2 (the review's cheaper shape replaces the 2026-09-30 per-root rights map) |
| D2 | Widen (`composed | Dataverse`), not replace | Keeps the B-17 contact-grants union on writes; under the flag no constant can reach `composed` |
| D3 | AppendTo → Create; Dataverse `CreateAccess` ignored | RPA's CreateAccess is table-level; AppendTo is "attach to this record" everywhere else in the BFF |
| D4 | PATCH to-do confirms on the to-do (Write), not its root | The target record is the to-do; the root's Read stays the scope check |
| D5 | Delete-own adds Dataverse Delete on the row; the creator stamp stays required | FR-26 stamp unchanged; Dataverse adds its own answer, never stands in for the stamp |
| D6 | Invoice upload confirms on the invoice, before the secure-roots gate | The invoice is the target record; a Dataverse-confirmed Create meets the same gate as a grant-derived one |
| D7 | Service-request writes not confirmed | Requester scope is neither a grant nor membership; widening could only add nothing; replacing would refuse requesters Dataverse does not admit (their request is owned by their BU's team). Owner question (e) |
| D8 | Read routes (lists, `canCreate`, `/me`) ask nothing | Cost; a per-row RPA on every list is the shape round 59 rejected. Consequence: UX gap (g) |
| D9 | No DI change | `CallerRecordAccessProbe` (typed client, `ExternalAccessModule.cs:157`), `IImpersonatedRootSetSource` (`:317`) and the options binding (`BindConfiguration("ExternalAccess")`) already exist; `AccessibleRecordSetService` (`:542`) takes the source and options as optional constructor parameters. The POML names `ExternalAccessModule.cs` as a modified file; nothing there needed to change |
| D10 | Truncation in both flag states | The AC is unqualified; flag-off capping was silently dropped (pre-existing defect) |
| D11 | Flag-off membership `Capped` previously dropped — fixed in scope | Same code path; filing it separately would leave NFR-03 half-done |

## 4. AC → test map

Test files: `AccessibleRecordSetServiceTests` (ARS), `CallerPrincipalResolverTests` (CPR), `ExternalTodoScopeTests` (TODO),
`ExternalRecordDocumentRoutesTests` (DOC), `ExternalDeleteOwnTests` (DEL), `ExternalMessageSendTests` (MSG),
`ExternalModuleColumnAllowListTests` (MOD), `ImpersonationNegativeCanaryTests` (CAN).

| AC | Test(s) | Twin (must-not-fire) |
|---|---|---|
| Flag off identical; zero test edits for flag off | ARS `Fr20_FlagOff_IsTheMembershipWalk_AndNeverAsksTheSource`; every pre-existing test unchanged (diff of existing tests: additions only; one ArchTest ledger seam path updated, §6) | — |
| Flag on: shared record appears | ARS `Fr20_FlagOn_ASharedRecord_Appears_AtExactlyRead` | ARS `Fr20_FlagOff_ASharedRecord_IsAbsent` |
| Flag on: BU-only match absent | ARS `Fr20_FlagOn_ABusinessUnitOnlyMatch_IsAbsent` | ARS `Fr20_FlagOff_ABusinessUnitOnlyMatch_IsPresent` (flag-off unchanged — no under-grant) |
| Flag on: BU default team record Dataverse does not admit is absent | ARS `Fr20_ADefaultTeamOwnedRecordDataverseDoesNotAdmit_FollowsTheFlag` (both states) | same theory, flag-off row |
| Flag on parity: BU/role-depth record appears | ARS `Fr20_FlagOn_ARoleDepthRecord_Appears` | — |
| Contact-grants union in both states | ARS `Fr20_TheContactGrantsUnion_SurvivesInBothStates`; TODO `Fr20_CreateTodo_ContactGrantUnion_StillAllows` | — |
| Source throw / missing SystemUserId → deny, no fallback | ARS `Fr20_FlagOn_ASourceThrow_FailsTheComposition_AndNeverFallsBackToMembership`, `Fr20_FlagOn_AnEmptySystemUserId_FailsBeforeAnyRead`, `Fr20_FlagOn_AMissingSystemUserId_Fails`, `Fr20_FlagOn_WithNoSource_FailsClosed` | — |
| Failed rights lookup denies, never a constant | TODO `Fr20_CreateTodo_ProbeFault_Is403_AndWritesNothing`; DOC `Fr20_Upload_AProbeThatFaults_Is403_AndWritesNothing`; DEL `Fr20_AProbeThatFaults_Is403_AndNothingIsDeleted`; MSG `Fr20_AReaderThatThrows_OrNoReader_Is403_AndCreatesNoRow` | — |
| Read-not-Write → 403 on EACH write route | TODO `Fr20_CreateTodo_ReadOnlyInDataverse_Is403_AndWritesNothing` (3 roots), `Fr20_PatchTodo_IsConfirmedOnTheTodo` (Read row), `Fr20_CreateEvent_IsConfirmedOnTheProject` (Read\|Write row); DOC `Fr20_Upload_ReadAndWriteButNoAppendToInDataverse_Is403_AndWritesNothing` (4 parents); DEL `Fr20_TheCreator_WithoutDeleteInDataverse_Is403_AndNothingIsDeleted` (3 kinds); MSG `Fr20_ReadAndWriteButNoAppendToInDataverse_Is403InsufficientRights_AndCreatesNoRow` (4 roots) | TODO `Fr20_CreateTodo_FlagOff_NeverAsksDataverse`, `Fr20_PatchTodo_FlagOff_NeverAsksDataverse`, `Fr20_CreateEvent_FlagOff_NeverAsksDataverse`; DOC `Fr20_Upload_FlagOff_TheComposedRightsDecide_AndDataverseIsNeverAsked`; DEL `Fr20_FlagOff_TheSystemUserCreatorDeletes_AndDataverseIsNeverAsked`; MSG `Fr20_FlagOff_TheComposedRightsDecide_AndDataverseIsNeverAsked`, `Fr20_FlagOff_AFullAccessSystemUserPosts_AndDataverseIsNeverAsked` |
| Write granted → allowed | TODO `Fr20_CreateTodo_AppendToInDataverse_IsCreated`, PATCH and event theory rows; DOC `Fr20_Upload_AppendToInDataverse_Is201`; DEL `Fr20_TheCreator_WithDeleteInDataverse_Deletes`; MSG `Fr20_AppendToInDataverse_Is201` | — |
| Outside the set / vetoed → refused with no Dataverse call | TODO `Fr20_CreateTodo_RootOutsideTheSet_Is403_AndAsksNothing`; MSG `Fr20_ARecordOutsideTheSet_IsNotAccessible_AndAsksDataverseNothing`; DEL `Fr20_DeleteInDataverse_NeverStandsInForTheCreatorStamp` | — |
| Restricted survivor = Dataverse term at Read; contact grant removed | ARS `Fr20_ARestrictedRecord_KeepsTheOwnTermAsItsSurvivor_AndLosesTheContactGrant` (both states) | flag-off row |
| ApplyVetoPipeline in force both states | ARS `Fr20_TheDenyVeto_StaysInForce` (both states) | — |
| No constant: three records, three Dataverse answers | ARS `Fr20_FlagOn_NoConstant_ThreeRecordsThreeDataverseAnswers` (Read / Read\|Write / Read\|Write\|AppendTo\|Delete → Read / Read\|Write / Read\|Write\|Create\|Delete) | — |
| Truncated reaches the module data response | ARS `Fr20_FlagOn_TruncatedBecomesCapped`; CPR `WorkforceStrategy_CarriesTheDataverseDecisionAndTheTruncation`; MOD `ExecuteScopedFetch_WhenTheCallersSetIsTruncated_SaysSo_AndOtherwiseTheWireIsUnchanged`, `ExecuteScopedFetch_WhenTheAccessibleSetIsEmptyButTruncated_SaysSo_WithoutQuerying` | MOD theory `truncated: false` (no `truncated` key on the wire) |
| Flag defaults false; never checked in on | `appsettings.template.json` `"Enabled": false`; CAN `Fr20ImpersonatedRootSetFlag_WhenEnabledInCheckedInConfiguration_RequiresAProvisionedCanary` (pre-existing tripwire, passes) | — |
| The DataverseConfirmsWrites marker | CPR `WorkforceStrategy_CarriesTheDataverseDecisionAndTheTruncation` (impersonated × capped) | its `impersonated: false` rows |
| PlatformOwnershipColumns / BU pin unchanged | `MembershipResolverServiceTests` unchanged and green; `MembershipResolverService.cs` not in the diff | — |
| `WorkforceProjectAccessLevel` has not crept back | `git grep WorkforceProjectAccessLevel -- src tests` = 0 hits | — |
| docs §6 privilege + Org-scope warning | `docs/guides/auth-deployment-setup.md` (§6 note), `SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` §7.7 | — |
| ADR-034 path B, concise + full | `docs/adr/ADR-034-user-record-membership.md` Amendment A2; concise draft `notes/036-adr034-concise-amendment.md` (main session applies) | — |
| MANUAL LIVE GATE (i)–(v); PR cites the canary | **Not run** (brief: no canary, no dev writes). Command in §7 | — |

### Fail-before evidence

The behaviour was neutered in place, the API surface kept (flag read forced false; `WithDataverseRightsAsync` returning
`composed`; delete step (2b) skipped; `scopeTruncated` forced false; strategy markers forced false/empty), the new tests
run, then the real files restored (`cmp` identical). Result: **54 failed / 44 passed** of the 98 cases in the new-test
filter (97 new + the pre-existing canary tripwire). The 43 new cases that pass before are, by design, the flag-off
must-not-fire twins and deny-direction cases a deny-everything base also satisfies (probe fault → 403, outside the set →
403, stamp first, missing SystemUserId → throw, which the task 020 invariant already did). Each deny-direction case is
paired with an allow case that fails before. After: **98 / 98 pass**.

## 5. NFR-02 round trips

| Path | Dataverse calls added |
|---|---|
| Flag on, principal composition, cache miss | 3 impersonated id-only reads (one per root type, `$select=<id>&$top=5000`), replacing the membership walk's ≥ 3 resolver pages. Cached 2 minutes per (user, type): 0 on a hit |
| Flag on, each confirmed write (to-do, upload, event, PATCH, message) | +1 RetrievePrincipalAccess as the caller (OBO; the token exchange is MSAL-cached) |
| Flag on, delete-own | +1 RPA on the row, after the existing reads |
| Flag on, reads (lists, `/me`, module fetch) | 0 |
| Flag off; CIAM; workforce contacts | 0 |

## 6. Verification

- **Build**: `dotnet build src/server/api/Sprk.Bff.Api` and the test projects — 0 warnings, 0 errors.
- **Filtered tests (Sprk.Bff.Api.Tests)** — `AccessibleRecordSetServiceTests | CallerPrincipalResolverTests |
  ExternalTodoScopeTests | ExternalRecordDocumentRoutesTests | ExternalDeleteOwnTests | ExternalMessageSendTests |
  ExternalModuleColumnAllowListTests | MembershipResolverServiceTests | ImpersonatedRootSetSource* | CallerRecordAccessProbe* |
  ImpersonationNegativeCanaryTests`: **726 passed, 0 failed**. Consumers of the changed surface (contract, intake,
  multi-root, registration, grantee, membership paging, entitlements, org membership, workforce email, module data,
  root events): **640 passed**; seam / data-mutation consumers (effective access, grant cache, secure-parent veto,
  standing grants, unified evaluator): **177 passed**. The full unfiltered suite was not run (brief).
- **ArchTests** (`RouteAuthorizationGuard* | TenantIsolation | ExternalAccessQueryIntegrityGuard |
  ExternalChildRowLiveParentGuard | CallerIdentityGuard | ADR008`): **176 passed**. The route guard first failed on the
  three to-do creates: their ledger seam `RightsForRoot` is now one named hop deeper. `RouteAuthorizationGuardTests.Ledger.cs`
  names the hop `ExternalProjectDataEndpoints.RightsForRootWriteAsync` (seam unchanged), and the to-do / event reasons cite
  the new lines.
- **Publish** (`dotnet publish -c Release -r linux-x64 --self-contained false`, zipped with the same deflate script):
  base `5b2375f6f` 38,250,205 bytes (re-zipped 38,249,877) → branch **38,254,025** bytes: **+3,820 bytes** (+4,148 like for
  like); uncompressed 130,852,084 → 130,867,988 (+15,904). Total ≈ 36.5 MiB, under 55 / 60 MB.
- **CVE**: `dotnet list src/server/api/Sprk.Bff.Api package --vulnerable --include-transitive` — no vulnerable packages;
  no package or csproj change.
- **Client lint**: `node scripts/quality/client-lint/client-lint.mjs check --package ui-components` — ok. `tsc --noEmit`
  on Spaarke.UI.Components shows 9 pre-existing errors (unbuilt sibling packages `@spaarke/sdap-client`, `@spaarke/auth`),
  none in `IDataverseClient.ts`.
- `git grep WorkforceProjectAccessLevel -- src tests`: 0.

## 7. The NFR-04 canary (manual gate — not run here)

`tests/integration/auth/UnifiedAccessControl/ImpersonationNegativeCanaryTests.cs`; environment contract in
`tests/integration/auth/README.md` (§ Provisioning the canary user, § Running it):

```bash
export SPAARKE_CANARY_DATAVERSE_URL="https://spaarkedev1.crm.dynamics.com"
export SPAARKE_CANARY_SYSTEMUSERID="<existing non-admin test user in a child BU — e.g. uac.child.user@demo.spaarke.com's systemuserid>"
export SPAARKE_CANARY_SEEDED_MATTER_IDS="<matters that user's role depth admits, derived app-only — never from the impersonated read>"
export SPAARKE_CANARY_REQUIRED=true
export AZURE_TOKEN_CREDENTIALS=AzureCliCredential
export SPAARKE_TESTS_ALLOW_OUTBOUND=1
az login   # an identity that maps to the BFF Dataverse application user (dev only — never sprk-prod-kv)

dotnet test tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj \
  --filter "FullyQualifiedName~ImpersonationNegativeCanaryTests"
```

🔴 **PASS = the impersonated set is STRICTLY SMALLER than the app-only set. Equality is a FAILURE** (impersonation is
inert). A NOT RUN is not a pass. The first run (2026-10-03, dev, `uac.child.user@demo.spaarke.com`) measured 59 vs 61 and
predates this change; the gate applies again before this PR merges and before the flag is enabled anywhere. Record the
two sizes in `notes/task-034-negative-canary.md`.

## 8. Open questions and escalations

- **(a) WorkforceHome-linked systemusers can't be probed.** The OBO probe cannot exchange a customer-directory (A9) token
  for a Dataverse token in Spaarke's tenant, so under the flag such a user's writes deny unless contact grants allow.
  Options: accept as a known limit until Model 1 users use a Spaarke-tenant token; or an app-only RPA keyed on the
  systemuserid (a departure from the probe's caller-token design — ADR-028 review needed). Owner decision.
- **(b) RPA CreateAccess semantics.** Decided D3 (AppendTo → Create). Flagged for review.
- **(c) Delete-own needs Dataverse Delete on the row** in addition to the stamp (D5). A creator whose role lacks Delete on
  `sprk_document`/`sprk_todo`/`sprk_event` can no longer delete their own row under the flag. Confirm.
- **(d) Non-root entity types throw under the flag.** `ComposeAsync` is called only by `WorkforcePrincipalStrategy` for the
  three root types, and `IsRecordAccessibleAsync` / `IsOperationPermittedAsync` have no production callers, so nothing
  reaches it. Stated, not changed.
- **(e) Service-request writes** (upload, message, delete) keep the requester rule and are not confirmed (D7). Round 59
  says "every write route"; these rest on the requester scope, not membership. Owner: confirm, or require a Dataverse
  answer (which would refuse requesters whose request is owned by another BU's team).
- **(f) C-1 ordering.** The impersonated read is `$select=<id>&$top=5000` with no `$orderby`, so which 5,000 records a
  capped set keeps is Dataverse's default order. The cap is flagged (`truncated`); the ordering is an owner decision
  (escalation trigger in the POML) — not chosen here.
- **(g) Write affordances under the flag (must be settled before enabling).** Dataverse-term records are listed at Read,
  so `/me` reports ViewOnly and the document list `canCreate = false`; the SPA (`DocumentLibrary.tsx`,
  `RecordDetailModal.tsx`) hides create/upload although the routes admit writes Dataverse confirms. Options: a batched RPA
  for affordances (the cost round 59 avoided); a distinct "Dataverse decides" level the client treats as "offer and let
  the route decide"; or the client always offers and handles 403. Pinned by DOC
  `Fr20_List_AsksDataverseNothing_AndReportsCanCreateFromTheComposedRights`.
- **(h) Pre-existing, fixed in scope:** flag-off membership `Capped` was silently dropped (D11).
- **Rollout:** default off; enabled on dev only after the canary passes strictly smaller and 142 and 143 are done
  (and (g) is settled). No date can be named yet. Flag-off is transitional and does **not** close the flag-off BU
  over-grant.
- **Enforcement follow-up (not built):** a source-scan ArchTest that every `MapPost/MapPatch/MapPut/MapDelete` on the
  external group either calls `WithDataverseRightsAsync` or is listed with a reason (§2.4) would mechanise the round 59
  rule (CLAUDE.md §16 ladder). Recommended as a follow-up issue.

## 9. Deviations

- `docs/guides/auth-deployment-setup.md` is a retired stub with no §6. The privilege and Org-scope note was added there as
  a short "§6" section (the brief names it) that points to `SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` §7.7 (the App Users) and
  §12.8 (the runbook), where the content lives.
- `ExternalAccessModule.cs` is listed as modified in the POML; no change was needed (D9).
- `TASK-INDEX.md` was not updated: this branch is cut from R3's tip, and the task closes on merge + deploy + the live gate.
- The `.claude/adr` concise amendment is a draft (`036-adr034-concise-amendment.md`) for the main session.
