# Daily Update Service — Personalized Activity Digest

> **Project**: spaarke-daily-update-service
> **Status**: Design
> **Priority**: Medium
> **Last Updated**: March 30, 2026

---

## Executive Summary

Build a "Daily Briefing" system that uses Dataverse's native `appnotification` entity and extends the existing **Playbook execution engine** to generate notifications. Notification rules are playbooks — same JSON definition schema, same `sprk_analysisplaybook` entity, same `PlaybookOrchestrationService` execution engine — with a new `CreateNotification` node executor (ActionType 50) that outputs `appnotification` records instead of analysis results. A **Daily Digest Code Page** queries those notifications, groups by category, and presents a narrative TL;DR with action links. Users customize which notification playbooks are active and configure per-playbook parameters via preferences.

### Why Playbooks (Not a Separate Engine)

The existing playbook architecture already has everything a notification rule engine needs:

| Capability | Existing Playbook Infrastructure | Notification Use |
|------------|--------------------------------|------------------|
| **Definition schema** | JSON with nodes, scopes, conditions, actions | Same — notification rules ARE playbook definitions |
| **Storage** | `sprk_analysisplaybook` + `sprk_playbooknode` entities | Same entities, new `mode` category |
| **Node executors** | `CreateTask` (20), `UpdateRecord` (22), `SendEmail` (21), `Condition` (30) | Add `CreateNotification` (50) — same `INodeExecutor` pattern |
| **Execution engine** | `PlaybookOrchestrationService` with DAG, parallel batching, variable passing | Same engine — called by scheduler instead of user click |
| **Scope resolution** | Skills, knowledge, tools per node | Same — AI nodes use full scopes, deterministic nodes skip |
| **Builder UI** | PlaybookBuilder canvas (React Flow) | Same — add `createNotification` to node palette |
| **Management** | Playbook Library | Same — add "Notification Rules" category |

**What we add** (not what we build from scratch):
1. `CreateNotificationNodeExecutor` (new `INodeExecutor`, ActionType 50)
2. `PlaybookSchedulerService` (BackgroundService that triggers playbooks on schedule)
3. Daily Digest Code Page (reads `appnotification`, renders narrative)
4. User preference integration (which playbooks are active, parameter overrides)

---

## Problem Statement

Today, users must navigate to multiple views and entities to understand what happened since their last session:

- **Emails**: Check Outlook or the Communication entity list
- **Tasks due**: Open SmartToDo or the Events grid, filter by due date
- **New documents**: Navigate to each matter/project document library
- **Matter/project activity**: Scroll through the Activity Feed on the workspace
- **Similar items**: No way to discover that a document similar to yours was uploaded, or a matter similar to yours was created

There is no single view that answers: "What do I need to know right now?"

---

## Goals

1. **Single-glance digest** — one popup shows everything important in narrative TL;DR format
2. **Subscription-based** — users choose which notification playbooks are active and configure parameters
3. **Actionable** — each item links to the relevant record, document, or form
4. **Clearable** — users can mark items as read/dismissed (like notifications)
5. **Two types of intelligence**:
   - **Deterministic**: A document was added to your matter, a task is due tomorrow
   - **Probabilistic (AI)**: A document similar to yours was uploaded, a matter similar to yours was created
6. **Non-intrusive** — auto-popup on workspace load (if enabled) or manual trigger
7. **AI-enhanced narrative** — LLM-generated briefing summary (R1)
8. **Future-proof** — same playbook architecture supports workflow rules (event-driven record creation) when ready

---

## Architecture

### Foundation: Playbook Engine + `appnotification`

**Playbook type field**: `sprk_playbooktype` (OptionSet, already exists on `sprk_analysisplaybook`):
- AIAnalysis (0) — existing analysis playbooks
- Workflow (1) — future event-driven workflow rules
- **Notification (2)** — notification playbooks (this project)
- Rules (3) — future deterministic rule evaluation
- Hybrid (4) — future mixed-mode playbooks

```
                    Playbook Definition (shared JSON schema)
                    sprk_analysisplaybook entity (shared storage)
                    sprk_playbooktype field distinguishes modes
                    Playbook Library / Builder UI (shared management)
                              │
              ┌───────────────┼───────────────┐
              │               │               │
    type: AIAnalysis(0)  type: Notification(2) type: Workflow(1)
              │               │               │
     PlaybookOrchest-   PlaybookScheduler   EventTrigger
     rationService      Service (new)       Service (future)
     (user-initiated)   (scheduled)         (data-change)
              │               │               │
       Same INodeExecutor pipeline            │
       Same DAG, batching, variables          │
              │               │               │
       sprk_analysis    appnotification   sprk_event
       + SSE stream     (native entity)   (create/update)
```

### Existing Playbook Execution Engine (Reference)

> **Source**: `docs/architecture/playbook-architecture.md`, `docs/guides/JPS-AUTHORING-GUIDE.md`

The engine already supports:

**Node Executor Framework** (`Services/Ai/Nodes/`):
- `INodeExecutor` interface with `NodeExecutorRegistry[ActionType]` dispatch
- Existing executors: `AiAnalysis` (0), `AiCompletion` (1), `CreateTask` (20), `SendEmail` (21), `UpdateRecord` (22), `Condition` (30), `DeliverOutput` (40), `DeliverToIndex` (41)

**Orchestration** (`PlaybookOrchestrationService`):
- DAG construction via Kahn's topological sort
- Parallel batch execution (independent nodes grouped, `SemaphoreSlim` throttle)
- `PlaybookRunContext` with thread-safe `ConcurrentDictionary` for node outputs
- Variable substitution: `{{variableName}}` references upstream outputs
- Streaming events via SSE (`PlaybookStreamEvent`)

