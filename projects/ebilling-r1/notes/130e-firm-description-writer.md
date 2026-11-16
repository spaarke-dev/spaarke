# 130e-1: DEF-060 firm description writer (server)

Task: `tasks/130e-firm-description-writer.poml`, server side only. Design: `notes/def-060-firm-description-design.md`, with §11 overriding §§2-9
(C1 replaces the §3.2 "persisted plan" bullet; EB-D-213 / O5: the column is unsecured; C2 still applies to the BFF path).
The Rule Review editor field is task 130e-2 (contract below).

## What changed, per file

### New
| File | What |
|---|---|
| `Services/Finance/FirmDescriptionRules.cs` | The one validator. `Validate(raw)` and `Validate(raw, ruleSetName, policyName)`: trim, blank = no description (valid, null), at most 200 runes (Unicode scalars, not UTF-16 units), no `{ }`, no `< >`, no invisible rune (categories Control, Format, LineSeparator, ParagraphSeparator; `IsInvisibleControl`, moved here from `FirmRejectionFindings`), and (C4) not containing the rule set or policy name, case-insensitively. Stable codes `firm_description_too_long / _braces / _markup / _invisible_character / _names_rule`; drop log code `EBILLING-FIRM-DESCRIPTION-DROPPED`. |
| `Services/Signals/Billing/BillingFirmReasonReplay.cs` | C1: replay re-derives the firm reason from the plan's policy version id: reads `sprk_firmdescription`, the policy name and the rule set name, runs `Validate` with both names, and sets `FirmReason` to the result (the plan's own value, if any, is overwritten). Cached per run (one read per version). A version that no longer exists drops the text (`firm_description_row_gone`); a version whose policy cannot be read drops it (`firm_description_names_unread`); any other read fault propagates (as every other replay read). |
| `scripts/ebilling/Deploy-EbillingFirmDescriptionColumn.ps1` | O4 schema script on `EbillingSchema.psm1`: `sprk_policyversion.sprk_firmdescription`, SchemaName `sprk_FirmDescription`, Memo, MaxLength 2000, optional, not secured. The `MSCRM.SolutionUniqueName` is the one unmanaged solution that contains `sprk_policyversion` (read from `solutioncomponents`; `-SolutionUniqueName` may name it and must then contain the table; none or several is exit 2). Publishes `sprk_policyversion` only, only after a write; never `PublishAllXml`; never alters an existing column (another type, length or secured is exit 2). `-WhatIf`, `-Apply`, `-Verify` (column, Memo, MaxLength 2000, not secured). Every request goes through the module's `Invoke-Dv` (`Consistency: Strong`). **Not run** (no live call in this task). |
| `tests/Spaarke.ArchTests/FirmReasonWriteSitesGuardTests.cs` | O3 guard (below). |
| Tests under `tests/unit/domain/**` and `tests/integration/**` | Listed with each condition below. |
| `tests/scripts/Deploy-EbillingFirmDescriptionColumn.Tests.ps1` | Pester 5.7.1, 20 tests. |

