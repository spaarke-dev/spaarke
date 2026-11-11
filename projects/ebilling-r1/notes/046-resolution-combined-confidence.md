# Task 046 - Resolution and combined confidence

> **Project**: ebilling-r1, task 046 (FR-23, D-47, FR-26, NFR-06, Processing Pipeline step 2)
> **Date**: 2026-10-16
> **Commit**: see section 11
> **Code**: `src/server/api/Sprk.Bff.Api/Services/Finance/Resolution/`
> **Tests**: `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/Resolution/` (190 tests)

## 1. What was built

`IInvoiceResolutionService.ResolveAsync(InvoiceResolutionInput, ct)` is Processing Pipeline step 2. For one invoice it:

1. checks the arithmetic (`ArithmeticChecker`): row products, rows to section totals, sections to total due;
2. matches each fee line's billed name to the invoice firm's roster (`TimekeeperMatcher`): exact, then normalized, then AI-assisted;
3. finds the approved rate in force on each fee line's date in the Published rate card (`RateCardResolver`);
4. combines the four D-47 signals into one confidence per field (`CombinedConfidenceCalculator`) from the `EbillingSettings` weights and caps.

| File | Role |
|---|---|
| `IInvoiceResolutionService.cs`, `InvoiceResolutionService.cs` | The seam and its Dataverse-backed implementation. Two paged reads per invoice at most (roster, rates), then the pure components. |
| `InvoiceResolutionModels.cs` | Input and output records and enums. The input is this task's own record (043 is not a dependency); 049 maps 043's extracted invoice into it. |
| `CombinedConfidenceCalculator.cs` | Pure function of four signals and settings. |
| `ArithmeticChecker.cs` | Pure. |
| `TimekeeperMatcher.cs`, `TimekeeperNameParser.cs` | Matching; the parser is an internal helper (name folding, initials, edit distance). |
| `RateCardResolver.cs` | Pure; rows come from the single rate read. |
| `FinanceModule.cs` | `AddScoped<TimekeeperMatcher>()` and `AddScoped<IInvoiceResolutionService, InvoiceResolutionService>()`, unconditional (eBilling flag rule, task 040). |

**Calling it (task 049).** Build `InvoiceResolutionInput`: `FirmId` and `CoreRecord` from the submission context (044), never from extracted text; `Header` totals and `Lines` as `ResolutionField<T>(value, analyzerConfidence, selfReportConfidence)`; `TimekeeperSummaryNames` = the full names printed in the invoice's own timekeeper summary table (see assumption A9). The result has, per input line and in input order: `Timekeeper` (fee lines), `Rate`, and `Fields` (combined confidence per field); `HeaderFields`; every `ArithmeticChecks` entry; and one `ApprovedTimekeeperFindingInput` per unmatched fee line, ready for task 071.

## 2. Placement justification (root CLAUDE.md section 10, bff-extensions.md section A)

Obviously in the BFF: it is an in-request, deterministic domain step of the Finance feature that already lives in `Sprk.Bff.Api/Services/Finance`, called by the pipeline orchestration (049) in the same process. ADR-052: no background work is added here (the host decision belongs to 049). ADR-001 / ADR-008: no endpoint is added. ADRs applied: ADR-010 (feature-module DI, concrete types, one documented seam), ADR-013 (AI only through the `IInvoiceAi` PublicContracts facade; D-52: `GenerateEmbeddingAsync` only, no Action, no prompt), ADR-002 (read-only; security fails closed), ADR-015 (counts only in logs), ADR-032 (no new Null peer: `IInvoiceAi` already has one, and the matcher degrades to "unmatched" when it throws), ADR-038 (Maintain-class tests), ADR-044 (typed `Guid`s only; no GUID is interpolated into a query string).

## 3. Component justification (root CLAUDE.md section 11)

