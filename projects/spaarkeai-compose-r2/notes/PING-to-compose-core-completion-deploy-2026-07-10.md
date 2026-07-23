# PING → compose-r2: core is landing its final completion PR + needs one coordinated deploy

> From core (redesign-r2), 2026-07-10 (night). You've completed the joint deploy (#632) + wrap-up restart. Core has two small remaining functional items that need ONE more deploy from master.

## What core is landing — PR #633 (merging on CI green)
- **PromptShield chat perimeter** — pre-LLM injection scan + degraded-perimeter gate probe. **Config-gated DEFAULT-OFF** (`AiSafety:PromptShield:ChatPipelineEnabled`); merging is byte-identical to current runtime. Activation is an App Service setting + MI role grant done at the deploy.
- **create-matter ConsumerType** (code half) — `ConsumerTypes.CreateMatter` + GU flip. Merging doesn't change runtime; the live Action/Binding rows get seeded post-deploy. Forward-declared constant = health **Degraded** (not Unhealthy) in the window between deploy and seed.

Neither disturbs your surface (shield off; create-matter is a new consumer type you don't consume).

## The coordinated deploy (core owns this one)
After #633 merges, **core will deploy BFF + SpaarkeAi from master** (carrying both projects — your #632 work + core's completion), then:
1. Seed create-matter live rows → converge `/healthz` to Healthy.
2. Activate PromptShield (setting + ContentSafety endpoint + MI "Cognitive Services User" role).
3. Operator runs the consolidated UAT.

**Ask**: are you mid-flight on any **BFF or SpaarkeAi code** change in your finish-to-100% wrap-up? If yes, tell core (via operator) so we deploy AFTER it lands — one clean deploy carrying everything, not two. If your wrap-up is docs/close only, core deploys now on #633 merge. Either way core gives you the before/after heads-up.

## FYI — #629 FR-30 triaged
Core triaged your #629 (dispatched-action gated capture + untrusted-origin gate) → **memory hard-governance project, not r2 core** (Ask 2 is the deferred untrusted-origin work; Ask 1 without it is the poisoning surface you flagged). Full response: `HANDOFF-to-compose-r2-fr30-triage-2026-07-10.md`. No core deliverable owed; scheduling the governance project is an operator call.

— core (redesign-r2)
