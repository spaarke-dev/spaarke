# Current Task State — `unified-access-control-r2`

> **Format:** CURRENT state only. Rewrite at each checkpoint, never prepend; ≤10 KB. Standing rules: project `CLAUDE.md` (§2 Binding rules, §3 Owner directives, §5 Environment, §6 Gotchas). Decisions: `notes/session27-owner-decisions-and-research.md` (rounds 1–96) and `notes/decisions.md`. Live records: `notes/batch5-live-gates-2026-10-08.md`. Narrative: checkpoint commit messages.

> **Last Updated**: 2026-10-13 (checkpoint #27, after compaction).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Mode** | Round 90 wrap-up: fix only findings that affect uac-r2 or break a real path; file everything else as an issue. A task closes on merge + deploy + server-side live gate. |
| **Running** | (a) Task 184 executor, agent `a2bd29e032484065c`, worktree `C:/wt184`, branch `uac/184-e11-intent-and-existing-ciam`. It must not run the schema `-Apply`. (b) Task 183 verifier pass 1, agent `a6bd48068a1ce5b9b`, on PR #1797. |
| **Next Action** | When the 183 verifier reports: resume the 183 executor for ONE fix round covering (1) F3.1, two RefusedSecure cases plus the AlreadyOwned-order mutation; (2) F3.2, a cancellation test plus the filter-inversion mutation; (3) a NotFound outcome; (4) K2→F, a row-version-conditional owner write with a `Conflict` outcome, a test and a mutation; and (5) the pass-1 findings. Then verifier pass 2 (required: security and tenant-isolation tags), then send the fix diff to the ontology session, which merges into `docs/ontology-platform-design`. |

## Active lanes
- **#1746 (ontology Signal suppression):** UAC review is CLEAN at `8e24a42d9`, sent to the ontology session and posted as comment 6139088001. The ontology session merges once CI is green on that head (Router, full unit tests, ArchTests). If the 5 uncaptured ArchTests failures come back, they send the names.
- **#1797 / task 183 (ReownOrdinaryRowAsync, base `docs/ontology-platform-design`):** see Next Action. Outcome mapping 148 uses: Reowned, AlreadyOwned→AlreadyApplied, RefusedSecure→SkippedSecure, RefusedNotTeamOwned→SkippedUserOwned, Failed→RepairFailures, NotFound→SkippedGone, Conflict→SkippedConflict.
- **Task 184 (E11 intent gate + ExistingAccount invite):** the owner chose a pending-invite column, `contact.sprk_externalinvitedon` (DateTime). The BFF writes it and never clears it. It is field-secured with the existing BFF-Managed profiles and added to SpaarkeMaster and SpaarkeCore. After the PR: two verifier passes (auth, security, tenant-isolation); send the PR to provisioning for the cross-customer review; the main session applies the schema script on dev; then merge, deploy and live gate. NOTE: the POML's "no new column" constraint is superseded by the owner's round 96 column decision.
- **Model 1 workforce sign-in (word-add-in T126):** discussion open with the owner.
  - The BFF accepts only tenant `a221a95e`. The workforce realm signs in via `/organizations`, which gives the home tid, so the BFF returns 401 IDX40001. This was live on dev on 2026-10-13.
  - Recommendation A: home-tenant sign-in, the BFF accepts the listed customer tenants on external routes, 141's member test, a registration form at first sign-in, and the customer sees a workforce contact.
  - Option B: guest invite plus a recorded oid set.
  - Open questions: A or B? Do registrations need approval? What is the baseline access?
  - Provisioning is holding H11 until this is decided. The 401 must be fixed or filed.

## Done this period
- #1665 merged (`aeb9e0973`).
- #1780 / task 182 merged (`a3c9aaa24`).
- The #1732 live gate passed (2026-10-12).
- The Q27 `sprk_createdbyperson` field permissions are complete on dev.
- Issues filed: #1777 and #1779.

## Owner decisions (round 96, in the notes)
- D-134/D-135: the personal To Do decision-record owner is the assignee's BU team, follow on reassign, fall back to the To Do's team. "Personal" means filed under nothing.
- Q27 is decided.
- E11 uses the pending-invite column.
- Do NOT run `Set-ExternalFlagForB2BGuests.ps1 -Apply` in Model 1 (this rule is in the project CLAUDE.md).

## Open owner items
- The Model 1 questions above.
- #1747 (word-add-in sign-off).
- #1776 (Dev 1 and Test 1 BU team roles).
- F7, F9, CIAM emails G8, banner re-check 153, Remove-TestContainers, the checklist, 165(d), 171.
- Optional: file an issue for the legacy `spaarke_core` solution; update `scripts/README.md` (it says Set-ChildRecordCreatorPersonSchema was never run).

## Not started
178, 136, 036, 037, 039, 099, 090 wrap-up, and 013/058 bookkeeping.
