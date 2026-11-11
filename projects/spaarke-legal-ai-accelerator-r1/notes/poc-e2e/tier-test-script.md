# Owner test script: individual tiers (base and advanced), round 1

Mechanics only: does each piece install, get found, and do what it should. No scoring yet.
Design: `notes/accelerator-components-by-surface.md`. Layout and archive: `onedrive-layout.md`.

**How to record.** Screenshots go to OneDrive `Documents/Spaarke Test Documents/Screens/`, named `<test id>-<n>.png`
(for example `G-1-2.png`). For each test, note the result row at the end (or just send me the screenshots). The
**verification code** in the answer tells us which file Copilot really used:

| Code | File |
|---|---|
| `SRM-1110-A` | `Cowork/Playbooks/Review method/Spaarke Review Method.docx` (the real one) |
| `SRM-DECOY-1` | the decoy copy (G-1, G-2 only) |
| `NDAR-1110-A` / `LHR-1110-A` | the NDA / Legal Hold Review agents (v0.2 instructions) |

Test documents (from the test zip): `Spaarke Test Documents/Brightwater Mutual NDA.docx` (contract) and
`Harbor Point – Legal Hold Notice (draft A).docx` (non-contract). **Before any test that edits, save a copy**
(File › Save a copy, add the test id to the name) so the originals stay clean.

---

## 0. Preparation (once, about 15 minutes)

1. **Fix OneDrive sync on this PC.** It stopped downloading on about 2026-11-07. Cloud icon › Settings › Pause
   syncing › Quit OneDrive, then start OneDrive. Check: `Documents/Cowork/Test` is gone locally (it was moved to
   the archive in the cloud).
2. **Turn off the old Cowork plugin.** In Cowork, Customize › Plugins (or Skills): turn off or remove
   **Spaarke NDA (MVP)**. Its skills have the same names as the new skill folders.
3. **Clear the custom instruction.** Copilot app › Settings › Personalization › Custom instructions: copy the
   current text somewhere safe, then delete it.
4. **Install the base package.** Extract `Spaarke Legal Accelerator - Individual base v0.1.zip` into OneDrive
   `Documents` (choose `Documents` itself as the target; the zip's top folder is `Cowork/`, so it adds
   `Cowork/Skills/` and `Cowork/Playbooks/` to the Cowork folder you already have and replaces nothing). Extract
   `Spaarke test documents v0.1.zip` into `Documents` too.
5. **Paste the custom instruction** from `Cowork/Playbooks/Start here/Spaarke – Custom instruction.txt` into
   Settings › Personalization › Custom instructions. Save.
6. Wait for OneDrive to show the green ticks, then wait about 15 minutes so search picks up the new files.

---

## W-1. Word, contract (regression check of the packaged Word playbook)

1. Open a copy of `Brightwater Mutual NDA.docx` in **Word desktop**. Copilot pane, **Allow editing**.
2. Attach `Cowork/Playbooks/NDA/Spaarke NDA – Mutual v1.2.docx`. Type: *Review this NDA against the attached playbook.*
3. When the review shows, choose *Apply all › Apply edits with comments*.

**Record:** the playbook card name; the number of topics; whether tracked changes and comments appear in the
document. **Pass:** card shows v1.2, 24 topics, tracked changes with comments in the open document.

## G-1. Word: does the custom instruction find the method file by its name? (decoy present)

1. Copy `Spaarke Test Documents/Decoy/Spaarke Review Method.docx` to `Documents/` (OneDrive root of Documents).
   It has the same name as the real method file but code `SRM-DECOY-1`. Wait 15 minutes.
2. Open a copy of `Harbor Point – Legal Hold Notice (draft A).docx` in Word desktop. Copilot pane, **agent
   mode**, **Chat only** (not Allow editing).
3. Attach `Cowork/Playbooks/Legal Hold Notice/Spaarke Legal Hold Notice – Employee Custodians v1.0.docx`.
   Type: *Review this notice against the attached playbook.*
4. Repeat steps 2–3 in **three new chats**.

**Record per chat:** the verification code in the header (SRM-1110-A, SRM-DECOY-1, or none); the number of rows;
Sources (expand and screenshot). **Tells us:** whether naming the file in the custom instruction is enough when a
copy with the same name exists.

## G-2. Word: does a full file link in the custom instruction pin the exact file?

1. In OneDrive, select `Cowork/Playbooks/Review method/Spaarke Review Method.docx` › Copy link (people in your
   organisation). Edit the custom instruction: replace the method file's name with that link. Save.
2. Leave the decoy in place. Repeat G-1 steps 2–3 in **three new chats**.

**Record:** the code per chat. **Pass:** SRM-1110-A all three times.

Afterwards, **move the decoy** to `Spaarke test archive (not current)/` and keep whichever custom instruction
worked better (tell me which).

