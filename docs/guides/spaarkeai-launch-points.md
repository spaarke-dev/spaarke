# SpaarkeAi Launch Points

> **Last Updated**: 2026-05-16
> **Status**: Current
> **Web Resource**: `sprk_spaarkeai`

This guide documents all four supported launch points for the SpaarkeAi standalone Code Page (`sprk_spaarkeai`). Each launch point opens the three-pane AI workspace with an optional entity context that scopes the conversation, playbooks, and tools to a specific Dataverse record.

---

## URL Parameter Contract

All launch points use the same query parameter contract. All parameters are optional.

| Parameter | Type | Description |
|-----------|------|-------------|
| `entityLogicalName` | string | Dataverse logical name of the entity (e.g. `sprk_matter`, `contact`) |
| `entityId` | GUID string | Record GUID. Braces are stripped if present (`{abc-123}` → `abc-123`). |
| `matterId` | GUID string | Shorthand matter GUID — used by the M365 Copilot handoff action. When present, `StandaloneAiProvider` resolves entity context using this ID as the matter record GUID without requiring `entityLogicalName`. |

When no parameters are passed, the AI opens in general (unscoped) mode.

---

## Launch Point 1: Workspace Command Bar Button

**Trigger**: Global navigation command bar in the Spaarke workspace (not entity-specific).

**Behaviour**: Opens SpaarkeAi as a **full-page navigation** (`target: 1`) with no entity context. The AI assistant loads in general mode, ready for a new conversation.

**Ribbon Script**: `sprk_spaarkeai_workspacelaunch` web resource
**Function**: `Sprk.SpaarkeAi.WorkspaceLaunch.openFromWorkspace()`
**Source**: `src/solutions/SpaarkeAi/src/ribbon/WorkspaceLaunch.ts`

**Ribbon XML snippet**:

```xml
<CommandDefinition Id="Sprk.SpaarkeAi.Workspace.Command">
  <EnableRules />
  <DisplayRules />
  <Actions>
    <JavaScriptFunction
      Library="$webresource:sprk_spaarkeai_workspacelaunch"
      FunctionName="Sprk.SpaarkeAi.WorkspaceLaunch.openFromWorkspace" />
  </Actions>
</CommandDefinition>
```

**Effective URL**: `sprk_spaarkeai` (no `data` parameter — no entity context)

---

## Launch Point 2: Entity Form Command Bar Button

**Trigger**: Command bar on any Dataverse entity form where the button has been added (e.g. Matter form, Contact form, Document form).

**Behaviour**: Opens SpaarkeAi as a **modal dialog** (`target: 2`, 90% × 90%) pre-seeded with the currently open record's `entityLogicalName` and `entityId`. The `StandaloneAiProvider` resolves playbooks, tools, and quick actions scoped to the entity type and record.

**Ribbon Script**: `sprk_spaarkeai_entityformlaunch` web resource
**Function**: `Sprk.SpaarkeAi.EntityFormLaunch.openFromEntityForm(primaryControl)`
**Source**: `src/solutions/SpaarkeAi/src/ribbon/EntityFormLaunch.ts`

**CrmParameter**: `PrimaryControl` — passes the Xrm `FormContext` to the function

**Ribbon XML snippet**:

```xml
<CommandDefinition Id="Sprk.SpaarkeAi.EntityForm.Command">
  <EnableRules />
  <DisplayRules />
  <Actions>
    <JavaScriptFunction
      Library="$webresource:sprk_spaarkeai_entityformlaunch"
      FunctionName="Sprk.SpaarkeAi.EntityFormLaunch.openFromEntityForm">
      <CrmParameter Value="PrimaryControl" />
    </JavaScriptFunction>
  </Actions>
</CommandDefinition>
```

**Effective URL for a matter record**:

```
sprk_spaarkeai?entityLogicalName=sprk_matter&entityId=3fa85f64-5717-4562-b3fc-2c963f66afa6
```

**Supported entities** (extensible — any entity works with generic context):

| Entity Logical Name | Context loaded |
|---------------------|---------------|
| `sprk_matter` | Matter playbooks, scoped document search, matter tools |
| `contact` | Contact-scoped tools and document context |
| `sprk_document` | Document viewer pre-loaded in source pane |
| *(any entity)* | Generic entity context: entity type + ID available to playbooks |

---

## Launch Point 3: Deep-Link URL (External Systems)

**Trigger**: External systems (integrations, emails, SharePoint links, browser bookmarks) that need to open SpaarkeAi for a specific record without going through Dataverse navigation.

**Behaviour**: The web resource is opened directly via browser URL. Xrm is NOT available in this context — the page handles missing Xrm globals gracefully. No `Xrm.Navigation.navigateTo` is called; the entity context comes from the URL query string directly.

**URL format**:

```
https://{org}.crm.dynamics.com/WebResources/sprk_spaarkeai?entityLogicalName={entity}&entityId={guid}
```

**Example — open SpaarkeAi for a specific matter**:

```
https://spaarkedev1.crm.dynamics.com/WebResources/sprk_spaarkeai?entityLogicalName=sprk_matter&entityId=3fa85f64-5717-4562-b3fc-2c963f66afa6
```

**Example — open SpaarkeAi for a contact record**:

```
https://spaarkedev1.crm.dynamics.com/WebResources/sprk_spaarkeai?entityLogicalName=contact&entityId=7c9e6679-7425-40de-944b-e07fc1f90ae7
```