**JPS Prompt Schema** (`PromptSchemaRenderer`):
- Externalized prompts in JSON (stored in `sprk_aiaction.sprk_systemprompt`)
- Scope composition via `$ref` (knowledge, skills)
- Template parameters, override merging
- Structured output with JSON Schema generation

**Three-Level Node Type System**:
- Canvas Type (React component selection)
- Dataverse NodeType (scope resolution: `AIAnalysis`, `Output`, `Control`, `Workflow`)
- ActionType (fine-grained executor dispatch)

### What We Add

#### 1. CreateNotificationNodeExecutor (ActionType 50)

```csharp
// New file: Services/Ai/Nodes/CreateNotificationNodeExecutor.cs
public class CreateNotificationNodeExecutor : INodeExecutor
{
    public ActionType ActionType => ActionType.CreateNotification;

    public async Task<NodeOutput> ExecuteAsync(
        NodeExecutionContext context, CancellationToken ct)
    {
        var config = context.Node.ConfigJson;
        var title = context.ResolveTemplate(config.Title);
        var body = context.ResolveTemplate(config.Body);
        var userId = context.RunContext.UserId;  // new field

        await dataverseService.CreateRecordAsync("appnotifications", new {
            title,
            body,
            ownerid_appnotification = $"/systemusers({userId})",
            icontype = MapCategoryToIcon(config.Category),
            toasttype = config.ToastType ?? 200000000,
            priority = MapPriority(config.Priority),
            expiryon = DateTime.UtcNow.AddDays(config.ExpiryDays ?? 14),
            data = JsonSerializer.Serialize(new {
                actions = config.ActionUrl != null ? new[] {
                    new { title = "Open", data = new { url = config.ActionUrl } }
                } : null,
                customData = new {
                    category = config.Category,
                    regardingEntityType = config.RegardingEntityType,
                    regardingEntityId = context.ResolveTemplate(config.RegardingEntityId),
                    isAiGenerated = config.IsAiGenerated ?? false,
                    aiConfidence = config.AiConfidence,
                    playbookId = context.RunContext.PlaybookId.ToString()
                }
            })
        });

        return new NodeOutput { Text = $"Notification created: {title}" };
    }
}
```

**Registration** (add to `NodeExecutorRegistry` and enums):
```csharp
// In ActionType enum:
CreateNotification = 50

// In NodeType enum (maps to existing Workflow category):
// CreateNotification uses NodeType.Workflow (100000003) — no LLM scopes needed

// In NodeExecutorRegistry:
Register(ActionType.CreateNotification, new CreateNotificationNodeExecutor(...));
```

**Canvas type** (add to PlaybookBuilder palette):
```typescript
// In PlaybookNodeType:
| 'createNotification'  // Notification action — always Spaarke code

// Mapping:
'createNotification' → NodeType.Workflow → ActionType.CreateNotification (50)
```

#### 2. PlaybookSchedulerService (BackgroundService)

Reads playbooks tagged as `mode: notification` and runs them on schedule:

```csharp
public class PlaybookSchedulerService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await WaitUntilNextRun(ct);  // e.g., daily at 6am

            // Get all notification-mode playbooks
            var playbooks = await playbookService.GetPlaybooksByMode("notification");

            foreach (var playbook in playbooks)
            {
                // Get users subscribed to this playbook
                var subscribers = await GetSubscribedUsers(playbook.Id);

                foreach (var user in subscribers)
                {
                    var prefs = await GetUserPreferences(user.Id, playbook.Id);

                    // Create user-scoped run context
                    var context = new PlaybookRunContext
                    {
                        RunId = Guid.NewGuid(),
                        PlaybookId = playbook.Id,
                        UserId = user.Id,          // new field
                        UserPreferences = prefs,    // new field
                        TenantId = tenantId,
                        DocumentIds = Array.Empty<Guid>()  // no documents
                    };

                    // Use EXISTING orchestration engine
                    await orchestrationService.ExecutePlaybookAsync(
                        playbook, context, ct);
                }
            }
        }
    }
}
```

**Key point**: The scheduler doesn't execute nodes — it calls `PlaybookOrchestrationService.ExecutePlaybookAsync()`, which handles DAG construction, parallel batching, and node dispatch exactly as it does for analysis playbooks.

#### 3. PlaybookRunContext Extension

```csharp
// Add to existing PlaybookRunContext:
public Guid? UserId { get; set; }           // For notification mode
public Dictionary<string, object>? UserPreferences { get; set; }  // Parameter overrides
```

#### 4. Inline Notification Generation (User-Action Triggers)

For events that happen during BFF endpoint processing (document upload, analysis complete), notifications are created **inline** — not via playbook execution. This is a simple `NotificationService` call:

```csharp
// Shared service for inline (non-playbook) notification creation
public class NotificationService
{
    public async Task CreateAsync(
        string userId, string title, string body,
        string category, string priority = "Normal",
        string? regardingEntityType = null,
        string? regardingEntityId = null,
        string? actionUrl = null)
    {
        await dataverseService.CreateRecordAsync("appnotifications", new { ... });
    }
}
```

Added inline after existing operations:

