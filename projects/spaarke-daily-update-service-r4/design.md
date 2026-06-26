# Daily Briefing — Functional Completeness (R4)

> **Project**: spaarke-daily-update-service-r4
> **Status**: Design (draft for review)
> **Predecessors**:
> - [`spaarke-daily-update-service-r2`](../spaarke-daily-update-service-r2/) — Pattern D widget migration + consumer fixes (shipped)
> - [`spaarke-daily-update-service-r3`](../spaarke-daily-update-service-r3/) — Read-state decoupling + TTL fix + 3 actions ([draft PR #451](https://github.com/spaarke-dev/spaarke/pull/451))
> - [`spaarke-platform-foundations-r3`](../spaarke-platform-foundations-r3/) — `MembershipResolverService` + `LookupUserMembership` node executor (shipped)
> **Created**: 2026-06-25
> **Author**: UAT-driven architectural review session, 2026-06-25

---

## Executive Summary

UAT of R3 in spaarkedev1 confirmed the headline R3 win (widget renders content, no longer shows EmptyState when notifications exist) but also surfaced that the Daily Briefing widget is structurally incomplete: dead preferences, hallucinating AI narration, missing producer-side membership integration, stub playbooks producing no notifications, and architectural inconsistencies between Daily Briefing's hardcoded `/narrate` endpoint and the rest of Spaarke's playbook-driven AI architecture.

R4 closes these gaps in a single project shipped as phased PRs across three coordinated workstreams:

- **Workstream 0 — JPS Deployment Layer**: ensure JPS primitives are deployed as Dataverse data (not just C# code). This includes deploying the missing `sprk_analysisaction` row for ActionType 52 (LookupUserMembership), authoring + deploying the new `daily-briefing-narrate` JPS playbook + its Action/Skill/Tool rows, and verifying/updating the existing 7 notification playbook `sprk_configjson` to actually consume the membership Action.
- **Workstream 1 — Producer (Notifications)**: enrich notification payloads, migrate playbooks to membership-aware queries, implement the 2 stub playbooks, expand task playbooks to cover membership-scope (not just ownership).
- **Workstream 2 — Consumer (Widget + `/narrate` playbook)**: wire preferences end-to-end, dispatch `/narrate` through the JPS playbook engine (replacing the hardcoded BFF endpoint), fix `/narrate` caching, redesign per-item UX with three-dot overflow menu, fix link navigation.

**Critical architecture point**: JPS is *data, not code* (per [`docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md`](../../docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md)). Source-code changes to `INodeExecutor` enums and `NodeExecutor` classes are necessary but not sufficient — the corresponding `sprk_analysisaction` and `sprk_analysisplaybook` rows must be deployed to every environment. R4 makes this a first-class concern, following the JPS skills and patterns (`jps-action-create`, `jps-playbook-design`, `jps-playbook-audit`, `jps-validate`, `jps-scope-refresh`). This was the gap Insights Engine Phase 1 hit ("Phase 1 deployment didn't wire `sprk_analysisaction` rows for the new ActionTypes — a JPS-convention gap"); R4 explicitly avoids repeating it.

R4 builds on R3's read-state work and platform-foundations-R3's membership service. Estimated effort: 50–70 hours engineering across all workstreams; ~5 PRs into the work branch before merge to master.

---

## Problem Statement

### What the user observed in UAT (2026-06-24/25)

UAT confirmed five categories of issues, all with code evidence:

| # | Issue | Surface | Root cause |
|---|---|---|---|
| **P1** | AI narration hallucinates firm names not in user's data (Johnson & Lee LLP, GreenTech Solutions, Davis v. Metro Transit when actual data is all CMRCL-441482) | `/narrate` prompt | Hardcoded example names baked into prompt as instructional examples; no grounding constraint; no system message; non-zero temperature; FR-17 validates only IDs, not names |
| **P2** | Activity Notes section disappears after Check/Remove actions or browser refresh | Widget + `/narrate` cache | `useBriefingNarration` only fetches `/narrate` once per session (`hasFetchedRef`); `ActivityNotesSection` returns `null` if `channelNarratives.length === 0` |
| **P3** | Preference settings don't work (Recency window doesn't filter, Due-soon window does nothing, AI confidence threshold is meaningless) | Preferences UI | 4 of 5 settings are dead code: stored on save, never consumed by any query or BFF call |
| **P4** | Overdue Tasks shows tasks "for CMRCL-441482" that aren't actually regarding-set to that matter; record link doesn't navigate | Producer + widget link | `notification-tasks-overdue` playbook queries by `ownerid eq-userid` with no matter join; `customData` carries only `actionUrl` + `dueDate` (no `regardingName`, no matter context); AI cross-channel hallucination produces fictional matter associations |
| **P5** | Per-item action buttons confusing (5 icons, 2 checkmarks look identical) | Widget UX | R3 added 3 new action icons inline; existing "Add to To Do" and "Dismiss" also inline; visual collision of checkmark icons |

### Architectural debt revealed

Beyond the visible bugs, UAT investigation revealed the Daily Briefing has structural inconsistencies with the rest of Spaarke's AI architecture:

1. **`/narrate` is a hardcoded BFF endpoint, not a JPS playbook** — The prompts are C# string literals embedded in `DailyBriefingEndpoints.cs:452` and `:526`. The endpoint calls Azure OpenAI directly via `IOpenAiClient.GetCompletionAsync`. There are no actions, skills, tools, or knowledge nodes. This contradicts the playbook-based AI architecture used throughout the rest of Spaarke (Insights Engine, notification producers, analysis playbooks).

2. **`customData` is minimal** — Producer writes only `actionUrl` and `dueDate`. The widget's `parseNotificationData` expects 9 fields and null-coalesces the rest. The LLM gets sparse input and confabulates the missing context.

3. **Membership service is shipped but underused** — R3 platform-foundations delivered `MembershipResolverService` and `LookupUserMembershipNodeExecutor` (ActionType 52). 3 of 7 notification playbooks were migrated. 2 use `ownerid` only. 2 are stubs.

4. **Preferences hooks have no downstream wiring** — R2 set up the three-hook decomposition (`useBriefingNotifications` / `useBriefingPreferences` / `useBriefingActions`) but never connected preferences to queries or BFF calls.

---

## Architecture Context

### Current data flow

```
PRODUCER (BFF playbook engine, scheduled per-user fan-out):

  PlaybookSchedulerService iterates SystemUsers
    For each user U:
      For each active playbook P (currently 7):
        Run P with NodeExecutionContext.UserId = U.id

  Example for notification-new-documents (migrated):
    Node 1: Start
    Node 2: LookupUserMembership(entityType=sprk_matter) → matterIds[]
    Node 3: QueryDataverse(FetchXml: documents linked to matterIds)
    Node 4: Condition (count > 0)
    Node 5: CreateNotification (iterateItems=true)
              ownerid = U.id (the iterating user)
              customData = { actionUrl, dueDate }  ← MINIMAL TODAY

  Example for notification-tasks-overdue (NOT migrated):
    Node 1: Start
    Node 2: QueryDataverse(FetchXml: tasks where ownerid eq-userid)
    Node 3: CreateNotification → ownerid = U.id

DATAVERSE: appnotification table
  - One row per (User × source-record)
  - Owner-scoped Dataverse row security (User-A cannot see User-B's rows)
  - data field is Memo (~1MB cap; ~2KB headroom plenty for membership context)

WIDGET (browser, on-demand):
  Xrm.WebApi.retrieveMultipleRecords('appnotification', …)
    → auto-scopes to ownerid = current user
  Group by customData.category → channels[]
  POST /api/ai/daily-briefing/narrate (all channels + items in one payload)
    → BFF builds prompt as concatenated C# string, calls LLM directly
    → Returns TL;DR + per-channel bullets
  Render: <TldrSection /> + <ActivityNotesSection channelNarratives + channels />
```

### Target data flow (R4)

```
PRODUCER (enriched):
  Same per-user fan-out + LookupUserMembership pattern
  All 7 playbooks use membership (including tasks-overdue, tasks-due-soon)
  customData enriched:
    {
      category, priority, actionUrl, dueDate,
      regardingName, regardingEntityType, regardingId,
      viaMatter: {
        id, name,
        memberships: [{ role: "owner" }, { role: "assignedAttorney" }]
      },
      source: { entityType, id, modifiedOn, owningUser }
    }
  2 stub playbooks (matter-activity, work-assignments) now functional

WIDGET (consumer):
  Same auto-scoped query (no membership awareness needed at widget)
  Preferences wired end-to-end:
    timeWindow → fetchNotifications $filter on createdon
    dueWithinDays → fetchNotifications $filter on customData.dueDate (or BFF filter)
    disabledChannels → server-side $filter on customData.category
    autoPopup → workspace launcher checks this
    minConfidence → REMOVED (vestigial; data is deterministic)
  /narrate replaced with JPS playbook invocation:
    POST /api/ai/daily-briefing/narrate-playbook (or extend existing /narrate to dispatch to playbook engine)
    Playbook executes:
      Skill: Grounded summarization (system message + temp 0 + strict instruction
             to use ONLY names/entities present in payload)
      Tool: EntityNameValidator (FR-17 extended to scrub names from TL;DR + bullets)
      Knowledge: Channel registry (deterministic)
  /narrate refetches when notification data changes (no hasFetchedRef cache)
  ActivityNotesSection: renders narratives when present; falls back to raw channel
    item list when narrative is unavailable (defense in depth — per Q4 owner
    preference, narration is integral so failure mode should be rare)
  UX: per-item three-dot overflow menu (match semantic search PCF pattern)
  Link clicks: graceful 403 fallback if user lacks Dataverse read on regarding record
```

### Membership architecture (confirmed via research)

The membership architecture is shipped and orthogonal to Dataverse UAC:

- **Dataverse UAC** controls record CRUD permissions (security roles, ownership, BU hierarchy)
- **Spaarke Membership** answers "which records is this user logically associated with via business semantics" — owner, assignedAttorney, assignedParalegal, etc.
- The two overlap at owner fields but membership extends to custom Lookup fields
- Membership is computed-at-request (Phase 1A) via field discovery + FetchXml; Redis-cached 5 min per resolved query
- Phase 2 (junction table + event-driven sync) is built but feature-gated OFF (Service Bus topic pending operator deploy)

**Identity normalization (6-pronged)**: A single `systemuserid` resolves to `{SystemUserId, ContactId, TeamIds[], BusinessUnitId, AccountId, OrganizationIds[]}`. The membership service finds records where ANY of these identities appears in ANY membership-bearing Lookup field. This implicitly handles "I'm a member because I'm the assigned attorney" (where the Lookup field points to my Contact identity).

**Contact-only members (no SystemUser, no AAD oid match)**: Receive no notifications. This is a documented R4 limitation, accepted per Q&A D-A (skip silently). External attorneys without tenant accounts are out of scope for Daily Briefing notifications; if email notifications are needed for that population, that's a separate R5 (or later) project.

### `/narrate` as a JPS playbook (architectural correction)

Per Q&A D-E, R4 converts `/narrate` from a hardcoded BFF endpoint to a JPS playbook executed by the playbook engine. This aligns Daily Briefing with the rest of Spaarke's AI architecture and provides:

- **Configurable prompts** — no recompile to iterate on prompt quality
- **Composable nodes** — skills, tools, knowledge nodes are interchangeable
- **Consistent observability** — playbook run history, retries, idempotency apply
- **Future RAG path** — adding an AI Search "matter context" knowledge node later (similar to Insights Engine's `spaarke-files-index` grounding pattern) is a node addition, not a refactor

Playbook composition (initial R4 scope):

| Node | Type | Purpose |
|---|---|---|
| Start | n/a | Receives payload from widget (channels with items) |
| LoadKnowledge | Knowledge | Channel registry (static metadata) |
| GenerateTldr | Skill | LLM call: TL;DR with grounded prompt (system msg + temp 0 + strict "use only names from input" instruction) |
| GenerateChannelNarratives | Skill (parallel per channel) | LLM call: per-channel bullets with same grounding rules |
| ValidateEntityNames | Tool | Extends FR-17: scrubs LLM output to remove names not present in input payload; logs warning |
| ReturnResponse | n/a | Assembled `{tldr, channelNarratives, generatedAtUtc}` |

**Deferred to R5** (per Q&A D-E): AI Search "matter context" knowledge node that lets the playbook enrich bullets with grounded matter context (similar to Insights Engine). Useful if Daily Briefing should surface insights like "this matter took 6 weeks last time."

---

### JPS Layer — Data, not Code (architectural reality check)

R4's most important architectural correction: **the JPS layer is data deployed to Dataverse, not just code in the BFF**. Source-code changes to `INodeExecutor.cs`, `NodeExecutor` classes, and PlaybookBuilder forms are **necessary but not sufficient**. Each JPS primitive must exist as a Dataverse row to be usable by playbook authors and the runtime engine.

Per `docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md` §"JPS is data, not code":

> "JPS itself is **data, not code**. JPS data lives in Dataverse on `sprk_analysisaction.sprk_systemprompt` (a JSON document with `$schema`, `$version`, `instruction { role, task, constraints, context }`, `input`, `parameters`) and on `sprk_playbook` rows."

This was the exact failure mode of Insights Engine Phase 1:

> "Phase 1 deployment didn't wire `sprk_analysisaction` rows for the new ActionTypes — a JPS-convention gap, not an engine bug."

#### Verified state of JPS deployment in spaarkedev1 (2026-06-25)

| Artifact | Layer | Status |
|---|---|---|
| `LookupUserMembershipNodeExecutor.cs` (BFF C# code) | Code | ✅ Shipped |
| `LookupUserMembershipForm.tsx` (PlaybookBuilder UI form) | Code | ✅ Shipped |
| `ActionType = 52` enum value in `INodeExecutor.cs` | Code | ✅ Shipped |
| **`sprk_analysisaction` row with `sprk_executoractiontype = 52`** | **JPS data** | ❌ **0 rows found** in spaarkedev1 |
| `sprk_analysisaction` rows for ActionType 51 (QueryDataverse) | JPS data | ✅ 2 rows (INS-FETCH-KPI, SYS-QUERY-DV) |
| `sprk_analysisplaybook` rows for the 7 notification playbooks (PB-016 through PB-022) | JPS data | ✅ 7 rows exist (all Active, playbooktype=2 Notification) |
| Whether existing playbook `sprk_configjson` references ActionType 52 | JPS data | ❓ **Unknown** — needs per-row inspection (planned in W0.4) |
| `DAILY-BRIEFING-NARRATE` playbook row | JPS data | ❌ Does not exist (must be created in R4) |

**Conclusion**: even though platform-foundations-R3 marked `LookupUserMembership` "shipped," the JPS data layer was not updated. Playbook authors cannot reference ActionType 52 from the PlaybookBuilder because no Action row exists for them to drag onto the canvas, and the runtime cannot dispatch to it because the row's lookup-by-actioncode would fail. This is the gap R4 W0 closes.

#### R4 W0 closes this gap with JPS skills + patterns

R4 follows the established Spaarke JPS workflow:

| Skill | Purpose | Used in R4 |
|---|---|---|
| [`jps-action-create`](.claude/skills/jps-action-create/) | Author a new `sprk_analysisaction` row with JPS-compliant `sprk_systemprompt` | W0.1, W0.2 |
| [`jps-playbook-design`](.claude/skills/jps-playbook-design/) | Design and deploy a complete JPS playbook (Dataverse row + nodes + config) | W0.3 |
| [`jps-playbook-audit`](.claude/skills/jps-playbook-audit/) | Audit existing playbooks against current scope catalog + standards | W0.4 |
| [`jps-validate`](.claude/skills/jps-validate/) | Validate JPS JSON against schema + test rendering | W0.1 through W0.4 |
| [`jps-scope-refresh`](.claude/skills/jps-scope-refresh/) | Refresh scope + model index from Dataverse | W0.5 (post-deployment verification) |

Architecture reference:
- [`docs/architecture/AI-ARCHITECTURE.md`](../../docs/architecture/AI-ARCHITECTURE.md) — Spaarke AI overall architecture
- [`docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md`](../../docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md) — Reference for JPS-as-data principle
- [`docs/adr/ADR-013-ai-architecture.md`](../../docs/adr/ADR-013-ai-architecture.md) — BFF AI architecture constraints
- [`docs/adr/ADR-034-user-record-membership.md`](../../docs/adr/ADR-034-user-record-membership.md) — Membership resolution pattern
- [`projects/spaarke-platform-foundations-r3/`](../spaarke-platform-foundations-r3/) — Source of LookupUserMembership code + design

---

## Solution Approach

### Workstream 0 — JPS Deployment Layer (foundational; blocks W1 and W2)

W0 is the foundation: every JPS primitive R4 references must exist as a Dataverse row before W1 playbook configs or W2 narration dispatch can work end-to-end. Each W0 task follows the canonical JPS skill (`jps-action-create` or `jps-playbook-design`), validates with `jps-validate`, and verifies with `jps-scope-refresh`.

**W0.1 — Deploy `sprk_analysisaction` row for `LookupUserMembership` (ActionType 52)**

Author and deploy the missing JPS Action row that platform-foundations-R3 left unwired. Required fields:

```
sprk_actioncode: "SYS-LOOKUP-MEMBERSHIP" (convention: SYS- prefix for system-level data ops, mirrors SYS-QUERY-DV)
sprk_name: "System: Lookup User Membership"
sprk_executoractiontype: 52
sprk_systemprompt: (JPS-formatted input schema for entityType, roles, identityTypes, includeRelated, limit; no LLM prompt — this is a Dataverse-data-ops action)
sprk_outputformat: JSON
sprk_outputschemajson: (schema for { entityType, count, ids[], byRole, continuationToken, cacheExpiresAt })
sprk_tags: "membership,system,dataverse-ops"
sprk_description: "Resolves the current user's memberships against a target entity (e.g., sprk_matter). Used by notification playbooks to scope queries to records the user is associated with."
sprk_sortorder: 100 (positions in PlaybookBuilder palette near QueryDataverse)
statecode: Active
```

Acceptance: PlaybookBuilder UI exposes the Action; a hand-built playbook with LookupUserMembership → QueryDataverse executes end-to-end against spaarkedev1.

**W0.2 — Author + deploy `sprk_analysisaction` rows for the `daily-briefing-narrate` playbook's Skills + Tools**

The new narrate playbook needs at least 2 Action rows:

| Action code | Name | ExecutorActionType | Purpose |
|---|---|---|---|
| `BRIEF-NARRATE-TLDR` | Daily Briefing — Generate TL;DR | (Existing AiAnalysis = 0) | LLM call with grounded JPS system prompt; temp=0; max 500 tokens |
| `BRIEF-NARRATE-CHANNEL` | Daily Briefing — Generate Channel Narrative | (Existing AiAnalysis = 0) | LLM call per channel; same grounding rules; max 300 tokens |
| `BRIEF-VALIDATE-ENTITY-NAMES` | Daily Briefing — Validate Entity Names | NEW ExecutorActionType (TBD — 141 or next free) | Tool: scrubs LLM output against input allow-list; logs hallucination warnings |

**Important**: `BRIEF-VALIDATE-ENTITY-NAMES` requires a NEW C# `INodeExecutor` implementation (`EntityNameValidatorNodeExecutor.cs`) AND a new ActionType enum value AND its Dataverse Action row. R4 does both (code + JPS data) atomically.

Acceptance: `jps-validate` passes for each Action row; each Action's `sprk_systemprompt` JPS document parses with the correct `$schema` version.

**W0.3 — Author + deploy `sprk_analysisplaybook` row `DAILY-BRIEFING-NARRATE`**

The new playbook row + `sprk_configjson` that orchestrates the narrate workflow. Uses `jps-playbook-design`. Key fields:

```
sprk_playbookcode: "BRIEF-NARRATE"
sprk_name: "Daily Briefing — Generate Narration"
sprk_playbooktype: 0 (AiAnalysis) — NOT 2 (Notification) because this is a synthesis playbook, not a notification producer
sprk_playbookmode: 1 (NodeBased)
sprk_triggertype: 0 (Manual — dispatched on-demand by the BFF endpoint wrapper)
sprk_configjson: node graph definition (Start → LoadKnowledge → GenerateTldr ‖ GenerateChannelNarratives → ValidateEntityNames → ReturnResponse)
sprk_capabilities: 100000006 (Summarize)
statecode: Active
```

Acceptance: `jps-playbook-audit` passes; BFF wrapper endpoint `/api/playbooks/run/BRIEF-NARRATE` (or equivalent) invokes the playbook successfully.

**W0.4 — Audit + update existing 7 notification playbook `sprk_configjson` to use `LookupUserMembership`**

Per `jps-playbook-audit`, inspect each of PB-016 through PB-022's current `sprk_configjson`:

| Playbook | Current state | R4 action |
|---|---|---|
| PB-016 (New Emails) | Source JSON migrated to LookupUserMembership; deployed configjson unknown | Verify deployed; redeploy if needed |
| PB-017 (Matter Activity) | Stub (per source files) | Implement complete node graph in W1.3; deploy via W0.4 |
| PB-018 (New Documents) | Source JSON migrated; deployed configjson unknown | Verify deployed; redeploy if needed |
| PB-019 (New Events) | Source JSON migrated; deployed configjson unknown | Verify deployed; redeploy if needed |
| PB-020 (Tasks Due Soon) | Source uses ownerid-only | Expand to membership scope per W1.2; deploy |
| PB-021 (Tasks Overdue) | Source uses ownerid-only | Expand to membership scope per W1.2; deploy |
| PB-022 (Work Assignments) | Stub | Implement complete node graph in W1.3; deploy via W0.4 |

For each playbook, the audit-and-deploy cycle:
1. Read deployed `sprk_configjson` from spaarkedev1
2. Compare against source JSON file in repo
3. If divergent: reconcile (deploy source-of-truth from repo) using `dataverse-deploy` skill patterns
4. If both reference ActionType 52 LookupUserMembership: validate W0.1 is deployed first (else playbook will fail at runtime)
5. Run the playbook end-to-end with a test user; confirm notification rows appear in `appnotification` with enriched customData (after W1.1 lands)

**W0.5 — Run `jps-scope-refresh` post-deployment**

After W0.1, W0.2, W0.3, W0.4 complete: refresh the Spaarke scope + model catalog so downstream tooling (Claude Code skills, AI agents, PlaybookBuilder palette) sees the new Action and Playbook rows.

---

### Workstream 1 — Producer (notifications + playbooks)

**W1.1 — Enrich `CreateNotificationNodeExecutor` customData schema**

Extend the producer to write a richer `customData` shape:

```json
{
  "category": "tasks-overdue",
  "priority": "high",
  "actionUrl": "/main.aspx?…",
  "dueDate": "2026-06-25T…",
  "regardingName": "Review motion to dismiss",
  "regardingEntityType": "task",
  "regardingId": "11111111-…",
  "viaMatter": {
    "id": "22222222-…",
    "name": "CMRCL-441482: Engagement of legal services by ACME Corporation",
    "memberships": [
      { "role": "owner" },
      { "role": "assignedAttorney" }
    ]
  },
  "source": {
    "entityType": "task",
    "id": "11111111-…",
    "modifiedOn": "2026-06-25T…",
    "owningUser": "Jane Doe"
  }
}
```

`viaMatter.memberships[]` carries multi-role context for transparency ("you're seeing this as: owner, assigned attorney"). When the source record links to no matter (rare), `viaMatter` is omitted.

**W1.2 — Migrate `notification-tasks-overdue` and `notification-tasks-due-soon` to membership scope (per Q&A D-B)**

Current: `ownerid eq-userid` only. R4 expands to surface tasks regarding any matter the user is a member of (in addition to tasks they own directly). The new playbook shape:

```
Node 1: Start
Node 2: LookupUserMembership(sprk_matter) → matterIds[]
Node 3: QueryDataverse (union):
          tasks WHERE ownerid eq-userid AND statecode=0 AND scheduledend < today
          OR tasks WHERE regardingobjectid IN {{matterIds}} AND statecode=0 AND scheduledend < today
Node 4: Deduplicate by activityid
Node 5: CreateNotification (iterateItems=true)
```

**W1.3 — Implement the 2 stub playbooks (per Q&A D-D)**

`notification-matter-activity` and `notification-work-assignments` are currently empty shells. R4 implements them following the membership-aware pattern:

- `notification-matter-activity` — surfaces recent activity (modifications, comments, status changes) on matters the user is a member of
- `notification-work-assignments` — surfaces new or updated `sprk_workassignment` records linked to the user's memberships

Exact FetchXml definitions in spec.md.

**W1.4 — Audit and standardize customData across all 7 playbooks**

Ensure all playbooks write the enriched customData shape from W1.1. Update playbook JSON definitions to include `customDataTemplate` that resolves variables from the LookupUserMembership and QueryDataverse output.

**W1.5 — Producer-side documentation of Contact-only limitation**

Document that Contact members without `azureactivedirectoryobjectid` match are silently excluded from notifications. Add a structured log warning when discovered, for operator visibility.

### Workstream 2 — Consumer (widget + `/narrate` playbook)

**W2.1 — Convert `/narrate` to JPS playbook**

- New playbook definition: `daily-briefing-narrate.json` (or similar)
- Playbook engine endpoint dispatches based on payload type
- Existing `DailyBriefingEndpoints.HandleNarrate` either: (a) becomes a thin wrapper that invokes the playbook, or (b) is deprecated in favor of `/api/playbooks/run/daily-briefing-narrate`
- Migration path for existing consumers (BriefingService.cs, WorkspaceMatterEndpoints.cs) audited

**W2.2 — Grounded prompt + entity validation**

The playbook's GenerateTldr and GenerateChannelNarratives skill nodes use prompts that:
- Include a strong system message: "You are a notification summarizer. You MUST only reference entity names, dates, and identifiers present in the provided input. If you cannot summarize a category from the data, write 'no items' or omit the bullet."
- Remove the "Acme Corp engagement letter" example names
- Use temperature 0 for deterministic summarization
- Receive enriched customData (W1.1) so the LLM has real names to ground on

The ValidateEntityNames tool node post-processes LLM output:
- Builds an allow-list from input payload: every `regardingName`, every `viaMatter.name`, every `source.owningUser`
- Scans LLM output (TL;DR `summary`, `keyTakeaways`, `topAction`, each bullet's `narrative`)
- Removes or replaces sentences mentioning entities not in the allow-list
- Logs structured warning for monitoring (`hallucination_detected` event)

**W2.3 — Fix `/narrate` caching + render reliability**

- Remove `hasFetchedRef` from `useBriefingNarration` OR invalidate it on `actionsRefresh` bump
- `ActivityNotesSection` falls back to raw channel item rendering when `channelNarratives.length === 0` (rare; defense-in-depth — narration is integral per owner so this should not happen, but graceful failure mode preserves usability)
- TL;DR `totalNotificationCount` reconciles with rendered Activity item count

**W2.4 — Wire preferences end-to-end (per Q&A — minConfidence removed)**

| Preference | Wiring |
|---|---|
| `timeWindow` (Recency: 12h/24h/48h/7d) | `fetchNotifications` $filter on `createdon ge {now - window}` |
| `dueWithinDays` (Due-soon: 1/2/3/5/7) | Server filter or client filter on `customData.dueDate` |
| `disabledChannels[]` | Server-side $filter on `customData.category not in {disabled}` |
| `autoPopup` | Workspace launcher checks this; opens Daily Briefing tab on workspace mount if true |
| `minConfidence` | **REMOVED** (vestigial; data is deterministic from FetchXml — no probabilistic AI scoring) |

PreferencesDropdown UI updated to remove the minConfidence row.

**W2.5 — Per-item UX redesign: three-dot overflow menu (per Q&A D-E response confirmed during research)**

Replace the inline 5-icon row with:
- Inline (always visible): regarding-name link + 1–2 most-used actions (e.g., "Mark read" inline; or no inline actions)
- Overflow menu (three-dot trigger): Mark as read, Remove from briefing, Keep on briefing 7 more days, Add to To Do, Dismiss, Open record (modal)

Match the semantic search PCF list pattern shown in user's reference screenshot. Specific icon + label combinations defined in spec.md.

**W2.6 — Record link → modal open + graceful 403 fallback (per Q&A D-C)**

- Fix link click to invoke `Xrm.Navigation.navigateTo({pageType: 'entityrecord', entityName, entityId}, {target: 2, width: {value: 80, unit: '%'}, height: {value: 80, unit: '%'}})` (modal open)
- On 403 / promise rejection: render a non-blocking toast "Cannot open record — you may not have access"
- No defensive pre-check against Dataverse before showing the notification (per Q&A D-C — accept the UAC fidelity gap)

**W2.7 — TL;DR ↔ Activities count reconciliation**

The TL;DR's `totalNotificationCount` must equal the number of items rendered in Activity Notes. Track via:
- `totalNotificationCount` = sum of all items in input payload to the playbook
- Activity Notes renders the same input items
- Add a smoke test that asserts equality

---

## Scope

### In Scope

**JPS Deployment (Workstream 0)**:
- W0.1 — Deploy `sprk_analysisaction` row for `LookupUserMembership` (ActionType 52)
- W0.2 — Author + deploy `sprk_analysisaction` rows for `BRIEF-NARRATE-TLDR`, `BRIEF-NARRATE-CHANNEL`, `BRIEF-VALIDATE-ENTITY-NAMES` (incl. new C# NodeExecutor for the entity validator tool)
- W0.3 — Author + deploy `sprk_analysisplaybook` row + `sprk_configjson` for `DAILY-BRIEFING-NARRATE`
- W0.4 — Audit + reconcile + deploy `sprk_configjson` for PB-016 through PB-022
- W0.5 — Run `jps-scope-refresh` post-deployment

**Producer (Workstream 1)**:
- Enrich `CreateNotificationNodeExecutor` customData schema (W1.1)
- Migrate `notification-tasks-overdue` and `notification-tasks-due-soon` to membership-scope (W1.2)
- Implement `notification-matter-activity` and `notification-work-assignments` playbooks (W1.3)
- Audit + standardize customData across all 7 playbooks (W1.4)
- Document Contact-only limitation; structured log warning (W1.5)

**Consumer (Workstream 2)**:
- Convert `/narrate` to JPS playbook (W2.1)
- Grounded prompt with entity name validation tool (W2.2)
- Fix narration caching; fallback rendering when narratives empty (W2.3)
- Wire 4 preferences end-to-end; remove `minConfidence` (W2.4)
- Three-dot overflow menu for per-item actions (W2.5)
- Fix link click + 403 fallback (W2.6)
- TL;DR ↔ Activities count reconciliation (W2.7)

### Out of Scope

- **Email fallback for Contact-only members** — deferred to a separate project; R4 documents the limitation
- **Phase 2 membership infrastructure deployment** — the junction-table + Service Bus topic mechanism built in platform-foundations-R3 remains feature-gated OFF; R4 uses Phase 1A live-compute. Operator deploys Phase 2 separately.
- **AI Search "matter context" knowledge node** for `/narrate` playbook — deferred to R5. R4 ships the playbook with Skill + Tool only; Knowledge node integration is a future enhancement.
- **Insights Engine integration** — Daily Briefing remains a separate AI surface from Insights Engine. The two share architectural patterns (playbooks) but not the same playbook or data sources.
- **Defensive Dataverse UAC pre-check** at widget level — accepted as a documented gap per Q&A D-C
- **Bell-panel parity** — bell-panel lifecycle remains independent (R3 FR-7 invariant preserved)
- **Native bell-panel changes** — out of scope

### Explicitly NOT Changing

- R3 schema (`sprk_briefingstate` Choice column on `appnotification`)
- R3 BFF fix (`ttlinseconds = 604800` in `NotificationService.cs`)
- R3 widget read-state derivation (continues using `sprk_briefingstate`)
- ADR-013 BFF AI architecture pattern
- ADR-021 Fluent v9 design system
- ADR-024 sprk_todo regarding catalog
- ADR-027 Subscription isolation
- ADR-028 Spaarke Auth v2
- ADR-034 Membership resolution pattern (built upon, not modified)
- Existing `sprk_briefingstate` Dataverse Choice column
- `@spaarke/daily-briefing-components` package boundary
- The 3 per-user actions added in R3 (Check, Remove, Keep) — preserved, moved into overflow menu

---

## Requirements

### Functional Requirements (W1 — Producer)

**FR-1 — Enriched customData schema**: `CreateNotificationNodeExecutor` writes the W1.1 customData shape including `viaMatter` (with `memberships[]`), `regardingName`, `source`. Acceptance: a notification produced by any migrated playbook surfaces all 4 enriched fields when source data supports them.

**FR-2 — tasks-overdue + tasks-due-soon membership scope**: Both playbooks add a parallel branch using `LookupUserMembership` so tasks regarding the user's membership matters surface in addition to owned tasks. Acceptance: a user who is `assignedAttorney` on Matter-X but doesn't own any task on it sees overdue tasks on Matter-X in their Daily Briefing.

**FR-3 — matter-activity playbook**: Implements surfacing of recent matter-level changes for the user's memberships. Acceptance: when a matter the user is a member of is modified, an `appnotification` row is created in this category.

**FR-4 — work-assignments playbook**: Implements surfacing of new/updated work assignments. Acceptance: when a `sprk_workassignment` linked to user's memberships is created or updated, an `appnotification` row appears.

**FR-5 — customData consistency across playbooks**: All 7 playbooks write the same enriched schema. Acceptance: schema validation in test fixtures passes for each playbook's notification output.

**FR-6 — Contact-only limitation logging**: When a matter has a membership lookup pointing to a Contact with no SystemUser cross-ref, the producer logs a structured `member_skipped` warning event. Acceptance: log entry visible in Application Insights when condition occurs.

### Functional Requirements (W2 — Consumer)

**FR-7 — `/narrate` as JPS playbook**: A new playbook `daily-briefing-narrate` (or similar) is defined and dispatched by either a wrapper endpoint or a generic playbook-run endpoint. Acceptance: the widget's narration request is fulfilled by playbook execution; no C# string-literal prompts remain in `DailyBriefingEndpoints`.

**FR-8 — Grounded prompt**: The playbook's TL;DR + per-channel skill nodes use prompts that include a system message, strict grounding instruction, temperature 0, and NO baked example names. Acceptance: prompt audit confirms absence of "Acme Corp engagement letter" or similar example names.

**FR-9 — Entity name validation tool**: `ValidateEntityNames` node post-processes LLM output to remove names not in the input payload allow-list. Logs `hallucination_detected` warning for each scrub. Acceptance: test passes — LLM emits a fictional name; tool scrubs it; warning logged.

**FR-10 — Narration cache invalidation**: `useBriefingNarration` re-fetches when `channels` or `actionsRefresh` change. Acceptance: after user clicks Check / Remove / Keep, a new `/narrate` call fires and TL;DR + bullets update.

**FR-11 — Activity Notes fallback rendering**: When `channelNarratives` is empty, `ActivityNotesSection` renders raw channel item lists instead of returning `null`. Acceptance: with mocked narration failure, all notification cards still render in their channels.

**FR-12 — Preferences wired end-to-end**:
- 12a `timeWindow` filters `fetchNotifications` by `createdon`
- 12b `dueWithinDays` filters by `customData.dueDate`
- 12c `disabledChannels` server-side filter on `customData.category`
- 12d `autoPopup` triggers workspace launcher to open Daily Briefing tab
- 12e `minConfidence` removed from UI, types, storage
Acceptance: each setting change produces a measurable difference in rendered content.

**FR-13 — Three-dot overflow menu**: Per-item action UI replaces inline 5-icon row with overflow menu containing: Mark as read, Remove from briefing, Keep +7 days, Add to To Do, Dismiss, Open record. Acceptance: visual + accessibility audit passes; touch target sizes meet WCAG.

**FR-14 — Link → modal open with 403 fallback**: Clicking the regarding name invokes `Xrm.Navigation.navigateTo` modal; on rejection, non-blocking toast shows "Cannot open record." Acceptance: works with and without Dataverse read access.

**FR-15 — TL;DR ↔ Activities count match**: TL;DR's `totalNotificationCount` equals number of items rendered in Activity Notes. Acceptance: smoke test asserts equality with various item counts.

### Non-Functional Requirements

- **NFR-01**: No new HIGH-severity CVE
- **NFR-02**: BFF publish-size delta ≤ +1 MB (NEW playbook + tool nodes; expect minimal NuGet adds)
- **NFR-03**: Unit + integration tests cover all FRs; minimum 90% line coverage on changed files; widget jest tests use `jest-environment-jsdom`
- **NFR-04**: Widget action latency unchanged from R3 (optimistic UI ≤16ms; backend write ≤300ms p95)
- **NFR-05**: Backward compatible with existing notifications (older customData shape without `viaMatter` falls back gracefully)
- **NFR-06**: All BFF-touching tasks pass §10 BFF Hygiene (publish-size + CVE verification per task; `code-review` + `adr-check` at task-execute Step 9.5)

---

## Owner Clarifications (Resolved 2026-06-25)

| Q&A | Decision | Rationale |
|---|---|---|
| Recipient model | One `appnotification` per (User, source-record). Owner = SystemUser; customData carries `viaMatter.memberships[]` describing roles. | Forced by Dataverse owner-scoped row security; cleanest pattern. Implicit Contact→SystemUser resolution via membership service's 6-pronged identity normalization. |
| D-A: Contact-only members | Skip silently. Document limitation. Structured log warning when encountered. | External attorneys without tenant accounts are out of scope for Daily Briefing. Email fallback is a separate future project. |
| D-B: tasks-overdue/due-soon expansion | Yes — expand to membership scope (union of owned + member-of). | Matches "comprehensive summary of everything I'm associated with" intent. |
| D-C: Link click UAC fidelity | Graceful 403 fallback; no defensive pre-check. | Avoids doubling query cost for rare edge case. |
| D-D: Stub playbooks | Implement `matter-activity` and `work-assignments` in R4. | Otherwise briefing is missing 2 channels by design intent. |
| D-E: `/narrate` playbook nodes | Skill + Tool. AI Search "matter context" knowledge node deferred to R5. | Start with the architectural correction; defer RAG enhancement until evidence it's needed. |
| D-F: Phasing | Single R4 project; ship phases as separate PRs into work branch; merge to master at end. **Revised post-JPS-investigation 2026-06-25**: 5 PRs (not 4) — added PR 1 (JPS Action rows + EntityNameValidator code) and PR 2 (JPS Playbook configs) as foundational JPS deployment before code/widget changes. | Producer enrichment lands first (widget improvements depend on richer customData). JPS deployment lands FIRST (everything else depends on Action rows existing). |
| JPS deployment as first-class concern | Add Workstream 0 explicitly. Follow `jps-action-create`, `jps-playbook-design`, `jps-playbook-audit`, `jps-validate`, `jps-scope-refresh` skills. Reference `AI-ARCHITECTURE.md` + `INSIGHTS-ENGINE-ARCHITECTURE.md`. | Discovered via `mcp__dataverse__read_query`: ActionType 52 has 0 deployed `sprk_analysisaction` rows in spaarkedev1, despite C# code being shipped. This is the exact Phase 1 Insights Engine failure mode. R4 explicitly avoids repeating it. |
| `minConfidence` setting | Remove entirely. | Vestigial: data is deterministic from FetchXml; no probabilistic AI scoring concept applies. |
| Both owner AND contact association on same record | Naturally handled. `customData.viaMatter.memberships[]` is an array reflecting all roles. UI shows: "you're seeing this as: owner, assigned attorney." | Membership resolver returns matter under multiple `byRole` buckets simultaneously. |

---

## Risks & Mitigations

| Risk | Probability | Impact | Mitigation |
|---|---|---|---|
| **W0 first** — JPS data deploy order matters: if a playbook config (PR 2) references an Action row that PR 1 hasn't deployed yet, runtime dispatch fails silently | High | High | **Enforce PR 1 merges before PR 2.** W0.1+W0.2 (Action rows) MUST be deployed to spaarkedev1 before W0.3+W0.4 (playbook configs that reference them). PR template includes a deployment-order checkbox. Replicate the Phase 1 deployment lesson from Insights Engine. |
| BRIEF-VALIDATE-ENTITY-NAMES requires both a NEW C# NodeExecutor + a NEW ActionType enum value + a JPS Action row + a sprk_executoractiontype int — easy to miss one | Medium | High | All 4 artifacts authored together in a single PR (PR 1); `jps-validate` runs end-to-end (Action row resolves to executor; executor runs in test playbook); spec.md FR includes acceptance criterion: PlaybookBuilder shows the Tool on the canvas |
| Existing 7 notification playbook configs (PB-016 — PB-022) might already be partly migrated to LookupUserMembership at the source-JSON level but not in deployed Dataverse row — silent divergence | High | Medium | W0.4 explicitly reads deployed `sprk_configjson` via `mcp__dataverse__read_query`, compares against repo source, reconciles via `dataverse-deploy`. Document the diff in `notes/`. |
| Playbook engine doesn't currently support "stateless summarization" (no Dataverse query, just LLM call with structured input) | Medium | Medium | Audit playbook engine capabilities at R4 task 010. If not supported: add a minimal stateless mode (small refactor) or wrap `/narrate` as a degenerate playbook with a no-op Start node |
| Entity name validator over-scrubs (removes legitimate names) | Medium | Medium | Allow-list built from BOTH `regardingName` AND `viaMatter.name` fields; tested with realistic notification sets; warning log lets us iterate |
| Membership Phase 1A live-compute introduces latency on per-user playbook runs | Low | Low | Already mitigated by Redis cache (5 min); R4 doesn't change Phase 1A behavior |
| `tasks-overdue`/`due-soon` expansion produces too many notifications (noisy briefing) | Medium | Low | Add a relevance filter (e.g., max 50 per channel per user); user can use `disabledChannels` to opt out per channel |
| Stub playbook implementation surfaces source data quality issues (e.g., matters without modification timestamps) | Medium | Low | FetchXml gracefully handles nulls; document any source-side requirements in spec.md |
| Three-dot overflow menu requires Fluent v9 `MenuItem` patterns the widget doesn't already use | Low | Low | Pattern already used in semantic search PCF; reuse the same approach |
| Removing `minConfidence` breaks something (unlikely — it's dead code) | Low | Low | grep for all references; remove from UI, types, storage, preferences hook; jest test sweep |
| `/narrate` cache removal causes excessive LLM calls (e.g., user clicks 3 actions in quick succession → 3 narration calls) | Medium | Medium | Debounce narration refetch (200ms) so action burst triggers a single refetch when settled |
| Customer data enrichment makes `appnotification.data` field exceed Dataverse Memo cap | Very Low | Medium | Typical enriched payload ~2KB; Memo cap is 1MB+; monitored via structured log |
| Migration of existing playbooks breaks production-deployed scheduled jobs | Medium | High | Stage migrations through DEV first; verify scheduler runs against migrated playbook before promoting; documented rollback path |

---

## Implementation Estimate

| Workstream | Component | Effort |
|---|---|---|
| W0.1 | Deploy `sprk_analysisaction` for ActionType 52 (LookupUserMembership) via `jps-action-create` | ~2 hours |
| W0.2 | Author + deploy 3 Action rows (BRIEF-NARRATE-TLDR, BRIEF-NARRATE-CHANNEL, BRIEF-VALIDATE-ENTITY-NAMES) + new C# EntityNameValidatorNodeExecutor | ~6 hours |
| W0.3 | Author + deploy `DAILY-BRIEFING-NARRATE` playbook (`sprk_analysisplaybook` row + `sprk_configjson`) via `jps-playbook-design` | ~4 hours |
| W0.4 | Audit + reconcile + deploy `sprk_configjson` for PB-016 through PB-022 via `jps-playbook-audit` | ~5 hours |
| W0.5 | `jps-scope-refresh` post-deployment | ~1 hour |
| W1.1 | customData schema enrichment in CreateNotificationNodeExecutor + tests | ~3 hours |
| W1.2 | tasks-overdue + tasks-due-soon migration to membership scope + tests | ~4 hours |
| W1.3 | matter-activity + work-assignments playbook implementation (code + config) + tests | ~6 hours |
| W1.4 | customData audit + standardization across 7 playbooks | ~3 hours |
| W1.5 | Contact-only logging | ~1 hour |
| W2.1 | BFF wrapper endpoint dispatching to JPS playbook (replacing hardcoded HandleNarrate) | ~4 hours |
| W2.2 | Grounded prompt content for BRIEF-NARRATE-TLDR / BRIEF-NARRATE-CHANNEL Action rows (system prompt JSON) | ~3 hours |
| W2.3 | Narration cache + fallback rendering (widget changes) | ~3 hours |
| W2.4 | Preferences wired end-to-end + minConfidence removal | ~5 hours |
| W2.5 | Three-dot overflow menu UX | ~4 hours |
| W2.6 | Link click + 403 fallback | ~2 hours |
| W2.7 | TL;DR ↔ Activities count reconciliation | ~2 hours |
| - | BFF Hygiene §10 verification on touched tasks | ~2 hours |
| - | Manual UAT in spaarkedev1 (full graduation criteria) | ~3 hours |
| - | Lessons-learned + project wrap-up | ~2 hours |
| **Total** | | **~65 hours** |

**PR strategy** (per D-F, revised with W0): **5 PRs into work branch**, sequenced to respect data ↔ code dependencies:

- **PR 1 — JPS Foundation**: W0.1 + W0.2 (Action rows + EntityNameValidatorNodeExecutor C# code). Deploys the Action primitives that all later playbook configs reference. After this PR merges to work branch, spaarkedev1 has ActionType 52 + 3 new daily-briefing Actions available in PlaybookBuilder.

- **PR 2 — JPS Playbook Configs**: W0.3 + W0.4 + W0.5 (the `DAILY-BRIEFING-NARRATE` playbook + reconciled `sprk_configjson` for PB-016–PB-022 + scope refresh). After this PR, all 7 notification playbooks correctly reference ActionType 52 in their deployed configs, and the narrate playbook is dispatchable.

- **PR 3 — Producer Code**: W1.1 + W1.2 + W1.3 + W1.4 + W1.5 (CreateNotificationNodeExecutor enrichment + customData standardization + Contact-only logging). Code changes that the playbook configs in PR 2 will produce richer customData.

- **PR 4 — Consumer Plumbing**: W2.1 + W2.2 + W2.3 (BFF wrapper endpoint + prompt content in Action JPS + narration cache fix + fallback rendering). After this PR, the widget hits the JPS playbook engine instead of the hardcoded endpoint; narration is grounded.

- **PR 5 — Consumer Polish**: W2.4 + W2.5 + W2.6 + W2.7 (preferences + UX + link + count match).

**Merge to master after PR 5 passes UAT.**

**Dependency note**: PR 1 must merge before PR 2 (Action rows must exist for playbook configs to reference); PR 1+2 must merge before PR 3 (customData enrichment is consumed by the deployed playbook configs); PR 1+2+3+4 must merge before PR 5 (UI depends on backend behaving correctly first).

---

## Graduation Criteria

R4 graduates when all of the following pass:

- [ ] All 15 FRs deliver per spec
- [ ] All NFRs pass
- [ ] User can run full UAT scenario in spaarkedev1:
  - [ ] Widget renders content for a user with notifications
  - [ ] TL;DR references ONLY entities from actual data (no Johnson & Lee LLP)
  - [ ] User clicks Check/Remove/Keep — narration refreshes, content stays consistent
  - [ ] User changes Recency to 7 days — visible filter applies
  - [ ] User changes Due-soon to 5 days — visible filter applies
  - [ ] User disables a channel — channel disappears from briefing (server-side; AI doesn't see it either)
  - [ ] User clicks record link — opens matter modal; gracefully handles 403
  - [ ] Per-item actions accessed via three-dot overflow menu; no visual collision
  - [ ] Assigned attorney (with SystemUser via AAD oid) sees notifications for their matters' tasks/events/documents
  - [ ] Contact-only members produce log warning, no notifications
- [ ] BFF publish-size delta ≤ +1 MB
- [ ] No new HIGH-severity CVE
- [ ] All Workstream 1 PRs merged + verified in DEV before Workstream 2 PRs deploy

---

## References

### JPS architecture (binding for W0)
- **`docs/architecture/AI-ARCHITECTURE.md`** — Spaarke AI overall architecture; defines `sprk_analysisaction` as the JPS dispatch + prompt primitive
- **`docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md`** — Reference for "JPS is data, not code" principle; same JPS substrate used by Insights Engine
- **ADR-013** — BFF AI architecture (`docs/adr/ADR-013-ai-architecture.md` — Minimal API + playbook engine constraints)
- **ADR-034** — User-record membership resolution pattern (`docs/adr/ADR-034-user-record-membership.md`)
- **ADR-028** — Spaarke Auth v2 (Contact ↔ SystemUser cross-ref via `azureactivedirectoryobjectid`)

### JPS skills (load before each W0 task)
- **`jps-action-create`** — Author + deploy a `sprk_analysisaction` row (W0.1, W0.2)
- **`jps-playbook-design`** — Design and deploy a complete JPS playbook (W0.3)
- **`jps-playbook-audit`** — Audit existing playbooks against current scope catalog + standards (W0.4)
- **`jps-validate`** — Validate JPS JSON against schema + test rendering (used throughout W0)
- **`jps-scope-refresh`** — Refresh the scope + model index from Dataverse (W0.5)

### Project predecessors
- **R3 daily-update-service spec**: [`projects/spaarke-daily-update-service-r3/spec.md`](../spaarke-daily-update-service-r3/spec.md)
- **R3 platform-foundations design**: [`projects/spaarke-platform-foundations-r3/design.md`](../spaarke-platform-foundations-r3/design.md) — source of LookupUserMembership code + design
- **R3 platform-foundations CLAUDE.md**: [`projects/spaarke-platform-foundations-r3/CLAUDE.md`](../spaarke-platform-foundations-r3/CLAUDE.md) — confirms ActionType 52 enum addition; flags PlaybookBuilder pattern

### Code references (consumed by R4)
- **Membership service**: `src/server/api/Sprk.Bff.Api/Services/Ai/Membership/`
- **LookupUserMembership node executor**: `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/LookupUserMembershipNodeExecutor.cs`
- **CreateNotificationNodeExecutor**: `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/CreateNotificationNodeExecutor.cs`
- **Current /narrate endpoint** (to be replaced by playbook dispatch): `src/server/api/Sprk.Bff.Api/Api/Ai/DailyBriefingEndpoints.cs`
- **Playbook engine**: `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs` + sibling services
- **PlaybookBuilder UI form**: `src/client/code-pages/PlaybookBuilder/src/components/properties/LookupUserMembershipForm.tsx`
- **Widget service layer**: `src/client/shared/Spaarke.DailyBriefing.Components/src/services/notificationService.ts`
- **Widget narration hook**: `src/client/shared/Spaarke.DailyBriefing.Components/src/hooks/useBriefingNarration.ts`

### Data model references
- **Matter team-member fields**: `docs/data-model/sprk_matter-related-tables.md`
- **Microsoft Learn — appnotification entity**: https://learn.microsoft.com/power-apps/developer/model-driven-apps/clientapi/send-in-app-notifications
- **Spaarke JPS-deployed playbooks (current state in spaarkedev1, verified 2026-06-25 via mcp__dataverse__read_query)**:
  - PB-016 New Emails on Matters · PB-017 Matter/Project Activity Summary · PB-018 New Documents on Your Matters · PB-019 New Events on Matters/Projects · PB-020 Tasks Due Soon · PB-021 Tasks Overdue · PB-022 New Work Assignments
- **Spaarke JPS-deployed Actions for ActionType 51 (QueryDataverse)**: INS-FETCH-KPI, SYS-QUERY-DV
- **Missing Actions (R4 W0 will deploy)**: `SYS-LOOKUP-MEMBERSHIP` (ActionType 52), `BRIEF-NARRATE-TLDR`, `BRIEF-NARRATE-CHANNEL`, `BRIEF-VALIDATE-ENTITY-NAMES`

---

*Drafted 2026-06-25 from UAT-driven architectural review. Next step: `/design-to-spec` to produce the AI-optimized spec.md, then `/project-pipeline` for plan + tasks.*
