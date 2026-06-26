# Design: DAILY-BRIEFING-NARRATE Playbook Node Graph

> **Authored**: 2026-06-25 (task 010)
> **Status**: Design — implemented in `projects/spaarke-daily-update-service/notes/playbooks/daily-briefing-narrate.json`; deployed by task 011
> **Spec References**: FR-4 (line 123), FR-12 (line 159), FR-13 (line 166), FR-14 (line 170)
> **Related**: `notes/decisions/030-dispatch-path.md` (dispatch wrapper), ADR-037 (multinode composition), `.claude/skills/jps-playbook-design/SKILL.md`

---

## 1. Node Graph

```
                                +-----------------+
                                |     Start        |
                                | (payload ingest) |
                                +--------+--------+
                                         |
                                         v
                                +-----------------+
                                |  LoadKnowledge   |
                                | (channel reg —   |
                                |  placeholder R5) |
                                +---+---------+---+
                                    |         |
                                    |         |
                          +---------+         +---------+
                          v                             v
                +------------------+         +-------------------------+
                |   GenerateTldr   |         | GenerateChannelNarratives|
                |  Skill — TLDR    |         | Skill — CHANNEL (fan-out)|
                |   ActionType 0   |         |   ActionType 0 per chan  |
                +---------+--------+         +-------------+-----------+
                          |                                |
                          +-------------+------------------+
                                        |
                                        v
                            +---------------------+
                            | ValidateEntityNames |
                            |  Tool — ActionType  |
                            |  141 (scrub allow-  |
                            |   list, log warnings)|
                            +----------+----------+
                                       |
                                       v
                            +---------------------+
                            |   ReturnResponse    |
                            | (bind to            |
                            |  DailyBriefing-     |
                            |  NarrateResponse)   |
                            +---------------------+
```

**6 nodes / 6 edges**. The fan-out + merge happens at LoadKnowledge → [GenerateTldr ‖ GenerateChannelNarratives] → ValidateEntityNames.

---

## 2. Rationale

### 2.1 Why parallel TL;DR + channels (not sequential)

The two narration Skills have **no inter-Skill data dependency**:

- `BRIEF-NARRATE-TLDR` reads the whole payload (categories + priorityItems + channels + totalNotificationCount) and emits a summary across all of it
- `BRIEF-NARRATE-CHANNEL` reads ONE channel at a time and emits per-channel bullets

They share the same root input (the briefing payload) but produce orthogonal outputs. Running them sequentially adds wall-clock latency without buying determinism. Running them in parallel matches the orchestrator's stated capability and the response shape (`DailyBriefingNarrateResponse { Tldr, ChannelNarratives, GeneratedAtUtc }`) which presents them as independent siblings.

### 2.2 Why ValidateEntityNames after BOTH

The scrubber is a deterministic in-process Tool (ActionType 141 — `EntityNameValidatorNodeExecutor`). Its purpose is post-LLM defense-in-depth per FR-14: every LLM-emitted proper-noun span gets checked against an allow-list built from the input payload, and sentences containing un-allowed spans are removed. Both narration Skills can hallucinate independently (the TL;DR can fabricate firm names just as easily as a channel bullet can), so the scrubber must consume the union of their outputs.

It MUST run AFTER both branches complete — running it on the TL;DR alone leaves channel bullets unscrubbed; running it twice (once per branch) doubles the cost without improving correctness. The merge happens at the ValidateEntityNames node entry.

### 2.3 Why NOT DeliverComposite (ADR-037 NodeType 100_000_004)

ADR-037 introduces `DeliverComposite` for **section-name-keyed SSE streaming** to FE widgets. The DAILY-BRIEFING-NARRATE response shape is:

- A fixed-arity JSON DTO (`DailyBriefingNarrateResponse { Tldr, ChannelNarratives[], GeneratedAtUtc }`)
- Returned non-streamed via `TypedResults.Ok(...)` at the /narrate endpoint
- Consumed by the widget's existing `useBriefingNarration.ts` hook (which parses JSON synchronously)

Per ADR-037 §"When Composite vs Single-Action Output", `DeliverComposite` applies to **workspace** rendering with per-section streamed updates. The daily-briefing widget does NOT use SSE — it fetches /narrate once via `authenticatedFetch` and renders the parsed payload. So `DeliverComposite` would add complexity without benefit. The playbook uses standard parallel fan-out into a Tool node + ReturnResponse, which the new payload-execution method on `AnalysisOrchestrationService` (task 031) projects into the response DTO.

### 2.4 Why no LLM streaming

Two reasons:
1. **Response contract preservation (AC-12b)** — the existing widget parses a single non-streamed JSON response. Switching to SSE would break backward compat with `useBriefingNarration.ts` and require widget rewrites — out of R4 scope.
2. **Deterministic scrubbing requires whole-text input** — the EntityNameValidator Tool sentence-splits its `candidateText` input. A streamed Skill output cannot be sentence-split until terminal punctuation is observed, which negates the streaming UX benefit. Buffering the stream until completion is functionally equivalent to non-streaming, with extra complexity.

