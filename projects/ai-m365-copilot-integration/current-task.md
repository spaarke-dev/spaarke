# Current Task State - M365 Copilot Integration (R1)

> **Last Updated**: 2026-03-28 08:00 (by context-handoff)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | API Plugin Auth Flow - Copilot cannot call BFF API |
| **Step** | Blocked: Auth token exchange between Copilot and BFF API |
| **Status** | blocked |
| **Next Action** | Research Microsoft's exact OAuth flow for Declarative Agent API Plugins. The current setup sends tokens that the BFF rejects with 401. Need to determine: what audience/issuer does Copilot use, and how to configure BFF to accept it. |

### Files Modified This Session (across 2 days)
- `src/server/api/Sprk.Bff.Api/Api/Agent/*.cs` - 11 agent service files (endpoints, handler, formatter, auth, conversation, playbook, email, status, config, error, telemetry)
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AgentModule.cs` - DI registration for all agent services
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/EndpointMappingExtensions.cs` - MapAgentEndpoints + debug/token endpoint
- `src/server/api/Sprk.Bff.Api/Configuration/AgentTokenOptions.cs` - OBO token config
- `src/server/api/Sprk.Bff.Api/Program.cs` - AddAgentModule registration
- `src/server/api/Sprk.Bff.Api/appsettings.template.json` - AgentToken + CopilotAgent sections
- `src/solutions/CopilotAgent/declarativeAgent.json` - Agent instructions with vocabulary mapping
- `src/solutions/CopilotAgent/spaarke-api-plugin.json` - API Plugin with runtimes/functions (auth: None)
- `src/solutions/CopilotAgent/spaarke-bff-openapi.yaml` - OpenAPI spec (35+ operations)
- `src/solutions/CopilotAgent/appPackage/manifest.json` - Teams manifest with webApplicationInfo
- `src/solutions/CopilotAgent/cards/*.json` - 10 Adaptive Card templates
- `infrastructure/bot-service/main.bicep` - Bot Service Bicep template
- `infrastructure/byok/` - BYOK deployment templates
- `tests/unit/Sprk.Bff.Api.Tests/Api/Agent/*.cs` - 68 unit tests
- `scripts/Update-CopilotEntityDescriptions.ps1` - Entity description updater
- `scripts/Configure-CopilotKnowledge.ps1` - Glossary terms + synonyms configurator
- `scripts/Deploy-CopilotAgent.ps1` - Full deployment orchestrator
- `docs/guides/` - 5 documentation guides

### Critical Context

**The blocker**: M365 Copilot's Declarative Agent with API Plugin is deployed and visible in MDA Copilot and Teams. The agent responds using its Spaarke instructions. App Insights confirms Copilot DOES attempt to call our BFF API (GET /api/v1/events?status=overdue), but gets 401. The BFF's JWT middleware (AddMicrosoftIdentityWebApi) rejects the token. We pre-authorized the Copilot Bot app (f257a0a9) in the BFF's Entra app registration (1e40baad), but tokens are still rejected.

**What we've tried that didn't work**:
- webApplicationInfo in manifest.json (added, Copilot attempts API calls but gets 401)
- Pre-authorizing Copilot Bot app in BFF Entra app registration (done, still 401)
- Adding ValidAudiences config to App Service (added, no effect)
- Anonymous endpoints (proved pipeline works but unsafe, reverted)

**What needs investigation**:
1. What exact audience/issuer does the Copilot token have? (debug/token endpoint is deployed but needs Copilot to call it)
2. Does API Plugin auth require "OAuthPluginVault" with a registered OAuth connection in Teams Developer Portal? (Teams Dev Portal doesn't show this option clearly for Declarative Agents)
3. Should we use Copilot Studio to configure the API connection instead of manifest-only approach?
4. Is there a Microsoft-specific token audience for API Plugins that we need to accept?

---

## Full State (Detailed)

### Project Progress: 23/31 Tasks Complete (74%)

**Completed Tasks**: 001, 002, 006-008, 010-014, 016-023, 024-029
**Remaining Tasks**: 003-005 (spikes), 009 (validation), 015 (integration tests), 030 (E2E), 031 (wrap-up)

### Infrastructure Deployed

| Component | Status | Details |
|-----------|--------|---------|
| BFF API | Live | https://spe-api-dev-67e2xz.azurewebsites.net (auth restored) |
| Bot Service | Live | spaarke-bot-dev in spe-infrastructure-westus2 |
| Entra App (Bot) | Registered | f257a0a9-1061-4f9b-8918-3ad056fe90db |
| Entra App (BFF) | Existing | 1e40baad-e065-4aea-a8d4-4b7ab273458c |
| Teams App | Uploaded v1.0.4 | Spaarke AI in org app catalog |
| App Service Config | Set | AgentToken + CopilotAgent sections configured |
| Entity Descriptions | Applied | 9/10 Spaarke entities updated |
| Glossary Terms | Applied | 15 terms in CopilotGlossaryTerm table |
| Column Synonyms | Applied | 13 synonyms in CopilotSynonyms table |

### Key Entra App Details

| App | App ID | Purpose |
|-----|--------|---------|
| SDAP-BFF-SPE-API | 1e40baad-e065-4aea-a8d4-4b7ab273458c | BFF API (token audience) |
| Spaarke Copilot Bot Dev | f257a0a9-1061-4f9b-8918-3ad056fe90db | Bot/Copilot agent identity |

BFF API accepts tokens with audience: api://1e40baad-e065-4aea-a8d4-4b7ab273458c
Copilot Bot app is pre-authorized for user_impersonation + SDAP.Access scopes.
webApplicationInfo in manifest points to BFF app: api://1e40baad-e065-4aea-a8d4-4b7ab273458c

### App Insights Evidence

Copilot IS attempting API calls (confirmed via App Insights query):
```
GET /api/v1/events?status=overdue           -> 401
GET /api/v1/events?assigneeId=me&status=overdue -> 401
```

These come from the Copilot API Plugin invoking our listEvents function. The 401 means the BFF's JWT middleware rejects the token before it reaches application code.

### Git State

Branch: work/ai-m365-copilot-integration-r1
Remote: pushed and up to date
Last commit: 7ba6efc5 - "fix(copilot): restore auth on agent endpoints + add debug/token endpoint"
Working tree: clean

### Documentation Created

| Guide | Path |
|-------|------|
| Deployment Guide | docs/guides/M365-COPILOT-DEPLOYMENT-GUIDE.md |
| Architecture Overview | docs/architecture/M365-COPILOT-INTEGRATION-ARCHITECTURE.md |
| User Guide | docs/guides/M365-COPILOT-USER-GUIDE.md |
| Admin Guide | docs/guides/M365-COPILOT-ADMIN-GUIDE.md |
| Build & Deploy Lessons | docs/guides/DECLARATIVE-AGENT-BUILD-AND-DEPLOY-GUIDE.md |
| Knowledge Configuration | docs/guides/COPILOT-KNOWLEDGE-CONFIGURATION-GUIDE.md |

### Recovery Instructions

1. Read this file for current state
2. The blocker is AUTH - Copilot tokens rejected by BFF
3. Research: Microsoft docs on API Plugin OAuth for Declarative Agents
4. Key question: What token audience does Copilot's API Plugin use?
5. The /debug/token endpoint is deployed - if Copilot calls it, it will reveal the token claims
6. Alternative: Configure API connection via Copilot Studio instead of manifest-only

---

*Checkpoint saved by context-handoff skill*
