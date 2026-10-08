# Tasks 036 and 026 (stream C) - completion record

> 2026-10-07 - branch `stream/c-036-026` (from `docs/ontology-platform-design` @ `65ff77650`).

## Placement decision (BFF hygiene, `.claude/constraints/bff-extensions.md`)

In the BFF: the wizard's data owner is the BFF (Signals are read only through BFF routes, FR-24); the catalog is closed
code (spec A-2, no Action Engine in R1, section 2.2). No AI-internal type is used. The route is mapped and its services
registered unconditionally (`SignalsModule`). Component justification is in each POML and in the class remarks.

## uac-r2 dependencies re-read (origin/master `0ea74d1c3`, 2026-10-07)

`git diff HEAD origin/master` over uac-r2's surface showed only `AccessibleRecordSetService.cs` and a new
`RestrictedExternalShareRemover.cs` changed since the branch point; the route ledger
(`RouteAuthorizationGuardTests.Ledger.cs`), `RecordRouteAccessAuthorizationFilter`, `CallerRecordAccessProbe`,
`IDataverseUserClient` and `SecureChildLineage` are unchanged, so the plan is not invalidated. Reused, none rebuilt:
`IDataverseUserClient` (caller-identity client), the uniform 404 / single 403 shapes (`ProblemDetailsHelper`,
`sdap.access.deny.caller_unresolved`), the census ledger. Not touched: `SecureChildLineage.cs`,
`config/secure-record-owner-role.json`, `RecordOwnershipResolver`, `CoreAncestorResolver`. The only uac-r2-owned file edited
is the ledger (three small insertions, below); it still needs uac-r2's review per the coordination rule.

## Task 036 - what was built

- `Services/Signals/Actions/DecisionActionCatalog.cs`: 13 actions (send-budget-inquiry, revise-budget, approve-variance;
  mark-complete, reschedule, reassign, send-reminder, extend-response-date, record-the-response; Next-step creators add-todo,
  create-event, send-email, assign-work), per-lane dismissal lists (R-8) with `countsTowardSuppression` false only for
  *Wrong matter - misresolved* (R-10). No escalate / extend-sla / close-inquiry (D-20).
- `Services/Signals/Actions/DecisionPlanService.cs`: plan resolution against the catalog. A plan is refused as a whole on an
  unknown code, an action not allowed for the Signal's lane (or a Next step that is not a creator), a duplicate, a missing or
  malformed plan. Logged once (EventId **50302** `DecisionPlanRefused`, Warning, version id + bounded reason only) and metered
  (`ontology.decisionplan.refused`, dimension `reason`).
- `Services/Signals/SignalCoreRecordAccess.cs`: THE one Signal access decision (task 038 reuses it). Reads the Signal AS THE
  CALLER, then its core record of any type (catalog row -> table; D-36, no code names a type); a no-core Signal is owner-only
  and Do-lane only (D-35). Any denial or fault is the uniform 404; an unresolved caller is the single 403.
- `GET /api/v1/signals/{signalId}/decision-plan` (`Api/Signals/DecisionPlanEndpoints.cs`): 200 plan / 404 / 403 / 422 refused.
- Ledger edits (all in `RouteAuthorizationGuardTests.Ledger.cs`): `ExpectedEndpointFileCount` 117 -> 118 with a history line,
  one `GovernedFile`, one `HandlerDecision` (seam `IDataverseUserClient`, hop `SignalCoreRecordAccess.AuthorizeAsync`).
  Task 046 and the other ontology route tasks bump the same count; expect a trivial conflict at integration.

## Decisions and deviations (none change an acceptance criterion)

1. **Task text says "the Signal's grouping matter"; implemented as the core record of any type** per D-34/D-36 and task 038's
   amended wording. Same mechanism, no type branch.
2. **Authorization is a handler decision (ledger HandlerDecision), not an endpoint filter (ADR-008 prefers filters).** The
   decision needs the Signal row's lane and policy version, which the handler also needs; a filter would read the row twice.
   Same accepted shape as the events list (uac-r2 task 159). Surfaced, not silent: ADR-008 path C is available (move the
   read into a filter) if the reviewer prefers it.
3. **The plan and the catalog/entity-set lookups use the BFF's own `IGenericEntityService`**: configuration and metadata, not
   matter data, and nothing read that way is returned except the plan and catalog.
