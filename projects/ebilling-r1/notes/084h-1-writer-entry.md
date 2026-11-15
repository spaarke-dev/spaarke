# 084h-1: the Decision Record writer entry for per-record Procedure changes

> **Task:** `tasks/084h-1-record-procedure-writer-entry.poml` · **Design:** `084h-procedure-change-decision-records-design.md` (revised, ontology-r1 PASS on re-check) · **Base:** `work/ebilling-r1` at 73ce24172c · **Status:** built, awaiting review (code-review, adr-check, one adversarial pass, ontology-r1 path B review). Not run here, as the brief says.

## What was built

| Item | Where |
|---|---|
| `RecordProcedureDecisionKey` (`procedure-dr-{subjectEntity}-{subjectId}-{decisionId}`) | `DecisionRecordWriter.cs`, after `BillingDecisionRecordRequest` |
| `RecordProcedureDecisionRecordRequest` (no Signal, policy-version or privilege member) | same |
| `DecisionRecordRefusalReason.RecordProcedureLookupFailed` = `decision_record_procedure_lookup_failed` | same |
| `public Task<DecisionRecordWriteResult> WriteRecordProcedureDecisionAsync(RecordProcedureDecisionRecordRequest, CancellationToken = default)` and the internal overload with the lazy decider contact | `DecisionRecordWriter.cs`, the "084h-1" region after the billing lookup |
| `internal void PreflightRecordProcedureDecision(...)`, `ValidateRecordProcedure`, `FindRecordProcedureRecordsAsync`, `RecordProcedureDecisionHash`, `RecordProcedureValuesOf`, `RecordProcedureReview` | same region |
| `KeyedRecordsQueryAsync` / `AddWriterFilterAsync`: the billing query body, moved unchanged and given the code set | same file; `BillingRecordsQueryAsync` and `AddBillingWriterFilterAsync` now forward to them with `BillingLookupCodes` |
| `DecisionActionCatalog.RecordProcedureActions`, `TryGetRecordProcedure`, `DeclaredRecordProcedure` (seven codes), `SeparateRecordProcedure` (three-way) | `DecisionActionCatalog.cs`, the "084h-1" region after `FirstBillingConflict` |
| Tests W1-W8 | `tests/unit/Sprk.Bff.Api.Tests/Services/Signals/DecisionRecordWriterTests.RecordProcedure.cs` (new partial) |

## Decisions

