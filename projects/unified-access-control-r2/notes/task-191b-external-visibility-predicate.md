# Task 191b — the `ExternalVisibility` predicate (notes)

> Branch `task/uac-191b`, PR #2711. Rigor FULL. The owner decisions of 2026-11-11 (Q1–Q7) are applied as given; none was re-asked. The second-pass fixes (F1–F4) are the main session's decisions under the project's round-75 rule.

## What landed

| File | Change |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Access/SecureChildLineage.cs` | Every `Table` must declare an `ExternalMarker`: a null one throws, and leaving it out does not compile. New marker kind `HardDeny`. New members: `OutsideLineage` (`sprk_affinity` = Never), `ExternalMarkerOf`, `NeverExternalTables`, `DocumentTextTables`. |
| `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ExternalVisibility.cs` (new) | THE predicate. `DecideAsync(rows, Readers, ct)` answers Visible, Hidden or Undecided for each row. Each row's own rule is chosen from its table's declared marker (`RuleTables`). It adds no DI, endpoint or package; its callers come in 191c, 191g and 191h. |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/Dataverse/ExternalCallerCommunicationGuard.cs` | Its two table lists are now derived from the registry markers. Their contents and order are unchanged, and tests pin them. |
| `tests/Spaarke.ArchTests/ExternalDocumentNarrowingGuardTests.cs` | New guard: no second map from table to internal-only. It has must-fire and must-not-fire controls and a reasoned baseline of 6. The predicate is added to the 188 seam allow-list. |
| `tests/Spaarke.ArchTests/ExternalLookupAbsentReadGuardTests.cs`, `ExternalInvoiceVisibilityGuardTests.cs` | Reviewed entries for the predicate's lookup read and its (a′) call. |
| `tests/unit/...` | New `ExternalVisibilityTests`. New marker and hard-deny pins in `SecureChildLineageTests`. |

**Pre-existing failure, not changed here.** `ChildMirrorMask_OnEveryHardDenyTable_CarriesReadOnly` already fails on master, because #2689 added three hard-deny lineage children. Ebilling PR #2708 fixes it, so 191b leaves that test as it is on master.

## Marker assignment (36 lineage tables, plus 1 outside the lineage)

| Marker | Tables |
|---|---|
| `Predicate(Communication065)` | communication |
| `Predicate(ThreadOpen)` | communicationthread |
| `Predicate(DocumentNarrowing)` | document |
| `Predicate(InvoiceSource)` | invoice |
| `Never` | emailartifact, attachmentartifact, emailreviewlog, and `sprk_affinity` (outside the lineage). This set is also the AI guard's refused list and message, unchanged. |
| `HardDeny` | policy, policyversion, procedureassignment, ruleset, storedfile (`RecordOwnershipResolver.RestrictedContentTables`, design §0 item 1). Always Hidden, and so is any row filed under one. They are kept out of the Never set. A pin test stops a new hard-deny table from arriving with marker None. |
| `DocumentText` | analysis, analysisoutput. These carry document text and are tainted through the walk. |
| `None` | every other table |

## How the rules are applied

1. **Document chain.** A document reached through `sprk_parentdocument` (a chain member) contributes everything except its own 188 verdict. All of its lookups, its communication link included, are lineage edges.
   - So an attachment under the .eml of a hidden communication is Hidden (design §3.3 rule 3, second-pass F1). This is stricter than 188's first-link answer.
   - The 3-hop bound still holds. Documents up to 3 hops below a hidden communication are Hidden. The document 4 hops below is Undecided: its chain is past the bound, so the walk never reaches the communication.
2. **Cycles.** A cycle is decided by the rows on it. A document and its current version name each other, so every versioned document is on a cycle. A document-chain cycle is Undecided through 188's `Cycle`. A closure that reaches no root, including a cycle with no root, is Undecided (`NoRoot`).
   - The POML criterion "a cycle … answers Undecided, never Visible" is amended to: **a cycle is decided by the rows on it; a cycle that reaches no root, an over-deep chain or a failed parent read answers Undecided, never Visible.**
   - Design §3.3 is amended to match, in `notes/2179-flagged-external-dataverse-design.md`.
3. **Faults.**
   - A read that throws makes every row Undecided (`ReadFailed`).
   - A rule that reports a failed read without throwing (188, (a′)) keeps the rows already Hidden by a sound rule (second-pass F2). Every other row becomes `ReadFailed`.
   - In both cases `Faulted` is true.