**Example — open SpaarkeAi with no entity context (general mode)**:

```
https://spaarkedev1.crm.dynamics.com/WebResources/sprk_spaarkeai
```

**Implementation note**: `src/solutions/SpaarkeAi/src/main.tsx` parses `window.location.search` directly. Parameters passed via `Xrm.Navigation.navigateTo` arrive via the `data` query parameter (URL-encoded), while direct deep-links pass parameters at the top level of the query string. `main.tsx` checks both locations:

```typescript
const searchParams = new URLSearchParams(window.location.search);
const dataParam = searchParams.get("data");
const dataParams = new URLSearchParams(dataParam ? decodeURIComponent(dataParam) : "");
const entityLogicalName = searchParams.get("entityLogicalName") ?? dataParams.get("entityLogicalName");
const entityId = searchParams.get("entityId") ?? dataParams.get("entityId");
```

---

## Launch Point 4: M365 Copilot Handoff

**Trigger**: The Spaarke Declarative Agent in M365 Copilot invokes a handoff action when a user's request requires deep analysis that is better handled in the full SpaarkeAi workspace.

**Behaviour**: The Declarative Agent constructs a deep-link URL using the `matterId` parameter shorthand. Unlike `entityLogicalName` + `entityId`, `matterId` is a purpose-built parameter for the Copilot handoff scenario — it signals to `StandaloneAiProvider` that the session should be scoped to a specific matter without requiring the caller to know Dataverse logical names.

**URL format**:

```
https://{org}.crm.dynamics.com/WebResources/sprk_spaarkeai?matterId={matterGuid}
```

**Example**:

```
https://spaarkedev1.crm.dynamics.com/WebResources/sprk_spaarkeai?matterId=3fa85f64-5717-4562-b3fc-2c963f66afa6
```

**Adaptive Card action for M365 Copilot agents** (in `cards/handoff-card.json` or inline agent response):

```json
{
  "type": "Action.OpenUrl",
  "title": "Open in SpaarkeAi",
  "url": "https://spaarkedev1.crm.dynamics.com/WebResources/sprk_spaarkeai?matterId={{matterId}}"
}
```

**Declarative Agent handoff action** (in `spaarke-api-plugin.json`):

```json
{
  "name": "handoffToSpaarkeAi",
  "description": "Opens the SpaarkeAi deep analysis workspace for a specific matter. Use this when the user needs to do complex multi-document analysis, run an AI playbook, or work with AI-generated charts and structured outputs.",
  "parameters": {
    "type": "object",
    "properties": {
      "matterId": {
        "type": "string",
        "description": "The GUID of the matter record to open in SpaarkeAi."
      }
    },
    "required": ["matterId"]
  }
}
```

**How SpaarkeAi handles matterId**:

When `StandaloneAiProvider` detects a `matterId` parameter (passed from `main.tsx` via the `entityContext` prop), it:
1. Sets `entityContext.entityType = "sprk_matter"`
2. Sets `entityContext.entityId = matterId`
3. Requests scoped playbooks and tools from the BFF `/api/ai/chat/context-mappings/standalone` endpoint using the resolved matter context

This means the AI session opens identically to an entity form launch from a matter record, without requiring the user to navigate to the matter form first.

---

## Parameter Resolution in main.tsx

The `src/solutions/SpaarkeAi/src/main.tsx` entry point resolves parameters from two sources:

1. **Xrm.Navigation.navigateTo** passes parameters inside the `data` query parameter (URL-encoded string)
2. **Direct deep-links** pass parameters as top-level query string values

Resolution order (first non-null wins):

```
entityLogicalName = queryString["entityLogicalName"] ?? dataString["entityLogicalName"]
entityId          = queryString["entityId"]          ?? dataString["entityId"]
matterId          = queryString["matterId"]           ?? dataString["matterId"]
```

All resolved values are passed as props to the `<App />` component, which forwards them to `StandaloneAiProvider`.

---

## All Parameters at a Glance

| Launch Point | Target | Parameters | Source of Entity Context |
|---|---|---|---|
| Workspace command bar | Full page (`target: 1`) | *(none)* | None — general mode |
| Entity form command bar | Modal dialog (`target: 2`) | `entityLogicalName` + `entityId` | Xrm `FormContext.data.entity` |
| Deep-link URL | Direct browser | `entityLogicalName` + `entityId` | URL query string |
| M365 Copilot handoff | Direct browser | `matterId` | URL query string (agent constructs URL) |

---

## Related Files

| File | Purpose |
|------|---------|
| `src/solutions/SpaarkeAi/src/utils/launch-resolver.ts` | URL assembly + `openSpaarkeAi()` function |
| `src/solutions/SpaarkeAi/src/ribbon/WorkspaceLaunch.ts` | Workspace ribbon script (invocation only) |
| `src/solutions/SpaarkeAi/src/ribbon/EntityFormLaunch.ts` | Entity form ribbon script (invocation only) |
| `src/solutions/SpaarkeAi/src/ribbon/xrm-globals.d.ts` | Minimal ambient Xrm type declarations |
| `src/solutions/SpaarkeAi/src/main.tsx` | URL parameter parsing and App bootstrap |
| `src/client/shared/Spaarke.AI.Context/src/providers/StandaloneAiProvider.tsx` | Entity context resolution from URL params |

---

*Document for SpaarkeAi Launch Points — Phase 1, Wave 4 (AIPU-041)*