| Event | BFF Location | Notification |
|-------|-------------|-------------|
| Document uploaded | `SdapEndpoints.cs` | "Contract_v3.pdf uploaded to Acme Corp" |
| Analysis completed | `AiToolEndpoints.cs` | "Risk analysis for Johnson IP completed — 3 high-priority items" |
| Email received | `IncomingCommunicationProcessor` | "New email from J. Smith: RE: Settlement Proposal" |
| Work assignment created | Dataverse create endpoint | "New work assignment: Review NDA for Smith Matter" |

These are **not** playbooks — they're simple one-line calls. The playbook engine handles the **scheduled, rule-based** notification generation (overdue tasks, budget alerts, AI similarity).

---

## Notification Playbook Examples

### Example 1: Tasks Due Soon (Deterministic, No AI)

```
┌──────────┐     ┌──────────────────┐     ┌────────────┐     ┌──────────────────┐
│  Start   │ ──► │  Query           │ ──► │  Condition  │ ──► │ Create           │
│  (scope: │     │  (OData: events  │     │  (count>0?) │     │ Notification     │
│  user's  │     │  where todoflag  │     │             │     │ per result item  │
│  matters)│     │  =true, due in   │     └─────┬───────┘     └──────────────────┘
└──────────┘     │  {{dueWindow}}d) │           │ false
                 └──────────────────┘           ▼
                                          (skip — no items)
```

User-configurable parameter: `dueWindow` (1, 2, 3, 5, 7 days) — stored in preferences, injected as template parameter.

### Example 2: Budget Burn Rate Alert (Computed, No AI)

```
┌──────────┐     ┌──────────────────┐     ┌────────────┐     ┌──────────────────┐
│  Start   │ ──► │  Calculate       │ ──► │  Condition  │ ──► │ Create           │
│  (scope: │     │  (sum invoices / │     │  (burn >    │     │ Notification     │
│  user's  │     │  matter budget)  │     │  threshold?)│     │ "Matter X has    │
│  matters)│     └──────────────────┘     └─────┬───────┘     │  used 85% of     │
└──────────┘                                    │ false       │  budget"         │
                                           (skip)            └──────────────────┘
```

User-configurable parameter: `threshold` (70%, 80%, 90%) — default 80%.

### Example 3: Similar Documents (AI, Semantic Search)

```
┌──────────┐     ┌──────────────────┐     ┌────────────┐     ┌──────────────────┐
│  Start   │ ──► │  AI Analysis     │ ──► │  Condition  │ ──► │ Create           │
│  (scope: │     │  (semantic search│     │  (confidence│     │ Notification     │
│  recent  │     │  against user's  │     │  > min?)    │     │ "Doc X is 87%    │
│  docs)   │     │  document index) │     └─────┬───────┘     │  similar to your │
└──────────┘     └──────────────────┘           │ false       │  Doc Y"          │
                                           (skip)            └──────────────────┘
```

User-configurable parameter: `minConfidence` (60%, 75%, 85%, 95%) — default 75%.

### Example 4: Future Workflow — Patent Filing Deadline

```
Start (trigger: sprk_matter.sprk_patentfilingdate changed)  ← event trigger (future)
  │
  ▼
Condition (sprk_patentfilingdate != null AND changed)
  │ true
  ▼
CreateTask (                                    ← existing executor (ActionType 20)
  subject: "File Non-Provisional — {{matter.name}}",
  duedate: "{{matter.sprk_patentfilingdate + P12M}}",
  regarding: "{{matter.id}}"
)
  │
  ▼
CreateNotification (                            ← new executor (ActionType 50)
  title: "Filing Deadline Created",
  body: "Non-provisional for {{matter.name}} due {{duedate}}"
)
```

**This playbook uses existing node executors** (`Condition`, `CreateTask`) plus the new `CreateNotification`. The only future addition is the **event trigger** (data-change detection) — the node execution is already supported.

---

## Unified Playbook Execution Modes

| Mode | Trigger | Engine | Output | Phase |
|------|---------|--------|--------|-------|
| **analysis** | User clicks "Run Analysis" | `PlaybookOrchestrationService` (existing) | `sprk_analysis` + SSE stream | Exists |
| **notification** | `PlaybookSchedulerService` (schedule) | `PlaybookOrchestrationService` (existing) | `appnotification` records | R1 |
| **notification** | Inline BFF call (user action) | `NotificationService` (simple, no playbook) | `appnotification` records | R1 |
| **workflow** | Event trigger (data change) | `PlaybookOrchestrationService` (existing) | `sprk_event`, field updates | Future |
| **composite** | Any trigger | `PlaybookOrchestrationService` | Multiple outputs (notify + create + update) | Future |

### R1 Architecture Decisions That Protect the Future

| Decision | Why It Matters for Workflow Mode |
|----------|--------------------------------|
| Notification rules stored as `sprk_analysisplaybook` records | Workflow rules use same entity, same builder |
| `CreateNotification` is an `INodeExecutor` (not special-cased) | Workflow nodes follow identical pattern |
| `PlaybookSchedulerService` calls `ExecutePlaybookAsync` | Event trigger service calls same method |
| `PlaybookRunContext` has `UserId` + `UserPreferences` | Workflow context extends same object |
| Playbook definitions support template parameters `{{param}}` | Workflow needs `{{filingDate + P12M}}` — same parser |
| `mode` field on `sprk_analysisplaybook` distinguishes types | Library UI filters by mode |

### What NOT to Build in R1

| Capability | Why Defer |
|------------|-----------|
| Event-driven triggers (real-time data change detection) | Requires BFF middleware for Dataverse webhook interception |
| Date arithmetic in templates (`{{date + P12M}}`) | R1 thresholds are simple numbers; expression parser is future |
| Conditional branching with complex expressions | R1 conditions are simple threshold checks |
| Approval steps / human-in-the-loop | Workflow feature, not notification feature |