For R5 (when AI Search knowledge bindings are added), the playbook may add SSE side-channels for status events (per ADR-033 Path 3) without changing the terminal response shape — but R4 stays non-streamed.

---

## 3. Payload Contract Mapping

The Start node ingests `DailyBriefingNarrateRequest` (per `DailyBriefingEndpoints.cs` line 189 input + R4 W1 enrichments per FR-6):

```typescript
{
  categories:   Array<{ category: string, count: number, items: NotificationItem[] }>,
  priorityItems: NotificationItem[],
  channels:     Array<{ channel: string, items: NotificationItem[] }>,
  totalNotificationCount: number,
  // NotificationItem (enriched per FR-6):
  // { regardingName, regardingEntityType, regardingId,
  //   viaMatter?: { id, name, memberships[] },
  //   source?:    { entityType, id, modifiedOn, owningUser } }
}
```

### Action input bindings (per node)

| Node | Action input | Source field | Notes |
|---|---|---|---|
| GenerateTldr | `payload` (single JSON string, maxLength 50000) | `{{json start}}` (the entire ingested payload) | Action prompt instructs LLM to use ONLY entity names present in this payload |
| GenerateChannelNarratives | `payload` (per-iteration) | `{{json channel}}` (one element of `start.channels`) | Fan-out: one Skill invocation per channel |
| ValidateEntityNames | `candidateText` | Concatenation of `tldrResult.summary`, `tldrResult.keyTakeaways`, `tldrResult.topAction`, plus every `channelNarrationResults[*].narrative[*]` joined with `\n\n` | Whole-text input enables sentence-level scrub |
| ValidateEntityNames | `allowList` | Deduplicated union of every `regardingName` + every `viaMatter.name` + every `source.owningUser` present in `priorityItems`, `categories[].items`, `channels[].items` | Empty array allowed (scrub semantics: remove every proper-noun-bearing sentence) |

### Return value mapping (ReturnResponse → `DailyBriefingNarrateResponse`)

| Response field | Source binding |
|---|---|
| `Tldr` | `{{tldrResult}}` (already shape-compatible with `TldrResult` record) |
| `ChannelNarratives` | `{{channelNarrationResults}}` (per-iteration array preserved input order) |
| `GeneratedAtUtc` | `{{run.completedAtUtc}}` (orchestrator-provided UTC stamp) |
| `_validationMetadata.scrubbedText` | `{{validationResult.scrubbedText}}` — sidecar, not consumed by widget today |
| `_validationMetadata.removedTerms` | `{{validationResult.removedTerms}}` — sidecar for observability |

**Shape-bridging note**: `ChannelNarrationResult` in `DailyBriefingEndpoints.cs` line 906 has `{ Category, Bullets[] }` whereas the Action's output schema is `{ channel, narrative[], itemCount, bulletCount }`. The new payload-execution method on `AnalysisOrchestrationService` (task 031) performs the field-name mapping `channel → category` and `narrative[] → bullets[].narrative` when projecting. This keeps the Action's natural schema (referenced by its own JPS metadata) intact while preserving the widget's existing contract.

---

## 4. Allow-List Construction Logic

The allow-list passed to `ValidateEntityNames` is built **declaratively** in the node's `inputBinding.allowList` template (see the JSON file). The logic is:

```
allowList = distinct(
  flatten([
    map(priorityItems,    item => item.regardingName),
    map(priorityItems,    item => item.viaMatter?.name),
    map(priorityItems,    item => item.source?.owningUser),
    map(categories,       cat  => map(cat.items, item => item.regardingName)),
    map(categories,       cat  => map(cat.items, item => item.viaMatter?.name)),
    map(categories,       cat  => map(cat.items, item => item.source?.owningUser)),
    map(channels,         ch   => map(ch.items, item => item.regardingName)),
    map(channels,         ch   => map(ch.items, item => item.viaMatter?.name)),
    map(channels,         ch   => map(ch.items, item => item.source?.owningUser)),
  ])
)
```

Key properties:

- **Flat extraction** — no nesting; the allow-list is `string[]` per the Action's contract
- **Deduplicated** — `distinct(...)` removes repeated entity names that appear in priorityItems AND in a channel
- **Null-safe** — missing `viaMatter` or missing `source` does not contribute to the list (the templating engine skips null/undefined paths)
- **Empty-allowed** — when the payload is empty, the allow-list is `[]`. Per the EntityNameValidator contract this is the explicit opt-in for "scrub every proper-noun-bearing sentence" semantics. In practice, with an empty payload the prior narration Skills also emit empty/no-op responses, so the scrubber has nothing to remove.