### Modified
| File | What |
|---|---|
| `Services/Signals/SignalWriter.cs` | `SignalWriteRequest.FirmReason`. `BuildEntity` (the create payload) sets `sprk_firmreason` when it is non-blank; no update path sends it. `CheckRequest` (shared by `PreflightRefusal` and `WriteAsync`) refuses a `FirmReason` that fails `Validate` with `firm_reason_invalid` (an `ArgumentException` naming the code, never the text), before any Dataverse call. |
| `Services/Signals/Billing/BillingFactLoader.cs`, `ProcedureResolver.cs` | `BillingRuleCandidate` carries the version's `FirmDescription`, `PolicyName` and `RuleSetName` (read with the rule). |
| `Services/Signals/Billing/BillingFindingSignalMapper.cs` | `FirmReasonOf` validates with the version's names; an invalid text is dropped with a code-only Warning. The firm reason is set only when the preflight fallback was not taken (O2). Takes an optional logger. |
| `Services/Finance/Submission/InvoiceSubmissionPersister.cs` | Comment only: the plan persists the policy version id (already there) and never the firm reason (C1). |
| `Services/Finance/InvoiceRuleSignalReconciler.cs`, `Submission/InvoiceSubmissionAfterSteps.cs`, `Submission/InvoiceSubmissionRerun.Signals.cs`, `InvoiceCorrectionReevaluation.cs`, `InvoiceLineAddService.cs` | Every plan replay goes through `BillingFirmReasonReplay`. The reconciler takes `IGenericEntityService`. |
| `Services/Finance/FirmFindingsProjection.cs` | Q2/K1: a Rejected submission's finding takes a Signal's firm reason only when the Signal's own `billing.reason` evidence names the finding's reason code (`ReasonCodesOf`, an allow-list of `ref` values of that kind) and `BillingRuleCatalogue.IsRejectCapableReason` knows the code for the rule type. Reads `sprk_evidencerefs`. The Signals stay gated by `WriterCreatedRecord`, the version and the line. Decided items (Q3) are unchanged: they read only the Signals the decision resolved. |
| `Services/Finance/FirmRejectionFindings.cs` | `ShownFirmReason` uses the shared predicate from `FirmDescriptionRules`. |
| `Services/Finance/RuleSetService.cs` | Edit: `firmDescription` (null leaves it, blank clears it, trimmed). C2: a change on a published working version is 409 `ruleset.firm_description_frozen`, nothing written. Validation (edit and publish) through `ValidateRule(..., ruleSetName)`: each `Validate` refusal is an error with field `firmDescription` and its code; a Duplicate rule with a description is `firm_description_duplicate`. An `Invalid` outcome with any `firmDescription` error is **422** (Q4), otherwise 400 as before. A new draft (other edit on a published version) and the copy forward carry the text. The read model has `firmDescription`. |
| `Services/Finance/Procedures/ProcedureAssignmentService.cs` | A Procedure change copies the base version's firm description onto the new version. |
| `Api/Finance/RuleSetEndpoints.cs`, `ProcedureEndpoints.cs`, `ProcedureAssignmentEndpoints.cs` | `RuleEditBody.FirmDescription`; the Invalid problem uses the outcome's status code. |
| `tests/Spaarke.ArchTests/ExternalInvoiceVisibilityGuardTests.cs` | `FirmFindingsProjection::NamesTheFindingsReason` listed as not a surface (a predicate over a row already read; reads nothing). |
| Existing tests (grandfathered) | Fixtures for the replay reads (`InvoiceSubmissionAfterStepsTests`, `InvoiceSubmissionRerunTests`, `InvoiceSubmissionProcessingFlowTests`, `InvoiceRuleSignalReconcilerTests`); projection tests write the Signal's reason evidence; four preflight rows; `partial` on three classes. No assertion was loosened: the exact-set assertions (`SignalWriteRefusal` declared vs tested codes, the refresh update keys) were extended by the new code / kept exact. |

## Conditions and the tests that prove them

Test classes: `SignalWriterTests` (unit/domain/Signals), `FirmDescriptionRulesTests` (unit/domain/Finance), `BillingFindingSignalMapperFirmReasonTests`
(unit/domain/Signals/Billing), `BillingFirmReasonReplayTests` (integration/data-mutation/Signals), `InvoiceSubmissionAfterStepsTests` (partial in
integration/data-mutation/FinanceSubmission), `RuleSetServiceTests` / `ProcedureAssignmentServiceTests` (partials in integration/data-mutation/Ebilling),
`RuleSetEndpointsTests` (partial in integration/contract/Finance), `FirmFindingsProjectionTests`, `FirmReasonWriteSitesGuardTests` (ArchTests).

