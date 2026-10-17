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
| 2 | Nightly: evaluated within M; Signals written/closed only for M; N gets none; run succeeds | `Run_AMatterScopedRule_IsEvaluatedWithinItsMatter_AndAQualifyingSubjectOnAnotherMatterGetsNoSignal` (Success, raised 1, no key for the other matter), `Run_AMatterScopedRule_ClosesOnlyInsideItsMatter_AndNamesAnOpenSignalOutsideItInsteadOfClosingIt`; control `Run_TheSameDataWithoutAScope_RaisesForBothMatters` |
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

- A Signal citing no version is not superseded by a scope change (the existing supersede rule); the job's "outside its matter
  scope" warning names it so it is not silent. All Signals written since versioning cite a version.
- Redaction (D-123) still covers out-of-scope Signals of the policy; it only ever removes detail (design §4).
- A stored non-blank `sprk_tenant` (written outside the BFF) is not cleared by a publish; the policy stays `ScopedNotEvaluated`.
- A publish without `matterId` un-scopes the policy; the wizard always sends the scope it shows, so this is the "All matters"
  choice. A non-wizard caller must send the scope it wants kept.
- `RuleSubjectOf` in the authoring service duplicates the job's private `RuleSubjectEntity` (both read the body's `subject`).
- No live UI check (no deploy in this lane); dark-mode and browser runs not done. `tsc` for Spaarke.AI.Widgets cannot resolve
  `@spaarke/ui-components` locally (pre-existing; affects unchanged files too). Spaarke.UI.Components `tsc` shows 9 pre-existing
  module-resolution errors, none in changed files.

### Live reads (read-only, own `az` token, spaarkedev1)

- `sprk_policy` relationship metadata: the ManyToOne lookup to `sprk_matter`, navigation property `sprk_Matter`.
- `sprk_policies?$select=sprk_code,_sprk_matter_value,sprk_tenant`: all six policies have no matter and a blank tenant, so
  nothing in dev changes behaviour on deploy.
