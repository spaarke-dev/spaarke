# Task 157: a billing Duplicate's earlier-invoice detail hidden from a reader who cannot read that invoice (D-176, #2118)

> Branch `task/ontology-157-duplicate-hide` (worktree `C:\wts-157`), cut from `origin/docs/ontology-platform-design`, fast-forwarded to
> `c7659c6fe1` (the POML as amended after escalation 1, option B, plus the shared check for ebilling 119). Rigor **FULL** (security tag).
> ebilling reviews the range before merge.

## What changed

**Where.**
- `Services/Signals/EarlierInvoiceReadability.cs` (new): the earlier-invoice check, shared with ebilling task 119. Input: the caller's `IDataverseUserClient` and refs `(EarlierInvoiceId?, VendorOrgId?, TrackingNumber?)`. Output: the refs whose earlier invoice the caller's own `sprk_invoices` read returns. An id ref needs only the invoice read. A number-only ref is resolved through `sprk_invoicesubmissions` (vendor org + tracking number, read as the caller): exactly one row with an invoice, or not readable. Reads (round 2): one submission read per chunk of 200 distinct tracking numbers, across vendor orgs, and one invoice read per chunk of 200 ids. A 403 is a verdict (none of that table's rows). Anything else (401, 5xx, unparseable body, exception) is a fault: nothing readable. `public const WithheldSentence = "This matches an earlier submission."` mirrors `BillingRuleCatalogue.WithheldDuplicateTemplate` on `work/ebilling-r1`.
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
  - Any read of `sprk_signal` made directly as the user (native form, views, or any generic endpoint) returns `sprk_sentence` and `sprk_evidencerefs` as stored. This BFF check does not cover it.
  - ebilling's own invoice-review surfaces must apply the same rule; ebilling task 119 is to use `EarlierInvoiceReadability`.
- **Rate cards (agreed with ebilling 2026-10-21):** ApprovedRate and Discount rate-card quotes are inside the family. They are not touched.
- **Evidence that is not a JSON array**, or has colliding property names, cannot be parsed. Such a row is not detected as a Duplicate, so its sentence is returned as stored. (9) still returns its evidence as null. SignalWriter never writes such evidence (K4).
- **Pairing ids with numbers:** when a row carries at least one `duplicateOf.invoiceId` GUID, its tracking numbers are not resolved. ebilling writes the id beside the number of the same prior; it omits the id only for `DuplicateOfEarlierLine` and withheld findings. A merged row mixing an id-bearing prior with a different, id-less number does not occur under ebilling's merge-by-subject (K4).
- **Inactive submissions** are not filtered (the POML names no state filter). A withdrawn submission still counts toward "exactly one".
- **D-123 stored-evidence sweep:** `SignalReevaluationJob.RedactPoliciesNotInForceAsync` / `RedactSignalsOfDeletedPoliciesAsync` → `SignalSuppressionService.StoredEvidenceToRedactAsync` strips the text of `duplicateOf.*` lines on Signals of an Off, Retired or deleted policy where a Secure team exists. Such a row is then withheld from every reader. Fails closed.
- **A prior submission with no invoice link** (no `duplicateOf.invoiceId`, and the submission's `_sprk_invoice_value` is null): the Duplicate is withheld for everyone. Fails closed.
- **Id path vs tracking-number path:** the id path checks only the earlier invoice. The tracking-number path also needs the caller to read the submission, the current invoice and (line-level) the billing event. So an older Signal can be withheld where a newer one about the same prior is shown. Accepted under D-176.
- **URL length:** about 200 very long tracking numbers in one chunk could exceed Dataverse's URL limit. The read then faults and the page's Duplicates are withheld (fail closed). Real numbers are about 20 characters.
- **One page per read (round 3):** a read never follows `@odata.nextLink`; a page that carries one is a fault (Warning logged, every Duplicate on the page withheld). Every submission read names at most 20 orgs and 200 numbers, so under the (org, number) alternate key it holds at most 4,000 rows and cannot reach the 5,000-row page; the guard is a backstop.
- **Structural characters (round 2):** apostrophe, `"` and backslash are proven live; other characters JSON-encode under `UnsafeRelaxedJsonEscaping` and are then URL-escaped once, but have no live test.
- **Case (round 2):** the (org, number) pair is matched `OrdinalIgnoreCase` after the read; Dataverse's In match follows the org collation (case-insensitive on dev). A culture-specific fold that differs from ordinal case-folding could leave a returned row unmatched, which withholds (fail closed).
- **"Two submissions → withhold"** cannot occur live (the Active alternate key on tracking number + vendor org); it is defensive.

## Tests

New tests, all in the task's scope:
- **`SignalReadRouteTests`, section "D-176 (task 157)": 11 methods, 14 cases.**
  - `D176_ADuplicateWhoseEarlierInvoiceTheCallerReads_KeepsItsSentenceAndEvidence`
  - `D176_ADuplicateWhoseEarlierInvoiceTheCallerCannotRead_IsWithheld_InEbillingsWriteTimeShape`
  - `D176_ATrackingNumberOnly_IsResolvedThroughTheOneSubmissionOfTheCurrentInvoicesVendorOrg`: readable / unreadable / zero / two / an unreadable submission. **Beyond the criteria:** a matter subject → withheld, because it is the POML constraint "any other subject type with only a tracking number withholds".
  - `D176_WhenTheEarlierInvoiceReadFaults_EveryDuplicateOnThePageIsWithheld_AndNoOtherRowChanges`: theory over 500 / 401 / unparseable / throw.
  - `D176_WhenTheCallerHasNoReadOnTheInvoiceTable_TheDuplicateIsWithheld_AndNothingIsLoggedAsAFault`: 403.
  - `D176_ARowThatIsNotADuplicate_IsUnchanged_AndCausesNoInvoiceSubmissionOrBillingEventRead`
  - `D176_ALineLevelRepeatOfAnEarlierLine_IsKeptWhenTheCallerReadsItsEventAndInvoice_AndWithheldWhenNotTheEvent`
  - `D176_ALineLevelRepeatOfAPriorSubmissionLine_WithAnInvoiceId_IsJudgedByThatInvoice`
  - `D176_ReadsAreBatched_OneOfEachPerRequest_AndOneSubmissionReadPerVendorOrg_InListsOfAtMost200Ids`
  - `D176_ADuplicateWhoseEvidenceUsesOtherCasing_AndWhoseEarlierInvoiceIsUnreadable_IsWithheld`: added in the fix round. Justification: a security filter whose comparison mode (OrdinalIgnoreCase) could regress to Ordinal silently, which would let a differently-cased `Billing.Reference` / `DUPLICATEOF.INVOICEID` line pass undetected.
  - `D176_ADuplicateNamingTwoEarlierInvoices_OneReadableAndOneNot_IsWithheld`: added in the fix round. Justification: a security filter whose quantifier (`All`, not `Any`) could regress silently, which would show a Duplicate when only one of its priors is readable.
- **Fake world additions (test-only):**
  - catalog entry `sprk_billingevent`;
  - `Invoice` / `BillingEvent` / `Submission` builders;
  - `PathFaults`, which fails one read of a table while another read of the same table succeeds;
  - a submission branch that evaluates the vendor org and the tracking-number In list.

  The bodies of the existing tests are unedited.
- **`EarlierInvoiceReadabilityTests` (unit, new): 8 methods, 15 cases.**
  - id readable/unreadable;
  - the number resolution cases;
  - **beyond the criteria:** `ATrackingNumberIsSentAsAJsonStringInTheInList_NotAnODataDoubledQuote`, because the number is firm-supplied text in a filter (round 2: the earlier `''` escape was a live 400, see "Round 2");
  - neither id nor number → no read;
  - a fault in either read (500 / 401 / unparseable / throw) → nothing readable;
  - 403 on submissions (numbers unresolved, ids still judged) and on invoices (none);
  - bounded reads.

**Results:**

| Suite | Result |
|---|---|
| `SignalReadRouteTests` | 83/83 (69 existing + 14 new; round 2 re-run) |
| Unit `Sprk.Bff.Api.Tests.Services.Signals` | 394/394 (round 2) |
| ArchTests | 1093/1093 (full) |
| BFF build with `-warnaserror` | 0 warnings |

**Failed before / pass after:** with the pre-change `SignalReadService.cs`, 11 of the 12 new route cases fail. The one that passes is the "not a Duplicate → unchanged" case, as it should.

**Seeding proofs:**
- **(a)** `EarlierInvoiceReadability`'s invoice check forced to `true` → `D176_ADuplicateWhoseEarlierInvoiceTheCallerCannotRead_…` and the unit `AnInvoiceId_IsReadable_…` fail.
- **(b)** A fault in the invoice read returns every ref readable → all 4 cases of `D176_WhenTheEarlierInvoiceReadFaults_…` fail.

Both were restored and the D176 tests rerun: 12/12.
- **(c)** Fix round: the four case-insensitive comparisons in `SignalReadService` (kind, the two `duplicateOf` refs, the `duplicateOf.` prefix strip) changed to `Ordinal` → `D176_ADuplicateWhoseEvidenceUsesOtherCasing_…` fails (the sentence is returned as stored). Restored.
- **(d)** Fix round: `references.All(readable.Contains)` changed to `Any` → `D176_ADuplicateNamingTwoEarlierInvoices_…` fails (the sentence is returned as stored). Restored.

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

## Round 2 (shared combining rule, batched submission read, live query proof)

**Moved.** The combining rule left `SignalReadService.DuplicatesShownAsync` for the shared class:
`EarlierInvoiceReadability.ShownAsync<TKey>(caller, IReadOnlyDictionary<TKey, IReadOnlyCollection<Reference>>, ct)` returns the keys that may be shown.
An item is shown only with at least one reference and every reference readable; no references, or any reference with no usable id or (number plus
vendor org), is not shown; on any fault nothing is shown. One `Reference` = one prior. `ReadableAsync` stays public (judges one by one) and its doc points
to `ShownAsync`. `SignalReadService` builds the references (every Duplicate gets an entry, empty when none is found) and calls `ShownAsync`.

**Reads.** One submission read per chunk of 200 distinct tracking numbers, across vendor orgs (was one per org): an In list on `sprk_trackingnumber`,
plus an In list on `sprk_vendororg` when the chunk names at most 20 orgs. `$select=sprk_trackingnumber,_sprk_vendororg_value,_sprk_invoice_value`. The exact
(org, number) pair is matched after the read (number without regard to case); zero or several matches, or no invoice: not readable.

**Live proof (spaarkedev1, GET only, 2026-10-21).** The first shape of the new query returned **400** and was fixed:
- `PropertyValues=['O''Brien']` (the OData doubled quote) is `400 Invalid JSON. A comma character ',' was expected in scope 'Array'`: the In list is parsed as a JSON array.
  The round 1 code escaped that way, so a number with an apostrophe would have faulted the read (fail closed, but wrong).
- Fix: values are JSON strings (`"O'Brien #2"`, `"` and `\` JSON-escaped), the whole filter then URL-escaped. Proven semantically on `sprk_documents.sprk_documentname`
  (same In function; the table has `Examiner's Report 8-24-2026`): the JSON-string form returns that row (200, 1 row, also in lower case); the doubled-quote form is 400.
  A value holding `"` or a backslash parses (200).
- The org In list needs the LOGICAL name: `PropertyName='sprk_vendororg'` is 200 (3 rows on `sprk_invoices`); `'_sprk_vendororg_value'` is 400 (entity has no such attribute).

| Query (built by the code, 2 real vendor orgs `b713f0f2-...`, `67577f8c-...`, 3 numbers incl. `O'Brien #2`) | Status | Rows |
|---|---|---|
| `sprk_invoicesubmissions` (number In list + org In list) | 200 | 0 |
| `sprk_invoices` earlier-invoice read (`In sprk_invoiceid`) | 200 | 1 |
| `sprk_billingevents` (`$select=sprk_billingeventid,_sprk_invoice_value`) | 200 | 1 |
| `sprk_invoices` current-invoice read (`$select=sprk_invoiceid,_sprk_vendororg_value`) | 200 | 1 |

**Limit of the proof:** `sprk_invoicesubmissions` has **0 rows** on dev (`$count` = 0), so no real tracking number exists and the submission query could only be shown to
parse (200) and return an empty set, not to return a matching row. The matching semantics are proven on `sprk_documents` as above.

**Tests (round 2).** Unit +5 `ShownAsync` cases (empty list, all readable, one unreadable, unusable reference, fault theory), +2 org-filter cases, the batching test
rewritten (two orgs one number each: ONE read; 201 numbers: two reads). The route fake now rejects a doubled-quote literal with 400 and evaluates the JSON list;
the batching route test adds a 201-number page (2 submission reads). ArchTests 1093/1093. `-warnaserror` build 0 warnings.

**Seeding (e).** In `ShownAsync`, `Count: > 0` removed (an item with no references counted as shown): `ShownAsync_AnItemWithNoReferences_IsNotShown_AndCausesNoRead` fails, and two
D176 route tests (`...TrackingNumberOnly...`, `...LineLevelRepeatOfAnEarlierLine...`) fail. Restored; all pass.

## Round 3 (ebilling F3: a truncated page withheld silently)

**Finding (ebilling round-2 re-check).** With more than 20 vendor orgs in a chunk, the submission read dropped its org filter. Tracking numbers are
`{firmInvoiceNumber}.{seq}` (ebilling `TrackingNumberAllocator.cs:202`), so values such as `1001.1` recur across firms, and a page could reach Dataverse's
5,000-row cap. `ReadAsCallerAsync` read only `value`, so the rows past the page resolved to "no row" and readable priors were withheld with nothing logged.
It never leaked (the exact pair match still applied), but it was wrong visible behaviour.

**Fix.** Both of ebilling's options:
- `ResolveByTrackingNumberAsync` reads each chunk of 200 numbers in groups of `OrgsPerRead` (20) vendor orgs. Every read carries the org In-filter and
  asks only its own orgs' numbers (was `MaxOrgsInFilter`, above which the filter was dropped).
- `ReadAsCallerAsync` treats a body that carries `@odata.nextLink` as a fault: a Warning is logged and null is returned. This is shared by every D-176 read.

**Tests.** The 21-org test is rewritten: two reads (20 orgs and 1), each carrying its org list, and the single-org read alone asks the number only that org
holds. The fault theory gains a `nextLink` case for each table. Signals unit 396/396; `SignalReadRouteTests` 83/83; ArchTests 1093/1093;
`--no-incremental -warnaserror` build 0 warnings.

**Seeding.** With the `@odata.nextLink` check disabled, exactly the two new `nextLink` cases fail (27 others pass). Restored; all pass.