---

## Dataverse `appnotification` Entity

Uses the **native Dataverse in-app notification system** — no custom notification entity needed.

| Capability | Native `appnotification` |
|------------|------------------------|
| Entity | System entity, already exists |
| MDA bell icon | Built-in — shows in Power App top nav bar |
| Read/dismiss tracking | Built-in (`isread`, dismiss action) |
| Toast popups | Built-in (configurable: timed, hidden) |
| Action buttons | Up to 2 per notification with navigation URLs |
| Auto-expiry | Built-in TTL (default 14 days) |
| Per-user targeting | `ownerid` field |
| Priority levels | High, Normal, Informational |
| Queryable | Standard `Xrm.WebApi.retrieveMultipleRecords` |

**Key insight**: The MDA bell icon handles real-time notification display for free. We build only:
1. **Notification generation** (playbook engine + inline BFF calls create `appnotification` records)
2. **Daily Digest Code Page** (grouped, narrative view of same records)

---

## Two-Level Narrative Format

### Level 1: Template-Based Narrative (R1)

Pre-written templates per category. No AI needed — string interpolation:

```
┌──────────────────────────────────────────────────────────────┐
│ Daily Briefing                     March 30, 2026      [✕]  │
│ Since yesterday (22 hours ago)              [⚙️ Settings]   │
├──────────────────────────────────────────────────────────────┤
│                                                              │
│ ⚠️ NEEDS ATTENTION                                           │
│ ─────────────────                                            │
│ You have 2 overdue tasks and 3 coming due in the next        │
│ 3 days.                                                      │
│                                                              │
│  • Review NDA — Acme Corp is 2 days overdue         [Open →] │
│  • File Response Brief — Smith IP is 1 day overdue  [Open →] │
│  • Complete Compliance Checklist is due tomorrow     [Open →] │
│  • Send Engagement Letter is due April 1             [Open →] │
│  • Review Invoice #4521 is due April 2               [Open →] │
│                                                              │
│ 📄 NEW DOCUMENTS                                             │
│ ─────────────────                                            │
│ 4 documents were added to your matters.                      │
│                                                              │
│  • Smith_Lease_Amendment.pdf → Acme Corp             [Open →] │
│  • Q1_Compliance_Report.xlsx → Q1 Audit Project      [Open →] │
│  • NDA_Redline_v3.docx → Smith IP                    [Open →] │
│  • Invoice_4521.pdf → Acme Corp                      [Open →] │
│                                                              │
│ 📧 EMAILS                                                    │
│ ─────────────────                                            │
│ 2 new emails related to your matters.                        │
│                                                              │
│  • RE: Settlement Proposal — from J. Smith           [Open →] │
│  • Engagement Letter Draft — from K. Williams        [Open →] │
│                                                              │
│ 🤖 AI INSIGHTS                                               │
│ ─────────────────                                            │
│ We found items that may be relevant to your work.            │
│                                                              │
│  • Contract_v3.pdf (Acme Corp) is 87% similar to     [Open →] │
│    your NDA_Template.docx                                    │
│  • New matter "Patent Infringement — Globex" is      [Open →] │
│    similar to your "Patent Defense — Initech"                │
│                                                              │
│ 🔄 ACTIVITY                                                  │
│ ─────────────────                                            │
│ 3 updates on your matters and projects.                      │
│                                                              │
│  • Acme Corp — status changed to "Active"            [Open →] │
│  • Q1 Audit Project — new work assignment added      [Open →] │
│  • Smith IP — outside counsel assigned               [Open →] │
│                                                              │
│            [✓ Mark All Read]  [Open Workspace]  [Dismiss]    │
└──────────────────────────────────────────────────────────────┘
```

### Level 2: AI-Generated Briefing Summary (R1 optional / R2)

An LLM reads all notification items and produces a contextual, prioritized summary:

```
┌──────────────────────────────────────────────────────────────┐
│ 🤖 AI BRIEFING                                               │
│                                                              │
│ The Acme Corp matter needs attention — the NDA review is     │
│ 2 days overdue and a new lease amendment was uploaded         │
│ yesterday. You also have a compliance checklist due           │
│ tomorrow for the Q1 Audit Project.                           │
│                                                              │
│ Of note: a contract uploaded to Acme Corp is highly similar  │
│ to your NDA template — worth reviewing for consistency.      │
│                                                              │
│ 3 items need immediate action:                               │
│  • Review NDA — Acme Corp (2 days overdue)           [Open →] │
│  • Complete Compliance Checklist (due tomorrow)       [Open →] │
│  • Review similar contract — Acme Corp                [Open →] │
├──────────────────────────────────────────────────────────────┤
│ [Full details below...]                                      │
```

**Implementation**: BFF endpoint `POST /api/ai/daily-briefing/summarize`
- Input: structured notification data (not raw text)
- Uses existing `AiToolService` + Azure OpenAI
- No semantic index needed — LLM reformats structured data into prose

---

## User Customization

### Channel Preferences with Configurable Parameters

Each notification playbook maps to a user-facing "channel" with on/off toggle and playbook-specific parameters:

```
┌──────────────────────────────────────────────────────────────┐
│ Daily Briefing Settings                                [✕]   │
│                                                              │
│ GENERAL                                                      │
│ ────────                                                     │
│ Show on workspace launch     [=====●] On                     │
│ AI Briefing summary          [=====●] On                     │
│                                                              │
│ CHANNELS                                                     │
│ ────────                                                     │
│                                                              │
│ [=====●] Upcoming Tasks                                      │
│          Due within  [ 3 days ▾]                              │
│                                                              │
│ [=====●] Overdue Tasks                                       │
│          (no parameters — always shows all overdue)           │
│                                                              │
│ [=====●] New Documents                                       │
│          From  [ Last 24 hours ▾]                             │
│                                                              │
│ [=====●] New Emails                                          │
│          From  [ Last 24 hours ▾]                             │
│                                                              │
│ [=====●] New Events                                          │
│          From  [ Last 24 hours ▾]                             │
│                                                              │
│ [=====●] Matter & Project Activity                           │
│          From  [ Last 24 hours ▾]                             │
│                                                              │
│ [=====●] Work Assignments                                    │
│          From  [ Last 24 hours ▾]                             │
│                                                              │
│ [=====●] AI Similar Documents                                │
│          Min. similarity  [ 75% ▾]                            │
│                                                              │
│ [=====●] AI Similar Matters                                  │
│          Min. similarity  [ 75% ▾]                            │
│                                                              │
│                              [Cancel]  [Save]                │
└──────────────────────────────────────────────────────────────┘
```

### Parameter Definitions

| Channel (Playbook) | Parameter | Options | Default |
|---------------------|-----------|---------|---------|
| Upcoming Tasks | Due within | 1 day, 2 days, 3 days, 5 days, 7 days | 3 days |
| Overdue Tasks | (none) | Always shows all overdue | — |
| New Documents | Time window | 12h, 24h, 48h, 7 days | 24h |
| New Emails | Time window | 12h, 24h, 48h, 7 days | 24h |
| New Events | Time window | 12h, 24h, 48h, 7 days | 24h |
| Matter/Project Activity | Time window | 12h, 24h, 48h, 7 days | 24h |
| Work Assignments | Time window | 12h, 24h, 48h, 7 days | 24h |
| AI Similar Documents | Min. confidence | 60%, 75%, 85%, 95% | 75% |
| AI Similar Matters | Min. confidence | 60%, 75%, 85%, 95% | 75% |

### Preference Storage

Uses existing `sprk_userpreference` entity:

```json
{
  "autoPopup": true,
  "aiBriefing": true,
  "channels": {
    "tasks-due": { "enabled": true, "dueWithinDays": 3 },
    "tasks-overdue": { "enabled": true },
    "new-documents": { "enabled": true, "timeWindow": "24h" },
    "new-emails": { "enabled": true, "timeWindow": "24h" },
    "new-events": { "enabled": true, "timeWindow": "24h" },
    "matter-activity": { "enabled": true, "timeWindow": "24h" },
    "project-activity": { "enabled": true, "timeWindow": "24h" },
    "assignments": { "enabled": true, "timeWindow": "24h" },
    "similar-documents": { "enabled": true, "minConfidence": 0.75 },
    "similar-matters": { "enabled": true, "minConfidence": 0.75 }
  }
}
```

Parameters are injected into playbook execution as template parameters (`{{dueWithinDays}}`, `{{timeWindow}}`, `{{minConfidence}}`).

### Design Decisions (Resolved)

**1. Playbook Type Discrimination**: Uses existing `sprk_playbooktype` field (OptionSet) on `sprk_analysisplaybook`. Notification playbooks use value `Notification (2)`. Playbook Library and scheduler filter by this field.

**2. Schedule Configuration**: Stored in `sprk_configjson` on the playbook record as JSON: `{ "schedule": { "frequency": "daily", "time": "06:00" } }`. No schema changes needed — consistent with how other playbook config is stored.

**3. Subscription Model**: **Opt-out**. All notification playbooks with `sprk_playbooktype = Notification (2)` run for all users by default. User preferences in `sprk_userpreference` store only overrides — disabled channels and parameter customizations. New playbooks automatically reach all users without requiring subscription.

**4. Notification Deduplication**: Idempotency check inside `CreateNotificationNodeExecutor`. Before creating an `appnotification`, the executor queries: "Does an unread notification already exist for this user + regarding record + category?" If yes, skip creation. This prevents duplicate "Task X is due tomorrow" notifications when the scheduler runs hourly.

```csharp
// In CreateNotificationNodeExecutor.ExecuteAsync:
var existing = await dataverseService.QueryAsync("appnotifications",
    $"$filter=_ownerid_value eq '{userId}' " +
    $"and isread eq false " +
    $"and contains(data, '{regardingEntityId}') " +
    $"and contains(data, '{category}')&$top=1");

if (existing.Count > 0)
{
    return new NodeOutput { Text = "Notification already exists, skipped" };
}
```

**5. Existing NotificationPanel**: **Remove entirely**. The mock `NotificationPanel` component in LegalWorkspace (with 8 hardcoded items) will be removed. The MDA native bell icon handles real-time notifications. The Daily Briefing Code Page handles the digest view. No third notification surface needed.

**6. AI Briefing**: **Included in R1**. One BFF endpoint + one prompt. Low effort (~2-4 hours), high value. Data is already structured — LLM reformats into contextual narrative.

---

## Clearing / Dismissing Items

Since items are `appnotification` records, clearing uses built-in capabilities:

| Action | Implementation | Effect |
|--------|---------------|--------|
| **Mark read** | `Xrm.WebApi.updateRecord("appnotification", id, { isread: true })` | Blue dot disappears in MDA bell AND digest |
| **Dismiss item** | Delete or mark read | Item hidden from digest |
| **Mark all read** | Batch update all unread for user | All blue dots cleared |
| **Clear channel** | Batch update all in category | Channel shows "No new items" |