4. **Own rules come from the declared marker** (second-pass F4).
   - `RuleTables` names the one table each rule's implementation reads.
   - A rule declared on any other table faults the decision, as does a rule with no step. Neither is ever skipped.
   - A pin test checks that the registry's declared (table, rule) pairs equal `RuleTables`.

## Review findings and disposition

**First adversarial pass**

| # | Finding | Class | Disposition |
|---|---|---|---|
| 1 | Two existing ArchTest guards failed on the new file | F4 | Fixed: added reviewed entries. |
| 2 | A closure that reaches no root answered Visible | F1 (latent) | Fixed: answers `NoRoot` (Undecided). |
| 3 | A rule that reported a failed read did not set `Faulted` | F4 | Fixed: see Faults above. |
| 4 | The "no second map" ArchTest has a narrow view | K4 | Recorded as K10. |
| 5 | Hard-deny tables answered by lineage | K | Superseded by second-pass F3. |

**Second adversarial pass**

| # | Finding | Class | Disposition |
|---|---|---|---|
| F1 | A document under a hidden parent document answered Visible | F1 | Fixed: removed the chain-member link skip. `AnAttachmentUnderTheEmlOfAHiddenCommunication_IsHidden`. |
| F2 | A fault discarded Hidden answers that were already sound | F | Fixed: Hidden rows are kept. `AHiddenRow_StaysHidden_WhenAnotherRuleAnswersFaulted`. |
| F3 | Hard-deny tables were marked None | F | Fixed: new `HardDeny` marker. `AHardDenyTable_IsHidden_UnderAStandardRoot`, `EveryHardDenyTableInTheRegistry_IsNeverExternallyVisible`. |
| F4 | `ExternalMarker.Rule` was never read | F | Fixed: dispatch on the declared rule. `TheDeclaredOwnRules_AreExactlyTheOnesThePredicateImplements`. |

**Mutation checks.**
- **First run:** 21 single-point mutations, covering every rule leg, every fail-closed branch, both bounds, the combine order, `NoRoot` and the fault flags. After two test additions (`AHiddenFinding_BeatsAnEarlierUndecidedOne` and `TheClosureBoundsTheDocumentChain_EvenWhenTheSeamSaysVisible`), every mutation failed at least one test.
- **Second-pass spot check:** see the PR comment.

## Known limits (K-class)

- **K1** The filing parents of roots are not walked for taint. Restricted through filing is still covered by `EffectiveRootFlagReader`.
- **K2** Only the first path to each (row, chain run) state is walked.
- **K3** A registry child whose closure reaches no root answers Undecided (`NoRoot`), never Visible.
- **K4** If `ProcedureLibraryDriveId` is not set, no document is treated as a Procedure Library document.
- **K5** An event has no lookup to a document, so "an event regarding a document" cannot be represented.
- **K6** The Secure and Inactive root flags are not part of the predicate (owner Q4).
- **K7** There is no cache.
- **K8** The split between Hidden and Undecided is for reporting only. See the 191g handoff for single-record callers.
- **K9** (corrected) The five hard-deny tables are `HardDeny`: always Hidden. The external role also holds no privilege on them.
- **K10** The "no second map" ArchTest only finds the Never table names loaded as strings in a static constructor.
- (K11 is removed: a document whose only root is through its parent document's communication link now gets that root.)

## Handoffs

**191c (share job)**
- Bind `ExternalVisibility.Readers` to the external plane's app-only readers.
- Add the DI type that resolves `AppOnlyDocumentNarrowing` to the 188 seam allow-list ArchTest.
- The (a′) readers that take no caller are private in `ExternalDataService`; 191c needs an entry point for them.
- Bind `Finance:ProcedureLibraryContainerId`.
- When a row is Undecided or the batch is `Faulted`, follow design §6: no grant, revoke any existing team or flagged-user share, and carry the row to retry.
- `DirectThreadAccessService` still grants `sprk_emailreviewlog` to direct-message participants. That table is Never here, so the share job must not depend on that grant, and that grant path needs its own decision.

**191g (Office edit) and any single-record caller**
- Refuse unless the verdict is Visible.
- Hidden and Undecided can be told apart: an id that does not exist answers `RowNotFound`. Map every non-Visible answer to the same response, so the endpoint never reveals whether a record exists.

**191h (SPA lists, AI tools)**
- Moving the 188 surfaces onto the predicate makes document results stricter: the taint closure, attachments of hidden .eml files, and `NoRoot`. The design says "unchanged result", so this needs an owner decision.
- Rows shared outside the root model (for example direct-message participant shares) answer `NoRoot` when they have no root. Such a surface keeps its own gate.

## Gates

Test counts, the publish-size delta and the CVE result are in the PR body and its comments.
