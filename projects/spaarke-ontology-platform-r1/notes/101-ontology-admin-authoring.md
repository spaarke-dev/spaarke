# Task 101: rule authoring in the BFF (catalog, dry run, save, publish)

Lane `task/ontology-101`, base `387e2d70e` (docs/ontology-platform-design). FULL rigor; tagged auth/security (two adversarial
passes). No UI (task 102).

## 1. What was built

| Route (group `/api/v1/ontology/admin`, admin auth + rate limit inherited) | Service method |
|---|---|
| `GET /authoring/catalog` | `OntologyAdminAuthoringService.GetCatalogAsync` |
| `POST /authoring/dry-run` | `DryRunAsync` |
| `POST /authoring/policies` (201) | `SavePolicyAsync` |
| `POST /authoring/policies/{policyId:guid}/versions` | `PublishVersionAsync` |

Files:
- `Api/Ontology/OntologyAdminAuthoringEndpoints.cs` (new), plus one wiring line in `OntologyAdminEndpoints.cs`.
- `Services/Ontology/OntologyAdminAuthoring{Service,Models,SignalEngine}.cs` (new).
- `SignalsModule` registers them.
- `PredicateCompiler` gains its operator tables as `internal static` members. This is a behaviour-preserving refactor, so
  the catalog reads them instead of copying them.

Identity:
- Every Dataverse read and write runs as the caller (`IDataverseUserClient`, OBO). Dataverse enforces the Ontology
  Administrator role on each write, and the audit names the human.
- The capability probe (task 104) is never consulted.
- The evaluator runs as the evaluator, but only through the `IOntologyAdminAuthoringSignalEngine` seam:
  - the dry run passes a `SignalEvaluationPolicy` built in memory from the caller's own read plus the draft;
  - publish passes the id of a policy the caller's own read admitted.

## 2. Deviations (record for review)

