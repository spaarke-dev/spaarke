# End-to-end POC: NDA – Mutual in Word and Cowork (runbook)

**Goal.** Walk one NDA matter end to end in Word and in Cowork, using every kind of component the package will have:
- the playbook;
- precedents;
- grounding (normative, format, factual);
- Microsoft's AI for Legal skills;
- our Cowork skill with redlining and visuals;
- a multi-NDA library review.

Find the gaps before we formalize anything. Every gap goes in `gap-log.md`.

**POC rules: light on purpose.**
- Hand-made files.
- The current single plugin, shared "Only you", plus personal skill folders for anything new.
- No K/E plugin split, no component manifest or bill of materials, no gates G1–G5, no attorney sign-off, no admin deploy.
- Synthetic or CC BY documents only. Real NDAs (Corteva, the 2017 RPX NDA) stay out of the repo.

**Already proven, so re-run only as part of the story:**
- Word: playbook import, the saved-playbook picker (T-P4), the playbook review (S5).
- Cowork: `nda-review` with engines and redlines (T-05), the OneDrive personal playbook (T-07), the profile (S3), plugin upload.
- Word: Identify regulatory gaps question cards (seen, not run to the end).

---

## 1. SharePoint or OneDrive?

Both are SharePoint underneath:
- **OneDrive** is each person's own SharePoint site (`spaarke-my.sharepoint.com/personal/<user>`).
- A **team site** such as Acme Company (`spaarke.sharepoint.com/sites/AcmeCompany`) is shared, and its owners control permissions.

Copilot treats both the same way for finding files: Attach from Cloud, the `/` file picker and search all respect the user's permissions. The difference is ownership and sharing:

| | OneDrive | SharePoint team site |
|---|---|---|
| Owned by | One user | The site owners (practice lead, KM) |
| Fits | **Personal**: the attorney's own playbooks, profile, Cowork outputs | **Governed**: one copy of the firm's playbooks, precedents and grounding for everyone, read-only for attorneys |
| Cowork | Proven: reads and writes `Documents/Cowork/...` (T-07, S3) | **Unproven: T-P10.** Can Cowork read a team-site folder, and can a skill be pointed at it? |
| Word | Attach from Cloud / `/` | Attach from Cloud / `/` (to confirm in Act 2) |

**POC decision:**
- The governed kit goes in **Acme Company › Shared Documents › `Legal Knowledge/`**, a folder rather than a new library, to keep the POC light.
- Personal items and outputs stay in OneDrive `Documents/Cowork/`.
- The read-only test needs a second, non-owner user. If none is available, log it as a gap and test read access only.

## 2. The kit (prepared by Claude; uploaded by drag-and-drop)

```
Acme Company / Shared Documents / Legal Knowledge/
├── Playbooks/NDA/
│   ├── Spaarke NDA – Mutual v1.0.md               master (what Cowork reads)
│   ├── Spaarke NDA – Mutual v1.0.docx             our heading format (Word import)
│   └── Spaarke NDA – Mutual v1.0 (table).docx     Microsoft four-column format (T-P15)
├── Precedents/NDA/
│   ├── Acme Mutual NDA (model form).docx          our paper, built from the playbook's model language
│   ├── nda-01 … nda-06 (.docx and .pdf)           the eval cases, rendered
│   └── nda-02 counterparty markup.docx            the negotiated example (tracked changes)
├── Grounding/
│   ├── Normative/NDA/Acme NDA Approval and Signature Authority Policy v1.0.docx
│   ├── Format/Acme Legal Memo Template v1.0.docx
│   ├── Format/NDA Key Terms Columns v1.0.docx     column list for compare-key-terms and the library review
│   └── Samples/NDA/Business request email (synthetic).eml/.docx
└── Inbox/NDA batch/                               ~16 NDAs for the library review (Act 6)
    ├── nda-01 … nda-06 (.docx)                    eval cases with known expected results
    └── cnli-01 … cnli-10 (.docx, 3 as .pdf)       ContractNLI NDAs (CC BY 4.0, attributed in a README)
```

The policy follows §7.2a of `playbook-library-and-grounding.md`:
- a header with title, version and jurisdiction;
- a scope clause;
- numbered, atomic requirements with IDs.

