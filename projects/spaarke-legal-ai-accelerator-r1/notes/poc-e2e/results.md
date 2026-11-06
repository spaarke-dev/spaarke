# End-to-end POC results

Per act: what ran, what came out, and how it compares with the expected result. Gaps go in `gap-log.md`.

## Act 0: setup (2026-11-05, Claude via Chrome and the synced OneDrive)

| Step | Result |
|---|---|
| Kit upload | 32 files to Acme Company › Shared Documents/`Legal Knowledge/`, through SharePoint REST from the browser. A listing confirms all 32 files, 16 of them in `NDA batch`. Nothing from `answer-key/` or `kit-held/`. SharePoint adds about 9 KB of document properties to each `.docx`; the `.txt`, `.eml` and `.md` files match byte for byte. |
| Profile | `Playbook folder:` now points to the team-site folder, falling back to the personal one; Role Mutual, Posture Balanced, no longer defaults. Backup in `Cowork/Test/_parked-2026-11-05/`. |
| Personal skills | `nda-library-review` (16 files) and `spaarke-visual-probe` (3 files) in `Cowork/Skills/`; both appear under Cowork › Plugins › Added. |
| Plugin | `spaarke-nda-mvp` 0.1.2 published to "Only you". |
| Legal for Cowork | Already ON (notes said OFF). |
| Preferences | Copilot › Settings › Personalization › Customize Copilot had been empty. Now one line: the playbook name and team-site path, plus the profile path. It names no skill, so it doesn't bias T-P2. |
| Stray skill | A Cowork-authored `nda-review` from 11-04 (no publisher, one reference file) was deleted from OneDrive. Cowork still lists it as its own entry, and the Cowork-side delete is permanent, so that click is the owner's. |
| Quota | Cowork showed "8% of monthly limit remaining", a per-user limit in the M365 spending policy. The owner raised it, and `/cost` now reports unlimited. |

## Act 1: build a playbook from precedents (2026-11-05)

**Run.** New Cowork task, no skill named. Input: the five documents on Acme paper (the model form and the four executed NDAs) on the team site; the Fernhill markup was left out. The runbook said "the three most consistent". We used five because `create-playbook` needs at least three precedents agreeing on a topic before it calls a position the standard, and the two variant deals supply the alternatives.

- **Routing:** Cowork chose Legal for Cowork (`create-playbook`) with the SharePoint and Word connectors.
- **T-P10, Cowork side: yes.** It found and read all five files on the team site from their names and folder path.
- **Question card:** three questions, answered from our baseline:
  - 2-year term: Approved compromise;
  - 12-month non-solicit: Acceptable if asked;
  - topics no precedent covers: Include as gap rows.
- **Approvals:** three prompts while saving (create `Test`, which already existed; create `POC-Act1`; upload a new file). It also kept a second copy in `Cowork/Tasks/<task>/output/`.
- **Cost:** `/cost` = **262 credits** for the task; 2,563 used this month.
- **Output:** `Documents/Cowork/Test/POC-Act1/mutual-nda-review-playbook.docx`; copy in `evidence/act1/cowork-create-playbook.docx`. Title "Acme Mutual NDA Review Playbook", "Draft for counsel review" header, one table with 4 columns (Topic | Instructions | Example Text | Compromise Positions) and 32 rows. No page numbers, which it said itself.

**Comparison with `Spaarke NDA – Mutual v1.0` (24 topics):**

| Result | Topics |
|---|---|
| One row each (16) | definition-unmarked, purpose-limited-use, representatives, compelled-disclosure, reverse-engineering, copying-and-breach-notice, no-licence-no-warranty, return-destruction, term (and a separate disclosure-period row), non-solicit, remedies, whistleblower-dtsa, trade-secret-preservation, assignment, governing-law-venue, confidentiality-of-agreement |
| Merged into another row (6) | oral-disclosure → definition; exclusion-prior-possession, exclusion-independent-development, exclusion-third-party, one-sided-carveouts → one "Exclusions" row; data-use-licence-back → "Use restriction" |
| Partial (1) | housekeeping → "Execution": signatory and names, but nothing on another deal's leftover names or signatures |
| Wrong frame (1) | residuals → a "no Acme precedent position, escalate" gap row. Our position is **no residuals clause**; the precedents carry that position by leaving the clause out, and the skill reads absence as silence |
| Extra rows (4 + 10) | agreement form and mutuality, purpose, disclosure period, entire agreement; gap rows for liability cap, residuals, standstill, securities trading, personal data, export control, no obligation to proceed, termination right, notices, severability and waiver |

**Fallbacks.** Only the two compromises we approved appear. Signed agreements show where deals ended up, not what was conceded, and the skill says so. On about 9 topics, our baseline fallback becomes "reject" or "escalate" in Cowork's draft: marking with a 30-day designation, a written summary of oral disclosures within 30 days, aggregated data, prior knowledge without records, a third-party exclusion without known breach, notice-only compelled disclosure, return without a deadline, the other party's home-state law, and consent not unreasonably withheld. So a precedent-built playbook is **stricter** than ours until counsel adds fallbacks.

**Join key.** No topic ids, and row names are the model's own wording. Without a mapping step, a Cowork-built playbook can't be compared topic by topic with `review.json` or with the library grid.

**Step 3** (convert with `playbook_import.py`, then render again) could not run: the script doesn't exist. See gap-log #9.

**Step 4** is the owner's, in Word desktop: New playbook › import each format. Files on the team site under `Playbooks/NDA/`:
- `Spaarke NDA – Mutual v1.0.docx` (headings);
- `Spaarke NDA – Mutual v1.0 (table).docx` (four columns).

Also import Cowork's `mutual-nda-review-playbook.docx` from OneDrive `Documents/Cowork/Test/POC-Act1/`. For each, log the saved name, whether the compromise or fallback column survives, and what General guidelines contains.
