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
4. **The "already recorded" decision logic is the billing entry's** (another code → `ReviewInvalid`; same code, other or unreadable hash → `ReviewInvalid` with its own message; same hash → `AlreadyRecorded`). In the first build it was written again for this entry; fix round 1 moved it, and the row-reading lookup, into two shared helpers that both entries call (`FindKeyedRecordsAsync`, `AlreadyRecordedOrRefuse`; see Fix round 1, item 1). The stored hash is read with the existing `StoredBillingHashOf`.
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
| K-c | (Corrected in fix round 1.) The core derivation does not catch a core subject that no longer exists: the CORE branch makes no read by design, and the record-type read is by table. The OWNER step catches it, before the create: `FiledAndOwnedAsync` asks the ownership resolver about the core record, the resolver's parent read finds no row and answers `Refused(ParentUnresolved)`, and the writer refuses `OwnerRefused` (owner code `record_owner_parent_unresolved`; this path never sets the gate's skip). A resolver fault is `OwnerResolutionFailed`. Nothing is written in either case. The create is never reached, so it is not `CreateFailed`. Verified by reading `DecisionRecordWriter.ResolveOwnerCoreAsync` and `RecordOwnershipResolver.ResolveFromParentsAsync` / `ReadParentAsync`; no unit test covers it, because the writer tests mock the resolver. |
| K-d | Design K-1, K-3, K-4 (DELETE with no answer, crash before record or enqueue, Pin-to-Follow files two records) belong to 084h-2 and 084h-3 and are unchanged here. |

## Fix round 1

Base: `e93ad7298d`. Binding rule unchanged: no existing signature or behaviour changes (design §3.4 P7). Lines move into shared helpers; billing keeps its constants and messages.

| # | Finding | Fix |
|---|---|---|
| 1 | (F2) The "already recorded" mechanism was duplicated, about 55 lines. The row-reading loop, catch filter and refusal wrapping copied `FindBillingRecordsAsync`, and the other-code, other-hash and unreadable-hash logic copied `WriteBillingDecisionAsync`. | Two private helpers in `DecisionRecordWriter.cs`. `FindKeyedRecordsAsync(subject, key, codes, isCode, refusalReason, refusalMessage, ct)` is the billing lookup body, moved unchanged: the same query (via `KeyedRecordsQueryAsync`), the same per-row code check (as a predicate), and the same catch filter, so a `TaskCanceledException` with the caller's token not cancelled is still the lookup refusal. `AlreadyRecordedOrRefuse(recorded, code, hashOf, otherCodeRefusal, otherContentRefusal, contentUnreadableRefusal)` is the billing decision body, moved unchanged. The hash is passed lazily (`hashOf`) so it is still computed only after the other-code check, as before. `FindBillingRecordsAsync` and `FindRecordProcedureRecordsAsync` keep their signatures and forward to the lookup helper with their own codes, predicate, reason and message. Both entries call `AlreadyRecordedOrRefuse` with their own three messages. **Billing unchanged:** `git diff e93ad7298d -- …DecisionRecordWriterTests.Billing.cs tests/unit/domain/Signals/DecisionActionCatalogTests.cs` is empty, and every billing writer test passes. Mutation proof: swapping the unreadable/other-content choice in the shared helper and dropping the timeout clause of its catch filter failed 9 tests, 6 billing and 3 procedure, so both entries run through the helpers. The writer was then restored byte-for-byte. |
| 2 | The procedure entry had no unreadable-hash test and no timeout-as-cancellation test. | New tests in `DecisionRecordWriterTests.RecordProcedure.cs`: (a) a writer row under the key with the code and no readable hash → `ReviewInvalid`, `RecordProcedureContentUnreadableRefusal`, nothing created; (b) of two rows with the code, one unreadable, the row with this hash answers; (c) a `TaskCanceledException` lookup fault with the caller's token not cancelled → `RecordProcedureLookupFailed`, nothing created, no owner ask; (d) the caller's own cancellation in the lookup propagates as `OperationCanceledException`, nothing created. |
| 3 | (K, fixed) `RecordProcedureValuesOf` dropped a null or blank note only when it was a .NET string or null. A note read back from JSON (a `JsonElement` / `JsonNode` holding a JSON null or a blank string), as the 084h-3 repair job reads its payload, was kept and hashed. So a repair would be refused on hash against the inline record. | `IsBlankNote` treats a `JsonElement` of kind Null/Undefined or a blank String, and a `JsonNode` of the same kinds, as no note. Tests: Values after a JSON round trip (as `JsonElement` and as `JsonNode`) hash equal to the inline Values for assign, change-rule and remove-override, with and without a note. A JSON null or blank note (4 cases) is dropped from the values, the hash and the stored `sprk_steps`, and the inline request without a note then answers AlreadyRecorded. A JSON note with text is kept. **Fail-before:** with the base writer, the blank-JSON-note theory fails 4/4; the other 10 new tests pass, because they cover existing behaviour. |
| 4 | Comments. | The `KeyedRecordsQueryAsync` row-cap comment now says "at most one row per key, billing or Procedure". The `ResolveBillingCoreAsync` summary now also names the Procedure entry's core subject (the CORE branch, no ancestor read). The `BillingRecordsQueryAsync` summary notes that `FindBillingRecordsAsync` runs the same query through the helper. K-c in Known limits is corrected (owner step, `OwnerRefused`, not `CreateFailed`). |
| 5 | Hand-offs, recorded here as known limits and not fixed in 084h-1. | See the table below. |

