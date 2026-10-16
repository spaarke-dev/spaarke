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
   rule type, rule body and template.
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
   - A reevaluation failure after the pointer moved returns 200 with `reevaluation: "failed"`. The version stands, and the
     nightly run retries. The job's summary is logged, not returned.
8. **Engine seam (§11).**
   - *Existing:* `SignalEvaluator` and `SignalReevaluationJob`.
   - *Extension:* they are sealed concrete singletons over the app identity.
   - *Cost of doing nothing:* the dry run's caller filtering and the publish ordering could only be tested live.

## 3. Known limits

- **K-101-1.** The dry run's open/suppressed/new is a preview of the nightly planner over caller-readable Signals, not the
  planner itself. Two cases are approximate: a Signal the admin cannot read, and quiet-window edge cases at the second.
- **K-101-2.** The dry run returns identity and display only (name, number, core). It returns no facts and no evidence: a row
  shows only what the caller can already read.
- **K-101-3.** If the save's pointer PATCH fails after the deep insert, the policy is left Off with v1 not current, and the
  route returns 503. It never evaluates (it is Off), and the nightly run reports it.

## 4. Tests

`tests/integration/auth/Ontology/OntologyAdminAuthoringEndpointTests.cs`, 35 cases. They run through the real pipeline with
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
