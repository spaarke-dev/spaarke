# Declarative-agent spike (DA-1): owner script

About 60 minutes in Word desktop, **agent mode**. Question: can a specialist declarative agent give a consistent,
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

### S1. The two agents
SETUP — see "Agent setup" below (filled in after the research refresh and the review of the existing Spaarke AI agent).

## Rules for every test
- **Agent mode** (not chat mode). Start a **new chat** for each test.
- Don't click Apply unless the step says so. If something is applied, close the document **without saving**.
- Screenshots to OneDrive `Documents/Cowork/Test/POC-DA1/screens/` with the names given.
- Both agents print a verification code in their header (`NDAR-…` / `LHR-…`). If it's missing, the agent's
  instructions were not applied: note it.

## Legal Hold Review (LH)

| Test | Do | Capture | Question |
|---|---|---|---|
| LH-1 | Open `POC Workspace/Matters/Harbor Point Logistics/Harbor Point – Legal Hold Notice (draft A).docx`. Type `@Spaarke Legal Hold Review` › *Review this.* Attach nothing; select nothing | `LH-1-*.png` (header, every table row, conclusion) | Does the agent see the whole document and rate all 13 topics? |
| LH-2 | Same document, new chat, **Ctrl+A** first, then the same prompt | `LH-2-*.png` | Any difference from LH-1 (selection vs whole document)? |
| LH-3 | New chat, same document: *Which playbook and version are you using? List all its topics.* | `LH-3.png` | Does the agent read the whole playbook from its knowledge (13 topics), or fragments? |
| LH-4 | Open draft B (`… (draft B).docx`), new chat, same prompt as LH-1 | `LH-4-*.png` | False alarms (expected: only one topic not Acceptable) |
| LH-5 | Draft A again, new chat, LH-1, then: *Apply the Example Text for the escalated topics as tracked changes.* | `LH-5-*.png`; then close **without saving** | Can Word edit after the agent's review, in the same chat? Tracked changes or direct edits? |
| LH-6 | Open `POC Workspace/Matters/Brightwater Systems/Brightwater Mutual NDA.docx`, new chat, `@Spaarke Legal Hold Review` › *Review this.* | `LH-6.png` | Does it say the document isn't a legal hold notice (and point to NDA Review)? |

## NDA Review (N)

| Test | Do | Capture | Question |
|---|---|---|---|
| N-1 | Open `POC Workspace/Matters/Brightwater Systems/Brightwater Mutual NDA.docx` (the original). `@Spaarke NDA Review` › *Review this.* | `N-1-*.png` (header, every row, conclusion) | All 24 topics rated; score against the v1.1 key |
| N-2 | New chat, same document: *Which playbook and version are you using? List all its topics with their Topic IDs.* | `N-2-*.png` | Full playbook read from knowledge (24 topics, v1.2)? |
| N-3 | Open `POC Workspace/Inbox/NDA batch/b01 - Kestrel Mutual Non-Disclosure Agreement.docx`, new chat, N-1 prompt | `N-3-*.png` | False alarms (expected: none) |
| N-4 | Brightwater, new chat: attach `Legal Knowledge/NDA/Spaarke NDA – Mutual v1.2.docx` and type *Review this against the attached playbook* (**no agent**) | `N-4.png` | With agents installed, does Microsoft's Review contract still start (playbook card)? |
| N-5 | In the **same chat** as N-4, after Microsoft's review: `@Spaarke NDA Review` › *Assess the same document with your method.* | `N-5-*.png` | Can Microsoft's flow and our agent work in one chat? Do they agree? |
| N-6 | Brightwater, new chat: N-1, then *Apply edits with comments for the RED topics.* | `N-6-*.png`; close **without saving** | Tracked changes with comments after the agent's review? |
| N-7 | Draft A legal hold notice, new chat, `@Spaarke NDA Review` › *Review this.* | `N-7.png` | Wrong-type guard |

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