## G-3. Word: `/`-reference instead of attaching

1. New chat, agent mode, Chat only, a fresh copy of draft A.
2. Type *Review this notice against* then `/` and pick **Spaarke Legal Hold Notice – Employee Custodians v1.0**,
   then type *using* `/` and pick **Spaarke Review Method**. Send.

**Record:** what the `/` picker offered (screenshot it, with paths); the code; the row count. **Pass:** the right
two files are offered and used.

## G-4. Word: Markdown playbook instead of Word

Same as G-1 step 3, but attach `Cowork/Playbooks/Legal Hold Notice/Playbook master/Spaarke Legal Hold Notice –
Employee Custodians v1.0.md`. **Record:** whether Word accepts the `.md` file; row count; code.

## H-1. Word, non-contract: apply the review as tracked changes with comments

1. After a complete review from G-1, G-2 or G-3 (same chat), switch to **Allow editing** if needed and type:
   *Apply the recommended changes to this document as tracked changes, with a comment on each change naming the
   topic and the reason.*

**Record:** does the open document get tracked changes? comments? how many of each? did it change anything you
didn't expect (headings, numbering)? **This is the open question** from Act 7: can Word redline a non-contract in
place after a rated review?

## P-1. One custom instruction for Word and Cowork?

1. Add one line at the end of the custom instruction: *Start every answer with "P1-CHECK".* Save.
2. New chat in Word (agent mode): *hello*. New chat in Cowork: *hello*. New chat in the Copilot app: *hello*.

**Record:** where P1-CHECK appears. Then remove the line. **Tells us:** whether one paste covers both surfaces.

---

## Word round 2 (after round 1 on 2026-11-11)

Round 1 showed: W-1 passes; G-1 failed 3 of 3 (the method file was never read); attaching the playbook **and**
the method works for a non-contract; a contract review takes only one attached file. Results: `results.md`, gaps
#36–#38. Run in this order. G-4 (`.md` playbook) is dropped: attaching the `.docx` works.

**Before you start.** The decoy was edited in round 1 (its "Review protocol" line became a link). Replace both
decoy copies (`Spaarke Test Documents/Decoy/` and `Documents/`) with a fresh one from the test zip. Rename each
test copy before the first prompt, for example `… - R2 G-1b.docx`.

### P-1 first (5 minutes)
As above, but in **four** places: Word agent mode with Chat only, Word agent mode with Allow editing, Cowork, the
Copilot app. **Tells us** whether Word loads the custom instruction at all. If P1-CHECK never appears in Word,
skip G-1b and G-2: the custom-instruction route is out for Word.

### G-1b. Same as G-1, a day after install (indexing)
Two new chats, as G-1 steps 2–3. Chat 1: the same prompt. Chat 2: *Review this notice against the attached
playbook, following my review method.* **Record:** code or none, and Sources (expanded) for each.

### G-2 (as designed). A link in the custom instruction
Change the **custom instruction**, not the method file: Copilot app › Settings › Personalization › Custom
instructions. In the first bullet, replace `"Spaarke Review Method.docx", in Documents/Cowork/Playbooks/Review
method/` with the file's link (OneDrive › right-click the real method file › Copy link). Save. Then one chat as
G-1. **Record:** code or none. Put the custom instruction back afterwards.

