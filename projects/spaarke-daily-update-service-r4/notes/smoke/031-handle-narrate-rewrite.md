# Smoke: 031 — HandleNarrate rewrite to playbook dispatch (Path A.5)

> **Task**: 031-replace-handle-narrate-with-playbook-dispatch.poml
> **Date**: 2026-06-26
> **Environment**: spaarkedev1 (Dataverse) + Sprk.Bff.Api Release build
> **Path decision**: A.5 — `IConsumerRoutingService` + existing `IInvokePlaybookAi` facade
> **Spec refs**: FR-12 (lines 159–164), AC-12a / AC-12b / AC-12c

---

## sprk_playbookconsumer row deployed

| Field | Value |
|---|---|
| `sprk_playbookconsumerid` | `b4503359-1771-f111-ab0e-7ced8ddc4a05` |
| `sprk_name` | `Daily Briefing Narrate (default)` |
| `sprk_consumertype` | `daily-briefing-narrate` |
| `sprk_consumercode` | `default` |
| `sprk_environment` | `*` (matches all environments) |
| `sprk_priority` | `500` (matches existing 6 consumers) |
| `sprk_enabled` | `true` |
| `sprk_playbook` (lookup) | `7b5a6ed3-0271-f111-ab0e-000d3a13a4cd` → DAILY-BRIEFING-NARRATE (deployed by task 011) |
| `statecode` / `statuscode` | `0` (Active) / `1` (Active) |

**Deployment method**: MCP `create_record` (no existing row found via `read_query` for `sprk_consumertype = 'daily-briefing-narrate'` — idempotency confirmed before create).

**Verification query (post-deploy)**:

```sql
SELECT sprk_playbookconsumerid, sprk_consumertype, sprk_consumercode,
       sprk_environment, sprk_priority, sprk_enabled, statecode, sprk_playbook
FROM sprk_playbookconsumer
WHERE sprk_consumertype = 'daily-briefing-narrate'
```

Result: 1 row, `sprk_playbook = '7b5a6ed3-0271-f111-ab0e-000d3a13a4cd'`, `statecode = 0 (Active)`. Verified.

---

## HandleNarrate signature change (before → after)

### BEFORE (R3 / task 070 repair)
```csharp
private static async Task<IResult> HandleNarrate(
    DailyBriefingNarrateRequest request,
    ILoggerFactory loggerFactory,
    HttpContext httpContext,
    CancellationToken cancellationToken,
    IBriefingAi? briefingAi = null)
```

Body (~280 lines):
- 503 if `briefingAi is null`
- Empty-payload short-circuit (preserved)
- `GetTldrAsync` → inline TL;DR prompt → LLM call
- `request.Channels.Select(GetChannelNarrationAsync)` → inline per-channel prompt → LLM calls
- `Task.WhenAll` parallel fan-out
- Aggregate into `DailyBriefingNarrateResponse`

### AFTER (R4 task 031 / FR-12 Path A.5)
```csharp
private static async Task<IResult> HandleNarrate(
    DailyBriefingNarrateRequest request,
    ILoggerFactory loggerFactory,
    IConsumerRoutingService routing,
    IInvokePlaybookAi invokePlaybookAi,
    HttpContext httpContext,
    CancellationToken cancellationToken)
```

Body (~120 lines):
- Empty-payload short-circuit (preserved BEFORE playbook dispatch — never burn an LLM call for nothing-to-narrate)
- `routing.ResolveAsync(ConsumerTypes.DailyBriefingNarrate, "default", null, null, ct)` → playbook GUID
- 503 if routing returns null (analogous to the prior briefingAi-null 503)
- Serialize request as `briefingPayload` JSON-string parameter (+ 4 scalar convenience keys for template conditions)
- `invokePlaybookAi.InvokePlaybookAsync(playbookId, parameters, context, ct)` → `PlaybookInvocationResult`
- `ProjectPlaybookResultToNarrateResponse` → maps `StructuredData → DailyBriefingNarrateResponse` (TL;DR + ChannelNarratives + GeneratedAtUtc)
- 503 (`AiUnavailable`) if `playbookResult.Success == false`
- Fallback: if `StructuredData` missing but `TextContent` present → Summary-only TL;DR (graceful degradation per FR-16)
- `FeatureDisabledException` (NullInvokePlaybookAi P3 fail-fast) → 503
- `OpenAiCircuitBrokenException` → `AiUnavailable`

**Diff**: -1 parameter (`IBriefingAi? briefingAi`), +2 parameters (`IConsumerRoutingService routing`, `IInvokePlaybookAi invokePlaybookAi`). `IBriefingAi` is still used by the sibling `Summarize` endpoint (not affected by R4 / FR-12).

---

## AC-12a verification — no inline prompt strings remain

### Source grep (FR-12 / AC-12a binding)

```bash
grep -nE "concise executive assistant|Summarize the user|JSON object|TL;DR|narrative bullet|Do NOT invent IDs" \
  src/server/api/Sprk.Bff.Api/Api/Ai/DailyBriefingEndpoints.cs
```

Match in `Summarize` (lines 147–149) — UNCHANGED, the `/summarize` endpoint still
hosts the prioritized briefing summary prompt (NOT in scope for FR-12; FR-12 is
the `/narrate` endpoint only).