| Component | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `CombinedConfidenceCalculator` | Nothing combines signals; the only scoring code is `InvoiceTotalsCalculator` (sums of line amounts), a different job. | No host: it is the D-47 formula. | FR-26 cannot tell a reliable read from a misread, so NFR-06 (no rejection from a misread) fails. |
| `ArithmeticChecker` | `InvoiceTotalsCalculator` (016) computes the authoritative totals from line statuses; it does not compare extracted values with each other. | Different input (extracted, unverified values) and output (pass / fail per field), and it must not depend on the write-path owner of the totals. | Without it no field can carry the arithmetic signal or be capped by `sprk_arithmeticfailcap`. |
| `TimekeeperMatcher` + `TimekeeperNameParser` | No roster matching exists (grep `timekeeper` in `Services/Finance`: only the table references from 017). `IInvoiceAi.GenerateEmbeddingAsync` is reused. | `IRecordMatchService` matches documents to matters, a different entity and signal. | Every fee line would be unmatched, or matched by an unreviewed rule: ApprovedTimekeeper (071) cannot work. |
| `RateCardResolver` | Task 078 writes rate cards; nothing reads them. | Reads only, in the pipeline's process. | ApprovedRate (071) and the reference signal on `Rate` have no approved rate to compare with. |
| `IInvoiceResolutionService` | See ADR-010 below. | One implementation. | 049's tests would have to stand up the roster and rate Dataverse reads and the embedding call to prove each branch it takes on a resolution. |
| `TimekeeperMatcher` DI registration | none | Concrete, scoped (it takes the scoped `IInvoiceAi`). | The service cannot be constructed. |

**ADR-010 ceiling: 162 -> 163 (+1).** `IInvoiceResolutionService -> InvoiceResolutionService` is the only new 1:1 interface (confirmed: with the ceiling left at 162 the ADR-010 test fails with "increased from 162 to 163" and lists exactly this pair). The seam justification is in `tests/Spaarke.ArchTests/ADR010_DITests.cs` next to the number, and is falsifiable: if 049 does not fake it, register the concrete and drop the ceiling back to 162.

## 4. Settings check (escalation trigger 2) and the cap decision

Spec D-47, task 025's `EbillingSettings` and the seeded row agree: threshold 0.90; weights analyzer 0.50, arithmetic 0.25, reference match 0.15, self-report 0.10 (sum 1.00); caps arithmetic 0.50, reference-missing 0.80. The trigger did not fire.

Task 025 left one question: "046 confirms or changes" whether a cap is a **ceiling** (`min`) or a **replacement**. **Confirmed: ceiling.** A field whose raw score is already below the cap keeps its lower score (a cap that raised a 0.20 field to 0.50 would overstate confidence, the unsafe direction). Both caps in force: the lower wins. Proposed decision-log text: "D-47 caps are ceilings (`min`), confirmed in task 046."

## 5. Assumptions and deviations

