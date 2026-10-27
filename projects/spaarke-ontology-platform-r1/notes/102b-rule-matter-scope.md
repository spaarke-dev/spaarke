# 102b: rule scope in R1 (a matter scope, evaluated within the matter, an exact dry run)

Task: [`tasks/102b-rule-matter-scope.poml`](../tasks/102b-rule-matter-scope.poml). Owner decision D-165 (2026-10-17); closes
DI-62 #1647 and DI-112 #2069. Builds on 101 ([`101-ontology-admin-authoring.md`](101-ontology-admin-authoring.md)), 102
([`102-ontology-admin-rule-wizard.md`](102-ontology-admin-rule-wizard.md)) and 102a ([`102a-authoring-contract.md`](102a-authoring-contract.md)).

Rigor: FULL (tenant-isolation, security). Model: Opus, effort high.

## Design (written before code, Step 0)

### 1. The shared scope check (validate, dry run, save, publish)

One private method on `OntologyAdminAuthoringService`, called by all four routes after the rule's own validation (the 102a
pattern: one path, so they cannot disagree). The four request records gain two optional members: `matterId` (Guid?) and
`tenant` (string?). `tenant` exists on the wire only so that a request carrying one can be refused; it is never read into
anything.

| Input | Answer |
|---|---|
| `tenant` non-blank | 400 field error on `tenant`. The tenant is never taken from the request body (every environment is single-tenant, `CommunicationRuleGate.cs` ~274). |
| `matterId` absent or null | No scope: all matters (the rule behaves exactly as today). |
| `matterId` set, rule subject is not `sprk_matter` | 400 field error on `matterId`, `reasonCode = ontology.admin.scope_subject_mismatch`. The subject is the draft body's `subject`, which is what the evaluator evaluates (a version's subject can differ from the policy's stored subject type). |
| `matterId` set, the caller's own read does not return it (absent, unreadable, or `Guid.Empty`) | 400 field error on `matterId` with ONE message, "The matter was not found." Absent and unreadable are the same answer (no oracle). |
| The caller's read faults / the caller is unresolved | 503 / the single 403, as every other caller read. |

The matter is read AS THE CALLER through the dry run's existing readable-ids read (`ReadableIdsAsync`, an `In` filter on
`sprk_matterid` against `sprk_matters`), so no new query shape. A 400 field error (rather than a 404) is used so the wizard can
place the refusal on the scope field; it is the same answer for "does not exist" and "cannot read", which is what the
constraint asks ("uniform not-found, no oracle").

The dry-run proof key binds the scope as well as the body: a test run for matter M is not a proof for publishing for N or for
all matters (a scope change changes the counts the admin confirmed). The key text is unchanged when there is no scope, so an
unscoped proof is exactly as before.

### 2. Persisting the scope

- **Save**: the policy create carries `sprk_Matter@odata.bind = /sprk_matters(M)` when scoped; `sprk_tenant` is never written
  (it stays blank).
- **Publish**: the policy is read as the caller with `_sprk_matter_value`. When the requested scope differs from the stored one,
  the scope change rides in the SAME conditional PATCH that moves `sprk_currentversion` (If-Match on the row version read), so
  the pointer and the scope move together or not at all, and the existing compensation is unchanged. Clearing the scope writes
  `"sprk_Matter@odata.bind": null` (the `ExternalGrantLifecycle` precedent). An unchanged scope sends exactly the old body.

### 3. The evaluator (`SignalReevaluationJob`)

`PolicyRow.IsScoped` is replaced by `TenantScoped` and `MatterId`.

- A non-blank tenant: still `ScopedNotEvaluated` with the Warning (unevaluable; the run is not successful), as today.
- A matter scope M on a rule whose subject is not `sprk_matter` (only possible when written outside the BFF): `ScopedNotEvaluated`,
  Warning, nothing evaluated, closed or raised.
- A matter scope M on a matter rule: evaluated with the EXISTING subject-scoped evaluation, `SubjectScope("sprk_matter", M)`:
  the evaluator's compiled query carries `sprk_matterid eq M` (`PredicateCompiler`, the event-trigger path of task 032).
  Fail-closed guard: a hit outside the scope (which the query makes impossible) fails the policy for this pass before anything
  is read, closed or written.
- The planner (raise, re-raise, ConditionCleared close, reconcile, re-own), the suppression reconcile and the Do-lane closure
  record are handed ONLY the in-scope Signals, so nothing outside M is raised, closed as ConditionCleared, reconciled or re-owned.
- An event trigger on another subject than M is `NotApplicable` for a policy scoped to M.

### 4. Closure on scope change (publish)

The publish re-evaluation is the job's single-policy pass. For a matter-scoped policy it reads ALL the policy's Signals with the
existing policy-prefix read (the query an unscoped policy uses) and runs the EXISTING supersede over all of them: a Signal citing
a version that is not current closes `Superseded`. A publish always creates a new version, so every open Signal of the policy
cites the old version: the ones outside the new scope close `Superseded` and are never re-raised (the planner sees only the
in-scope Signals); the ones inside it are superseded and re-raised as a new episode exactly as for any publish (D-13). So:

- no new closure reason and no new Signal state (escalation trigger 2 does not fire): `Superseded` is the reason a publish
  already gives, and "the rule was replaced by a version that no longer covers this subject" is what it means;
- no new query shape (escalation trigger 1 does not fire): the evaluation, the planner and every ConditionCleared close are
  confined by the existing `SubjectScope` path; the only policy-wide step is the existing version-based supersede;
- the nightly pass runs the same code, so a publish whose re-evaluation failed is finished by the next nightly run (it closes
  only Signals citing a superseded version, which a stable scoped policy has none of).

The dry run therefore stays exact: `wouldSupersede` (every caller-readable open Signal of the policy) equals what the publish
supersedes, and `wouldReraise` (in-scope hits with an open Signal) equals what it re-raises.

The stored-evidence redaction (task 145, D-123: it only ever removes detail) still covers every Signal of the policy, in and out
of the scope, as for an unscoped policy; otherwise a scoped policy's out-of-scope Signals would never be redacted again.

### 5. Dry run

The dry run evaluates with the same subject scope (`IOntologyAdminAuthoringSignalEngine.EvaluateAsync(policy, matterScope)`
→ `SignalEvaluator.EvaluateAsync(policy, subjectId: M)`), with the same fail-closed hit guard. The wizard's "counts are worked out
without the rule's scope" sentence is removed.

### 6. Wizard

Scope and noise: for a rule about matters, a matter picker (the shared `LookupField`) or "All matters". For any other subject the
step says a matter limit is offered only for a rule about matters (and, if a scope is still set from an earlier choice, shows it
with the refusal so it can be removed). The client sends only the matter id. New version opens with the policy's current scope
and can change it. `ontology.admin.scope_subject_mismatch` is placed on the scope field.

**Changed during implementation: the picker comes from the host.** The RuleWizard sources may not name a Dataverse table or use
Xrm (D-149, enforced by `ruleWizardSources.test.ts`), so the wizard cannot know that "matters" means `sprk_matter`. The wizard
takes an optional `MatterPicker { subject, search }` prop (through `RulesArea`). The Console's `OntologyAdminWorkspaceWidget`
supplies `{ subject: 'sprk_matter', search: q => searchLookupTarget('sprk_matter', q) }`: the EXISTING shared lookup search
(`components/RecordHeader/lookupSearch.ts`, Xrm.WebApi as the caller, `$top=10`, never throws). No new search code (§11 reuse;
an intermediate `matterSearch.ts` was dropped for it). Without a picker the step shows the scope, can remove a limit, and says
a matter cannot be searched here.