The Daily Digest renders only `isread = false` notifications by default, with a toggle to "Show all recent" (includes read items).

---

## Component Architecture

```
DailyBriefing Code Page (sprk_dailyupdate)
├── React 19 + Vite single-file build
├── FluentProvider (theme from shared themeStorage)
│
├── DailyBriefingApp.tsx
│   ├── useBriefingData(webApi, userId, preferences)
│   │   ├── Reads user preferences from sprk_userpreference
│   │   ├── Queries appnotification records: unread, grouped by category
│   │   ├── Applies channel parameters (time window, confidence threshold)
│   │   ├── Returns { channels: ChannelResult[], since: Date, isLoading }
│   │   └── Uses Promise.allSettled (one channel failure doesn't crash digest)
│   │
│   ├── useAiBriefing(channels, enabled)
│   │   ├── POST /api/ai/daily-briefing/summarize (if enabled)
│   │   ├── Returns { summary: string, priorityItems: DigestItem[] }
│   │   └── Graceful fallback if AI unavailable (just skip AI section)
│   │
│   ├── BriefingHeader
│   │   ├── Title + date
│   │   ├── "Since yesterday" subtitle with relative time
│   │   └── Settings gear → opens PreferencesPanel
│   │
│   ├── AiBriefingSection (optional, top of page)
│   │   ├── AI-generated narrative summary
│   │   └── Priority action items with links
│   │
│   ├── ChannelSection × N (one per subscribed channel with items)
│   │   ├── Category header with icon + count badge
│   │   ├── Template narrative sentence ("You have 3 tasks due...")
│   │   ├── AI Insight badge (for similarity channels)
│   │   └── DigestItem × N
│   │       ├── Title (record name)
│   │       ├── Subtitle (matter/project, date, sender, confidence %)
│   │       ├── [Open →] link to source record
│   │       └── [✓] mark read button (per item)
│   │
│   ├── EmptyState ("You're all caught up!")
│   │
│   └── Footer
│       ├── [✓ Mark All Read]
│       ├── [Open Workspace]
│       └── [Dismiss]
│
└── PreferencesPanel (inline, slides in from right)
    ├── General: auto-popup toggle, AI briefing toggle
    ├── Channel toggles with per-channel parameter dropdowns
    └── [Save] → persists to sprk_userpreference
```

---

## Launch Behavior

| Trigger | How | User Control |
|---------|-----|--------------|
| **Auto on workspace load** | LegalWorkspace checks preference → opens dialog | User enables/disables in preferences |
| **Manual** | Command bar button "Daily Briefing" | Always available |
| **MDA bell icon** | Native `appnotification` bell in MDA shell | Always available (free) |
| **Deep link** | `Xrm.Navigation.navigateTo({ webresourceName: "sprk_dailyupdate" })` | For M365 Copilot handoff |

Auto-popup fires **once per session** (tracked via `sessionStorage` flag `spaarke-daily-briefing-shown`).

---

## BFF API Endpoints

| Method | Path | Purpose | Phase |
|--------|------|---------|-------|
| `POST` | `/api/ai/daily-briefing/summarize` | AI-generated briefing from notification data | R1 (optional) |

Notification **generation** happens via:
- **Inline**: `NotificationService.CreateAsync()` calls in existing BFF endpoints (user-action triggers)
- **Scheduled**: `PlaybookSchedulerService` runs notification-mode playbooks (time-based + AI triggers)

No new CRUD endpoints — notifications are read/updated via `Xrm.WebApi` directly in the Code Page.

---

## BFF API & Azure Infrastructure

### BFF Changes

| Component | Type | Purpose |
|-----------|------|---------|
| `NotificationService` | New service (DI singleton) | Shared service for creating `appnotification` records inline |
| `CreateNotificationNodeExecutor` | New `INodeExecutor` | Playbook node that creates `appnotification` records |
| `PlaybookSchedulerService` | New `BackgroundService` | Scheduled execution of notification-mode playbooks |
| `PlaybookRunContext` extension | Modification | Add `UserId`, `UserPreferences` fields |
| `NodeExecutorRegistry` | Modification | Register `CreateNotification` (ActionType 50) |
| `ActionType` enum | Modification | Add `CreateNotification = 50` |
| `POST /api/ai/daily-briefing/summarize` | New endpoint | AI briefing narrative generation (optional) |
| Inline calls in existing endpoints | Modification | Add `NotificationService.CreateAsync()` after document upload, analysis complete, email received |

### Scheduling: BackgroundService vs Azure

| Option | How | Pros | Cons |
|--------|-----|------|------|
| **BackgroundService** (ADR-001) | `PlaybookSchedulerService` runs inside BFF API process | No new Azure resources, follows ADR-001, simple | Tied to App Service lifecycle — restarts lose schedule state |
| **Azure Timer Trigger** (Azure Functions) | Separate Azure Function on CRON schedule | Reliable, independent lifecycle | **Violates ADR-001** (no Azure Functions) |
| **Azure WebJob with CRON** | WebJob in same App Service | Runs alongside BFF, CRON expression | Separate deployment, maintenance |
| **Hangfire** | In-process scheduler library | Reliable, persistent schedule, dashboard | Adds dependency, needs storage (Redis or SQL) |

**Recommendation: BackgroundService with resilience** (follows ADR-001):

