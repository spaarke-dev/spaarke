# AI Chat Session Continuity and Persistence — Research Notes

> **Created**: 2026-05-16
> **Purpose**: Research best practices for AI chat session continuity and persistence in enterprise applications, with concrete recommendations for the Spaarke AI platform.
> **Context**: `sprk_spaarkeai` standalone Code Page, law firm users, existing stack: Dataverse + Redis + Azure Blob + BFF API. Sessions currently stored in Redis with 24h TTL.

---

## 1. Where to Store AI Chat Sessions — Storage Tier Comparison

### 1.1 The Industry Consensus: Tiered Storage

Every major enterprise AI platform that operates at scale uses a **tiered storage model** rather than a single backend. The pattern has converged:

| Tier | Technology | Role | Latency |
|------|-----------|------|---------|
| Hot (working) | Redis / in-memory | Active session, last N turns, pending plans | <5ms |
| Warm (recent) | Cosmos DB / Postgres | Completed sessions, searchable history, 30-90 days | 5-50ms |
| Cold (archive) | Azure Blob / Table Storage | Sessions older than 90 days, compliance archive | 50ms-2s |

This is already partially implemented in Spaarke: `ChatSessionManager` uses Redis as hot cache with a 24h sliding TTL, and `ChatDataverseRepository` writes to `sprk_aichatsummary` / `sprk_aichatmessage` as cold storage. The gap is a **warm tier** (sessions between 24h and 90 days old are unreachable once Redis evicts them, unless Dataverse holds them — and Dataverse has its own limitations below).

### 1.2 Dataverse Tables — Current Spaarke Approach

**What Dataverse is good for:**
- Audit trail and compliance: full message records, tied to Dataverse security model
- CRM entity linkage: sessions attached to matters, contacts, documents via lookup columns
- Power Platform integration: sessions surfaceable in model-driven apps, Power BI
- Zero additional infrastructure: already provisioned and running

**Dataverse limitations for high-volume chat storage:**
- Storage is metered and expensive at scale. Database storage runs ~$40/GB/month within the licensed pool, and overages are charged at $10/GB/month beyond entitlement. For reference, 1000 users x 200 sessions x 50 messages x 2KB/message = ~20GB of message data per year — real cost in a Dataverse-only model.
- Row-level operations are slower than purpose-built document stores. Dataverse write throughput peaks around 500 API requests/second per environment (hard limit). High-concurrency chat with many simultaneous users will hit this ceiling.
- The December 2025 storage capacity update increased default allotments, but AI-powered features (Copilot/Agents, enhanced semantic indexing) consume additional Dataverse storage from that same pool.
- No native TTL or automatic archival. Old records must be deleted by a background job or Flow.
- No vector/semantic search on message content natively (requires Azure AI Search index or Dataverse search with enhanced indexing).

**What Copilot Studio uses:** Copilot Studio writes every conversation to a `conversationtranscript` table in the associated Dataverse environment with a configurable 30-day default retention period. They use Dataverse as the system of record, not for high-performance session reads — agents re-hydrate from the transcript only on explicit resume.

**Verdict for Spaarke:** Dataverse is correct for the **audit/compliance tier** (cold) and for **CRM entity linkage** (attach sessions to matters/contacts). It is not the right backend for active session reads/writes during a live conversation.

### 1.3 Azure Cosmos DB — The Industry Recommendation for AI Chat

**Why Cosmos DB has become the default for AI conversation persistence:**

OpenAI migrated ChatGPT's conversation history to Azure Cosmos DB because chat history is scoped to a specific user or conversation ID, rarely needs joins across users — making it embarrassingly parallel and ideal for horizontal sharding. OpenAI's pattern: writes are buffered in Redis, then flushed to Cosmos DB in batches roughly once per minute.

Azure AI Foundry Agent Service uses Cosmos DB as its BYO Thread Storage backend (GA as of Build 2025). The `enterprise_memory` database contains three containers:
- `thread-message-store` — end-user conversation messages
- `system-thread-message-store` — internal system messages and metadata
- `agent-entity-store` — model inputs and outputs for audit/replay

**Cosmos DB advantages for AI chat:**
- Partition by `tenantId + userId` or `tenantId + sessionId` for multi-tenant isolation
- Composite partition key `{tenantId}/{userId}/{sessionId}` supports efficient per-user queries
- Native TTL per document — sessions auto-expire without background jobs
- Single-digit millisecond reads with multi-region replication
- Autoscale absorbs burst traffic from AI inference peaks
- Native vector indexing for semantic search on message content
- RBAC and customer-managed keys for legal data sovereignty