| Condition | How it is met | Proved by |
|---|---|---|
| **C1** replay re-derives, never trusts the plan | The plan has no `firmReason`; `BillingFirmReasonReplay` reads the version and validates with its names; a planted plan value is overwritten. | `BillingFindingSignalMapperFirmReasonTests.ThePlan_NeverCarriesTheFirmReason`; `BillingFirmReasonReplayTests.TheVersionsText_ReplacesThePlans`, `NoStoredText_GivesNone`, `AnInvalidText_IsDropped`, `AVersionWithoutItsPolicy_DropsTheText`, `AGoneVersion_DropsTheText_AnotherFaultPropagates`, `AVersion_IsReadOncePerRun`; `InvoiceSubmissionAfterStepsTests.Replay_WritesTheVersionsText_NotThePlans`, `Replay_WithNoStoredText_WritesNone`, `Replay_DropsAnInvalidText_AndStillWritesTheSignal`, `Replay_ReadsOnlyWhatItNeeds` |
| **C2** a published version's description cannot change | 409 `ruleset.firm_description_frozen`, nothing written (must fire); a draft takes it (must not fire); other edits and the copy forward carry it. Published predicate: `VersionRow.IsDraft` (see limits). | `RuleSetServiceTests.Published_FirmDescriptionChange_IsRefused_AndWritesNothing`, `Draft_TakesTheFirmDescription_InPlace`, `Draft_BlankClears_AndTheSameTextIsNoChange`, `Published_OtherEdit_CarriesTheFirmDescriptionToTheNewVersion`, `CopyForward_CarriesTheFirmDescription`, `AnInactiveDraft_IsNotRead`; `ProcedureAssignmentServiceTests.Change_CopiesTheBaseFirmDescription`, `Change_OfABaseWithoutAFirmDescription_WritesNone`; `RuleSetEndpointsTests.Edit_APublishedVersionsFirmDescription_Is409` |
| **C3** the preflight never drops a mapper Signal | The mapper sends only validated text (or null); the preflight `firm_reason_invalid` fires only for a direct caller, before any Dataverse call. | `BillingFindingSignalMapperFirmReasonTests.AnInvalidDescription_IsDropped_AndChangesNothingElse`, `AnInvalidDescription_NeverTakesTheHeldFallback`; `SignalWriterTests.InvalidFirmReason_IsRefused_BeforeAnyDataverseCall`, `ValidFirmReason_AtTheBound_PassesThePreflight`, the four `firm reason ...` rows of `Preflight_RefusedRequest_GetsItsCode_AndWriteAsyncStillThrowsExactlyAsBefore`; `FirmDescriptionRulesTests.ThePreflightAndTheMapper_RefuseTheSameInputs` |
| **C4** the name guard at copy time | `Validate(raw, ruleSetName, policyName)` in the mapper and the replay; case-insensitive; dropped like any invalid text; 422 at publish stays. | `FirmDescriptionRulesTests.NamingTheRule_IsRefused`, `NotNamingTheRule_IsAccepted`; `BillingFindingSignalMapperFirmReasonTests.AnInvalidDescription_IsDropped_AndChangesNothingElse` (name-bearing rows); `InvoiceSubmissionAfterStepsTests.Replay_DropsAnInvalidText_AndStillWritesTheSignal` (name-bearing rows); `RuleSetServiceTests.Publish_ANameBearingFirmDescription_IsRefused422` |
| **O1** FirmReason never changes Signal identity | Not in the dedupe key, the fingerprint, the sentence, the headline or the snapshot; the refresh sends `sprk_lastevaluated` only (plus `sprk_duedate` on an open Signal whose subject date changed, as before; a matter subject has none, so the test pins the exact set `{sprk_lastevaluated}`). | `SignalWriterTests.FirmReason_IsNotInTheKey_NorTheSentence`, `Refresh_NeverSendsTheFirmReason`, `Refresh_UnderANewerVersion_KeepsTheStoredTextAndVersion`; `BillingFindingSignalMapperFirmReasonTests.TheFirmReason_IsInNoKeyOrSentence` |
| **O2** the refusal is bypass detection | Code `firm_reason_invalid`; the message names the rule code, never the text; the preflight-fallback request carries no `FirmReason`. | `SignalWriterTests.InvalidFirmReason_IsRefused_BeforeAnyDataverseCall`; `BillingFindingSignalMapperFirmReasonTests.TheHeldFallbackRequest_CarriesNoFirmReason`, `AnInvalidDescription_NeverTakesTheHeldFallback`; `SignalWriterTests.Preflight_TheCodes_AreDistinct_AndStable` (exact declared/tested set) |
| **O3** only the mapper and the replay set FirmReason | ArchTest: `FirmReason =` in code only in `BillingFindingSignalMapper.cs` and `BillingFirmReasonReplay.cs`; `"sprk_firmreason"` in `SignalWriter.cs` code only inside `BuildEntity`; no other BFF file writes the column. Must-fire and must-not-fire controls. | `FirmReasonWriteSitesGuardTests.FirmReason_IsAssignedOnlyByTheMapperAndTheReplay`, `SignalWriter_NamesTheColumnOnlyInBuildEntity`, `NoOtherFile_WritesTheColumn`, `Control_AnAssignmentElsewhere_IsReported`, `Control_AllowedShapes_AreNotReported`, `Control_TheColumnOutsideBuildEntity_IsReported`, `Control_TheColumnInBuildEntityOrAComment_IsNotReported`, `Control_ColumnWritesElsewhere`; `SignalWriterTests.FirmReason_IsWrittenOnTheCreate`, `NoFirmReason_SendsNoKey` |
| **O4** schema by the ontology recipe | The script above. | Pester `Deploy-EbillingFirmDescriptionColumn.Tests.ps1` (20); `ScriptMetadataStrongConsistencyGuardTests` |
| **O5** unsecured | The script creates it unsecured; `-Verify` fails a secured column; C2 applies to the BFF path. | Pester `names a field-secured column (EB-D-213: unsecured)`, `is a Memo named as the BFF reads it ...` |
| **O6** scoped ontology review | Not this task: `SignalWriter.cs` and `Services/Signals/**` changed here go to the ontology review before merge. | — |
| Q2 code-matched override | See the projection row above. | `FirmFindingsProjectionTests.Rejected_TheOverride_ShowsForTheFindingsOwnReasonCode` (must fire), `Rejected_TheOverride_NeverShowsForASignalThatDoesNotNameTheFindingsCode` (another / unknown / no code, another kind, malformed, not an array), `Rejected_ASignalNamingSeveralCodes_OverridesOnlyTheFindingsItNames`, `ReasonCodesOf_IsAnAllowListOfBillingReasonRefs`, `Rejected_ADuplicate_NeverTakesTheOverride_ANonDuplicateDoes`; `SignalWriterTests.WrittenEvidence_IsReadBackByTheProjectionsReasonCodeReader` |
| Q3 decided items | Only the Signals the decision resolved. | `FirmFindingsProjectionTests.DecidedItems_TakeTheFirmReasonFromTheResolvedSignalsOnly` |
| Q4 publish guard | 422 `ruleset.publish_invalid`, field `firmDescription`, code `firm_description_names_rule`; the text is never echoed. | `RuleSetServiceTests.Publish_ANameBearingFirmDescription_IsRefused422`, `Publish_OtherErrorsStay400_AndACleanDescriptionPublishes`, `Draft_InvalidFirmDescription_IsRefused422`, `Duplicate_TakesNoFirmDescription`; `RuleSetEndpointsTests.Publish_ARefusedFirmDescription_Is422`, `Edit_ARefusedFirmDescription_Is422` |