| # | Topic | Choice | Why |
|---|---|---|---|
| A1 | Formula | `combined = sum(w_i x s_i) / sum(w_i)` over the **applicable** signals, then capped. | The weights need not sum to 1 (the platform does not enforce it), and a signal that does not apply to a field (a narrative has no roster or arithmetic) is left out rather than counted as a pass or a fail. With only the analyzer signal the score equals the analyzer confidence. |
| A2 | Missing analyzer confidence | Counts as 0 (fail closed). A field with no value and no confidence at all therefore scores 0 and still has an entry. | Missing evidence must not look like support. Callers read the value to tell "absent" from "low". |
| A3 | Self-report | Signal = `min(self-report, analyzer)`; left out when none was given. | D-47: it can lower a score, never raise one above the analyzer's. |
| A4 | Arithmetic and reference signals | Independent evidence: they can lift a score above the analyzer's. | A roster-confirmed name is better evidence than an OCR span alone. Rejection stays protected by the caps. |
| A5 | No rounding | A score of 0.89996 stays below 0.90. | Rounding could push a field over the line that gates rejection. |
| A6 | Arithmetic | Tolerance 0.01 inclusive; a product is rounded to cents (away from zero) first; a failed check marks **every** field it involved; a section sum counts a row with no amount as 0 (a lost amount fails the sum); a missing value skips the check. | Arithmetic cannot say which value is wrong, and a capped field can only stop an automatic rejection, never cause one. |
| A7 | Rate-card precedence (POML step 3 asked for this to be recorded) | Card scope first, basis second: (1) core-record card, timekeeper rate; (2) core-record card, classification rate; (3) firm card, timekeeper rate; (4) firm card, classification rate. First tier with any rate decides. Rate and card must be Published / active, the card must be of the invoice's firm, the line date inclusive inside both date ranges, a card scoped to another core record never applies, and a card scoped to more than one core record (forbidden by 078) is not used. | A matter-specific card overrides the firm's general card even for a timekeeper the general card names; within one card an individual's rate beats the classification's. |
| A8 | Rate disagreement | If the deciding tier holds rates of different amounts, the result is `Ambiguous`, no rate is used, and the `Rate` field is reference-missing. Equal amounts are one rate. | Never guess a rate that gates a finding. Not asked for by the POML. |
| A9 | Initials | A label that is initials only ("DL", "D.L.") or an initial plus family name ("Lockhart, D.") is expanded **only** through the invoice's own timekeeper summary names (new input `TimekeeperSummaryNames`), when exactly one person fits. Never from the roster. Unexpanded: unmatched with `NameIncomplete`, and no model call. | A roster that holds Hannah Rosenthal would otherwise absorb a Hector Rosenthal who is not on it (an evaluation-set case). Beyond the POML; needed for "normalized (initials)". |
| A10 | Normalization | Folds case, accents, apostrophes, full stops and hyphens, "Last, First" order, honorifics (Mr Mrs Ms Miss Mx Dr Prof Professor Sir Dame Hon) and a trailing Esq / Esquire (also after a comma). Keeps a generational suffix (Jr Sr II III IV): two people. Middle names may be missing on either side or be initials; two that contradict are two people. An initial is never guessed. | FR-23 normalized stage. |
| A11 | AI-assisted stage | Cosine similarity of `text-embedding-3-large` vectors of the canonical name. Used only if best >= reliability threshold (the same setting) **and** best leads the second by >= **0.05**. Candidates are blocked to approved timekeepers of the firm whose family name is within **2 edits**, whose suffix is the same, and whose middle names do not contradict. The match confidence is the similarity. | The margin 0.05 and the 2 edits are assumptions, not settings (025 has no column for them); calibrated in 161 / 162. The suffix and middle-name guards stop "John Smith Jr" becoming "John Smith" however close the vectors are. Blocking also bounds embedding calls to the few plausible people. |
| A12 | Known but not approved | If the name matches (exactly or normalized) a roster person of the firm who is not Approved, active and in date on the line date, the line is unmatched with `NotApprovedOnLineDate` and the AI stage is skipped. | The model must not hop from a known unapproved person to an approved look-alike. 071 can tell "unknown" from "known, not approved". |
| A13 | AI off or failing | The line stays unmatched (`AiUnavailable`); resolution does not fail. Caller cancellation propagates. | Fail closed for the match, never for the invoice. |
| A14 | Reference signals | `TimekeeperAsBilled`: matched / missing from the roster match. `Rate`: matched when a rate was found, missing otherwise, with strength = the timekeeper match confidence (a rate found through an AI-assisted name is only as strong as that name). | |
| A15 | Reads | One roster read (`sprk_timekeeper` of the firm, every status, paged 5000, ordered by key) and one rate read (`sprk_ratecardrate` joined to its card; firm, Published, date overlap, firm-wide or this core record's scope in the query) per invoice, skipped when no line needs them. The firm / status / scope rules are applied again on the rows, so a query that returned too much cannot change a result. A Dataverse failure is an error. | Asserted: the call count is the same for 1 and 300 lines. |

## 6. Embedding experiment (escalation trigger 1 did not fire)

Question: can embedding similarity separate correct from wrong timekeepers on the evaluation-set rosters? Method: read-only inference calls to the dev `text-embedding-3-large` deployment, synthetic names only (set F of the eBilling evaluation set; `ground-truth/compliance-invoices`), threshold 0.90, lead 0.05. 1,249 distinct names, 779,376 pairs.

| Test | Result |
|---|---|
| A. The 48 seeded ApprovedTimekeeper violation lines (name not on that firm's approved roster), best candidate | 0 at or above 0.90 (max 0.713, median 0.436). **0 wrong matches.** |
| B. 42 billed names that are not approved, against their firm's roster | 0 at or above 0.90 (max 0.713). 0 wrong matches. |
| C. Every pair of different people's names | 0 at or above 0.90 (0 at or above 0.95). Highest 0.864 ("aisha nwosu" / "aisha okafor"); 2 pairs above 0.85, 26 above 0.80. |
| D. 7,282 synthetic misspellings of approved names | 443 (6.1%) at or above 0.90, **all** to the correct person; the other 94% stay unmatched (median similarity 0.78). 0 wrong. |

Reading: at 0.90 the model never confused two people in this set, and it recovers only a small share of typos, so it is a conservative helper, not the main matcher (exact and normalized do the work). The closest different-person pair differs by 0.14 from the next, so the 0.05 lead is a floor, not a tuned value. Caveats: synthetic, English-style names; the experiment did not include generational suffixes, which is why the matcher guards them structurally (A11). 161 / 162 re-measure on real invoices.

## 7. Owner question (not a defect in this task)

**The ApprovedTimekeeper rule cannot auto-reject today.** FR-26 / I-5: only the combined confidence may gate a rejection. An unmatched timekeeper field is reference-missing, so it is capped at 0.80 (< 0.90). Therefore an invoice line billed by a timekeeper who is **not approved** (the very case the rule exists for) can never reach the threshold on its `TimekeeperAsBilled` field and will go to review, not be rejected, whatever the other signals say. That is safe but may not be intended. Options for the owner (task 071 / 161): (a) accept: the rule produces findings that a person actions; (b) for `NotApprovedOnLineDate` (the person is *known* and *known not approved*) treat the roster lookup as a matched reference for the identity and let the *approval* fact be the rule's own deterministic evidence; (c) a separate cap for that case. This task exposes `NotApprovedOnLineDate` so (b) needs no change to the matcher. No code change made: it is a policy decision about what may gate a rejection.

## 8. Known limits

| # | Limit | Concrete scenario | Direction |
|---|---|---|---|
| K1 | Total-due check ignores tax, prior balance, payments (not R1 fields) | An invoice whose total due includes tax fails the total-due check; its four total fields are capped at 0.50. | Safe: review, not rejection. |
| K2 | Only name-level evidence for the AI stage | A typo that embeds close to a *different* approved person's name with a clear lead would match them. 0 such cases in the experiment (section 6). | Re-measured in 161 / 162. |
| K3 | Margin 0.05 and 2 family-name edits are not settings | A tenant cannot tune them without code. | Revisit after calibration. |
| K4 | The rate read is not narrowed to the matched people | A firm with very large in-force rate cards reads all their rows (paged, 5,000 per page) once per invoice. | Performance only; narrow by timekeeper / classification if a firm exceeds tens of thousands of rates. |
| K5 | "Ana Lopez" normalizes to "Ana Garcia-Lopez" | A middle name present on one side only is treated as missing, so a family-name particle counted as a middle name can merge two people who share a first and last word. | Rare; consistent with the documented middle-name rule. |
| K6 | The matcher's `catch` around the embedding step also covers the similarity arithmetic | A programming error there would show as `AiUnavailable` (logged by exception type). | Safe direction (unmatched). |
| K7 | Names and amounts are never logged (ADR-015) | Debugging a mismatch needs the invoice. | Intended. |

## 9. Beyond the POML (one-line justifications)

- `TimekeeperSummaryNames` input and the `NameIncomplete` reason: the only invoice-internal source that can expand initials without guessing (A9).
- `NotApprovedOnLineDate` reason: lets 071 tell "unknown" from "known, not approved" and stops the model hopping (A12).
- Suffix and middle-name guards, 2-edit family-name blocking: bound wrong-person matches and embedding cost (A11).
- `Ambiguous` rate status: never guess a rate that gates a finding (A8).
- Comma-before-suffix parsing ("John Smith, Jr.", "Jane Doe, Esq.", "Smith, John Jr."): found while writing the tests; without it those common legal forms would not normalize (and would fall to the AI stage).

## 10. Verification

| Check | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | 0 errors, 0 warnings |
| New tests (`Services.Finance.Resolution`) | 190 passed, 0 failed: calculator 22 (incl. a 7 x 4 x 3 x 3 x 4 grid of invariants), arithmetic 26, rate card 31, matcher 72, service 39 |
| All `Services.Finance` unit tests | 338 passed |
| DI / options / Finance API unit tests | 270 passed |
| `tests/Spaarke.ArchTests` | 1052 / 1052 passed (ADR-010 ceiling 163) |
| Mutation proof ("fail before"; the code is new, so each guarantee was disabled in turn): 16 mutations of the production code (arithmetic cap off, self-report clamp off, status check off, firm filter off, suffix guard off, middle-name guard off, AI threshold made exclusive, embedding memo off, Draft-card check off, card-firm check off, tolerance 0.01 -> 0.001, paging off, roster always read, finding input not produced, comma-suffix parsing off in two places) | 45 test cases failed on the mutated code; every mutation was caught by at least one named test; restored and 190 / 190 pass |
| `dotnet format whitespace --verify-no-changes` on all changed files | clean |
| `dotnet list package --vulnerable --include-transitive` | no vulnerable packages (no csproj or package change) |
| Publish size | see section 12 |
| Gates | code-review and adr-check run on the commit, section 13 |

## 11. Commits

See the final report; this file is committed with the POML status update.

## 12. Publish-size delta (CLAUDE.md section 10, bff-hygiene item 4)

Measured 2026-10-16 from three clean `git archive` exports at short paths (`C:\wt046m`, `C:\wt046b`, `C:\wt046h`; `src/server`, `config/secure-record-owner-role.json` (an embedded resource of the BFF project) and the root build files; removed afterwards): `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -r linux-x64 --self-contained false -p:NuGetAudit=false`, framework-dependent linux-x64, then `Compress-Archive -CompressionLevel Optimal` over the output. **PDBs included** (4). **192 files on every side.**

| Build | Zip (B) | Zip (MiB) | Uncompressed (B) | `Sprk.Bff.Api.dll` (B) |
|---|---|---|---|---|
| fresh `origin/master` `a8c3182ec` | 38,953,350 | 37.15 | 130,103,520 | 18,960,384 |
| base `70bfb7586` (parent of this task) | 38,991,626 | 37.18 | 130,222,012 | 19,054,592 |
| head `25cc02f56` (this task) | 39,034,439 | 37.23 | 130,338,472 | 19,152,384 |
| **Delta, this task (head - base)** | **+42,813 (+0.04 MiB)** | | +116,460 | +97,792 |
| Delta vs fresh master (head - master) | +81,089 (+0.08 MiB) | | +234,952 | +192,000 |

Code only: no package, csproj or `Directory.Packages.props` change, the same 192 files. The master delta is an upper bound for this task: it also contains the `work/ebilling-r1` commits not yet on master. Well under the +5 MB per-task threshold; total 37.23 MiB, under the 55 MB review line. `dotnet list package --vulnerable --include-transitive`: "no vulnerable packages", so no new HIGH CVE.

## 13. Quality gates (task-execute Step 9.5)

Rigor: the POML says STANDARD; the task touches `tests/**`, so code-review and adr-check run regardless (root CLAUDE.md section 8). Reviewer: the implementing session (self-review) with the code-review and adr-check skill procedures. **An independent adversarial pass was not run**: the launch was refused ("concurrent subagent limit reached", 4 running). It is recommended once, from the main session, on commit `25cc02f56` (areas worth the time: wrong-person matches, anything that lifts a field to 0.90 it should not reach, Dataverse query shape that mocks cannot see).

**code-review (coverage-first, all findings listed).** No F-class (fix-now) finding. Metrics (lines / public methods / branches): `InvoiceResolutionService` 394 / 2 / 30 (cohesive: one reason to change, the resolution assembly; reads and query building are private helpers), `TimekeeperMatcher` 392 / 8 / 44 (cohesive: name-to-roster resolution in stages; the parser is already separate), `TimekeeperNameParser` 282 / 10 / 42, `InvoiceResolutionModels` 351 (records and enums), `RateCardResolver` 161, `ArithmeticChecker` 151, `CombinedConfidenceCalculator` 107, interface 21. Security: no secrets, no endpoint, firm from the submission context only, empty firm refused, no content in logs, typed `Guid`s in queries (no injection surface). Performance: reads bounded (asserted), paged, no per-line query, embeddings memoized per call. ADR-013: only `IInvoiceAi`. Findings, all known-limit or suggestion: K-class items in section 8 (K1 total-due vs tax, K4 unfiltered rate read, K5 middle-name particle, K6 broad catch around the embedding step); AI-smell 1 (`IInvoiceResolutionService` is 1:1: documented seam, ceiling raised by exactly 1); smell 3 / 5 suggestions (`ArgumentNullException.ThrowIfNull` on non-nullable inputs follows the repo idiom for public entry points; `ResolveAsync` is an orchestration of calls with section comments, not inline logic). Test review (ADR-038): Maintain-class remark on every class; mocks only at module boundaries (`IGenericEntityService`, `IInvoiceAi`, settings reader); no `Mock<HttpMessageHandler>`, no DI-registration or constructor-null-check tests, no coverage fillers; each guarantee is backed by a mutation that fails it (section 10).

**adr-check.** ADRs considered and why: ADR-001 / 052 (no function, no background work: compliant), ADR-002 (read-only, fails closed: compliant), ADR-007 (no Graph types), ADR-008 (no endpoint), ADR-009 (no cache added), **ADR-010 (Warning: one new 1:1 interface, documented seam, ceiling 162 -> 163, falsifiable)**, ADR-013 (compliant: PublicContracts facade, embedding only, D-52), ADR-015 (counts only), ADR-018 / 032 (unconditional registration, `IInvoiceAi` already has its Null peer), ADR-028 (no auth surface), ADR-029 (publish size measured, no package), ADR-038 (compliant), ADR-039 (embedding similarity is not intent detection; output is verified against the roster), ADR-044 (typed `Guid`s). No violation; nothing needs a section 6.5 path A / B. bff-extensions section A: placement justification (section 2), ADRs cited, publish-size delta (section 12), no new CRUD to AI dependency, feature-module DI.

## 14. Review fixes (2026-10-16)

Review finding F1-1 (FR-23, "never silently maps to a different timekeeper"), fixed in `TimekeeperMatcher.cs`.

- **Defect.** Stage 2 (normalized) ran over the eligible roster rows only, and the "known but not approved" check (A12) ran after it. A billed "David Lee", whose roster row was Inactive, Pending, deactivated or out of date, was matched to an Approved "David K. Lee" at confidence 1 (a middle name on one side only is treated as missing, A10).
- **Fix.** Before stage 2, if any roster row of the firm with exactly the billed key (trimmed, whitespace-collapsed, upper-cased) is not eligible on the line date, the line is unmatched with `NotApprovedOnLineDate`. Stage 1 still runs first, so an eligible exact row wins over an ineligible row of the same name.
- **Why exact key only, not "normalizes to the same name".** The 078 renewal (below) leaves several roster rows with one name and non-overlapping periods. Billed "Doe, Jane" on a 2027 date must still reach the eligible 2027 row at stage 2 even though the ended 2026 row of the same person normalizes to the same name. A test pins it.
- **Other stages checked.** Stage 3 (AI) is already after the known-but-not-approved check, which covers exact and normalized rows; no change. Stage 1 only sees eligible rows and returns the single eligible match, or `Ambiguous` when several are eligible.
- **Several rows sharing a name (needed by task 078 F1-2), verified and tested.** With non-overlapping periods, stage 1 returns the row eligible on the line date (2026 date: the 2026 row; 2027 date: the 2027 row); a date with no eligible row, or a still-Pending renewal row, gives `NotApprovedOnLineDate`.
- **Residual (known limit K8).** A roster row that differs from the billed name only by spelling (for example "Lee, David", not eligible) next to an Approved "David K. Lee" is still matched at stage 2. Closing it would need a stricter canonical-equality block and would conflict with renewals (above); that is a policy call for the owner.
- **Tests.** `tests/integration/regression/Ebilling/TimekeeperMatcherNeverMapsToADifferentTimekeeperTests.cs` (9 cases). Before the fix 5 failed (the five ineligible kinds), 4 passed (controls); after, 9 / 9.