**Cost estimate for Spaarke scale (hundreds of users, moderate volume):**
- Serverless Cosmos DB: ~$0.25 per million RUs consumed. At low-moderate volume this is significantly cheaper than Dataverse overages.
- 1GB stored = ~$0.25/month (Cosmos DB) vs ~$40/month (Dataverse database storage overage tier)
- For law firm scale (500 users, 10 sessions/user/month, 30 messages/session): ~180MB/month stored + minimal RU spend = approximately $5-15/month. This compares to potentially hundreds of dollars in Dataverse storage overage if all messages accumulate there.

**Verdict for Spaarke:** Cosmos DB should be the **warm storage tier** (sessions 24h to 90 days). It provides searchable, durable, cost-effective conversation history that Redis cannot hold and that Dataverse is too expensive to store at message-level granularity.

### 1.4 Azure Redis — Current Spaarke Hot Tier

**What Redis is correctly used for today:**
- Active session: `chat:session:{tenantId}:{sessionId}` with 24h sliding TTL
- Context mappings: 30-minute sliding TTL
- Pending plans: 30-minute TTL, atomic get+delete
- OBO token cache

**Redis limitations for persistent sessions:**
- TTL-based eviction means sessions are unreachable after 24h (or sooner if Redis memory pressure triggers eviction under LRU policy). This is the current gap: there is no warm tier to fall back to once Redis evicts a session.
- Redis is not designed for rich querying. "Show me all sessions for matter X from the last 30 days" requires scanning all keys or a separate index — neither is efficient.
- Storage cost scales with memory, not just data volume. Redis memory is more expensive per GB than blob/document storage.
- No native full-text or semantic search across message content.

**Redis best practices for Spaarke:**
- Extend TTL to 72h for active sessions (users return within 3 days in enterprise workflows)
- On session read from cold storage, re-warm Redis with the reconstructed session
- Keep Redis as the exclusive write path during active conversation; flush to Cosmos DB asynchronously
- Store only the last 20 messages verbatim in Redis; store the summary token in Redis pointing to Cosmos DB for full history

**LangGraph + Redis pattern (relevant to Agent Service integration):** LangGraph uses Redis as a persistent checkpointer for agent state — each workflow node writes a checkpoint to Redis, enabling pause/resume of long-running agent tasks. This pattern is directly applicable to the `AgentServiceNodeExecutor` (AT 60) being built in Wave 6.

### 1.5 Azure Blob Storage — Cold Archive Tier

**Role:** Archive tier for sessions older than 90 days. Not for session reads — only for compliance export, e-discovery, and regulatory retention.

**Pricing (2026, LRS East US):**
- Hot: $0.018/GB/month
- Cool: $0.010/GB/month
- Cold: $0.0045/GB/month
- Archive: $0.00099/GB/month (but retrieval costs $0.02/GB + $5.50/10K operations — avoid for anything that needs to be read back)

**Blob storage for chat sessions:** Serialize each completed session as a single JSON blob per conversation (not per message). Use Cold tier for sessions >90 days. Use container-per-tenant naming: `chat-sessions/{tenantId}/{year}/{month}/{sessionId}.json`. Lifecycle management rules automatically tier blobs from Cool to Cold to Archive without manual intervention.

**Verdict for Spaarke:** Blob cold storage is the correct archive tier. Add a nightly Azure Function (or BFF background service per ADR-001) that moves completed sessions from Cosmos DB to Blob after 90 days, then removes them from Cosmos DB. Maintain the Dataverse `sprk_aichatsummary` record (title, user, matter, timestamp, session ID) as a permanent index — the summary record in Dataverse points to the Blob URL for full session retrieval.

### 1.6 What the Major AI Platforms Use

| Platform | Hot Tier | Warm Tier | Cold/Archive |
|----------|---------|----------|--------------|
| **ChatGPT (OpenAI)** | Redis (buffered writes) | Azure Cosmos DB (message history) | Not public |
| **GitHub Copilot** | In-memory per session | Local files (local memory tool), GitHub-hosted memory service (28-day TTL) | Not persistent beyond 28 days unless explicitly saved |
| **Microsoft Copilot (M365)** | Exchange mailbox (hidden folder) | Exchange + Purview retention | Purview compliance archive, 18-month history |
| **Azure Copilot** | Microsoft-managed or customer Cosmos DB | Azure Cosmos DB (BYO storage option) | Customer-controlled |
| **Copilot Studio** | In-memory during turn | Dataverse `conversationtranscript` (30-day default) | Power Automate export to long-term store |
| **Azure AI Foundry Agent Service** | Redis (session context) | Cosmos DB `enterprise_memory` (BYO thread storage) | Customer-managed via Cosmos DB TTL + lifecycle |

**Key observation:** Microsoft's own AI Foundry Agent Service — which Spaarke will integrate with in Phase 2 — recommends Cosmos DB as the thread storage backend. Aligning Spaarke's warm tier to Cosmos DB creates a unified storage model for both custom chat sessions and Foundry agent threads.

