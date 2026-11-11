# Synopsis: playbook review and redlining in Word desktop and in Cowork (as of 2026-11-10)

What we tried, and what works, across the POC (Acts 2–7) and spike DA-1. Detail and scores: `results.md`.
"Key" means our answer keys; "gap #n" refers to `gap-log.md`.

## At a glance

| Capability | Word desktop: contract (NDA) | Word desktop: non-contract (legal hold notice) | Cowork desktop |
|---|---|---|---|
| Review against our playbook, rated per topic | **Yes.** *Review contract* with the playbook attached; 19–20 of 24 topics match the key | **Only with a method.** With the review protocol: 13 of 13 counts match the key. Without it: editing suggestions, no ratings | **Yes.** Our skill 12 of 13 flagged topics (one NDA); 99% of topic cells across 16 NDAs |
| Redline in the **open** document | **Yes.** Real tracked changes, applied when you click Apply | **Partly.** Editing suggestions can be applied; tracked changes with comments after a playbook review not yet tested | **No.** Produces a **new** redlined file; the preview is read-only |
| Comments explaining each change | **Yes** (*Apply edits with comments*) | Not tested | **Yes**, but only when asked for explicitly |
| Policy check | Separate skill (*Identify regulatory gaps*), 4 of 4 | Partly tested (first finding correct) | Through Microsoft's `draft-legal-memo`; our review skill doesn't read policy |
| Custom agent (`@Spaarke NDA Review`) | Reviews; **can't edit** the open document; can't share a chat with the Legal plugin | Same | Reviews **and** builds a redlined file with comments (2026-11-10) |

## 1. Word desktop, Copilot without a custom agent

### Contracts (NDA)
**Works**
- **Review contract with our playbook.** Attach the four-column library playbook and type "Review this NDA against
  the attached playbook". Word routes it to *Review contract* by itself (no `/` command) and builds a playbook card
  from the file. Attaching each time always uses the current library version (R7).
- **Ratings per topic:** Acceptable / Needs review / Not acceptable / Not relevant. 20 of 24 (R3) and 19 of 24 (R7)
  match the key.
- **Redline in place.** *Apply all › Apply edits with comments* writes real tracked changes and comments into the
  open document (Brightwater: 12 insertions, 17 deletions, 13 comments, attributed to the user). Nothing changes
  until the user clicks Apply.
- **Follow-on skills.** *Identify regulatory gaps* against the internal policy: 4 of 4 key items. *Check execution
  readiness*: pass (blank effective date, incomplete signature blocks).
- **Team library reachable:** Attach from Cloud lists the Acme Company site under Quick access.

