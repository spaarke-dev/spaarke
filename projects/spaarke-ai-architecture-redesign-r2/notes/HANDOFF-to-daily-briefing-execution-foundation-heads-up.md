# HEADS-UP → spaarke-daily-update-service (Daily Briefing): AI completion-engine convergence is coming — you're the regression-safety consumer

**From**: spaarke-ai-architecture-redesign-r2 (platform core) · **To**: spaarke-daily-update-service-r5 (Daily Briefing) · **Date**: 2026-07-09
**Status**: Advance notice + a small ask. **No action required today** — this is so a future change doesn't surprise you. Full context: core's `ai-execution-foundation-remediation-plan.md` (**Phase E**).

---

## Why you're getting this

Core is fixing a foundational gap in the AI execution layer (a platform assessment from compose-r2 + core verification found the "canonical" dispatch engine realizes only a narrow slice of the ADR-039 catalog contract). One move in the fix touches an engine **you depend on**.

There are two LLM-completion engines in the BFF that do the same job (resolve input → prompt → LLM → structured output):
- **`AiCompletionNodeExecutor`** (the playbook/node engine) — **this is the one Daily Briefing rides.** It already has the good input-resolution abstraction (`inputBinding` → `runtimeInput` → `PromptSchemaRenderer` `## Input`).
- **`ActionRunner`** (the newer "canonical" dispatch engine) — input-poor, takes only document text.

Core is **converging these two onto one shared input-resolution seam (`ContextBinder`)** so the canonical engine stops being the weaker one. Your engine (`AiCompletionNodeExecutor`) will be **migrated behind its existing interface** to use the shared Binder/renderer seam (Phase E task **E-12**).

## The key point: this must NOT change your behavior

You are the **regression-safety consumer** for the convergence. The migration is a refactor to a shared seam — Daily Briefing's playbook execution, briefing accuracy, and grounded output must be **byte-for-byte unchanged**. Your existing tests (the briefing-accuracy eval family + groundedness guardrails you shipped in r5) are the safety net that proves it.

## What we ask of you

1. **Awareness + window coordination** — when E-12 is scheduled, core will coordinate via `projects/INDEX.md` + `/conflict-check`. If Daily Briefing has active work touching `AiCompletionNodeExecutor`, `PromptSchemaRenderer`, `TemplateEngine`, or the playbook node path at that time, flag it so we sequence rather than collide.
2. **Interface vs. internals** — tell us if you depend on any **internal** behavior of `AiCompletionNodeExecutor` / `PromptSchemaRenderer` (private helpers, ordering side-effects, undocumented `runtimeInput` shaping) rather than their public contract. Anything you rely on that isn't a stable contract is where a refactor could bite — we want that list so we protect it or make it contractual first.
3. **Keep your eval coverage green + representative** — your briefing-accuracy corpus + groundedness guardrail tests are exactly the vertical-slice safety net this whole effort is about. If there are briefing behaviors they *don't* cover, that's the gap to close before E-12 lands. (Core is adding a platform `tests/integration/seam/**` KEEP category as a definition-of-done; your consumer-side coverage complements it.)

## What you can expect from core

- **No contract change to the playbook/node path** — E-12 is a seam-sharing refactor, not a redesign of playbook execution. If any public behavior *would* change, we escalate to you first, we don't ship it.
- **Advance scheduling** of the E-12 window (not a surprise merge).
- The convergence makes the platform's input model *richer* for you long-term (envelope slices: entity/matter context + prior-ledger outputs + durable memory as input), but that's opt-in and additive — nothing you must adopt.

## TL;DR
Core is merging the two completion engines onto one input seam. Yours is the reference engine that must not regress. No action now — just (1) coordinate the window when scheduled, (2) hand us any internal (non-contract) dependencies you have on the node engine, (3) keep your briefing-accuracy/groundedness tests strong. We treat Daily Briefing as the thing that proves the convergence is safe.

*Contact: redesign-r2 core. Source of truth: `ai-execution-foundation-remediation-plan.md` (Phase E, task E-12).*