```csharp
public class PlaybookSchedulerService : BackgroundService
{
    private readonly TimeSpan _runInterval = TimeSpan.FromHours(1);
    private readonly IServiceScopeFactory _scopeFactory;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var orchestrator = scope.ServiceProvider
                    .GetRequiredService<IPlaybookOrchestrationService>();

                await RunScheduledPlaybooks(orchestrator, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scheduler run failed, will retry next interval");
            }

            await Task.Delay(_runInterval, ct);
        }
    }

    private async Task RunScheduledPlaybooks(
        IPlaybookOrchestrationService orchestrator,
        CancellationToken ct)
    {
        var playbooks = await _playbookService
            .GetPlaybooksByModeAsync("notification");

        foreach (var playbook in playbooks)
        {
            var schedule = playbook.GetScheduleConfig();

            // Check if this playbook should run now
            if (!ShouldRunNow(playbook.Id, schedule))
                continue;

            var subscribers = await GetSubscribedUsers(playbook.Id);

            // Process users in parallel (throttled)
            await Parallel.ForEachAsync(
                subscribers,
                new ParallelOptions { MaxDegreeOfParallelism = 5, CancellationToken = ct },
                async (user, token) =>
                {
                    var prefs = await GetUserPreferences(user.Id, playbook.Id);
                    var context = new PlaybookRunContext
                    {
                        RunId = Guid.NewGuid(),
                        PlaybookId = playbook.Id,
                        UserId = user.Id,
                        UserPreferences = prefs,
                        TenantId = _tenantId,
                        DocumentIds = Array.Empty<Guid>()
                    };

                    await orchestrator.ExecutePlaybookAsync(playbook, context, token);
                });

            RecordLastRun(playbook.Id);
        }
    }
}
```

**Schedule state persistence**: Last-run timestamps stored in Dataverse (`sprk_systemsetting` or `sprk_analysisplaybook` custom field) — survives App Service restarts. On startup, the scheduler reads last-run timestamps and skips playbooks that already ran today.

### Azure Resources Required

| Resource | Needed? | Notes |
|----------|---------|-------|
| App Service (BFF API) | Exists | `PlaybookSchedulerService` runs inside existing BFF process |
| Azure OpenAI | Exists | For AI briefing endpoint and AI similarity playbooks |
| AI Search | Exists | For semantic similarity playbooks (R2) |
| Redis Cache | Exists | Optional — cache schedule state for faster startup |
| **New resources** | **None** | No new Azure resources needed for R1 |

### App Service Configuration

The BFF App Service must be configured to **not idle** (so BackgroundService keeps running):

| Setting | Value | Why |
|---------|-------|-----|
| `WEBSITE_ALWAYS_ON` | `true` | Prevents App Service from idling and stopping BackgroundService |
| `NOTIFICATION_SCHEDULER_ENABLED` | `true` | Feature flag to enable/disable scheduler |
| `NOTIFICATION_SCHEDULER_INTERVAL_HOURS` | `1` | How often the scheduler checks for playbooks to run |
| `NOTIFICATION_SCHEDULER_MAX_PARALLELISM` | `5` | Max concurrent user evaluations |

These are environment variables (App Service Configuration) — no hardcoded values per BYOK/multi-tenant requirement from M365 project.

---

## Future Intelligence Channels (R2+)

| Category | Channel | Type | Notes |
|----------|---------|------|-------|
| **Financial** | Budget burn rate alert | Computed | Query invoices, calculate ratio, threshold check |
| **Financial** | Invoice anomaly detection | AI | Compare invoice to historical average |
| **Financial** | Unbilled work alert | Computed | Work assignments with no associated invoice in 30+ days |
| **Risk** | Cascading deadline risk | Computed | Overdue task blocks downstream task due soon |
| **Risk** | Contract expiration | Deterministic | Document expiry date within configured window |
| **Risk** | Aging/stale matters | Deterministic | No activity in 60+ days |
| **Compliance** | Missing required documents | Deterministic | Document type checklist vs actual documents |
| **Compliance** | Approval bottleneck | Deterministic | Invoices pending approval > 48 hours |
| **Compliance** | Conflict check | AI | New matter parties match existing matter parties |
| **Workload** | Overloaded user | Computed | Open task count vs team average |
| **AI** | Communication sentiment shift | AI | Tone change in recent emails for a matter |
| **AI** | Document version drift | AI | Significant changes between document versions |
| **External** | Court filings | Integration | External API (PACER, court RSS) — R3+ |
| **External** | Regulatory updates | Integration | External feed — R3+ |

Each of these is a **playbook definition** deployed to `sprk_analysisplaybook` with `mode: notification`. No code changes needed — just new playbook definitions using existing node executors.

---

## Scope

### In Scope (R1)
- `CreateNotificationNodeExecutor` (ActionType 50) — new `INodeExecutor`
- `createNotification` canvas type in PlaybookBuilder palette
- `PlaybookSchedulerService` (BackgroundService) — scheduled playbook execution
- `PlaybookRunContext` extension (`UserId`, `UserPreferences`)
- Uses existing `sprk_playbooktype` field (Notification = 2) to distinguish playbook types
- `NotificationService` — shared BFF service for inline notification creation
- Inline notification generation in existing BFF endpoints (document upload, analysis complete, email received, assignment created)
- Notification deduplication (idempotency check in `CreateNotificationNodeExecutor`)
- 7 deterministic notification playbooks deployed:
  1. Tasks overdue
  2. Tasks due soon (configurable window: 1-7 days)
  3. New documents on user's matters
  4. New emails related to user's matters
  5. New events on user's matters/projects
  6. Matter/project activity (status changes, modifications)
  7. New work assignments for user