---

## 2. What to Persist in a Session

### 2.1 Session Record Schema (Recommended)

A Spaarke chat session should persist two distinct record types:

**Session Header** (stored in Cosmos DB, mirrored as summary in Dataverse):
```
sessionId          string   — ULID, URL-safe, sortable by creation time
tenantId           string   — Dataverse tenant/org ID
userId             string   — Entra user object ID
entityType         string   — "matter" | "project" | "document" | "none"
entityId           string   — ID of the anchoring entity (matterId, etc.)
entityDisplayName  string   — "Acme v. Smith (Matter #2024-0441)"
playbookId         string   — Active playbook at session creation
title              string   — Auto-generated or user-set (first 60 chars of first message)
status             enum     — "active" | "summarized" | "archived"
createdAt          datetime
updatedAt          datetime
lastMessageAt      datetime
messageCount       int
tokenCount         int      — Running total for cost tracking
summaryText        string   — LLM-generated summary (populated after summarization)
workspaceSnapshot  object   — See 2.3 below
```

**Message Record** (stored in Cosmos DB, partition key = `sessionId`):
```
messageId          string   — ULID
sessionId          string   — FK to session
role               enum     — "user" | "assistant" | "system" | "tool"
content            string   — Message text (user/assistant) or JSON (tool)
toolName           string?  — When role="tool": which tool was called
toolCallId         string?  — Matches function call ID from assistant turn
outputPaneEvent    object?  — Serialized SSE output_pane event if this message produced widget output
sourcePaneEvent    object?  — Serialized SSE source_pane event
isArchived         bool     — True if this message was rolled into a summary
createdAt          datetime
tokenCount         int      — Tokens in this message
```

### 2.2 Tool Call Results and Intermediate State

**What to store:** For each tool invocation (document fetch, legal research query, code interpreter run, playbook action), store the tool call ID, tool name, input arguments, and a digest of the result (not necessarily the full result, which may be large).

**What not to store verbatim:**
- Raw binary outputs from Code Interpreter (charts, data files) — store as blob references, not inline
- Full SPE document content — store the document ID and page range, not the text
- Streaming SSE chunks — store only the final assembled output

**Pending plan state:** `PendingPlan` records (compound intent plans awaiting user approval) currently live only in Redis with a 30-minute TTL. This is acceptable: plans are ephemeral. However, if a user closes the tab during a pending plan, the plan is lost on next page load. Consider persisting pending plans to Cosmos DB with the session, with a status of `"pending_approval"` — the session restore flow checks for this and re-presents the plan.

### 2.3 Workspace State

The workspace state captures the UI configuration at session close so it can be restored on resume. This is a snapshot, not a replay — the widgets are re-rendered from stored data, not by re-executing the AI queries.

**Workspace snapshot schema:**
```json
{
  "workspaceSnapshot": {
    "capturedAt": "2026-05-16T14:32:00Z",
    "panelLayout": {
      "leftPanelWidthPct": 30,
      "centerPanelWidthPct": 45,
      "rightPanelWidthPct": 25,
      "rightPanelCollapsed": false
    },
    "outputPane": {
      "activeWidgetType": "DocumentAnalysisEditor",
      "widgetState": { /* widget-specific serializable state */ }
    },
    "sourcePane": {
      "activeWidgetType": "SpeDocumentViewer",
      "widgetState": {
        "documentId": "abc123",
        "pageNumber": 7,
        "highlightRange": { "start": 245, "end": 312 }
      }
    },
    "chatPanelScrollPosition": 0,
    "activePlaybookId": "PLB-matter-analysis-v3"
  }
}
```

**Important:** Widget state must be serializable (no React component instances, no DOM references). Each widget type registers a `serializeState()` / `deserializeState()` pair in the widget registry.

**Panel layout** (left/center/right widths, collapsed/expanded) should be stored both per-session (restored on session resume) and as a user preference (applied to new sessions). See Section 4 (prompt libraries) for the user preferences model.

### 2.4 Entity Context

When a session is anchored to an entity (matter, project, document), store enough context to detect staleness on restore:

```json
{
  "entityContext": {
    "entityType": "matter",
    "entityId": "matter-8834a1c2",
    "entityName": "Acme Corp v. Smith Industries",
    "entityETag": "W/\"2026-05-14T09:15:00Z\"",
    "entityVersion": 42,
    "snapshotAt": "2026-05-15T16:44:00Z",
    "keyFields": {
      "status": "Active",
      "matterType": "Litigation",
      "responsibleAttorneyId": "user-abc"
    }
  }
}
```

The `entityETag` or `entityVersion` is used during session restore to detect stale context (see Section 3.3).

