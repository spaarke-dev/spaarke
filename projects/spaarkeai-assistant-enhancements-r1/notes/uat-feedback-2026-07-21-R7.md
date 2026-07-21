# UAT Feedback R7 — 2026-07-21 (Ralph, dev, after the R6 deploy)

**Meta-principle for the whole batch (R7-0):** *never dead-end with no next action.* Every surface the user lands on must offer a determinative next step — action cards (bordered, `→`) for actions, help/question pills (blue) for follow-up questions.

Note on the two affordance kinds (owner-confirmed, not a bug):
- **Follow-up question pills** (blue) = LLM-suggested next *questions*. Correct as pills.
- **Action cards** (bordered buttons with `→`) = Send as email / Save to document / Create a matter, etc. Correct as cards.

## Backlog + status

| Item | Feedback | Status |
|---|---|---|
| **R7-1** | Opening the Quick Start menu/modal makes the follow-up question pills **disappear permanently** from the transcript | ✅ shipped — root cause was the **⋮ menu opening AssistantToolMenu's OWN second QuickStartModal**; ConversationPane now passes `onQuickStart` so the ⋮ uses the ONE ConversationPane-owned modal. Opening it never remounts SprkChat, so the SSE suggestion pills survive. (Static trace confirmed a modal open alone never clears pills; the dual-modal path was the culprit.) |
| **R7-2** | Choosing Quick Start dead-ends: no file loads, no context, no clear next step. Acceptable that no file loads, but the surface MUST offer a determinative next step (cards + help pills) | ✅ shipped — `QuickStartModal.onCardLaunched` → ConversationPane injects a determinative next-step assistant message when a wizard card launches; the consumer cards (transcriptFooter) remain as follow-on actions. The ⋮ path now ALSO carries session file context (unified modal). |
| **R7-3** | Chat instruction "create a new matter **from this file**" opens the wizard but the file is **not loaded**. "from this file" should thread the session file(s) into the wizard handoff | ✅ shipped — `handleSurfaceLaunch` now carries ALL promoted session files (`promotedFileIdsByNameRef`, the same source Quick Start uses) with an active-source-doc fallback. Root cause: the single `activeSourceDocRef` is often still null right after an upload, so the file leg was empty. Keeps the server's drafted field values (still the server-driven surface_launch path — no client NL detection). |
| **R7-4** | Substantial-output asks ("write a brief on the chevron doctrine", "analyze this agreement", "draft a memo…") dump the long text into the Assistant chat. They should route to a **Compose tab** with the generated document; the chat shows only a short confirmation ("A brief on the Chevron Doctrine has been prepared.") + follow-on cards. | ✅ shipped — new `composeDraftRouting.detectDraftDocumentIntent` (verb+doc-noun / analyze-this-doc) intercepts the typed message in the decorate seam → dispatches the **Active** `compose-draft-document` capability (disposition=compose) via a discovered bindingId → `runBindingDispatch` opens a Compose tab from `body_html` + posts a short confirmation + follow-on cards; the raw agent turn is suppressed. Deterministic (no reliance on the model picking the tool). Client-only — `compose-draft-document` confirmed statecode=0 Active in dev. |
| **R7-5** | (from the follow-up screenshot) "write me an engagement letter email **in the open Compose tab**" was answered in chat instead of drafting into Compose | ✅ **routing** shipped — an explicit "…in/into the (open) Compose tab / this document" phrase forces the R7-4 draft-into-Compose route even for email/letter-worded asks (which otherwise go to draft-correspondence). ⚠️ **append-into-open-tab NOT yet done** — see below. |

## Screenshots (owner-captured)
1–2. Summarize → "what is effective date" → answer, then blue follow-up pills below (correct).
3. Post-Draft action cards (Send as email / Save to document / Create a matter / More) + help pills + attached-file chip (correct — this is the target pattern).
4. "write a brief on the chevron doctrine" — full brief rendered IN chat with citations (WRONG — should go to Compose per R7-4).

## R7-5b — "append INTO the open Compose tab" (owner decision pending; NOT in this deploy)
Owner chose **append** for "write X in the open Compose tab" (keep the tab's existing content, add the draft below).

**Why it's a follow-on, not in this deploy:** the shipped `compose-draft-document` materialize path (`ComposeWorkspace.tsx` ~L2034 `mountDraftHtml`) **replaces/mounts** the editor content — there is NO "append body_html at the end of the open document" mode today. True append needs a small **shared-lib (Spaarke.Compose.Components) addition**: an append action on the editor reducer + a Flow-5 `insertMode:'append'` path, plus the ConversationPane leg that reads the compose-outputs ledgerRef and emits the append event. That's a shared-lib change + dist rebuild + tests — deliberately scoped OUT of this client-only deploy.

**What ships now covers the reported case:** the screenshot's tab was BLANK, and for a blank/no-open tab the draft lands in Compose correctly (replace == append when empty). Append-preserving only matters when the open tab already has content the user wants to keep.

## Deploy (2026-07-21)
- **`sprk_spaarkeai`** rebuilt + deployed to dev (R7-1/2/3/4 + R7-5 routing). **No BFF change** — `compose-draft-document` was already Active in the dev catalog.
- Tests: composeDraftRouting (new) + QuickStartModal (fixed a stale R5-8 assign-work assertion) green; SpaarkeAi build exit 0.