**Hand-offs to 084h-2 / 084h-3 (known limits, not fixed here)**

| # | Limit | Owner |
|---|---|---|
| H-1 | The decider-contact supplier 084h-2 will reuse (`BillingDecisionRecorder.cs:166`, `DeciderUnreadable`) refuses as `BillingRecordLookupFailed`, not `RecordProcedureLookupFailed`. Through the Procedure entry, an unreadable deciding contact therefore surfaces with the billing reason. | 084h-2 maps it: it either supplies its own contact reader with a Procedure reason, or translates the refusal at the recorder boundary. 084h-3's retry list must classify `BillingRecordLookupFailed` (a transient identity read: retry) and `CreateFailed` (retryable; after a timeout the row may exist, so the next delivery answers AlreadyRecorded). It must also classify `OwnerRefused` (a deleted core, K-c: permanent) and `OwnerResolutionFailed` (retry), which design §7.3 does not list. |
| H-2 | A whitespace-only `Reason` hashes differently from a null one, as on the billing path (the writer hashes the reason as given). | 084h-2 trims the reason, and turns a blank one into null, before it builds the request. The repair job carries the same text, so the two stay equal. |
| H-3 | The writer caps `reason` at 2000 characters but not the note (ontology-r1, fix round 2 PASS). An over-long note would reach the create and fail as a retryable `CreateFailed`. | 084h-2 enforces or trims the note to 2000 at the service before it builds the request (design §9), with a test. |

**Runs (fix round 1)**

| Run | Result |
|---|---|
| `DecisionRecordWriterTests.RecordProcedure_*` (14 new) | 92 / 92 pass |
| `DecisionRecordWriterTests` (all partials) + `DecisionActionCatalogTests` | 549 / 549 pass |
| `BillingDecisionRecorderTests` | 22 / 22 pass |
| Services.Signals + Services.Finance + Domain.Signals | 8691 / 8691 pass |
| `tests/Spaarke.ArchTests` (full) | 2622 / 2622 pass |
| `dotnet build src/server/api/Sprk.Bff.Api/ --warnaserror --no-incremental` | 0 warnings, 0 errors |

## Fix round 2 (ontology-r1 review)

Base: `32fca114b3`. Two test fixes and one small hardening, from the ontology team's review of `WriteRecordProcedureDecisionAsync`. The only writer change is item 3 (`IsBlankNote`, a new `IsTextNote`, one check in `ValidateRecordProcedure`); `DecisionRecordWriterTests.Billing.cs` and `DecisionActionCatalogTests.cs` are untouched.

