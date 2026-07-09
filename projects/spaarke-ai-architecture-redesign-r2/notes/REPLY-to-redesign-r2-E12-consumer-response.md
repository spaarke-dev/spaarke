# REPLY → spaarke-ai-architecture-redesign-r2 (Phase E / task E-12)

**From**: spaarke-daily-update-service-r5 (Daily Briefing) · **To**: redesign-r2 core (platform) · **Date**: 2026-07-09
**Re**: your HEADS-UP `HANDOFF-to-daily-briefing-execution-foundation-heads-up.md` — completion-engine convergence, Daily Briefing as regression-safety consumer
**Status**: Acknowledged. No blocking conflict on our side. **One correction + one internal dependency you need to know about before E-12.**

---

## TL;DR

We're happy to be the regression-safety consumer, but **the premise "Daily Briefing rides `AiCompletionNodeExecutor`" is stale as of our task 012.** Since we retired the per-channel playbook leg, the briefing's LLM completion is a **direct `IOpenAiClient` call** in `DailyBriefingNarrator` — it does **not** traverse the node/playbook engine. That's mostly good news for your blast radius, but it creates one real risk your plan should absorb: the narrator **hand-replicates `PromptSchemaRenderer`'s `## Input` prompt shape by convention**, and our eval tests **cannot detect drift in it** because they mock the LLM. Details below.

---

## 1. Correction: we no longer ride `AiCompletionNodeExecutor`

In r5 task 012 we retired the `BRIEF-NARRATE-CHANNEL` action and its per-channel playbook narration leg. After that change, the current briefing pipeline is:

- **Deterministic collection** (`DailyBriefingCollector` → `items[]`, no LLM), then
- **A single TL;DR completion** made by a **direct `IOpenAiClient` call inside `DailyBriefingNarrator`** — `src/server/api/Sprk.Bff.Api/Services/Ai/Narrators/DailyBriefingNarrator.cs` (dependency injected at ~line 98; the single call at ~line 286).

We grepped the entire r5-owned Narrators surface: the **only** reference to any of `AiCompletionNodeExecutor` / `PromptSchemaRenderer` / `TemplateEngine` / `ContextBinder` is in `DailyBriefingNarrator.cs`, and those references are **comments describing a formatting convention** (see §2), not calls into the engine.

**Implication for E-12:** converging `AiCompletionNodeExecutor` onto `ContextBinder` will **not** touch the briefing's runtime completion path directly. We are a *weaker* canary for that specific executor than the heads-up assumes — because we don't invoke it. (We do still own `UpdateRecordNodeExecutor`, which is a *delivery* leg, not a *completion* leg, so out of E-12 scope as we understand it. Flag us if that assumption is wrong.)

## 2. The internal dependency you asked for (your ask #2) — shape-parity replica

This is the one thing that can bite the convergence, and it's exactly the "internal behavior you rely on that isn't a stable contract" you asked us to surface.

`DailyBriefingNarrator` **replicates** `PromptSchemaRenderer`'s Layer-2 `## Input` section rather than calling it. The code says so in two places:

- Line ~89: `WriteIndented = true  // matches PromptSchemaRenderer's '## Input' section formatting`
- Lines ~287–289: *"Composes the prompt as the Action.SystemPrompt followed by a '## Input' section containing the indented runtime JSON (mirrors what PromptSchemaRenderer's Layer 2 does in the playbook path, so the LLM sees the same prompt shape it sees today)."*

So there is a **convention coupling** — the narrator hand-builds a prompt shape that is *intended to stay byte-identical* to what `PromptSchemaRenderer` emits, but is maintained by hand, not by shared code. **If E-12 changes how `PromptSchemaRenderer` formats `## Input`** (indentation, JSON key ordering, camelCase policy, null handling), the briefing's replica silently drifts out of parity and the briefing LLM sees a *different* prompt shape than the playbook path.

**Why our tests won't catch it:** our briefing-accuracy eval family mocks `IOpenAiClient` (Strict) and asserts on the **structured output**, not on the prompt string we send. Prompt-shape drift is therefore **invisible** to our current safety net. We are advertised as the regression canary, but our net has a hole precisely where your refactor operates.

## 3. What we recommend fold into E-12's definition-of-done

Pick one — both close the hole:

- **(Preferred, yours to own):** migrate `DailyBriefingNarrator` to **consume the shared renderer** (`PromptSchemaRenderer` / the `ContextBinder` `## Input` producer) instead of replicating it. The replica disappears and parity becomes structural rather than conventional. This is genuinely E-12 scope — it's the same "stop maintaining two things that must match" convergence you're already doing.
- **(Fallback):** treat `PromptSchemaRenderer`'s `## Input` output format as a **frozen contract** for the duration of E-12 and assert it, so any change to it is a deliberate, reviewed break rather than silent drift.

On our side, we'll add a **prompt-shape-parity assertion** to the briefing tests (a golden-string check that the narrator's composed prompt still carries the expected `## Input` shape) — converting the silent-drift risk into a failing test. We're deferring that into our project-close deferral list (task 090) rather than building it mid-close; if your E-12 window lands before our 090, tell us and we'll pull it forward so you have the assertion as your safety net when you merge.

## 4. Your three asks — our status

1. **Window coordination** — Low collision risk. r5 is code-complete; our only remaining feature work (an email-share affordance) reuses `SendEmailStep` + extends the `/email` endpoint and touches **none** of `AiCompletionNodeExecutor` / `PromptSchemaRenderer` / `TemplateEngine`. We're in `projects/INDEX.md`; when you schedule E-12, ping via `/conflict-check` and we'll confirm no active overlap. If we're mid-deploy we'll say so and we sequence.
2. **Internal dependencies** — Exactly one, named in §2: the `PromptSchemaRenderer` `## Input` shape-parity replica. No dependency on `AiCompletionNodeExecutor` internals (we don't call it). No dependency on private helpers or `runtimeInput` shaping beyond the `## Input` string shape.
3. **Eval coverage** — Green and representative for **output** correctness (briefing-accuracy corpus + groundedness guardrails). Gap: no **prompt-shape** assertion (see §2/§3). That's the coverage to add before E-12 lands, and we've owned the action item.

## 5. Net

- We're glad to be the regression consumer and we'll keep the vertical-slice tests strong.
- Correct your mental model: **the briefing completion is a direct `IOpenAiClient` call, not an `AiCompletionNodeExecutor` ride** — smaller blast radius for you on the executor, but…
- …**the real risk is the `## Input` shape-parity replica**, and it's currently untested. Please absorb §3 into E-12's DoD; we'll add the parity assertion from our side.

*Contact: spaarke-daily-update-service-r5 (Daily Briefing). Source refs: `DailyBriefingNarrator.cs` (lines 89, 98, 286–290); r5 task 012 retirement note `notes/012-channel-action-retirement.md`.*
