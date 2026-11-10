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

## Act 2: Word (owner review, 2026-11-06)

**Not run as originally scripted.** In the act-by-act review, the owner judged that the Word feedback was already captured by the Act 1 discussion (A1-1 to A1-15) and by Acts 3–6. The findings:
- **Routes.** Word's saved-playbook picker exists (`/Review contract` → "Which playbook do you want to use?"), but it is per user. There is no organization-wide playbook (researcher, 2026-11-06).
- **Our files.** Neither of our formats imported directly. Cowork's `create-playbook` file did (gap #11).
- **Users** have to pick the skill first and then the playbook, so training is needed (quick cards).

**Assimilated into design v0.6:**
- §2.4.6 (revised Word approach);
- §5.3 (four-column template);
- §5.9 (`Legal Knowledge/` tree);
- §8.1a (setup modes, quick cards);
- §12 (Word test set).

Package items X1–X6 and 5-5 are in `package-backlog.md`. The remaining Word work is the focused **test set R1–R7, A/B, G1 and G2** in the runbook's Act 2. It is owed by the owner in Word desktop, and its results go here.

### Word test set (owner, Word desktop, 2026-11-06/07; script `word-test-script.md`)

Screenshots are in OneDrive `Documents/Cowork/Test/POC-Act2/screens/`. The reviewed copy is
`POC-Act2/Brightwater Mutual NDA - Word review.docx`.

