# UAT defects — launch context + session scoping (operator live test, 2026-07-12)

> Two defects found opening Spaarke AI via **Document → "Open in Compose"** on spaarkedev1. Both are SpaarkeAi client-side; both root-caused to specific code.

---

## DEF-UAT-1 — Assistant has NO document/host context after "Open in Compose"

**Symptom** (operator screenshot): "Open in Compose" on a `sprk_document` opens Spaarke AI; the **Compose editor loads the document** (the CIPO patent letter renders), but the **Assistant chat has no context** — "summarize this document" → *"Please upload the document…"*; "what is the document we are working?" → *"No document has been uploaded or loaded in this session yet."*

**Root cause (primary) — URL parameter-name mismatch.** The launcher and the app disagree on the parameter key for the host entity type:
- Launcher writes **`entityLogicalName`**: `launch-resolver.ts:138` → `record["entityLogicalName"] = params.entityLogicalName` (DocumentComposeLaunch passes `entityLogicalName: "sprk_document"`).
- App reads **`entityType`**: `main.tsx:421` → `searchParams.get("entityType") ?? dataParams.get("entityType")`.

So `entityLogicalName` arrives in the URL but `main.tsx` looks for `entityType`, finds nothing, and the app's `entityLogicalName` is **undefined**. The host context never forms. (`entityId`, `sprkDocumentId`, `speDriveItemId`, `composeMode` use matching keys, which is why the **Compose editor** loads the doc while the **Assistant** is context-blind.)

**Fix (small, high-confidence).** Make the URL key consistent. The app is the source of truth (`main.tsx` reads `entityType`; the UAT/URL contract documents `entityType`), so the launcher should emit `entityType`: in `buildLaunchUrl`, write `record["entityType"] = params.entityLogicalName` (keep the TS field name; only the emitted URL key changes). Add a launch-resolver test asserting the emitted string contains `entityType=`.

**Root cause (secondary / design) — host document ≠ chat-loaded content.** Even with the host context fixed, "summarize **this** document" needs the document's **text** available to the chat. Today the Assistant expects an **uploaded session file**; a host `sprk_document` (record id) is not auto-ingested as chat-usable content. The Compose editor loads the DOCX (document session); the Assistant (chat session) does not share it — the two-session split compose-r2 documented. So a full fix also needs: when launched on a host `sprk_document` (or when a doc is open in Compose), make its extracted text available to the Assistant so "summarize this document" resolves. This is the larger half and overlaps compose-r2's session-identity surface.

**Severity**: High for the "open a doc and ask the assistant about it" flow — the headline cross-pane experience is broken. Part 1 (param key) is a quick win; part 2 (share the doc with the Assistant) is a design task.

---

## DEF-UAT-2 — Chat session is not context-scoped (home page shows the Document's session)

**Symptom**: after using the Assistant in the Document launch, navigating to the **home page** (unbound mount) still shows **the Document session's conversation**. Expected: each new **context** starts its own session; prior sessions move to **History**.

**Root cause.** The chat session id is persisted as a **single global key** (`sprk_ai2_chatSessionId`) in `localStorage` (+ sessionStorage) via `AiSessionProvider` (see `ConversationPane.tsx:410-411`, `ConversationPane.new-session.test.tsx`). It is **not namespaced by host context**, so any mount — Document or unbound home — restores the same last session. There is no "new context → new session, old to History" keying.

**Fix (design).** Scope the persisted session id by context (e.g. key by host `entityType:entityId`, and a distinct key for the unbound home), so switching surfaces starts/loads the context-appropriate session and prior sessions are addressable from History. This is squarely the **two-session / session-identity** area compose-r2 has been working (DEF-09) — coordinate ownership: the fix belongs with whoever owns `AiSessionProvider` session keying.

**Severity**: Medium-High — sessions bleed across contexts; a matter/doc conversation shows up on the unbound home. Confusing and violates the expected per-context isolation.

---

## Ownership / coordination
Both are **SpaarkeAi client** defects on the surface compose-r2 is actively deploying (DEF-08/09 session identity). DEF-UAT-1 part 1 (param key) is an isolated launch-resolver fix core can own; DEF-UAT-1 part 2 + DEF-UAT-2 touch session identity (compose-r2's active area) → coordinate. Recommend filing both as issues and deciding fix ownership with compose-r2 before editing `AiSessionProvider`/session keying.