1. **Preflight is an `internal` instance method, not `internal static`** (design §3.2 wrote `internal static`). The preflight runs `Content(...)` for the column-size refusals, as `PreflightBillingDecision` does, and `Content` is an instance method (it reads the writer's clock). `internal` rather than billing's `public`: the only callers (084h-2's recorder, 084h-3's job) are in the BFF assembly.
2. **The note rule is enforced in the writer.** `RecordProcedureValuesOf` drops a `note` whose value is null or only white space before the values are validated, written to `sprk_steps` and hashed, so "no note", a null note and a blank note are one change (W3). A given note is written and hashed as given (not trimmed: the 084h-2 service trims it with `InvoiceCorrectionNote.Check`, and the repair job carries the same text).
3. **An `automated:` decider role is refused** (`ReviewInvalid`), as well as an empty confirmer: the path is human-only (P5), and this mirrors `ValidateBilling`'s "a user confirmed cannot carry an automated decider".
4. **The "already recorded" decision logic is the billing entry's, written again for this entry** (another code → `ReviewInvalid`; same code, other or unreadable hash → `ReviewInvalid` with its own message; same hash → `AlreadyRecorded`). Only the query body was extracted (the brief and design §3.2 step 2 allow that one move); extracting the decision logic too would have touched the billing method body beyond the approved extract. About 20 lines; the stored hash is read with the existing `StoredBillingHashOf`.
5. **Classes and gates in one place** (owner proposal, §13 Q7): `DecisionActionCatalog.RecordProcedureGate` (Tier2b, reversible, RecordOfTruthImpact, ExternallyVisible false) and the two `RecordProcedureRoutine` / `RecordProcedureJudgement` constants. Assign, pin, unpin: Routine; exclude-procedure, exclude-rule, change-rule, remove-override: Judgement.
6. **Parameter kinds and lookup tables** (the design gave codes and Required only): every id is a `Lookup` — `procedure` → `sprk_procedure`, `version` → `sprk_procedureversion`, `rule` → `sprk_policy`, `firm` → `sprk_organization`; `reason` and `note` are `TextArea`. Each code has one effect line. `PlanLane`, `WorkType` null; `Excludes` empty. `ResolvesSignal` keeps its default (true); nothing on this path reads it.
7. **The record's outcome is Authorized** (the change was made), as on the billing path; the change's own outcome word (`Assigned`, `Pinned`, …) is in `sprk_steps`.

## Amended existing tests (ontology-reviewed; list for ontology-r1)

| File | Change | Why |
|---|---|---|
| `tests/Spaarke.ArchTests/DecisionRecordContextImmutabilityTests.cs` | `CreateEntryPoints` gains `WriteRecordProcedureDecisionAsync`; the remarks say so; `OneCreateSite_Controls`' synthetic writer gains the procedure forwarder (as #2212 added the billing one) and two must-fire controls (its target not reaching the shared create; the entry renamed away) | `EveryPublicWriteResultMemberIsAListedEntryPoint` fails on any unlisted public write-result member, and the controls' "sanctioned shape" must hold every listed entry point. Additive. |

Not amended (the design required no change to them): `tests/unit/domain/Signals/DecisionActionCatalogTests.cs` (the billing exact-set test and the required-parameter theory: `BillingActions` is unchanged) and `DecisionRecordWriterTests.Billing.cs:191` (`BillingSubjectByCode` is unchanged). `git diff 73ce24172c -- tests/unit/Sprk.Bff.Api.Tests/Services/Signals/DecisionRecordWriterTests*.cs` shows only the new `RecordProcedure` partial.

## Tests

| Run | Result |
|---|---|
| New `DecisionRecordWriterTests.RecordProcedure.cs` (W1–W8) | 78 / 78 pass |
| `DecisionRecordWriterTests` (all partials) + `DecisionActionCatalogTests` | 539 / 539 pass |
| Services.Signals + Services.Finance + Domain.Signals (billing-writer users included) | 8677 / 8677 pass |
| `tests/Spaarke.ArchTests` (full) | 2622 / 2622 pass |
| `dotnet build src/server/api/Sprk.Bff.Api/ --warnaserror --no-incremental` | 0 warnings, 0 errors |

Fail-before: with the base writer and catalog restored, the new partial does not compile (CS0246 `RecordProcedureDecisionRecordRequest`).
Mutation proof: three seeded mutations in one build (note normalization removed; `BillingLookupCodes` used in the procedure lookup; the `FilingCoreTables` subject check removed) gave 13 failures across W2/W3/W4/W5; the writer was then restored and diff-checked.

## Publish size and CVE

Release publish (`dotnet publish -c Release`) in short-path worktrees, then `Compress-Archive`:

| Build | Files | Raw | Zip |
|---|---|---|---|
| origin/master `d03537e133` (`C:\wt84hm`) | 191 | 140,307,565 B | 42,311,915 B |
| 084h-1 `ee4f632e31` (`C:\wt84hb`) | 191 | 140,335,029 B | 42,319,854 B |
| Delta | 0 | +27,464 B | **+7,939 B (~0.008 MB)** |

Both worktrees removed with `Remove-WorktreeSafe.ps1`. The delta also includes everything on `work/ebilling-r1` not yet on master, so the 084h-1 share is at most this. Far under the +5 MB per-task threshold; total ~42.3 MB zip stays under the 55 MB review line. No new packages.

CVE: `dotnet list src/server/api/Sprk.Bff.Api package --vulnerable --include-transitive` reports no vulnerable packages.

## Known limits

| # | Limit |
|---|---|
| K-a | W8's "the Values the service builds" uses the design §4.1 table as the fixture (the 089 service builds nothing yet). 084h-2's S1 runs the real service's seven requests through `PreflightRecordProcedureDecision`, which closes it. |
| K-b | Two CONCURRENT writes of the same decision id can both create (lookup then create, no unique key), exactly as on the billing and gate paths. 084h-2 writes once per change after its own commit; the repair job's second delivery is sequential. |
| K-c | A core subject that no longer exists is not caught before the create (the CORE branch makes no read by design); the Dataverse create then fails (`CreateFailed`), nothing is written. |
| K-d | Design K-1, K-3, K-4 (DELETE with no answer, crash before record or enqueue, Pin-to-Follow files two records) belong to 084h-2 and 084h-3 and are unchanged here. |

## Out of scope (untouched)

084h-2 (recorder, `ProcedureAssignmentService`, endpoints, S1-S10), 084h-3 (repair job, J1-J5), 084h-4 (UI). No live Dataverse, no deploy.

## Process notes

- Step 9.5 adversarial review was not run here, per the brief; the orchestrator reviews.
- `current-task.md` was not edited (shared project state, managed by the orchestrator).
- `scripts/check-task-status-drift.ps1` cannot parse the hyphenated id `084h-1` (its id regex accepts `\d{3}(\.\d+)?[a-z]?` only), so it reports "INDEX has no row" for this POML. Tooling limit, not fixed here. It also shows pre-existing drift unrelated to this task (050a, 084d, 161, 151d; 084h has no POML).

rework: none task 084h-1
