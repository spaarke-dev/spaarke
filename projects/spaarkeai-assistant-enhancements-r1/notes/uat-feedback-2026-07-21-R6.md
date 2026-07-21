# UAT Feedback R6 — 2026-07-21 (Ralph, dev, after the R5 round-2 deploy)

## Backlog + status

| Item | Feedback | Status |
|---|---|---|
| **R6-1** | After summarize, add a "Revise document" follow-on chip (rename "Revise in Compose" → "Revise document") | ✅ shipped — relabeled + now appended to the post-summarize chip set (when a file is indexed) |
| **R6-2** | After "revise the document", the transcript shows BOTH the old doc-action links AND the new consumer cards | ✅ shipped — one row at a time: the revise-context doc-action chips replace the consumer cards right after a mount, and clear once the user acts (`transcriptFooter` + `handleDocAction`) |
| **R6-3** | Compose "Couldn't place this suggested edit: target text not found" on a mounted file | ✅ resolved — a parallel effort's **"Fix #4"** (whitespace-tolerant fallback in `resolveTargetSpans`, runs only on an exact-miss) was already on master and fixes the same root cause (mammoth docx→HTML re-segmentation vs the server text projection). My duplicate implementation was dropped at merge in favor of the existing one. |
| **R6-4** | The "Send as email" modal is too small vs the standard | ✅ shipped — widened the Assistant email dialog **600 → 760px** (engine `dialog` cap + a `DialogSurface` maxWidth override) with a fuller body. **Modal-size question answered below.** |
| **R6-5** | Email form doesn't look up contacts for To/Cc/Bcc; standard modal's lookup drops the full email | ✅ shipped — wired recipient lookup on the Assistant modal (reuses `searchUsersAndContacts` via host Xrm.WebApi); **fixed the SHARED bug**: `ILookupItem` now carries a first-class `email` (set by `userLookup`, used by `RecipientField`) instead of round-tripping the address through the display name. Fixes the standard CommunicationPage modal too — see deploy note. |
| **R6-6** | "rewrite the response" answered in chat instead of redlining the open Compose doc | ✅ shipped — broadened revise-intent detection (`rewrite`/`redraft` verbs + `response`/`letter`/`email` nouns) and added a branch that routes a whole-document rewrite to the shipped compose-revise **redline** path when a doc is already open in Compose. A specific-**section** rewrite with no selection now instructs the user to highlight it + use "Draft alternative". |

## R6-4 — answering "do we have standard Spaarke modal sizes?"
**No single enforced modal-width token exists.** `docs/standards/MODAL-DECISION-CRITERIA.md` defines two layouts (OOB `navigateTo` at 85%×85%; or a proprietary Fluent dialog with **content-driven** width), but no shared width constant. De-facto today, each modal hardcodes its own: form/content dialogs cluster **520–640px** (CloseProjectDialog 520, AppErrorBoundary 640, ActionConfirmation 480), the email **page** composer is **960px**, and `RichFilePreviewDialog` is **1280px/85vh**. The Assistant email dialog was at the small end (Fluent's ~600px default + the engine's 600px `dialog` cap); R6-4 raised it to 760px. **Recommendation (open):** if consistency across modals matters (it does — they're core UX), we should establish a small shared set of width tokens (e.g. `sm 480 / md 640 / lg 960 / xl 1280`) and migrate modals onto them. Flag if you want that as its own cleanup.

## Deploy note
- **Deployed `sprk_spaarkeai`** to dev (R6-1/2/4/5a/6 + the shared email/lookup fixes it bundles).
- **R6-3** is already live via master's "Fix #4".
- **The shared recipient-email fix (R6-5b)** also fixes the STANDARD email modal, but that surface (`sprk_communicationpage` / EmailComposerSlot) was **not separately redeployed this round** — the fix is in the merged shared source and applies on its next deploy. Say the word and I'll build + deploy CommunicationPage to make it live there now.

## Tests
76 SpaarkeAi + 73 redline tests pass. Commits squashed into `d3428b930` (merged origin/master).
