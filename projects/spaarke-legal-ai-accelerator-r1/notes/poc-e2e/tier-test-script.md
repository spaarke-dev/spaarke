# Owner test script: individual tiers (base and advanced)

Mechanics only: does each piece install, get found, and do what it should. No scoring yet.
**Word:** sections W-1 to P-1 below are round 1 (run 2026-11-11; kept for reference). Run Word from
"Word round 2", which is organised by the four Word use cases. The Word plugin tests (WP-0 to WP-4) come next,
then Cowork (C-) and advanced (A-) tests.
Design: `notes/accelerator-components-by-surface.md`. Layout and archive: `onedrive-layout.md`.

**How to record.** Screenshots go to OneDrive `Documents/Spaarke Test Documents/Screens/`, named `<test id>-<n>.png`
(for example `G-1-2.png`). For each test, note the result row at the end (or just send me the screenshots). The
**verification code** in the answer tells us which file Copilot really used:

| Code | File |
|---|---|
| `SRM-1110-A` | `Cowork/Playbooks/Review method/Spaarke Review Method.docx` (the real one) |
| `SRM-DECOY-1` | the decoy copy (G-1, G-2 only) |
| `WPI-1112-A` / `WPS-1112-A` | the probe plugin's instructions skill / script skill (WP-0 to WP-2) |
| `NDAR-1112-A` / `LHR-1112-A` | the NDA / Legal Hold Review agents (v0.3 instructions: review in chat, redlines through the skill). `NDAR-1110-A` / `LHR-1110-A` = the old v0.2 agents |

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

## Word round 2 (after round 1 on 2026-11-11), by Word use case

Round 1 showed: W-1 passes; G-1 failed 3 of 3 (the method file was never read); attaching the playbook **and**
the method works for a non-contract; a contract review takes only one attached file. Results: `results.md`, gaps
#36–#38.

**Owner decision (2026-11-11): Word does not look in folders.** Users give Word what it needs in each chat: they
pick a saved playbook, or attach a file with the paperclip or **+**. Our job is a setup that makes the right file
easy to find. So round 2 tests the four Word use cases, not ways to make Word find files on its own:

| | Use case | What drives it |
|---|---|---|
| 1 | **Contract review** (core) | A playbook: saved in Word, or our `.docx` attached. Microsoft's legal skills, which we can't edit |
| 2 | **Contract drafting** (core) | A template or precedent, attached |
| 3 | Non-contract review | Our playbook `.docx` (the review method inside), attached |
| 4 | Non-contract drafting | A template or precedent, attached, plus the matter facts |

**Dropped:** G-1b and G-2 (custom instruction finds the method), R-1a (precedent beside the playbook: Microsoft says
reviews run from playbooks only, and R-1b settles it), G-4. **Moved:** P-1 goes to the Cowork tests (it matters
for the Copilot app, not for Word).

