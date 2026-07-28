# UAT — AI Advisory Review Word-comment export gap (2026-07-28)

> **🔜 ADDRESS IN THE NEXT PROJECT: `ai-advanced-capabilities-agreements-r1`** (directory + project NOT yet created —
> create it and pull this in when initiated). Logged here in `ai-advanced-capabilities-nda-r1` for now because this is
> the NDA advisory-comments feature and there is no agreements-r1 folder yet.
> **Source**: owner dev-UAT of a saved-then-opened-in-Word NDA. **Investigated**: 2026-07-28 (code-grounded).
> **Owner subsystem**: Compose **client-side comment export mapping** (NOT the AI playbook — the data already exists;
> NOT Compose R4/R4.5/R5 core). Currently **untracked** (task 040 only ensured comments *reach* Word, via raw text).

## The observed problem

When an NDA reviewed in Compose is saved and opened in Word, each AI review comment renders as:
- **Author** = "AI Advisory Review"
- **Body** = `"Grounded fact — …"` (raw prose), with **no structure**.

## Expected format (owner)

Each Word comment should mirror the on-screen gutter's structure:
1. **Author** — "AI Advisory Review" (owner questioning whether this is the right name — make it configurable).
2. **Label "Flagged clause"** (NOT "Grounded fact").
3. **"Assessment says: …"** section.
4. **"Standard: …"** reference — at minimum the citation, ideally the full standard clause text.

## Root cause (ONE cause, four symptoms)

The Word-comment **export bakes the AI review's raw `explanation` string verbatim** + a hardcoded author. The structured/renamed rendering (Flagged clause / Assessment says / Standard) exists **only in the on-screen gutter** and is **never applied to the exported `w:comment`**. The playbook already produces the data.

Pipeline: `nda-review` Action output `{ sectionRef, quotedText, riskLevel, explanation, standardRef }`
→ `useNdaReviewAdvisoryCommentsBridge.ts:91-108` → `ComposeEditor.placeAdvisoryComments` (`ComposeEditor.tsx:2492`)
`createThread(item.explanation, …)` → thread `{ author:'AI Advisory Review', text: explanation, standardRef/riskLevel/sectionRef = UI-only metadata }`
→ export `composeSessionCommentThreadsToAnchoredComments` (`ComposeCommentThread.types.ts:256-262`) reads **only** `author`/`text`/`timestamp`
→ server `ComposeShadowPatchEngine.ApplyComment` (`:664-698`) writes `w:comment` faithfully.

## Per-issue root cause (file:line)

| # | Issue | Root cause | Fix |
|---|---|---|---|
| a | Author "AI Advisory Review" | Hardcoded literal `ComposeEditor.tsx:2146` (`useComposeCommentThreads(editor, 'AI Advisory Review')`) | Make it a prop/config; one line |
| b | "Grounded fact" not "Flagged clause" | Relabel map is **display-only** (`ComposeCommentGutter.tsx:343-347`, `parseAdvisoryNote` `:357-378`); export uses raw `explanation` | Apply the same relabel when composing `commentText` for export |
| c | Missing "Assessment says" | Same — the "Assessment says" label is gutter-only. The judgment prose lives *inside* the one `explanation` string, so it MAY already be in Word under the raw marker **if the model emitted it** for that finding. | Split + relabel on export. **Runtime check**: inspect the ledgered `dispatched.result.flaggedSections[].explanation` to confirm the judgment segment was produced. |
| d | Missing "Standard" reference | `standardRef` **is** produced (`nda-review.schema.json:45`) + carried as thread metadata (`ComposeEditor.tsx:2495`), but **explicitly dropped at export** — the docx export reads only author/text/timestamp (`ComposeCommentThread.types.ts:89`); `ComposeAnchoredComment` has no standard field. | Add `standardRef` (citation, ideally + full clause text) to the exported comment; lift the "UI-only, never-exported" scope decision. |

## Where the fix goes (Compose client, two candidate seams)

1. `ComposeEditor.placeAdvisoryComments` (`ComposeEditor.tsx:2492`) — build a **structured export string** ("Flagged clause: … / Assessment says: … / Standard: …") instead of passing raw `item.explanation`; OR
2. the export mapping `composeSessionCommentThreadsToAnchoredComments` (`ComposeCommentThread.types.ts:256-262`) — assemble `commentText` from `thread.text` + `thread.standardRef` (requires lifting the never-export scope at `ComposeCommentThread.types.ts:89`).

Server (`ApplyComment`) needs **no change** — it renders whatever `commentText`/`Author` it is given.

## Scope note

- **Playbook** (`infra/dataverse/actions/nda-review.action.json`, `outputschemas/nda-review.schema.json`) needs **no change** for (b)/(c)/(d) — the data exists (grounded-fact + judgment in `explanation`; separate `standardRef`). Possible future playbook enhancement: split `explanation` into discrete `flaggedClause` / `assessment` fields so the export doesn't have to string-parse markers.
- **Prior work** (`ai-advanced-capabilities-nda-r1` UAT rounds 5–7, `current-task.md:111,122,144`) built the gutter relabel + "Standard:" line as **display-only**. The **export mirror** was never built — this note.
