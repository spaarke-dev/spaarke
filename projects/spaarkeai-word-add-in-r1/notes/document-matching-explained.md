# How Word documents get matched to Spaarke — the decision, in plain terms

> Written 2026-09-17 for the owner, who asked: *"I'm not following the question or decision — provide more
> explanation."* This is the explainer for the one decision that still blocks tasks 014, 025 and 045.
> Nothing here is new work; it restates what the code does today and what the three options would change.

---

## ✅ DECIDED — owner, 2026-09-17

**"Follow the recommended approach (A+C with B also playing supporting role)."**

| Option | Verdict | Task |
|---|---|---|
| **A** — the invisible marker inside the `.docx` | ✅ **BUILD** | **014** (server-side stamp) + **051** (client-side reader, new) |
| **C** — the collision prompt on save | ✅ **BUILD** | **025** |
| **B** — the content hash | ✅ **KEEP as support** — already shipped (028); no new work | — |

**Two sequencing constraints come with it, both from `notes/014-xml-part-stamp-decisions.md` — neither is optional:**

1. **Version-path stamping first; create-path stamping only after 025 lands.** Create-path stamping makes the D1
   collision strictly worse until 025's refuse-before-upload exists (§6e). This is data safety, not preference.
2. **Precedence: a resolved cloud URL wins; the stamp is the fallback.** This *overrides* task 014's POML step 5
   ("prefer the stamp"), because stamp-first has a documented data-loss case (§9): a copy carrying its source's
   stamp X, whose URL resolves to row W, would have W's content written into X's file.

**One accepted consequence**, recorded as a §6.5 path-A exception in `spec.md`'s ADR Tensions table: once stamping
ships, the `sprk_canonicaldocument` **link** can no longer be produced for Word-pane saves, because each record's
stamp makes its stored bytes unique by construction. NFR-08's data-safety half is untouched — a create still always
creates, and suppress is still never used for editable documents. A lost link costs a notification, never a record.

---

## 1. The thing we are trying to prevent

A user opens a Word document and saves it to Spaarke. Later — same document — they save again.

Spaarke must answer one question: **is this the same document I already have, or a new one?**

- Answer it right → the second save becomes a **new version** of the existing `sprk_document`.
- Answer it wrong → Spaarke creates a **second record** for the same document, and the two drift apart.
  The user sees two rows with the same name, each with half the history.

## 2. What Spaarke can already do (no decision needed)

**When the user opens the file from Spaarke**, the match is exact and automatic. The file lives in SharePoint
Embedded, Word knows the URL it opened, and that URL contains the SPE item id. Spaarke stores that id on the
document row (`sprk_graphitemid`, with a uniqueness key), so the pane asks the server "who owns this item id?"
and gets a definite answer. This is task 012/013, already shipped. **It never mis-matches.**

## 3. Where it breaks

The id lives in the *location*, not in the *file*. So Spaarke loses the thread whenever the document arrives by
any other route:

- the user downloads it to their PC and opens that copy;
- someone emails it to them and they open the attachment;
- it is copied to OneDrive, or to a network share;
- it was created locally and has never been in Spaarke at all.

In every one of those cases Word opens a file with no Spaarke identity attached. Save it, and Spaarke has no
basis to connect it to the record it came from — so it creates a new one.

## 4. The three ways to close that gap

### Option A — put an invisible marker inside the file (this is task 014 / FR-02)

**What it is:** when Spaarke saves a document, it writes the document's Spaarke id into a hidden part of the
`.docx` itself (a "custom XML part" — the same mechanism Word uses for document properties).

**Crucially, this is NOT the file name.** Nothing the user sees changes: not the file name, not the document
text, not anything in the Word window. The marker travels *inside* the file, so it survives being downloaded,
emailed, renamed, or copied to another machine.

- **Catches:** every copy of a document Spaarke has saved at least once, however it got to the user.
- **Cost:** Spaarke writes into the user's file. That is a real change of posture — the bytes differ from what
  the user produced, and the write happens on save.
