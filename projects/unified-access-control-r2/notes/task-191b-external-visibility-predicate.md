# Task 191b — the `ExternalVisibility` predicate (notes)

> Branch `task/uac-191b`. Rigor FULL. Owner decisions 2026-11-11 (Q1–Q7) applied as given; none re-asked.

## What landed

| File | Change |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Access/SecureChildLineage.cs` | Every `Table` declares a required `ExternalMarker` (null throws; omitting it does not compile). `OutsideLineage` (`sprk_affinity` = Never), `ExternalMarkerOf`, `NeverExternalTables`, `DocumentTextTables`. |
| `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ExternalVisibility.cs` (new) | THE predicate: `DecideAsync(rows, Readers, ct)` → Visible / Hidden / Undecided per row. No DI, no endpoint, no package; its callers come in 191c/191g/191h. |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/Dataverse/ExternalCallerCommunicationGuard.cs` | Its two table lists now derive from the registry markers. Contents unchanged (pinned by tests). |
| `tests/Spaarke.ArchTests/ExternalDocumentNarrowingGuardTests.cs` | "No second table → internal-only map" guard (must-fire / must-not-fire controls, reasoned baseline of 6), predicate added to the 188 seam allow-list. |
| `tests/Spaarke.ArchTests/ExternalLookupAbsentReadGuardTests.cs`, `ExternalInvoiceVisibilityGuardTests.cs` | Reviewed entries for the predicate's lookup read and its (a′) call (review finding 1). |
| `tests/unit/...` | `ExternalVisibilityTests` (new), `SecureChildLineageTests` (marker pins; stale hard-deny pin corrected — it had been failing on master since ebilling 089a). |

## Marker assignment (36 lineage tables + 1 outside)

- `Predicate(Communication065)`: communication. `Predicate(ThreadOpen)`: communicationthread. `Predicate(DocumentNarrowing)`: document. `Predicate(InvoiceSource)`: invoice.
- `Never`: emailartifact, attachmentartifact, emailreviewlog (and `sprk_affinity`, outside the lineage).
- `DocumentText`: analysis, analysisoutput (carry document text; taint through the walk).
- `None`: every other table, including the five hard-deny tables (per the POML; see K9).

## Deviations from the POML wording (and why)

1. **A non-document lineage path cycle is skipped**, as the secure-child lineage walk does; the AC "a cycle → Undecided" holds for the document chain through 188's `Cycle`, and (after review finding 2) any closure that reaches no root — a rootless cycle included — is Undecided (`NoRoot`).
2. **Chain-member documents** (reached through `sprk_parentdocument`) contribute everything except their own 188 verdict and their own communication link. This keeps 188's first-link rule and its 3-hop bound unchanged, and is what makes "4 hops → Undecided" hold.
3. **The taint closure is stricter than 188 alone**: it adds the roots of a document's parent, its other related links and its communication's lineage.

## Review findings and disposition

| # | Finding | Class | Disposition |
|---|---|---|---|
| 1 | Two existing ArchTest guards failed on the new file (lookup-absent read, invoice-family surface) | F4 | Fixed: reviewed entries added with reasons. |
| 2 | A closure that reaches no root (parentless row, rootless cycle, a service-request core) answered Visible | F1 (latent, no caller yet) | Fixed: `NoRoot` → Undecided. Tests added. |
| 3 | A rule that answers faulted (not throwing) made only its rows Undecided; `Faulted` stayed false | F4 | Fixed: a faulted 188 or (a′) answer faults the whole decision, as a throw does. Tests added. |
| 4 | The "no second map" ArchTest sees only static-constructor string loads of the Never tables | K4 | Recorded below (K10). |
| 5 | Hard-deny tables answer by lineage (marker None) | K | Recorded (K9); 191h decides before wiring reads. |
| 6 | Notes missing from the commit | process | This file. TASK-INDEX is owned by the main session. |
| — | Mutation run: "Hidden beats Undecided" had no test that failed when broken | F4 | Fixed: `AHiddenFinding_BeatsAnEarlierUndecidedOne`. |
| — | Mutation run: the closure's own chain bound was masked by 188's (the real seam bounds the chain too) | F4 | Fixed: `TheClosureBoundsTheDocumentChain_EvenWhenTheSeamSaysVisible`. |

**Mutation check.** 21 single-point mutations of `ExternalVisibility.cs` were run against `ExternalVisibilityTests`. They cover every rule leg, every fail-closed branch, the chain and depth bounds, Combine order, `NoRoot` and the fault flags. After the two fixes above, each mutation fails at least one test. Four were first written as constant conditions, which the compiler rejects as unreachable code (CS0162), so they were re-run in a non-constant form.

## Known limits (K-class)

- **K1** The filing parents of roots (a root filed under an event or an invoice) are not taint-walked; root Restricted through filing is covered by `EffectiveRootFlagReader`.
- **K2** The path check keeps the first path per (row, chain run); a later path to the same state is not re-walked (closure over a finite set).
- **K3** (narrowed by finding 2) A registry child whose closure reaches no root is Undecided (`NoRoot`), never Visible.
- **K4** An unset `ProcedureLibraryDriveId` means no document is treated as a Procedure Library document.
- **K5** An event has no document regarding lookup, so "an event regarding a document" cannot be represented.
- **K6** The Secure and Inactive root flags are not part of the predicate (Secure is decided per user elsewhere; owner Q4).
- **K7** No cache: one decision per call, batched per level and table.
- **K8** The Hidden-versus-Undecided split of 188 and (a′) reasons is for reporting only; every caller treats both as not visible.
- **K9** The five hard-deny tables carry marker None per the POML and answer by lineage. The predicate must not be the only gate for them (the external role holds no privilege on them by design).
- **K10** The "no second map" ArchTest finds only static-constructor string loads of the Never table names; a map built elsewhere, or over other tables, is a reviewer's catch.
- **K11** A document whose only route to a root runs through a chain ancestor's communication link is Undecided (`NoRoot`): that link is 188's question, not the closure's. Fail-closed; pinned by a test.

## Handoffs

- **191c (share job):** bind `ExternalVisibility.Readers` from the external plane's app-only readers. A DI type that resolves `AppOnlyDocumentNarrowing` must be added to the 188 seam allow-list ArchTest. The caller-less (a′) reader set is private in `ExternalDataService` today, so 191c needs an entry point for it. Bind `Finance:ProcedureLibraryContainerId`. Undecided or `Faulted` → no grant, no revoke, carry the previous state. Measure the `NoRoot` count on dev before relying on attachments being shared (K11).
- **191g (Office edit):** refuse unless the verdict is Visible.
- **191h (SPA lists, AI tools):** moving 188 surfaces onto the predicate makes document results stricter (the taint closure, `NoRoot`); the design says "unchanged result", so this needs an owner call. Do not make the predicate the only gate for stored files or procedure content (K9). Rows shared outside the root model (for example direct-message participant shares) answer `NoRoot` when rootless; such a surface keeps its own gate.

## Gates

See the PR body for test counts, the publish-size delta and the CVE result.
