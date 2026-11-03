# Task 005 — Extraction spike (FR-03): results and go / no-go recommendation

> Run 2026-10-15 by task-execute (STANDARD rigor, directional steps), dev only.
> Resources: Content Understanding (CU) and Document Intelligence (DI) on `spaarke-docintel-invoices-dev` (westus); Azure OpenAI `spaarke-openai-dev` (eastus).
> Auth: Entra tokens from `az account get-access-token` only. No key or key-bearing app setting was read.
> No change under `src/`, no package reference on this branch, no model deployment created.
> **Status: recommendation written; the go / no-go is PROPOSED and awaits the owner (D-81 in `decisions-log.md`).**
> Harness and raw results: `notes/spikes/extraction/` (README in each script's docstring); machine-readable results in `notes/spikes/extraction/results/`.

## 0. Owner summary

**The question:** should eBilling read invoices with Azure Content Understanding (CU), using the generally available version (GA) or the newer preview? Or should it fall back to the simpler "OCR + language model" approach?

**What we did:**
- We ran the same 41 invoices through each approach and compared every value with the known right answers. The 41 include a 108-page fee application, a 26-page invoice and 14 PDFs with a cover letter or statement attached.
- We also compared two models for assigning billing codes, and measured how much the CU library adds to the BFF.

| | Today's extraction | CU ready-made invoice model | **CU, our analyzer (GA)** | CU, our analyzer (preview) | Fallback: OCR + gpt-5.2 |
|---|---|---|---|---|---|
| Invoice header right (target 95 %) | 51 % | 58 % | **98 %** | 98 % | 98 % |
| Time-entry date / timekeeper / hours / rate / amount right (target 90 % each) | 49 / 0 / 50 / 37 / 60 % | 91 / 0 / 90 / 100 / 100 % | **92 / 92 / 92 / 100 / 100 %** | 96 / 96 / 96 / 100 / 100 % | 54 / 53 / 52 / 31 / 57 % |
| Same, on invoices of 20 pages or fewer | 94 / 0 / 99 / 99 / 99 % | 97 / 0 / 96 / 100 / 100 % | **98 / 99 / 100 / 100 / 100 %** | 100 / 99 / 100 / 100 / 100 % | 100 / 100 / 100 / 100 / 100 % |
| Values marked ≥ 90 % sure that were wrong | 2.8 % | (never that sure) | **1.0 %** | 0.6 % | 0.8 % |
| Slowest 1 in 10 invoices takes | 37 s | 62 s | **49 s** | 53 s | 49 s |
| Cost per typical invoice (1–4 pages) | $0.006 | $0.07 | **$0.07** | $0.07 | $0.05 |
| Meets the accuracy target | no | no | **yes** (with automatic clean-up of hh:mm hours) | yes | no (fails on long invoices) |

- **Billing codes:** both models score about 62–66 % on the main task code (target 80 %), and their "very sure" answers are wrong 22–31 % of the time.
  - gpt-5.4-mini is slightly better and better calibrated. gpt-5.6-luna is about 4× cheaper, but both cost under $0.30 per 1,000 lines.
  - **Keep gpt-5.4-mini.** The 80 % target is at risk and needs better context (the matter's code set, firm codes, corrections), not a different model.
- **Coding inside the analyzer** (the "classify" experiment) was no better than the separate coding step: 76 % vs 77 %. Keep coding separate.
- **Preview analyzers cannot be used from the GA API** (and vice versa). Switching later means re-creating the analyzer and re-testing.
- **Package size:** +0.16 MB (+1 file), far below the +5 MB review threshold, and no vulnerable packages. **Not flagged.**

**Recommendation: GO with Content Understanding, GA only.** It meets the accuracy targets, says honestly how sure it is, points to where on the page each value came from, and handled the 26-page invoice that broke both language-model approaches. The preview is a few points better on unusual formats (day-month dates, "1:30"-style hours), but GA already meets the targets and the preview has no service guarantee. **No preview exception is needed.**

**What this means for task 043.** 043 builds invoice reading on GA CU, with four additions:
1. automatic clean-up of hh:mm hours and missing rates;
2. analyzer v2 with clearer instructions for hours and non-US dates;
3. reading very long PDFs in page ranges (re-tested in 161 / 162);
4. the confidence weights tuned to CU's cautious scores.

If you choose **no-go** instead, 043 builds the fallback (OCR layout + an extraction Action). It is just as accurate on ordinary invoices, but needs our own long-document chunking and has no page highlighting per value.

**Decision needed:** A (GO, GA only — recommended), B (GO with a preview exception) or C (NO-GO, fallback). Proposed as **D-81** in `decisions-log.md`.

## 1. Method

### 1.1 Items (FR-03: ≥ 30 invoices, the largest available, ≥ 3 bundled PDFs, same items for every arm)

41 items, 255 pages, 1,825 ground-truth fee and expense lines:

| Group | Items | Notes |
|---|---|---|
| Set B (synthetic) | 37 | B-0001–B-0024, B-0026, the 10 items B-0031, B-0043 … B-0139 (every 12th), B-0149 (12 pages) and B-0150 (26 pages). 14 are bundled PDFs: 10 with a cover letter before the invoice, 4 with a statement of account after it. |
| Set A (real fee application) | A-07 | 108 pages, the largest document available. Partial ground truth (254 labelled lines), so spurious rows are not counted on it. |
| Set C (real invoices) | C-03, C-10, C-11 | |

**Excluded: A-02.** Its manifest PII scan disposition is "REVIEW: identifier-like strings found" (5 card-number-like hits), so it was not sent to a model (ADR-015). Every other spike item is synthetic or has a reasoned clearance. A-07 and the set C items are `local-only`: their raw results stay in the git-ignored `evaluation-set/local/spike-005/`; only synthetic items have a stripped copy (fields and usage, no markdown or page text) under `spikes/extraction/raw/`.

### 1.2 Arms

| Arm | What runs | API | Model |
|---|---|---|---|
| a | Today's path, reproduced outside the BFF: DI `prebuilt-read` text (as `TextExtractorService`) → the verbatim `ExtractInvoiceFactsAsync` user prompt and a verbatim copy of `FinanceJsonSchemas.ExtractionResultSchema`, temperature 0, strict schema | DI 2024-11-30, AOAI 2025-04-01-preview | **`gpt-4o-mini` deployment, which serves gpt-4.1-mini 2025-04-14** (dev has no gpt-4o; DEF-003). The `FinanceExtraction` system prompt never existed in dev or the repo (#1918), so a short stand-in system prompt was used (in `run_text_llm.py`). |
| b | CU `prebuilt-invoice` | GA 2025-11-01 | service default (gpt-5.2 on the resource) |
| c | CU `sprk_invoice_v1` (task 004, unchanged) | GA 2025-11-01 | gpt-5.2, text-embedding-3-large |
| c′ | `sprk_invoice_v1p`: the same field schema, created on the preview API | 2026-06-01-preview | gpt-5.2 |
| a2 (supplementary) | The no-go fallback shape: DI `prebuilt-layout` markdown → gpt-5.2 (reasoning effort low) with an FR-21 schema carrying per-field confidence | DI 2024-11-30, AOAI 2025-04-01-preview | gpt-5.2 on the CU resource |

Arm a2 is not an FR-03 arm. It answers "how good would the 043 fallback be", which the go / no-go needs.

No arm used labelled samples (knowledge sources). The research note's "up to 3.5 %" preview claim is about labelled-sample training; c′ here tests the plain preview analyzer.

### 1.3 Scoring (`score.py`, reusable by 161 / 162)

- **Header:** the 12 FR-21 header fields, normalised (dates ISO, amounts to 0.01, firm names without legal-form suffixes, `total_due` = total due or current charges, an adjustment of equal magnitude and opposite sign counts and is tallied).
- **Lines:** each ground-truth line is matched to at most one extracted row. A missed line counts as wrong for every field, so recall is inside the accuracy figure.
- **Three line views:**
  - *printed* — only the fields the document prints on the row, values as returned. This is the extraction measure.
  - *repaired* — the same fields after a deterministic completion any pipeline can apply (hh:mm hours → decimal when hours × rate ≠ amount, rate from the timekeeper summary, arithmetic). This is what 043 can store, and the figure compared with NFR-04.
  - *complete* — every ground-truth field after completion, including values the row does not print (for example a rate shown only in a legend). Reported for context.
- **Calibration:** every value scored, binned by the arm's own confidence. The headline is the count and rate of wrong values at confidence ≥ 0.90.
- **Latency:** wall time from POST to the terminal result, 3 workers in parallel per arm.
- **Cost:** usage returned by the service (CU returns `usage` at the top level of the poll result: pages, contextualization tokens and model tokens) × Azure Retail Prices list prices read 2026-10-15 (`prices.json`). Cached input is priced at the cached rate.

**Targets:** NFR-04 = header ≥ 95 %; line date, timekeeper, hours, rate and amount each ≥ 90 %. NFR-05 = UTBMS task top-1 ≥ 80 %.

## 2. Accuracy per arm

All 41 items succeeded on every arm (no re-runs were needed).

### 2.1 Against NFR-04 (the *repaired* view: printed fields, after deterministic completion)

| Field | a (today) | b prebuilt | **c GA** | **c′ preview** | NFR-04 |
|---|---|---|---|---|---|
| Header, 12 FR-21 fields | 50.6 % | 58.1 % | **97.6 %** | **98.2 %** | ≥ 95 % |
| Fee line date | 48.9 % | 91.2 % | **91.5 %** | **95.9 %** | ≥ 90 % |
| Fee line timekeeper | 0 % (not in schema) | 0 % (not in schema) | **92.1 %** | **95.6 %** | ≥ 90 % |
| Fee line hours | 49.5 % | 89.8 % | **92.0 %** | **95.6 %** | ≥ 90 % |
| Fee line rate | 36.7 % | 99.8 % | **100 %** | **100 %** | ≥ 90 % |
| Fee line amount | 60.4 % | 99.8 % | **100 %** | **99.9 %** | ≥ 90 % |
| Expense date / amount | 100 / 97.5 % | 98.3 / 99.0 % | 97.7 / 100 % | 100 / 100 % | — |
| Line recall (of 1,825) | 56.9 % | 93.1 % | 93.2 % | 96.3 % | — |
| Extracted rows matching no line | 16 | 46 | 18 | 21 | — |
| **Meets NFR-04** | no | no | **yes, with repair** | **yes** | |

- **Values as returned (*printed* view, no repair):** c date 91.5, timekeeper 92.1, **hours 89.3**, rate 100, amount 100. Without the deterministic hh:mm repair, GA misses the hours target by 0.7 points. c′ meets every target as returned (hours 95.0, 95.6 after repair). Arm b: hours 87.8 as returned.
- **Long documents decide the margins.** On the 39 items of ≤ 20 pages, without A-07 (108 pages) and B-0150 (26 pages); repaired view; `results/long-doc-sensitivity.json`:

  | | a | a2 (fallback, §10) | b | c | c′ |
  |---|---|---|---|---|---|
  | Header | 51.3 | 99.1 | 58.8 | 97.6 | 98.7 |
  | date / timekeeper / hours / rate / amount | 93.6 / 0 / 99.1 / 99.1 / 98.9 | 99.9 / 99.5 / 99.9 / 100 / 99.7 | 96.9 / 0 / 96.1 / 100 / 99.9 | 98.0 / 99.3 / 99.9 / 100 / 100 | 100 / 99.3 / 99.9 / 100 / 99.9 |
  | Line recall | 99.6 | 100 | 98.2 | 100 | 100 |
  | Mean cost / p90 | $0.008 / 33.5 s | $0.067 / 32.9 s | $0.094 / 55.6 s | $0.088 / 37.8 s | $0.091 / 51.5 s |

  - On ordinary invoices, c, c′ and the fallback a2 are all at about 98–100 %.
  - The differences come from the two long documents:
    - **B-0150 (26 pages, 650 lines):** c and c′ extracted 650 / 650. Arm a returned 121 rows and a2 returned 69: the LLM stops early.
    - **A-07 (108 pages):** c matched 130, c′ 187 and a2 81 of 254 labelled lines.
- ***Complete* view** (every ground-truth field, including values the row does not print, after completion): c 88.8 %, c′ 91.0 %, b 68.6 %, a 39.4 % over fee fields. The gap from the repaired view is mostly values the row does not print: A-07 rates (the fee application has no per-row rate and the completion found none), and expense dates on templates that print none. Attaching those is a reference-data job in 043, not an extraction error.
- **Header detail:** arm a carries only 5 of the 12 FR-21 header fields (98.5 % on those 5); arm b carries 7 (96.9 % on those; 12 of its adjustment matches are sign-only, because it reports a discount as positive). c had 1 sign-only adjustment match.

### 2.2 Where GA (arm c) loses points

| Failure | Items | Effect | Fix in 043 / analyzer v2 |
|---|---|---|---|
| Long document: runs of pages (about 20 of the 108: within 36–45, 62–64, 84–85, 89–94) came back without usable rows (no timekeeper; dates, hours and amounts that match no labelled line) | A-07 | 124 of 254 labelled lines missed; this alone takes date / timekeeper / hours from ≈ 99 % to ≈ 92 % | Analyze long PDFs in page ranges (`inputs[].range`, FR-48 chunk size from settings) and merge; re-measure in 161 / 162. c′ also lost lines here (187 of 254 matched). |
| dd/mm dates on GBP invoices read as US mm/dd | B-0002, B-0021 | 15 fee and 4 expense dates wrong; 4 header values | v2 field description: "dates follow the invoice's locale; a GBP / EUR invoice uses day-month order" plus a deterministic check (a date outside the billing period → swap day and month when that fits). c′ got all of them right. |
| hh:mm hours ("1:30") returned as decimals (1.30) | B-0008, B-0015, B-0023 | 43 hours wrong as returned; all 43 repaired by the hours × rate = amount check (B-0015 prints no rate on the row, so the rate comes from the invoice's own timekeeper summary first) | v2 description for Hours; keep the deterministic repair in 043 either way. |
| Timekeeper name differs from the ground truth (group-header titles, initials) | B-0011, B-0023, B-0055, B-0079 | 5 lines | none needed; within tolerance |

§2.4 shows these GA errors are systematic: they recur on a second run.

### 2.3 Arm a (today's path) and arm b in one paragraph each

**Arm a** cannot be the eBilling extractor:
- Its schema has no timekeeper and 5 of 12 header fields.
- It stops early on long documents: B-0150 returned 121 of 650 lines, and A-07 returned 4.
- Recall is 56.9 %. On invoices of ≤ 20 pages it reads the values it does carry well (93.6–99.1 %).
- It is cheap: median $0.006 per invoice.

**Arm b** (`prebuilt-invoice`) reads dates, amounts and rates well. But it has no timekeeper, period, matter reference, fee total or expense total. Its confidences are all below 0.70, so they cannot drive FR-26. It also had the slowest p90 and was the most expensive arm. It is not a candidate.

### 2.4 Repeatability check (c and c′ re-run once on the 6 items where they differed)

Cost rule: one run per arm and re-runs only for failures. These 6 items are the accuracy failures that decide between GA and preview, so arms c and c′ were each re-run once on them (`crep`, `cprep`; `results/repeatability.json`).

| Item | Issue | c run 1 | c run 2 | c′ run 1 | c′ run 2 |
|---|---|---|---|---|---|
| B-0002 | dd/mm dates (GBP) | wrong | wrong | right | right |
| B-0021 | dd/mm dates (GBP) | wrong | **right** | right | right |
| B-0008 | hh:mm hours | wrong | wrong | right | right |
| B-0015 | hh:mm hours, no rate on row | wrong | wrong | wrong | **right** |
| B-0023 | hh:mm hours | wrong | wrong | right | right |
| B-0115 | one amount | right | 1 wrong | 1 wrong | 1 wrong |

**Conclusion:** the GA analyzer's hh:mm and dd/mm errors are systematic, not noise. The preview handles them better. On these items the GA errors are fully removed by the deterministic repair (hours) and need a v2 description plus a date check (dates). Run-to-run variation exists on both versions (B-0021 on GA, B-0015 on preview), so a single 41-item run is good to ±1–2 points on these fields.

## 3. Confidence calibration

| | a | b | c | c′ |
|---|---|---|---|---|
| Values scored | 3,358 | 5,756 | 7,512 | 7,681 |
| **Values at confidence ≥ 0.90** | 3,358 (100 %) | 0 | 619 (8.2 %) | 708 (9.2 %) |
| **…of which wrong** | **93 (2.8 %)** | — | **6 (1.0 %)** | **4 (0.6 %)** |
| Line values ≥ 0.90 / wrong | 3,153 / 90 | 0 | 249 / 1 | 293 / 1 |
| Header values ≥ 0.90 / wrong | 205 / 3 | 0 | 370 / 5 | 415 / 3 |
| Wrong rate at 0.70–0.80 / 0.80–0.90 | — | — | 1.8 % / 1.4 % | 0.9 % / 1.2 % |
| Share of correct values that reach ≥ 0.90 | 100 % | 0 % | 8.3 % | 9.2 % |

What this means:

- **Arm a is over-confident.** It reports one invoice-level number (always ≥ 0.90), so 2.8 % of "high-confidence" values are wrong, and the number says nothing about which ones.
- **CU confidence is honest but conservative.** At ≥ 0.90 about 1 value in 100 (c) to 1 in 175 (c′) is wrong. But most correct line values sit at 0.70–0.90, where the error rate is still only 1–2 %.
- **Consequence for D-47 / FR-26 (task 069, the D-69 weights):** with analyzer confidence as the dominant signal and a 0.90 threshold, about 90 % of correct values would never be reject-eligible. This is safe (AI never rejects wrongly) but makes the rejection rule rare. The arithmetic and reference-data signals have to carry the combined score above 0.90 for good lines. 161 / 162 should calibrate the weights on these bins, not on the 0.90 default.
- **Arm b's confidences are all below 0.70** (wrong rate 2.8 % below 0.50, 1.7 % at 0.50–0.70): not usable as a gate.

## 4. Latency and cost

| | a | b | c | c′ |
|---|---|---|---|---|
| Latency p50 | 17.7 s | 27.9 s | 25.1 s | 24.9 s |
| **Latency p90** | **36.5 s** | **61.9 s** | **49.2 s** | **53.4 s** |
| Latency max (item) | 186 s (A-07) | 738 s (A-07) | 341 s (A-07) | 364 s (B-0150) |
| **Cost per invoice, mean** | **$0.014** | **$0.192** | **$0.171** | **$0.183** |
| Cost per invoice, median | $0.006 | $0.069 | $0.065 | $0.069 |
| Cost, typical 1–4-page invoice | $0.006 | $0.073 | $0.068 | $0.071 |
| Cost, all 41 items | $0.55 | $7.86 | $7.02 | $7.48 |

- **Arm c cost split (41 items):**
  - gpt-5.2 output tokens $4.76 (68 %);
  - pages $1.28;
  - input tokens $0.72;
  - contextualization $0.26.
  - No embedding tokens: there are no labelled samples.
- **At 1,000 invoices a month** of typical size, c costs about $70 a month.
- **Latency:**
  - 3 parallel workers per arm.
  - The c′ run of B-0150 overlapped the start of the classify run, so its maximum is slightly inflated.
  - Both CU arms keep p90 under a minute, so SPA submission stays asynchronous (Processing), as designed.

## 5. Coding comparison: gpt-5.4-mini vs gpt-5.6-luna

Setup (`coding.py`):
- **Inputs:** ground-truth lines (narrative, title as billed, hours), so coding is measured apart from extraction.
- **Prompt and schema:** the closed code list goes in the prompt, and the output schema restricts it to an enum (FR-22).
- **Call settings:** reasoning effort low, 25 lines per call (FR-48 chunk), strict schema, both models on `spaarke-openai-dev`.
- **Corpus A:** set A fee applications cleared for model use (A-03, A-04, A-06, A-07), 499 lines, with bankruptcy task codes from task 018's seed scored against the code the firm **filed**.
  - Sub-codes outside the 21-code list are mapped to their parent: B145→B140, B155→B150, B115→B110, B161 / B164→B160.
  - Phase-only codes and a category with no UTBMS code are not scored.
- **Corpus B:** 35 synthetic set B items, 460 lines, litigation task codes plus activity codes, scored against the generator's independent answer key (`intended_codes`).

| | gpt-5.4-mini A | gpt-5.6-luna A | gpt-5.4-mini B | gpt-5.6-luna B |
|---|---|---|---|---|
| **Task code top-1** | **66.1 %** | **65.7 %** | **63.9 %** | **61.7 %** |
| Task phase (code family) top-1 | 74.3 % | 75.6 % | 74.1 % | 71.7 % |
| Activity code top-1 | — | — | 75.2 % | 76.1 % |
| Lines at confidence ≥ 0.90, wrong | 299, 21.7 % | 331, 26.0 % | 318, 27.0 % | 343, 31.2 % |
| Latency per 25-line call p50 / p90 | 3.5 / 5.2 s | 3.9 / 5.3 s | 5.8 / 7.0 s | 5.8 / 7.2 s |
| **Cost per line** | **$0.000185** | **$0.000047** | **$0.000255** | **$0.000067** |
| Cost per 1,000 lines | $0.19 | $0.05 | $0.26 | $0.07 |

The two models agree on 88 % (A) and 91 % (B) of lines. Where they agree they are right only 70 % (A) and 67 % (B) of the time.

Reading:

- **Both models are well below NFR-05 (task top-1 ≥ 80 %)**, and the gap between them (0.4–2.2 points) is too small for one run to separate. About a quarter of the misses (8–10 points) land in the right phase, on a neighbouring code. The high agreement between the models on wrong answers suggests many lines are ambiguous without matter context, or are coded differently by the firm than a model would.
- **Self-reported confidence is not a usable gate:** 22–31 % of "≥ 0.90" codes are wrong. A code must never be auto-accepted on model confidence alone (consistent with I-5 / FR-26).
- **Cost does not decide the choice.** Both are below $0.30 per 1,000 lines. luna is about 4× cheaper but slightly less accurate on B and worse calibrated.
- **Recommendation for 047:** keep gpt-5.4-mini (D-51) at low reasoning effort. Treat NFR-05 as at risk, since model choice will not close it. The levers are context, not model:
  - the resolved code set (firm codes → matter type → practice area, FR-22), so firm-specific codes and the matter's own set replace a generic 21–29-code list;
  - matter and phase context per invoice (for example, the phases the matter is in);
  - corrections as few-shot examples (D-64).
  
  Re-measure in 161 / 162 with the resolved code set in place. A per-line "suggested, flagged" presentation (FR-22) is the safe default until then.

## 6. Activity code as an analyzer classify field

`sprk_invoice_v1_cls` is a GA copy of v1 plus `TimeEntries.ActivityCodeClassified` (`method: classify`, enum A101–A112 with descriptions). It ran on the 21 set B items whose rows print no activity code (259 fee lines); answer key = `intended_codes`.

| | Classify field (analyzer) | Coding step gpt-5.4-mini | Coding step gpt-5.6-luna |
|---|---|---|---|
| Activity top-1 (259 lines) | 75.7 % | 77.2 % | 76.4 % |
| Confidence | median 0.61, max 0.888 — never ≥ 0.90 | — | — |

On the same 21 items, adding the field changed nothing in extraction: repaired fee-line accuracy 99.1 % and header 98.8 % for both v1 and the copy. It added about $0.005 per invoice (+8 %, mean $0.0705 vs $0.0653) and about 6 s at p90 (37.7 s vs 31.8 s).

**Reading:** the classify field is no better than the separate step, and it cannot use the per-matter resolved code set (FR-22): the analyzer's enum is fixed per analyzer version. **Keep activity coding in the coding Action (D-51); do not add a classify field to v2.**

**Deviation:** the spec says "the 28-value activity code". That is the LOC 2013 revised list (A101–A128), which is not seeded (DEF-005 / #1929) and has no answer key in the evaluation set. The experiment used the 12-value list the evaluation set is labelled with (A101–A112). The conclusion — a fixed enum inside the analyzer is no better than the coding step — does not depend on the list length.

## 7. Bundled PDFs: routing and segment confidence (research note §9.8 item 2)

**Corpus:** the 14 bundled spike items.
- 10 have a cover letter on page 1 followed by the invoice.
- 4 have the invoice followed by a one-page *statement of account*.

**Analyzers:**
- `sprk_invoice_router` (GA): `contentCategories` invoice → `sprk_invoice_v1`, plus `cover_letter` and `other`, with `enableSegment`.
- `sprk_invoice_router_p`: the same router created on the preview API, which also returns segment confidence.

`results/router.json`:

| | v1, no router (c) | v1p, no router (c′) | Router GA | Router preview |
|---|---|---|---|---|
| Header accuracy | 100 % | 99.4 % | 100 % | 100 % |
| Fee-line accuracy | 99.7 % | 99.5 % | 99.7 % | 99.7 % |
| Invoice page range exactly right | — | — | 10 / 14 | 10 / 14 |
| p90 latency / mean cost | 27.7 s / $0.068 | 29.7 s / $0.071 | 31.6 s / $0.070 | 34.0 s / $0.075 |

- **The custom analyzer extracts bundled invoices correctly without a router.** The cover letter or statement page adds no wrong lines or header values. The router is only needed to know *which pages* are the invoice.
- **Both routers made the same 4 errors.** All 4 are the statement-of-account bundles. The statement page was merged into the invoice (B-0019 on GA, B-0079) or returned as a second "invoice" segment (B-0043, B-0115, and B-0019 on preview). **All 10 cover letters were split correctly.** Fix: add a `statement_of_account` (remittance / statement) category to the router. A second invoice segment must never become a second invoice in 043.
- **Segment confidence (preview only) would not have caught the errors.** The wrong splits scored 0.78–0.99. Correct splits also scored as low as 0.78 (B-0010) and 0.90 (B-0067, B-0103, B-0139). This preview feature gives no usable signal here.
- **Source page numbers refer to the original file, not the segment.** For all 10 items whose invoice starts after page 1, `FirmName` grounding pointed to an original-file page: the invoice's first page in 7 (GA) and 8 (preview), and a later invoice page otherwise. No segment-relative page was seen. The 043 bounding-box contract (`{page, …}`, F-4) can use the page numbers as returned.

## 8. Preview-under-GA probe (research note §9.8 item 1)

| Call | Result |
|---|---|
| `GET sprk_invoice_v1p` (created on preview) with `api-version=2025-11-01` | **404 `ModelNotFound`** |
| `analyze sprk_invoice_v1p` with `2025-11-01` on B-0004 | **404 `ModelNotFound`** |
| `GET` / `analyze sprk_invoice_router_p` (preview) with `2025-11-01` | **404 `ModelNotFound`** |
| `GET sprk_invoice_v1` (created on GA) with `2026-06-01-preview` | **404 `ModelNotFound`** |
| Controls: `sprk_invoice_v1` on GA, `sprk_invoice_v1p` on preview | 200 (`v1p` workflow `standard.2026-06-01-preview`) |

**A preview-built analyzer does not run under the GA API.** The reverse also fails: a GA analyzer is not visible from the preview API, which contradicts the research note §9.6 statement. Analyzers are scoped to the api-version they were created with, and the analyzer listing is per api-version too. Moving between versions therefore means re-creating the analyzer (the JSON ports unchanged) and re-running the evaluation; there is no in-place promotion. The research note is annotated accordingly (§9.8).

## 9. Package publish-size delta (`Azure.AI.ContentUnderstanding` 1.1.0, NFR-12)

Procedure (bff-hygiene item 4):
- **Worktrees:** two throwaway worktrees at `origin/master` `f63b57f8d`, `C:\wt005m` (master) and `C:\wt005b` (master + `dotnet add package Azure.AI.ContentUnderstanding --version 1.1.0`).
- **Build:** `dotnet publish -c Release -o deploy/api-publish` in `src/server/api/Sprk.Bff.Api` (linux-x64, framework-dependent, from the csproj), dotnet SDK 10.0.101.
- **Zip:** PowerShell `Compress-Archive -CompressionLevel Optimal`, PDBs included.
- **Clean-up:** both worktrees removed and pruned afterwards. Nothing was changed on this branch.

| | Files | Zip | Uncompressed |
|---|---|---|---|
| master | 192 | 38,904,719 B (38.90 MB) | 129,938,144 B |
| master + package | 193 | 39,067,975 B (39.07 MB) | 130,437,140 B |
| **Delta** | **+1** | **+163,256 B (+0.16 MB)** | +498,996 B |

- The only added file is `Azure.AI.ContentUnderstanding.dll` (498,208 B). `Sprk.Bff.Api.deps.json` changed. There is no transitive package change: `Azure.Core` is already present.
- `dotnet list package --vulnerable --include-transitive`: no vulnerable packages on either side.
- **+0.16 MB is far below the +5 MB justification threshold**, and the total of 39.07 MB is below the 55 MB review trigger.
- Raw: `spikes/extraction/results/package-delta.json`.

## 10. The fallback, measured (supplementary arm a2: DI `prebuilt-layout` + gpt-5.2)

Arm a2 is the shape task 043 would build on a no-go: DI `prebuilt-layout` markdown, then an extraction Action on gpt-5.2 (reasoning effort low) with an FR-21 schema and per-field self-reported confidence.

| | All 41 items | 39 items ≤ 20 pages |
|---|---|---|
| Header | 98.2 % | 99.1 % |
| Line date / timekeeper / hours / rate / amount (repaired) | 53.6 / 53.4 / 51.7 / 30.8 / 57.0 % | 99.9 / 99.5 / 99.9 / 100 / 99.7 % |
| Line recall | 58.7 % | 100 % |
| Values at self-reported confidence ≥ 0.90, wrong | 3,879, 31 (0.8 %) | 3,491, 27 (0.8 %) |
| Latency p90 | 48.7 s | 32.9 s |
| Cost per invoice, mean / typical 1–4 pages | $0.111 / $0.052 | $0.067 |

**Reading:**

- On ordinary invoices the fallback is as accurate as Content Understanding and a little cheaper. Its self-reported confidence was also well behaved on this set.
- It fails NFR-04 on the full set for one reason: **it stops early on long documents**, returning 69 of 650 lines on B-0150 and 81 of 254 on A-07, both with `finish_reason: stop`. Fixing that needs page-range chunking and merging in our own code.
- It returns **no grounding**: no page or polygon per value. CU returned a `source` for 96.6 % of line values (date, hours, amount, timekeeper). The review UI's document highlight (D-58 layout A, F-4 boxes) and the "every value traceable to the PDF" story would need an extra text-to-layout matching step.
- The fallback is a credible no-go path, not a better primary.

## 11. Deviations and risks

**Deviations from the POML / spec:**

- **Arm a model.** Dev has no gpt-4o, so arm a ran on the `gpt-4o-mini` deployment, which serves gpt-4.1-mini (DEF-003). The `FinanceExtraction` system prompt never existed (#1918), so a short stand-in was used; the user prompt and schema are verbatim. Arm a's weaknesses come from its schema (no timekeeper, 5 header fields) and from stopping early on long text, so a stronger model would not change the verdict.
- **c′ without labelled-sample training.** No labelled samples exist yet, so c′ is the plain preview analyzer. Labelled-sample effects (GA in-context retrieval vs preview distillation) remain untested and belong to 161 / 162.
- **Activity list** is 12 values (A101–A112), not 28 (§6, DEF-005).
- **Scoring views.** NFR-04 is reported on the *repaired* view (printed fields after deterministic completion). The as-returned and complete views are given beside it so nothing is hidden.
- **Two extra runs:**
  - the repeatability re-run of c and c′ on 6 items (§2.4);
  - arm a2 (§10), added because the no-go option needs a number.
- **Owner gate.** Per the session brief, the run ends with the owner summary and a PROPOSED D-entry. The task stays in progress until the owner decides.

**Risks:**

1. **The GA margin is thin with very long documents in the set.** One 108-page fee application moves date, timekeeper and hours from ≈ 99 % to ≈ 92 %. Page-range chunking is the mitigation and is not yet measured.
2. **Ground truth.** The set is 37 synthetic items and 4 real ones, and A-07 has partial labels. Results on real law-firm invoices (161 / 162, UAT) may be lower; the dd/mm and hh:mm findings show template variety matters.
3. **Single run per arm.** Run-to-run variation is about ±1–2 points on the affected fields (§2.4).
4. **CU confidence is conservative.** Combined-confidence weights (D-47, D-69) must be calibrated, or the reject path will almost never fire.
5. **Coding (NFR-05) is at risk** independent of extraction (§5).
6. **Preview drift.** GA has no segment confidence or distillation. If a future GA adds them, adopting it means re-PUT and re-evaluation (§8), roughly the one-week cost in research §9.6.

## 12. Recommendation

**Recommendation: GO on Content Understanding, GA only (`2025-11-01` + SDK 1.1.0). D-49 and D-53 stand, and no §6.5 path-A exception is needed.**

Why:

1. **GA meets NFR-04** on the 41-item set, with deterministic repair:
   - header 97.6 % (target ≥ 95 %);
   - line date 91.5 %, timekeeper 92.1 %, hours 92.0 %, rate 100 %, amount 100 % (target ≥ 90 % each);
   - on invoices of ≤ 20 pages, 98–100 %.
2. **Its confidence is honest.** About 1 % of values at ≥ 0.90 were wrong, against 2.8 % for today's path. 96.6 % of line values come with page-and-box grounding, which the review UI needs.
3. **It is the only option that handled the 26-page invoice** (650 / 650 lines) without our own chunking code.
4. **Cost and size are small:**
   - $0.07 for a typical invoice;
   - p90 49 s, asynchronous by design;
   - the package adds 0.16 MB to the publish.
5. **The preview is better but not needed.** It is about 4 points better on date, timekeeper and hours, with systematic wins on dd/mm dates and hh:mm hours (§2.4). But GA meets the target, and the preview's extra features gave no usable signal (segment confidence, §7). A preview analyzer cannot be promoted to GA (§8), and the preview has no SLA. **D-53's trigger for a path-A exception ("only the preview meets NFR-04") did not fire.** Re-evaluate at the next GA (research §9.7 adoption trigger).

**Conditions the recommendation depends on.** These become 043 acceptance items if the owner agrees:

| # | Condition | Why |
|---|---|---|
| 1 | A deterministic completion step after extraction: hh:mm → decimal when hours × rate ≠ amount; rate from the invoice's timekeeper summary; arithmetic fill-ins. Each repaired value is flagged as repaired. | Without it, GA hours is 89.3 % (below target). |
| 2 | Analyzer **v2** (`sprk_invoice_v2`, re-PUT on GA via `Deploy-Analyzers.ps1`) with two description changes: Hours ("h:mm means hours and minutes; return decimal hours") and dates ("non-US invoices use day-month order"). Plus a post-check: a date outside the billing period gets day and month swapped when that fits. | Closes the two systematic GA errors (§2.2, §2.4). |
| 3 | Page-range analysis for long PDFs (`inputs[].range`, chunk size from the D-59 settings), merging the results. Re-measure a > 50-page document in 161 / 162. | The GA margin over NFR-04 depends on one 108-page document (§2.1). |
| 4 | If 043 routes bundles, the router has a `statement_of_account` category, and a second "invoice" segment never becomes a second invoice. Without a router, the single analyzer already extracts bundled invoices correctly (§7). | 4 / 14 wrong splits on both versions. |
| 5 | Combined-confidence weights (D-47 / D-69) are calibrated on the measured bins: CU confidence is conservative, and correct values mostly sit at 0.70–0.90. | Otherwise the FR-26 reject path almost never fires. |
| 6 | Coding stays a separate Action on gpt-5.4-mini (D-51), with no classify field in the analyzer. NFR-05 is flagged at risk (≈ 64–66 % task top-1, §5). The levers are the FR-22 resolved code set and context, to be measured in 161 / 162. | §5, §6. |

**What each choice means for task 043:**

| Choice | 043 builds | Cost / risk |
|---|---|---|
| **A. GO, GA only (recommended)** | `IInvoiceAi.ReadInvoiceAsync` on the GA CU client (SDK 1.1.0), `sprk_invoice_v2`, conditions 1–5 | As planned. Thin margin on very long documents until condition 3 is measured. |
| B. GO with a §6.5 path-A exception for the preview | The same, but against `2026-06-01-preview`: the GA SDK does not cover it, so REST or the beta SDK, plus a GA fallback analyzer kept in step | About +4 points on hard templates. No SLA. A preview analyzer is not callable on GA, so a forced migration means re-create + re-evaluate (≈ 1 week). Needs owner sign-off and a retirement watch. **Not recommended:** GA already meets the target. |
| C. NO-GO | The D-49 fallback: DI `prebuilt-layout` (`DocumentIntelligenceService`) + an extraction Action behind the same `ReadInvoiceAsync`, as measured in arm a2 | Equal accuracy on ordinary invoices and slightly cheaper. But our own chunking for long documents (otherwise 58.7 % line recall), no value-level grounding without an extra matching step, and confidence is model self-report only. The package is not needed. |

## 13. Azure resources created or changed by this task

| Resource | Change |
|---|---|
| `spaarke-docintel-invoices-dev` analyzer `sprk_invoice_v1p` | **Created** 2026-10-15T15:21Z on `2026-06-01-preview` (spike arm c′) |
| analyzer `sprk_invoice_v1_cls` | **Created** 2026-10-15T15:21Z on `2025-11-01` (classify experiment) |
| analyzer `sprk_invoice_router` | **Created** 2026-10-15T15:21Z on `2025-11-01` (bundled-PDF router) |
| analyzer `sprk_invoice_router_p` | **Created** 2026-10-15T15:21Z on `2026-06-01-preview` (router, segment confidence) |
| analyzer `sprk_invoice_v1` | Unchanged (read and analyze only) |
| Model deployments, Key Vault, app settings, Dataverse, BFF | None |

- The four spike analyzers are not referenced by any code or script. The owner decides whether to keep them; the suggestion is to delete them after the decision.
- `scripts/content-understanding/Deploy-Analyzers.ps1` never deletes, so deletion is a REST `DELETE` per analyzer. It was not done here.
- Approximate spend for the whole spike, at list prices: **about $32**.
  - Arms b / c / c′: $7.86 / $7.02 / $7.48.
  - Classify run: $1.48.
  - Routers: $2.02.
  - Repeats: $0.98.
  - Arm a: $0.55.
  - Arm a2: $4.54.
  - Coding: $0.26.

## 14. Reproduce (for 161 / 162)

From `notes/spikes/extraction/` with `az login`:

1. `python deploy_spike_analyzers.py NAME…` (create-only).
2. `python run_cu.py ARM [--ids …] [--only-failed]` and `python run_text_llm.py a|a2`.
3. `python coding.py run MODEL A|B`, then `python coding.py score`.
4. `python score.py --all`, `python classify_score.py`, `python router_score.py`, `python probe.py`.

Raw outputs:
- **Local-only items:** git-ignored, under `notes/evaluation-set/local/spike-005/`.
- **Synthetic items:** a stripped copy (fields and usage only) under `raw/`.
- **Per-call records:** `runs/*.jsonl`.
- **Scores:** `results/*.json`. These hold counts, rates and identifiers only.
