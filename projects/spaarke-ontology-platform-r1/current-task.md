# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-11-03 (main session, context-handoff at 85%). State only: history is in git log, task `<completion>` blocks and
> `notes/handoff-history/2026-10.md`. Standing rules: [`CLAUDE.md`](CLAUDE.md) §3 and §6. Decisions: [`notes/decisions.md`](notes/decisions.md)
> (D-1..D-199). Deferred work: one self-contained GitHub issue per item, label `ontology-deferred`, plus a DI-nn row in both tables of
> `notes/defer-issues.md` (next free: **DI-139**). Sub-agents never edit this file.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | **Billing-guideline compliance test (owner option C, 2026-11-03)**: ontology writes the synthetic guidelines GL-01..06, answer keys and synthetic reference data; ebilling seeds dev, publishes, submits and scores (their 161/162). Plan: `notes/billing-guidelines-compliance-test-plan.md`. Work lives in temp worktree **`C:\wto-gl`**, branch **`ontology/billing-guidelines-eval`** off `origin/work/ebilling-r1` (`ff4ff75005`); WIP pushed `0823a5e8d9`; the PR goes into **`work/ebilling-r1`** (not master). Commit there with `PATH=/c/code_files/spaarke-wt-spaarke-ontology-platform-r1/node_modules/.bin:$PATH` (that worktree has no prettier; never --no-verify). Remove it later with `Remove-WorktreeSafe.ps1`. |
| **Status** | Done in WIP: `generator/build_guideline_data.py` (subset 6 x (15 seeded + 5 clean) = 120 Set F invoices, clean picks maximise A110 lines; reference data per RS = firms with 078 workbook JSON {Timekeepers, Rates} with the exact column names, per-timekeeper approved rates because Senior Associate is not a LEDES classification, classification rows too, roster from 2024-01-01 open-ended; matters with billing status Final where CoreRecordWindow applies) and `generator/production_key.py` (production-faithful re-label, read-only on Set F; summary: AiObservation 24 reproduced (proxy), ApprovedRate 40 + 1 extra, ApprovedTimekeeper 16, CoreRecordWindow 11 + 9 not expressible (budget), DailyHoursCap 11 moved (first line of the group), Duplicate 23, ExpenseRule 9 + 11 n/e, InvoiceAge 10 + 13 n/e + 4 missed, NonBillable 20 + 41 over-flag (A110) + 3 n/e, RequiredData 9 + 14 n/e (PO), TimeIncrement 43). Owner answers: 1 option C; 2 targets as recommended (90% mechanical, 75% AiObservation, <=2% clean false alarms, consequence correct); 3 six guidelines for different law departments (size, spend, matter mix), must match the existing invoices, no regeneration, web research; 4 budget fine, subset only (not 600); 5 synthetic firms/timekeepers/rate cards by Claude, research-grounded. Researcher agent (sonnet) is running: law-department profiles, rate benchmarks, roster structure, provisions by department type (P1..P6 profiles). Uploads accept .pdf and .docx (RuleSetService:2880). |
| **Next Action** | 1) When the researcher returns: write `guidelines/GL-01..06.md` (persona framing per researcher; every RS rule and parameter verbatim in prose as numbered clauses; non-rule provisions for the drafter's "not converted" list; rate schedule appendix matching the reference data; RS meta titles unchanged), a renderer `generator/render_guidelines.py` (reportlab, to local/guidelines/*.pdf, git-ignored), guideline keys `guidelines/keys/GL-0n.json` (Set E format: rules with clause_ref + production bodies from PRODUCTION_RULES, unmapped_provisions), `guidelines/README.md`, a gap report with counts, and a dated note in `generator/manifest-notes.md` §7. 2) Send ebilling the not-expressible list with counts (they triage PO RequiredData and InvoiceAge-by-received-date as likely product gaps; ApprovedTimekeeper Reject is D-89 by design). 3) Open the PR into `work/ebilling-r1`; ebilling reviews. Other open: owner UAT on dev `5796774091` (168/173 runs A/B/C, Tony Howe Assign Task on CMRCL-479772 expecting 50395 and no bell, BUD- budget, 170 latency, re-checks 158-163/166, 114, 111 row 4; then 105, then 090). ebilling peer threads answered: D-201 assign-firm (095d), D-202 record-rule code `BRR-{policyId:N}` (089; warned base-code Signals are likely never re-evaluated: billing policies skip the nightly job), 095c no-firm flow (agree skip; 4 corrections sent). Reviews pending when they arrive: ebilling 095a (sourceemaildocument guard), 095c-3 (firm_missing code/template). Docs head `893da14d00`. |

## Task 168 decisions: D-199 (owner, 2026-10-29)

Q1 = A (an eligible internal user, or an onboarded external contact with a grant on the matter and a portal link); Q2 = the system-mailbox fallback to the same recipients. Built and merged (see the 168 POML completion block).

## Lane facts to carry

- **Docs now contains master through 194b** (`2cd0f62e2c`, merge `4470c95b86`). 194b moved `ResolveSecureBusinessUnitAsync` to `SecureRecordOwnerTeam`; our call was repointed in `f1133d951b` (uac-r2 approved). ArchTests 1665/1665 on that state.
- **Dev BFF** `136568cf70` (R3, live 02:23:53Z). Smoke PASS (deploy-log `83e3e409cb`). **Pending:** the task-170 commit-path check (before: 28 WhoAmI, 22.4 s per commit) and the `BUD-` number need the owner's next real decision commit. Then query App Insights `spe-insights-dev-67e2xz` (rg `spe-infrastructure-westus2`) for `POST /api/v1/signals/{id}/decision` after 02:23:53Z.
- **SpaarkeMaster 1.2.4.0 stamp check:** customer-provisioning built `Test-RolePrivilegeExpectations.ps1` + `spaarkemaster-1.2.4.0.json` (`b6e94f4499`). Stamps run 1.2.3.1, so the upgrade REMOVES Basic User `prvReadsprk_Policy` (D-189 note `53c01465dc`). They will send ACME's result after its upgrade.
- **uac-r2 dev move:** root-homed humans move to BU1 (owner approval pending, no writes yet; they give notice first). The seed policies likely stay root-owned (`DoNotMoveAllRecords=true`). After the move, BU1-homed Ontology Admins (Deep) can't read root-owned seed policies; uac-r2 is preparing an owner question and will copy us. **Hold the memo-LIKE proof** until they confirm placement.
- **CI:** `ontology-script-tests` is pinned to Pester 5.7.1 (#2428). DI-134 (#2430) asks the CI owner to pin `verdict-script-tests`.
- **Don't push to docs while a docs→master PR is open** (it moves the PR head and restarts CI).
- **Ordinary moves on dev (uac-r2):** both switches on since 2026-10-30 19:53:19Z (owner approved in uac-r2's session). Ontology re-read: **PASS** (9 WA-core Signals root team → BU1, owner only; `notes/ordinary-moves-baseline-2026-10-30.md`).
- **Commits in a fresh worktree:** lint-staged needs prettier: `export PATH="/c/code_files/spaarke-wt-spaarke-ontology-platform-r1/node_modules/.bin:$PATH"` (no junctions). Integration tests under `tests/integration/auth/**` compile into `tests/unit/Sprk.Bff.Api.Tests`; filter there.

## Next docs→master

The last was PR #2463, merged as master `c8bb55d0cd` (2026-10-29): 168 and 173. Before the next: notices to R3, uac-r2 and word-add-in (current name `spaarke-wt-spaarkeai-word-add-in-r1-8a`); merge master in; build, the filtered Signals tests and the full ArchTests (1858 now).

## Blocked follow-ups (ours)

- `OntologyAdminClassificationService.cs:658/685/705` waits for `sprk_publishstate`. Five stale `DecisionRecordWriter.cs` comments wait for ebilling 074. Close #2212 when ebilling `f0884a175f` reaches master.
- Owner re-checks still open: project create as a Core User (#2394), check 6 (the composer), then close 114, 158–163 and 166.

## Dev state (spaarkedev1 / spaarke-bff-dev)

- **BFF:** `136568cf70` (R3, 2026-10-29). **Console:** `a8314c2e0e`. **SpaarkeMaster:** 1.2.4.0. **POL-MATTER-NOBUDGET:** v3 `…6313`.
- **Worktrees:** none for lanes. Remove only with `Remove-WorktreeSafe.ps1`, one path per call.
- **Stash:** two old `lint-staged automatic backup` entries (20 and 30 h old) are not ours. Leave them.

## Peers (current session names)

ebilling `spaarke-wt-ebilling-r1-5b` · R3 `spaarke-wt-spa-external-access-platform-08` · uac-r2 `spaarke-wt-unified-access-control-r2-ac` ·
word-add-in `spaarke-wt-spaarkeai-word-add-in-r1-c4` · field-mapping `spaarke-wt-field-mapping-server-write-pa-be` ·
customer-provisioning `spaarke-wt-customer-provisioning-orchest-2f`. Send before and after notices to word-add-in, R3 and uac-r2 for any dev BFF,
Console or landing change. word-add-in needs no after-notice for a Console-only change.