| # | Finding | Fix | Proof |
|---|---|---|---|
| F3-1 | The pre-I/O refusals in `ValidateRecordProcedure` (an empty `DecisionId`, a blank `DeciderRole`, a blank `Outcome`, a `Reason` over `ReasonMaxLength`) had no test. Deleting the reason check left everything green, and a 2001-char reason would then fail AFTER I/O, in the create, as `CreateFailed` (retryable). | Theory `RecordProcedure_FixRound2_APreIoRefusal_IsReviewInvalid_WithNoIo_AndThePreflightRefusesIt_TheSame` (six cases: reason too long, empty decision id, empty and whitespace role, empty and whitespace outcome). Each answers `ReviewInvalid` through the writer with `NoIo(h)`, and through `PreflightRecordProcedureDecision` (which throws the same refusal). New test `RecordProcedure_FixRound2_ThePreflight_RefusesAReasonOverTheColumnSize_AndPassesOneAtTheLimit` (2001 refused, 2000 passes). | Each check deleted in turn, the tests run, the check restored: decision id, 1 fails; role, 2 fail; outcome, 2 fail; reason, 2 fail. |
| F3-2 | W6's "the derivation reads nothing" verified only `RetrieveAsync(table, id, ...)` never ran. A derivation read through `RetrieveMultipleAsync`, or of another table or id, passed. | W6 (`RecordProcedure_W6_TheRecordIsFiledUnderTheCoreRecord_...`) now also asserts that every `h.Entities` invocation is `RetrieveMultipleAsync` and that `h.Lookups` holds exactly one query (the already-recorded lookup). | An extra `RetrieveMultipleAsync` before the core derivation: W6 (3 cases) and W4 fail. An extra `RetrieveAsync("sprk_other", ...)`: W6 (3 cases) fail; the old assertion passed both. |
| 3 | (Hardening) `IsBlankNote` called `GetValue<string>()` on a `JsonNode` of kind String. A `JsonValue` over a Guid or a date reports that kind but holds no string, so the call threw `InvalidOperationException` while the note was classified (an unhandled exception out of the entry, not a refusal). A non-string note (a number, an object, an array, a boolean) was also accepted, hashed and written as if it were text, because `note` is no declared parameter and nothing validated it. | `IsBlankNote` uses `JsonValue.TryGetValue<string>`, so a non-string value is not blank and nothing throws. New `IsTextNote` (a string, a JSON string, or a `JsonValue` holding a string), and `ValidateRecordProcedure` refuses a note that is left and is not text: `ReviewInvalid` ("A note must be text."), before any I/O, so the preflight refuses it too. | Tests: `..._ANonStringNote_IsNotBlank_AndDoesNotThrowWhileItIsClassified` (8 kinds: JSON number, object, array, boolean; JsonNode number, Guid, object; CLR int; the Guid case threw before) and `..._ANonStringNote_IsRefusedAsReviewInvalid_BeforeAnyIo_AndByThePreflight` (the same 8); `..._ATextNote_ThroughAJsonValue_IsStillWritten`. Before the change 9 of these failed; with the note check deleted, 8 fail. |

**Known limits (not fixed here)**

| # | Limit | Owner |
|---|---|---|
| K1 | 084h-3's retry list must classify every refusal this entry gives for an unreadable dependency: `BillingRecordLookupFailed` (the deciding contact's read, via the billing recorder's supplier, H-1), `OwnerRefused` (a deleted core record, K-c: permanent, so no retry), `OwnerResolutionFailed` (retry) and `CreateFailed` (retryable; after a timeout the row may exist, so the next delivery answers `AlreadyRecorded`). The `OwnerRefused`-for-a-deleted-core case needs its own test in 084h-2/3, where the request reaches the entry through the real path. | 084h-3 (retry list), 084h-2/3 (the test). ontology-r1 checks the list in the 084h-3 review. |
| K2 | The required-parameter check accepts `Guid.Empty` and blank ids for a parameter (it refuses only a missing, null or whitespace-string value), as `ValidateBilling` does. No real path reaches it: the 089 service builds every id from a row it has read. | None (same as billing). |

**Runs (fix round 2)**

| Run | Result |
|---|---|
| `DecisionRecordWriterTests.RecordProcedure_*` | 116 / 116 pass (92 after round 1, plus 24 new cases: 6 + 1 + 8 + 8 + 1; W6 was strengthened in place) |
| `DecisionRecordWriterTests` (all partials) + `DecisionActionCatalogTests` + `DecisionRecordContextImmutabilityTests` | 573 / 573 pass |
| `dotnet build src/server/api/Sprk.Bff.Api/ --warnaserror --no-incremental` | 0 warnings, 0 errors |

## Out of scope (untouched)

084h-2 (recorder, `ProcedureAssignmentService`, endpoints, S1-S10), 084h-3 (repair job, J1-J5), 084h-4 (UI). No live Dataverse, no deploy.

## Process notes

- Step 9.5 adversarial review was not run here, per the brief; the orchestrator reviews.
- `current-task.md` was not edited (shared project state, managed by the orchestrator).
- `scripts/check-task-status-drift.ps1` cannot parse the hyphenated id `084h-1` (its id regex accepts `\d{3}(\.\d+)?[a-z]?` only), so it reports "INDEX has no row" for this POML. Tooling limit, not fixed here. It also shows pre-existing drift unrelated to this task (050a, 084d, 161, 151d; 084h has no POML).

rework: none task 084h-1
