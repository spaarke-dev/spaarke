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
