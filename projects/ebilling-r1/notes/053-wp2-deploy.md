# 053 — WP2 deploy to dev: evidence

Task 053 is partly done. Steps 2 and 3 ran on 2026-11-03 with the owner's OK, because a synthetic invoice failed with `ai.invoice.binding_missing`. Steps 1 and 4–13 are still open.

## Step 2: Actions (spaarkedev1, 2026-11-03)

| Action | Ensure-ActionRow.ps1 | Deploy-ActionMirrors.ps1 -DryRun afterwards |
|---|---|---|
| invoice-classify | already present | UNCHANGED |
| invoice-line-code | created, then PATCHED (prompt 5,838 chars, output schema 3,398) | UNCHANGED |
| invoice-codeset-match | created, then PATCHED (prompt 5,967 chars, output schema 1,883) | UNCHANGED |

After `Ensure-ActionRow`, the mirror deployer found nothing to change, so no separate live `Deploy-ActionMirrors` run was needed.

## Step 3: Bindings

- `Seed-PlaybookConsumers.ps1 -DryRun` against the full mirror would upsert all 39 rows. `-DiffOnly` before seeding showed 7 differences:
  - Missing in the environment: `billing-rule-draft`, `invoice-codeset-match`, `invoice-line-code`.
  - Missing in the mirror (other projects' rows): `assistant-suggest|default|`, `chat-summarize|reanalyze|*`, `create-project|default|*`, `create-todo|default|`.
- Only the two approved rows were seeded, from a filtered two-row copy of the mirror (`-MirrorFile`, then `-SkipConfirm`). The result was Upserted=2, Failed=0, exit 0.
- `-DiffOnly` afterwards shows 5 differences. Neither invoice row is among them. The remaining drift is not part of this step:
  - `billing-rule-draft` is not seeded. It needs its own OK, and its Action row must exist first.
  - The four rows missing from the mirror belong to other projects. Running `-Export` would bring them into the mirror. That is a separate decision.
- Read back: the invoice-classify, invoice-line-code and invoice-codeset-match bindings are each enabled, active, and resolve to their Action ("Invoice Classify", "Invoice Line Code", "Invoice Code Set Match").
- The dev BFF (5796774091) lists both consumer types in `ConsumerTypes.All` (`ConsumerTypes.cs:445-446`). `/healthz` returns 200.
- `ConsumerRoutingService` caches a binding for 5 minutes (`CacheDuration`). The next lookup after that window sees the new rows.

## Deviation

The step's "-DiffOnly (expect no difference)" is not met, because of pre-existing drift outside this step (listed above). Seeding the full mirror would also have created `billing-rule-draft`, which was not approved.

## Result after seeding: INV-2025-78232 (owner, 2026-11-03)

The owner submitted it from the Outlook pane twice after the seeding.

| Tracking | Submission | Created (Z) | Outcome | Invoice |
|---|---|---|---|---|
| .1 | c2cc6fa8 | 16:04 | Failed `ai.invoice.binding_missing` (before seeding) | none |
| .2 | 98449214 | 16:15 | Accepted | 25717fc3, now Superseded |
| .3 | e2cfc99d | 18:35 | Accepted, a correction of .2 (`resubmissionof` = 98449214) | 0d9178d3, In Review, `supersedesinvoice` = 25717fc3 |

Invoice 0d9178d3:
- **Header:** INV-2025-78232, firm Tennant Whitcombe PC, matter MAT-34929-0040, USD, invoice date 7/31/2026.
- **Totals:** fees $11,890.00, expenses $1,223.36, total $12,518.86. Both invoices carry the same values.
- **Lines:** 22 billing events, matching the synthetic PDF.
- **Code set:** "UTBMS IP Patent Prosecution", from the code-set match Action (`source` AiSuggestion, `needsReview` true). The matter is a Patent matter (practice area Intellectual Property Patents).
- **Coding:**
  - The 6 expense lines are Clean at 0.95.
  - The 16 fee lines are Flagged. The firm billed UTBMS Litigation task codes (L1xx and L5xx), which don't match the Patent set.
  - For 12 of them the AI mapped a Patent task code (for example L120 to 110103), each flagged `mismatch` at 0.85-0.9.
  - For the other 4 (L520 and L190) it proposed codes outside the set. Those were refused (`OutsideCodeSet`) at confidence 0.0.
  - This is the intended behaviour for a litigation-coded invoice on a patent matter. "L 120" (with a space) is read as billed.
- **Not checked here:** the review UI, which is WP4 (110, 111, 112).