**Doesn't work, or has limits**
- **Run-to-run variation:** the same playbook gave different ratings on 3 topics between two runs (gap #29).
- **Repeated misses:** independent development and compelled disclosure rated *Needs review* where the key says RED;
  the non-solicit fallback is lost because *Compromise positions* are dropped on import (gap #30).
- **Over-reach in edits:** it deleted the whole non-solicit and residuals sections, headings included, without
  renumbering.
- **Saved playbooks are frozen copies.** No link to the library, no version check and no delete; stale cards pile up
  and a user can silently review against an old position (R6). Hence "attach, don't save".
- **Topic IDs are stripped** on import, so results can't be tied back to our topic IDs.
- **Policy isn't an input** to *Review contract*. Users must run *Identify regulatory gaps* and type the policy under
  "Something else".

### Non-contracts (legal hold notice)
**Works**
- **With a stated method.** The owner's review protocol, referenced from the custom instruction (agent mode, no
  `@agent`), gave a header, a 13-row table and totals of 5 Meets / 2 Acceptable fallback / 6 Escalate, exactly the key.
- **Chat-only mode with an explicit prompt** ("topic by topic") also gives a per-topic table.
- **Content quality is good even unguided:** in *Allow editing* mode it found all 6 REDs, including the clause that
  was missing.

**Doesn't work, or has limits**
- ***Review contract* is for contracts only.** A non-contract playbook review in *Allow editing* mode becomes
  Word's generic editing suggestions: no ratings, categories Copilot invents, 7 of 8 issues (gap #33).
- **Without a playbook it improvises** from a web search and doesn't ask for one.
- **Stray files become sources.** Old skill and playbook copies in OneDrive were cited (gap #34).
- **Not yet tested:** applying the fixes as tracked changes with comments after a playbook review (spike LH-5 and the
  hand-off test).

### Modes matter
- **Chat mode** ignores custom instructions and can't `@` agents.
- **Agent mode** reads custom instructions and accepts `@agent`.
- ***Allow editing* vs *Chat only*** changes the route for the same prompt.

### With a custom agent (spike DA-1, for comparison)
- The agent reviews in chat (verification code shown, so its instructions apply).
- **It can't edit the open document.** Asked to apply changes, it creates a revised copy instead.
- Once `@`-mentioned it owns the chat, so Microsoft's Legal skills (*Review contract*, *Apply edits with comments*)
  aren't available in that chat.

## 2. Cowork desktop

### With our plugin skills (Acts 3–6)
**Works**
- **One NDA (`nda-review`):** 12 of 13 flagged topics match the key. A plan card comes first and nothing is built
  before approval. Outputs: an internal redline with comments and a linked summary, an external redline with no
  comments, a clean copy, a dashboard and an audit pack. Saved beside the original after one approval. 122–166
  credits per review.
- **A position change is picked up:** after the library moved to v1.1 it used v1.1, said the profile was stale and
  rated term GREEN.
- **PDF input:** rebuilds a Word copy and says so; asks for the counterparty's Word file.
- **Library review (`nda-library-review`):** 16 NDAs × 24 topics, 375 of 384 cells (99.0% after kit errors), every
  one of 356 quotes found word for word, 185 credits. Three follow-on redlines for 96 credits more.
- **Microsoft's Legal for Cowork skills** use our material well: `summarize-redlines` (4 of 4 changes),
  `draft-legal-memo` (applies the policy), `compare-key-terms`, and `review-contract` across a folder (16 of 16
  overall status).

**Limits**
- `nda.representatives` is misread as RED every time (playbook wording, gap #16).
- `nda-review` doesn't read the policy; the memo skill does.
- Approval cards are repetitive (gap #24).
- Redlines are files, not edits to an open document.

### With the custom agent (owner, 2026-11-10)
Steps: new chat (no agent), open `Brightwater Mutual NDA.docx`, add the **Spaarke NDA Review** agent, "review this
NDA", then "apply edits including redline and comments".

**Works**
- **Review:** the agent's format with header and code `NDAR-1109-A` (its instructions applied), "the full 42-line
  NDA … against all 24 playbook topics", and citations to `Spaarke NDA – Mutual v1.2`. The cited copy's 2013 date
  is the converter's default, so it appears to be the table-free knowledge copy.
- **Redline:** `Brightwater Mutual NDA - Redlined.docx` with real tracked changes and comments. The comments are
  authored "Spaarke NDA Review" and start with the topic ID (for example `nda.one-sided-carveouts: Delete the
  Acme-only exclusion …`).
- **Custom instruction applied:** Sources › Personalization shows "Mark results as drafts for attorney review".
- **Chat-editing the document afterwards** works, slowly.

**Doesn't work, or has limits**
- **Redline and comments only on request.** "Apply edits" alone didn't give them; the owner had to ask.
- **The preview pane is read-only.** *Open in Word* opens **Word for the web**, not desktop.
- **Word for the web** has no Microsoft Legal plugin. It shows the Spaarke NDA (MVP) plugin, but earlier tests found
  plugins don't run there.
- **One agent or skill at a time.** Swapping (remove one, add another) works; running two together doesn't.
- **Stray sources in Word for the web.** When the owner added the legal hold playbook there, Copilot's *Verify
  sources for your request* listed old OneDrive test files (MVP retest notes, `SKILL`, converted-plugin probes,
  `03-nda-review-instructions`, old NDA playbooks, "Spaarke Cowork for Legal") next to the playbook (gap #34). The
  rest of that step's result wasn't recorded.

## What follows
- **In place, in Word:** Microsoft's *Review contract* with our playbook (contracts only), or Compose (Spaarke's
  editor), or a Word add-in of our own (out of R1 scope). A custom agent in Word can't edit.
- **Redlined file:** Cowork does this well, with our skills or with the custom agent, and it's the only route that
  works the same for any document type.
- **Non-contracts in Word:** need a stated method (protocol or agent) for a rated review. Whether the fixes can then
  be applied as tracked changes with comments is still open.
- **Clean-up needed:** remove stray skill and playbook copies from OneDrive (gap #34). They now show up in Word for
  the web too.