### Coordinator checks
1. **FirmReason is written on create only:** `SignalWriterTests.FirmReason_IsWrittenOnTheCreate`, `Refresh_NeverSendsTheFirmReason` (exact key set `{sprk_lastevaluated}` for Open and Resolved), `Refresh_UnderANewerVersion_KeepsTheStoredTextAndVersion`; ArchTest `SignalWriter_NamesTheColumnOnlyInBuildEntity`.
2. **`firm_reason_invalid` fails closed with no partial write:** `SignalWriterTests.InvalidFirmReason_IsRefused_BeforeAnyDataverseCall` (no create, no update, no retrieve, no rows), and the shared preflight table rows.
3. **Exact-set tests updated, never loosened:** `Preflight_TheCodes_AreDistinct_AndStable` (declared == tested, the new code added to both); `Refresh_NeverSendsTheFirmReason` uses `BeEquivalentTo` on the key set; `FirmReason_IsNotInTheKey_NorTheSentence` asserts the payload difference is exactly `["sprk_firmreason"]`.
4. **Reason codes against `BillingRuleCatalogue`; description by `FirmDescriptionRules`:** `FirmFindingsProjectionTests.Rejected_TheOverride_NeverShowsForASignalThatDoesNotNameTheFindingsCode` (unknown code row), `ReasonCodesOf_IsAnAllowListOfBillingReasonRefs`; `FirmDescriptionRulesTests.*`.
5. **One predicate on every path:** the preflight (`Validate(raw)`), the mapper and the replay (`Validate(raw, names)`) and the projection (`ShownFirmReason`, through `IsInvisibleControl`) share `FirmDescriptionRules`. `FirmDescriptionRulesTests.ThePreflightAndTheMapper_RefuseTheSameInputs` pins that the preflight and the mapper refuse the same inputs; `APreflightRefusedRune_IsAProjectionDroppedRune` pins that every rune the preflight refuses is one the projection drops.
6. **200-rune bound in runes:** `FirmDescriptionRulesTests.TheBound_IsTwoHundredRunes` (200 accepted, 201 refused, for `x`, `é`, a CJK character and two surrogate-pair emoji); `SignalWriterTests.ValidFirmReason_AtTheBound_PassesThePreflight` (200 emoji).
7. **Each refused class:** `BracesAndMarkup_AreRefused` (`{ } < >`), `InvisibleRunes_AreRefused` (Control incl. NUL, bell, tab, line feed, NEL and DEL; Format incl. U+061C, U+FEFF, U+200B, U+202E, U+2066, U+00AD and a tag character outside the BMP; LineSeparator U+2028; ParagraphSeparator U+2029), `OrdinaryText_IsAccepted`.
8. **C4 self-reference drop:** `NamingTheRule_IsRefused` (rule set and policy names, any case), `NotNamingTheRule_IsAccepted`; mapper and replay rows above; `TheMappersDrop_LogsTheCodeOnly`.

