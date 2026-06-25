# Spaarke Daily Update Service (R4) — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-06-25
> **Source**: [`design.md`](design.md) (UAT-driven architectural review session, 2026-06-25)
> **Predecessors**:
> - [`spaarke-daily-update-service-r3`](../spaarke-daily-update-service-r3/) — draft PR #451 (read-state + TTL + 3 actions)
> - [`spaarke-platform-foundations-r3`](../spaarke-platform-foundations-r3/) — shipped (MembershipResolverService + LookupUserMembership code)

---

## Executive Summary

R3 UAT in spaarkedev1 confirmed the headline R3 win (widget renders content, no longer shows EmptyState) but surfaced four classes of structural defects: AI narration hallucinating firm names not in user data, 4 of 5 preferences are dead code, missing JPS deployment of the membership Action primitive (despite C# code shipping), and 2 of 7 notification playbooks are stubs. R4 closes these gaps in a single project shipped as 5 phased PRs across three coordinated workstreams: (W0) JPS deployment layer — deploy the missing `sprk_analysisaction` rows that make `LookupUserMembership` (ActionType 52) and the new daily-briefing-narrate JPS playbook actually dispatchable; (W1) Producer — enrich notification payloads, migrate playbooks to membership-aware queries, implement 2 stub playbooks, expand task playbooks to membership-scope; (W2) Consumer — convert `/narrate` from hardcoded BFF endpoint to JPS playbook dispatch, wire preferences end-to-end, fix narration cache, redesign per-item UX with three-dot overflow menu, fix link navigation.

**Critical architecture principle**: JPS is data, not code. Source-code changes are necessary but not sufficient — corresponding `sprk_analysisaction` + `sprk_analysisplaybook` rows must be deployed to each environment. R4 makes this a first-class concern.

R4 estimated effort: ~65 hours engineering, 5 PRs into work branch, merge to master after PR 5 passes UAT.

---

## Scope

### In Scope

**Workstream 0 — JPS Deployment Layer**:
- Deploy `sprk_analysisaction` row for `SYS-LOOKUP-MEMBERSHIP` (ExecutorActionType 52) — closes platform-foundations-R3 deployment gap
- Author + deploy 3 new Action rows: `BRIEF-NARRATE-TLDR`, `BRIEF-NARRATE-CHANNEL`, `BRIEF-VALIDATE-ENTITY-NAMES`
- Author + ship new C# `EntityNameValidatorNodeExecutor` with ExecutorActionType **141** (slots into post-LLM cluster with Sanitization=130/ObservationEmit=140)
- Author + deploy `sprk_analysisplaybook` row `DAILY-BRIEFING-NARRATE` + its `sprk_configjson` node graph
- Audit + reconcile + deploy `sprk_configjson` for the 7 existing notification playbooks (PB-016 through PB-022)
- Run `jps-scope-refresh` post-deployment

**Workstream 1 — Producer (Notifications + Playbooks)**:
- Enrich `CreateNotificationNodeExecutor` `customData` to include `viaMatter` (id, name, memberships[]), `regardingName`, `source` (entityType, id, modifiedOn, owningUser)
- Migrate `notification-tasks-overdue` and `notification-tasks-due-soon` playbooks to union `ownerid eq-userid` + membership-scope (via `LookupUserMembership`)
- Implement `notification-matter-activity` playbook (currently stub) — surfaces modifications + status changes on Matter/Project/WorkAssignment within last 24h for user's membership matters
- Implement `notification-work-assignments` playbook (currently stub) — surfaces new or updated `sprk_workassignment` within last 24h linked to user's memberships
- Audit + standardize enriched customData across all 7 playbooks
- Structured `member_skipped` log warning when Contact has no SystemUser cross-ref via `azureactivedirectoryobjectid`

**Workstream 2 — Consumer (Widget + `/narrate` playbook)**:
- Replace hardcoded `HandleNarrate` in `DailyBriefingEndpoints.cs` with thin wrapper dispatching to JPS playbook engine via `DAILY-BRIEFING-NARRATE`
- Grounded prompt content in `BRIEF-NARRATE-TLDR` and `BRIEF-NARRATE-CHANNEL` Action rows: system message, strict "use only names from input" instruction, temperature 0, NO baked example names ("Acme Corp engagement letter" etc.)
- `EntityNameValidatorNodeExecutor` Tool: scrubs LLM output to remove names not present in input payload allow-list; logs `hallucination_detected` warning per scrub
- Remove `hasFetchedRef` cache from `useBriefingNarration` so narration refetches when notifications change
- `ActivityNotesSection` fallback: render raw channel item list when `channelNarratives.length === 0` (defense-in-depth)
- Wire 4 preferences end-to-end: `timeWindow` → `fetchNotifications` `createdon` filter; `dueWithinDays` → due-date filter; `disabledChannels` → server-side `customData.category` filter; `autoPopup` → workspace launcher
- Remove `minConfidence` setting entirely (vestigial; data is deterministic)
- Per-item action UX: three-dot overflow menu (semantic-search-PCF pattern). Inline: regarding link + optional 1 quick action. Overflow: Mark as read, Remove from briefing, Keep +7 days, Add to To Do, Dismiss, Open record (modal)
- Fix link click → `Xrm.Navigation.navigateTo` modal open; graceful 403 fallback toast on rejection
- TL;DR `totalNotificationCount` reconciles with rendered Activity item count (smoke test enforces)

### Out of Scope

- **Email fallback for Contact-only members** (members without SystemUser via AAD oid) — deferred to a separate project; R4 documents the limitation via structured log warning
- **Phase 2 membership infrastructure deployment** — junction-table + Service Bus topic mechanism built in platform-foundations-R3 remains feature-gated OFF; R4 uses Phase 1A live-compute. Operator deploys Phase 2 separately.
- **AI Search "matter context" knowledge node** for `/narrate` playbook — deferred to R5. R4 ships the playbook with Skill + Tool only; Knowledge node integration is a future enhancement.
- **Insights Engine integration** — Daily Briefing remains a separate AI surface from Insights Engine. The two share architectural patterns (JPS playbooks) but not the same playbook or data sources.
- **Defensive Dataverse UAC pre-check** at widget level — accepted as a documented gap per Q&A D-C; widget shows notification and renders 403 fallback toast if user lacks access on click.
- **Bell-panel parity** — bell-panel lifecycle remains independent (R3 FR-7 invariant preserved).
- **Native bell-panel changes** — out of scope.

### Explicitly NOT Changing

- R3 schema (`sprk_briefingstate` Choice column on `appnotification`) — preserved
- R3 BFF fix (`ttlinseconds = 604800` in `NotificationService.cs`) — preserved
- R3 widget read-state derivation (continues using `sprk_briefingstate`)
- R3 per-user actions (Check, Remove, Keep) — preserved, moved into overflow menu
- ADR-013 BFF AI architecture pattern (extended, not modified)
- ADR-021 Fluent v9 design system
- ADR-024 sprk_todo regarding catalog
- ADR-027 Subscription isolation
- ADR-028 Spaarke Auth v2
- ADR-034 Membership resolution pattern (built upon, not modified)
- `@spaarke/daily-briefing-components` package boundary
- `appnotification` Microsoft OOB entity (only customData JSON content changes)

### Affected Areas

| Path | Purpose | Workstream |
|---|---|---|
| Dataverse: `sprk_analysisaction` table | Add `SYS-LOOKUP-MEMBERSHIP`, `BRIEF-NARRATE-TLDR`, `BRIEF-NARRATE-CHANNEL`, `BRIEF-VALIDATE-ENTITY-NAMES` rows | W0 |
| Dataverse: `sprk_analysisplaybook` table | Add `DAILY-BRIEFING-NARRATE` row; update `sprk_configjson` for PB-016–PB-022 | W0 |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/INodeExecutor.cs` | Add ExecutorActionType 141 = EntityNameValidator | W0 |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/EntityNameValidatorNodeExecutor.cs` (NEW) | Tool node: post-process LLM output against entity-name allow-list | W0 |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/CreateNotificationNodeExecutor.cs` | Enrich customData with viaMatter, regardingName, source | W1 |
| Source playbook JSON files at `projects/spaarke-daily-update-service/notes/playbooks/*.json` | Migrate to membership-aware FetchXml; implement stubs; standardize customData template | W1 |
| `src/server/api/Sprk.Bff.Api/Api/Ai/DailyBriefingEndpoints.cs` | Replace `HandleNarrate` body with playbook dispatch | W2 |
| `src/client/shared/Spaarke.DailyBriefing.Components/src/hooks/useBriefingNarration.ts` | Remove `hasFetchedRef`; refetch on data change | W2 |
| `src/client/shared/Spaarke.DailyBriefing.Components/src/components/ActivityNotesSection.tsx` | Fallback render when narratives empty | W2 |
| `src/client/shared/Spaarke.DailyBriefing.Components/src/services/notificationService.ts` | Wire preferences to `fetchNotifications` query | W2 |
| `src/client/shared/Spaarke.DailyBriefing.Components/src/components/PreferencesDropdown.tsx` | Remove `minConfidence` row; wire other 4 settings | W2 |
| `src/client/shared/Spaarke.DailyBriefing.Components/src/types/notifications.ts` | Remove `minConfidence` / `AiConfidenceThreshold` types | W2 |
| `src/client/shared/Spaarke.DailyBriefing.Components/src/components/NarrativeBullet.tsx` | Replace inline action row with three-dot overflow menu | W2 |
| `src/client/shared/Spaarke.DailyBriefing.Components/src/components/ActivityNotesSection.tsx` (re-touched) | Wire overflow callbacks | W2 |
| `src/client/shared/Spaarke.DailyBriefing.Components/src/components/DailyBriefingApp.tsx` | Pass new callbacks; wire autoPopup launcher hook | W2 |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Nodes/EntityNameValidatorNodeExecutorTests.cs` (NEW) | xUnit tests for the new Tool node | W0 |
| `src/client/shared/Spaarke.DailyBriefing.Components/test/*` | Jest test updates for all consumer changes | W2 |

---

## Requirements

### Functional Requirements

#### Workstream 0 — JPS Deployment Layer

**FR-1 — Deploy `SYS-LOOKUP-MEMBERSHIP` Action row** — Author + deploy `sprk_analysisaction` row in spaarkedev1 with `sprk_executoractiontype = 52`, `sprk_actioncode = "SYS-LOOKUP-MEMBERSHIP"`, JPS-compliant `sprk_systemprompt` defining input schema (entityType, roles, identityTypes, includeRelated, limit), `sprk_outputschemajson` defining output shape, `statecode = Active`. Follow `jps-action-create` skill.
- **AC-1**: `mcp__dataverse__read_query` `SELECT … FROM sprk_analysisaction WHERE sprk_executoractiontype = 52` returns 1 row; PlaybookBuilder UI palette displays the action; a hand-built test playbook with LookupUserMembership → QueryDataverse executes end-to-end in spaarkedev1

**FR-2 — Deploy `BRIEF-NARRATE-TLDR` and `BRIEF-NARRATE-CHANNEL` Action rows** — Author + deploy 2 `sprk_analysisaction` rows with `sprk_executoractiontype = 0` (AiAnalysis), JPS-compliant `sprk_systemprompt` (instruction.role = "notification summarizer", instruction.constraints = ["use only entity names present in input"], etc.), `sprk_temperature = 0`, appropriate `sprk_outputformat`/`sprk_outputschemajson`. Follow `jps-action-create`.
- **AC-2a**: Both rows exist + Active in spaarkedev1; JPS systemprompt validates via `jps-validate`
- **AC-2b**: NO example names from old prompts (Acme Corp engagement letter, etc.) appear in `sprk_systemprompt` text

**FR-3 — Implement + deploy `BRIEF-VALIDATE-ENTITY-NAMES` Action + executor + PlaybookBuilder form** — Author NEW C# `EntityNameValidatorNodeExecutor.cs`. Add `EntityNameValidator = 141` to `INodeExecutor.cs` ActionType enum. Author + deploy `sprk_analysisaction` row with `sprk_executoractiontype = 141`. Executor receives input `{ candidateText, allowList[] }` and returns `{ scrubbedText, removedTerms[] }`. Per-term removal logs structured `hallucination_detected` event. Follow `jps-action-create` + standard NodeExecutor pattern (mirror `QueryDataverseNodeExecutor.cs`).

**PlaybookBuilder UI**: Author NEW `EntityNameValidatorForm.tsx` at `src/client/code-pages/PlaybookBuilder/src/components/properties/EntityNameValidatorForm.tsx`. Follow existing pattern from `LookupUserMembershipForm.tsx` (sibling: 'follow existing patterns' per owner). Configures allow-list source binding (variable reference panel) + scrub strategy options.
- **AC-3a**: `EntityNameValidator = 141` enum value present; ExecutorActionType doesn't conflict with existing values (0/51/52/60/70/80/90/100/110/120/130/140)
- **AC-3b**: xUnit test passes — input with "Johnson & Lee LLP" (not in allow-list) → output has it removed AND warning event emitted
- **AC-3c**: PlaybookBuilder UI palette displays the Tool; property panel shows configurable allow-list source binding via `EntityNameValidatorForm.tsx`

**FR-4 — Deploy `DAILY-BRIEFING-NARRATE` playbook** — Author + deploy `sprk_analysisplaybook` row with `sprk_playbookcode = "BRIEF-NARRATE"`, `sprk_playbooktype = 0` (AiAnalysis — NOT 2 Notification), `sprk_playbookmode = 1` (NodeBased), `sprk_triggertype = 0` (Manual), `sprk_capabilities = 100000006` (Summarize). `sprk_configjson` defines node graph: Start → LoadKnowledge (channel registry) → [GenerateTldr ‖ GenerateChannelNarratives (parallel per channel)] → ValidateEntityNames → ReturnResponse. Follow `jps-playbook-design`.
- **AC-4a**: Row exists + Active in spaarkedev1; `jps-playbook-audit` passes
- **AC-4b**: BFF wrapper endpoint successfully dispatches narrate request to this playbook; receives valid response

**FR-5 — Audit + reconcile `sprk_configjson` for PB-016 through PB-022** — For each of the 7 deployed notification playbooks, read deployed `sprk_configjson`, compare against canonical repo JSON file at `projects/spaarke-daily-update-service/notes/playbooks/*.json`. If divergent, redeploy canonical (repo files are source-of-truth). PB-016/PB-018/PB-019 verified to reference ActionType 52 (LookupUserMembership). PB-020/PB-021 expanded to membership scope per FR-7. PB-017/PB-022 implemented per FR-8/FR-9. Run `jps-scope-refresh` after all deploys. Follow `jps-playbook-audit`.
- **AC-5a**: All 7 playbook rows have `sprk_configjson` matching canonical repo source
- **AC-5b**: PB-016/PB-018/PB-019/PB-020/PB-021 `sprk_configjson` contains a Node referencing ActionType 52 (LookupUserMembership)
- **AC-5c**: `jps-scope-refresh` completes without error; PlaybookBuilder scope catalog reflects new state

#### Workstream 1 — Producer

**FR-6 — Enriched `customData` schema + `sprk_category` column write** — `CreateNotificationNodeExecutor.cs` `BuildNotificationEntity` writes customData with: `category`, `priority`, `actionUrl`, `dueDate` (existing) + `regardingName`, `regardingEntityType`, `regardingId`, `viaMatter` (object: id, name, memberships[]), `source` (object: entityType, id, modifiedOn, owningUser) (NEW). When source-record has no matter linkage, omit `viaMatter`. When source-record has multiple membership roles (owner + assignedAttorney), `viaMatter.memberships` is array with one entry per role.

**Column dual-write requirement** (per owner clarification 2026-06-25 — Dataverse OData does NOT support JSON-nested `$filter`): producer MUST also write the standalone Dataverse column `sprk_category` on every `appnotification` row, mirroring `customData.category`. The two fields stay in sync. The widget consumes `customData.category` (existing); query filters (e.g., FR-17c disabledChannels) consume `sprk_category`. Audit at task-create: confirm executor already writes `sprk_category`; if not, add a writer.
- **AC-6a**: A notification produced by any migrated playbook surfaces all enriched customData fields when source data supports them (verified by xUnit test fixture)
- **AC-6b**: Backward compat: widget's existing `parseNotificationData` continues to work with old + new shape
- **AC-6c**: `appnotification.data` payload typical size <2KB; documented test fixture asserts <10KB ceiling
- **AC-6d**: Every `appnotification` row produced by R4-migrated playbooks has `sprk_category` populated (verified by xUnit test fixture + manual UAT Dataverse query)

**FR-7 — `notification-tasks-overdue` + `notification-tasks-due-soon` membership scope** — Both playbooks add parallel branch via `LookupUserMembership` so tasks regarding any matter the user is a member of surface alongside tasks they own directly. FetchXml union: `(ownerid eq-userid)` OR `(regardingobjectid IN membership-matterIds)`. Dedupe by activityid.
- **AC-7**: A user who is `assignedAttorney` on Matter-X but doesn't own any task on it sees overdue tasks on Matter-X in their Daily Briefing (verified by manual UAT in spaarkedev1)

**FR-8 — `notification-matter-activity` playbook implemented** — Replaces stub. Node graph: Start → LookupUserMembership(sprk_matter) → QueryDataverse (matters + projects + workassignments modified within last 24h where ID IN membership matters) → CreateNotification (one per modified record). Surfaces modifications + status changes.
- **AC-8**: When a matter the user is a member of has a modification within last 24h, an `appnotification` row appears in this category

**FR-9 — `notification-work-assignments` playbook implemented** — Replaces stub. Node graph: Start → LookupUserMembership(sprk_matter) → QueryDataverse (`sprk_workassignment` records linked to membership matters, created or updated within last 24h) → CreateNotification.
- **AC-9**: When a `sprk_workassignment` linked to user's memberships is created or updated, an `appnotification` row appears

**FR-10 — customData consistency across 7 playbooks** — All 7 playbooks emit the enriched FR-6 schema. Schema validation fixture passes for each playbook's notification output.
- **AC-10**: Test fixture passes asserting customData schema conformance for fixtures from all 7 playbooks

**FR-11 — Contact-only limitation logging** — When the membership service is asked to resolve and detects a Contact-typed membership lookup with `azureactivedirectoryobjectid` NULL (no SystemUser cross-ref), the producer logs a structured `member_skipped` warning event with fields: `matterid`, `contactid`, `role`, `reason: "no_systemuser_mapping"`.
- **AC-11**: Application Insights / log destination receives the structured warning when condition occurs; verified by integration test with a fixture matter having an unmappable Contact assignedAttorney

#### Workstream 2 — Consumer

**FR-12 — `/narrate` as playbook dispatch** — `DailyBriefingEndpoints.HandleNarrate` body replaced with a thin wrapper that invokes the playbook engine, passing the existing `DailyBriefingNarrateRequest` payload and dispatching to playbook code `BRIEF-NARRATE`. No C# string-literal prompts remain in the endpoint body. Response shape unchanged (TL;DR + channel narratives + generated UTC).

**Dispatch mechanism** — Per owner clarification 2026-06-25, evaluate the existing **`sprk_playbookconsumer` entity + service** built in [`work/spaarke-ai-platform-chat-routing-redesign-r1`](../spaarke-ai-platform-chat-routing-redesign-r1/) Phase 1R as the primary dispatch path. `sprk_playbookconsumer` maps playbooks to consumers (such as wizards); R4 extends it to support the Daily Briefing widget as a consumer. If `sprk_playbookconsumer` cannot accommodate the daily-briefing payload shape, fall back to direct invocation via `AnalysisOrchestrationService` (or sibling) with a degenerate playbook (no Dataverse query, just LLM + Tool nodes).
- **AC-12a**: Endpoint dispatches to playbook engine; no inline prompt construction remains
- **AC-12b**: Response shape backward-compatible — existing widget continues to work without changes to its parser
- **AC-12c**: Dispatch path documented in task notes — either `sprk_playbookconsumer`-mapped (preferred) or direct-invoke fallback (with rationale recorded)

**FR-13 — Grounded prompt content** — Action rows `BRIEF-NARRATE-TLDR` and `BRIEF-NARRATE-CHANNEL` have JPS `sprk_systemprompt` that: includes explicit system-role instruction ("You are a notification summarizer. You MUST only reference entity names, dates, and identifiers present in the provided input. If you cannot summarize a category from the data, write 'no items' or omit the bullet."), uses temperature 0, contains NO baked example names.
- **AC-13a**: Audit of `sprk_systemprompt` for both Action rows confirms absence of "Acme Corp", "Johnson & Lee", or any specific case/firm name as instructional examples
- **AC-13b**: Manual UAT with a test user whose data is entirely about "ACME Corporation" produces TL;DR that mentions only ACME (no Johnson & Lee LLP, no Davis v. Metro Transit)

**FR-14 — Entity name validation tool wired into playbook** — Playbook node graph includes `BRIEF-VALIDATE-ENTITY-NAMES` Tool node after `GenerateTldr` + `GenerateChannelNarratives`. The Tool builds an allow-list from input payload (every `regardingName`, every `viaMatter.name`, every `source.owningUser`), scrubs LLM output (TL;DR `summary`, `keyTakeaways`, `topAction`, each bullet's `narrative`), removes or replaces sentences mentioning entities not in the allow-list, logs structured warning for each scrub.
- **AC-14a**: Test fixture: LLM emits a fictional name → Tool scrubs it; output text no longer contains the name; `hallucination_detected` event logged
- **AC-14b**: Test fixture: LLM output uses only names from allow-list → Tool passes through unchanged

**FR-15 — Narration cache invalidation** — `useBriefingNarration.ts` refetches `/narrate` (now the playbook dispatch) when `channels` or `actionsRefresh` change. The `hasFetchedRef.current` cache is removed OR invalidated on `actionsRefresh` bump.
- **AC-15**: After user clicks Check / Remove / Keep in widget, a new `/narrate` call fires (visible in browser DevTools Network tab); TL;DR + bullets update to reflect current state

**FR-16 — Activity Notes fallback rendering** — When `channelNarratives` is empty (narration failed, returned empty, or playbook unavailable), `ActivityNotesSection` renders raw channel item lists (one card per notification, grouped by category) instead of returning `null`. Header notes "AI summary unavailable" when fallback triggered.
- **AC-16**: With mocked narration failure, all notification cards still render in their channels; "AI summary unavailable" banner present

**FR-17 — Preferences wired end-to-end (4 settings + 1 removal)**:
- **17a `timeWindow`**: `fetchNotifications` query includes `$filter` clause `createdon ge <now - window>`; window value mapped per setting (12h/24h/48h/7d)
- **17b `dueWithinDays`**: Filter applied either at fetchNotifications query (`customData.dueDate` if filterable) or post-fetch client filter; setting value drives boundary
- **17c `disabledChannels[]`**: `fetchNotifications` query includes `$filter` clause `sprk_category not in <disabled set>` — uses the existing **`sprk_category` custom column** on `appnotification` (NOT nested-JSON filter on `customData.category`, which Dataverse OData does NOT support per owner clarification 2026-06-25). Disabled channels do NOT reach the widget; they do NOT reach `/narrate` either (server-side filtering at query level). Pre-requisite: producer (`CreateNotificationNodeExecutor`) MUST write `sprk_category` field on every `appnotification` row in addition to `customData.category` (verify during W1.1 audit)
- **17d `autoPopup`**: Workspace launcher (`LegalWorkspaceApp` / `SpaarkeAi` shell) checks this on workspace mount; opens Daily Briefing tab automatically if true
- **17e `minConfidence`**: Removed from `PreferencesDropdown`, removed from `DailyDigestPreferences` interface, removed from default preferences object, removed from any persistence/serialization paths. Sweep `grep -rn minConfidence` post-implementation; expect zero references
- **AC-17a–d**: Each setting change produces visible difference in rendered content (manual UAT + Jest test for the wiring)
- **AC-17e**: `grep -rn "minConfidence\|AiConfidenceThreshold"` returns 0 results

**FR-18 — Three-dot overflow menu UX** — Per-item action UI in `NarrativeBullet.tsx` replaces inline 5-icon row with: (inline) regarding-name link + optionally 1 quick action (no inline action by default); (overflow `⋯`) menu with items: Mark as read, Remove from briefing, Keep on briefing for 7 more days, Add to To Do, Dismiss, Open record. Match Fluent v9 `Menu` / `MenuItem` patterns. Match semantic-search PCF list visual pattern. Touch targets meet WCAG.
- **AC-18a**: Visual audit: no inline action collision; overflow menu shows all 6 actions in defined order
- **AC-18b**: Accessibility: keyboard navigation works; screen reader announces menu items; dark mode renders correctly per ADR-021
- **AC-18c**: Existing "Add to To Do" (ADR-024 `useInlineTodoCreate` + `TODO_REGARDING_CATALOG`) works regression-free via overflow menu

**FR-19 — Link click → modal open with 403 fallback** — Clicking regarding-name invokes `Xrm.Navigation.navigateTo({pageType: 'entityrecord', entityName, entityId}, {target: 2, width: {value: 80, unit: '%'}, height: {value: 80, unit: '%'}})`. On promise rejection (Dataverse 403 or other), show non-blocking Fluent v9 toast "Cannot open record — you may not have access."
- **AC-19a**: Click → modal opens for user with Dataverse read access
- **AC-19b**: Click → toast shown (no error overlay) for user without Dataverse read access

**FR-20 — TL;DR ↔ Activities count match** — TL;DR's `totalNotificationCount` field equals number of items rendered in Activity Notes section. Reconcile via single source of truth: input payload to playbook is also displayed by widget.
- **AC-20**: Smoke test asserts equality with N items in 3 categories; widget header count equals visible card count

### Non-Functional Requirements

- **NFR-01**: No new HIGH-severity CVE (verify via `dotnet list package --vulnerable --include-transitive` + `npm audit --production`)
- **NFR-02**: BFF publish-size delta ≤ +1 MB (NEW `EntityNameValidatorNodeExecutor` + minor wiring; expect minimal NuGet adds)
- **NFR-03**: Unit + integration tests cover all FRs; ≥90% line coverage on changed files; widget jest tests use `jest-environment-jsdom`
- **NFR-04**: Widget action latency unchanged from R3 (optimistic UI ≤16ms; backend write ≤300ms p95); `/narrate` playbook dispatch latency ≤2s p95 (vs R2 baseline)
- **NFR-05**: Backward compatible — existing notification rows with old (pre-enrichment) `customData` shape continue to render without errors; widget treats missing fields as null
- **NFR-06**: All BFF-touching tasks pass §10 BFF Hygiene (publish-size + CVE verification per task; `code-review` + `adr-check` at task-execute Step 9.5). FULL rigor on all W0/W1/W2 code-change tasks; STANDARD rigor on data-deployment-only tasks (W0.4 reconcile-and-deploy).

---

## Technical Constraints

### Applicable ADRs

- **ADR-013** — BFF AI Architecture: All AI work stays in BFF. `/narrate` playbook dispatch follows established AnalysisOrchestrationService pattern. No new AI endpoints introduced beyond what JPS Actions/Playbooks naturally compose.
- **ADR-021** — Fluent v9 Design System: Three-dot overflow menu uses Fluent v9 `Menu` / `MenuItem` / icons; dark-mode required; semantic tokens only (no raw hex).
- **ADR-024** — sprk_todo Polymorphic Resolver: Existing `useInlineTodoCreate` + `TODO_REGARDING_CATALOG` preserved in overflow menu's "Add to To Do" action. Multi-entity regarding resolution unchanged.
- **ADR-027** — Subscription Isolation: `appnotification` is CORE entity. `sprk_briefingstate` Choice column (R3) preserved. No new schema in R4 (only customData JSON content evolves).
- **ADR-028** — Spaarke Auth v2: Contact ↔ SystemUser cross-ref via `azureactivedirectoryobjectid` is the canonical mapping mechanism. Membership service relies on it.
- **ADR-034** — User-record Membership Resolution Pattern: Built upon; not modified. `LookupUserMembership` ActionType 52 is the canonical primitive.

### MUST Rules

**JPS Data Layer (W0)**:
- ✅ MUST deploy `sprk_analysisaction` rows to spaarkedev1 for every new ActionType referenced by any playbook
- ✅ MUST follow `jps-action-create` skill for each Action row authored
- ✅ MUST follow `jps-playbook-design` skill for the new playbook
- ✅ MUST follow `jps-playbook-audit` skill when reconciling existing playbook configs
- ✅ MUST validate each JPS document via `jps-validate` before deployment
- ✅ MUST run `jps-scope-refresh` after W0 deployments complete
- ❌ MUST NOT skip JPS deployment ("code is shipped" is not sufficient — code + data both required)
- ❌ MUST NOT merge PR 2 (playbook configs) before PR 1 (Action rows) lands and propagates to spaarkedev1
- ✅ MUST use repo JSON files as canonical source-of-truth when reconciling against deployed `sprk_configjson`

**Producer (W1)**:
- ✅ MUST enrich customData backward-compatibly (null/missing fields gracefully handled)
- ✅ MUST use `LookupUserMembership` ActionType 52 in all 7 playbooks where membership applies
- ✅ MUST include explicit `viaMatter.memberships[]` array showing all roles user has on the matter
- ✅ MUST log structured `member_skipped` warning for Contact-only members
- ❌ MUST NOT skip the Contact-only logging (silently dropping members is the issue R4 documents)
- ❌ MUST NOT introduce a separate notification path for Contact-only members (email fallback is out of scope for R4)
- ✅ MUST preserve idempotency check (`CheckForDuplicateNotificationAsync`) when adding new playbook nodes

**Consumer (W2)**:
- ✅ MUST replace hardcoded prompt strings in `DailyBriefingEndpoints.cs` with playbook dispatch
- ✅ MUST use temperature 0 in `BRIEF-NARRATE-TLDR` and `BRIEF-NARRATE-CHANNEL` Action JPS prompts
- ✅ MUST add explicit grounding instruction in system prompt ("use ONLY names from input")
- ✅ MUST scrub LLM output via `EntityNameValidator` Tool before returning to widget
- ✅ MUST treat narration failure as a fallback path (raw channels render), NOT as a hard error
- ❌ MUST NOT bake legal-genre example names (Acme Corp, etc.) into JPS prompts
- ✅ MUST use server-side `$filter` for `disabledChannels` (not just UI hide)
- ✅ MUST remove `minConfidence` references from all layers (UI, types, persistence)
- ✅ MUST use Fluent v9 `Menu` patterns for overflow menu
- ❌ MUST NOT preserve inline 5-icon row (current UX collision must go)
- ✅ MUST preserve R3 deliverables: schema, BFF TTL fix, read-state derivation, 3 action functions (moved into overflow menu)

### Existing Patterns to Follow

- `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/QueryDataverseNodeExecutor.cs` — Canonical Dataverse-data-ops NodeExecutor pattern (closest analog for `EntityNameValidatorNodeExecutor`)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/LookupUserMembershipNodeExecutor.cs` — Sibling executor (already shipped); shows in-process service invocation pattern
- `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs` — Playbook engine entry point that `/narrate` wrapper will invoke
- `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/CreateNotificationNodeExecutor.cs:471-546` — `BuildNotificationEntity` is target for customData enrichment
- `projects/spaarke-daily-update-service/notes/playbooks/notification-new-documents.json` — Canonical migrated playbook (uses LookupUserMembership); template for tasks-overdue/due-soon migration
- `src/client/shared/Spaarke.DailyBriefing.Components/src/components/DailyBriefingApp.tsx:238-287` — `handleAddToTodo` reference for action handler pattern in overflow menu wiring
- `.claude/patterns/ui/fluent-v9-component-authoring.md` — Fluent v9 conventions for overflow menu UI
- Existing semantic-search PCF list component (referenced by user) — visual reference for three-dot overflow menu pattern

### Cross-cutting Constraints (CLAUDE.md)

- **§10 BFF Hygiene — Placement Justification**: `/narrate` migration from hardcoded endpoint to playbook dispatch + NEW `EntityNameValidatorNodeExecutor` are minimal additions to existing surface. NodeExecutor extends established framework. No new DI registration beyond standard NodeExecutor wiring. PR descriptions explicitly state placement justification per §10.
- **§10 BFF Hygiene — Publish-size verification**: Per-task BFF publish-size check on PR 1 (NEW NodeExecutor + Action wiring) and PR 4 (BFF wrapper endpoint). Expected delta ≤+0.1 MB on each.
- **§10 BFF Hygiene — CVE check**: `dotnet list package --vulnerable --include-transitive` on PR 1 and PR 4.
- **§11 Component Justification**: NEW `EntityNameValidatorNodeExecutor` justified — extends existing NodeExecutor pattern; no analog in current codebase; concrete failure mode without it (LLM hallucination names pass through; verified by R3 UAT). NEW `DAILY-BRIEFING-NARRATE` playbook justified — only path to grounded narration that fits architectural convention. NEW Action rows justified — JPS-required primitives.

---

## Success Criteria

R4 graduates when ALL of the following pass:

1. [ ] **JPS deployment complete** — Verify by: `mcp__dataverse__read_query` confirms all 4 new Action rows + 1 new playbook row + reconciled 7 notification playbook configs exist in spaarkedev1; `jps-scope-refresh` completes
2. [ ] **All 20 FRs deliver per spec** — Verify by: per-FR acceptance criteria pass (manual UAT + automated tests)
3. [ ] **All 6 NFRs pass** — Verify by: tests + CVE scan + publish-size measurement + perf measurement
4. [ ] **Hallucination eliminated** — Verify by: manual UAT with test user whose data is entirely about ACME Corporation; TL;DR contains NO Johnson & Lee LLP, NO Davis v. Metro Transit, NO names not present in source data; `hallucination_detected` events visible in logs when LLM does emit fictional names
5. [ ] **Activity Notes never disappears** — Verify by: clicking Check/Remove/Keep refreshes narration; never hides Activity section unless explicitly 0 notifications
6. [ ] **All 4 preferences work** — Verify by: Recency window change visibly filters; Due-soon change visibly filters; Disabled channel hidden from briefing AND from AI input; Auto-open triggers tab open
7. [ ] **Three-dot menu replaces inline buttons** — Verify by: visual screenshot review; accessibility audit; no two-checkmark collision
8. [ ] **Record link opens modal** — Verify by: click → modal opens; 403 case → toast shown
9. [ ] **Membership-scoped tasks-overdue/due-soon** — Verify by: test user with `assignedAttorney` role on Matter-X (not owner of any task) sees overdue tasks on Matter-X
10. [ ] **2 stub playbooks functional** — Verify by: PB-017 (Matter Activity) produces notifications on matter modifications; PB-022 (Work Assignments) produces notifications on workassignment activity
11. [ ] **Contact-only logging present** — Verify by: spaarkedev1 fixture with unmappable Contact → `member_skipped` event in Application Insights
12. [ ] **All R3 deliverables preserved** — Verify by: regression test — schema present, BFF TTL=604800 still written, read-state still uses sprk_briefingstate, 3 actions still functional (now via overflow menu)
13. [ ] **All 5 PRs merged in correct order** — Verify by: PR merge order audit (PR 1 → 2 → 3 → 4 → 5)
14. [ ] **BFF publish-size delta ≤ +1 MB**
15. [ ] **No new HIGH-severity CVE**

---

## Dependencies

### Prerequisites

- **R3 platform-foundations shipped** — `MembershipResolverService` + `LookupUserMembershipNodeExecutor` code present (verified). R4 deploys the JPS Action row that was missing.
- **R3 daily-update-service** — `sprk_briefingstate` Choice column deployed to spaarkedev1 (verified — R3 task 001 ✅); BFF NotificationService TTL fix shipped to dev (verified — R3 task 010 ✅). R3 PR #451 stays in draft and merges separately (R4 is independent; can be developed in parallel and merged in any order).
- **spaarkedev1 environment access** — operator + developer access for JPS deployments (W0)
- **PlaybookBuilder code page** — present in source; R4 doesn't modify it but verifies the new Action/Tool appear in palette post-W0 deployment
- **`@spaarke/daily-briefing-components` package** — present (R2 deliverable); R4 modifies its consumer code

### External Dependencies

- **None** — all infrastructure internal to Spaarke. No third-party APIs, services, or approvals needed.
- **Microsoft `appnotification`** entity is OOB Dataverse; `customData` JSON evolution is purely structural change to an existing field — no Microsoft dependency.
- **Azure OpenAI** continues to be the LLM backend per existing `IOpenAiClient` wiring; no new deployment/model requirements.

---

## Owner Clarifications

*Answers captured during 2026-06-25 architectural review + investigation Q&A:*

| Topic | Question | Answer | Impact |
|---|---|---|---|
| `/narrate` architecture | Hardcoded BFF endpoint OR JPS playbook? | Convert to JPS playbook | Restores architectural consistency; enables prompt iteration without recompile |
| `/narrate` AI Search index integration | Add knowledge node now or defer? | Defer to R5 | R4 ships Skill + Tool only |
| Insights Engine integration | Migrate Daily Briefing to Insights Engine? | No — keep `/narrate` as its own playbook | Insights Engine is matter-scoped + document-grounded; Daily Briefing is multi-matter + Dataverse-row-based |
| Recipient model | One row per User × source-record OR per-record with viewer list? | Per (User × source-record); customData carries memberships array | Forced by Dataverse owner-scoped row security |
| Contact-only members | Email fallback or skip silently? | Skip silently; document; log warning | R4 scope; email fallback is a separate R5 project if needed |
| tasks-overdue/due-soon scope | Owner-only or expand to membership? | Expand (union of owner + member-of) | Matches "comprehensive summary of everything I'm associated with" |
| Link click UAC fidelity | Defensive pre-check or 403 fallback? | 403 fallback (no pre-check) | Avoids doubling query cost for rare edge case |
| Stub playbooks (matter-activity, work-assignments) | Implement in R4? | Yes — both | Otherwise briefing is missing 2 channels |
| AI confidence threshold setting | Keep, fix, or remove? | Remove entirely | Vestigial; data is deterministic, no probabilistic AI scoring concept applies |
| Multi-role membership on single record | How surfaced in customData? | `viaMatter.memberships[]` array; UI shows "you're seeing this as: owner, assigned attorney" | Membership resolver returns matter under multiple `byRole` buckets |
| Membership data storage | Computed or stored? | Phase 1A computed (live FetchXml + Redis cache); Phase 2 junction table built but feature-gated OFF | R4 uses Phase 1A; doesn't change phase state |
| Membership UAC duplication | Concern? | None — membership and UAC are orthogonal (membership = activity targeting; UAC = CRUD permissions) | Document UAC fidelity gap (notification may exist for record user can't Dataverse-read) |
| Phasing (PR strategy) | 1 big PR or phased? | 5 phased PRs; W0 first, then producer, then consumer | Establishes JPS data before code consumes it; producer enrichment before consumer integration |
| JPS deployment as workstream | Implicit or explicit? | Explicit Workstream 0 | Direct response to discovery that ActionType 52 has 0 deployed Action rows in spaarkedev1 |
| Action codes naming convention | Conventions? | SYS-* for system data-ops; BRIEF-* for Daily Briefing | Mirrors existing pattern (SYS-QUERY-DV for QueryDataverse; INS-FETCH-KPI for Insights) |
| Dataverse OData `$filter` on nested customData JSON | Does Dataverse support `$filter=customData.category eq 'tasks-overdue'`? | **No, not supported.** Use the existing `sprk_category` custom column on `appnotification` for query filters. Producer writes both `sprk_category` (column) and `customData.category` (JSON) | FR-17c uses `sprk_category` for disabledChannels server filter. FR-6 adds AC-6d to verify producer dual-writes. |
| `/narrate` dispatch mechanism | Stateless playbook dispatch — supported by playbook engine? | **Investigate `sprk_playbookconsumer` pattern.** Recently built in [`work/spaarke-ai-platform-chat-routing-redesign-r1`](../spaarke-ai-platform-chat-routing-redesign-r1/) Phase 1R to map playbooks to consumers (such as wizards). R4 evaluates extending it to support the daily-briefing widget. If unsuitable, fall back to direct invocation with a degenerate playbook. | FR-12 task includes investigation of sprk_playbookconsumer entity + service before final dispatch design. AC-12c records dispatch path chosen + rationale. |
| PlaybookBuilder form for EntityNameValidator | Required? | Yes, follow existing patterns. Author `EntityNameValidatorForm.tsx` mirroring `LookupUserMembershipForm.tsx`. | FR-3 expanded with PlaybookBuilder UI form + AC-3c. |
| `EntityNameValidator` ExecutorActionType integer | 141 OK? | Yes, **141 confirmed** (follows recommendation; slots into post-LLM cluster). | FR-3 AC-3a uses 141. |
| Idempotency of JPS Action row deployment | Upsert or fail on existing? | Upsert pattern — if row exists, update it; if not, create. | W0.1, W0.2, W0.3 deployment scripts use Dataverse upsert (`PATCH` on key, fallback to `POST`). |
| Reconciliation direction for W0.4 | Deployed Dataverse rows vs repo JSON files — which wins? | Repo JSON files = canonical source-of-truth. W0.4 reads deployed, compares against repo, redeploys from repo if divergent. | FR-5 unchanged; assumption confirmed as decision. |

---

## Assumptions

*Proceeding with these assumptions (owner did not specify, R4 will document):*

- ~~**`EntityNameValidator` ExecutorActionType integer**: Assuming **141**~~ — **Confirmed by owner 2026-06-25; see Owner Clarifications.**
- ~~**Repo JSON files as source-of-truth**~~ — **Confirmed by owner 2026-06-25; see Owner Clarifications.**
- **`notification-matter-activity` activity types**: Assuming **modifications + status changes on Matter, Project, and WorkAssignment entities within last 24 hours** of run time. Final entity list + recency window may be tuned in implementation.
- **`notification-work-assignments` trigger**: Assuming **new or updated `sprk_workassignment` records within last 24 hours** for matters user is a member of. Activity types subject to implementation refinement.
- **Narration refetch debounce**: Assuming **200ms debounce** so a burst of action clicks triggers a single refetch when settled. Avoids excessive LLM calls.
- **`appnotification.data` payload typical size**: Assuming **<2KB** typical, **<10KB** ceiling. Microsoft Memo column supports much more (1MB default cap), so unconstrained.
- **`autoPopup` workspace launcher hook**: Assuming **`LegalWorkspaceApp` mount hook** is the right place to check this preference. If a different shell owns this, wiring point moves but behavior is the same.
- **Empty narration UI copy**: Assuming **"AI summary unavailable. Showing raw notifications below."** when fallback triggers. Owner may revise copy during UAT.
- **Three-dot menu icons**: Assuming Fluent v9 `MoreHorizontalRegular` for the overflow trigger; semantic icons inside (`CheckmarkRegular`, `DismissRegular`, `CalendarAddRegular`, `AddRegular`, `OpenRegular`). Owner may revise icons during UAT.
- **`disabledChannels` server filter syntax**: Assuming **`$filter=not(customData.category in ('cat1','cat2'))`** OData syntax. If Dataverse OData doesn't support `in` against nested JSON, fallback is `or`-joined `customData.category ne 'cat1' and customData.category ne 'cat2'`.

---

## Unresolved Questions

*All blocking architectural questions resolved 2026-06-25. Two implementation-time investigations remain (not blocking task decomposition):*

- [ ] **`sprk_playbookconsumer` dispatch path investigation** (FR-12): Read `work/spaarke-ai-platform-chat-routing-redesign-r1` Phase 1R commits + entity/service definitions; confirm whether daily-briefing widget can be registered as a consumer that dispatches to `DAILY-BRIEFING-NARRATE` playbook with structured payload. If yes, this is the dispatch path. If no, fall back to direct `AnalysisOrchestrationService` invocation with degenerate playbook (Start = no-op accepting payload). **Investigated during**: PR 4 task design (W2.1). **Not blocking** project-pipeline.

- [ ] **W0.4 reconciliation scope for ALREADY-MIGRATED playbooks (PB-016, PB-018, PB-019)**: Source JSON files at `projects/spaarke-daily-update-service/notes/playbooks/notification-new-{documents,emails,events}.json` reference LookupUserMembership. Are their deployed `sprk_configjson` (in spaarkedev1) already correct, or also out-of-sync? **Investigated during**: PR 2 W0.4 audit task. **Not blocking** task decomposition — discovery determines work volume, ranges from "all 3 fine" to "all 3 need redeploy."

**Resolved Questions** (moved to Owner Clarifications):
- ✅ Dataverse OData filter on nested JSON → use `sprk_category` column
- ✅ EntityNameValidator ExecutorActionType integer → 141 confirmed
- ✅ PlaybookBuilder form for EntityNameValidator → yes, follow LookupUserMembershipForm pattern
- ✅ Repo JSON vs Dataverse source-of-truth → repo JSON canonical
- ✅ JPS Action row deployment idempotency → upsert pattern

---

*AI-optimized specification. Original design: [design.md](design.md). Generated by `/design-to-spec` 2026-06-25.*