### 2.5 Playbook Configuration

Store the playbook that was active when the session was created:
- `playbookId` — permanent reference to the Dataverse `sprk_jpsplaybook` record
- `playbookVersion` — version at session creation time (for change detection)
- `appliedScopes` — list of scope IDs that were active

On session restore, compare stored playbook version against current. If the playbook has been updated since session creation, notify the user: "The analysis playbook for this matter type was updated since your last session. Resume with the original playbook or switch to the current version?"

### 2.6 User Preferences and Pinned Items

These live at the user profile level, not the session level:

```json
{
  "userPreferences": {
    "userId": "user-abc123",
    "tenantId": "tenant-xyz",
    "defaultPanelLayout": { "leftPct": 30, "centerPct": 45, "rightPct": 25 },
    "preferredTheme": "dark",
    "chatFontSize": "medium",
    "autoOpenSourcePane": true,
    "pinnedSessions": ["session-001", "session-042"],
    "pinnedPrompts": ["prompt-lib-id-1", "prompt-lib-id-7"],
    "recentEntities": [
      { "entityType": "matter", "entityId": "matter-001", "displayName": "Acme v. Smith", "lastAccessedAt": "..." }
    ]
  }
}
```

**Storage:** User preferences are small (< 5KB) and read on every app load. Store in Dataverse as a custom `sprk_aiuserpreference` record (one per user), cached in Redis with a 1h TTL. Dataverse is correct here: it integrates with the existing user/contact model, supports admin override, and doesn't need the throughput performance of Cosmos DB for this low-volume data.

---

## 3. Session Restore Patterns

### 3.1 Efficient Context Reload (Not Full Conversation Replay)

**The wrong approach:** Feeding the entire message history back into the LLM context window on session restore. This is expensive (tokens), slow (LLM processing), and unnecessary.

**The right approach — context reconstruction without replay:**

On session resume, the BFF should construct a `ResumedChatContext` object that the agent receives instead of raw message history:

```csharp
public record ResumedChatContext
{
    // What the AI needs to continue intelligently
    public string SessionSummary { get; init; }        // LLM summary of prior conversation
    public ChatMessage[] RecentMessages { get; init; } // Last 10 verbatim messages
    public EntityContext EntityContext { get; init; }  // Matter/project anchoring
    public WorkspaceSnapshot WorkspaceSnapshot { get; init; }

    // What the UI needs to reconstruct state
    public SessionHeader SessionHeader { get; init; }
}
```

The system prompt for a resumed session should include the `SessionSummary` as context, followed by the last 10 verbatim messages. The model then has sufficient context to continue naturally without re-processing the full history.