**Before you start.**
1. **Install the rebuilt package.** Extract the new `Spaarke Legal Accelerator - Individual base v0.1.zip` into
   `Documents` and choose **Replace the files in the destination**. What changed: the legal hold Word playbook now
   starts with the review method (G-5); the review method copies the playbook version from the playbook's own text
   (gap #38); the Read me has the Word steps by use case and a folder guide.
2. **Install the new test zip.** Extract `Spaarke test documents v0.1.zip` into `Documents` (replace files). New:
   `Spaarke Test Documents/References/` with five reference documents:
   - `Acme Mutual NDA (model form).docx`: a contract precedent (R-2);
   - `Acme NDA Approval and Signature Authority Policy v1.0.docx`: a policy, rules numbered `NDA-1.1` to `NDA-5.1`
     (R-1b);
   - `Acme Legal Hold Notice Template v1.0.docx`: an approved template (R-3);
   - `Legal Hold Notice – Kestrel Freight (2025-02-28).docx`: a precedent notice from another matter (R-3);
   - `Harbor Point – Demand letter (2026-10-20).docx`: the matter facts (R-3).
3. Wait about 15 minutes, so the **+** search finds the new files.
4. Rename each test copy before the first prompt, for example `… - R2 U-1.docx`.

### Use case 1: contract review

#### U-1. Saved playbooks: what the user sees and can keep current (gap #32)
1. Fresh copy of `Brightwater Mutual NDA.docx`, Copilot pane. Choose **Review contract with a playbook**.
   Screenshot the playbook list.
2. On each stale entry (v1.1, "NDA - Mutual (Spaarke baseline)"), look for a **…** menu or hover options. If there
   is a delete or remove option, delete **v1.1** only.
3. Start a new playbook by attaching `Cowork/Playbooks/NDA/Spaarke NDA – Mutual v1.2.docx` again, and save it.
4. Open the list again and screenshot it. If a saved playbook can be opened for editing, open v1.2 and count its
   topics.

**Record:** the options each entry offers (delete, rename, edit, none); after step 3, one v1.2 or two; the final
list. **Tells us** what the Read me says about keeping saved playbooks current (re-attach each time, or delete and
re-save on each new version).

#### G-3b. Attaching from Microsoft 365 with **+** (replaces G-3)
1. Fresh Brightwater copy, Allow editing. Select **+** (or the paperclip › attach from cloud) and look for
   `Spaarke NDA – Mutual v1.2` first by **search**, then by **browsing** to `Documents/Cowork/Playbooks/NDA/`.
2. In a second new chat, type `/` and the start of the name instead.
3. Run the review from whichever worked.

**Record:** which routes find the file (search, browse, `/`) and the path each shows; screenshot the picker.
**Pass:** the review card shows v1.2 with 24 topics. If you can reach the Acme team site in the same picker, note
whether its library is offered too (that is the team setup's route).

#### W-2 (optional). The rated chart as a second step
After a contract review, same chat, attach only `Cowork/Playbooks/Review method/Spaarke Review Method.docx`:
*Follow the attached review method to produce the topic-by-topic chart for the review you just ran.* **Record:**
the "Playbook applied" line (expected v1.2, gap #38), the row count (24), and whether each rating matches the
review card's icon. Run it only if the chart is to be part of the contract process.

#### R-1b (optional, 3 minutes). A policy next to the playbook
Fresh Brightwater copy, Allow editing. Attach the NDA playbook **and**
`References/Acme NDA Approval and Signature Authority Policy v1.0.docx`. Type: *Review this NDA against the
attached playbook. Also check it against the attached approval policy and tell me who must approve and sign it.*
**Record:** accepted or refused (exact message); if accepted, any rule number cited (`NDA-3.5`, `NDA-4.2`) or an
approval and signatory answer. **Expected:** refused, as in round 1.

### Use case 2: contract drafting

#### R-2. Draft a new NDA from a precedent
1. New blank document in Word desktop, saved as `Documents/Spaarke Test Documents/R2 R-2 draft.docx`. Copilot pane.
2. Choose the prompt starter **Draft new contract from a precedent** (or type `@Draft-new-contract`). When asked
   for the precedent, attach `References/Acme Mutual NDA (model form).docx`. Type: *Draft a mutual NDA with
   Brightwater Systems, Inc. from this precedent.*
3. Follow Word's steps to the end (fill in the placeholders, then **Finish Draft** if it offers it).

**Record:** whether the starter is offered and asks for a precedent; whether it drafts in the open document or in
the pane; placeholders or content controls, and which; whether the clauses follow the model form (spot-check term,
residuals, governing law); about how long it took.

### Use case 3: non-contract review

#### G-5. One file: the method inside the playbook
Fresh copy of `Harbor Point – Legal Hold Notice (draft A).docx`, Chat only. Attach only
`Cowork/Playbooks/Legal Hold Notice/Spaarke Legal Hold Notice – Employee Custodians v1.0.docx` (its first section is
"How to review with this playbook: Spaarke Review Method v1.0"). Type: *Review this notice against the attached
playbook.* **Record:** code, colours, row count, the "Playbook applied" line. **Pass:** SRM-1110-A, 🟢🟡🔴, 13 rows,
"Spaarke Legal Hold Notice – Employee Custodians v1.0", without attaching the method.

#### H-1. In-place redline after the rated review
Same chat as G-5: switch to **Allow editing** and type *Apply the recommended changes to this document as tracked
changes, with a comment on each change naming the topic and the reason.* **Record:** tracked changes? comments? how
many of each? anything changed you didn't expect (headings, numbering)? **This is the main open Word question.**

### Use case 4: non-contract drafting

#### R-3. Draft the Harbor Point legal hold notice from the template
1. New blank document in Word desktop, saved as `Documents/Spaarke Test Documents/R2 R-3 draft.docx`. Copilot pane.
2. Try **Draft new contract from a precedent** (or `@Draft-new-contract`) first. If it isn't offered, or says it
   only drafts contracts, use agent mode with **Allow editing** instead, and note which route you used.
3. Attach `References/Acme Legal Hold Notice Template v1.0.docx` **and** `References/Harbor Point – Demand letter
   (2026-10-20).docx`. Type: *Draft a legal hold notice for the Harbor Point matter from the attached Acme
   template. Use the attached demand letter for the facts. Keep every section of the template.* If only one file
   is accepted, attach the template and say so in the notes.

**Record:** the route (drafting skill or agent mode); one or two files accepted; drafted in the document or in the
pane; all seven template sections present; `[bracketed]` items left unfilled; facts taken from the letter (Harbor
Point Logistics, the agreement of March 1, 2024, preserve from January 1, 2024); any invented names or dates.

**Optional:**
- Repeat with the **Kestrel Freight precedent** instead of the template. Does Kestrel's matter carry over into the
  Harbor Point draft (names, dates, the transportation system)?
- Review the R-3 draft with G-5's playbook: it is how the two Word use cases for notices join up.

---

## Word plugin tests (WP-0 to WP-4), added 2026-11-12

**Why.** On 2026-11-01 Word listed our NDA plugin but never ran it (T-12b). That test was narrow: Word for the web
only, a plugin uploaded for you only, with skills that need Python. Partner plugins that reach Word (Harvey,
iManage, Litera) work differently: their work runs on their own server, which Copilot calls as an MCP tool. These
tests separate the questions: does Word read a third-party skill's instructions, run its scripts, or call an MCP
tool such as the Spaarke MCP? Design note: `notes/copilot-app-components.md`.

**The probe plugin** (`Spaarke Word probe plugin v0.1.zip`, sent with this script; built by
`src/accelerators/build/build_word_probe.py`). Two harmless skills that review nothing and change nothing:
- `spaarke-word-probe` (instructions only): answers *Run the Spaarke Word probe* with a line holding `WPI-1112-A`.
  The code is only in the skill's instructions, not in its description, so seeing it proves Word read them.
- `spaarke-word-probe-script` (runs Python): answers *Run the Spaarke script probe* with the line its script prints.

Save the zip on your PC **outside OneDrive** (for example `C:\Temp`) and don't extract it, so Word's search can't
find the skill text and fake a pass.

**Reading the script probe's answer:**

| Answer | Means |
|---|---|
| `WPS-1112-A 528452740 python 3.x …` | the script ran |
| `Spaarke script probe · scripts not run · WPS-1112-A` | Word read the skill but can't run scripts |
| `WPS-1112-A` with any other number | Word read the skill and made up the output (record it: a model faking a script run) |
| no `WPS-1112-A` at all | Word didn't load the skill |

### WP-0. Control: the probe works in Cowork (5 minutes; run first)
1. Cowork › Customize › Plugins › **Upload** the probe zip (Only you).
2. New Cowork chat: *Run the Spaarke Word probe.* Then: *Run the Spaarke script probe.*

**Pass:** the `WPI-1112-A` line, then `WPS-1112-A 528452740 python 3.… Linux`. If either fails here, stop: the
probe is broken, and the Word results would mean nothing.

### WP-1. Word desktop: does it run a third-party skill's script?
1. Word for Windows, open any document (a copy of Brightwater is fine). Copilot pane › the **+** or plugins button ›
   **Manage plugins & skills**: switch **Spaarke Word Probe** on. Close Word completely and reopen it.
2. Three tries, each in a new Copilot chat:
   - (a) Type *Run the Spaarke script probe.*
   - (b) Pick the skill `spaarke-word-probe-script` from **+** (it shows as a chip), then type the same.
   - (c) As (b), with agent mode on (Chat only).

**Record:** for each try, the answer per the table above, and screenshots of the plugin list and the answer.

### WP-2. Word desktop: does it follow a third-party skill's instructions?
Same as WP-1, with the skill `spaarke-word-probe` and *Run the Spaarke Word probe.*

**Pass:** the `WPI-1112-A` line plus the document's headings. This is the test that matters most for Word: if Word
follows instructions-only skills, a Word edition of our playbook procedures becomes possible even without scripts.

**Optional:**
- **WP-2b:** WP-1 and WP-2 again in Word for the web, to compare with the 2026-11-01 result.
- **WP-2c (needs an admin):** if WP-1 and WP-2 show no code, have the probe deployed by an admin instead (Microsoft
  365 admin center › Agents › Tools › upload, assigned to you only). Check again after 24 hours.

### WP-3. Word: does it call the Spaarke MCP?
Run this as the user who passed the Spaarke MCP connection test on 2026-11-12 (the demo user), or any licensed user
with **Spaarke MCP** connected. I'm confirming with the MCP project that its test tools are still deployed on the
dev server; wait for my go before running this.
1. Word for the web (an InPrivate window signed in as that user is easiest): open a blank document, Copilot pane ›
   **+** › **Sources** (or the plugins list): is **Spaarke MCP** there? Select it.
2. Type: *Use Spaarke MCP to run SpaarkeMCP_spike_echo with the text "Word probe".*
3. Then: *Run SpaarkeMCP_spike_whoami and show me the full result.*
4. If you can sign in to Word desktop as that user, repeat 1–3 there.

**Record:** whether Spaarke MCP is listed in Word; whether each tool ran (echo returns the server's time; whoami
returns `status: ok` and the tenant id); any approval card Word showed. **Pass:** a tool result with the server's
time. This is the route to the advanced redline in Word: the redline engine would run on our server as a tool.

### WP-4 (optional, 3 minutes). Word: watch a partner plugin run
1. Word desktop, Copilot pane › plugins list: is **Harvey** (or another legal partner) listed?
2. Pick it and type: *Ask Harvey what a mutual NDA is.*

**Record:** what Word does. A Harvey sign-in prompt is enough: it means Word tried to call Harvey's tool. **Don't
sign up or sign in**; just screenshot and close it.

**Afterwards:** remove the probe plugin (Cowork › Customize › Plugins › Remove) so it doesn't show in your lists.

---

## P-1. One custom instruction for Word and Cowork? (moved here from Word; run before C-1)

As written above, in three places: Cowork, the Copilot app, and Word agent mode (Chat only). Word is included only
to record the fact; the Word steps no longer depend on it. **Tells us** whether one paste covers Cowork and the
Copilot app.

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

## A-2. The v0.3 instructions: review in chat, redlines through the skill

Owner decision 2026-11-12: skills first (`notes/copilot-app-components.md`). Rebuild both agents from the new
recipes first (paste the new instructions over the old ones).

1. **Copilot app:** new chat, `@Spaarke NDA Review`, attach a fresh copy of the Brightwater NDA, type *Review this NDA.*
   Then type *Apply these changes to the Word file.*
2. **Cowork:** new chat with a fresh copy, type `/nda-review`. Approve the plan.
3. Repeat 1 with **Spaarke Legal Hold Review** on draft A, then `/legal-hold-review` in Cowork.

**Record:** the code (`NDAR-1112-A` / `LHR-1112-A`); whether the rating cells start with 🟢/🟡/🔴; whether the
agent declined to edit and named the skill (expected), or changed a file (fail). From step 2: whether the plan
table and the redline files came from one request with one approval.

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
| G-1b, G-2 (round 2), R-1a | — | dropped | — | Word doesn't look in folders (owner, 2026-11-11) | — |
| G-4 | — | dropped | — | attaching the .docx works | — |
| **Round 2, use case 1** | | | | | |
| U-1 (saved playbooks) | | | — | options per entry; one or two v1.2 | |
| G-3b (+ from M365) | | | — | search / browse / `/` | |
| W-2 (optional) | | | | "Playbook applied" line | |
| R-1b (optional) | | | — | accepted / refused | |
| **Use case 2** | | | | | |
| R-2 (draft from precedent) | | | — | | |
| **Use case 3** | | | | | |
| G-5 | | | | | |
| H-1 | | | — | | |
| **Use case 4** | | | | | |
| R-3 (draft from template) | | | — | route; files accepted | |
| **Word plugin tests** | | | | | |
| WP-0 (Cowork control) | | | WPI / WPS | must pass before WP-1 and WP-2 count | |
| WP-1 (script skill) (a) (b) (c) | | | | answer per the table | |
| WP-2 (instructions skill) (a) (b) (c) | | | | headings listed? | |
| WP-2b / WP-2c (optional) | | | | web; admin-deployed | |
| WP-3 (Spaarke MCP) | | | — | listed? echo / whoami ran? web / desktop | |
| WP-4 (Harvey, optional) | | | — | sign-in prompt? | |
| **Cowork and app** | | | | | |
| P-1 | | | — | where P1-CHECK appears | |
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