- **Limits:** only helps *after* the first Spaarke save (a brand-new document has no marker yet), and it is
  forward-only — documents saved before this shipped stay unmarked.
- **Note on your earlier answer:** you said we cannot add the id to the document *file name*, and that is not
  what this does. If the objection was to hidden data inside the file at all, that rules Option A out — but if
  the objection was to the visible name, Option A is still open.

### Option B — compare the content (already built, for a narrower purpose)

**What it is:** hash the bytes and compare. Identical content ⇒ same document. Spaarke already does this
(`sprk_canonicalhash`, task 028) to spot duplicate uploads.

- **Catches:** unmodified copies — the emailed attachment nobody edited.
- **Misses:** everything else. Change one character and the hash changes, so an edited copy looks like a
  brand-new document. This is why it cannot be the primary answer.
- **Cost:** none. It is already there, and it keeps working whatever else you choose.

### Option C — ask the user (this is task 025)

**What it is:** when a save would collide with something Spaarke already has — same name, same matter — stop
and ask, instead of guessing:

> *"There's already a document called **Acme Merger Agreement** on this matter. Save this as a new version of
> it? / Use a different name? / Cancel."*

- **Catches:** the case the user can recognise, which is most of the ones that matter in practice.
- **Misses:** a copy that arrives under a different name — no collision is detected, so nothing is asked and a
  duplicate is created quietly.
- **Cost:** one prompt at save time, and it only fires on a collision.

## 5. What these do and don't cover, side by side

| Situation | A: invisible marker | B: content hash | C: ask the user |
|---|---|---|---|
| Opened from Spaarke | already handled by the item id — no option needed | — | — |
| Downloaded, then edited, then saved | ✅ matched | ❌ looks new | ⚠️ only if the name still collides |
| Emailed copy, unedited | ✅ matched | ✅ matched | ⚠️ only if the name still collides |
| Copy renamed by the user | ✅ matched | ✅ if unedited | ❌ no collision, silent duplicate |
| Brand-new document, first save | n/a (nothing to match) | n/a | n/a |
| Document saved before this shipped | ❌ unmarked | ✅ if unedited | ⚠️ if the name collides |
| Writes into the user's file? | **yes** | no | no |

## 6. What I recommend, and why

**A + C together**, with B kept as the cheap extra it already is:

- **A** is the only option that reliably answers "same document?" after an edit, which is the common case —
  people edit documents, that is what documents are for.
- **C** is the honest fallback for everything A cannot know about: an unmarked document, or the first save.
  It also protects against the one failure mode that has no technical fix — two genuinely different documents
  that happen to share a name.
- **B** costs nothing and already catches exact copies.

**If you'd rather Spaarke never wrote anything into a user's file**, then A is out and the answer becomes
**C alone, strengthened**: the prompt has to carry more weight, and we accept that a renamed copy will create a
duplicate silently. That is a legitimate choice — it trades some duplicates for never touching the file — but
it should be made deliberately, because it is the difference between "Spaarke usually knows" and "Spaarke knows
when the user tells it".

## 7. What your answer unblocks

| Task | What it becomes |
|---|---|
| **014** | Builds the invisible marker (Option A), or is closed as declined |
| **025** | Builds the collision prompt (Option C) — needed under either answer, but it is the *whole* answer if A is declined |
| **045** | 🔴 The pane currently uploads the bytes it read when the Save tab opened, so edits made after that are silently lost. Queued behind 025 because both touch the same save path |
| — | Also settles 047's leftover: a *new* document saved as B, then A, then B again still loses the third save through the same collision path 025 will handle |

## 8. The smaller question inside this one — now answered

Should the pane's **Document Name** default to something more specific than the file name, because
`Untitled Document.docx` collides so often? **Owner, 2026-09-17: no — default to the file name.** Task 020
shipped exactly that. Naming stays as the user made it; collisions are handled where collisions belong, in
option C's prompt.
