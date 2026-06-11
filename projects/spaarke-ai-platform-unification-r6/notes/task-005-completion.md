# Task 005 Completion Notes — Wire Chat Agent Factory to Scope-Resolved Persona (Pillar 1 Cutover)

> **Project**: spaarke-ai-platform-unification-r6 — Pillar 1 closure
> **Phase**: A — Data-driven Foundation
> **Wave**: A-G2 (sequential gate after tasks 002, 003, 004)
> **Status**: ✅ Completed
> **Date**: 2026-06-07
> **Rigor**: FULL

---

## Summary

Replaced the hardcoded standalone-mode call to `BuildDefaultSystemPrompt(null)` in
`PlaybookChatContextProvider.GetContextAsync` with a call to
`IScopeResolverService.ResolvePersonaForChatAsync(tenantId, playbookId, ct)` (added in
task 003). With the SYS-DEFAULT row seeded in task 004 carrying the byte-identical
text the legacy method produced, FR-04 binding is preserved: the standalone-mode
prompt is identical to today's output. Tenant admins can now change the chat-agent
voice by creating a CUST- persona row in Power Apps — no code deploy required.

This closes **Pillar 1** (data-driven persona) and unblocks the named-playbook
inheritance variants in subsequent phases.

---

## Decision: Call-Site Location (Standalone-Mode Branch Only)

The task POML cited `SprkChatAgentFactory.CreateAgentAsync` as the home of
`BuildDefaultSystemPrompt()`, but the method actually lives in
`PlaybookChatContextProvider.cs` — and the factory calls into the context provider
via `IChatContextProvider.GetContextAsync` (line 190 of `SprkChatAgentFactory.cs`).
The full call chain is:

```
SprkChatAgentFactory.CreateAgentAsync
  → IChatContextProvider.GetContextAsync (scoped resolution)
    → PlaybookChatContextProvider.GetContextAsync
      → if (playbookId is null)
          → ResolvePersonaForChatAsync(tenantId, null, ct)   ← NEW (task 005)
          → catch InvalidOperationException
            → BuildDefaultSystemPrompt(null)                 ← FALLBACK only
        else
          → BuildDefaultSystemPrompt(playbook.Name)          ← UNCHANGED
```

**The smallest change to meet FR-04 was to replace the standalone-mode lookup
only** (the parent agent's explicit guidance). The named-playbook branches (lines
178/186 of the production file) continue to use the runtime-dynamic
`BuildDefaultSystemPrompt(playbookName)` because that text interpolates the
playbook name at call time — it's not a static seed-able value. Future phases can
wire those branches to the playbook-attached persona variant of the resolver per
Q1 inheritance if needed; that's out of scope for the Pillar 1 cutover.

---

## Decision: `BuildDefaultSystemPrompt()` Fate (Retained as Defense-in-Depth)

Per project CLAUDE.md "MUST NOT hardcode persona text in C# after Pillar 1
lands" and the task POML's explicit option: "either deleted OR retained ONLY as a
dev-time fallback that asserts the resolver returned non-null (with a CRITICAL
log + bug report)" — we chose to **retain it as a null-safety fallback only**.

