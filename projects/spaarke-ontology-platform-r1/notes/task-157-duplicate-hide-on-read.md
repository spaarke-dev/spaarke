# Task 157: a billing Duplicate's earlier-invoice detail hidden from a reader who cannot read that invoice (D-176, #2118)

> Branch `task/ontology-157-duplicate-hide` (worktree `C:\wts-157`), cut from `origin/docs/ontology-platform-design`, fast-forwarded to
> `c7659c6fe1` (the POML as amended after escalation 1, option B, plus the shared check for ebilling 119). Rigor **FULL** (security tag).
> ebilling reviews the range before merge.

## What changed

**Where.**
- `Services/Signals/EarlierInvoiceReadability.cs` (new): the earlier-invoice check, shared with ebilling task 119. Input: the caller's `IDataverseUserClient` and refs `(EarlierInvoiceId?, VendorOrgId?, TrackingNumber?)`. Output: the refs whose earlier invoice the caller's own `sprk_invoices` read returns. An id ref needs only the invoice read. A number-only ref is resolved through `sprk_invoicesubmissions` (vendor org + tracking number, read as the caller): exactly one row with an invoice, or not readable. Reads: one submission read per vendor org and one invoice read, In lists of at most 200. A 403 is a verdict (none of that table's rows). Anything else (401, 5xx, unparseable body, exception) is a fault: nothing readable. `public const WithheldSentence = "This matches an earlier submission."` mirrors `BillingRuleCatalogue.WithheldDuplicateTemplate` on `work/ebilling-r1`.
- `SignalReadService.cs`:
  - Step (9a), `DuplicateQuotesOf`, runs **before** the D-142 witness step and reads the stored evidence.
  - Step (9b), `ApplyDuplicateRuleAsync`, runs **after** the witness step.
  - Helpers: `DuplicatesShownAsync`, `CallerLookupAsync`, `WithheldDuplicateEvidence`, `StringOf`.
  - The constructor takes the new class.
- `Infrastructure/DI/SignalsModule.cs`: one line, `AddScoped<EarlierInvoiceReadability>()`, beside `SignalReadService`.

**Why detection runs before (9).** The D-142 step removes `text`/`source` from every line whose `ref` is not a `{table}:{id}` witness. That covers every billing line. So the `duplicateOf.invoiceId` / `duplicateOf.trackingNumber` texts must be taken from the stored evidence first. The shaping still runs after (9), and both rules keep. **Finding:** on the route before this task, a billing Duplicate's evidence `text` was already withheld by D-142. The detail that leaked was the `sprk_sentence` ("Invoice X duplicates INV-77.1.") and the `duplicateOf.*` lines' refs.

**Detection (POML D-176).** A row is a Duplicate when its parsed stored evidence has a line with `kind` = `billing.reference` and `ref` = `duplicateOf.invoiceId` or `duplicateOf.trackingNumber`. Property names are matched case-insensitively (as `ParseEvidence`), and so are the kind and ref values. No other row is touched or causes a read.

**Resolution.**
- **Earlier invoice:** each `duplicateOf.invoiceId` text that parses as a GUID. If there is none, the tracking numbers are used, within the CURRENT invoice's vendor org.
- **Current invoice:**
  - subject `sprk_invoice` → the subject id;
  - subject `sprk_billingevent` → the event's `_sprk_invoice_value`, from one batched caller read of `sprk_billingevents`;
  - any other subject, an event or invoice the caller cannot read, or one with no invoice → withheld.
- **Vendor org:** `_sprk_vendororg_value` from one batched caller read of `sprk_invoices`.
- A row is kept only when the caller reads every earlier invoice it quotes.

**Withheld shape (ebilling write time).**
1. Every evidence line whose `ref` starts with `duplicateOf.` is removed.
2. Every remaining line loses `text` and `source` and gets `"withheld": true`. This includes the detail-free reason-set line.
3. Sentence = `EarlierInvoiceReadability.WithheldSentence`.
4. Title is unchanged.

A fault anywhere in the step withholds every Duplicate row on the page. Non-Duplicate rows and the list are unchanged.

## Live names (spaarkedev1, read-only GETs, 2026-10-21)

| Table | Entity set | Key | Columns used |
|---|---|---|---|
| `sprk_invoice` | `sprk_invoices` | `sprk_invoiceid` | `_sprk_vendororg_value` (lookup → sprk_organization). No `sprk_trackingnumber` column on dev |
| `sprk_invoicesubmission` | `sprk_invoicesubmissions` | `sprk_invoicesubmissionid` | `sprk_trackingnumber` (String), `_sprk_vendororg_value`, `_sprk_invoice_value`. Alternate key `sprk_invoicesubmission_firm_trackingnumber` = (`sprk_trackingnumber`, `sprk_vendororg`), **Active** |
| `sprk_billingevent` | `sprk_billingevents` | `sprk_billingeventid` | `_sprk_invoice_value` (it also has its own `sprk_vendororg`, unused per option B) |
| `sprk_recordtype_ref` | | | Active rows for `sprk_invoice` (`8e4a04f1-…`) and `sprk_billingevent` (`fb30d221-…`), so both subject types resolve through the catalog |

No Signal on dev has `duplicateOf` in its evidence, so there is no live data to prove against.

## Title (`sprk_name`) finding

Title cannot carry the earlier invoice, so it is left unchanged. ebilling's headline comes from one of:
- `BillingRuleCatalogue.ReasonHeadline` (static constants);
- the policy version's `ShortHeadline`;
- `BillingRuleDraftTranslator.Wording(...).Headline`;
- `HeldHeadline`.

None of them takes a token or fact (`BillingFindingSignalMapper.cs:342-351` on `work/ebilling-r1`). The Held sentence (`HeldSentenceTemplate` with the rendered reason) can name the earlier number, but it is the sentence, which is replaced.

## Deviations and accepted differences

- **`billing.reason` text:** a withheld `billing.reason` line here keeps no text. ebilling's write-time shape carries the withheld template text there. This difference was accepted by the main session in the POML amendment.
- **Class is `public sealed`, not `internal sealed`.** `SignalReadService` is public with a public constructor, and Microsoft DI uses public constructors only. An internal parameter type is CS0051. Its helpers (`ReadAsCallerAsync`, `ReadGuid`, the column consts) stay internal.
- **The withheld-sentence const lives on the class**, as the shared-check constraint says. That supersedes withheld-shape item (3), "private const in SignalReadService".
- **`ReadAsCallerAsync` is shared:** `SignalReadService`'s billing-event and current-invoice reads use the class's `ReadAsCallerAsync`, so all D-176 reads share one 403/401/fault rule.

## Known limits

- **Surfaces not covered (POML):**
  - The native model-driven `sprk_signal` form and views read `sprk_sentence` and `sprk_evidencerefs` directly. This BFF check does not cover them.
  - ebilling's own invoice-review surfaces must apply the same rule; ebilling task 119 is to use `EarlierInvoiceReadability`.
- **Rate cards (agreed with ebilling 2026-10-21):** ApprovedRate and Discount rate-card quotes are inside the family. They are not touched.
- **Evidence that is not a JSON array**, or has colliding property names, cannot be parsed. Such a row is not detected as a Duplicate, so its sentence is returned as stored. (9) still returns its evidence as null. SignalWriter never writes such evidence (K4).
- **Pairing ids with numbers:** when a row carries at least one `duplicateOf.invoiceId` GUID, its tracking numbers are not resolved. ebilling writes the id beside the number of the same prior; it omits the id only for `DuplicateOfEarlierLine` and withheld findings. A merged row mixing an id-bearing prior with a different, id-less number does not occur under ebilling's merge-by-subject (K4).
- **Inactive submissions** are not filtered (the POML names no state filter). A withdrawn submission still counts toward "exactly one".

## Tests

New tests, all in the task's scope:
- **`SignalReadRouteTests`, section "D-176 (task 157)": 9 methods, 12 cases.**
  - `D176_ADuplicateWhoseEarlierInvoiceTheCallerReads_KeepsItsSentenceAndEvidence`
  - `D176_ADuplicateWhoseEarlierInvoiceTheCallerCannotRead_IsWithheld_InEbillingsWriteTimeShape`
  - `D176_ATrackingNumberOnly_IsResolvedThroughTheOneSubmissionOfTheCurrentInvoicesVendorOrg`: readable / unreadable / zero / two / an unreadable submission. **Beyond the criteria:** a matter subject → withheld, because it is the POML constraint "any other subject type with only a tracking number withholds".
  - `D176_WhenTheEarlierInvoiceReadFaults_EveryDuplicateOnThePageIsWithheld_AndNoOtherRowChanges`: theory over 500 / 401 / unparseable / throw.
  - `D176_WhenTheCallerHasNoReadOnTheInvoiceTable_TheDuplicateIsWithheld_AndNothingIsLoggedAsAFault`: 403.
  - `D176_ARowThatIsNotADuplicate_IsUnchanged_AndCausesNoInvoiceSubmissionOrBillingEventRead`
  - `D176_ALineLevelRepeatOfAnEarlierLine_IsKeptWhenTheCallerReadsItsEventAndInvoice_AndWithheldWhenNotTheEvent`
  - `D176_ALineLevelRepeatOfAPriorSubmissionLine_WithAnInvoiceId_IsJudgedByThatInvoice`
  - `D176_ReadsAreBatched_OneOfEachPerRequest_AndOneSubmissionReadPerVendorOrg_InListsOfAtMost200Ids`
- **Fake world additions (test-only):**
  - catalog entry `sprk_billingevent`;
  - `Invoice` / `BillingEvent` / `Submission` builders;
  - `PathFaults`, which fails one read of a table while another read of the same table succeeds;
  - a submission branch that evaluates the vendor org and the tracking-number In list.

  The bodies of the existing tests are unedited.
- **`EarlierInvoiceReadabilityTests` (unit, new): 8 methods, 15 cases.**
  - id readable/unreadable;
  - the number resolution cases;
  - **beyond the criteria:** `ATrackingNumberIsSentAsAnEscapedODataLiteral`, because the number is firm-supplied text in a filter;
  - neither id nor number → no read;
  - a fault in either read (500 / 401 / unparseable / throw) → nothing readable;
  - 403 on submissions (numbers unresolved, ids still judged) and on invoices (none);
  - bounded reads.

**Results:**

| Suite | Result |
|---|---|
| `SignalReadRouteTests` | 81/81 (69 existing + 12 new) |
| Unit `Sprk.Bff.Api.Tests.Services.Signals` | 382/382 |
| ArchTests | 1093/1093 (full) |
| BFF build with `-warnaserror` | 0 warnings |

**Failed before / pass after:** with the pre-change `SignalReadService.cs`, 11 of the 12 new route cases fail. The one that passes is the "not a Duplicate → unchanged" case, as it should.

**Seeding proofs:**
- **(a)** `EarlierInvoiceReadability`'s invoice check forced to `true` → `D176_ADuplicateWhoseEarlierInvoiceTheCallerCannotRead_…` and the unit `AnInvoiceId_IsReadable_…` fail.
- **(b)** A fault in the invoice read returns every ref readable → all 4 cases of `D176_WhenTheEarlierInvoiceReadFaults_…` fail.

Both were restored and the D176 tests rerun: 12/12.

## BFF hygiene (CLAUDE.md §10)

- **Placement:** in the BFF, inside the existing Signals read path. No new route, package or background work. One new scoped service, which is §11-justified in the POML and in its remarks.
- **Packages:** none added, so no new CVE surface.
- **Publish-size:** Measured 2026-10-21 from fresh detached worktrees at short paths, all with the same 192 files. Convention: `dotnet publish -c Release <wt>/src/server/api/Sprk.Bff.Api/ -o <wt>/deploy/api-publish/` (the canonical command; framework-dependent, project-default RID), Release, `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`, **PDBs included**.

  | Side | Commit | Zip |
  |---|---|---|
  | master (`origin/master`) | `f1b3dadcf5` | 37.677 MB |
  | parent (`origin/docs/ontology-platform-design`) | `6edfdb6d6b` | 37.332 MB |
  | this branch | `bdc4178cc5` | 37.341 MB |

  **This task's delta is +0.009 MB** (branch vs its parent). Against master it shows -0.336 MB. That difference is the parent branch being behind master, not this change. Well under +5 MB and the 55/60 MB thresholds. The worktrees were removed with `Remove-WorktreeSafe.ps1`.

  BFF Hygiene §10 + NFR-01: publish size 37.341 MB, delta +0.009 MB, no new package and no new HIGH CVE.

## Other findings (not in scope, not changed)

- `dotnet format --verify-no-changes` reports `IMPORTS` ordering in `Infrastructure/DI/SignalsModule.cs`. These are pre-existing `using` lines; this task added three body lines only.
- `dotnet format` also reports three `WHITESPACE` findings in pre-existing lines of `SignalReadRouteTests.cs` (2161, 2215-2216; from `147bc993a7f` and `8024c9b922b`).

rework: none task 157
