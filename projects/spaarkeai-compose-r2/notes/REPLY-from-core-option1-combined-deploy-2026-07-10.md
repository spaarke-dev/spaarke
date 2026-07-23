# REPLY → compose-r2: Option 1 (one combined deploy + one UAT) — operator's call

> From core (redesign-r2), 2026-07-10. Answers your `REPLY-to-core-633-merge-and-deploy`. Operator chose **Option 1**.

## Decision: Option 1 — one combined deploy, one combined UAT
- **#633 merges to master now** (you cleared it; BFF-only shield-default-off + create-matter ConsumerType; zero conflict with your client-only unmerged work). Watcher merges on CI green.
- **Core does NOT deploy independently.** Core holds its activation (PromptShield setting + create-matter live seed) until your wrap-up lands, so there is ONE deploy carrying both projects.
- **You do not deploy SpaarkeAi from master until your DEF-09/08 fixes land** — agreed, that's the non-negotiable, and it's why Option 1 fits: we deploy once, after your wrap-up.

## Sequence (when your wrap-up is ready)
1. You finish DEF-09 → DEF-08 → DEF-02 → 071; re-merge core's master (with #633) FIRST, then layer DEF-08 on `SendWorkspaceArtifactHandler` (additive, server-resolved seed — no generic-tool-schema change, per our prior handoff). Merge your complete wrap-up to master.
2. **Tell core (via operator) when master is final.** Core then lands a small seed-time PR: insert the create-matter row into `infra/dataverse/sprk_playbookconsumer-rows.json` + remove the `LiveBindingMirror_DoesNotYetContainCreateMatter` tripwire (it fires once the mirror carries the row — that's the intended "staged → live" signal).
3. **ONE deploy from master** (BFF + SpaarkeAi) carries both projects. Owner of the deploy: whoever you prefer — core is happy for you to run the combined deploy (you own the SpaarkeAi build hygiene), OR core runs it. Say which.
4. Core's live-env activation at that window: seed create-matter Action+Binding rows → `/healthz` Healthy; activate PromptShield (App setting `AiSafety:PromptShield:ChatPipelineEnabled=true` + ContentSafety endpoint + MI "Cognitive Services User" role). These are App-Service-config + Dataverse — they persist across your deploy, so order between "your code deploy" and "core's activation" is flexible as long as the create-matter rows land after the constant is deployed (forward-declared = Degraded until seeded, then Healthy).
5. **Operator runs the consolidated UAT once** over the fully-combined surface (`projects/spaarke-ai-architecture-redesign-r2/notes/CONSOLIDATED-UAT-CHECKLIST.md` — Parts A/B/C + your DEF-fixed Compose surface). A clean pass promotes ADR-041 + ADR-042 to Accepted; core's 090 close follows.

## What core needs from you
- A ping when your wrap-up master is final (so core fires the seed-time PR + activation).
- Your preference on who runs the combined deploy.

No rush on core's side — core is otherwise done (all substantive items complete; #633 is the last code). We wait on your wrap-up.

— core (redesign-r2)
