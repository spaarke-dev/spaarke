# Declarative-agent spike (DA-1): owner script

About 85 minutes (15 to install, 70 to test) in Word desktop, **agent mode**. Question: can a specialist declarative agent give a consistent,
complete playbook review in Word, for a contract (**NDA Review**) and a non-contract (**Legal Hold Review**)?
Background: `results.md` Act 7 (Word chat mode vs agent mode; owner's review protocol). Answer keys: `answer-key/`
(Claude scores; don't open them).

## Before you start

### S0. Neutral custom instruction (2 minutes)
The protocol line in your custom instruction would mix with the agents' own instructions. For this spike, replace it
with a neutral one (Copilot › Settings › Personalization › Customize Copilot) and **Save customization**:

```
I'm an in-house lawyer at Acme Company. Mark results as drafts for attorney review. Never send email on my behalf.
```

Restore your protocol instruction after the spike (S9).

### S1. Install the agents (about 15 minutes)
Round 1 found that agents built in Agent Builder or Copilot Studio don't appear in Word (`results.md`, Spike DA-1).
So install them the way Spaarke AI was installed: as **declarative-agent packages**.

The packages are in OneDrive `Documents/Cowork/Test/POC-DA1/packages/` (built by `da-spike/Build-SpikePackages.ps1`):

| Package | Name to type after `@` in Word | What it contains |
|---|---|---|
| `spaarke-nda-review-da.zip` | Spaarke NDA Review DA | Skill `spaarke-nda-review`: the review method and the NDA playbook v1.2 |
| `spaarke-legal-hold-review-da.zip` | Spaarke Legal Hold Review DA | Skill `spaarke-legal-hold-review`: the method and the legal hold playbook v1.0 |
| `spaarke-legal-review-da.zip` | Spaarke Legal Review DA | Both skills. It picks one from the document (the front door) |

The packages have no knowledge sources, no actions and no sign-in. Each playbook travels inside its agent as a skill,
so `.md` files and tables are not an issue.

**Install each zip** (either route):
- **Teams admin center**, as for Spaarke AI: *Teams apps › Manage apps › Upload new app*, then choose the zip.
  Check that the app is available to you.
- **Teams desktop**, if your tenant allows custom apps: *Apps › Manage your apps › Upload an app › Upload a custom
  app*. This installs it for you only.

Then restart Word. In agent mode, type `@Spaarke` and screenshot the list (`S1-picker.png`). Note which of the three
appear, and whether your Copilot Studio agents appear too. New apps can take a while to show up. If an upload is
rejected, screenshot the error (`S1-upload-error.png`) and send it to me; I'll fix the package.

**How to tell what answered.** Each packaged agent starts its answer with
`Agent: … (package) · skill: …`, and the skill's header carries `NDAR-1109-B` or `LHR-1109-B`.
## Rules for every test
- **Agent mode** (not chat mode). Start a **new chat** for each test.
- Don't click Apply unless the step says so. If something is applied, close the document **without saving**.
- Screenshots to OneDrive `Documents/Cowork/Test/POC-DA1/screens/` with the names given.
- Both agents print a verification code in their header (`NDAR-…` / `LHR-…`). If it's missing, the agent's
  instructions were not applied: note it.
- If an answer appears **twice** in the pane (a known issue for declarative agents in Word), note it once in the
  results; it isn't a fault in our agent.

## Legal Hold Review (LH)

| Test | Do | Capture | Question |
|---|---|---|---|
| LH-1 | Open `POC Workspace/Matters/Harbor Point Logistics/Harbor Point – Legal Hold Notice (draft A).docx`. Type `@Spaarke Legal Hold Review DA` › *Review this.* Attach nothing; select nothing | `LH-1-*.png` (header, every table row, conclusion) | Does the agent see the whole document and rate all 13 topics? |
| LH-2 | Same document, new chat, **Ctrl+A** first, then the same prompt | `LH-2-*.png` | Any difference from LH-1? (Microsoft says an agent reads the whole document by default, so there should be none) |
| LH-3 | New chat, same document: *Which playbook and version are you using? List all its topics.* | `LH-3.png` | Does the agent read the whole playbook from its knowledge (13 topics), or fragments? |
| LH-4 | Open draft B (`… (draft B).docx`), new chat, same prompt as LH-1 | `LH-4-*.png` | False alarms (expected: only one topic not Acceptable) |
| LH-5 | Draft A again, new chat, LH-1, then: *Apply the Example Text for the escalated topics as tracked changes.* | `LH-5-*.png`; then close **without saving** | Can Word edit after the agent's review, in the same chat? Tracked changes or direct edits? |
| LH-6 | Open `POC Workspace/Matters/Brightwater Systems/Brightwater Mutual NDA.docx`, new chat, `@Spaarke Legal Hold Review DA` › *Review this.* | `LH-6.png` | Does it say the document isn't a legal hold notice (and point to NDA Review)? |

## NDA Review (N)

| Test | Do | Capture | Question |
|---|---|---|---|
| N-1 | Open `POC Workspace/Matters/Brightwater Systems/Brightwater Mutual NDA.docx` (the original). `@Spaarke NDA Review DA` › *Review this.* | `N-1-*.png` (header, every row, conclusion) | All 24 topics rated; score against the v1.1 key |
| N-2 | New chat, same document: *Which playbook and version are you using? List all its topics with their Topic IDs.* | `N-2-*.png` | Full playbook read from knowledge (24 topics, v1.2)? |
| N-3 | Open `POC Workspace/Inbox/NDA batch/b01 - Kestrel Mutual Non-Disclosure Agreement.docx`, new chat, N-1 prompt | `N-3-*.png` | False alarms (expected: none) |
| N-4 | Brightwater, new chat: attach `Legal Knowledge/NDA/Spaarke NDA – Mutual v1.2.docx` and type *Review this against the attached playbook* (**no agent**) | `N-4.png` | With agents installed, does Microsoft's Review contract still start (playbook card)? |
| N-5 | In the **same chat** as N-4, after Microsoft's review: `@Spaarke NDA Review DA` › *Assess the same document with your method.* | `N-5-*.png` | Can Microsoft's flow and our agent work in one chat? Do they agree? |
| N-6 | Brightwater, new chat: N-1, then *Apply edits with comments for the RED topics.* | `N-6-*.png`; close **without saving** | Tracked changes with comments after the agent's review? |
| N-7 | Draft A legal hold notice, new chat, `@Spaarke NDA Review DA` › *Review this.* | `N-7.png` | Wrong-type guard |

## One agent with two skills (B)

| Test | Do | Capture | Question |
|---|---|---|---|
| B-1 | Brightwater NDA, new chat, `@Spaarke Legal Review DA` › *Review this document.* | `B-1-*.png` (first line, header, every row, conclusion) | Did it pick the NDA skill from the document alone (code `NDAR-1109-B`, 24 rows)? |
| B-2 | Legal hold draft A, new chat, same prompt | `B-2-*.png` | Did it pick the legal hold skill (`LHR-1109-B`, 13 rows)? |
| B-3 | Open `Legal Knowledge/NDA/Spaarke NDA – Mutual v1.2.docx` (a playbook, not an NDA), new chat, same prompt | `B-3.png` | Does it decline, or mistake the playbook for an NDA? |

## Research checks (R)

| Test | Do | Capture | Question |
|---|---|---|---|
| R-1 | Keep `Spaarke NDA – Mutual v1.2.docx` open (the four-column table). New chat, `@Spaarke NDA Review DA` › *Quote the Instructions cell for topic nda.residuals in the open document.* | `R-1.png` | Can an agent read a table in the open document? (Research says tables are dropped) |

## Optional
- **FD-1 (front door):** `@Spaarke AI` › *I need to review a legal document.* Does the existing agent ask for the
  document type or suggest a specialist? (Tests what the existing agent does today; no change to it.)
- **CM-1 (chat mode):** switch to chat mode and type `@`. Confirm agents are not offered (screenshot).

## S9. After the spike
Restore your protocol custom instruction.

## Results (one line each; Claude scores against the keys)

| Test | Answer |
|---|---|
| LH-1 / LH-2 (whole document? 13 topics?) | |
| LH-3 (playbook read in full?) | |
| LH-4 (false alarms) | |
| LH-5 (edits after review?) | |
| LH-6 (wrong-type guard) | |
| N-1 (24 topics; score) | |
| N-2 (playbook read in full?) | |
| N-3 (false alarms) | |
| N-4 (Review contract still starts?) | |
| N-5 (both in one chat?) | |
| N-6 (tracked changes with comments?) | |
| N-7 (wrong-type guard) | |
| B-1 / B-2 (right skill picked? complete?) | |
| B-3 (declines a non-NDA?) | |
| R-1 (table read?) | |
| Duplicate answers seen? | |
| S1: which agents appear in the `@` list (packages? Copilot Studio agents?) | |
| Installing: anything that didn't match S1 | |