It deliberately contains rules that some batch NDAs break (term > 3 years, a non-solicit, no carve-out for compelled disclosure, a signature threshold), so Act 2 and Act 6 have something to find.

## 3. The walk-through

### Act 0: set up (owner, ~30 min)
1. Upload the kit folder to Acme Company.
2. Personal setup (manual):
   - `Documents/Cowork/Profile/legal-profile.md` points to the library path, side = mutual, posture = balanced.
   - Cowork **Preferences** gets one line: `/`-reference the playbook.
3. Install the POC plugin (0.1.2-poc: `nda-review` plus the dashboard step) with Publish "Only you". Add the POC skill `nda-library-review` as a personal skill folder.
4. Turn **Legal for Cowork ON** (it was OFF since the 11-02 clean-up).

### Act 1: build a playbook from precedents (Cowork → Word). Proves setup path B, T-P15.
1. Cowork: "Build a playbook from these precedents" over the three most consistent precedents. Microsoft `create-playbook` should run and return a four-column `.docx`.
2. Compare it with our baseline: topics found, missing, wrongly merged. Log the differences.
3. Convert it with `playbook_import.py`, then render it again (Claude, locally).
4. Word desktop: New playbook → import **each** format (heading and table). Save both. Log:
   - which `.docx` element becomes the saved name;
   - whether the Compromise Positions / fallback survive;
   - what General guidelines contain.