1. **Policy domain uses an explicit filter, not `RuleDomains`.** ebilling-r1's `RuleDomains` (task 070, #1911) is not on the
   base.
   - One private helper, `OntologyAdminAuthoringService.OntologyDomain`, carries the comment
     `// Swap to RuleDomains (ebilling-r1 task 070, #1911) when it lands`.
   - The filter is `(sprk_domain eq null or sprk_domain eq 100000001)`. It is applied to every policy read (the dry run's
     policy, publish, and the save's code-taken check), and `Admits()` re-checks the returned row.
   - It runs before any write. A Billing or Docketing policy id gets the uniform 404 with zero writes (tested).
   - New policies are created with `sprk_domain = 100000001`.
   - `ValidateForSave` stays Ontology-only.
2. **Save of an existing rule = publish.** No draft-version row exists. Only `sprk_inforceto` may change on an existing
   version (D-14), so a "draft" row would need a second PATCH path. Publish creates the version.
3. **Saving a new rule creates the policy Off, with v1 as current.** It also requires the dry-run proof (v4: "Save needs a test
   run"). It is not evaluated, since an Off policy is not evaluated by `ReevaluatePolicyAsync`; it is evaluated when turned On.
4. **Some subjects are not offered: `sprk_budget`, `sprk_budgetrevision`, `sprk_memo`.** They are evaluator-global-readable but
   have no `sprk_subjecttype` option on `sprk_policy` (live option set: Matter, Communication, Project, Service Request, Work
   Assignment, To Do, Event, Invoice, Document). A policy about them cannot be saved.
5. **⚠ For task 102: the catalog's fields are only what the compiler lists.**
   - Date-only window columns (`DateOnlyColumns`), Threshold columns (`ThresholdColumnRanges`) and verified joins
     (`VerifiedJoins`).
   - The DateTime columns the flagship rules use in a relative window (`sprk_communication.sprk_receiveddate`,
     `sprk_budgetrevision.sprk_revisedon`) are NOT in any compiler list, and nor are arbitrary equality fields. So they are
     not offered, as the criterion "offers only what compiles, and only from the compiler's lists" requires.
   - The wizard cannot rebuild the flagship rule from catalog entries alone. Widening the catalog is a compiler-list change
     with its own review, not a 101 change.
6. **The dry-run proof** is a tenant-cache entry (TTL 1 h) keyed by caller, policy (or "new") and a length-prefixed SHA-256 of
   lane (fix round 1, K2), rule type, rule body and template.
   - Save and publish require it for the exact text: any edit needs a new test run.
   - The headline, proposed action and plan are not part of the key, because they do not change what the rule raises.
   - An unreadable cache fails closed (409).
7. **Publish order** follows the task 033 contract:
   1. create the version;
   2. stamp the old version's `sprk_inforceto`, conditional on its row version;
   3. move `sprk_currentversion`, conditional on the policy's row version;
   4. `ReevaluatePolicyAsync` once, only if the policy is On.

   Compensation:
   - A 412 at step 2 marks the new version never in force (`inforceto = inforcefrom`) and returns 409.
   - A failure at step 3 also undoes our stamp on the old version, but only if the stamp is still ours.
   - Fix round 1 (F2-2, F2-3): after step 1 everything runs on `CancellationToken.None`. A non-412 answer or a throw at step 2
     or 3 re-reads the row as the caller first: a stamp or pointer move that landed is a success (no compensation, step 4
     runs); a pointer that cannot be re-read is a 503 with nothing undone (logged). The new version is never stamped while
     the pointer names it.
   - A reevaluation failure after the pointer moved returns 200 with `reevaluation: "failed"`. The version stands, and the
     nightly run retries. The job's summary is logged, not returned.
8. **The POML's live seam tests are written but NOT run in this lane (fix round 1, F3-4).** The lane allows no live writes.
   `tests/integration/Sprk.Bff.Api.IntegrationTests/Ontology/OntologyAdminAuthoringLiveTests.cs` holds them, opt-in
   (skip-via-return, the `BusinessOwnedAppendToLiveTests` pattern), self-cleaning (`zz-101-test` names, policy code
   `ZZ-101-…`; Signals, Decision Records, versions and the policy deleted in `finally`; zero-count check):
   - `LiveMode_AnAdminPublish_SupersedesTheOneOpenSignal_AndReRaisesItAsEpisode2`: admin dry run + save (Off) of a rule
     that holds for one configured matter; the operator turns it On; the job's single-policy pass raises episode 1; the
     admin dry-runs and publishes v2; asserts `reevaluation: completed`, superseded 1, raised 1, and a `|2` Signal citing v2.
   - `LiveMode_AConsoleUser_DryRunIs403_AndSaveIsRefusedByDataverse_403`: the Console User's dry run is 403
     `ontology.admin.forbidden`. A Console User can no longer obtain a proof, so the test plants one in the test host's own
     tenant cache; the save then reaches the deep insert and Dataverse refuses it as that user (403
     `ontology.admin.refused`, zero rows).
   - One shared change: `WorkAssignmentCreateLiveTests.LiveUserClient` gains the version-conditional `PatchAsync`
     overload (If-Match `W/"n"`, as production), which publish needs.

   To run them (the main session; they WRITE to spaarkedev1, then delete what they wrote):
   ```
   az login   # an identity that may create policies and impersonate (System Administrator)
   export SPAARKE_LIVE_101_DATAVERSE_URL=https://spaarkedev1.crm.dynamics.com
   export SPAARKE_LIVE_101_ADMIN_ID=<systemuserid holding Spaarke Ontology Administrator>
   export SPAARKE_LIVE_101_CONSOLE_USER_ID=<systemuserid of a Spaarke Console User without it, e.g. Chelsea Friez>
   export SPAARKE_LIVE_101_MATTER_ID=<sprk_matterid of an ACTIVE matter the admin can read>
   export SPAARKE_LIVE_101_WRITER_ID=3121bf1b-9fbf-f111-aaaf-0022482913fc
   dotnet test tests/integration/Sprk.Bff.Api.IntegrationTests/ --filter "FullyQualifiedName~OntologyAdminAuthoringLiveTests" --logger "console;verbosity=detailed"
   ```
   Substitutions: the inbound token (the user's real oid), the OBO exchange (operator token + MSCRMCallerID), the app identity
   (operator `az` credential), and the Signal writer (operator ServiceClient impersonating the writer principal, the
   `SignalWriterSeamTests` method). Without the variables both tests return at once (verified: 2 pass, no network).
9. **Engine seam (§11).**
   - *Existing:* `SignalEvaluator` and `SignalReevaluationJob`.
   - *Extension:* they are sealed concrete singletons over the app identity.
   - *Cost of doing nothing:* the dry run's caller filtering and the publish ordering could only be tested live.

## 3. Known limits

- **K-101-1.** The dry run's open/suppressed/new is a preview of the nightly planner over caller-readable Signals, not the
  planner itself. Two cases are approximate: a Signal the admin cannot read, and quiet-window edge cases at the second.
- **K-101-2.** The dry run returns identity and display only (name, number, core). It returns no facts and no evidence: a row
  shows only what the caller can already read.
- **K-101-3.** If the save's pointer PATCH fails (an answer or a throw) after the deep insert, the policy is left Off with v1
  not current. It never evaluates (it is Off), and the nightly run reports it. Since fix round 1 (K6) the 503 body carries
  `policyId`, so the client can finish by publishing a version of that policy; since review pass 2 (F1-b) every pointer
  failure, a refusal or an unresolved caller included, is that 503.
- **K-101-4 (K1).** The dry-run proof binds the BODY (lane, rule type, body, template) and the caller, not the data: it
  proves this caller ran this exact rule, not that the data is unchanged since.
- **K-101-5.** A throw at publish step 1 (the version create itself, e.g. a timeout) is a 503 with no compensation: whether the
  version row exists is unknown, and it is never current. Steps 2 to 4 and every compensation run on `CancellationToken.None`
  (F2-2), re-read the pointer before deciding (F2-3), and never stamp the current version.

## 4. Tests

`tests/integration/auth/Ontology/OntologyAdminAuthoringEndpointTests.cs`, 35 cases at first, 55 after fix round 1 (section 9). They run through the real pipeline with
the real `PolicyVersionValidator`; Dataverse-as-caller and the engine seam are faked.
- With the route mapping removed, 28 fail. The 7 that pass split two ways:
  - the four 401 cases: the fallback policy answers 401 for an unmapped path too, so these are regression guards on the group's
    authentication;
  - the DI case, the source scan and the service-level catalog case, which do not compile without the change.
- Contract cases map to the criteria:
  - **catalog:** compiler lists only; every offered entry compiles; one past the grammar does not;
  - **dry run:** zero writes; open/suppressed/new; an unreadable matter or core is absent and not counted; lane from the
    caller's read; invalid gives 400; non-Ontology gives 404;
  - **save:** Off; domain; nested v1 plus pointer; invalid gives 400; other-lane plan gives 400; no proof gives 409;
    code taken gives 409; Console User gives 403;
  - **publish:** order and conditional writes; no proof or an edited body gives 409; a proof for another policy does not
    count; non-Ontology gives 404; unreadable gives 404; Console User gives 403; retired gives 409; invalid gives 400;
  - **authorization:** an unresolved caller gets 403 on every route, with only WhoAmI asked; no token gives 401 on every
    route.
- Beyond the contract, one line each:
  - `Publish_WhenAConcurrentPublishStampedTheOldVersion…` and `…PolicyChangedBeforeThePointerMove…` prove the compensation
    never leaves the current version stamped (EventId 50309).
  - `Publish_AnOffPolicy…` covers the job's refusal of an Off policy.
  - `Publish_WhenTheSupersedePassFails…` covers the 200-with-failed contract.
  - `Source_TheOnlyPatchOfAVersion_IsTheInForceToStamp` is the D-14 negative, read off the code.
  - `Production_WiresTheEngineSeam…` checks that the seam is not left as the fake in DI.
- The episode-2 re-raise itself is proven by the existing `SignalReevaluationJobTests` (`ReevaluatePolicyAsync` supersedes
  and raises `|2` citing v2), and the route calls it unchanged.

## 5. uac-r2 re-read

`origin/master` at `72b55bb6c94199b40a5cabdba7998bbce6f9c68e`, fetched 2026-10-16, compared with the base `387e2d70e`:

| File | Change since base |
|---|---|
| `Services/Access/SecureChildLineage.cs` | none |
| `config/secure-record-owner-role.json` | none |
| `Services/Dataverse/RecordOwnershipResolver.cs` | none |
| `Api/Filters/RecordRouteAccessAuthorizationFilter.cs` | none |
| `Infrastructure/ExternalAccess/CallerRecordAccessProbe.cs` | none |
| `Infrastructure/ExternalAccess/ExternalCallerContext.cs` | none |
| `.claude/adr/ADR-034-user-record-membership.md` | none |
| `Services/Dataverse/CoreAncestorResolver.cs` | `72b55bb6c` (#2029): `CoreAncestorIntent` log level only. 101 uses only `CoreRecordEntities`, unchanged |
| `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs` | `b1eaecb49` (#2021) retires one route file, so `ExpectedEndpointFileCount` is 125 on master. This branch sets 127 (base 126 + 1). At merge the count is (merged count) + 1 |

Open PRs that touch the same files:
- **#2035 (uac-r2 099a):** the ledger, a count conflict.
- **#2031 (docs→master sync):** `OntologyAdminEndpoints.cs` and the ledger.
- Lanes 100 and 103 add to the same group and ledger.

The main session resolves these.

## 6. Publish size (CLAUDE.md section 10)

Method: Release `dotnet publish` (linux-x64), `-m:1`, from short paths. The base is a `git archive` of
`origin/docs/ontology-platform-design` (`58e810405`; its `src/` is identical to this branch's base `387e2d70e`) in
`C:\ps101\base`; the branch is built in `C:\wts-101`. Both are zipped with `Compress-Archive`.

| | Files | Raw bytes | Zip bytes |
|---|---|---|---|
| base | 192 | 130,142,340 | 38,967,723 (37.16 MB) |
| branch | 192 | 130,295,652 | 39,021,339 (37.21 MB) |
| delta | 0 | +153,312 | **+53,616 (+0.05 MB)** |

No package added; no new CVE surface. The total is under the 55 MB review line.

## 7. ADR-010 ceiling (159 → 160)

`IOntologyAdminAuthoringSignalEngine` is a new 1:1 interface. The ceiling in `tests/Spaarke.ArchTests/ADR010_DITests.cs` is
raised with a seam justification, following the procedure that test documents (same category as `IAppOnlyCommunicationQuery`).
This conflicts at merge with any other lane that moves the ceiling: the result is (merged count) + 1.

## 8. Found in passing (not fixed; D-106)

- The full `Spaarke.ArchTests` run on this base fails
  `InboundBodyDtoMappingGuardTests` (`OntologyAdminCapabilities.CanAuthorPolicies`). This is the known 104 issue, already
  fixed on the docs branch at `58e810405`, and is not touched here.

## 9. Fix round 1 (review of PR #2039)

| Finding | Fix | Evidence |
|---|---|---|
| F1-1 any signed-in user can run the dry run | `CheckAuthoringPrivilegeAsync`: WhoAmI, then `RetrieveUserPrivilegeByPrivilegeName` as the caller, fresh per request; not held 403 `ontology.admin.forbidden`, unresolved the single 403, fault 503. Catalog not gated (static lists + the caller's own read). Never the probe or its cache (104 rule A, D-22) | `DryRun_AsAConsoleUser_…` (x2), `DryRun_WhenThePrivilegeCannotBeRead_…` (x3), `DryRun_AsksThePrivilegeFresh_…`; seeding: gate removed, both Console User cases fail (200 with rows) |
| F2-2 timeout/disconnect skips compensation | `StampAndMoveAsync`: steps 2-4 and compensation on `CancellationToken.None`, wrapped in try/catch | `Publish_WhenThePointerMoveTimesOut_…`, `Publish_WhenTheRequestIsAbandoned…` |
| F2-3 compensation can end the current version | re-read (`PointerOfAsync`, `CarriesStampAsync`) before compensating; `RetireUnpublishedAsync` refuses to stamp unless the pointer names another version | `Publish_WhenThePointerMoveAnswers5xxButLanded_…`, `Publish_WhenTheOldVersionsStampAnswers5xx…` (x2) |
| F3-5 untested security claims | tests only | `Publish_AnotherCallersDryRun_IsNotAProof_409`, `DryRun_WhenTheProofCacheThrows_…` |
| K2 lane not in the proof | `ProofKey` canonical v2 includes the lane | `Save_ADryRunInTheOtherLane_IsNotAProof_409`, `ProofKey_BindsTheLane` |
| K5 no-subject-type tables fail only at save | dry run of a NEW rule runs `TrySubjectType` (400) | `DryRun_ANewRuleAboutATableWithNoPolicySubjectType_…` (x3) |
| K6 orphaned policy id | the save's failed pointer write returns `policyId` in the problem body | `Save_WhenThePointerCannotBeSet_…` (x2) |
| K1 proof binds body not data | stated (class remarks, K-101-4, PR body) | — |
| F3-4 live seam tests | written, opt-in, not run (section 2, item 8) | — |

## 10. Review pass 2 (PR #2039: adversarial pass 2 and the uac-r2 code pass)

The adversarial pass found the dry-run gate sound. Fixed, each with a test that failed before the fix (proved by restoring the
old service and endpoint: 5 failed, then passed):

| Finding | Fix | Evidence |
|---|---|---|
| F1-a a concurrent publish wipes the end stamp of a version that has really ended | `ReadVersionsAsync` selects `sprk_inforceto` (`VersionRow.Ended`); a publish whose current version already carries an end stamp is refused 409 `policy_changed` before step 1, with no write (`OntologyAdminAuthoringService.cs:905`; narrowed to a FRESH stamp in round 3, §10.3). `RestorePreviousAsync` (`:1165`) undoes the stamp only while `PointerOfAsync`, as the caller, still names the previous version; another version: the stamp stands; unreadable: left and logged as an Error (the unknown-pointer pattern) | `Publish_WhenAConcurrentPublishStampedTheCurrentVersion_ButHasNotMovedThePointer_Is409_AndTheEndStampStands` (the A/B interleaving), `Publish_WhenTheOldStampLanded_ButThePointerNoLongerNamesTheOldVersion_TheCompensationLeavesItsEndStamp`, `Publish_WhenThePointerMoveIs412_AndThePointerCannotBeReRead_TheOldStampIsLeft_NotUndoneOnAGuess` |
| F1-b (uac-r2 F1) a refused or unresolved pointer PATCH after the save's creates read "Nothing was written" or a policy-less 403 | every pointer-write failure after the creates is `Failure.Unavailable` with `PolicyId` (`:803`, `:812`), the "saved Off, publish a version to finish" 503. The endpoint's Refused problem no longer carries a policy id | `Save_WhenThePointerWriteIsRefusedOrTheCallerUnresolved_IsTheSavedOff503_WithThePolicyId` (AccessDenied, OboExchangeFailed) |
| F3-a two claimed behaviours untested | tests only (mutation-checked: compensating in the default branch, or dropping the Retire guard, fails them) | `Publish_WhenThePointerMoveThrows_AndThePointerCannotBeReRead_Is503_AndNothingIsUndone`, `Publish_WhenTheOldStampIs412_RetireUnpublished_NeverStampsAVersionThePointerNamesOrMayName` (New, Unknown) |
| F3-b WhoAmI 401/403 not pinned | tests only; the mapping is unchanged | `AWhoAmIDataverseRefuses_401Or403_IsTheSameSingle403_OnEveryRoute` (x4), `tests/unit/domain/Dataverse/DataverseUserClientErrorMappingTests.cs` (401, 403, 404, 400, 412, 429, 5xx) |
| uac-r2 F3 privilege-read mapping coverage | tests only (mutation-checked) | `DryRun_EveryOtherPrivilegeAnswer_FailsClosed_AndNothingIsEvaluated`: 200 without `RolePrivileges` 503, a throw 503, 404 503, 400 503, a row naming another privilege 403 forbidden; the evaluator is never called |
| live-test cleanup | `CleanUpAsync` PATCHes `sprk_enabled=false` (If-Match `*`, never an upsert) on every `ZZ-101` policy before any delete, so a failed delete leaves no enabled test rule for the nightly job | `OntologyAdminAuthoringLiveTests.cs` `DisableAsync`; not run |

### 10.1 Role change pending owner approval (repo only, not applied live)

Confirmed live by the main session on spaarkedev1: `prvAppendsprk_Policy` and `prvAppendsprk_PolicyVersion` are held only by
Service Writer, System Administrator and System Customizer. Setting a lookup needs Append on the record being written (AppendTo
on the target; on `systemuser` that comes from Basic User), so a non-sysadmin Ontology Administrator gets 403 on save and publish.

`src/dataverse/solutions/SpaarkeMaster/Roles/Spaarke Ontology Administrator.xml` adds, at **Deep** (the depth the role already
holds for Write, Create, Read and Delete on both tables):

| Privilege | Why |
|---|---|
| `prvAppendsprk_Policy` | the save's and the publish's pointer PATCH sets `sprk_currentversion` on the policy (`sprk_CurrentVersion@odata.bind`) |
| `prvAppendsprk_PolicyVersion` | the version create sets `sprk_policy` (`sprk_Policy@odata.bind`, or the deep insert's link) and `sprk_authoredby` (`sprk_AuthoredBy@odata.bind`) on the version |

The main session applies it live after the owner approves. No repo doc lists this role's `prvAppendTosprk_Policy*` rows, so
no doc table was changed (`notes/security-roles.md` records applied changes; it gains a row when this is applied).

### 10.2 Known limits (not fixed)

- **K-101-5** (above) stands: an orphan version from a throw at step 1 is harmless. Only `ReadVersionsAsync` reads versions
  outside the pointer, and only to number the next one.
- **K-101-6 (K6, save side).** The save's deep insert runs on the request token. A cancel after Dataverse commits leaves an Off
  policy and no `policyId` is returned. Harmless: the policy is Off and never evaluates, and the next save creates a new one.
- **K2.** Two consecutive Dataverse failures (an unknown pointer plus a failed re-read, or a failed restore PATCH) can leave the
  current version stamped. The double failure now heals on the next publish after `ConcurrentPublishWindow` (2 minutes, §10.3):
  that publish restamps the version and moves the pointer. Until then the nightly job refuses the policy (EventId 50309). It is
  logged as an Error and the request returns 503 (or 409). Within the window a retry still gets 409 `policy_changed`.
- **K3.** `CancellationToken.None` is bounded per call by HttpClient's 100 s default but not in total: about 7 calls, roughly 12
  minutes worst case. `ReevaluateAsync(None)` has no overall deadline and cannot be amplified.
- **K4.** The gate accepts any depth of `prvCreatesprk_PolicyVersion`. That is correct for "may author": Dataverse still
  enforces the save and publish writes at the role's depth.
- **uac-r2 K2.** A proof recorded before the deploy gets 409 `dry_run_required`, because the proof key moved to v2 (the lane).
  It fails closed and lasts at most the proof's 1 h lifetime.
- **uac-r2 K3.** A step-3 PATCH can time out on the client but commit after the pointer re-read saw "Other". The current version
  then stays stamped, and EventId 50309 reports it nightly.
- **uac-r2 K4.** Stamps are compared at one-second resolution (`CarriesStampAsync` `:1111`, `RestorePreviousAsync` `:1186`).
  This predates review pass 2.
- **Live tests:** written, opt-in; the main session runs them. Not run here.

### 10.3 Round 3 (PR #2039: one F2 finding)

| Finding | Fix | Evidence |
|---|---|---|
| F2 the pre-write 409 added in round 2 fired on ANY end stamp, so a K2 double failure (pointer move failed, restore failed, pointer still names the stamped version) refused every retry for good; before round 2 a retry healed it | the 409 now fires only when the stamp is FRESH: `IsFreshStamp` (`OntologyAdminAuthoringService.cs:1567`) against `ConcurrentPublishWindow = 2 minutes` (`:130`), read from the injected `TimeProvider`, the clock that writes the stamp. A concurrent publish holds its stamp for seconds; an older stamp is the leftover of a failed publish, so the publish proceeds, restamps and moves the pointer. A stamp that cannot be parsed, or one in the future, counts as fresh (refused: fails closed). `VersionRow` gains `EndedAt`. `RestorePreviousAsync` is unchanged: it still undoes a stamp only while the pointer, re-read as the caller, names the previous version | `Publish_WhenTheCurrentVersionCarriesAStaleEndStamp_FromAFailedPublish_Heals_AndIs200`, `Publish_WhenTheCurrentVersionCarriesAFreshEndStamp_Is409_AndMakesNoWrite`, `Publish_WhenTheCurrentVersionsEndStampCannotBeParsed_Is409_AndMakesNoWrite`; the round 2 interleaving test now seeds a 5-second-old stamp |

Seeding: window set to `TimeSpan.Zero` fails the fresh-stamp test and the round 2 interleaving test; the old rule (any end stamp
refuses) fails the stale-stamp test; treating an unparseable stamp as stale fails the unparseable test. All three restored.

### 10.4 Merge of the docs branch

`origin/docs/ontology-platform-design` (task 100, #2037) merged into this branch. Conflicts resolved keeping both lanes:
`OntologyAdminEndpoints.cs` maps the Rules (100) and authoring (101) routes; the route ledger keeps both GovernedFiles and one
Aggregator naming both bound files; `ExpectedEndpointFileCount` 127 → **128**. `SignalsModule.cs` auto-merged with both
registrations. The ADR-010 ceiling stays 160 (100 added no 1:1 interface).
