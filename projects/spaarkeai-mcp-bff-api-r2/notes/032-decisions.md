# Task 032 - spaarke_get_matter_summary: STOPPED at the POML escalation (2026-11-26)

No code was written. The POML escalation ("if 030's path cannot target the matter-summary Binding, STOP and escalate") fires.

## Finding: 030's run path cannot start a matter summary

| Fact | Evidence |
|---|---|
| 030's start takes a JPS playbook id plus document ids: `IPlaybookRunAi.StartAsCallerAsync(http, playbookId, documentIds, parameters, ct)` builds a `PlaybookRunRequest { PlaybookId, DocumentIds, Parameters }` and runs `IPlaybookOrchestrationService`. | `Services/Ai/PublicContracts/IPlaybookRunAi.cs`, `Services/Ai/PlaybookRuns/DetachedPlaybookRunner.cs:205` |
| The `matter-summary` Binding is a chat capability, not a playbook: `sprk_playbook` is null, action SUM-CHAT@v1, disposition work_product. There is no playbook id to pass. | `notes/012-matter-summary-topic.md` section 1 |
| Its only producer is the chat dispatch seam: `SessionDispatchOrchestrator.DispatchAsync` requires an existing chat session (`GetSessionAsync(tenant, sessionId)`; not-found throws), summarizes the session's files (`FetchAsync` session-file text), and `OutputRouter` then calls `TopicRegistryWorkProductPersister.PersistAsync` with the session's host record (the matter). | `Services/Ai/Chat/SessionDispatchOrchestrator.cs:199-231, 830-920`; `Services/Ai/WorkProductRecordPersister.cs`; `Api/Ai/SummarizeSessionEndpoint.cs` |
| An MCP call has no chat session and no session files. Creating one would be a new session store (FR-10 / ADR-052 forbid) and would summarize no files. | - |
| The persisted value is the `WorkProductEnvelope` JSON (the persister writes the envelope into `sprk_recordsummary`), so the column carries its own generated date; a playbook `UpdateRecord` node would write plain text with no date. | `WorkProductRecordPersister.cs:230-247` |
| `TopicRegistryTtlLookup` keys on `sprk_playbookname` (`chat-summarize` on dev), so a by-name lookup of `matter-summary` misses until the owner approves the 012 PATCH. | `notes/012` section 4 |
| The matter form's insight card (MatterHeader sparkle) does read `sprk_recordsummary`, so "matches the card" holds for the read half. | `src/client/pcf/MatterHeader/control/MatterHeaderView.tsx` |

The read half (column read as the caller, freshness vs registry TTL, uniform not-found) does not depend on 030. The start half does.

## Options for the owner

1. **Author a matter-subject JPS playbook** (data, no engine edit; ADR-039 allows playbook authoring), modelled on `matter-health-single`: a `matterId` record parameter, a prompted node, an `UpdateRecord` node writing `sprk_matter.sprk_recordsummary`. The tool then calls `StartAsCallerAsync(playbookId, [], { matterId })`; 007's record-parameter policy already authorizes a matter parameter (Read, Write when a node writes through it). Costs: (a) an authoring task plus a registry/Binding decision (a second summary producer next to the chat one, or the chat Binding retargeted at the playbook); (b) the `UpdateRecord` node writes plain text, so the column has no date: freshness would need the envelope shape, or the Insights cache (spec FR-10's original wording; `InsightsPlaybookExecutionCache` is synchronous GetOrExecute, so it needs a peek-only read); (c) the facade/tool path takes at least one document id today (`RunPlaybookTool` caps), so confirm an empty `documentIds` run is accepted; (d) in-flight lookup by (caller, playbook, matterId): a small extension of the runner's in-memory table through `IPlaybookRunAi`, with tests (no new store).
2. **Read-only v1 now**: build `spaarke_get_matter_summary` as the read half only. Fresh column returns `{ summary, generatedOn }`; missing or expired returns a not-generated result with the Console link (the summary is generated from the matter's chat). Meets the cached-path and not-found criteria; drops the "starts exactly one run" criterion, so FR-10 and acceptance criterion 2 need an owner amendment. Needs the 012 PATCH (or a direct registry read) for the TTL key.
3. **Chat-session start** (a detached `SessionDispatchOrchestrator` run on an ephemeral session with host context = the matter). Not recommended: needs a session store entry (a new store), summarizes session files not matter data, and is a second ADR-052 detached shape.

Recommendation: option 2 if the owner wants the tool this wave; option 1 if FR-10's "start a run" must hold. They compose (2 first, 1 later adds the start branch).