### Act 2: the attorney in Word. Proves the Word deliverable.
Open `nda-01` (from Precedents, via Word's open dialog on the team site).
1. `/` **Review contract** → pick `Spaarke NDA – Mutual v1.0` → **Run review**. Keep the result.
2. `/` **Identify regulatory gaps** → **Internal compliance policy** → attach the policy from `Legal Knowledge/Grounding/Normative`. Log whether Attach from Cloud finds the team-site file (T-P10, Word side). Deliver in chat.
3. `/` **Check execution readiness**.
4. Save as `nda-01 – Word review.docx` (OneDrive).

### Act 3: the attorney in Cowork, with redlining and visuals. Proves the Cowork deliverable, T-P2, T-P10, T-P13.
1. **T-P13 visual probe first** (solution-packs note §5.6, items 1–3, 7 and 8). Run one throwaway skill and record what renders: PNG/SVG, HTML with and without script, `.xlsx`/`.pptx` charts, Markdown/Mermaid.
2. New task: "Review this NDA" with `nda-01` attached, no skill named. Log **which skill ran** (T-P2: ours or Microsoft `review-contract`).
3. `nda-review` should:
   - read the playbook from the **team-site** path in the profile (T-P10, Cowork side);
   - fall back and say so if it can't.
4. Expected outputs, all checked by `check_outputs`:

   | Output | Visual level |
   |---|---|
   | Provenance line naming the playbook and its version | chat |
   | Plan preview before files: changes by priority, escalations, approve / change | chat table (V3 card if T-P13 says cards render) |
   | `Redline with Comments (internal).docx`, with the page-1 linked summary table | V1 |
   | `Redline.docx` (external), `Clean Revised.docx` | V1 |
   | `Review Dashboard.html`: static HTML + SVG; risk by topic, changes by priority, escalations, links to each change | V2 |
   | `Audit Pack.xlsx`: change log, risk summary with a native chart, topic coverage | V1, if `openpyxl` is present (T-P13 item 1) |

5. Open every output from Cowork's preview **and** from OneDrive in Word desktop. Log fidelity, for example whether the dashboard renders in the preview or only after download.

### Act 4: the two surfaces together. Proves alignment, Microsoft skills that consume grounding, the PDF path.
1. Open Cowork's external redline in Word. Compare it topic by topic with Act 2's Word review: same positions → same edits? Use the topic IDs in comments.
2. Counterparty answers with `nda-02 counterparty markup.docx`. Run `/` **Summarize redlines** in Word **and** `summarize-redlines` in Cowork, then compare.
3. Cowork: `draft-legal-memo` to the business owner, with the **memo template** as format grounding and the policy as normative grounding.
4. Cowork: `explain-legal-concepts` → an email draft to sales (not sent).
5. The PDF path: run `nda-review` on `nda-04.pdf`. Log what happens; a redline needs `.docx`.

### Act 5: change a position. Proves drift handling (L24).
1. The owner changes one position in the master, e.g., the term from 3 years to 2. Claude renders v1.1, and the owner replaces the files in the library. v1.0 moves to `Playbooks/NDA/Archive/`, so it is not deleted.
2. Cowork: re-run on `nda-01`. The provenance line should say v1.1.
3. Word: start a review. The picker still shows the saved **v1.0**. Log:
   - does the attorney notice it?
   - how many clicks does re-importing take?
   - can the old saved copy be deleted or replaced?

### Act 6: library review across many NDAs (Cowork only). Proves the Harvey Vault / Legora Tabular Review pattern.
**What we emulate.** Harvey Vault "Review" and Legora "Tabular Review" show a grid: rows are documents, columns are questions, and each cell holds an answer plus a citation linked to the source. Word works on one open document, so this is **Cowork only**. That is a finding in itself.

Three runs over `Inbox/NDA batch/` (~16 NDAs):

| Run | How | What it tells us |
|---|---|---|
| **6a. Microsoft `compare-key-terms`** | "Build a key-terms grid for these NDAs" + `NDA Key Terms Columns v1.0` (format grounding) → Excel | Microsoft's baseline for a tabular review: accuracy, citations, limits |
| **6b. Microsoft `review-contract` on a folder** | "Review all NDAs in this folder against our playbook" | Does it loop over documents, refuse, or do one? (We expect one at a time) |
| **6c. POC skill `nda-library-review`** | One entry skill. The **model** classifies each NDA against the playbook one document at a time; **scripts** segment the clauses and write a per-document JSON. A script then assembles the outputs. | Our differentiator: playbook positions rather than raw fields, a deterministic grid, and visuals |

Outputs of 6c:
- `NDA Library Review.xlsx`:
  - one row per NDA;
  - one column per playbook topic;
  - each cell holds GREEN / YELLOW / RED, a short answer, the clause reference and a quote;
  - a summary sheet with a chart.
- `NDA Library Review – Dashboard.html`:
  - a heatmap of NDA × topic;
  - counts by status;
  - the top issues;
  - each row links to the file on the team site.

Then a follow-up in the same task: "Redline the three RED ones". `nda-review` should run on each. Log whether Copilot sequences them or needs one task each (the race rules).

**Measure:**
- accuracy on nda-01…06 against their expected results;
- the citation hit rate (spot-check 10 cells);
- the number of NDAs before context or time limits bite (try 6, then 16);
- elapsed time, and credits used (`/cost`).

## 4. Prep list (Claude, before Act 0)

| # | Item | Notes |
|---|---|---|
| 1 | Render `tests/eval/cases/nda-01…06.md` to `.docx`, plus `.pdf` for 04–06 | The existing corpus has three `.docx`; reuse them |
| 2 | Pick 10 ContractNLI NDAs and convert them to `.docx` (3 also as `.pdf`) | CC BY 4.0; an attribution README in the folder |
| 3 | Model form, policy, memo template, key-terms column list, synthetic business email | Hand-written, short; the policy follows §7.2a |
| 4 | Playbook v1.0 in the three formats; v1.1 ready for Act 5 | Our renderer + a four-column variant |
| 5 | POC plugin 0.1.2-poc: `nda-review` + a quick `render_dashboard` (stdlib) + an optional `audit_pack` | POC quality: no gates beyond the existing build |
| 6 | Personal skill `nda-library-review` (SKILL.md + scripts that reuse `segment_clauses` and the dashboard renderer) | Personal skill folder route, outside the plugin |
| 7 | T-P13 probe skill | Throwaway |
| 8 | `gap-log.md` | Ready |

## 5. Who does what, and sessions

- **Session 1:** prep items 1–8 (Claude); Act 0 (owner); Act 1.
- **Session 2:** Act 2 (owner in Word desktop; Claude logs); Act 3 (Claude drives Cowork in Chrome; owner checks the Word desktop side).
- **Session 3:** Acts 4, 5 and 6, then the gap-log summary, and the gap log folded into design v0.6.

Owner-only steps: Word desktop clicking, SharePoint uploads and permissions, and anything that needs a password.