### H-1. Non-contract, in-place redline after the rated review
After any complete rated review of draft A (G-1b, G-2, G-5, or round 1's "attach both" route), same chat:
switch to **Allow editing** and run H-1 as written above. **This is still the main open Word question.**

### G-5. One file for non-contracts: the method inside the playbook (needs a rebuild; I'll send the file)
Attach only `Spaarke Legal Hold Notice – Employee Custodians v1.0.docx` from the rebuilt package, whose first
section is the review method. Chat only, *Review this notice against the attached playbook.* **Record:** code,
colours, row count. **Pass:** SRM-1110-A, 🟢🟡🔴, 13 rows, without attaching the method.

### W-2. Contract: the method chart as a second step (after the rebuild)
After a W-1 review, same chat, attach only the method file: *Follow the attached review method to produce the
topic-by-topic chart for the review you just ran.* **Record:** the "Playbook applied" line (expected v1.2, gap #38),
the row count (24), and whether each rating matches the review card's icon.

### G-3 (optional). `/`-reference instead of the paperclip
As written above. Useful only if typing `/` is easier for users than attaching.

---

## C-1. Cowork: fresh install from the zip

1. New Cowork chat. Type `/` and screenshot the list.
2. Run `/accelerator-setup`, choose **Quick**, answer name and initials, choose the **Spaarke baseline**, skip the
   trial. Approve saving the profile.

**Record:** which Spaarke skills appear (expected: accelerator-setup, nda-review, nda-library-review,
legal-hold-review); whether the probe step ran its script; the saved profile path and its `Playbook folder:` line.
**Pass:** four skills listed without any plugin; profile saved to `Documents/Cowork/Profile/legal-profile.md`;
playbook folder under `Documents/Cowork/Playbooks/`.

## C-2. Cowork: NDA review reads the playbook from `Cowork/Playbooks`

1. New chat. Attach or point to `Spaarke Test Documents/Brightwater Mutual NDA.docx`. Type: *Review this NDA.*
   Choose **No email** on any card. Approve the plan.

**Record:** the provenance line (expected `Standards: Spaarke NDA – Mutual v1.2 (customer playbook)`), where it
says it found the playbook, the output files and where they were saved, credits used.

## C-3. Cowork: legal hold review and redline

1. New chat. Point to `Spaarke Test Documents/Harbor Point – Legal Hold Notice (draft A).docx`. Type: *Review
   this legal hold notice.* Approve the plan; choose **No email**.

**Record:** the 13-row table (screenshot), the totals line, whether `… - Spaarke redline.docx` was created, and
in it: the shaded summary table on page 1, tracked changes, comments starting with `lh.`, any invented facts
(dates, names) instead of `[CONFIRM …]` placeholders.

---

## A-1. Agent Builder: build the agents from the recipes, knowledge from OneDrive files

1. Open `Cowork/Playbooks/Start here/Spaarke – Agent recipe – NDA Review.docx`. In the Copilot app › Agents ›
   Create agent › **Configure**, enter the name, description and instructions from the recipe.
2. Knowledge: choose **OneDrive files** and pick `Cowork/Playbooks/NDA/Spaarke NDA – Mutual v1.2 (agent
   knowledge).docx` (don't upload a copy). Add the suggested prompts. Create.
3. Do the same for **Legal Hold Review** with its recipe and `(agent knowledge).docx`.
4. In each agent: *Which playbook and version are you using? List all its topics with their Topic IDs.*

**Record:** whether Agent Builder accepts the OneDrive file as knowledge; the topic count (expected 24 and 13);
any missing topics. If Agent Builder only offers upload, say so and upload instead.

## A-2. The v0.2 instructions: review plus automatic redlined copy

1. **Cowork:** new chat, open the Brightwater NDA, add **Spaarke NDA Review** (the new Agent Builder agent), type
   *Review this NDA.* Don't ask for a redline.
2. **Word desktop:** a copy of the Brightwater NDA, agent mode, `@Spaarke NDA Review` *Review this NDA.*
3. Repeat 1 with **Spaarke Legal Hold Review** on draft A.

**Record:** the code (`NDAR-1110-A` / `LHR-1110-A`), whether the rating cells start with 🟢/🟡/🔴, and whether a
`… - Spaarke redline.docx` was created **without being asked** (Cowork), with the shaded summary page. In Word,
what the agent does about the redline (expected: says it can't create files and lists the edits).

## A-3. Does a playbook change reach the agent without rebuilding it?

1. Open `Cowork/Playbooks/NDA/Spaarke NDA – Mutual v1.2 (agent knowledge).docx` and add a sentence to the
   **Term and survival** topic: *Test marker A3: three years is preferred.* Save and close.
2. After 15 minutes, and again after an hour: ask the agent *What does the playbook say about term and survival?*
3. Remove the sentence afterwards.

**Record:** when (if at all) the marker appears.

---

## Record sheet

| Test | Date | Result (pass / fail / partial) | Code seen | Notes | Screens |
|---|---|---|---|---|---|
| W-1 | 2026-11-11 | pass | — | 24 topics; 16/16/15 (attach) and 11/16/12 (picker) | test-results/ |
| G-1 (3 chats) | 2026-11-11 | fail | none ×3 | method never read; Sources = playbook only | test-results/ |
| G-2 (3 chats) | 2026-11-11 | not run as designed | SRM-1110-A when attached | decoy file edited instead; "attach both" works | test-results/ |
| P-1 (round 2, first) | | | — | | |
| G-1b (2 chats) | | | | | |
| G-2 (round 2) | | | | | |
| H-1 | | | | | |
| G-5 | | | | | |
| W-2 | | | | | |
| G-3 (optional) | | | | | |
| G-4 | — | dropped | — | attaching the .docx works | — |
| C-1 | | | — | | |
| C-2 | | | — | | |
| C-3 | | | — | | |
| A-1 | | | — | | |
| A-2 | | | | | |
| A-3 | | | — | | |

## Later rounds (not in this script)
- **Team, no IT** (needs a second user who isn't the site owner): T-1 read-only library, T-2 shared plugin, T-3
  shared Agent Builder agent, T-4 setup in team-member mode.
- **Team, IT** (needs admin): I-1 admin-deployed plugin, I-2 packaged agents, I-3 packaged agent with library
  knowledge.
- **Preflight:** what users see when the Legal plugin, Cowork, Agent Builder or organisation-built plugins are off.
