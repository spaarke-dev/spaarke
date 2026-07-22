# 050 build plan — "what are my tasks?" → My Tasks Workspace grid tab

> Owner-approved 2026-07-22 (FR-J1 approach sign-off): **capability only** (no Quick Start card / header button).
> Scope: Event-type **Task** (`sprk_event`), **assigned to me (owner), open only**, surfaced in a **Workspace tab**.
> Verified feasibility via the workspace-grid seam map (Explore agent, 2026-07-22).

## Key finding — NO BFF code change
- The generic grid widget already exists: `DataverseEntityViewWidget` → shared `<DataGrid configId=…/>` (`@spaarke/ai-widgets`, consumed by SpaarkeAi). The `documents-list` / `matters-list` / … tabs are this widget with a baked `configId`.
- "Assigned to me" = native FetchXML **`eq-userid`** operator (resolved to the caller server-side; supported by `DataGrid/fetchXmlOverlay.ts:68,203`). No dynamic-token machinery, no BFF query handler — the grid queries Dataverse itself (Xrm.WebApi, caller privileges).
- `surface_launch` SSE emit is **generic** (`BindingCapabilityTool.cs:318-352` + `SessionDispatchOrchestrator.cs:628-631`): any Binding with `disposition=surface_launch` emits the event with its `consumerType` + payload. The chunk disposition = the Binding's declared disposition (`storedEntry.Disposition`), NOT the Action's content. So `list-tasks` routes through the existing path.
- The code already anticipates this: `BindingCapabilityTool.cs:306-308` names "list-tasks grid" as a planned client surface.

## Build pieces (all data + client; no BFF deploy)

### 1. Dataverse DATA (live via MCP / Web API — no deploy)
- **Saved query** on `sprk_event`: FetchXML filter = `sprk_eventtype_ref = 124f5fc9-98ff-f011-8406-7c1e525abd8b` (Task subtype, GUID known — `surfaceLaunchRegistry.ts:29`) AND `statecode = 0` (open) AND `<condition attribute='ownerid' operator='eq-userid'/>` (assigned to me). ⚠️ Verify the "assigned to" attribute on `sprk_event` is `ownerid` vs a custom user lookup — `eq-userid` works on any user-lookup attribute.
- **`sprk_gridconfiguration` row**: `sprk_configjson` = `DataGridConfiguration` v1.0 with `source.type='savedquery', savedQueryId=<the saved query>`. Record the GUID → `MY_TASKS_CONFIG_ID`.

### 2. Client code (SpaarkeAi + shared lib @spaarke/ai-widgets)
- **Widget registration**: add a `my-tasks-list` type in `register-workspace-widgets.ts` via `createEntityViewFactory(MY_TASKS_CONFIG_ID)` (mirror `work-assignments-list`, ~10 lines). (Alt: dispatch an existing `*-list` type with `widgetData.configId` override — the factory honors caller `configId`, `:645-648`.)
- **Client surface-launch branch**: in `ConversationPane.handleSurfaceLaunch`, branch `if (consumerType === 'list-tasks') dispatch("workspace", {type:"widget_load", widgetType:"my-tasks-list", widgetData:{}, displayName:"My Tasks"})` and return — do NOT call `launchSurface` (it only handles wizard/oob-form; `launchSurface.ts:224` bails on workspace-tab). Same for the Click path if a chip ever carries it.

### 3. Catalog DATA (Action + Binding — live via MCP; OWNER REVIEWS toolDescription per FR-J1)
- **Action** `LIST-TASKS@v1` (`sprk_analysisaction`): minimal prompted Action — output schema trivial (e.g. `{ acknowledgement: string }`); systemPrompt: acknowledge in one line that the user's tasks are opening; never fabricate task details. Input schema minimal (request string, mirror-first). Needs input+output schema rows (contract-validated by CatalogInputSchemaContractTests + jps-validate).
- **Binding** `list-tasks` (`sprk_playbookconsumer`): `disposition = surface_launch (100000007)`, `surfaces = assistant`, `actionCode = LIST-TASKS@v1`, `enabled = true`, `risk`/`captureMode` per siblings, `chipTransitions` optional (e.g. a "Create a task" next-step chip). **toolDescription (OWNER REVIEW)**: "Use when the user asks to SEE / list / review THEIR tasks, to-dos, or open items — e.g. 'what are my tasks', 'show my open tasks', 'what's on my plate'. Opens the user's task list. Do NOT use to CREATE a task (that is create-task) or for another person's tasks."
  - One small LLM call per invocation (the Action) — accepted tradeoff vs. a coded workflow (which would be BFF code). Fully ADR-039 (server owns the routing decision; client only opens the tab).

### 4. Ambiguity-set authoring (the other half of 050, FR-J1 review)
- Enrich `sprk_tooldescription` for the narrow high-frequency ambiguity set: file / open / close / matter + **To Do vs Event-Task** cues, so the agent disambiguates without a classifier (ADR-039 ambiguity-in-descriptions). Draft edits to the relevant Binding toolDescriptions; owner reviews.

### 5. Eval debt (feeds 051)
- Add to `owed-eval-cases.md`: positive ("what are my tasks?" → `list-tasks`), disambiguation ("create a task" → create-task NOT list-tasks; "what are my tasks" → list-tasks NOT create-task), negative (a create utterance never selects list-tasks). Surface-open (grid tab) verified client-side.

## Deploy
- Client (widget + branch) → rebuild + deploy `sprk_spaarkeai` (+ any wizard consumer? no — SpaarkeAi only). No BFF deploy.
- Catalog rows (Action/Binding/schemas) + grid config + saved query → live via MCP/Web API (no deploy). Prod parity deferred (054 = dev only per owner).
- Owner reviews toolDescription content (FR-J1) before marking 050 ✅.

## Open verifications (during build)
1. `sprk_event` "assigned to" attribute (`ownerid` vs custom lookup) + open `statecode` value.
2. That a minimal prompted `LIST-TASKS@v1` Action dispatches cleanly to a `surface_launch` chunk (mirror create-task; confirm the prompted executor accepts a trivial output schema).
3. `DataGridConfiguration` v1.0 exact JSON shape for a `savedquery` source (from `types/DataGridConfiguration.ts`).