- **P3, before.** The saved playbooks were "NDA - Mutual (Spaarke baseline)" (Nov 1, named after our MVP file's title)
  and "Acme Mutual NDA Review Playbook" (Nov 5). There was no saved "Spaarke … v1.0", contrary to the script.
- **R2, team library.** Attach from Cloud opens the SharePoint file picker. **Acme Company is listed under Quick
  access**, and the owner browsed straight to `Documents › Legal Knowledge › Playbooks › NDA`.
- **R1 = A, imported directly.** `/Review contract` → New playbook → Attach from Cloud → the new four-column
  `Spaarke NDA – Mutual v1.1 (table).docx`. Word reported "I've created a playbook based on …" and showed a card named
  **"Spaarke NDA – Mutual v1.1"** (from the Title), with Save and run review / Save playbook. It was not synthesised as a
  precedent (unlike 11-05 via `/Create playbook` with the old layout).
  - 24 topics plus **General guidelines**, which is our scope paragraph verbatim.
  - Fields per topic are **Name / Rules / Examples**, all editable (with Delete topic): Instructions → Rules, with
    the "Escalate if" line kept; Example Text → Examples.
  - **Compromise Positions dropped** (gap #30).
  - Long text is accepted.
- **R4, save.** "Saved" shown in the card. **No file was created in OneDrive**: the saved playbook lives in Copilot's
  own store.
- **R3, review** with the saved v1.1. Status per topic: Acceptable / Needs review (n) / Not acceptable (n) / Not
  relevant. **20 of 24 match the v1.1 key.**
  - **Term GREEN** (the v1.1 change was honoured). **Representatives YELLOW is correct**; Cowork gets this wrong (gap #16).
  - Misses:
    - independent development: Needs review, where the key says RED. Its RED edit sits under one-sided carve-outs;
      Word shows "This edit overlaps with…" (cf. gap #27);
    - compelled disclosure: Needs review, key RED (it still made the full edit);
    - reverse engineering: Not relevant, key RED (missing clause; gap #31);
    - non-solicit: Not acceptable, key YELLOW (dropped fallback; gap #30).
  - **Edits:** suggestions with Apply, or **Apply all › Apply edits / Apply edits with comments**. With comments:
    **12 insertions, 17 deletions, 13 comments**, tracked and attributed to the user (Cowork Act 5: 12 revisions,
    13 comments).
    - Good: Effective Date "[CONFIRM EFFECTIVE DATE]" with a comment; governing law California → Delaware;
      compelled-disclosure notice; return and destruction with certification.
    - Over-reach: deleted the whole non-solicit (§10) and the residuals section, including headings, without
      renumbering.
- **R1b, topic id.** The "ids in Instructions" variant imports the same way, but **the "Topic ID: nda.term" line is
  stripped** from Rules, though it is in the source. Topic ids can't travel through Word, so the template stays at
  `topic_ids="none"`.
- **R5, persistence.** In a new session the picker lists three playbooks, including v1.1. Picking it goes straight to
  Run review **without asking for a file**: the saved playbook stands alone.
- **R6, copy or link (2026-11-07).** Claude overwrote the library file in place, keeping the same item
  (`6b17d5a3…`, version 2.0), with the Term instructions prefixed "SNAPSHOT-TEST:". The saved v1.1 Term rules **did
  not change** (`R6-term.png`).
  - **The saved playbook is a frozen copy taken at import.** Library updates never reach users who already saved it,
    and each user re-imports.
  - The library file was restored afterwards (RestoreByLabel 1.0 → version 3.0, 50,718 bytes, same item).
  - **The only staleness signal is "Last updated on <date>"** in View all › Playbooks. There is no source link,
    version or "newer available" notice. The stale "NDA - Mutual (Spaarke baseline)" (Nov 1) is the MVP layout
    (Priority / Preferred position / Prohibited / Rationale in Rules) and **says 3 years where v1.1 says 2**. A user
    who picks it gets the old position silently. The version in the card name (from the Title) is the only thing that
    tells cards apart.
  - **No delete or replace for a whole playbook in the chat UI.** View all › Playbooks lists the cards (name, date,
    chevron), and the open card has only per-topic Delete topic. Stale cards accumulate.
- **R7, no slash command (2026-11-07).** In a new chat the owner attached the library `(table).docx` with the
  paperclip and typed "Review this NDA against attached playbook".
  - **Word routed it to Review contract on its own.** It said "I've created a playbook based on …", showed the card
    "Spaarke NDA – Mutual v1.1" (Save and run review / Save playbook), and the owner clicked **Run review without
    saving**. It produced the same per-topic findings, View rules and suggested edits with Apply.
  - Nothing is applied until the user clicks Apply.
  - **This is the answer to gap #32.** Attaching the library file each time needs no `/`, no saved card and no
    re-import after an update, and it always uses the current library version.
  - **Score: 19/24 vs the v1.1 key.**
    - Now right, though R3 missed it: **reverse engineering RED** (Not acceptable). Gap #31 is intermittent, not
      systematic.
    - Still wrong: independent development (Needs review; key RED), compelled disclosure (Needs review; key RED),
      non-solicit (Not acceptable ×2; key YELLOW, the dropped fallback of gap #30).
    - Newly wrong: **purpose and limited use** (Needs review; key GREEN) and **derived data and licence-back** (Needs
      review; key RED). This looks like the data-use clause split across two topics (gap #27).
    - Term is Acceptable (v1.1, "2 years"). Residuals are Not acceptable, with two clean edits: the right and its
      heading.
  - **Same playbook content as R3 and a different rating on 3 topics**, so this is run-to-run variation (gap #29).
    Word's single-run score is about 19–20/24 with this playbook.
- **G1, Identify regulatory gaps against our policy (2026-11-07). 4/4 on the key.**
  - **Route.** `/Identify regulatory gaps` → delivery: In chat. It then offered **only outside law** (Federal DTSA,
    California law, both), picked from the NDA's California governing law. **No internal-policy option**, so the
    owner typed it under "Something else". Word asked for the file, and the owner attached it from
    `Legal Knowledge/Grounding/Normative/NDA/`.
  - **Output.** A cited conformance report: 16 requirements traced, coverage reconciled, states Addressed / Partial /
    Gap / Conflict / Cannot determine. The report is saved as
    `POC-Act2/screens/Identify Regulatory Gaps - … v1.0.md`.
  - **Key items:**
    - NDA-3.3: non-solicit needs GC approval (R04);
    - NDA-3.4: no compelled-disclosure notice (R05 Gap);
    - NDA-3.5: residuals conflict (R06);
    - NDA-4.3: GC approval for residuals before signature (R15).
  - It also noted the blank Effective Date and the VP signatory (R14 Partial).
  - **It asked for our playbook:** "Deviation" (NDA-2.1) is defined by the current Acme NDA playbook, which wasn't
    attached, so R12/R13 were "Cannot determine". Attaching the playbook as well should resolve them.
  - **This closes gap #18 for Word**: policy is not an input to Review contract, but this second skill does the
    policy check well. The Word path becomes two steps: Review contract (playbook) and Identify regulatory gaps
    (policy, plus the playbook as the deviation benchmark).
  - **Setup note:** users have to know to type the internal policy under "Something else". This belongs on the quick
    card.
- **G2, Check execution readiness (2026-11-07). Pass.** It asked no questions and answered in chat:
  - "Not ready for execution";
  - **blank Effective Date**, noting that it also starts the two-year disclosure period. This is the planted
    housekeeping YELLOW (`make_kit.py` variant Y = blank date);
  - both signature blocks incomplete, with Priya Raman and Jordan Ellis named;
  - no defined-term or cross-reference issues;
  - "Information needed: what Effective Date?".

  It checks completeness only, not signature *authority*. That comes from G1 (R14: VP named; the policy requires
  VP or above for a Deviation). Together the three skills cover the review, the policy and execution.

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

## Act 4: the two surfaces together (2026-11-06, Claude via Chrome; Word steps 1–2 are the owner's)

Steps 1 and 2 (Word side) are with the owner. For step 2, Claude uploaded `kit-held/Act 4 - counterparty response/Brightwater Mutual NDA - Brightwater response (tracked).docx` to the matter folder (SharePoint REST, 49 KB).

| Step | Routed to | Result | Credits |
|---|---|---|---|
| 2 (Cowork) "Summarize their redlines" | Legal for Cowork `summarize-redlines` | **All 4 tracked changes found**, matching the key: residuals added, aggregated-data use, term 3 → 2 years, Delaware → California. For each: impact, beneficiary and clause locator. **Not rated against the playbook** (no RED/YELLOW): Microsoft's skill reports what changed, not whether it is acceptable (gap #19). It opened our Act 3 Clean Revised as the base and **flagged that the untracked text doesn't match**. That is correct: the kit built the response from its own idealized draft (gap #20) | 32 |
| 3 `draft-legal-memo` | Legal for Cowork `draft-legal-memo` (Word + SharePoint connectors) | Memo to Dana Whitfield (taken from the business request), in the **template's six sections**, "Draft for counsel review", saved to the matter folder after one approval. Valid `.docx`: 124 paragraphs, 18 footnotes citing sources, 2 tables. **Policy applied:** NDA-3.5 (no residuals), NDA-3.3 (General Counsel approval in writing for the non-solicit), NDA-4.2 for the revised draft (Legal approval, then a VP signs), NDA-5.1 (store within 5 business days). It raised NDA-4.3 (General Counsel approval) only conditionally, if Brightwater's residuals clause stays. That is right for the revised draft; the key's NDA-4.3 is for the original draft. It also found the response file, noted its date (9 November, three days ahead) and flagged it for review rather than treating it as Brightwater's answer | not taken |
| 4 `explain-legal-concepts` | Legal for Cowork `explain-legal-concepts` | A plain-English explanation of residuals quoting Brightwater's 8.1, and a 248-word email to sales, **in chat only** as asked. Its sources line says it **didn't read the policy** and quoted it from the memo instead (gap #21) | not taken |
| 5 PDF path: `nda-review` on `b16 - Oakhollow … .pdf` | **Our `nda-review`** | Made a Word copy from the PDF text ("same wording, plainer formatting"), planned 8 changes (5 edits, 3 comments), and asked: "please ask Oakhollow for their Word version before anything goes back to them". Built all 5 outputs and `check_outputs` passed. Internal: 8 revisions, 8 comments, 8 linked summary rows; external: 8 revisions, no comments; clean: none. **It saved only to the Cowork task folder**, not beside the PDF in the Inbox. Score: **6 of 7** key topics, plus 1 extra YELLOW (housekeeping: confirm the signatory and effective date). The miss is again `nda.representatives` RED vs YELLOW (gap #16, same wording, same misreading). Policy NDA-3.1 (5-year term) is not raised, as expected (gap #18) | 166 |

**What Act 4 shows:**
- Microsoft's skills **consume our grounding well**: the format template and the policy were applied, sources were footnoted, and the work was saved in the matter folder.
- The policy reaches the attorney through `draft-legal-memo`, not through `nda-review`. That is one answer to gap #18: compose the skills rather than add policy reading to `nda-review`.
- No skill rates a counterparty's markup against the playbook (gap #19). That is the next obvious Spaarke skill: "respond to markup".
- The PDF path works, with a clear warning. The redline is built on a reconstruction, so formatting differs from the original.

## Act 5: change a position (2026-11-06, Claude via Chrome; step 3 in Word is the owner's)

### Step 1: replace the playbook in the library
Done through the SharePoint REST API from a team-site tab:
- created `Playbooks/NDA/Archive/`;
- moved the three v1.0 files there (`MoveFileByPath`; nothing deleted);
- uploaded the three v1.1 files from `kit-held/Act 5 - playbook v1.1/`.

The only change from v1.0 is `nda.term`: Preferred goes from 3 years to 2, and the Fallback from 2 years to 18 months. The title and version line change too.

### Step 2: re-run Cowork on the Brightwater NDA (148 credits; about 5 minutes)
Prompt: "Review the Brightwater Mutual NDA in … Matters/Brightwater Systems/ against our current NDA playbook. Save the outputs to … Test/POC-Act5/. No cover email." We pre-created `POC-Act5` in the synced OneDrive.
- **Drift handled:**
  - The plan card says "against Spaarke NDA – Mutual v1.1, the current playbook in the team-site Playbooks/NDA folder. **v1.0 is now archived, even though your profile still names it.**"
  - The provenance line in the dashboard and the redline reads **Spaarke NDA – Mutual v1.1**.
  - So the skill takes the newest playbook in the folder and says the profile is stale. It does not fail, and it does not quietly use the archive.
- **`nda.term`: GREEN**, as expected. The audit-pack note reads "Two-year disclosure period, two years per disclosure, open-ended trade-secret tail". In Act 3, under v1.0, it was YELLOW with a term edit. The plan now has **13 changes** and no term edit. The internal redline has 12 revisions (8 insertions, 4 deletions), 13 comments and 13 summary links. The external redline has the same revisions and no comments, and the clean copy has no revisions. Evidence: `evidence/act5/`.
- **Against the v1.0 key, 10 of 13 match.** Two of the misses are expected or known:
  - `nda.term` is GREEN under v1.1, which is the intended change.
  - `nda.representatives` is RED again (gap #16).
- **The third miss is new:** `nda.housekeeping` is GREEN, where Act 3 gave YELLOW. Both runs raise the same two comment-only items: the blank Effective Date and the Acme signatory. Only the rating changed (gap #29).
- **The 0.1.3 audit-pack colours work in the tenant:**
  - chart 1 has 4 per-bar colours (D03B3B, FAB219, 0CA30C, 8A8A86) and chart 2 has 3;
  - the second chart is anchored under the first (rows 1 and 16, same column).
  - The labels still say `v0.1.2-poc`, because the version constants in the skill and engines weren't bumped (gap #28).
- **One approval** (upload), because the folder already existed (gap #24).
- **Step 3 (Word picker still on the saved v1.0):** the owner's.

## Act 6: library review across 16 NDAs (2026-11-06, Claude via Chrome)

### 6a: Microsoft `compare-key-terms` (259 credits; about 25 minutes, about 10 of them waiting on an approval)
Prompt: "Build a key-terms grid … use NDA Key Terms Columns v1.0 … Excel with a citation for each answer, save to `Test/POC-Act6/`". Cowork loaded both our `nda-library-review` and `compare-key-terms` and used **`compare-key-terms`** with the Excel connector.
- **Output** `evidence/act6/6a - NDA Key Terms Grid (compare-key-terms).xlsx`:
  - **Key Terms:** 16 rows × 17 columns, following our format grounding. Each cell holds the answer, the section and a short quote.
  - **Citations:** 225 rows, each with the term, value, Found/Not stated, the exact quote, the location and the source URL. The URLs are text, not hyperlinks.
  - **Coverage:** what was read, and how, for each file.
  - **Schema:** the columns and questions.
- **No playbook positions:** it extracts fields; it doesn't say whether a field is acceptable. That is Microsoft's baseline for a tabular review.
- **Approvals:** create the folder `POC-Act6`, then upload. The create-folder card waited about 10 minutes before Claude noticed it (gap #24).

### 6b: Microsoft `review-contract` on a folder (449 credits)
Prompt names the skill: "Use the Legal for Cowork review-contract skill: review all NDAs in … NDA batch/ against our NDA playbook".
- **It doesn't refuse and doesn't do just one: it loops over all 16.** The runbook expected one at a time.
- **First it summarizes the playbook and asks for confirmation.** It took the `.md` as the master over the two `.docx` copies. It turned our General guidelines into run rules ("you MUST NOT replace whole clauses"), which is gap #3 in action. It listed **gaps in our playbook**:
  - nothing on inserting a missing clause;
  - reverse engineering when nothing tangible is disclosed;
  - DTSA notice applicability;
  - disclosure periods over 2 years;
  - home state unknown for the governing-law fallback.
  These are useful for playbook quality (gap #25).
- **Four questions:** confirm the summary; where redlines go (copies / edit originals / report only); insert missing clauses or comment; PDFs report-only or convert. We chose report only, comments, and PDFs findings-only.
- **Output** `evidence/act6/6b - review-contract batch report.docx`: one Word report, with a summary table (overall status, escalations, other findings) and a section per NDA with clause, quote, classification and action.
- **Score: overall status 16 of 16** against the key (all-GREEN = Compliant, any YELLOW = Fallback, any RED = Escalate). Before signing, it also flagged that every NDA names Jordan Ellis as signatory, that 11 effective dates are in the future, and that none has a notices clause.
- **What it lacks against 6c:** no topic-by-document grid, no machine-readable result, no visuals. It is a narrative report.

### 6c: our `nda-library-review` (185 credits; about 13 minutes including approvals)
The prompt asked for "the library view: a grid of NDAs by topic with a dashboard" and named no skill. Cowork routed it to **`nda-library-review`** (our personal skill folder).
- **Plan card first:** 16 NDAs (13 Word, 3 PDF); mutual side, balanced posture; "Spaarke NDA – Mutual v1.0" from the team site (validated, all 24 topics, marked as test positions); the three outputs; and a warning that PDF quotes can't be checked by script. We chose Start.
- **Approvals: 5 cards.** Create `Test` (which already existed), create `POC-Act6`, create the data folder ("this and the rest"), then 2 upload cards. The second upload card came up again for the data files even after "Upload this and the rest" (gap #24).
- **Outputs**, in `evidence/act6/6c/`:
  - **`NDA Library Review.xlsx`**, five sheets:
    - **Grid:** 16 × 24 topics. Each cell shows the rating and the clause, and the document names link to the team site.
    - **Detail:** 384 rows: quote, quote check, note and playbook basis.
    - **By topic:** with a chart.
    - **Documents:** counts and quote checks.
    - **About.**
  - **`NDA Library Review - Dashboard.html`:** tiles, a heatmap and the top issues. There is no `<script>` and no `on*=` handler, and it has a dark-mode block. Its only URLs are the 16 links to the source documents.
  - **`NDA Library Review - data/`:** one `review.json` per NDA.
- **Chat summary:** 25 RED, 21 YELLOW, 338 GREEN; 14 of 16 NDAs have a RED. It gives a table by NDA, the topics most often RED, and the quote checks. The verbatim text is in `evidence/act6/6c-chat.md`.
- **Score (`poc_check.py score`): 375 of 384 topic cells (97.7%).** Five of the nine misses are **errors in the kit**, not the model (gap #26):
  - **4 × housekeeping YELLOW where the key says GREEN** (b07, b09, b10, b15). The kit writes "a Ohio / a Arizona / a Oregon / a Illinois corporation". The model rightly flagged these as typos, and 6a and 6b noticed them too.
  - **b14 housekeeping RED expected, GREEN given.** `make_kit.py` never planted the wrong signatory (`col == 1` is always Acme in the batch), so the PDF has the right party and GREEN is correct.
  - **Corrected for the kit: 380 of 384 (99.0%).**
- **The four real misses:**
  - **b16 `nda.representatives`: RED for YELLOW.** The same wording problem as Act 3 and Act 4 (gap #16).
  - **b15 `nda.whistleblower-dtsa`: RED for YELLOW**, on a missing clause. The key treats a missing notice as a fallback; the model treated it as the Prohibited bar on reporting, while saying "[verify applicability per party type]".
  - **b10 `nda.trade-secret-preservation`: RED for GREEN.** The term clause ends *all* obligations after 1 year, and the model counted that again under trade secrets. It is a fair reading, but it counts one clause twice.
  - **b11 `nda.data-use-licence-back`: YELLOW for GREEN.** The feedback carve-out (3.2) was counted again under data use.
- **Citations: all 356 quotes are found word for word** in the source documents. That covers 289 from Word, which the skill also checked by script, and 67 from PDF, which we checked here with `pypdf`. A sample of 10 non-GREEN citations all point at the right clause.
- **Small defect:** the standards text reads "Spaarke NDA – Mutual v1.0 **v1.0**". The playbook title already carries the version, and the skill appends it again. It also reports skill `v0.1.2-poc`: the personal skill folder was not rebuilt for 0.1.3, and nothing changed in it.

### 6c follow-up: "Redline the three with the most REDs" (96 credits for three; same task)
Prompt, in the same task: "Yes. Redline the three with the most REDs: b16 Oakhollow, b10 Verdant Loop and b03 Larkspur. Save each set of outputs next to its NDA … No cover emails." 14 NDAs had a RED, not the 3 the runbook assumed, so we named the top three.
- **Sequencing:** one task, no new task per NDA. Cowork loaded the `Spaarke NDA (MVP)` plugin (`nda-review` **v0.1.3**) next to `nda-library-review`, planned all three, showed **one plan card for all three**, then built them. The plan and choices are in `evidence/act6/6c-redline-plan.md`.
  - The choices were "build all three", "b10 and b03 only (skip the PDF)" or "change". We built all three.
  - Choosing an option needs **Submit** as well, which cost Claude a minute.
- **The plan handled the double counts from gap #27.** b10 trade secrets became "comment only: fixed by the new 8.1 wording". The b16 Representatives edit adds responsibility and keeps the written-duty standard as an accepted fallback. That is the right fix, though it is still labelled "Escalate".
- **The PDF path was explained on the plan card:** "rebuilt a Word version from the PDF text … the first comment says so … compare against the PDF or ask for their Word file."
- **Outputs:** 15 files in `Inbox/NDA batch/` on the team site, 5 per NDA. "All three passed every output check." One upload approval. No emails.
- **Our check, read in the browser from SharePoint:**

  | NDA | Internal redline: ins / del / comments / summary links | External redline | Clean copy |
  |---|---|---|---|
  | b16 Oakhollow (PDF, rebuilt) | 3 / 2 / 8 / 8 | 3 / 2, no comments | no revisions |
  | b10 Verdant Loop | 2 / 2 / 4 / 4 | 2 / 2, no comments | no revisions |
  | b03 Larkspur | 3 / 2 / 3 / 3 | 3 / 2, no comments | no revisions |

  Comments match the planned changes (8, 4 and 3), and every change has a summary link. The rebuilt b16 has **no signature-block table**: the PDF text rebuild flattens it (part of gap #22).
- **Open questions it raised:**
  - Larkspur is a health-data company, so check whether a data-protection agreement is needed.
  - Verdant Loop: confirm the legal name and state, and the effective date.
  - Oakhollow: ask for the Word file; confirm the 5-year term and the non-solicit.
- **Cost:** the task total went from 185 to 281 credits, so **96 for three redlines, about 32 each**, because the library review already held the ratings. The single Brightwater review in Act 3 cost 122. The month total is 4,147. The banner now says "unlimited credits": the spending policy changed after Act 0 (gap #5).

### 6a, 6b and 6c compared
| | 6a `compare-key-terms` | 6b `review-contract` | 6c `nda-library-review` |
|---|---|---|---|
| What it gives | Extracted terms, no positions | Rated narrative report | Rated grid, dashboard and data files |
| Accuracy | n/a (not rated) | Overall status 16/16 | Topic level 375/384 (99.0% corrected); overall status 16/16 |
| Citations | 225, URLs as text | Quoted in the report | 356, all found word for word; script-checked for Word |
| Machine-readable | Excel cells | No | `review.json` per NDA |
| Visuals | No | No | Heatmap, tiles, chart |
| Questions before running | None | Playbook confirmation plus 4 questions | Plan card, then Start |
| Credits | 259 | 449 | **185** |

6c is the cheapest of the three and the only one rated at topic level. It also leads straight into redlines: three more for 96 credits in the same task. 6b's playbook audit (gap #25) is the one thing it does that 6c doesn't.

## Act 7: the document-type library (2026-11-07)

**Owner direction (2026-11-07).** Organise Legal Knowledge by document type and build NDA plus one non-contract type.
Write a quick card, test whether one habit works for both, and fold the results into the design.

**Terminology (owner).**
- "Playbook" is the primitive for the standard procedure of any document type, whether Microsoft Legal or we
  created it. People may also call it a checklist or a procedures table.
- It is distinct from skills and tools (consistent with ontology D-209: one concept, several formats).

**Setup, done by Claude.**
- **Library reorganised** (runbook note: old → new paths). Moves only; the empty old folders are in the site recycle
  bin. `Inbox/` and `Matters/` moved to `Shared Documents/POC Workspace/`. The Cowork profile now points at
  `Legal Knowledge/NDA/Playbook master/`.
- **NDA v1.2** (X2b) is at the top of `NDA/`, with the master in `NDA/Playbook master/`, and v1.1 moved to `Archive/`.
  Positions are unchanged from v1.1, so the v1.1 key still applies. The Word copy adds "Acceptable fallback (accept
  without redlining)" lines, and "If missing" plus model language for reverse engineering and compelled disclosure.
- **Non-contract type: the legal hold notice.**
  - Why: it is a document in-house Legal reviews before sending, with objective, checkable elements.
  - Basis: FRCP 37(e) and The Sedona Conference, Commentary on Legal Holds, 2nd ed. (2019).
  - Playbook: `Legal Hold Notice/Spaarke Legal Hold Notice – Employee Custodians v1.0.docx`, 13 topics in the same
    four-column shape. A numbered-requirement variant is in `POC Workspace/Test variants/` (W1b).
  - Synthetic drafts in `POC Workspace/Matters/Harbor Point Logistics/`:
    - **A**: 6 RED, 2 YELLOW, 5 GREEN; automatic deletion planted by absence;
    - **B**: clean except a mailbox-only contact (YELLOW).
  - Key: `answer-key/legal-hold-answer-key.md`.
- **Quick card** `Legal Knowledge/Start here – Quick card.docx`:
  - one three-step habit and a table of document types;
  - optional follow-ups (Identify regulatory gaps via "Something else"; Check execution readiness for contracts);
  - "attach rather than save".
- **Owner tests:** `doctype-test-script.md` (W0 card, W1a/W1b non-contract in two formats, W2 wrong playbook, W3 no
  playbook, W1c clean draft, W4 NDA v1.2 retest).

Results: pending.

**Act 7 update (2026-11-08, owner review of the setup).**
- **Folder standard** written into design §5.9, with the same subfolders in every type.
  - Legal Hold Notice now has the full skeleton.
  - NDA `Samples/` moved to `POC Workspace/Samples/NDA`.
- **Realistic Legal Hold material:**
  - Acme Records Retention and Legal Hold Policy v1.0 (Policies);
  - Acme Legal Hold Notice Template v1.0 (Templates);
  - Kestrel Freight notice, 2025 (Precedents);
  - Harbor Point demand letter of 2026-10-20 with a preservation demand (matter folder).
  - The playbook now has a master (`Playbook master/`) and is rendered from it.
- **Quick card v1.1** = two step lists (contract / other document) plus the one-time custom instruction.
  `user-steps.md` holds the same text.
- **Owner's Copilot custom instruction found stale:** it named "Spaarke NDA – Mutual v1.0" at
  `Legal Knowledge/Playbooks/NDA/`. A generic replacement is in the quick card, and W3 tests whether Word follows it.
- **Script:** W0 now tests the step lists; W5 is an optional policy check, with its key in `answer-key/`.

### Act 7 results (owner, Word desktop, 2026-11-09)

Screenshots: OneDrive `Documents/Cowork/Test/POC-Act2/screens/` (W1a-*, W1b-explicit-instructions, W3-*, W5-*),
plus the owner's in-chat screenshots of the protocol run. Scored against `answer-key/legal-hold-answer-key.md`
(draft A: 6 RED, 2 YELLOW, 5 GREEN).

| Test | Setup | Route | Result |
|---|---|---|---|
| W1a | Draft A + table playbook attached, "Review this against the attached playbook", mode **Allow editing** | Word's **editing-suggestion cards** (7 suggestions in 3 categories Copilot invented); no playbook card, no ratings | All 6 REDs found, including automatic deletion (planted by absence). Acknowledgment found as "Clarify". **Missed** other custodians (YELLOW). No false positives. 7/8 issues |
| W1a (follow-up) | Owner asked why it was not a review | Copilot apologised and recommended an Agent Builder agent; it also cited stale Cowork skill files from OneDrive (`standards-protocol`, `SKILL`, `review-checklist`, three `mutual-nda-review-playbook` copies) | See gaps #33, #34 |
| W1b (as run) | Same table playbook; explicit prompt naming the four columns and asking for "topic by topic output"; mode **Chat only** | Chat answer: **topic-by-topic table** (Topic / Assessment / Analysis / Recommended action) | First rows match the key. The requirement-list variant was not run |
| Protocol run | Owner's protocol file `OneDrive/Documents/Copilot/playbook-document-review.md` v1.0 (verification code PBR-1109-A), referenced from the custom instruction; prompt "analyze using this playbook" | Chat: header block, 13-row table, overall conclusion with reconciled totals | Custom instruction followed (tracking phrase and verification code echoed). Totals **Meets 5 / Acceptable fallback 2 / Escalate 6** = key 5 GREEN / 2 YELLOW / 6 RED; the six escalations are exactly the six REDs. Copilot found the protocol by search, not at the instructed path (`Copilot/Skills/`; the file is in `Copilot/`) |
| W3 | Draft A, nothing attached, "review this" (earlier custom instruction) | Editing-suggestion cards after a **web search** (FRCP 37(e), exterro); did **not** ask for a playbook | Improvised: 7 suggestions; the clean-up conflict was softened to "contact Legal if unsure" rather than removed |
| W3 (Claude add-in, benchmark only) | Same draft, Claude for Word pane, "review this" | Chat, severity-grouped | Without any playbook: 7 of 8 key issues (clean-up, date range, expiry, merits, data sources, acknowledgment, auto-delete as "Medium"); missed departures |
| W5 | Draft A + Acme policy attached, "review based on this policy", Allow editing | Editing-suggestion cards: 8 suggestions in 3 categories | First card = RLH-3.5/3.9 clean-up conflict (correct). Remaining 7 not captured |
| W0, W1b (requirements), W1c, W2, W4 | — | — | Not run |

**Findings.**
1. **The route depends on the pane's mode and the prompt, not on the playbook's shape.** In *Allow editing*, a
   non-contract playbook review becomes Word's editing suggestions (good content, no per-topic ratings, one topic
   missed). In *Chat only*, or with a stated review method, Word returns a complete topic-by-topic assessment.
2. **A review protocol fixes it.** A short method file (extract every topic, rate each, reconcile totals, don't
   edit) produced a 13/13-count match with the key. Copilot followed the custom instruction that points at it.
3. **Enterprise search pulls stray files into reviews.** Old Cowork skill copies in OneDrive were cited as sources
   (gap #34). The governed library is not the only grounding Copilot uses.
4. **Without a playbook Word improvises** (web search; no ask), so the "ask me to attach" line did not hold in W3
   (earlier instruction text; retest with the protocol instruction).

**Owner follow-up (2026-11-09): two Copilot modes in Word desktop.**
- **Chat mode:** cannot @-mention agents and does **not** read Settings › Personalization (custom instructions).
- **Agent mode:** accepts `@agent-name` (adds a declarative agent inline to the same chat; seen with "Spaarke AI") and
  reads the custom instructions. The protocol run above was in this mode; W3's improvising is consistent with chat mode.
- Owner's proposed direction: users always pick a **specialist declarative agent** (for example `@NDA Review`, a legal
  hold agent, later patent application, real estate lease, litigation pleading) for a consistent procedure, and we
  build a library of them. To be spiked before design changes (see the plan in the session, 2026-11-09).

**Gaps.** #33: the Word pane's *Allow editing* mode turns a non-contract playbook review into editing suggestions
(no ratings, incomplete coverage). #34: stale skill and playbook copies in the user's OneDrive are retrieved as
sources during reviews (`Cowork/MVP-Evidence/T-02/skills-folder-copy/`, `Cowork/Skills/nda-library-review/`,
`Microsoft Copilot Chat Files/playbook-format.md`, `Copilot/Created/_before-Act2/`). #35: the W1a findings table may
have been inserted into draft A (W1a.4 shows it in the document body); check draft A before further tests.
