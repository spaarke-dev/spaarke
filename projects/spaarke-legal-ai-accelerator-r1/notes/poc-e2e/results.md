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

**Step 3 (done 2026-11-05).** Built `core/engines/playbook_import.py` (7 tests) and added an optional `aliases` field to `topics.yaml` (`housekeeping`, `one-sided-carveouts`).
- **Cowork's playbook:** 32 rows: 15 mapped, 3 merged, 10 gap, 4 extra. With `--baseline`, residuals is taken from our baseline, so 24 topics are written and none is silent. The mapping matches the manual comparison above.
- **Fallback suggestions:** 12, where our baseline has a fallback the import lacks.
- **Rows to check:** 1, 2, 3, 4, 5, 12, 13, 18 and 21 (merged, extra or weak matches).
- **Our table format:** 24 of 24 rows mapped by their explicit topic ids.
- **Word's output:** 24 of 24 rows mapped by keyword.
- **Rendered back** to our Word heading format: `evidence/act1/Acme Mutual NDA Review Playbook (imported, Spaarke format).docx`. The Markdown and the import report are beside it.

**Step 4 (owner, Word desktop, 2026-11-05).** Word's `/` **Create playbook** behaves differently depending on the file:

| Input | What Word did |
|---|---|
| `Spaarke NDA – Mutual v1.0 (table).docx` (our four columns, plus title, scope and version lines, plus `(nda.xxx)` ids) | Ran **create-playbook synthesis**: it treated our playbook as a single precedent. It wrote a new `Documents/Copilot/Created/mutual-nda-review-playbook.docx` with 24 topics, every one "Counsel decision — single-instance evidence", and no saved-playbook card. The compromise positions survived as content. It listed 8 "topics not addressed in the source", among them the public-domain exclusion, which our playbook does cover inside other topics. Copy: `evidence/act1/word-create-playbook-from-table.docx` |
| The same file again, in the same chat | "The playbook has already been created and validated": no new run |
| Our heading format `Spaarke NDA – Mutual v1.0.docx` | Owner confirmed this was the file used for item 1. Not imported directly (see the follow-up below) |
| Cowork's `mutual-nda-review-playbook.docx` | **Imported directly** as a saved-playbook card named **"Acme Mutual NDA Review Playbook"** (the document's Title), with Save and run review / Save playbook. So Cowork-to-Word works |
| Review run with the imported playbook | All 32 topics "Not relevant": a blank document was open (owner confirmed), so this is expected |

Our files carry no property marker that differs. The visible differences are the preamble (scope and version lines, Heading 1 versus Title) and the ids in the Topic column. A/B variants are in OneDrive `Documents/Cowork/Test/POC-Act1/word-ab/`:
- `A - title only.docx`: Title-style heading only, no ids;
- `B - no ids.docx`: the original preamble, ids removed.

If A imports directly and B doesn't, the preamble is the trigger.