## Evidence

### Acceptance criteria

| # | Criterion | Evidence |
|---|---|---|
| 1 | Save and publish persist `sprk_matter`; tenant stays blank; publish raises only for M | `Scope_SaveWithAMatter_BindsTheMatterAsTheCaller_AndNeverWritesATenant`, `Scope_PublishWithAMatter_MovesThePointerAndTheScopeInOneConditionalWrite_ThenReevaluatesOnce`, `Publish_FromAllMattersToM_ClosesTheOpenSignalsOutsideM_Superseded_AndReraisesOnlyM` |
| 2 | Nightly: evaluated within M; Signals written/closed only for M; N gets none; run succeeds | `Run_AMatterScopedRule_IsEvaluatedWithinItsMatter_AndAQualifyingSubjectOnAnotherMatterGetsNoSignal` (Success, raised 1, no key for the other matter), `Run_AMatterScopedRule_ClosesConditionClearedOnlyInsideItsMatter_AndClosesAnOpenSignalOutsideIt_Superseded` (fix round 1); control `Run_TheSameDataWithoutAScope_RaisesForBothMatters` |
| 3 | Dry-run counts equal publish counts, in one test | `DryRunWithinAMatter_CountsExactlyWhatThePublishSupersedesAndReraises`: the real `OntologyAdminAuthoringService` and the production engine over the same world; dry run (wouldSupersede 3, wouldReraise 1, wouldClose 0) equals the publish pass (superseded 3, re-raised 1, closed 0) |
| 4a | Unreadable and nonexistent matters get the same answer | `Scope_AMatterThatDoesNotExist_AndOneTheCallerCannotRead_GetTheSameAnswer` (Theory over validate, dry run, dry run of a new rule, save, publish; also `Guid.Empty`): same status, same body, matter read as the caller |
| 4b | Mismatch on all four routes | `Scope_AMatterScopeOnARuleThatIsNotAboutMatters_IsScopeSubjectMismatch` (same Theory; reasonCode `ontology.admin.scope_subject_mismatch`, no matter read) |
| 4c | Tenant refused | `Scope_ATenantValue_IsRefused_WithOrWithoutAMatter`; must-not-fire `Scope_ABlankTenant_IsNoTenant_AndIsNotRefused` |
| 4d | Unscoped unchanged | `Scope_None_DryRunSaveAndPublishBehaveAsBefore_NoMatterIsReadOrBound`, `Scope_PublishKeepingTheScope_SendsExactlyThePointerMove`, the whole pre-existing suite unchanged and green |
| 5 | Scope change closes open Signals outside the new scope, none inside | `Publish_FromAllMattersToM_...` (all to M), `Publish_FromMToN_ClosesM_Superseded_AndRaisesN` (M to N), `Publish_KeepingTheScope_...`; closure reason is the existing `Superseded` (design §4) |
| 6 | Wizard: one matter or All matters with a shared picker; no matter list; picker only for a matter subject; New version shows and can change the scope | `RuleWizard.test.tsx` describe "RuleWizard - matter scope" (11 tests: offered only for the picker's subject, must-not-fire for another subject, no picker without a host search, sends only the id, all matters sends nothing, a test is stale after a scope change, New version shows / keeps / clears / changes the scope, mismatch placed on the scope field and removable, dry-run refusal routed to Scope and noise); the sentence test is replaced by "no longer says…"; `ruleDraft.test.ts`, `authoringService.test.ts`, `OntologyAdminWorkspaceWidget.test.tsx` (picker subject `sprk_matter`, search is `searchLookupTarget`) |
| 7 | Seeding proof | Below |
| 8 | Tests cover the contract; full ArchTests; size delta | Below |

### Seeding proof (the cross-matter test fails when the scope is removed)

1. `SignalReevaluationJob`: `_evaluator.EvaluateAsync(spec, confinedTo?.Id, ct)` changed to `(spec, null, ct)`.
   `Run_AMatterScopedRule_IsEvaluatedWithinItsMatter_...` FAILS: Success false, `policyFailures = 1` (the fail-closed hit guard
   refuses the out-of-scope hit).
2. The hit guard also disabled: the same test FAILS with the other matter's Signal key (`...dc989|1`) among the created keys.
3. Both reverted; `confinedTo?.Id` confirmed back in place; the test passes.

### Fail before, pass after

- The job from `1d89b2ede` restored (`SignalReevaluationJobTests`, 233 cases): 8 fail, all of them new matter-scope tests (the
  AC3 dry-run/publish test among them); the unscoped control `Run_TheSameDataWithoutAScope_RaisesForBothMatters` passes, as it
  should, and so does every pre-existing test.
- The service from `1d89b2ede` restored (with only the engine call adapted so it compiles): 23 of the 30 endpoint scope cases plus
  the AC3 test fail. The 5 blank-tenant cases, the unscoped test and the keep-scope test pass (regression guards).
- Everything restored from the backup afterwards.

### Tests run

| Suite | Result |
|---|---|
| BFF, filtered (`Ontology\|Signal\|DecisionRecord\|PolicyVersion\|Predicate`) | 2440 passed, 3 skipped, 0 failed |
| `Spaarke.ArchTests`, full | 1076 passed, 0 failed |
| Jest, Spaarke.UI.Components (OntologyAdmin + LookupField) | 9 suites, 222 passed |
| Jest, Spaarke.AI.Widgets (OntologyAdminWorkspaceWidget, ontologyAdminGate) | 2 suites, 18 passed |

Why the tests that are not a criterion's own exist: the unscoped control proves the cross-matter test's data would raise without
the scope; the blank-tenant cases are the must-not-fire for the tenant refusal; the `IgnoreScope` engine (an evaluation that
escapes the scope) proves the dry run's fail-closed guard; the trigger test proves an event on another matter does not run a
policy scoped to M; the widget test proves the Console actually supplies the picker.

### Publish size

`dotnet publish -c Release` of `Sprk.Bff.Api`, zipped with `Compress-Archive`, fresh worktrees at short paths:

| Build | Zip | Files |
|---|---|---|
| `origin/master` `1df4ee12d` | 39,130,331 B (37.32 MB) | 192 |
| `origin/docs/ontology-platform-design` `71d713e59` (the PR base) | 39,115,294 B (37.30 MB) | 192 |
| This branch | 39,119,192 B (37.31 MB) | 192 |

Delta against the PR base: +3,898 B zipped (+14,660 B raw), no new files. Against master: -11,139 B (master carries unrelated
newer work). Well under +5 MB; total far below 55 MB. No package changes, so no new CVE.

### Known limits (not defects on a real path)

- A stored non-blank `sprk_tenant` (written outside the BFF) is not cleared by a publish; the policy stays `ScopedNotEvaluated`.
- The other limits are listed under "Fix round 1" below (K2 to K5, uac K3, the `RuleSubjectOf` duplicate).
- No live UI check (no deploy in this lane); dark-mode and browser runs not done. `tsc` for Spaarke.AI.Widgets cannot resolve
  `@spaarke/ui-components` locally (pre-existing; affects unchanged files too). Spaarke.UI.Components `tsc` shows 9 pre-existing
  module-resolution errors, none in changed files.

### Live reads (read-only, own `az` token, spaarkedev1)

- `sprk_policy` relationship metadata: the ManyToOne lookup to `sprk_matter`, navigation property `sprk_Matter`.
- `sprk_policies?$select=sprk_code,_sprk_matter_value,sprk_tenant`: all six policies have no matter and a blank tenant, so
  nothing in dev changes behaviour on deploy.

## Fix round 1 (PR #2075 review)

Model: Sonnet-tier implementer brief, executed literally. F1 (the cascade RemoveLink on `sprk_policy_matter` widens the rule when its
matter is deleted or archived) **awaits the owner's schema decision and is not in this round**; nothing under `src/dataverse/**` changed.

### F4. A publish without a scope intent no longer clears the scope

- `OntologyDryRunRequest` and `OntologyPublishRequest` gain `bool? AllMatters`. In the shared `CheckScopeAsync` (dry run and publish
  pass the policy's stored `_sprk_matter_value`; validate and save pass none): a stored scope with neither `matterId` nor
  `allMatters: true` is a 400 on `matterId`, reasonCode `ontology.admin.scope_intent_required` ("This rule is limited to a matter. Send
  that matter to keep the limit, or all matters to remove it."); `matterId` with `allMatters: true` is a 400 with the same code ("Send a
  matter or all matters, not both."), whatever is stored; a stored null with neither keeps today's unscoped behaviour.
- The dry-run proof's cached payload is now `DryRunProof(At, StoredMatter)` (payload version 2): the policy's stored scope when the dry
  run ran. Publish answers the existing 409 `ontology.admin.policy_changed` when the stored scope differs (two admins), before any write.
  The live test that plants a proof was updated to the new payload.
- The dry run of an existing policy returns `storedMatterId`, and `storedMatterName` when the caller's read of the matter returns it
  (the `OntologyAdminRulesService` query shape; a failed or refused read gives null, never a failed dry run).
- Wizard: the test and the publish send `scopeIntentRequest(draft)`: `{ matterId }` or `{ allMatters: true }` (validate and save still
  send only `matterId`). The publish confirmation adds `scopeChangeSentence`, e.g. "This changes the rule from Matter Nine to all
  matters.", comparing the chosen scope with the stored scope the test read (falling back to the page's scope). The new code is placed on
  the scope field.

Tests (`OntologyAdminAuthoringScopeEndpointTests.cs`): `Scope_AStaleClientWithNoIntent_OnAScopedPolicy_IsRefused_AndTheScopeIsKept`,
`Scope_AllMattersFalse_IsNoIntent_OnAScopedPolicy`, `Scope_AMatterAndAllMattersTogether_IsRefused_WithTheIntentCode` (dry run, dry run
of a new rule, publish), `Scope_TwoAdmins_AStoredScopeChangedSinceTheDryRun_IsPolicyChanged_AndNothingIsWritten`,
`Scope_TwoAdmins_AScopeAddedSinceAnUnscopedDryRun_IsPolicyChanged`, `Scope_DryRun_ReturnsTheStoredScope_WithItsNameWhenTheCallerCanReadIt`;
`Scope_PublishFromOneMatterToAllMatters_WithAllMatters_ClearsTheScopeWithANullBind` (allMatters clears); the unscoped rule with no intent
is `Scope_None_DryRunSaveAndPublishBehaveAsBefore_NoMatterIsReadOrBound` (unchanged, green). Jest: `ruleDraft.test.ts` (intent, sentence),
`authoringService.test.ts` (code on `matterId`), `RuleWizard.test.tsx` (allMatters on test and publish, the sentence for M to all, M to
N, the stored scope the test read, none when kept or unscoped).

### uac-r2 F2. Open Signals outside the scope are closed, not only warned about

`SignalClosureService.SupersedeInPassAsync` takes an optional outside-scope predicate: on a matter-scoped policy's own pass every OPEN
Signal outside the scope closes `Superseded` through the same `CloseAsync` the version supersede uses, whatever version it cites. The
Warning is gone; the job counts them as `outOfScopeSuperseded` (part of `supersededClosed`) and logs at Information with their ids. A close
that fails is a refusal and fails the run, as for any supersede. Tests: the flipped
`Run_AMatterScopedRule_ClosesConditionClearedOnlyInsideItsMatter_AndClosesAnOpenSignalOutsideIt_Superseded`; route (a)
`Run_AScopeChangedInDataverseFromMToN_WithoutAPublish_ClosesMsOpenSignal_Superseded_AndRaisesN`; route (b)
`Publish_Narrowing_ClosesANoVersionOpenSignalOutsideTheNewScope_Superseded`; `Publish_FromMToN_...` now also asserts the Information line.

### uac-r2 F3. The job's fail-closed hit guard has a must-fire test

Harness switch `World.IgnoreSubjectIdCondition` (the fake Dataverse ignores the scoped root condition, so the evaluation returns every
matter). `Run_AScopedEvaluationThatReturnsAnotherSubject_FailsThePolicy_AndWritesNothing`: Success false, `policyFailures` 1, no create
or write, the "returned another subject" Error. Must-not-fire control: `Run_AMatterScopedRule_IsEvaluatedWithinItsMatter_...`.

### Fail before, pass after

- F2: the job and closure service from `5152cabea` restored: 4 fail (the three new or flipped closure tests and `Publish_FromMToN`).
- F3: the guard disabled (`if (false && ...)`): the new test fails; restored, it passes.
- F4 server: the service and models from `5152cabea` restored: the 8 new endpoint cases fail (the renamed allMatters-clears test passes,
  as a regression guard).
- F4 wizard: `authoringService.ts`, `ruleDraft.ts`, `RuleWizard.tsx` from `5152cabea` restored: 8 jest tests fail.

### Tests run (fix round 1)

| Suite | Result |
|---|---|
| BFF, filtered (`Ontology\|Signal\|Reevaluation\|PolicyVersion`) | 2406 passed, 3 skipped, 0 failed |
| `Spaarke.ArchTests`, full | 1076 passed, 0 failed |
| Jest, Spaarke.UI.Components (OntologyAdmin) | 8 suites, 205 passed |
| Jest, Spaarke.AI.Widgets (OntologyAdminWorkspaceWidget, ontologyAdminGate) | 2 suites, 18 passed |

### Known limits after fix round 1

- K2: the D-123 redaction still writes to out-of-scope Signals of the policy. It only removes detail, and the D-123 sweep covers them
  anyway. Accepted explicitly as a deviation from the POML wording "never write".
- K3: a tenant stored outside the BFF is not read by the dry run; publish then reports `reevaluation=failed` and the run fails visibly.
  #2069's tenant-counts clause is met by refusing a tenant in the request.
- K4: an admin who cannot read the current matter cannot keep it (fails closed); they can only change or clear the scope.
- K5: a failed matter search shows as no results (pre-existing `searchLookupTarget` behaviour).
- uac K3: a reader who cannot read the matter sees its id but not its name.
- `RuleSubjectOf` in the authoring service duplicates the job's private `RuleSubjectEntity` (both read the body's `subject`).
- Validate carries no `allMatters` (the brief's closed set is dry run and publish); it checks a scope as save does.

## Fix round 2 (PR #2075 review)

Model: Sonnet-tier implementer brief, executed literally. The cascade finding (F1) still awaits the owner; nothing under `src/dataverse/**` changed.

### F3 (a). The `s.IsOpen` check on the out-of-scope supersede is pinned

`SignalClosureService.SupersedeInPassAsync` closes out-of-scope Signals only when `s.IsOpen`. Test
`Run_AMatterScopedRule_NeverReopensOrOverwrites_ADecidedSignalOutsideItsMatter`: a Signal on the other matter, Resolved/Dismissed and citing the
current version, keeps its status and resolution, no conditional update targets it, `outOfScopeSuperseded` is 0. Fail-before: with
`s.IsOpen &&` removed it fails (the resolution becomes Superseded); restored, it passes.

### F3 (b). The redaction of out-of-scope Signals is pinned

Test `Run_AMatterScopedRule_StillRedactsTheStoredEvidenceOfASignalOutsideItsMatter`. The brief named the T145 sweep test as the model, but
that test stops at the validator gate and is redacted by the post-loop sweep, which would pass even with the policy's own pass broken. So
the scoped variant uses a VALID, evaluated scoped policy (the policy's own pass runs the redaction; the sweep skips a policy already attempted):
a Dismissed Signal on `OtherMatterId` with stored evidence quoting a witness re-filed under secure X is redacted once, and nothing else on the
row changes. Fail-before: with only `existing` passed at the redaction call (`SignalReevaluationJob.cs`) it fails (`evidenceRedacted` 0).

### uac-r2 minor F1. The stored-matter name read never fails the dry run

`ReadMatterNameAsync` now catches `(Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)`, logs a Warning
with the exception type only (no matter name) and returns null. Test `Scope_DryRun_AFaultReadingTheStoredMattersName_NeverFailsTheDryRun`
(world switch `MatterNameReadThrows`): a `TaskCanceledException` (an HttpClient timeout) with the request token live gives 200, the counts,
`storedMatterId` and a null `storedMatterName`. Fail-before: the old method returns 503 `ontology.admin.unavailable`.

### K2. Validate carries the scope intent

- `OntologyValidateRequest` gains `bool? AllMatters`. For an existing policy validate passes the stored `policy.MatterId` to `CheckScopeAsync`, so it
  refuses the no-intent and matter-plus-allMatters cases with the same code (`ontology.admin.scope_intent_required`), field (`matterId`) and
  messages as the dry run and publish. Validate answers a refusal as 200 `valid:false` (as for every validate refusal), not a 400.
- Save only ever creates a new policy (`OntologyNewPolicyRequest` has no policy id, so there is no stored scope), so it is left as is.
- The doc comments that claimed validate/save/publish check the scope alike are corrected (`OntologyValidateRequest`, the validate and publish
  comments, `CheckScopeAsync`).
- Wizard: `validateRequest` now uses `scopeIntentRequest`, so validate sends `{ allMatters: true }` when "All matters" is chosen and `{ matterId }`
  otherwise (needed: without it every validate of a scoped policy's new version would now be refused). `ValidateRequest` extends
  `RuleScopeIntentRequest`; save still sends only `matterId`.
- Tests: `Scope_Validate_OnAScopedPolicy_RefusesNoIntent_LikeTheDryRunAndPublish` (with must-not-fire: matter, allMatters, unscoped policy),
  `Scope_Validate_AMatterAndAllMattersTogether_IsRefused_WithTheIntentCode` (stored and unstored); jest `ruleDraft.test.ts` (the validate shape) and
  `RuleWizard.test.tsx` (validate says `allMatters` for All matters, and not for a matter). Fail-before: the service and models from the previous
  head: the 3 server cases fail; `ruleDraft.ts` from the previous head: the 2 jest tests fail.

### K4. The out-of-scope closure log is capped

The Information line now carries the exact count and at most the first `MaxLoggedOutOfScopeIds` (50) ids, with " and N more". Test
`Run_AMatterScopeChange_ClosingManySignals_LogsTheExactCount_AndOnlyTheFirstFiftyIds` (60 Signals: `outOfScopeSuperseded` 60, 50 ids in the line,
"and 10 more"). Fail-before: with the cap removed all 60 ids are logged and it fails. The earlier one-id log assertions are unchanged and green.

### K3. Deploy order

Deploy the BFF before the Console bundle. An old BFF ignores `matterId` (and the new `allMatters`) and never widens a rule; the reverse order
would let the new wizard send an intent an old BFF does not read.

### Tests run (fix round 2)

| Suite | Result |
|---|---|
| BFF, filtered (`Ontology\|Signal\|Reevaluation\|PolicyVersion`) | 2413 passed, 3 skipped, 0 failed |
| `Spaarke.ArchTests`, full | 1076 passed, 0 failed |
| Jest, Spaarke.UI.Components (OntologyAdmin) | 8 suites, 206 passed |
| Jest, Spaarke.AI.Widgets (OntologyAdminWorkspaceWidget, ontologyAdminGate) | 2 suites, 18 passed |

### Re-check of fix round 2 (2026-10-17)

- **Scoped re-check (sonnet, `d2b2ee72b..af6ce7171`):** all five items closed, no F-class findings. The checker mutated `s.IsOpen &&` and the redaction call site (`SignalReevaluationJob.cs:419`; the finding said ~412) and both tests failed. It confirmed save only creates new policies: no policy id on `OntologyNewPolicyRequest`, `Guid.NewGuid()`, a deep-insert POST, and no update route.
- **uac-r2:** approved, 374 filtered tests passed, all five guard mutations caught. Validate is not an existence oracle: the stored matter comes from `ReadPolicyAsync` as the caller, and the request matterId still goes through `ReadableIdsAsync`.
- **Known limits, accepted (no fix):**
  - K2: the cancellation half of the catch at `OntologyAdminAuthoringService.cs:1689` has no test. A bare catch would pass all tests; the code mirrors :498 and a swallowed cancel would only send 200 to a client that has gone. Optional: a must-not-fire test with a cancelled request token.
  - K1: the 50 logged ids are in read order; the count is exact. The log test covers 60 Signals, not the 50/51 boundary (the code is correct at both, by reading).

## Fix round 3: matter delete removes its rule

### Owner decision (2026-10-17)

If a rule (`sprk_policy`) is limited to one matter and that matter is deleted, the rule is deleted. A rule with no matter (all
matters) is never affected: it has no `sprk_matter` value, so no matter's delete reaches it.

### Schema change

`sprk_policy_matter` (`src/dataverse/solutions/SpaarkeMaster/Other/Relationships/sprk_Matter.xml`, the relationship at :555):
CascadeDelete `RemoveLink` -> `Cascade`. Nothing else under `src/dataverse/**` changed. The repo already has 9 relationships with
Delete=Cascade and Archive=RemoveLink (e.g. `sprk_analysis.xml`), so the platform exports and imports that pair.

**Parental rule.** A table can be the child in only ONE relationship with Delete=Cascade. Every relationship in
`src/dataverse/solutions/**` with `sprk_Policy` as the referencing entity:

| Relationship | Parent | CascadeDelete |
|---|---|---|
| `business_unit_sprk_policy` | BusinessUnit | Restrict |
| `owner_sprk_policy` | Owner | NoCascade |
| `sprk_policy_matter` | sprk_Matter | **Cascade** (this change) |
| `sprk_policy_currentversion` | sprk_PolicyVersion | RemoveLink |
| `lk_sprk_policy_createdby`, `lk_sprk_policy_modifiedby`, `user_sprk_policy` | SystemUser | NoCascade |
| `team_sprk_policy` | Team | NoCascade |

No other relationship has Cascade, so `sprk_policy_matter` is the only one.

### Cascade chain (what a matter delete removes)

| Hop | Relationship | CascadeDelete | Effect |
|---|---|---|---|
| matter -> policy | `sprk_policy_matter` | Cascade | a rule limited to the matter is **deleted** |
| policy -> policy version | `sprk_policyversion_policy` (`sprk_Policy.xml:11`) | RemoveLink | versions keep their rows; `sprk_policy` nulled |
| policy -> Signal | `sprk_signal_policy` (`sprk_Policy.xml:42`) | RemoveLink | Signals keep their rows; `sprk_policy` nulled |
| policy version -> Decision Record | `sprk_decisionrecord_policyversion` (`sprk_PolicyVersion.xml`) | RemoveLink | not reached (the version is not deleted) |
| policy version -> Signal | `sprk_signal_policyversion` (`sprk_PolicyVersion.xml`) | RemoveLink | not reached |

Only the policy row is deleted. **Decision Records are never deleted.** No hop reaches them, and they keep their version link
because the versions survive. No Restrict is on the path.

### The deleted-policy sweep

A deleted policy's open Signals were stranded. The policy loop reads Signals by the dedupe-key prefix of an EXISTING enabled
policy. The Off/Retired close reads by the `sprk_policy` lookup, which RemoveLink nulled. So those Signals stayed open in the
Worklist forever (outer join, null work type). `notes/033-closure-progress.md` item 2 recorded this as "no path creates the state".
This change creates that state, and so does an Ontology Administrator's hard delete. Item 2 now points here.

`SignalClosureService.CloseSignalsOfDeletedPoliciesAsync` (:188) runs in the nightly job after the Off/Retired close
(`SignalReevaluationJob.cs:138`, wrapper :458):

- It reads every `sprk_policy` code, in any state, paged (`AllPolicyCodesAsync`, :227). More than `MaxPages` pages throws,
  because an incomplete list could make a live policy look deleted.
- It reads Open and Acknowledged Signals with `sprk_policy` null (`ReadOpenSignalsWithoutPolicyAsync`, :258), at most `MaxPages`
  pages of `PageSize` (200 x 500) per run. Any remainder is logged as a Warning and the next run continues (a closed Signal
  drops out of the read).
- It closes, as `PolicyRetired`, each one whose dedupe key parses and whose code matches no existing policy. Codes are compared
  ignoring case, so a doubtful match counts as "exists". The close uses the same `CloseAsync` -> `SignalWriter.CloseAsync` as
  `ClosePolicyRetiredNowAsync`: status, resolution, resolvedon, lastevaluated, and no Decision Record.
- It leaves alone: a Signal whose code still exists (whatever the policy's state or scope), an unparseable key (Warning,
  counted), and any row read back with a `sprk_policy` despite the condition.
- Fail closed: both reads finish before any close. A fault in either closes nothing, counts a policy failure (the run is failed,
  visibly) and logs EventId `EvaluatorPolicyFailed`. The run continues, and the redaction sweep still runs.
- Logging: the exact count and at most `MaxLoggedOutOfScopeIds` (50) ids, the K4 pattern. Result JSON: `deletedPolicyRetired`,
  which is part of `policyRetiredClosed`.
- §11: no new service. This is one method on the existing closure service and one call in the existing job.

**Can a Signal legitimately have a null `sprk_policy`?** No code path creates one:

- The only create of `sprk_signal` is `SignalWriter` (`SignalWriter.cs:583`), which always sets
  `entity["sprk_policy"] = new EntityReference("sprk_policy", request.PolicyId)` (:1519).
- The only `SignalWriteRequest` builder is `SignalReevaluationJob.BuildRequest` (:1055). It sets `PolicyId: policy.Id` from a
  policy row it has read, so the id is never empty.
- No script, seed or client creates Signals: a grep of `scripts/`, `src/client`, `src/solutions` and `src/dataverse` found none.
  Task 049 removes Signal Write from the Console User and Ontology Administrator roles; only the BFF creates Signals.
- The column is optional (`sprk_Signal/Entity.xml:995`, RequiredLevel `none`), so a System Administrator or an import could
  still write one.

So the sweep requires BOTH conditions (lookup null AND the code names no existing policy), and the code condition decides.

### Archive

`CascadeArchive` stays `RemoveLink`. Microsoft Learn documents `CascadeConfiguration.Archive` as "For internal use only", and
nobody knows whether `Restrict` is accepted there.

**Limit:** long-term retention is not enabled on these tables. If it ever is, archiving a matter would clear the rule's
`sprk_matter` link, and the rule would **widen to all matters**. Revisit this before retention is enabled on `sprk_matter` or
`sprk_policy`.

### Privileges for the matter delete (RESOLVED live, 2026-10-18: NOT refused)

~~The `DeleteRequest` remarks say a cascade delete checks the caller's Delete privilege on each child, so a user without Delete on
`sprk_policy` cannot delete a matter that a rule names.~~ **Wrong on dev.** The live check showed the cascade runs without that
privilege: a Core User's matter delete also deletes the rules limited to that matter. The owner accepted this (D-167). See
"Live on spaarkedev1" below.

### Live on spaarkedev1 (2026-10-18, main session; owner-approved)

- **Metadata.** `sprk_policy_matter` CascadeDelete changed RemoveLink -> Cascade by `PUT RelationshipDefinitions` (full body from a
  GET, `MSCRM.MergeLabels`), then `PublishXml` for `sprk_matter` and `sprk_policy` only. Read back: Delete=Cascade; Archive
  RemoveLink; Assign, Merge, Reparent, Share, Unshare, RollupView NoCascade (unchanged).
- **Check A, non-admin (observed platform fact; contradicts the DeleteRequest remark).** Call path: admin token plus
  `MSCRMCallerID` = `# UAC Child BU Test User` (d6f8f439, BU1; Spaarke Basic User + Core User, which hold NO privilege on
  `sprk_policy`). `DELETE sprk_matters(M1)` returned **204**. The matter AND the disabled rule naming it were deleted; the audit
  records both deletes under that user (03:36:47Z). So Dataverse ran the cascaded child delete without the caller's
  `prvDeletesprk_Policy`. **Anyone who can delete a matter (Spaarke Core User, Deep) also deletes the rules limited to that
  matter**, and nothing else: no rule on another matter, no all-matters rule. Owner accepted this (D-167, option 1, 2026-10-18).
- **Check B, admin.** `DELETE sprk_matters(M2)` returned 204. Only rule P2 was deleted. Its version V2 and Signal S2 survive with
  `_sprk_policy_value` null; S2 keeps its version link. A Decision Record references the version, so it is untouched.
- **Cleanup.** Every `zz-102b` row deleted (0 matters, policies, versions, Signals); BU1 matter count back to 59 (uac-r2 canary).
- **Expected afterwards.** A Signal like S2 (policy null, code matching no policy) is closed by the nightly sweep as
  PolicyRetired and its stored evidence redacted (fix rounds 3-4). That closure is the design, not a bug. It needs the 102b BFF
  code, which reaches dev with the next docs->master sync and R3 deploy.
- **Orphaned versions (known limit).** After a cascade, the rule's `sprk_policyversion` rows survive with no policy link, holding
  the rule text; nothing cleans them up. A version has no matter column (`VersionPayload`), so it exposes a matter only through
  text an administrator typed into the template, headline or action. Console User reads versions at Deep. Recorded on #2084.
- Scripts: session scratchpad `live/cascade_meta.py`, `live/cascade_check.py`.

### `SignalSuppressionService` retrieves of `sprk_policy` (checked)

- `:892` reads the policy id from the Signal's `sprk_policy`, and returns null first when the lookup is empty (:870). After
  RemoveLink the lookup is null, so the retrieve is not reached. Only a race throws: the policy deleted between the Signal read
  and this retrieve. The method is documented to throw on a fault, and the route answers it as a fault, not a guess.
- `:1138` gets `policyId` from that same path or from the caller's own policy, so the same holds.

No change.

### Tests (fix round 3)

New file `tests/unit/domain/Signals/SignalReevaluationJobDeletedPolicyTests.cs`. It runs the real job and closure service over
the existing in-memory seams. The fake now answers the sweep's `sprk_policy` null query.

| Test | Covers |
|---|---|
| `Run_OpenAndAcknowledgedSignalsOfADeletedPolicy_AreClosedPolicyRetired_AndAResolvedOneIsUntouched` | (a) must-fire; no Decision Record |
| `Run_ASignalWithNoPolicyLookup_WhosePolicyStillExists_IsNeverClosedByTheDeletedPolicySweep` (enabled, off, retired, scoped) | (b) must-not-fire, with an orphan control in the same world |
| `Run_WhenTheSweepCannotRead_ItClosesNothing_TheRunFailsVisibly_AndContinues` (signals, policies) | (c) fail closed |
| `Run_ASignalWithNoPolicyLookup_AndAnUnparseableKey_IsLeftOpen_AndLogged` | (d) |
| `Run_ManySignalsOfADeletedPolicy_LogTheExactCount_AndOnlyTheFirstFiftyIds` | K4 cap |

- **Seeding proof (b):** with the `existingCodes.Contains(code)` check removed, all four (b) cases fail. So do four existing
  tests that seed policy-less Signals of live policies. With the check restored, all pass.
- **Fail before:** with the pre-change job and closure service, all 9 new cases fail.
- **Fixture:** `Run_AnotherPoliciesKeyUnderTheSamePrefix_PolicyCodeContainingAPipe_IsNotUnparseable` seeded a policy-less Signal
  for policy `POL|ODD` without that policy existing. That is now a deleted policy's Signal, so the test now adds `POL|ODD` (Off).
  Its intent and assertions are unchanged.

| Suite | Result |
|---|---|
| BFF, filtered (`Ontology\|Signal\|Reevaluation\|PolicyVersion\|Closure`) | 2456 passed, 3 skipped, 0 failed |
| `Spaarke.ArchTests`, full | 1076 passed, 0 failed |

No test pins `sprk_policy_matter` or its cascade; a grep of `tests/` found none. The two relationship scans
(`OntologyWriterAppendToGuardTests`, `SignalPairTypeSchemaAgreementTests`) read only the lookup's shape, and both pass.

### Known limits after fix round 3

- An unparseable key on a policy-less Signal is left open with a Warning, and it does not fail the run. The job never read these
  Signals before, so failing on them would turn every run red over data the job cannot fix.
- *(Fixed in fix round 4, item 3.)* If a new policy reuses a deleted policy's code, the old Signals are left alone because the code exists. The new policy's prefix
  read then treats them as its own episodes, which is how a recreated policy already behaved.
- *(Fixed in fix round 4, item 1.)* The redaction sweep (task 145) covers Off/Retired policies by code. A deleted policy's code is in neither list, so its Signals'
  stored evidence is not re-checked. Reported; not fixed here because it is out of scope.
- The per-run bound could in theory starve an orphan: that needs more than 100,000 policy-less Signals (live codes or
  unparseable keys) to sort ahead of it by id.

## Fix round 4 (2026-10-17)

Two reviews of `854c0b0b6..2f210d545` ran: an adversarial pass and uac-r2. This round fixes their closed set. Fail-before evidence
for each item is a mutation of the fix (applied, tested, restored) or the pre-round file, as noted.

### Item 1 (F, D-123): a deleted rule's Signals are redacted again

**Before.** The job gave the task 145 stored-evidence sweep only the enabled codes and the Off/Retired codes. A deleted rule's code
is on neither list. The round-3 sweep closed Open/Acknowledged Signals but never redacted, and never revisited Resolved ones. So a
deleted rule's Signal whose witness later moved into a Secure record kept the witness's detail in its stored evidence for good.

**After.**

- `SignalClosureService.ExistingPolicyCodesAsync` reads every policy code, in any state, with its `createdon`, paged; it throws past
  `MaxPages` (the round-3 rule). The job reads it ONCE per run (`CloseSignalsOfDeletedPoliciesAsync` in the job) and passes it to
  both deleted-policy steps, so the same list is never read twice.
- `SignalClosureService.DeletedPolicyCodesWithSignalsAsync` reads `sprk_dedupekey` and `createdon` of every Signal with an EMPTY
  `sprk_policy`, in ANY status (no status condition), paged and bounded by `MaxPages`. It keeps each parseable code that is an
  orphan by the item-3 rule (`ExistingPolicyCodes.IsOrphan`).
- `SignalReevaluationJob.RedactSignalsOfDeletedPoliciesAsync` runs after the Off/Retired redaction. It passes those codes to
  `SweepStoredEvidenceAsync(codes, "was deleted", counts, ct)`, which reads by prefix across every status. A code this run already
  swept (in force, or Off/Retired) is skipped by the existing `EvidenceRedactionAttempted` set, so no code is swept twice.
- Fail closed: if the policy list could not be read, the run is already failed and logged, and nothing extra is redacted. A fault
  reading the codes redacts nothing extra, counts one policy failure (the run fails visibly), logs `EvaluatorPolicyFailed`, and the
  run continues.
- Bound: past `MaxPages` pages the codes read so far are swept, a Warning is logged and the run is failed. Unlike the close, Resolved
  Signals never drop out of this read, so reaching the bound is a standing condition that an operator must see. It needs more than
  100,000 Signals with an empty lookup.

| Test (`SignalReevaluationJobDeletedPolicyTests.cs`) | Covers |
|---|---|
| `Run_R4_AResolvedSignalOfADeletedRule_WhoseWitnessMovedIntoASecureRecord_HasItsStoredEvidenceRedacted` | must-fire (shape of `Run_T145F1_…`) |
| `DeletedPolicyCodes_R4_ACodeThatStillExists_IsNotADeletedCode_WhateverTheSignalsStatus` | must-not-fire: the list itself |
| `Run_R4_ACodeThatStillExists_IsSweptOnce_AndNeverAsDeleted` | must-not-fire: one prefix read; the fault names "is not in force", never "was deleted" |
| `Run_R4_WhenTheDeletedCodesCannotBeRead_NothingExtraIsRedacted_TheRunFailsVisibly_AndTheCloseStillHappens` | fail closed |

- **Seeding proof:** with the `RedactSignalsOfDeletedPoliciesAsync` call dropped, 4 tests fail: the must-fire test, the fail-closed
  test, `Run_R4_ACodeThatStillExists_IsSweptOnce…` (its control) and the `MoreRemain` test (its redaction-bound assertion). Restored:
  all pass.
- The round-3 fail-closed test (`Run_WhenTheSweepCannotRead…("signals")`) now faults only the close's read (it has a status
  condition). The new read has its own fail-closed test above. Its intent and assertions are unchanged.

### Item 2 (F3): the schema change is guarded

`tests/Spaarke.ArchTests/PolicyMatterCascadeGuardTests.cs` scans `src/dataverse/solutions/SpaarkeMaster/Other/Relationships/*.xml`.
It asserts that `sprk_policy_matter` has CascadeDelete=Cascade, and that `sprk_Policy` is the referencing side of EXACTLY one
relationship with CascadeDelete=Cascade (compared ignoring case).

- Controls on in-memory XML. Must-not-fire: the real shape, a Cascade where `sprk_Policy` is the PARENT
  (`sprk_policyversion_policy`), and another table's Cascade. Must-fire: the matter relationship as RemoveLink, Restrict or
  NoCascade; the matter relationship missing; and a second Cascade parent spelled `sprk_policy`.
- **Seeding proof:** with the real `sprk_policy_matter` set to RemoveLink, the real-tree test fails. Restored: 7/7 pass. Nothing
  under `src/dataverse/**` is committed.
- Placement: the brief said "next to `SignalPairTypeSchemaAgreementTests` under `tests/Spaarke.ArchTests`", but that test lives in
  `tests/integration/data-mutation/Signals/`. The guard is in `tests/Spaarke.ArchTests` beside `OntologyWriterAppendToGuardTests`
  (the other relationship scan), so it runs with the ArchTests (Stop hook and CI).

### Item 3 (F1, low): a reused code no longer keeps a deleted rule's open Signals open

**Before.** A new rule saves Off (`OntologyAdminAuthoringService.cs:970`). Delete a rule and recreate its code before the nightly
run, and the orphans match an existing code, so the sweep skipped them.

**After.** `ExistingPolicyCodes.IsOrphan(code, signalCreatedOn)` is the one rule for both the close and item 1's code list. A
Signal with an empty `sprk_policy` is an orphan when no policy holds its code, or when the policy now holding that code was created
AFTER the Signal. It is never an orphan when the Signal was created at or after that policy, or when either date is unknown. The
hand-edited case is the reason: Console User has Write on `sprk_signal`. When two rows hold a code that matches ignoring case, the
earliest date is kept, and an unknown date wins. Both keep more Signals as "still exists", the direction that never closes a live
rule's Signal. Both reads now select `createdon`.

| Test | Covers |
|---|---|
| `Run_R4_AReusedCode_TheOldRulesSignalIsClosed_AndASignalCreatedAfterTheNewRuleIsNeverTouched` | must-fire (old Signal closed) and must-not-fire (newer Signal, empty lookup, untouched) |
| `DeletedPolicyCodes_R4_AReusedCode_IsListedOnlyForASignalOlderThanThePolicyHoldingIt` (-10 d listed, -0.5 d not) | the same rule on item 1's list (redaction) |

- **Seeding proof (must-fire):** with the date rule removed (IsOrphan = "code missing" only, the round-3 behaviour), 2 tests fail:
  the run test and the theory's listed case.
- **Seeding proof (must-not-fire):** with any dated code treated as an orphan, 3 tests fail: the run test (hand-edited Signal
  closed), the theory's not-listed case, and `DeletedPolicyCodes_R4_ACodeThatStillExists…`.
- Redaction note: a reused code still exists, so its prefix is also swept by the in-force or Off/Retired sweep, and those run first.
  Listing it again is harmless: `EvidenceRedactionAttempted` skips it.

### Item 4 (F, D-123, uac-r2 E): no rule limit on a Secure or Restricted matter

**Canonical source.** `ExternalParticipationService.FlagsFrom(isSecure, accessPermission, stateCode)` with the columns
`ExternalParticipationService.RootFlagColumns` (`sprk_issecure,sprk_accesspermission,statecode`). It is the one column-to-flag
mapping every access consumer uses (`RootRecordFlags`: `IsSecure`, `IsRestricted` = `sprk_accesspermission` 100000002,
`IsUnreadable` = an EMPTY `sprk_issecure`). Neither the columns nor the rule are new.

**Change.** `CheckScopeAsync`, shared by validate, dry run, save and publish, now reads the matter AS THE CALLER with
`sprk_matters?$select=sprk_matterid,sprk_issecure,sprk_accesspermission,statecode&$filter=sprk_matterid eq {id}&$top=1`
(`ReadMatterAccessAsync`). This replaces the id-only readable read, so it is still one read.

- No row (missing, or the caller cannot read it) gives `matterId` = "The matter was not found.", unchanged.
- Secure, Restricted, or `IsUnreadable` gives reason `ontology.admin.scope_matter_secured` on `matterId`, with "A rule cannot be
  limited to a Secure or Restricted matter, or to one whose access level could not be read."
- An unresolved caller is the single 403. Another non-success, or an exception, is 503 `ontology.admin.unavailable`. The scope is
  never allowed on a fault.
- Limited and Standard are allowed. Inactive is not refused; that behaviour is unchanged.

**Message decision (existence oracle).** "Missing" and "unreadable" keep ONE message, as before. "Secured" gets its own message and
reason code. That opens no oracle: the refusal is reached only after the caller's own read returned the row. `sprk_issecure` is
readable by every user (field profile "Spaarke BFF-Managed Field Readers", on every business-unit default team), and
`sprk_accesspermission` is not field-secured. So the caller learns nothing their read did not already show. A masked
`sprk_issecure` is named in the message as "could not be read", and is not claimed as Secure.

**Inheritance.** The check reads the matter's OWN stored columns. Secure inheritance stamps `sprk_issecure` on a record filed under
a secure root (`SecureRootInheritance`), and the access-permission floor (owner round 87) refuses a value below the inherited one,
so the stored columns carry what is inherited. The ancestry fold (`EffectiveRootFlags.ReadAncestryAsync`) is not applied, because
a matter is a root.

| Test (`OntologyAdminAuthoringScopeEndpointTests.cs`) | Covers |
|---|---|
| `Scope_R4_ASecureOrRestrictedMatter_IsRefused_ScopeMatterSecured_OnEveryRoute` (5 routes x secure / restricted / both / flag not returned) | must-fire; nothing evaluated or written |
| `Scope_R4_AnOrdinaryOrLimitedMatter_IsAllowed` (5 routes) | must-not-fire |
| `Scope_R4_WhenTheMattersAccessCannotBeRead_TheScopeIsNeverAllowed` (500 on 4 routes, throws on 2) | fail closed: 503, nothing evaluated or written |

- **Fail before** (the pre-round service): 26 fail (20 refusal cases, 6 fault cases); the must-not-fire cases pass.
- **Seeding proof (secure refusal):** with `flags.IsSecure` removed from the condition, the secure cases of the refusal theory fail.
  Restored: all 165 authoring endpoint tests pass.
- **Wizard.** The matter picker searches every matter the caller can read, secure ones included, so the refusal can arise. The BFF
  puts the message on `matterId`, which the Scope and noise step already shows. `authoringService.ts` now also maps
  `REASON_SCOPE_MATTER_SECURED` to `matterId`, so the code alone still shows a message there. Jest case:
  `puts a scope_matter_secured refusal on matterId, the scope field, with or without a message (fix round 4)`. It fails without the
  mapping (1 failed / 20 passed) and passes with it.

**Known limit: a stored scope that later becomes Secure or Restricted (out of scope this round).** The check runs on dry run,
validate, save and publish. A matter made Secure or Restricted after a rule was limited to it keeps the link, so a non-member with
Deep Read on `sprk_policy` still sees it.

*Proposed handling:*

- (a) The nightly job already reads each policy's `sprk_matter`. With the app identity it reads that matter's `RootFlagColumns`. When
  the matter is Secure or Restricted (or unreadable), it does not evaluate the policy: a partition-style skip, logged, with the run
  failed visibly.
- (b) The rules list shows that rule as "limit needs review", and an Ontology Administrator deletes it or removes the limit.
- (c) uac-r2's Make Secure / Restricted flow warns when a rule names the matter.
- (a) is the minimum. The link itself stays visible until (b) is done. Closing the read fully needs the rule row in the matter's
  secure family, or a narrower Console User read on `sprk_policy`. That is a uac-r2 decision.

### Item 5: cheap K items

- **(a)** The round-3 closure log line now ends with `policy code(s) {codes}{ and N more}`, capped at `MaxLoggedOutOfScopeIds` (50)
  like the ids (`DeletedPolicySweep.ClosedCodes`). Test: the round-3 must-fire test asserts `policy code(s) POL-DELETED.`. With the
  codes removed from the line, that test fails.
- **(b)** The unparseable-key Warning lists at most 50 Signal ids, plus "and N more" (`DeletedPolicySweep.UnparseableIds`). Tests:
  round-3 test (d) asserts the id; `Run_R4_ManyUnparseableKeys_LogTheExactCount_AndOnlyTheFirstFiftyIds` (55 keys) asserts the cap.
  With the cap removed, the cap test fails.
- **(c)** The job test fake (`World.RetrieveMultipleAsync`) now answers a query with `PageInfo.Count` set one page at a time,
  ordered by id, with `MoreRecords` and a paging cookie (`Paged`). It does this for the `sprk_policy` read and the two reads of
  Signals with an empty lookup. `PageSizeCap` shrinks a page so a test reaches page 2 or the bound; `PagedReads` records each page.
  - `Run_R4_ThePolicyListIsReadOverTwoPages_AndAPolicyOnTheSecondPageStillExists`: reads pages 1 and 2. With the read stopped
    after page 1, it fails (the policy on page 2 looked deleted and its Signal was closed).
  - `Run_R4_MoreOrphansThanOneRunReads_ClosesTheBound_WarnsMoreRemain_AndLeavesTheRestForTheNextRun`: 201 orphans, one per page.
    It closes 200, leaves 1 and warns `MoreRemain`. It also asserts the redaction bound's Warning and the failed run.

### Known limits after fix round 4

- **K1 (superseded 2026-10-18).** ~~A cross-BU cascade is refused as a whole.~~ The live check showed the cascade does not check
  the caller's privilege on `sprk_policy`, so a depth refusal is not expected either (not separately tested).
- **K2 (resolved 2026-10-18).** Not refused: anyone who can delete the matter deletes its rules; audited under that user; owner
  accepted (D-167).
- **K3 (void).** There is no refusal, so no refusal UX.
- **Archive.** Set `CascadeArchive` on `sprk_policy_matter` (today RemoveLink, which would widen the rule to all matters) before
  long-term retention is enabled on `sprk_matter` or `sprk_policy`.
- A stored scope that later becomes Secure or Restricted is not re-checked (item 4 above, with its proposed handling).
- The deleted-policy redaction read is bounded at 100,000 Signals with an empty lookup per run. Past that the run fails every night
  until an operator acts (item 1).

### Suites (fix round 4)

| Suite (fix round 4) | Result |
|---|---|
| BFF, filtered `Ontology\|Signal\|Reevaluation\|PolicyVersion\|Closure` | 2497 passed, 3 skipped, 0 failed |
| `Spaarke.ArchTests`, full | 1083 passed, 0 failed |
| Jest, `Spaarke.UI.Components` `src/components/OntologyAdmin` | 207 passed (8 suites) |
| `tsc --noEmit`, `Spaarke.UI.Components` | no error in the changed files (9 errors elsewhere, in files this round did not touch) |

## Fix round 5 (tests only; no production change)

Round 4's review found that the fake `World.RetrieveMultipleAsync` in `tests/unit/domain/Signals/SignalReevaluationJobTests.cs`
ignored `ColumnSet`, so a select could drop `createdon` and every test still passed. Production code is untouched.

- **F3, the fake projects to the query's `ColumnSet`.** A new `Project` helper (applied in `Paged` and to the two other Signal reads that
  feed the closure service) returns the primary id, the row version and only the selected columns, unless the query asks for
  `AllColumns`. All 258 closure and re-evaluation tests still passed with the projection, so no production select omits a column the
  code reads.
- **Fail-before, F3** (`createdon` removed from one select at a time, restored afterwards):

  | Select with `createdon` removed | Tests that fail |
  |---|---|
  | `ExistingPolicyCodesAsync` (policy list) | `Run_R4_AReusedCode_TheOldRulesSignalIsClosed_...`, `DeletedPolicyCodes_R4_AReusedCode_IsListedOnlyForASignalOlderThanThePolicyHoldingIt(-10, true)` |
  | `DeletedPolicyCodesWithSignalsAsync` (redaction list) | `DeletedPolicyCodes_R4_AReusedCode_IsListedOnlyForASignalOlderThanThePolicyHoldingIt(-10, true)` |
  | `ReadOpenSignalsWithoutPolicyAsync` (close read) | `Run_R4_AReusedCode_TheOldRulesSignalIsClosed_...` |

- **K3, the date rules are pinned** directly on `ExistingPolicyCodes`, each with its fail-before (the production line changed, the
  named test fails, the line restored):

  | Test | Production mutation that fails it |
  |---|---|
  | `ExistingPolicyCodes_R5_EqualTimestamps_AreNotAnOrphan` | `signal < policy` to `signal <= policy` |
  | `ExistingPolicyCodes_R5_DuplicateCodesIgnoringCase_TheEarliestDateWins_WhateverTheOrder` | `c < k ? c : k` to `c > k ? c : k` |
  | `ExistingPolicyCodes_R5_AnUnknownDate_BeatsAKnownOne_AndCountsAsExisting_WhateverTheOrder` | unknown-date branch `: null` to `: k` |
  | `Run_R5_ManyDeletedPolicies_LogTheExactCount_AndOnlyTheFirstFiftyCodes` (60 deleted codes) | `.Take(MaxLoggedOutOfScopeIds)` removed from `ClosedCodes` in the close log line |

- **uac-r2 K3 nit.** `Scope_R4_AnOrdinaryOrLimitedMatter_IsAllowed` (publish route) now also dry-runs and publishes `MatterB`, the
  Limited matter, and asserts the stored scope is `MatterB`. It is a must-not-fire addition (no refusal exists to remove), so it has no
  fail-before.

### Suites (fix round 5)

| Suite | Result |
|---|---|
| BFF, filtered `Ontology\|Signal\|Reevaluation\|PolicyVersion\|Closure` | 2501 passed, 3 skipped, 0 failed |
| `Spaarke.ArchTests`, full | 1056 passed, 0 failed |
