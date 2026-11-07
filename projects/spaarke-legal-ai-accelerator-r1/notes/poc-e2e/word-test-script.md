# Word test script (runbook Act 2): owner, Word desktop for Windows

About 45 minutes. One pause for Claude before R6, about 2 minutes. Each test answers one question; the design's
Word steps and quick card are written from the answers (design §2.4.6, §8.1a; backlog X3, X6).

**Screenshots.** Save every screenshot to OneDrive `Documents/Cowork/Test/POC-Act2/screens/` and name it after the
step (`R1-card.png`, `R3-findings-1.png`, …). Claude reads that folder through OneDrive sync and does the scoring,
so you don't need to write anything down beyond the one-line answers below.

**Rules for the whole session.**
- Start each test in a **new Copilot chat** (Copilot pane › New chat), unless the step says otherwise.
- Save only where a step says **Save**. Two of the test files share the same Title, so an extra save would leave two
  identical names in the picker.
- Don't delete any saved playbook until R6 asks you to.

---

## Before you start (2 minutes)

| # | Do | Capture |
|---|---|---|
| P1 | Open Word desktop. Open the team site **Acme Company › Documents › Matters › Brightwater Systems › Brightwater Mutual NDA.docx** | — |
| P2 | **File › Save a Copy** → OneDrive `Documents/Cowork/Test/POC-Act2/` → name it `Brightwater Mutual NDA – Word review.docx`. Close the original and work only in the copy, so the kit stays clean | — |
| P3 | Copilot pane → type `/` → **Review contract**. When it asks "Which playbook do you want to use?", **stop**. Don't pick anything | `P3-picker.png`: the full list of saved playbooks (use View all if it is cut off). This is the "before" record |

## R1: is our new Word copy imported as a playbook? (the key test)

| # | Do | Capture |
|---|---|---|
| R1.1 | In the same picker: **New playbook** → **Attach from Cloud** | `R1-nav.png`: how you got to the team site. Was it listed under Quick access, Sites or Recent, or did you have to search? (This is R2) |
| R1.2 | Go to **Acme Company › Documents › Legal Knowledge › Playbooks › NDA** → `Spaarke NDA – Mutual v1.1 (table).docx` → attach | — |
| R1.3 | Wait for Word to process it. **Don't click Save or Run yet** | `R1-card.png` |
| R1.4 | Answer: **A** = a playbook card named "Spaarke NDA – Mutual v1.1" with a list of topics; or **B** = it ran *create-playbook* synthesis (topics marked "Counsel decision", a new file written). How many topics? | One line: A or B, and the count |
| R1.5 | If **A**: open the **Term** topic and the **Definition and marking** topic | `R1-term.png`, `R1-definition.png`. Are the Instructions, the "Escalate if" line, the Example Text and the **Compromise Positions** all there? |

**If R1 is B, stop here and tell Claude.** We'll then run R1c and the A/B files to find the trigger before going on.

## R3 + R4: save it, then run the review

| # | Do | Capture |
|---|---|---|
| R4.1 | Click **Save playbook** (not "Save and run review") | `R4-saved.png`: the name it was saved under |
| R4.2 | Tell Claude "R4 saved". Claude checks OneDrive for any new file Word created, so you don't have to look | — |
| R3.1 | Now run the review: `/` **Review contract** → pick the saved **Spaarke NDA – Mutual v1.1** → **Run review** | — |
| R3.2 | When it finishes, screenshot the whole findings list (scroll; several screenshots are fine) | `R3-findings-1.png`, `-2`, … |
| R3.3 | Note whether it **added tracked changes and comments** to the document. Then **Save** the document (Ctrl+S); it is the copy from P2 | One line: tracked changes yes or no, comments yes or no |

Expected (Claude scores this from the screenshots and the saved document): **6 RED** (independent development,
one-sided carve-outs, reverse engineering, data use and licence back, compelled disclosure, residuals); **6 YELLOW**
(definition, Representatives, return and destruction, non-solicit, governing law, housekeeping); **Term GREEN**,
because v1.1 changed it. Everything else GREEN.

## R1b: can a topic id travel in the Instructions? (inspect only; do not save)

