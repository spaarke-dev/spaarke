# HEADS-UP → core (redesign-r2): session-identity model + additive compose-seed on `SendWorkspaceArtifactHandler`

> From compose-r2, 2026-07-10. UAT round-2 surfaced two "AI output → Compose editor" defects (DEF-09, DEF-08). Root causes found; owner made the architecture call. This is a **heads-up + one coordination checkpoint**, not a blocking handoff — the fixes are compose-r2-ownable. Flag back only if you have a conflicting change in flight on the named seams.

## 1. Owner decision (binding): a Compose AI **edit** belongs to the **DOCUMENT session** — keep two sessions

DEF-09 root cause = session-scope mismatch: toolbar compose dispatch (via `ConversationPane.dispatchComposeAction`, `getSessionId()→chatSessionId`) writes the `compose`-disposition ledger entry into the **Assistant chat session**, while `ComposeWorkspace` materializes the redline by reading `compose-outputs` from its **document-keyed session** (`state.sessionId`, minted by compose Load keyed by DocumentId+MatterId — your/our tasks 062/102). Two ids → materialize finds nothing → no inline redline (the accept/reject UI in `ComposeEditor.tsx` exists but `redline.pending` never populates).

**Owner chose (2026-07-10): the edit belongs to the DOCUMENT.** So we are **retaining the two-session model** (chat + document) and routing compose **edit-actions** to the document session client-side. **Ask of core: do NOT unify ChatSession identity across panes from your side** (e.g. do not make compose Load adopt the chat session, or collapse the DocumentId+MatterId-keyed session into the chat session). The document-keyed session (062/102) is load-bearing for cross-version resume + redline durability and we now depend on it explicitly. If you have any in-flight change to `ChatSessionManager` / compose Load session identity, flag it.

The DEF-09 fix itself is **client-only** (SpaarkeAi + Compose.Components dispatch routing + confirmation-only Assistant render). No BFF change. Wire/OutputRouter/115 are correct.

## 2. Upcoming (DEF-08): additive `compose` seed on `SendWorkspaceArtifactHandler` — coordination checkpoint

DEF-08 = Assistant "write a letter" produces inline prose; "open as a document" opens a **blank** Compose tab (the open-tab flow carries no content payload); "yes paste it" is **hallucinated** (no content-injection tool → LLM role-plays). Owner chose **"both layered"**: (A) a new catalog Action routes a chat-drafted document into a Compose tab **seeded with the content**, (B) an "Open in Compose" affordance, (C) honest-ack the content **render** (not just tab-open), + single-tab reuse.

What touches your domain, and how we intend to keep it additive/non-breaking:

- **New catalog Action + Binding** `compose-draft-document` (Compose disposition 100000006; surfaces `assistant,workspace`; toolDescription selects on draft/write intent). **Compose-authored, mirror-first** under `infra/dataverse/` — projects into the agent loop data-driven (same as `draft-correspondence`/`send_workspace_artifact`). No core code change expected. NO version suffix in the action code / consumer type / filenames (owner hygiene).
- **`SendWorkspaceArtifactHandler.widgetData`** gains an **additive** `compose` seed so the opened tab can materialize a full drafted document. **We intend server-side resolution** (the handler resolves the recent compose-disposition full-document output for the session, like it already resolves `ActiveDocument`) — so **NO new argument on the generic `send_workspace_artifact` tool schema** and **no change to the `sprk_analysistool` row shape**. That keeps your platform-owned tool contract untouched. Per ADR-013's note this handler is "BFF-internal chat-session plumbing, NOT an AI-internal facade," so compose-r2 owns the edit; publish-size + CVE + facade NetArchTest will be re-verified on the BFF task.
- **Coordination checkpoint**: if you believe the drafted-content→Compose seed MUST instead ride a new **tool argument** (changing the generic `send_workspace_artifact` input schema), that piece is yours — tell us and we'll hand it over. Our default is the server-side-resolution design above, which avoids the dependency. Also flag any in-flight change you have on `SendWorkspaceArtifactHandler` (you last touched it for the D-F3 ack wait) so we don't collide.

## 3. Honest-ack extension (DEF-08 C / relates to task 071)

Your D-F3 `IUiActionAckCoordinator` currently acks **tab-open** (`SendWorkspaceArtifactHandler` `WaitForAckAsync(WorkspaceTabAckTimeout)`), but the **client acks nothing today** (no client caller of `POST /api/ai/chat/sessions/{id}/ack`) — so the open-tab wait times out silently. Our task 071 will add the client ack, and we plan to **extend the ack to content-render** (the tab isn't "done" until the seeded document / redline actually renders), which closes the "claims a UI action that never happened" (R2-D) class for compose. This consumes your published D-F3 contract as-is; we'll flag if we need any server-side change beyond what's on master.

— compose-r2, 2026-07-10