4. **Plan JSON shape** is task 009's `{"actions":[...],"nextSteps":[...]}`; `nextSteps` optional.
5. **Excludes are directional in v4 (Z-4); made MUTUAL in round 2** (see below).
6. **Parameter / effect-line wording** is mine (the spec fixes only approve-variance's effect line, which is verbatim). The
   Decide work types are ask / fund (v4 W-8); Do actions carry none (the rule declares it).
7. The 036 POML says "rule description from task 026 when present": wired in task 026 as `ruleDescription` on the response.

## Task 026 - what was built

`Services/Signals/RuleBodyDescriber.cs` (+ `PredicateCompiler.ParseBounded` made `internal`, one word). It gates on
`PolicyVersionValidator.ValidateForSave` (the only sanctioned path to the compiler, `PredicateCompilerCallerGuardTests`), so it
refuses whatever the compiler refuses, then re-reads the body with the compiler's own strict parser. One line per clause
(`when`, `all[N]`) with words, formal form and clause id. Lookup GUIDs in a closed table
(`sprk_communication.sprk_triagecategory`, `sprk_event.sprk_eventtype_ref`) are shown as names by one batched read per reference
table; an id with no row refuses the description. Triage-category conditions read "classified as ..." (HANDOFF 1.4 L116).
Nothing is stored; no column added.

Known gaps (not defects against the criteria):
- **Choice values print raw** (for example "review outcome is not 100000003", "status is 659490001"): option labels need a
  metadata read this task does not make. The formal form always carries the exact value. A label table or a metadata read is
  a follow-up if the owner wants it (wording only; the filter is unaffected).
- Column and table labels come from two small closed tables with a fall-back to the logical name, never a guess.
- Threshold bodies are refused until task 025 lands the grammar (the validator refuses them today).

## Test evidence

- `DecisionActionCatalogTests` (49) + `RuleBodyDescriberTests` (13 domain + 2 live seam). Whole Domain.Signals/Services.Signals
  suite: 356 passed. Seam run LIVE against spaarkedev1 (`SIGNALS_LIVE_DATAVERSE_URL`, `AZURE_TOKEN_CREDENTIALS=AzureCliCredential`):
  the two seam tests pass (real category and event-type names).
- `Spaarke.ArchTests`: 817 / 818; the one failure is the known writer-credential I5 test (also failing on the base branch,
  `OntologyWriterCredentialFactory`), not caused by this work. `RouteAuthorizationGuardTests` 86 / 86.
- Not run: a live low-privilege-user route test of the 404 path (needs the 097-style user fixtures; task 038 builds that
  harness). The authorization decision is covered with the caller-identity client faked at its module boundary.

## Size and CVE

`dotnet publish -c Release` (framework-dependent linux-x64, PDBs included), `Compress-Archive -CompressionLevel Optimal`,
fresh worktrees at short paths (`C:\wtcma` / `C:\wtcba` and `C:\wtcmz` / `C:\wtcbz`), 192 files on every side:

| Side | MB |
|---|---|
| fresh `origin/master` @ `0ea74d1c3` | 36.22 |
| + task 036 (`2cff1f051`) | 36.24 (+0.02) |
| + task 026 (`421da7944`) | 36.25 (+0.03 total) |

`dotnet list package --vulnerable --include-transitive`: no vulnerable packages; no csproj changes.

## Step 9.5 gates

code-review and adr-check were run for task 036 (skills invoked); for task 026 their checklists were applied directly to the new files without re-invoking the skills (same author, same session). No F-class finding. Known limits recorded: ADR-008 note above
(warning, path C available); array-backed catalog collections are mutable to code that casts them (K1, own repo only).

## Round 2 (independent review PASS-WITH-FINDINGS, 2026-10-07)

| # | Finding | Fix |
|---|---|---|
| 1 | F2 `send-budget-inquiry` had a required "Reply expected within" choice with no options (D-56, D-20) | Parameter removed; the action is now to / subject / body. Test pins the three codes. |
| 2 | F3 choice parameters could not carry options | `DecisionActionParameter` and `DecisionParameterDto` gain `Options` (`value` + `label`) and `OptionsSource`. The only remaining choice, `record-the-response` "Response", carries the D-58 values (`received-outside-spaarke`, `delivered-on-the-matter`, `no-longer-needed`; labels Received outside Spaarke / Delivered on the matter / No longer needed). The stable string values are the wizard-to-commit contract; the commit route (044) maps them to the `sprk_responseoutcome` option set from task 047. No option comes from Dataverse today, so no `OptionsSource` is in use, but the field exists for one. Test: every choice parameter has options XOR a source. |
| 3 | F3 excludes one-way: the seeded v2 plan let revise-budget and approve-variance both be taken | Exclusions are declared once and made mutual when the catalog is built (`Mutualize`); `FirstConflict(codes)` is the order-independent check the commit route runs. Tests: mutuality over the whole catalog, and the seeded plan's order. |
| 4 | F3 `RuleBodyDescriber` did not catch Dataverse faults, so a fault was a 500 | Faults (not cancellation) are caught, logged (EventId 50303 `RuleDescriptionRefused`, reason only) and metered (`ontology.ruledescription.refused`, reasons `lookup_read_failed` / `lookup_unresolved`); the describer returns a refusal and the route serves the plan with `ruleDescription: null`. Tests: describer-level (refusal + log + metric, message not leaked), cancellation still throws, route-level 200 with null description. |
| 5 | K2 core-record check not limited to the core set | `SignalCoreRecordAccess` accepts only tables in `CoreAncestorResolver.CoreRecordEntities` (uac-r2's list, read at run time, no per-type branch); anything else is denied and never read. Tests: all four core types go through the same code; `account`, `sprk_document`, `sprk_signal` are denied and not read; the set is pinned. This holds until task 049 removes Console User Write on `sprk_signal`. |
| 6 | Valid token, no Dataverse user, got 404 | A Dataverse 403 on the Signal or core read is followed by `WhoAmI` (needs no privilege): if the caller cannot be identified the result is the single 403 `caller_unresolved` (D-29), otherwise the ordinary 404. The extra call happens only on a denial. Tests for both. |
| 7 | Same code in `actions` and `nextSteps` | Refused as `duplicate_action`. |
| 8 | Ledger line citations | Corrected to the final file: `DecisionPlanEndpoints.cs:53` (AuthorizeAsync), `:54-56` (403), `:69` (404); `SignalCoreRecordAccess.cs:98` / `:150`. |
| 9 | Reschedule effect line showed "(sprk_duedate)" | "Moves the due date of the item". A test asserts no effect line contains `sprk_`. |
| 10 | Component justification | `SignalCoreRecordAccess` remarks now name `AuthorizationService.GetCallerRecordAccessAsync` (uac-r2 task 070) and say why the Signal read goes through `IDataverseUserClient`: the decision needs the Signal row's own columns under the caller's security in one uncached call, the shared method returns a rights snapshot cached 60 s and needs the user id and token plumbed in. It stays a drop-in for the core-record half if the reviewer prefers. |
| note | Describer validates Existence only | Noted in task 025's POML `<notes>`. |
| note | FR-54 Do-lane own work | Task 038's POML already states it. Task 043's POML had nothing; a note now says it is 038's rule and that 043 must re-check each Signal id with `SignalCoreRecordAccess` (036). |

Evidence: `Domain.Signals` + `Services.Signals` unit suites 375/375 (the earlier 356 plus the new tests); the two live seam tests
pass against spaarkedev1; `Spaarke.ArchTests` built explicitly then run 817/818, the one failure being the writer-credential I5
test that master/docs branch fixed in `0977c274d` (not in this branch's base); `RouteAuthorizationGuardTests` 86/86. Publish
size was not re-measured: round 2 adds no package and only small code (previous +0.03 MB).

## Round 3 (final)

| # | Finding | Fix |
|---|---|---|
| F-1 | After a Dataverse 403 the `WhoAmI` identity check treated every failure as "caller unresolved" | `CallerUnresolved` only for `AccessDenied` or the user-context / OBO codes (`UserContextRequired`, `OboExchangeFailed`, `OboNotConfigured`); `RateLimited`, `ServiceError` and anything else fall through to the uniform 404 (logged). Tests: 429 and 5xx after a 403 give 404; an OBO failure still gives the 403. |
| K-1 | `Mutualize` silently dropped an exclude naming an unknown code | It now throws at static initialisation (`InvalidOperationException`); test calls it with a typo'd and a good declaration. The `record-the-response` <-> `extend-response-date` (and `send-reminder`) pair is pinned as mutual, with reminder and extend still compatible. |
| K-2 | Describer catch used `ex is not OperationCanceledException` | Filter is now `ex is not OperationCanceledException \|\| !ct.IsCancellationRequested` (and not `PredicateCompilationException`): a timeout-style `TaskCanceledException` is a refusal; the caller's own cancellation still propagates. Tests for both. |
| K-4 | Refusal log had no exception type | `exceptionType={ExceptionType}` (type name only, `none` when there was no exception) added to the log; the metric stays dimensioned by reason only. Asserted in the tests. |

Evidence: Domain.Signals + Services.Signals + describer unit and live seam tests 383/383; `RouteAuthorizationGuardTests` and
`PredicateCompilerCallerGuardTests` pass (ArchTests built explicitly first).