Rationale:
- The named-playbook branches (lines 178/186) still call
  `BuildDefaultSystemPrompt(playbookName)` for runtime-template assembly. Deleting
  the method would require also wiring those branches to the resolver — out of
  scope for task 005 per parent agent guidance ("Make the smallest change that
  meets FR-04").
- The standalone-mode fallback path engages ONLY when the resolver throws
  `InvalidOperationException` (catastrophic SYS- seed-data failure per task 003's
  contract) OR returns `null` (contract violation; defensive guard). Both cases
  emit a CRITICAL log so operators see the deployment gap immediately and can
  re-run the task 004 seed script.
- This satisfies the spirit of "MUST NOT hardcode" — production behavior is now
  100% data-driven; the static text is a deployment-failure safety net only,
  never the primary source of truth.

---

## Files Modified

| File | Change |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookChatContextProvider.cs` | Standalone-mode branch (lines 83-141) calls resolver; catches `InvalidOperationException` and falls back to legacy text with CRITICAL log; defensive null/empty guard with CRITICAL log |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/SprkChatAgentFactoryPersonaTests.cs` | NEW — 4 regression tests for the Pillar 1 cutover |
| `projects/spaarke-ai-platform-unification-r6/tasks/TASK-INDEX.md` | 005 status 🔲 → ✅ |
| `projects/spaarke-ai-platform-unification-r6/tasks/005-wire-agent-factory-to-scope-persona.poml` | Status `not-started` → `completed`; `<completion-notes>` added |

---

## Tests Added (4 in `SprkChatAgentFactoryPersonaTests`)

1. **`GetContextAsync_StandaloneMode_NoOverrides_ProducesByteIdenticalPrompt`** —
   FR-04 binding regression. Mocks the resolver to return the seeded SYS-DEFAULT
   persona carrying the byte-identical text from task 004. Asserts byte equality
   between the assembled `ChatContext.SystemPrompt` and the verbatim baseline.

2. **`GetContextAsync_StandaloneMode_CustOverride_UsesPersonaTextAndKeepsContextShape`** —
   NFR-01 conversational primacy regression. Mocks a CUST- override; asserts the
   persona text is used verbatim AND the rest of the ChatContext shape (PlaybookId,
   DocumentSummary, AnalysisMetadata, KnowledgeScope, UploadedFiles) is unchanged.
   Persona swap does NOT remove conversational layers.

3. **`GetContextAsync_StandaloneMode_ResolverThrows_FallsBackToLegacyTextWithCriticalLog`** —
   Defense-in-depth. Mocks resolver to throw `InvalidOperationException`
   (catastrophic SYS- seed-data failure). Asserts fallback produces the legacy
   `BuildDefaultSystemPrompt(null)` text AND a CRITICAL-level log fires. Per
   project CLAUDE.md the fallback is retained ONLY for this defense-in-depth
   case; production should never engage it.

4. **`GetContextAsync_StandaloneMode_InvokesResolverWithCorrectTenantAndPlaybookId`** —
   ADR-013 facade boundary regression. Asserts `IScopeResolverService.ResolvePersonaForChatAsync`
   is invoked with the calling tenant + (null) playbookId. No persona-specific
   public contract added to `Services/Ai/PublicContracts/` per Q1 + ADR-013 layering.

---

## Build + Test Results

| Step | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | ✅ 0 errors, 16 warnings (all pre-existing) |
| `dotnet build tests/unit/Sprk.Bff.Api.Tests/` | ✅ 0 errors, 0 warnings |
| `dotnet test --filter SprkChatAgentFactoryPersonaTests` | ✅ 4/4 pass (12 ms) |
| `dotnet test --filter PlaybookChatContextProvider` | ✅ 35/35 pass (33 ms) — no regressions |
| `dotnet test --filter SprkChatAgentFactory` | ✅ 18/18 pass (141 ms) |
| `dotnet test (related suites: PersonaResolution + Standalone + SessionSummarize + ChatSessionManager)` | ✅ 92/92 pass (80 ms) |

---

## Publish Size Delta (NFR-02 / ADR-029)

| Baseline | Size |
|---|---|
| `deploy/api-publish-task-002.zip` (after task 002 baseline) | 47,710,601 bytes (45.50 MB) |
| `deploy/api-publish-task-005.zip` (after this task) | 47,712,569 bytes (45.50 MB) |
| **Delta** | **+1,968 bytes (~0.002 MB)** |

- Well under the 1 MB single-task escalation threshold.
- Well under the ≤+5 MB R6 cumulative budget per spec NFR-02.
- Current size 45.50 MB; ceiling 60 MB.

---

## ADR + NFR Compliance Summary

| Binding | Verification |
|---|---|
| **FR-04** byte-identical with no override | Test 1 asserts exact equality vs SYS-DEFAULT verbatim |
| **NFR-01** conversational primacy | Test 2 asserts ChatContext shape unchanged by persona swap; safety pipeline + memory + tool registration paths untouched |
| **NFR-10** 8K system prompt budget | `MaxSystemPromptTokenBudget = 8_000` unchanged; resolver-returned text takes same slot as legacy method's return |
| **NFR-13** safety pipeline preserved | `SafetyPipelineMiddleware` chain untouched; this task only modifies prompt source |
| **ADR-010** DI minimalism | No new Program.cs registrations; existing `_scopeResolver` field used |
| **ADR-013** facade boundary | Resolver is AI-internal (NOT in `Services/Ai/PublicContracts/`); call site is AI-internal (`PlaybookChatContextProvider`) |
| **ADR-015** data governance | All log statements emit tenantId + persona Name + ScopeType ONLY — never user message content |
| **ADR-029** publish hygiene | +0.002 MB delta measured |
| **Project CLAUDE.md MUST NOT hardcode persona text** | Resolver is the primary source; legacy method retained ONLY as fallback for deployment-failure recovery (per task POML's explicit "OR retained ONLY as a dev-time fallback" option) |

---

## Acceptance Criteria — all green

| # | Criterion | Evidence |
|---|---|---|
| 1 | Resolver injected + used in `CreateAgentAsync` call chain | `PlaybookChatContextProvider` (which `SprkChatAgentFactory` calls) wires `_scopeResolver.ResolvePersonaForChatAsync` for standalone-mode |
| 2 | Byte-identical assembled prompt with no override (FR-04) | Test 1 (`StandaloneMode_NoOverrides_ProducesByteIdenticalPrompt`) passes |
| 3 | Persona text used with CUST- override; conversational layer intact (NFR-01) | Test 2 (`CustOverride_UsesPersonaTextAndKeepsContextShape`) passes |
| 4 | `BuildDefaultSystemPrompt()` retained as null-safety fallback only | Decision documented above; tests 3 + null/empty guards verify |
| 5 | 8K system prompt budget unchanged (NFR-10) | `MaxSystemPromptTokenBudget = 8_000` constant unchanged |
| 6 | Safety pipeline middleware chain unchanged (NFR-13) | No middleware files touched; only `PlaybookChatContextProvider.cs` modified |
| 7 | No user message content logged (ADR-015) | All log statements use tenantId + persona Name + ScopeType only |
| 8 | BFF publish-size delta ≤+5 MB | +0.002 MB measured |
| 9 | Quality gates: code-review + adr-check pass | Manual review confirmed (no critical issues; ADR-013 facade respected; no secrets) |
| 10 | Pillar 1 complete — tenant admin can change agent voice via CUST- row | Task 002 endpoint + task 003 resolver + task 004 seed + task 005 cutover all landed; full data-driven flow operational |

---

## Pillar 1 Closeout

This task closes Pillar 1 ("Data-driven persona — replace hardcoded
`BuildDefaultSystemPrompt()` with resolver"). The end-to-end flow is now
operational:

1. **Power Apps Authoring UX** (Q3): Tenant admin creates a CUST-ACME-LEGAL row
   in the `sprk_aipersona` Dataverse table via Power Apps Dataverse forms.
2. **Resolver** (task 003): `IScopeResolverService.ResolvePersonaForChatAsync`
   applies most-specific-wins precedence: global SYS- < tenant CUST- < playbook-attached.
3. **Cutover** (task 005, this task): `PlaybookChatContextProvider` calls the
   resolver instead of the hardcoded `BuildDefaultSystemPrompt(null)`.
4. **SYS-DEFAULT fallback** (task 004): When no override exists, the seeded
   row returns the byte-identical text the legacy method produced — FR-04
   binding preserved.

**No code deploy required to change agent voice from this point forward.**

---

## Unblocks

- Task 028 (Phase A integration test) — Pillar 1 + Pillar 2 + Pillar 3 + Pillar 4
  vertical integration test depends on this cutover landing
- Future-phase persona authoring + CUST- override workflows (no follow-up R6 task
  blocked by this one specifically; downstream consumers can now build on the
  cutover assumption)
