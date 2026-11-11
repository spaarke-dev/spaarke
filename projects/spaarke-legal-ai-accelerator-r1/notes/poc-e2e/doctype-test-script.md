# Document-type library tests (W0–W4): owner script

About 40 minutes in Word desktop. Questions: can one habit ("attach the playbook for the document type, ask in plain
words") work for a contract **and** a non-contract? And does the NDA v1.2 Word copy keep its fallbacks (X2b)?
Background: `results.md` Act 7. Kit: `kit-src/make_doctype_library.py`. Answer keys: `answer-key/` (Claude scores;
don't open them).

## Before you start: the one-time setup (2 minutes)

Replace your Copilot custom instruction. The current one names "Spaarke NDA – Mutual v1.0" in a folder that no
longer exists. Go to Copilot › Settings › Personalization › Customize Copilot, delete the old text, paste the text
below, and click **Save customization**. The same text is in the quick card and in `user-steps.md`.

```
I'm an in-house lawyer at Acme Company. For legal document reviews:
- Use the playbook for the document's type from Acme Company › Documents › Legal Knowledge (the file at the top of that type's folder). Never use files in an Archive folder.
- If I ask for a review without attaching a playbook, ask me to attach the playbook for the document type before you review.
- Each type's policies are in its Policies folder.
- Mark results as drafts for attorney review. Never send email on my behalf.
```

## Rules for every test

- Start a **new Copilot chat** for each test.
- **Don't click Apply.** We are reading the ratings, not editing. If something gets applied, close the document
  without saving.
- Save screenshots to OneDrive `Documents/Cowork/Test/POC-Act7/screens/` with the names given. Several per test are
  fine (`-1`, `-2`).
- "Attach X" always means: paperclip › **Attach from Cloud** › Acme Company › Documents › … › X.

## The library now (for orientation)

```
Documents/
├── Legal Knowledge/                               (design §5.9: the same shape for every document type)
│   ├── Start here – Quick card.docx
│   ├── Shared templates/
│   ├── NDA/
│   │   ├── Spaarke NDA – Mutual v1.2.docx        ← the playbook to attach
│   │   └── Playbook master/ · Policies/ · Precedents/ · Templates/ · Archive/
│   └── Legal Hold Notice/
│       ├── Spaarke Legal Hold Notice – Employee Custodians v1.0.docx   ← the playbook to attach
│       └── Playbook master/ · Policies/ (Acme Records Retention and Legal Hold Policy) ·
│           Precedents/ (Kestrel Freight notice, 2025) · Templates/ (Acme notice template) · Archive/
└── POC Workspace/                                 (test material, not knowledge)
    ├── Matters/Harbor Point Logistics/            demand letter (2026-10-20), draft A, draft B
    ├── Matters/Brightwater Systems/ · Inbox/NDA batch/ · Samples/NDA/ · Test variants/
```

The story: Harbor Point's lawyers sent Acme a demand letter on October 20, 2026, with a preservation demand. A
paralegal drafted the hold notice (draft A). You are the attorney who reviews it before it goes out.

## W0: the user steps (3 minutes)

| Step | Do | Capture |
|---|---|---|
| W0.1 | Open `Legal Knowledge/Start here – Quick card.docx`. Read only the two step lists | — |
| W0.2 | For each list: is any step unclear, or would a lawyer new to Copilot get stuck? Would they know which list to use? | One line per list |
| W0.3 | For W1a and W4 below, **follow the quick card's steps** rather than this script where they differ, and note any step that didn't match what you saw | — |

## W1a: a non-contract with its playbook (the key test)

| Step | Do | Capture |
|---|---|---|
| W1a.1 | Open `POC Workspace/Matters/Harbor Point Logistics/Harbor Point – Legal Hold Notice (draft A).docx` in Word desktop | — |
| W1a.2 | New chat. Attach `Legal Knowledge/Legal Hold Notice/Spaarke Legal Hold Notice – Employee Custodians v1.0.docx`. Type: *Review this against the attached playbook.* | `W1a-start.png`: what Copilot says first (for example a playbook card) |
| W1a.3 | If it shows a playbook card, click **Run review** (not Save) | — |
| W1a.4 | Screenshot the whole list of topics with their ratings | `W1a-findings-1.png`, `-2` |
| W1a.5 | Open **Automatic deletion** and **Preservation instruction**: is there a suggested edit? | `W1a-autodelete.png`, `W1a-preserve.png` |
| W1a.6 | One line: did it treat the notice as a review against the playbook (topic by topic), or answer some other way? Did anything say "contract"? | One line |

## W1b: the same playbook as a numbered requirement list

| Step | Do | Capture |
|---|---|---|
| W1b.1 | Same document (draft A). New chat. Attach `POC Workspace/Test variants/Spaarke Legal Hold Notice – Employee Custodians v1.0 (requirements).docx`. Type: *Review this against the attached playbook.* | `W1b-start.png` |
| W1b.2 | Follow its lead (if it asks how to deliver, choose **In chat**). Screenshot the result | `W1b-result-1.png`, `-2` |
| W1b.3 | One line: which route did it take (a playbook card and topic list as in W1a, a requirement-by-requirement report as in G1, or plain chat)? | One line |

## W2: the wrong playbook

| Step | Do | Capture |
|---|---|---|
| W2.1 | Same document (draft A). New chat. Attach `Legal Knowledge/NDA/Spaarke NDA – Mutual v1.2.docx`. Type: *Review this against the attached playbook.* Run the review if it offers one | `W2-result.png` |
| W2.2 | One line: did it warn that an NDA playbook doesn't fit a legal hold notice? Or did it rate NDA topics against the notice (how many "Not relevant")? | One line |

## W3: no playbook

| Step | Do | Capture |
|---|---|---|
| W3.1 | Same document (draft A). New chat. Attach nothing. Type: *Review this.* | `W3-result.png` |
| W3.2 | One line: did it ask you to attach the playbook (as the custom instruction says), or review on its own? If on its own, did it catch the expiry date, the "clean up drafts" line or the missing auto-delete instruction? This tests whether Word's Copilot follows the custom instruction | One line |

## W5 (optional): check the notice against Acme's policy

| Step | Do | Capture |
|---|---|---|
| W5.1 | Same document (draft A). New chat. `/` **Identify regulatory gaps** → **In chat** → **Something else**: *Acme Records Retention and Legal Hold Policy v1.0. I will attach it.* Attach it from `Legal Knowledge/Legal Hold Notice/Policies/` | `W5-result.png` (or save the report as in G1) |
| W5.2 | Does it find the policy points the draft misses (issued too late after the October 20 letter; no acknowledgment deadline; nothing on auto-delete; a merits comment)? | One line |

## W1c (optional): the clean draft

| Step | Do | Capture |
|---|---|---|
| W1c.1 | Open `… Harbor Point – Legal Hold Notice (draft B).docx`. New chat. Attach the Legal Hold Notice playbook as in W1a and run | `W1c-findings.png`: how many topics are not Acceptable? |

## W4: NDA v1.2 (the X2b retest)

| Step | Do | Capture |
|---|---|---|
| W4.1 | Open `POC Workspace/Matters/Brightwater Systems/Brightwater Mutual NDA.docx` (the original, not your reviewed OneDrive copy) | — |
| W4.2 | New chat. Attach `Legal Knowledge/NDA/Spaarke NDA – Mutual v1.2.docx`. Type: *Review this against the attached playbook.* Run review | `W4-findings-1.png`, `-2` |
| W4.3 | Open **Non-solicitation**, then **View rules** | `W4-nonsolicit.png`, `W4-nonsolicit-rules.png`: does Rules include "Acceptable fallback (accept without redlining): …"? Is the suggestion still to delete the clause? |
| W4.4 | Open **Reverse engineering** and **Compelled disclosure** | `W4-reverse.png`, `W4-compelled.png`: rated Not acceptable, with an added clause or notice? |
| W4.5 | Close the document **without saving** | — |

## Results (one line each; Claude scores against the keys)

| Test | Answer |
|---|---|
| W0 (steps clear? which list to use?) | |
| W1a (route; ratings) | |
| W1b (route; result) | |
| W2 (warned, or applied the NDA playbook?) | |
| W3 (asked, or improvised?) | |
| W1c (false positives) | |
| W4 (fallback in Rules? non-solicit; reverse engineering; compelled disclosure) | |
| W5 (policy points found) | |