- `sprk_dailyupdate` Code Page (React 19, Vite single-file)
- AI briefing summary — `POST /api/ai/daily-briefing/summarize` endpoint + prompt
- User-customizable channel preferences (opt-out model — all playbooks active by default, users override)
- Template-based narrative format (Level 1) + AI narrative (Level 2)
- Clear/dismiss items (mark read, mark all read)
- Auto-popup on workspace launch (configurable, once per session)
- Remove mock NotificationPanel from LegalWorkspace (replace with MDA native bell)
- Dark mode support

### In Scope (R2)
- AI semantic similarity playbooks (similar documents, similar matters) — architecture supports this via AI nodes in notification playbooks
- Financial intelligence playbooks (budget burn rate, invoice anomaly, aging matters)
- Playbook Library UI filter by `sprk_playbooktype` (Analysis / Notification)
- Real-time SignalR for instant notification delivery
- Badge count on workspace nav

### Out of Scope
- Event-driven triggers (data change detection) — requires BFF middleware, future project
- Date arithmetic in templates (`{{date + P12M}}`) — workflow mode feature
- Custom user-defined notification rules — R3 (build your own playbook)
- Shared/team digests
- Custom notification panel in workspace (use MDA native bell)

---

## Technical Constraints

### Applicable ADRs
- **ADR-001**: BackgroundService for scheduled execution (no Azure Functions)
- **ADR-006**: Code Page for standalone dialog (not PCF)
- **ADR-012**: Shared components from `@spaarke/ui-components`
- **ADR-013**: AI features extend BFF (AI briefing endpoint, notification playbooks)
- **ADR-021**: Fluent UI v9 exclusively; semantic tokens; dark mode support

### MUST Rules
- MUST use native `appnotification` entity — no custom notification table
- MUST use existing `PlaybookOrchestrationService` for notification playbook execution — no separate engine
- MUST register `CreateNotificationNodeExecutor` via `NodeExecutorRegistry[ActionType]` pattern
- MUST store notification playbooks as `sprk_analysisplaybook` records with `mode: notification`
- MUST generate inline notifications via `NotificationService` (not playbooks) for user-action triggers
- MUST use `Xrm.Navigation.navigateTo` to open Daily Digest as dialog
- MUST query `appnotification` via `Xrm.WebApi.retrieveMultipleRecords`
- MUST fetch channels via `Promise.allSettled` — individual failures show inline error
- MUST label AI-identified items clearly (confidence badge, "AI Insight" indicator)
- MUST support dark mode via unified theme utility

### Architecture Reference Documents
- `docs/architecture/playbook-architecture.md` — Playbook engine internals, node executor framework, execution flow
- `docs/guides/JPS-AUTHORING-GUIDE.md` — JPS authoring, prompt schema, playbook design
- `docs/guides/SCOPE-CONFIGURATION-GUIDE.md` — Scope configuration, pre-fill, builder
- `docs/architecture/ai-document-summary-architecture.md` — Document creation flows (notification trigger points)
- `docs/architecture/AI-ARCHITECTURE.md` — AI platform overview

---

## Success Criteria

1. [ ] `CreateNotificationNodeExecutor` registered in `NodeExecutorRegistry` (ActionType 50)
2. [ ] `createNotification` node available in PlaybookBuilder canvas palette
3. [ ] `PlaybookSchedulerService` runs notification-mode playbooks on daily schedule
4. [ ] BFF creates `appnotification` records inline for document uploads, analysis completions, emails, assignments
5. [ ] 7 deterministic notification playbooks deployed and generating notifications
6. [ ] Notifications appear in MDA native bell icon (free, no custom UI needed)
7. [ ] Daily Briefing dialog opens on workspace launch (if user has auto-popup enabled)
8. [ ] Dialog shows narrative TL;DR grouped by subscribed channels
9. [ ] Each item links to the correct source record
10. [ ] Users can configure channel toggles and parameters (due window, time window, confidence)
11. [ ] Users can mark items read / mark all read / dismiss
12. [ ] Preferences persist to Dataverse (cross-device)
13. [ ] AI briefing summary generates contextual, prioritized narrative
14. [ ] AI similarity playbooks surface related documents/matters with confidence scores (R2)
15. [ ] Empty state displays when no new activity
16. [ ] Dark mode renders correctly
17. [ ] New notification playbooks can be deployed without code changes
18. [ ] Mock NotificationPanel removed from LegalWorkspace
19. [ ] Duplicate notifications are prevented (idempotency check)
20. [ ] Opt-out model works — new playbooks auto-apply to all users

---

## Dependencies

### Prerequisites
- `sprk_userpreference` entity (exists)
- `appnotification` entity (system, exists)
- `sprk_analysisplaybook` entity (exists)
- `PlaybookOrchestrationService` (exists)
- `NodeExecutorRegistry` (exists)
- Azure OpenAI endpoint (exists — for AI briefing and AI similarity)
- AI Search semantic index (exists — for similarity playbooks in R2)

### Related Projects
- `spaarke-mda-darkmode-theme-r2` — unified theme for dark mode support
- `spaarke-workspace-user-configuration-r1` — workspace header could include Daily Briefing button
- `ai-m365-copilot-integration-r1` — Copilot can suggest "Open Daily Briefing"
- `events-smart-todo-kanban-r2` — SmartToDo independence ensures clean separation

---

*Last updated: March 30, 2026*
