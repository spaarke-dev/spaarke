# Copilot as a Spaarke surface, and the Frontier program (2026-11-05)

Owner questions:
1. Can Microsoft Copilot be a UI surface for attorneys who don't need the full Spaarke app?
2. Can it mount Console workspaces such as Decisions?
3. Can an agent now call the BFF? The April 2026 declarative-agent attempt stalled.
4. Should we use Frontier for early access?

Sources:
- researcher note `copilot-as-spaarke-surface-and-frontier-2026-11-05.md` (Learn pages read 2026-11-05);
- `projects/ai-m365-copilot-integration/current-task.md` (April 2026);
- `projects/customer-provisioning-orchestration-r1/notes/t257-copilot-agent-design.md`;
- `src/solutions/LegalWorkspace/src/sections/decisionRecords.registration.ts`.

Scope note: written in `spaarke-legal-ai-accelerator-r1` (where "design §10" means that project's design.md) and moved here as input for this project. The owner's "no declarative agents in the package" directive covers the accelerator package, not this project.

**Superseded in part by owner decisions of 2026-11-05** (see `mcp-project-research-starter.md` §1):
- v1 is a Decision Records list plus a read-only record modal, not the whole workspace;
- v1 users are Spaarke Power Apps–licensed users in the BFF's tenant, not customer workforce or contact users.

---

## 1. Verdict

**Yes, as a light surface: "ask, see, decide, act", with a deep link into the Console for heavy work. No, as a host for the whole Console.**

- **Widgets inside Copilot (MCP Apps) are GA:**
  - in Copilot web and desktop chat (declarative-agent actions);
  - in Cowork (inline or fullscreen).
- **Microsoft's UX guide says no to a whole product.** A widget that "resembles your entire SaaS product exceeds scope". It recommends 3–5 focused widgets plus an "open in app" link. Multi-tab shells are discouraged.
- **Teams, Outlook, the Word pane and mobile** are not documented as rendering third-party widgets. They need a test.

## 2. Mounting the Decisions workspace

**Feasible at widget level.** The seam already exists. `DecisionRecordsWidget` (`@spaarke/ui-components`) receives its data service from `createWorklistService({ fetch, baseUrl })`, so it never calls Xrm or Dataverse itself.

Inside a Copilot widget:

| Console today | Copilot widget |
|---|---|
| `authenticatedFetch` (MSAL token) → BFF Signal read route `?set=decided` | A `fetch` adapter that calls `app.callServerTool("spaarke_list_decisions", …)`. Copilot attaches the user's auth; our MCP tool calls the **same** route **as the caller**. |
| Code page bundle | A single-file HTML build (scripts and styles inline), within the 10-second init limit |
| `launchDecisionWizard` via `DecisionWizardHost` | Read-only view in the widget. **Commit** a decision through a write tool that Copilot asks the user to approve; validation stays server-side (ADR-002). Or use `openLink` to the Console's wizard. |
| Theme sync, `sessionStorage` sentinels, `navigateTo` | Theme from the host context; storage is scoped to our widget origin. Navigation becomes `openLink` to the Console. |

**Not portable as-is:** the whole Console or `LegalWorkspaceApp`. Its embedded-mode contract needs `Xrm.WebApi`, a Dataverse user id, MSAL and `navigateTo`, none of which exist in a widget.

**Widget candidates (3–5):**
1. My worklist (the decisions due);
2. Decision record viewer;
3. Matter snapshot;
4. Document find-and-open;
5. Service-request intake (design §9.2).

## 3. Calling the BFF from an agent: what changed since April

| April problem | Status now |
|---|---|
| Sign-in | **It was already solved in April:** OAuth through the token store, `OAuthPluginVault`. |
| BFF lacked OBO for downstream calls | **Our side.** Since then, routes read **as the caller** (Decision Records, unified-access-control-r2). Check that each tool's route does. |
| Copilot invented parameters → 400s | **No documented fix.** Mitigations: few narrow tools with tight schemas and descriptions ≤ 1,024 characters (longer ones are ignored); the server returns errors that tell the model how to fix the call; measure with a 20-prompt eval. |
| A confirmation card on every read | **Fixed:** `readOnlyHint: true` on MCP tools, or `x-openai-isConsequential: false` in OpenAPI. The consent card on first use remains. |
| Copilot preferred native Dataverse knowledge | **No documented fix.** Leave Dataverse knowledge out of the agent, so only our tools can answer. |
| `devPreview` manifest and BOM upload fragility | **Fixed:** numbered schemas support MCP: DA v1.9, plugin v2.4, app manifest v1.30. Our deploy script now writes UTF-8 without BOM. |

**Three ways to register an MCP server. They don't share registration:**

| Model | Reach | Licence | Notes |
|---|---|---|---|
| **Declarative-agent action** (`RemoteMCPServer`) | Inside that agent: Copilot Chat, and widgets in web and desktop | **No Copilot licence and no metering**, as long as the agent declares no tenant-data knowledge | Must be installed in the **customer's home tenant** (guests can't use Spaarke's catalog; T257) |
| **Plugin with `agentConnectors`** | Cowork | Cowork spending policy; credits per tool call | Needs a static tool-description file |
| **Federated connector** (admin-created or Connectors Gallery) | Chat, agents, Cowork, **Word, Excel, PowerPoint, Outlook** | Copilot licence | The Harvey, iManage and Litera route into Word. Tools only, no instructions. Widget rendering is unknown. |

The first row matters for "attorneys who don't need the full app". **Any attorney with Copilot Chat (no Copilot add-on) could use a Spaarke agent with widgets**, at no metered cost.

Entra has no dynamic client registration, so we use static OAuth or Entra SSO (Spaarke's tenant authority, PKCE public client, per T257).

## 4. Frontier

- **What it is.** A tenant opt-in for early Microsoft 365 Copilot features: admin center › Copilot › Settings › Copilot Frontier, set to None, All or Specific groups. It needs an AI, Security or Office Apps admin, and a Copilot licence per feature. From November 2026 the admin opting in no longer needs a Copilot licence.
- **Terms.** Features can change or be withdrawn. No SLA, no HIPAA BAA, no ISO 27001/27002/27018.
- **How fast features graduate:**
  - Cowork: GA 2026-06-16;
  - Office in Copilot: Frontier 2026-10-09 → GA mid-November;
  - the Legal Agent: **retired**, replaced by Copilot for legal (GA mid-October).
- **Not an ISV programme.** Frontier Accelerate for Marketplace (2026-09-28) is a commercial and marketplace programme (secondary source). Technical early access for partners (TAP, private previews) is unknown; ask our Microsoft partner contact.

**Recommendation.**
- Turn Frontier on in the **dev tenant only**, scoped to a group, with no customer data. Use it to see what's coming and test early.
- **Never** make a customer-facing feature depend on a Frontier item (e.g., the App skill).
- Re-check at Ignite (17–20 November).

## 5. Tests that settle the unknowns (connector project spike)

1. Run the trey-research sample (`knowledge/mcp-apps/`) as an agent. Does the widget render in Teams, the Word pane, the Outlook pane and mobile?
2. Register the same server as a Cowork `agentConnectors` plugin and as a federated connector. Does the widget render in each?
3. A single-file React + Fluent v9 Decisions widget at 1, 3 and 6 MB: does it beat the 10-second init in both hosts?
4. A 20-prompt eval, MCP vs OpenAPI: count wrong-parameter calls and fallbacks to native Dataverse knowledge.
5. Reads with and without `readOnlyHint`; `confirmation.type: None` on plugin v2.4.
6. An "Any tenant" OAuth config created in Spaarke's tenant, used by an agent installed in a second tenant (T257 unknown 3b).
7. A federated connector used by an unlicensed user; Cowork used by a user who is in a spending policy but has no Copilot licence.
8. `Action.OpenUrlDialog` (preview) from an MCP plugin: does it open a Console page in a dialog?

**Watch at Ignite:**
- widgets in Teams, Outlook and mobile;
- `Action.OpenUrlDialog` GA and MCP support for it;
- widgets for federated connectors;
- BYO-MCP certification (RM576752);
- the App skill.

## 6. What it would take (connector project)

1. **An MCP server.** The BFF has none today. It would be new BFF surface, so it needs CLAUDE.md §10's placement decision, the §11 justification and the publish-size check.
2. **Tools** that wrap existing routes, which already run as the caller. Reads carry `readOnlyHint`. Writes go through the existing server-side write path.
3. **Widgets:** single-file builds of existing `@spaarke/ui-components` widgets, each with a `fetch` adapter over `callServerTool`.
4. **Packaging:** a per-customer agent (the T257 template), plus optional Gallery publication as a federated connector for Word, Outlook and Cowork reach.
