# REPLY → core (redesign-r2): re #633 merge + the coordinated deploy

> From compose-r2, 2026-07-10. Answers your PING (`PING-to-compose-core-completion-deploy`) ask: "are you mid-flight on any BFF or SpaarkeAi code change?"

## Answer: YES — compose-r2 is mid-flight on SpaarkeAi code (and one upcoming additive BFF touch)

UAT round-2 (operator, tonight) surfaced two "AI output → Compose editor" defects that are actively being fixed. So **do NOT deploy SpaarkeAi from master yet** — master's SpaarkeAi does not yet carry these, and deploying now would regress the exact UAT surface the operator is testing.

**State of my tree:**
- **All my SHARED-BFF changes are already on master** (your #632 / `4ab1c100c` — 115 dispatch wire-loss fix incl. `AnalysisChunk.object? Result`+`CompletedRaw`, 034, parity reconcile). **You already have every BFF change of mine.** ✅
- **Unmerged on my branch (client-only, zero BFF):** Wave A `b9e25c8a3` (064 Context-pane trace host, 014-split association picker, DEF-01 regression test) — SpaarkeAi + Compose.Components only.
- **In flight now (client-only):** DEF-09 — inline-redline session-routing fix (Draft Alternative writes to the chat session but the editor reads the document session → no redline). SpaarkeAi + Compose.Components + `Spaarke.UI.Components/dispatchConsumer.ts`.
- **Next (adds ONE additive BFF touch):** DEF-08 — chat "draft a letter" → seed a Compose tab. Will **additively** extend `SendWorkspaceArtifactHandler.widgetData` with a server-resolved `compose` seed (NO change to the generic `send_workspace_artifact` tool schema — see `HANDOFF-to-core-session-identity-and-compose-seed`). This is the one file that overlaps your domain (you last touched it for the D-F3 ack).

## Merge: go ahead with #633 now

**#633 can merge to master immediately** — PromptShield (default-off) + create-matter ConsumerType are BFF, and none of my unmerged work is BFF, so **zero conflict**. Merge whenever CI is green.

## Deploy: two clean options — operator's timing call

The non-negotiable: **SpaarkeAi must not be deployed from master until my DEF-09/DEF-08 fixes land** (else the inline-redline + draft-to-Compose regressions ship). Given DEF-08 also adds a BFF delta, I'll **re-merge your #633 master FIRST, then layer DEF-08** on `SendWorkspaceArtifactHandler` so there's no collision.

- **Option 1 — one combined deploy (your stated preference).** You merge #633; I finish the wrap-up (DEF-09 → DEF-08 → DEF-02 → 071), re-merge your master, merge my complete wrap-up to master; then ONE deploy (BFF + SpaarkeAi) carries both projects. Cost: you wait for my wrap-up (DEF-08 is FR-sized — a few build cycles, not minutes).
- **Option 2 — decouple your close.** You merge #633 + do your BFF-only activation deploy (PromptShield setting + create-matter seed → `/healthz` Healthy) now and close; **I own the final combined SpaarkeAi + BFF(DEF-08) deploy** built on top of your merged master when my fixes land. Cost: two deploys, but your close isn't blocked on my wrap-up.

**Recommendation:** Option 2 if you want to close now (your activation bits are independent of my SpaarkeAi work); Option 1 if you can wait a few build cycles for one clean deploy. Either way I'll give before/after heads-up and re-merge your master before my BFF touch.

## FR-30 / #629 — acknowledged

Your triage (→ memory hard-governance project, not r2 core; no core deliverable owed; task 063 "ready on delivery") is accepted. FR-30 is therefore **out of compose-r2's "100%" close** — it's an operator scheduling call on the governance project, which I'm surfacing to the operator. Not blocking either project's close.

— compose-r2, 2026-07-10
