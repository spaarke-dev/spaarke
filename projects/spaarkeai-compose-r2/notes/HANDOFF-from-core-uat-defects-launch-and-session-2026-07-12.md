# HANDOFF → compose-r2: UAT launch/session defects — core fixed part 1, two parts are yours

> From core (redesign-r2), 2026-07-12. Two defects from operator live UAT opening Spaarke AI via **Document → "Open in Compose"**. Full diagnosis: `projects/spaarke-ai-architecture-redesign-r2/notes/UAT-defects-launch-context-and-session-2026-07-12.md`.

## Core already fixed + deployed — DEF-UAT-1 part 1 (host-context param mismatch)
- **Root cause**: the shared ribbon launcher (`launch-resolver.ts buildLaunchUrl`) emits `entityLogicalName`, but the R2 app (`main.tsx`) read only `entityType` → the host record was silently dropped on **every ribbon launch** (Assistant had no document context).
- **Fix (merged `07aecc801`, PR #637)**: `main.tsx` now reads either key (`entityType ?? entityLogicalName`). **Deployed to spaarkedev1** (`sprk_spaarkeai` rebuilt from master-merged worktree + published). Re-merge master to pick it up.

## Yours (your session-identity surface — DEF-09 area)

### DEF-UAT-1 part 2 — the Compose/host document's TEXT isn't shared with the Assistant
Even with the host context now flowing, "**summarize this document**" still fails ("no document uploaded in this session") because the Assistant needs the document's **content**, not just the host record id. The Compose editor loads the DOCX (document session); the Assistant (chat session) doesn't share it — the two-session split you documented. **Ask**: when launched on a host `sprk_document` (or when a doc is open in Compose), make its extracted text available to the Assistant so doc-grounded chat works. This is the headline cross-pane experience.

### DEF-UAT-2 — chat session is not context-scoped
After using the Assistant on the Document launch, navigating to the **home page** still shows the **Document's** session. Root cause: the chat session id is a **single global `localStorage` key** (`sprk_ai2_chatSessionId`) in `AiSessionProvider`, **not namespaced by host context**. Every mount restores the same last session. **Ask**: key the persisted session by host context (host `entityType:entityId`, distinct key for the unbound home) so each context starts/loads its own session and prior ones are addressable from History.

## Why yours
Both touch `AiSessionProvider`/session identity + the Compose↔Assistant document sharing — your active DEF-09/two-session surface. Core's part-1 param fix is isolated launch plumbing (done). Flag if you want core to take either part instead; otherwise they're yours. No rush from core's side — core is otherwise at UAT/close.

— core (redesign-r2)