### Fail-before checks
The new tests use members the old code does not have (`FirmReason`, `FirmDescriptionRules`, `ReasonCodesOf`, `BillingFirmReasonReplay`), so against the
files at `4e26b3000f` they do not compile; that alone proves little. Instead, each behaviour was checked by putting the old behaviour back in the
changed file and running the tests. Method: copy each production file to the scratchpad, apply the reversion below, run, then copy the saved file back
(byte-compared after; no `git stash`). All eight reversions were applied together, in one run (2026-11-16):

| # | Reversion (old behaviour) | Tests that failed |
|---|---|---|
| M1 | `SignalWriter.BuildEntity` does not write `sprk_firmreason` | `FirmReason_IsWrittenOnTheCreate`, `FirmReason_IsNotInTheKey_NorTheSentence` |
| M2 | `CheckRequest` does not refuse an invalid `FirmReason` | 4 preflight table rows, 9 `InvalidFirmReason_IsRefused_BeforeAnyDataverseCall` rows, `ThePreflightAndTheMapper_RefuseTheSameInputs` and `APreflightRefusedRune_IsAProjectionDroppedRune` rows |
| M3 | the mapper copies the stored text unvalidated | 5 `AnInvalidDescription_IsDropped_AndChangesNothingElse` rows, `AnInvalidDescription_NeverTakesTheHeldFallback`, `TheMappersDrop_LogsTheCodeOnly`, `NoDescription_GivesNoFirmReason` rows |
| M4 | C2 block off (a published version takes the change) | `Published_FirmDescriptionChange_IsRefused_AndWritesNothing` |
| M5 | replay keeps the planned request (no re-derivation) | `TheVersionsText_ReplacesThePlans`, 3 `NoStoredText_GivesNone` rows, `AGoneVersion_DropsTheText_AnotherFaultPropagates`, `InvoiceSubmissionAfterStepsTests.Replay_WritesTheVersionsText_NotThePlans` |
| M6 | projection override not code-matched | 6 `Rejected_TheOverride_NeverShowsForASignalThatDoesNotNameTheFindingsCode` rows, `Rejected_ASignalNamingSeveralCodes_OverridesOnlyTheFindingsItNames` |
| M7 | a `firmDescription` error stays 400 | 5 `Draft_InvalidFirmDescription_IsRefused422` rows, `Publish_ANameBearingFirmDescription_IsRefused422`, `Duplicate_TakesNoFirmDescription`, `RuleSetEndpointsTests.Edit_ARefusedFirmDescription_Is422`, `Publish_ARefusedFirmDescription_Is422` |
| M8 | the bound counts UTF-16 units, not runes | `TheBound_IsTwoHundredRunes` (both surrogate-pair emoji rows), the 200-emoji row of `ThePreflightAndTheMapper_RefuseTheSameInputs` |

Totals under the reversions: 79 failed of the 718 tests matched by the first filter; 1 failed of 6 replay rows of `InvoiceSubmissionAfterStepsTests`.
After the files were restored, everything passed again (Verification). The O1 refresh tests (`Refresh_NeverSendsTheFirmReason`,
`Refresh_UnderANewerVersion_KeepsTheStoredTextAndVersion`) pin behaviour that 130e-1 must NOT change, so they pass before and after by design;
the ArchTest guard proves itself with its must-fire controls.


## The 130e-2 contract (editor)