**Latency target:** Session restore should complete in under 1 second. With Redis warm cache hit: ~20ms. With Cosmos DB read (cache miss): ~50-100ms. With Blob archive retrieval: 200-500ms (acceptable since archive sessions aren't resumed frequently).

### 3.2 Summarization for Long Conversations

The current `ChatHistoryManager` triggers summarization at 15 messages and archival at 50 messages. The summarization is noted as a "placeholder" pending AIPL-054. Here is the recommended approach based on 2025-2026 best practices:

**When to summarize:**
- Threshold-based: every 15 messages (current), or when accumulated token count exceeds 8,000 tokens in history
- Session boundary: always generate a summary when a session is closed/idle for >4 hours

**What summarization should produce:**

```
Summary format (for LLM system prompt injection on resume):
"Previous session (2026-05-15): User analyzed the Acme v. Smith litigation matter.
Key findings: (1) Contract breach identified in Clause 7.3, (2) three precedent cases
identified — Johnson v. Williams (2022), Brown Corp (2024). User drafted a risk
assessment memo. Outstanding question: whether the statute of limitations applies to
the counterclaim."
```

**Progressive summarization pattern:**
- Recent messages (last 10): kept verbatim in Redis and Cosmos DB
- Messages 11-50: rolled into an intermediate summary (stored in `summaryText` on the session record)
- Messages 50+: sessions beyond 50 messages get an archive summary — a more compressed version of the intermediate summary

The three memory tiers mirror enterprise best practice (working / episodic / semantic):
- **Working memory:** Last 10 messages verbatim (Redis)
- **Episodic memory:** LLM summary of the session (Cosmos DB session header)
- **Semantic memory:** Entity-level accumulated knowledge (out of scope for R1, but the pattern for R2)

**Summarization implementation:**
Do not summarize synchronously during a user message. Trigger summarization asynchronously via a background job (ServiceBus message per ADR-004) when the message count threshold is crossed. The summarization call is a short, cheap LLM call (gpt-4o-mini, ~200 token prompt → 150 token summary).

**Token reduction achieved:** Production implementations report 80-90% token cost reduction for long sessions using this approach while maintaining response quality equivalent to full-history replay.

### 3.3 Handling Stale Data (Entity Changed Since Session Was Saved)

This is one of the most common failure modes in enterprise AI session continuity. Agents hit staleness issues when they have access to both current and outdated information and use the wrong one.

**The staleness problem in Spaarke:** A user starts a session analyzing Acme v. Smith. The matter status changes (e.g., settled, or a new document is uploaded). When the user resumes the session next week, the AI has stale entity context from the snapshot.

**Detection strategy:**

On session restore, before presenting the session to the user, the BFF should:

1. Fetch the current entity record (e.g., the matter) with a lightweight ETag/version check
2. Compare against `entityContext.entityETag` stored in the session
3. Determine the staleness category:

| Condition | Action |
|-----------|--------|
| ETag matches (no change) | Restore silently |
| Minor change (e.g., responsible attorney changed) | Restore with inline notice: "Matter record was updated since your last session. The AI context has been refreshed." |
| Status change (matter closed/settled) | Restore with prominent warning: "This matter's status changed to 'Closed' on 2026-05-10. Continuing analysis may reflect outdated information." |
| Entity deleted | Block restore; show error: "This matter record has been deleted. The session cannot be resumed." |

4. If resumed, inject the current entity snapshot into the reconstructed context, not the stale snapshot.

**Document-level staleness:** If the session anchored to a specific SPE document, check document version/ETag against SpeFileStore. If the document was modified, note in the system prompt: "Note: The document was modified since this session was last active. Current version is shown in the source pane."

**Bi-temporal modeling (advanced, R2+):** Track both when events happened and when the system learned about them. This enables answering questions like "what did the AI know about this matter when it made its recommendation on May 15?" — valuable for legal accountability.

### 3.4 Workspace Snapshot vs. Replay Approach

**Snapshot approach (recommended):** On session close, serialize the current widget states to `workspaceSnapshot` in the session record. On resume, each widget reads its stored state and renders directly from it — no AI calls required to reconstruct the workspace.

**Replay approach (avoid):** Re-execute all AI queries from the session to regenerate widget outputs. This is prohibitively expensive (re-running document analysis, legal research queries) and introduces non-determinism (the AI may produce different outputs on second run).

**Hybrid for dynamic content:** Some widgets display data that may have changed since the session was saved (e.g., a budget dashboard showing matter financials). For these widgets:
- On restore, render the snapshot state immediately (instant UI)
- Offer a "Refresh" button to re-execute the underlying query with current data
- Show a "Last updated" timestamp on the widget

The `SpeDocumentViewer` widget preserves the document ID and page/highlight position but re-fetches the document content on restore (via SpeFileStore) to ensure it has the current version.

---

## 4. User Prompt Libraries

### 4.1 How Enterprise AI Products Approach Prompt Libraries

The enterprise AI prompt library market matured rapidly in 2025-2026. Key patterns from products like OpenAI's Prompt Packs (released September 2025), Microsoft's M365 Copilot, and GitHub Copilot:

**Tiered ownership model:**

| Level | Who Creates | Who Can Use | Examples |
|-------|-------------|-------------|---------|
| **System** | Spaarke / law firm IT | All users | "Review document for standard litigation risks", "Summarize matter status" |
| **Organization** | Firm-wide power users / admins | All users at firm | "Check billing narrative against {matterId} rate schedule" |
| **Team/Practice Group** | Practice group leads | Team members | "Analyze IP claim for {patentNumber} in {jurisdiction}" |
| **Personal** | Individual attorneys | That user only | "My standard opening review: check {documentName} for force majeure" |

**What research shows:** Teams with shared prompt libraries outperform individual AI users by 43% on complex tasks (Microsoft Work Trend Index 2025). Firms suffer from "prompt entropy" when AI is used ad-hoc — inconsistent quality and wasted time recreating prompts.

### 4.2 Prompt Template Data Model

```json
{
  "promptId": "prompt-lib-uuid",
  "tenantId": "tenant-xyz",
  "ownerId": "user-abc",          // null for org/team prompts
  "ownerType": "personal",        // "system" | "organization" | "team" | "personal"
  "teamId": "team-litigation",    // null unless ownerType=team
  "title": "Standard Contract Review",
  "description": "Comprehensive contract review checking key risk areas",
  "category": "Document Review",
  "tags": ["contract", "review", "risk"],
  "template": "Review {documentName} for the following risk areas: {riskAreas}. Flag any clauses that deviate from our standard terms. Output format: {outputFormat}.",
  "variables": [
    { "name": "documentName", "label": "Document", "type": "entity-ref", "entityType": "document" },
    { "name": "riskAreas", "label": "Risk Areas", "type": "multiselect", "options": ["Indemnification", "Limitation of Liability", "IP Assignment", "Termination"] },
    { "name": "outputFormat", "label": "Output Format", "type": "select", "options": ["Bullet list", "Executive memo", "Risk matrix"] }
  ],
  "suggestedPlaybookId": "PLB-contract-review",
  "usageCount": 47,
  "lastUsedAt": "2026-05-14T10:22:00Z",
  "isFavorited": false,           // per-user, stored in userPreferences.pinnedPrompts
  "isActive": true,
  "createdAt": "2026-03-01T00:00:00Z",
  "updatedAt": "2026-05-01T00:00:00Z",
  "version": 3
}
```

**Variable types:**
- `text` — free text input
- `entity-ref` — Dataverse record picker (matter, contact, document)
- `select` / `multiselect` — predefined options
- `date` — date picker
- `current-entity` — auto-filled from the active entity context (no user input required)

### 4.3 Storage Backend for Prompt Library

**Recommended: Dataverse custom table** (`sprk_aipromptlibrary`)

Rationale:
- Prompt library records are low volume (hundreds to low thousands of records per firm)
- Query patterns are simple: filter by `ownerType`, `teamId`, `ownerId`, `category`
- Dataverse security model handles RBAC automatically: org-level prompts visible to all, team prompts visible to team members, personal prompts visible only to owner
- Power Platform integration: firms can manage org-level prompts from a model-driven app or Power Pages admin portal without custom UI
- No real-time performance requirement: prompts are loaded on app open, not on each message

**Caching:** Cache the user's resolved prompt library (personal + team + org prompts) in Redis with a 30-minute sliding TTL per user. Invalidate on any create/update/delete operation.

**Do not use Cosmos DB for prompt library:** The query patterns and volume don't justify the additional infrastructure. Cosmos DB is the right choice for high-volume, time-series, session data — not for a lookup table with hundreds of records.

### 4.4 Prompt Template Variable Resolution

When a user selects a prompt template, the UI should:

1. Identify all `{variables}` in the template
2. Auto-resolve `current-entity` variables from the active entity context (no input needed)
3. Present an inline form for remaining variables before submitting to chat
4. Substitute variables and send the composed prompt as the chat message
5. Log the prompt usage (increment `usageCount`, update `lastUsedAt`) asynchronously

**Example UI flow:**
User selects "Standard Contract Review" prompt →
Dialog appears pre-filled: Document = [current document from source pane], Risk Areas = [multiselect], Output Format = [select] →
User confirms → composed prompt sent to chat.

### 4.5 Organization-Level Prompt Governance

For law firms, org-level prompts may be subject to approval workflows:

- New org-level prompts go through a review queue (Power Automate flow or BFF approval endpoint)
- Prompts can be tagged with practice area, risk level, and compliance notes
- Usage analytics: which prompts are used most, by which teams — data for quarterly prompt library reviews
- Version history: when an org prompt is updated, previous version is preserved (users who pinned it see an "Update available" badge)

---

## 5. Best Practice Recommendations for Spaarke

### 5.1 Architecture Summary: Three-Tier Session Storage

```
                    WRITE PATH
User Message ──> BFF ──> Redis (24h→72h TTL, hot)
                     ──> Cosmos DB (async, warm, 90 days)
                     ──> Dataverse summary record (sync, permanent index)

                    READ PATH (session restore)
Session List ──> Dataverse sprk_aichatsummary (index, forever)
                     │
                     ├── Active session (< 72h) ──> Redis (10ms)
                     ├── Recent session (3-90 days) ──> Cosmos DB (50ms)
                     └── Archived session (> 90 days) ──> Blob Storage (500ms)

                    ARCHIVE PATH (nightly background job)
Cosmos DB (>90 days) ──> Blob Cold Tier ──> Removed from Cosmos DB
Dataverse summary record kept permanently (points to Blob URL)
```

### 5.2 Specific Recommendations by Component

**ChatSessionManager (existing):**
- Extend Redis TTL from 24h to 72h (most users return within 3 days; reduces cold reads)
- Add Cosmos DB as the warm storage fallback when Redis misses
- On Redis miss + Cosmos DB hit: re-warm Redis (write the session back with 72h TTL)
- On Cosmos DB miss: check Blob archive (rare; < 5% of sessions)

**ChatHistoryManager (existing):**
- Implement real LLM-based summarization (replace the placeholder at AIPL-054)
- Use gpt-4o-mini for summarization (cheap, fast, sufficient quality)
- Trigger summarization at 15 messages OR 8,000 tokens in history, whichever comes first
- Store the summary in `session.summaryText` in Cosmos DB
- Keep last 10 messages verbatim in both Redis and Cosmos DB

**New: SessionRestoreService (to build):**
- Fetches `ResumedChatContext` from tiered storage (Redis → Cosmos → Blob)
- Runs staleness check against current entity ETag
- Reconstructs the system prompt with session summary + recent messages
- Returns workspace snapshot to the frontend for UI reconstruction
- Target: < 500ms total restore time including entity staleness check

**New: Cosmos DB Warm Tier (to provision):**
- Use the existing `spaarke-openai-dev` resource group or create a `spaarke-conversations-dev` Cosmos DB serverless account
- Container: `chat-sessions` (partition key: `/sessionId`)
- Container: `chat-messages` (partition key: `/sessionId`)
- Enable TTL at container level: 90 days (7,776,000 seconds)
- Enable indexing on: `userId`, `tenantId`, `entityId`, `lastMessageAt`, `status`
- Serverless billing at dev/moderate volumes; switch to provisioned throughput (100-400 RU/s autoscale) when user base grows beyond ~200 concurrent users

**New: Prompt Library (to build in R2):**
- Dataverse table `sprk_aipromptlibrary` with columns matching the schema in Section 4.2
- BFF endpoints: `GET /api/ai/prompts` (user's resolved library), `POST /api/ai/prompts`, `PUT /api/ai/prompts/{id}`, `DELETE /api/ai/prompts/{id}`
- Cache in Redis with 30-minute TTL per user
- UI: prompt picker accessible from chat input (slash command `/prompts` or dedicated button in chat toolbar)

**Session history panel (task 032-chat-history-panel, in-scope for R1):**
- Data source: Dataverse `sprk_aichatsummary` table (permanent index, always available)
- Display: session title, entity name, last active date, message count, summary preview
- Search: full-text search on `title` and `summaryText` via Dataverse search (enable enhanced indexing for this table)
- Resume action: triggers `SessionRestoreService`, loads workspace snapshot, re-warms Redis

### 5.3 Prioritized Implementation Plan

**Now (R1 — task 032 scope):**
- Chat history panel reading from `sprk_aichatsummary` (already exists in Dataverse)
- Workspace snapshot: capture `panelLayout` + `outputPane.activeWidgetType` + `sourcePane.activeWidgetType` on session close
- Session restore: load summary + last 10 messages from Dataverse, render workspace snapshot
- Extend Redis TTL to 72h (one-line change in ChatSessionManager)

**Near-term (R1 follow-on task or R2):**
- Provision Cosmos DB warm tier
- Update ChatSessionManager to write to Cosmos DB asynchronously on each message (fire-and-forget with retry)
- Implement real LLM summarization (replaces AIPL-054 placeholder)
- Entity staleness detection on session restore

**R2:**
- Prompt library (Dataverse table + BFF endpoints + UI)
- Blob archive tier + nightly background job
- User preferences table with panel layout persistence
- Playbook version change detection on session restore

### 5.4 What This Does Not Change (Existing Architecture Preserved)

The recommendations above are additive to the existing architecture. No existing components need to be replaced:

- Redis remains the hot session tier (just TTL extended to 72h)
- Dataverse `sprk_aichatsummary` and `sprk_aichatmessage` remain the permanent audit record
- ADR-009 (Redis-first caching) is honored — Cosmos DB is a new warm tier, not a replacement for Redis
- ADR-015 (data minimization) — Cosmos DB stores message content, which is user-generated chat. Ensure Cosmos DB RBAC and CMK are configured to meet legal data governance requirements before storing matter-related conversation content there
- ADR-010 (DI minimalism) — `SessionRestoreService` and `CosmosSessionRepository` add 2 registrations; remain within the 15-registration ceiling if added carefully in the `AddAiModule` extension

### 5.5 Decision Needed: Cosmos DB Timing

The most important architectural decision is whether to provision Cosmos DB in R1 or defer to R2.

**Arguments for R1:**
- Azure AI Foundry Agent Service (Phase 2, Wave 6-7) uses Cosmos DB for BYO Thread Storage — provisioning now avoids a second infrastructure setup later
- Session history restore will be noticeably better with Cosmos DB warm tier (50ms reads vs. Dataverse API reads which can be 200-500ms under load)
- The Bicep template from the Azure AI Foundry team provisions both the Cosmos DB account and the Foundry connection in one deployment

**Arguments for R2:**
- R1 is already large (35 tasks, 12 waves). Adding infrastructure provisioning mid-project increases risk.
- Current Dataverse cold storage + Redis hot cache is functional for the session history panel feature in task 032. Users can see and resume sessions; it just won't be as fast.
- Cosmos DB warm tier is a performance optimization, not a correctness requirement for R1.

**Recommendation:** Defer Cosmos DB warm tier to R2, but provision the Cosmos DB account as part of the Phase 2 (Wave 6) Foundry Agent Service infrastructure work (task 062-foundry-agent-definition.poml). This way the account exists when needed for both Foundry BYO threads and the chat warm tier, without adding scope to Phase 1.

---

## Sources

- [Azure AI Foundry BYO Thread Storage in Cosmos DB — Microsoft Learn](https://learn.microsoft.com/en-us/azure/cosmos-db/gen-ai/azure-agent-service)
- [Azure AI Foundry Connection for Cosmos DB blog post — Azure Cosmos DB Blog](https://devblogs.microsoft.com/cosmosdb/azure-ai-foundry-connection-for-azure-cosmos-db-and-byo-thread-storage-in-azure-ai-agent-service/)
- [How to Leverage Azure Cosmos DB for AI Agent Memory Management — Frank's World](https://www.franksworld.com/2026/04/29/how-to-leverage-azure-cosmos-db-for-ai-agent-memory-management/)
- [Chat History with Azure Cosmos DB and Semantic Kernel — Stochastic Coder](https://stochasticcoder.com/2025/01/27/chat-history-with-azure-cosmos-db-and-semantic-kernel/)
- [Baseline Microsoft Foundry Chat Reference Architecture — Azure Architecture Center](https://learn.microsoft.com/en-us/azure/architecture/ai-ml/architecture/baseline-microsoft-foundry-chat)
- [Store LLM Chat History in Azure Managed Redis — Redis Tutorial (February 2026)](https://redis.io/tutorials/howtos/use-amr-store-llm-chat-history/)
- [How OpenAI Scales ChatGPT on Azure Cosmos DB — Medium](https://medium.com/@abhinav.dobhal/how-openai-scales-chatgpt-to-800-million-users-on-a-single-postgresql-database-f11571f09f7f)
- [LLM Chat History Summarization: Best Practices — Mem0 (October 2025)](https://mem0.ai/blog/llm-chat-history-summarization-guide-2025)
- [AI Agent Context Compression Strategies — Zylos Research (February 2026)](https://zylos.ai/research/2026-02-28-ai-agent-context-compression-strategies)
- [7 State Persistence Strategies for Long-Running AI Agents 2026 — Indium Tech](https://www.indium.tech/blog/7-state-persistence-strategies-ai-agents-2026/)
- [Memory in VS Code Agents — GitHub Copilot Documentation](https://code.visualstudio.com/docs/copilot/agents/memory)
- [Copilot Memory now on by default — GitHub Changelog (March 2026)](https://github.blog/changelog/2026-03-04-copilot-memory-now-on-by-default-for-pro-and-pro-users-in-public-preview/)
- [Bring Your Own Storage for Azure Copilot — Microsoft Learn](https://learn.microsoft.com/en-us/azure/copilot/bring-your-own-storage)
- [Conversation History in Microsoft Copilot — Microsoft Support](https://support.microsoft.com/en-us/topic/conversation-history-in-microsoft-copilot-9a07325a-0366-4c2d-82cb-dab61be8287c)
- [Microsoft 365 Copilot Memory — Microsoft Community Hub (July 2025)](https://techcommunity.microsoft.com/blog/microsoft365copilotblog/introducing-copilot-memory-a-more-productive-and-personalized-ai-for-the-way-you/4432059)
- [Copilot Studio Custom Analytics and Conversation Storage — Microsoft Learn](https://learn.microsoft.com/en-us/microsoft-copilot-studio/guidance/custom-analytics-strategy)
- [Enterprise AI Prompt Library Best Practices — AICamp](https://aicamp.so/blog/why-team-needs-shared-prompt-libraries/)
- [Enterprise Prompt Engineering Best Practices — StackAI](https://www.stackai.com/insights/enterprise-ai-prompt-engineering-best-practices-templates-and-governance-for-business-teams)
- [Azure Blob Storage Pricing 2026 — nOps](https://www.nops.io/blog/azure-storage-pricing/)
- [Dataverse Flexible Capacity December 2025 — Microsoft Power Platform Blog](https://www.microsoft.com/en-us/power-platform/blog/2025/12/04/dataverse-capacity/)
- [Azure Managed Redis for AI Agents — ITNEXT](https://itnext.io/azure-managed-redis-for-ai-agents-semantic-caching-vector-knowledge-stores-and-memory-at-20d380c68047)
- [Context Window Management Strategies — Maxim AI](https://www.getmaxim.ai/articles/context-window-management-strategies-for-long-context-ai-agents-and-chatbots/)
- [Announcing Dataverse Capabilities for Multi-Agent Operations — Microsoft Copilot Blog](https://www.microsoft.com/en-us/microsoft-copilot/blog/copilot-studio/announcing-new-microsoft-dataverse-capabilities-for-multi-agent-operations/)