**Zero matches in `HandleNarrate`** or its helpers. The prior helpers
(`BuildNarrateTldrPrompt`, `BuildChannelNarrationPrompt`, `ParseTldrResponse`,
`ParseChannelBullets`, `BuildAllowedRegardingIdSet`, `ValidateBulletPrimaryEntityIds`,
`GetTldrAsync`, `GetChannelNarrationAsync`, the `TldrJsonPayload` DTO) are
DELETED. The test
`DailyBriefingEndpoints_Source_Has_No_Inline_LLM_Prompt_Helpers` (added in this
task) enforces this via reflection — any future regression that re-adds these
helpers will fail the test.

### Note on the sibling `Summarize` endpoint

`Summarize` (POST `/api/ai/daily-briefing/summarize`) is OUT OF SCOPE for FR-12.
It's a different endpoint serving the prioritized-briefing summary use case (R1).
Its prompt construction is preserved unchanged — its scope is governed by R1, not
this project. Future cleanup can convert it to a playbook-dispatched endpoint if
a similar refactor is requested.

---

## Build + Test + Publish + CVE results

| Check | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/ -c Release` | ✅ Build succeeded, 17 pre-existing warnings, 0 errors |
| `dotnet test --filter "FullyQualifiedName~DailyBriefingEndpoints"` | ✅ 10/10 passed |
| `dotnet test tests/unit/Sprk.Bff.Api.Tests/` (full suite) | ✅ 7873 passed, 0 failed, 134 skipped (matches baseline) |
| `dotnet publish` (compressed) | ✅ 46.30 MB — delta -0.01 MB vs PR 3 baseline (see `notes/debug/031-publish-size.md`) |
| `dotnet list package --vulnerable --include-transitive` | ✅ No new HIGH CVE (1 pre-existing: `Microsoft.Kiota.Abstractions 1.21.2` HIGH — present in PR 3 baseline) |

---

## DI / placement justification (CLAUDE.md §10, §11 binding)

- **§10 bullet 3** — Uses `Services/Ai/PublicContracts/` facades (`IConsumerRoutingService` + `IInvokePlaybookAi`) exclusively. NO direct injection of `IOpenAiClient`, `IPlaybookService`, `IPlaybookOrchestrationService`, or `IAnalysisOrchestrationService` into the endpoint. Maintains ADR-013 facade boundary.
- **§10 bullet 4** — Publish-size delta -0.01 MB (well under +5 MB escalation threshold).
- **§10 bullet 5** — No new HIGH CVE (pre-existing Kiota HIGH unchanged).
- **§10 bullet 6** — Tests added (10 new + 0 obsolete). `DailyBriefingEndpointsTests.cs` rewritten to mirror the new dispatch path. Includes the reflection-based AC-12a sanity check.
- **§11 Component justification (Default to Reuse)**: existing closest = inline LLM prompt body in `HandleNarrate`; extension = Yes (REPLACE inline implementation with thin dispatch wrapper); cost-of-doing-nothing = R3 UAT hallucination defect persists because prompts cannot be iterated without recompile + redeploy. No new component introduced — only adds a 7th caller of the existing `IConsumerRoutingService` + `IInvokePlaybookAi` wiring (both already registered for the 6 prior consumers per chat-routing-redesign-r1 + R6 Pillar 3).

---

## Acceptance criteria — task 031

| Criterion | Result |
|---|---|
| AC-12a — HandleNarrate contains no inline LLM prompt strings; dispatch wrapper only | ✅ PASS (grep verified above; reflection-test enforced) |
| AC-12b — `/narrate` response JSON shape unchanged from R3 (top-level: tldr, channelNarratives, generatedAtUtc) | ✅ PASS (`HandleNarrate_Projects_StructuredData_Into_DailyBriefingNarrateResponse` test verifies; same DTO records retained) |
| `dotnet build src/server/api/Sprk.Bff.Api/` succeeds with no new warnings | ✅ PASS (17 warnings, all pre-existing) |
| Publish-size delta within CLAUDE.md §10 thresholds; ≤60 MB compressed ceiling preserved | ✅ PASS (46.30 MB; -0.01 MB delta) |

---

## Caveats / open items for downstream tasks

- **Task 032 (next)** — Backward-compat verification of `/narrate` response shape against the widget parser (`useBriefingNarration.ts`). This task's unit test covers the BFF side; task 032 should add the integration test traversing the wire format.
- **Task 010 playbook node-graph payload binding** — The playbook expects `{{json start}}` + `{{start.*}}` template references. This task serializes the request as a single `briefingPayload` JSON-string parameter. If the playbook's node graph as authored in task 010 (and deployed in task 011) expects a different parameter key OR expects multiple keys for the structured arrays, the playbook's `inputBinding` blocks may need adjustment — verify in task 032 smoke test. The 4 scalar convenience parameters (`totalNotificationCount`, `categoryCount`, `priorityItemCount`, `channelCount`) are passed independently to enable template-condition checks without re-parsing the JSON blob.
- **Sibling endpoint `Summarize`** — Unchanged; out of scope for FR-12. Future cleanup task can apply the same Path A.5 pattern if owner requests.