- **Read:** `GET /api/finance/rulesets/{ruleSetId}` returns `proposals[].firmDescription` (string or null) for each rule's working version.
- **Edit:** `PATCH /api/finance/rulesets/{ruleSetId}/proposals/{policyId}` with body member `firmDescription`:
  - absent or `null` leaves it as it is; `""` or blank clears it; the text is trimmed before it is stored;
  - accepted only on a draft working version.
- **Refusals:**
  - `409` `errorCode` `ruleset.firm_description_frozen`: the working version is published. To change it, make another edit first (any other member creates a new draft), then set the description on that draft.
  - `422` `errorCode` `ruleset.invalid` (edit) or `ruleset.publish_invalid` (publish), with `errors[]` entries whose `field` is `"firmDescription"` and whose `reason` is one of `firm_description_too_long`, `firm_description_braces`, `firm_description_markup`, `firm_description_invisible_character`, `firm_description_names_rule`, `firm_description_duplicate`. The text is never echoed. Any other invalid edit stays `400`.
- **Editor rules to mirror client-side** (the server is authoritative): at most 200 characters counted as Unicode scalars; no `{ } < >`; no control, format or separator characters; does not contain the rule set's or the rule's name; not on a Duplicate rule.
- **Server codes that are not editor errors:** writer refusal `firm_reason_invalid` (a direct caller only); log code `EBILLING-FIRM-DESCRIPTION-DROPPED` with reason `firm_description_*`, `firm_description_names_unread` or `firm_description_row_gone` (replay).

## Verification

Run 2026-11-16 in the task worktree. No live environment call; the schema script was not run (Pester only).

| Check | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api --warnaserror --no-incremental` | 0 warnings, 0 errors |
| Affected suites: `tests/unit/Sprk.Bff.Api.Tests` filtered on `Signal`, `Billing`, `FirmFindings`, `FirmRejection`, `FirmDescription`, `RuleSet`, `ProcedureAssignment`, `InvoiceSubmission`, `InvoiceCorrection`, `InvoiceLineAdd`, `Procedure`, `InvoiceRuleSignal` | 9763 passed, 0 failed, 7 skipped (pre-existing skips); the same after the fail-before files were restored |
| `dotnet test tests/Spaarke.ArchTests` | 2643 passed, 0 failed (incl. `FirmReasonWriteSitesGuardTests`, `ScriptMetadataStrongConsistencyGuardTests`, `DraftPolicyVersionStatecodeGuardTests`, `ExternalInvoiceVisibilityGuardTests`) |
| Pester 5.7.1, `tests/scripts/Deploy-EbillingFirmDescriptionColumn.Tests.ps1` | 20 passed, 0 failed |
| Publish size (Release, zipped with `Compress-Archive`): branch tip `d7d7e2dcbd` vs fresh `origin/master` `ce6c241964`, each from its own short-path worktree | 42,339,790 vs 42,336,908 bytes (40.38 MB each); delta +2,882 bytes; 191 files each. Both worktrees removed with `Remove-WorktreeSafe.ps1`. The branch base (`4e26b3000f`) is behind that master, so the delta also holds master's own drift. |
| `dotnet list src/server/api/Sprk.Bff.Api package --vulnerable --include-transitive` | no vulnerable packages (no package changed) |

## Known limits

- **Deploy order:** the column must exist before the BFF build that carries 130e-1 is deployed (every policy version read selects it). Run the script's `-Apply` (owner's go) and `-Verify` first.
- **No backfill:** existing versions have no description; their Signals carry none (as today).
- **Only billing rule Signals carry a description.** System Signals and other policy domains never set `FirmReason`.
- **C2 is literal:** on a published working version the description cannot be the first change; another edit must create the draft first (contract above).
- **The change-override path validates the base description:** a base version whose stored text is invalid (for example set by a form edit) refuses the Procedure change with 422 until the text is fixed.
- **A form edit bypasses C2 and the editor checks (O5, accepted):** it can change a published version's text; this affects only Signals created afterwards, and the mapper, the replay and the projection re-validate.
- **Short names may over-refuse:** the C4 check is a case-insensitive containment check, so a very short rule set or policy name refuses any text containing it.
- **Replay read faults:** only a missing row drops the text; any other read fault fails the replay, as every other replay read does.
- **Published predicate:** C2 uses `VersionRow.IsDraft` (the rule set editor's own draft test). DEF-061's `VersionWasPublished` is for Procedure versions, not policy versions.
- **Decided items are not evidence-gated** (Q3, unchanged): they read the Signals the decision resolved.