**Owner follow-up (2026-11-05):**
- Item 1 was run on the heading format `Spaarke NDA – Mutual v1.0.docx`. Word's own coverage line names the "(table)" file, so possibly both were tried in one chat. Either way, **neither of our formats was imported directly**.
- The review in item 3 ran on a **blank document**, so "Not relevant" is expected (gap #12: information only).
- The stray `nda-review` was deleted by the owner; Cowork now lists 11 added.

## Act 3

### Step 1: T-P13 visual probe (2026-11-05, 30 seconds)

"Run the Spaarke visual probe (T-P13)" routed to our personal skill `spaarke-visual-probe`. It wrote 10 files to `Test/T-P13/`.

**Sandbox:**
- Python 3.12.15, with lxml 6.1.3, python-docx 1.2.0, **openpyxl 3.1.5, python-pptx 1.0.2, matplotlib 3.11.2**, pandas 3.0.6, PIL 12.3.0, pypdf 6.19.0, pdfplumber 0.11.10 and numpy 2.5.3. No plotly.
- Tools: node v24.7.0, soffice, pdftoppm, pandoc, tesseract. No Chromium.

| Format | Result |
|---|---|
| PNG / SVG in chat | **File only.** Neither shows inline in the chat |
| Markdown table in chat | Renders |
| Mermaid in chat | **Renders as a diagram** |
| LaTeX in chat | Renders (inline and block) |
| `dashboard-static.html` in the preview | Renders, and the SVG bars and labels show. **But the HTML text shows white on the page's light background:** the preview applies dark-theme text colour and keeps the page background. Text needs an explicit colour wherever a background is set |
| `dashboard-script.html` in the preview | **The inline script runs, the button works, and `fetch()` to the internet succeeds (HTTP 200).** The CDN `<script>` does not load |
| `chart.xlsx` (our `xlsx_lite`) in the preview | Native bar chart and coloured cells render (Excel for the web) |
| `chart.pptx` (python-pptx) in the preview | Chart renders (PowerPoint for the web) |

**What this means:**
- Interactive dashboards are possible (inline JavaScript).
- The preview is **not network-isolated**: an output page can call out to the internet. Keep our generated HTML script-free (OD-30 stands); that is now a security rule, not just a style choice.
- Cards (V3) are not needed for charts. Mermaid gives diagrams in chat, and the Office previews give charts in files.

### Steps 2–5: review the Brightwater NDA (2026-11-05, Claude via Chrome; about 5 minutes)

**Prompt** (new task, no skill named): `Review this NDA: "Brightwater Mutual NDA.docx" in the Acme Company team site, Shared Documents/Legal Knowledge/Matters/Brightwater Systems/.` Plan text: `evidence/act3/plan-chat.md`.

| Check | Result |
|---|---|
| **T-P2** routing | **Ours.** Cowork chose `Spaarke NDA (MVP) (nda-review)`, with Legal for Cowork ON and Microsoft's `review-contract` available. Connectors: SharePoint, Work IQ |
| **T-P10**, our skill | **Yes.** It read the profile, then `Spaarke NDA – Mutual v1.0.md` from the team-site `Playbooks/NDA/` folder. The personal fallback folder is empty, so no fallback was needed |
| Provenance line | "Standards: Spaarke NDA – Mutual v1.0 (customer playbook) · Skill: nda-review v0.1.2-poc", plus the profile defaults, the "test positions" warning and the hidden-text scan |
| Plan preview | Chat table of 12 changes in priority order, the escalations, the fallbacks accepted with no edit, 5 open questions, then an **Approve and build / Change something / Skip** card. No file was built before approval |
| Saving | One approval card for 5 uploads ("Approve All (5)") into the **matter folder beside the original**, plus a copy in `Cowork/Tasks/<task>/output/` |
| Cover email | Cowork offered: Email to me / to someone else / Draft only / No email. We chose No email |
| `check_outputs` | Passed, per the skill: every change is tracked, 12 comments, the clean copy equals the redline with changes accepted, and the external copy has no comments |
| Cost | `/cost` = **122 credits** (2,697 this month) |

**Scoring against `answer-key.md`** (from the audit pack's Topics sheet; scratchpad `act3_score.py`):
- 24 topics rated: **7 RED, 6 YELLOW, 11 GREEN**. The key has 6 RED and 7 YELLOW.
- **12 of 13** flagged topics match. The miss: `nda.representatives` was rated RED where the key says YELLOW. The Brightwater 4.2 clause is word for word our **Fallback** ("bound by written or professional obligations"). It says nothing about responsibility, and the model read that silence as our **Prohibited** "No responsibility for Representatives". This is a playbook-wording ambiguity, not a careless model (gap #16).
- No false GREENs: every topic the key flags was flagged.
- Not raised: the policy findings (NDA-3.3 to 3.5) and General Counsel approval (NDA-4.3). `nda-review` does not read the policy document; it is not in its inputs. Act 4 step 3 tests grounding on the policy.

**Outputs and fidelity:**

| Output | Cowork preview | Word or Excel desktop (COM, from `evidence/act3/`) |
|---|---|---|
| `Redline with Comments (internal).docx` | Opens | 5 pages; 15 tracked revisions (10 insertions, 5 deletions); 12 comments; page-1 summary table with 12 working links; 10 bookmarks |
| `Redline.docx` (external) | Opens | 4 pages; the same 15 revisions; **0 comments** |
| `Clean Revised.docx` | Opens | 4 pages; 0 revisions |
| `Review Dashboard.html` | **Renders in dark and light** (the preview has a theme toggle): tiles 7/6/11/12/1, both bar charts in rating colours, the escalation table and the change list. No script and no `on*=` handlers. Its only URL is a link to the source NDA | — |
| `Audit Pack.xlsx` | Renders (Excel for the web): 4 sheets, coloured rating cells. **Polish:** "Topics by rating" bars were all red, and the second chart sat at column L, off screen (gap #17, fixed) | — |

Gap #14 (white text in the preview) **does not affect our dashboard**: its CSS switches text and background together.
