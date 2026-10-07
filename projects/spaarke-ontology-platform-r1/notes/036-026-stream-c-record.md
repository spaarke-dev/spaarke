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
5. **Excludes are directional** (v4 Z-4: an earlier action removes the later step): `approve-variance` excludes `revise-budget`
   but not the reverse. A plan that lists `revise-budget` first lets both be taken. Plan authors order the excluding action
   first; a plan-save check for this is an owner call (not built).
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