This matches the `BRIEF-VALIDATE-ENTITY-NAMES` Action's documented `input.allowList` contract (see `brief-validate-entity-names.action.json` lines 40-45).

---

## 5. LoadKnowledge Placeholder Note (R5 Deferral)

Per spec line 58 — *"AI Search 'matter context' knowledge node deferred to R5"* — the `LoadKnowledge` node in this playbook is intentionally a **pass-through placeholder** for R4:

- It accepts the `start.channels` array as input
- It binds them unchanged to `channelRegistry.channels` for the downstream fan-out

It performs NO knowledge retrieval. R5 will replace its `configJson` with an actual AI Search binding (the channel-registry knowledge source code is `TBD-CHANNEL-REGISTRY` per the file's `r5BindingPlan.knowledgeSourceCode`). When that lands, the node will additionally retrieve channel display labels + per-channel category metadata, and the downstream fan-out will iterate over the enriched channel objects.

For R4 the node exists to **reserve the graph position** (so the R5 swap is a config edit, not a topology edit) and to make the node graph match the spec FR-4 specification verbatim.

---

## 6. Action GUID Cross-Reference

Action rows deployed in PR 1 (referenced by this playbook):

| Action Code | ActionType | Deployment Task | Dataverse GUID |
|---|---|---|---|
| BRIEF-NARRATE-TLDR | 0 (AiAnalysis) | task 006 | ce299eb4-fc70-f111-ab0e-7ced8ddc4cc6 |
| BRIEF-NARRATE-CHANNEL | 0 (AiAnalysis) | task 006 | dc3533c0-fc70-f111-ab0e-7ced8ddc4cc6 |
| BRIEF-VALIDATE-ENTITY-NAMES | 141 (Tool) | task 007 | 290e786c-ff70-f111-ab0e-7ced8ddc4cc6 |
| SYS-LOOKUP-MEMBERSHIP | 52 (Workflow) | task 005 | ca44b7aa-fc70-f111-ab0e-7ced8ddc4cc6 (NOT used by this playbook — referenced for catalog completeness) |

Task 011 (deploy) reads these GUIDs from the JSON's `metadata.actionGuidsDeployed` block and binds them to the node-level `actionCode` references during Deploy-Playbook.ps1 invocation. If any of these GUIDs is missing/stale at deploy time, task 011 fails fast with an explicit "Action row not found in spaarkedev1" error and does NOT proceed.

---

## 7. Anti-Hallucination Posture (FR-13 + FR-14 Combined)

The defense-in-depth layering:

1. **FR-13 (prompt grounding)** — Action `sprk_systemprompt` for `BRIEF-NARRATE-TLDR` and `BRIEF-NARRATE-CHANNEL`:
   - Explicit "use ONLY names present in input" instruction
   - Temperature 0
   - NO baked example names anywhere in the prompt
   - Examples in JPS use placeholder tokens `<matter-name-1>`, `<category-A>`, etc.

2. **FR-14 (post-LLM scrub)** — `ValidateEntityNames` Tool node:
   - Runs AFTER both narration Skills
   - Builds allow-list from input payload (the only source of truth)
   - Sentence-splits LLM output and removes any sentence containing a proper-noun span NOT in the allow-list
   - Emits `hallucination_detected` warning per removed term for App Insights monitoring

If the LLM hallucinates despite FR-13 (R3 UAT proved this can happen even with temperature 0 + grounded prompts), FR-14 catches it deterministically. The playbook node graph is the load-bearing wiring that ensures FR-13 + FR-14 actually compose.

---

## 8. Component Justification (CLAUDE.md §11)

Included in the JSON top-level metadata at `playbook.componentJustification`. Reproduced here for traceability:

- **Existing**: No existing playbook performs grounded narration of an arbitrary structured-notification payload. The 7 notification-* playbooks (PB-016–PB-022) PRODUCE notifications; they do not CONSUME a payload for narration. The chat-summarize playbooks consume document content, not structured notification arrays.
- **Extension**: No. Existing playbooks are notification producers (`sprk_playbooktype = 2`); this is an AiAnalysis playbook (`sprk_playbooktype = 0`) consumed by the daily-briefing widget via /narrate. They serve orthogonal consumer-types and cannot share a node graph.
- **Cost-of-doing-nothing**: Spec FR-12 (line 159) is binding: the /narrate endpoint must dispatch to JPS, not run hardcoded prompt strings. Without this playbook the new wrapper has no target to dispatch to — the entire R4 W2 Consumer workstream is blocked (PR 4 tasks 030-036 all depend on this playbook existing in spaarkedev1). The R3 UAT-observed firm/case-name hallucination defect (FR-13 / FR-14 evidence) cannot be remediated end-to-end without this playbook composing the grounded prompt with the scrubber Tool.

Passes the three-question test with concrete failure modes (not abstract "future flexibility" claims).