| # | Do | Capture |
|---|---|---|
| R1b.1 | New chat → `/` **Review contract** → **New playbook** → Attach from Cloud → OneDrive `Documents/Cowork/Test/POC-Act2/Spaarke NDA – Mutual v1.1 (table, ids in Instructions).docx` | — |
| R1b.2 | A or B, as in R1.4. If A, open **Term**: is the last line "Topic ID: nda.term" still there? | `R1b-term.png`. **Close without saving** |

## R5: is the saved playbook offered in a new chat?

| # | Do | Capture |
|---|---|---|
| R5.1 | Close Word completely. Reopen `Brightwater Mutual NDA – Word review.docx` | — |
| R5.2 | `/` **Review contract** | `R5-picker.png`: is "Spaarke NDA – Mutual v1.1" listed? |
| R5.3 | Pick it. Does it go straight to Run review **without asking you to attach anything**? Then cancel; no need to run it again | One line |

## ⏸ Pause: tell Claude "ready for R6"

Claude moves the library's `… v1.1 (table).docx` to `Archive/` and uploads an altered copy of the **same name**, with
the Term instructions starting `SNAPSHOT-TEST:`. Claude tells you when that's done, then restores the original after R6.

## R6: is a saved playbook a copy or a link? Can a stale one be replaced?

| # | Do | Capture |
|---|---|---|
| R6.1 | New chat → `/` **Review contract** → open (view or edit; don't run) the saved **Spaarke NDA – Mutual v1.1** → **Term** topic | `R6-term.png`. If the text starts **"SNAPSHOT-TEST:"**, it is a **link** to the library file; if not, it is a **copy** |
| R6.2 | In the picker, find the old saved **Spaarke NDA – Mutual v1.0** (from 11-05). Does anything other than the name tell you it is out of date? | One line |
| R6.3 | Try to remove or replace the v1.0 card (look for "…", Edit, Delete, View all › manage) | `R6-manage.png`. Is there a delete or replace option, and how many clicks did it take? Delete it if you can |

## R7: what happens without the skill?

| # | Do | Capture |
|---|---|---|
| R7.1 | New chat. **Don't** type `/`. Attach the library's `Spaarke NDA – Mutual v1.1 (table).docx` with the paperclip (Attach from Cloud), and type: *Review this NDA against the attached playbook.* | `R7-result.png` |
| R7.2 | Answer: did it start **Review contract** (playbook card, topic findings), or give a plain chat answer? Did it add tracked changes? | One line. **Undo any tracked changes** it made (Review › Reject All) |

## Optional (if time allows)

| # | Do | Capture |
|---|---|---|
| G1 | New chat → `/` **Identify regulatory gaps** → **Internal compliance policy** → attach `Legal Knowledge/Grounding/Normative/NDA/Acme NDA Approval and Signature Authority Policy v1.0.docx` | `G1.png`: does it cite NDA-3.3, NDA-3.4, NDA-3.5 and the General Counsel approval (NDA-4.3)? |
| G2 | New chat → `/` **Check execution readiness** | `G2.png`: does it catch the blank Effective Date and the Acme signatory? |

## Only if R1 was B

| # | Do | Capture |
|---|---|---|
| R1c | `/` **Create playbook** with the same `(table).docx`: is it synthesised as on 11-05? | One line |
| A/B | `/` **Create playbook** with `Documents/Cowork/Test/POC-Act1/word-ab/A - title only.docx`, then with `B - no ids.docx` | Which one imports directly? That tells us whether the preamble triggers synthesis |

---

## Results (one line each; Claude fills in the detail)

| Test | Answer |
|---|---|
| R1 (A/B, topic count, compromise positions kept?) | A; 24 topics plus General guidelines; Compromise Positions dropped |
| R2 (how Attach from Cloud reached the team site) | SharePoint picker; Acme Company under Quick access |
| R3 (tracked changes? comments?) | 20/24; with comments: 12 insertions, 17 deletions, 13 comments |
| R4 (saved name; files created) | "Spaarke NDA – Mutual v1.1"; no OneDrive file |
| R1b (Topic ID line kept?) | No, stripped |
| R5 (offered? runs without attaching?) | Yes and yes |
| R6 (copy or link; stale signal; delete or replace and clicks) | Copy; only "Last updated" date; no delete or replace in the chat UI |
| R7 (skill or plain chat?) | Skill: routed to Review contract without `/`, imported an unsaved card, ran; 19/24; edits only on Apply |
| G1, G2 | |
