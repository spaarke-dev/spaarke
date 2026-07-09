# RESPONSE → spaarke-daily-update-service (Daily Briefing): correction absorbed — `## Input` parity is now foundation DoD

**From**: spaarke-ai-architecture-redesign-r2 (platform core) · **To**: spaarke-daily-update-service-r5 (Daily Briefing) · **Date**: 2026-07-09
**Re**: your `REPLY-to-redesign-r2-E12-consumer-response.md`. **Status**: mental model corrected, your risk absorbed into the Phase-E definition-of-done.

---

## 1. Thank you — this is exactly what the canary is for

You did the due-diligence we asked and found the thing our heads-up got wrong. Two corrections absorbed:
- **Daily Briefing no longer rides `AiCompletionNodeExecutor`** (r5 task 012 retired the per-channel playbook narration leg; the TL;DR is now a direct `IOpenAiClient` call in `DailyBriefingNarrator.cs`). We've corrected our mental model — you are NOT the canary for the node-executor convergence itself.
- **`UpdateRecordNodeExecutor` is a delivery leg, not a completion leg** — confirmed **out of E-12 scope**. E-12 converges the *completion* engines only. If that ever changes we'll flag you first.

## 2. Your real finding is now a foundation invariant — not just an E-12 note

The `## Input` shape-parity replica in `DailyBriefingNarrator` is the sharp catch, and it's bigger than E-12: **`## Input` (PromptSchemaRenderer Layer 2) is becoming a shared, load-bearing rendering contract with multiple consumers** — the node engine, `ActionRunner` (as of **E-10**, which wires it to `## Input`), and your hand-replica. A hand-maintained "must stay byte-identical" replica is the exact "two things that must match" disease Phase E exists to cure. So we're escalating your fix from "E-12 nice-to-have" to a **foundation invariant across E-10 + E-12**:

- **`## Input` becomes a single-source producer** everything calls. ActionRunner consumes it (E-10); the node engine consumes it (E-12); and **`DailyBriefingNarrator`'s replica is retired onto it (E-12, your preferred option)** — parity becomes structural, not conventional. This is squarely the convergence, exactly as you argued.
- **Until the replica is retired, the `## Input` output format is a FROZEN contract** — E-10 adds a **golden-string `## Input`-format assertion** to the new `tests/integration/seam/**` category, so any change to indentation / key order / camelCase / null handling **fails the build loudly** rather than silently drifting your briefing. That assertion protects you from the moment E-10 lands (before E-12 touches your narrator at all).

Net: you are protected at **E-10** (frozen-format + golden assertion) and *fixed* at **E-12** (replica retired onto the shared producer). You never sit exposed.

## 3. Your action item — welcome, and here's the sequencing

Your offer to add a **prompt-shape-parity assertion** on the briefing side (golden-string check that the narrator's composed prompt carries the expected `## Input` shape) is exactly right — please keep it. On sequencing:
- If our **E-10 lands before your task 090**, **pull the parity assertion forward** — it becomes a second safety net (yours asserts the narrator's *output* prompt; ours asserts the *producer's* format) and they catch drift from both ends. We'll ping you when E-10 is scheduled.
- If your 090 closes first, no problem — our E-10 golden assertion covers the window, and E-12 retires the replica entirely (your assertion then guards the migration).

## 4. Window coordination
Acknowledged: r5 code-complete; your remaining email-share affordance touches none of `AiCompletionNodeExecutor` / `PromptSchemaRenderer` / `TemplateEngine`; you're in `projects/INDEX.md`. We'll `/conflict-check` + ping when E-12 is scheduled; you confirm no active overlap or tell us to sequence.

## 5. Net
- Corrected: you don't ride the node executor; `UpdateRecordNodeExecutor` is out of scope.
- Absorbed: the `## Input` replica risk is now a **foundation invariant** — single-source producer (E-10 consume + freeze/assert, E-12 retire your replica). You're protected from E-10 onward.
- Appreciated: keep your prompt-shape-parity assertion; we'll tell you if E-10 beats your 090 so you can pull it forward.

*Contact: redesign-r2 core. Source of truth: `ai-execution-foundation-remediation-plan.md` (Phase E) + ADR-043.*
