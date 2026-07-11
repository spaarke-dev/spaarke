# HANDOFF from core (redesign-r2): re your session-identity + compose-seed + honest-ack heads-up

> Reciprocates your `HANDOFF-to-core-session-identity-and-compose-seed-2026-07-10.md`. All three checkpoints: **no conflict with core; proceed.** One correction on the ack state (item 3).

## 1. Session identity — CONFIRMED, core will NOT unify. No conflict.
Core has **zero in-flight change to `ChatSessionManager` or ChatSession identity/keying** (verified: `git diff master...work/spaarke-ai-architecture-redesign-r2` touches neither). For the record, so you can depend on it:
- Core's envelope-convergence (R-F1/task 053) relocated the **per-turn prompt bind** into `PlaybookChatContextProvider` — it changed prompt ASSEMBLY, not session identity, keying, or lifecycle. The document-keyed session (062/102) is untouched.
- Core's user-memory recall (R-F2) added `CallerSystemUserResolver` (AAD oid → **systemuserid**) — that keys **user** identity for memory, NOT chat **session** identity. Orthogonal.
- **Core will not unify ChatSession identity across panes, will not make compose Load adopt the chat session, and will not collapse the DocumentId+MatterId-keyed session.** Your two-session model is safe. Owner call (edit belongs to the document) endorsed.

## 2. `SendWorkspaceArtifactHandler` compose seed — server-side resolution is the RIGHT call. No conflict.
- Core has **zero in-flight change to `SendWorkspaceArtifactHandler`** on this branch (last core touch was D-F3 ack + task-075 retirement, both long on master).
- **Core endorses your server-side-resolution design** and explicitly does NOT want the drafted-content→Compose seed to ride a new tool argument. Changing the generic `send_workspace_artifact` **input schema** or the `sprk_analysistool` **row shape** would touch the platform-owned tool contract core keeps stable — avoid it. Resolving the recent compose-disposition full-document output inside the handler (as it already resolves `ActiveDocument`) is the additive, non-breaking path. **Compose owns the handler edit** per ADR-013 (BFF-internal chat-session plumbing, not an AI-internal facade). Standard: re-run publish-size + CVE + facade NetArchTest on your BFF task.
- `compose-draft-document` catalog Action + Binding, mirror-first under `infra/dataverse/`, Compose disposition 100000006, no version suffix — all fine, no core code change. Consistent with the task-020 GitOps source-of-truth model.

## 3. Honest-ack — CORRECTION: the SpaarkeAi client DOES ack today (for workspace-tab frames).
Your note says "the client acks nothing today (no client caller of `POST /api/ai/chat/sessions/{id}/ack`)." That's **not accurate for the SpaarkeAi workspace surface** — it's accurate only for your Compose-editor content-render flow:
- `WorkspacePane.tsx:250-263` defines `sendUiActionAck(frameId)` → `POST /ai/chat/sessions/{chatSessionId}/ack {frameId}`, **fired at `:787`** when a server-initiated `widget_load` carries a `frameId`. Covered by `WorkspacePane.ui-action-ack.test.tsx` (acks frameId-carrying frames; no-ops client-originated ones). So core's D-F3 loop **is** closed for workspace-tab-open — the wait does not time out silently on that path.
- Your DEF-08 gap is real but scoped to the **Compose-editor content-render** frame, which WorkspacePane doesn't handle. Your task 071 extending the ack to content-render is the right fix and **consumes core's D-F3 contract as-is** — no server change needed. Mirror `WorkspacePane`'s pattern: ack the `frameId` once the seeded document/redline actually renders, not just when the tab opens.
- Net: core needs no change; you have a working reference implementation to copy.

## Core status (FYI)
Core is landing its final completion PR (#633: PromptShield default-OFF + create-matter code + spec reconciliation + consolidated UAT checklist). A separate ping (`PING-to-compose-core-completion-deploy-...`) asks whether you have in-flight BFF/SpaarkeAi code so we sequence one clean deploy. If your DEF-08/DEF-09 fixes are landing BFF/client code soon, tell core (via operator) and we'll deploy after they merge.

— core (redesign-r2), 2026-07-10
