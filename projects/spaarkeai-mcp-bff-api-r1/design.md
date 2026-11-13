# design.md — Spaarke in Microsoft Copilot and Cowork: MCP server in the BFF (r1)

> **Status:** 🟢 Owner-approved for spec (2026-11-05, third review round). Input to `/design-to-spec`.
> **Created:** 2026-11-05 · **Author:** Claude Code, from the owner's review of the research starter (2026-11-05) and four investigations run the same day:
> - BFF component model (Fable);
> - Decision domain read and write path (Fable);
> - registration model research (researcher, Microsoft Learn primary sources);
> - shared UI and Visual Host portability (code-mapper).
>
> **Next step after sign-off:** iterate this doc → `/design-to-spec` → `/project-pipeline`.
> **Supersedes** the scope parts of `notes/mcp-project-research-starter.md` §1, §3, §4.5 and §10, and `notes/copilot-as-spaarke-surface-2026-11-05.md` §1–§3. The platform facts in those notes still stand unless corrected here.

---

## 0. Owner decisions so far (2026-11-05)

| # | Decision |
|---|---|
| O-1 | **Goal:** bring Spaarke's legal capabilities into the Copilot and Cowork ecosystem, so users get Spaarke *and* Microsoft Work IQ in the same flow. Do not rebuild Spaarke in Copilot; do reuse Spaarke's shared components and design concepts so the experience is consistent. |
| O-2 | **v1 includes the write:** a grid of the user's decisions, the **Decision modal** (the decision wizard, not a read-only viewer), and completing the decision including entering form data. If something prevents that, raise it. |
| O-3 | **Registration preference: federated connector**, because a declarative agent makes the user switch context. A declarative agent is allowed only where it is technically required. Assume users hold Microsoft 365 Copilot licences. |
| O-4 | **v1 users:** Spaarke users in the BFF's own Entra tenant. No guests or other tenants in v1. |
| O-5 | **Modal fallback** if an in-frame dialog fails: agreed (order in §9.4). |
| O-6 | **If an agent is needed, start from the existing "Spaarke AI" declarative agent** (`src/solutions/CopilotAgent/`). |
| O-7 | **Frontier** is on in the tenant, but nothing here depends on it. |
| O-8 | **Next slice:** billing review and approval (§17). |
| O-9 | **Not the whole Console.** Focused surfaces only. |
| O-10 | **Rework where better:** a Fable-level review of the BFF component model; prefer reworking over bolting on where that is the better technical decision. |
| O-11 | **v1 is the Decide grid and the Decision wizard**, as in the Console (screenshots, 2026-11-05): the Decide lane header with its count, the category cards, the rows, **Start review**, and the wizard. Only the Decide lane. The Decided history (`DecisionRecordsWidget`) is out of v1. |
| O-12 | **Auth must be the production approach.** Reviewing ADR-028 and amending it where needed is part of this project. |
| O-13 | **The server-side lookups the wizard needs are in scope**, including any BFF extension. |
| O-14 | No model-callable "complete decision" tool in v1. |
| O-15 | **The April 2026 Copilot surface stays** unless it interferes with this project. |
| O-16 | Widget HTML is embedded in the BFF. Aim under 1 MB (the `widget-design` skill rule); 3 MB is the hard ceiling (refined 2026-11-06). |
| O-17 | **This project creates and configures the connector** (and the plugin package) in the dev tenant. |
| O-18 | **Entry points (Q-8):** v1 starts with the **`/` skill**, and users can also **ask directly** in a normal prompt. Organisational prompts are not v1. |
| O-19 | **Brand for a family, not one connector (Q-9), approved as proposed in §5.5:** one Copilot connector per product area, named "Spaarke" + area. The core member was approved as **"Spaarke"** (v1) and is now the connector **"Spaarke MCP"** (owner 2026-11-10; supersedes O-19's core-member name); routes are `/mcp/{area}`. |
| O-20 | **Tenant admin (Q-10):** the owner holds Global Admin in the dev tenant and runs the admin-center steps; a second admin can be designated for testing. |
| O-21 | **Backlog issues (Q-11):** filed. See §19. |
| O-22 | **Terms:** a Signal produces a **Work Item**; resolving it writes a **Decision Record**. The Decide grid lists Work Items (§4.1). |
| O-23 | **§5.2 registration approved:** federated connector primary, plugin package also built, the Spaarke AI agent only as the T1 fallback. |
| O-25 | **Naming conventions approved (2026-11-06)**, after a sweep of master and 15 other projects: v1 route `/mcp/legal-operations` ("core" already means *core record*); functions are tool groups and skills, not routes; skills `spaarke-{area}-{capability}` (area dropped for core); widget `ui://spaarke/decisions.html` in `src/client/widgets/spaarke-decisions/`; capability layer in `Services/Signals/`; "Spaarke AI" stays the agent name; "Spaarke Copilot" retired. Full table: `notes/coordination/2026-11-06-copilot-naming-and-ownership.md` §1. |
| O-26 | **Cross-project coordination is binding** (owner: "very important"): agreements with legal-ai-accelerator-r1, ontology-platform-r1, ebilling-r1 and unified-access-control-r2 are recorded in `notes/coordination/2026-11-06-copilot-naming-and-ownership.md` §2 and in §20. |
| O-24 | **§6.2 auth approved:** OAuth at the Spaarke-tenant authority, and the ADR-028 amendment by path B, now written as **ADR-028 A10**. |

---

## 1. Thesis

Spaarke becomes a **tool provider inside the user's normal Copilot and Cowork flow**, not a separate app the user goes to. One **MCP server in the BFF** exposes Spaarke capabilities as tools that run **as the user**, under Spaarke's server-side rules:
- In plain Copilot Chat and Cowork, the model calls them alongside Work IQ (mail, calendar, files, people). A prompt like "the decisions waiting on me about Tuesday's email from Acme's counsel" can draw on both.
- Where the task needs a real UI, such as reviewing a decision and completing a multi-step form, a tool returns an **MCP App widget** built from the same `@spaarke/ui-components` the Console uses.

Three properties make it Spaarke rather than a Dataverse projection:
1. **Our rules.** Every read and write goes through the BFF services that own the invariant, the same ones the Console calls. No path goes through Dataverse's own MCP server.
2. **Our identity model.** The caller resolves to their `systemuser`, and Dataverse is read and written as that user (OBO).
3. **Our components.** The widget is the Console's decision UI recompiled for a sandboxed frame, not a re-implementation.

v1 proves all of this end to end on one capability, **decisions**. Each later capability (billing review first) is a small project that adds tools and, where useful, a widget.

---

## 2. Read these first

- `notes/mcp-project-research-starter.md`: platform facts (MCP 2026-07-28 spec, .NET SDK 2.3.1, MCP Apps, Copilot limits), April 2026 lessons.
- `.claude/agent-memory/researcher/federated-connector-widgets-writes-2026-11-05.md`: the registration-model research behind §5.
- `.claude/adr/ADR-028-spaarke-auth-architecture.md` **Amendment A8** (Model 1 workforce client, Spaarke-tenant authority) and **A9**. They drive §6.
- `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md`, `.claude/adr/ADR-002-thin-plugins.md`: one server-side owner per invariant.
- `.claude/constraints/bff-extensions.md`: placement, publish size, the hot-path rule.

---

## 3. Current state (verified in code, 2026-11-05)

**Server: the decision domain is already built, and built well.**

| Piece | Where | Note |
|---|---|---|
| Worklist read, as the caller, with a core-record check | `Api/Signals/SignalReadEndpoints.cs:27-83`, `Services/Signals/SignalReadService.cs:308` | `set = lanes \| closedToday \| suppressed \| decided`. 90-day decided window enforced server-side (L185, L605-626). More than 5 FetchXML pages returns 503 `too_many_signals`, not a partial list (L641-645). |
| Decision plan | `GET /api/v1/signals/{id}/decision-plan` (`Api/Signals/DecisionPlanEndpoints.cs:38,160-247`) | Builds the closed action plan and resolves option sources as the caller. **Stamps a plan-read** (`DecisionPlanReadStamp`, 4 h TTL) that a commit requires. |
| Suppression | `GET /api/v1/signals/{id}/suppression` | |
| Commit | `POST /api/v1/signals/{id}/decision` (`Api/Signals/DecisionCommitEndpoints.cs:31`; `Services/Signals/DecisionCommitService.cs:432-1542`) | **Single server-side owner** of the invariant: re-derives the plan, checks rights (`DecisionCallerRights`: AppendTo on the core record, Write on each subject), runs the actions as the caller, writes one append-only Decision Record, closes the Signal(s). Idempotent on `{oid}:{reviewId}` (Redis lease, 7-day outcome). |
| Copilot audience merge | `Infrastructure/DI/AuthorizationModule.cs:117-217` | Merged into the default workforce scheme. **Never provisioned:** no script, KV entry or pipeline sets `AgentToken:CopilotAudience`. |

**Client: portable with adapters** (`src/client/shared/Spaarke.UI.Components`):
- `DecisionRecordsWidget`, `WorklistWidget`, `DecisionWizard` and `DecisionWizardHost` make no `Xrm`, MSAL or `navigateTo` calls. They take an injected `fetch` (`ResponseFetchFn`), a `WorklistService`, and optional `lookup` / `openLookup` / `openRecord` slots.
- `Xrm` enters only through those slots, from the Console (`src/solutions/LegalWorkspace/src/sections/worklist.registration.ts:94-105`).
- `@spaarke/visuals` (Bar, Line, Donut, Gauge, MetricCard, MiniTable, StatusDistributionBar, Calendar, DueDateCard) is presentational only, with no host globals (`Spaarke.Visuals/src/index.ts:8`). The Visual Host *PCF's* data loading (`context.webAPI`, FetchXML) and navigation are Xrm-bound and do not port.
- `vite-plugin-singlefile` is already used by about 10 code pages (e.g. `src/solutions/CreateMatterWizard/vite.config.ts`).

**Earlier Copilot surface (April 2026):**
- The **"Spaarke AI" declarative agent** (`src/solutions/CopilotAgent/`): DA v1.8 with an OpenAPI plugin of 28 functions and `OAuthPluginVault` against a Spaarke-tenant authority. It has no `webApplicationInfo`, so SSO was never wired.
- `/api/agent/*` in the BFF (`Api/Agent/AgentEndpoints.cs:37-107`): four routes the April plugin calls. Much of the surrounding code is dead:
  - `AgentTokenService` has no callers;
  - `SpaarkeAgentHandler` is unregistered, and it is the sole consumer of `Microsoft.Agents.Hosting.AspNetCore 1.0.1`;
  - `AdaptiveCardFormatterService` is registered, but only `PlaybookInvocationService` and `EmailDraftService` inject it, and nothing resolves those two (`AgentModule.cs:43,55,58`);
  - no BFF route can receive Bot Service traffic, so `infrastructure/bot-service/main.bicep` has nothing to point at.

  Filed as #2575 (and #1523 for `AgentTokenService`).

**What does not exist:**
- no MCP package and no `MapMcp`;
- no **workforce-plane** server-side lookup search for people, budgets or firms. The Console searches these through `Xrm.WebApi` in the browser. The external SPA's server-side scoped read (`POST /api/v1/external/api/dataverse/fetch`) serves only the external collaboration plane.

---

## 4. v1 scope

### 4.1 What v1 shows (O-11)

**Terms (owner, 2026-11-05; matches `ontology-component-model.md` §3):**
- **Signal** (`sprk_signal`, engineering): what a policy raises.
- **Work Item** (user-facing): the line each Signal produces in the user's worklist. Work Items come in three kinds, **Decide / Do / Know**.
- **Decision Record** (`sprk_decisionrecord`): the append-only record written when the user **resolves** a Work Item.

So the v1 grid is the **Decide lane: the Work Items waiting on the user's decision** (`set=lanes`, `WorklistLanePanel`). It is not a list of Decision Records. The Console's "Decision Records" tab (`set=decided`, `DecisionRecordsWidget`) lists the records already written, read-only, and is **not** in v1. User-facing text (widget copy, tool and skill descriptions) says "work items" or "items that need your decision"; the tool and skill names keep "decisions" because that is how users ask ("the decisions waiting on me").

**The v1 widget is the Console's Decide lane:**

| Part | Component (shared lib) | Behaviour |
|---|---|---|
| Header: "Decide", count, "Items that need your decision" | `WorklistLanePanel.tsx:47-48` | As the Console |
| Category cards: All / Approve or rebudget / Other (each with a count, oldest age and "done today") | `laneCountFilters.ts` | Click filters the rows |
| Work Item rows grouped by core record (matter), with a reason and an age | `WorklistLanePanel`, `MatterCard`, `IssueLine` | Click a row → the Decision wizard for that Work Item |
| **Start review** | `WorklistLanePanel` | Walks the visible list in order ("2 of 70"), one wizard per item |
| The Decision wizard: What was found → one step per planned action (e.g. Create budget, Assign task) → Next steps → Confirm | `DecisionWizardHost` / `DecisionItemWizard` | Resolves the Work Item. The server writes the Decision Record and closes the Signal. |

### 4.2 In and out

| In (v1) | Out (v1) |
|---|---|
| The Decide lane as above, in one widget | The Do lane, the Decided history, the full Console, `LegalWorkspaceApp`, the matter workspace |
| Completing a decision through the wizard, including every parameter kind: text, choice, money, date, boolean, **person and lookup** (needs §8 B-3) | The platform lookup *pane* (`Xrm.Utility.lookupObjects`), opening related records in a modal, any navigation inside Dataverse. These become an inline type-ahead and an "Open in Spaarke" link (§9.3). |
| Text-only answers when no widget renders (§9.5) | Inquiry outcome recording (`/api/v1/inquiries/*`), the worklist's narrative briefing |
| One MCP server in the BFF; registration as a federated connector plus a plugin package; the Spaarke AI agent only as the fallback route (§5) | Connectors Gallery (Partner Center) publication, customer tenants, B2B guests (§6 makes these reachable later without rework) |
| Users in the BFF's tenant (O-4) | Teams, mobile. Word and Outlook only if they come for free with the connector (§5). |

---

## 5. Registration model: federated connector first

### 5.1 What the research established (Microsoft Learn, retrieved 2026-11-05)

| | Federated connector | Plugin package (`agentConnectors`, app manifest 1.30) | Declarative agent (`RemoteMCPServer` action) |
|---|---|---|---|
| Context switch | **None** after a one-time Connect in Settings › Sources. Copilot picks the tools at runtime. | **None in Cowork** once installed | **Yes:** the user picks the agent |
| Surfaces | Copilot Chat and Cowork confirmed. Word, Excel, PowerPoint and Outlook listed inconsistently across Learn pages. Researcher is read-only. | Cowork confirmed; Copilot Chat implied | Copilot Chat web and desktop |
| **MCP Apps widget** | **Not documented** in either direction | **Cowork: yes**; Chat: unknown | **Yes (GA)** |
| Write consent | Documented per-tool approval card: Allow once / for the conversation / always | Annotation-driven; for third-party servers "rolling out progressively" | A prompt on every non-read call |
| Write started from a widget (`callServerTool`) | Unknown | Unknown | Unknown. The spec says the host "MAY" ask for approval. |
| Auth | Entra SSO, OAuth 2.0 (Teams Developer Portal registration), none | OAuthPluginVault | Entra SSO, OAuth |
| Licence | Copilot licence (assumed, O-3) | Copilot licence plus Cowork billing | Copilot Chat |
| Maturity | GA; writes GA October 2026 (MC1476316) | GA | GA |

**One server serves all three.** Registration is configuration: the BFF accepts each registration's token audience, every tool carries annotations, and responses come back in under 30 seconds. The server needs no fork.

### 5.2 Decision (approved, O-23)

1. **Primary: a federated connector**, admin-created in the tenant and pointing at the BFF's `/mcp/legal-operations` (§5.5). It meets O-3: no agent picker, tools available in normal Chat and Cowork beside Work IQ, a documented write-approval card. It is also the route the legal ISVs use (Harvey, iManage, Litera), and it becomes the Partner Center package later.
2. **Also built: a plugin package (`agentConnectors`)** with one or two Cowork skills that name our tools. This is the **documented widget route in Cowork**, and per Microsoft's ISV guide it converges with the connector into one Microsoft 365 app package. It costs little: same server, same registration.
3. **Fallback, only if the Phase 0 tests show no widget in Copilot Chat through the connector:** the **Spaarke AI** declarative agent (O-6), rebuilt on DA v1.9 / plugin v2.4 / app manifest v1.30 with **one MCP runtime action and no OpenAPI action**. This is the one case where a declarative agent is technically required: Microsoft documents widgets in Copilot Chat only for agents. The user would then see the decision UI in Chat only inside the Spaarke AI agent, while plain Chat and Cowork still get the tools and text answers.

Why not decide later: the tool contracts, auth and widget are identical across all three, so v1 builds once and Phase 0 picks the packaging.

### 5.3 Instructions without an agent

A connector carries no 8,000-character instruction block. It has:
- **tool descriptions**, of which Copilot reads at most 1,024 characters per tool;
- the admin-entered **connector description**, up to 4,000 characters, which steers when Copilot picks the connector.

Microsoft documents no host that reads the MCP `instructions` field. So:
- **Tool descriptions are the routing surface.** Write them as exact "use for / not for" contracts and test them with a prompt eval (Phase 0, test T6).
- **Cowork skills** in the plugin package carry the longer "how Spaarke works" guidance for Cowork.

### 5.4 How users start it: @, / and suggested prompts (Q-8)

Research on Microsoft Learn and Message Center, 2026-11-05. Full note: `copilot-entry-points-mention-slash-prompts-2026-11-05.md`.

| Entry point | Works with the connector + plugin model? | Notes |
|---|---|---|
| **"/" + skill** | **Yes.** Cowork documents it; Chat gets it as MC1493223 rolls out (October to mid-November 2026). | In Cowork, typing `/` or using **+** lists plugin **skills**. Admin-deployed plugins reach Chat and Cowork, and Chat gains an "Agents and Skills" tab reached by `/` or `@` (RM569213). Copilot can also pick the skill automatically from intent. Custom slash *commands* are not supported; only skills appear. |
| **Naming the source in the prompt** ("…in Spaarke") | Yes | Learn's current guidance for connectors: "include the data source name". Needs a distinctive display name and tool descriptions that match. |
| **Organisational prompts** (suggested) | Yes, but the **customer's admin publishes them** | Published in the admin center › Copilot › Prompts (AI Administrator or Search Editor role). Pin up to 4 under **Suggested** on the Chat home page; all appear as autosuggest while typing and in the prompt lab. Tenant-wide; group targeting not documented. An ISV can't push them, so we supply a CSV. |
| **"@" mention** | **No**, for connectors | "@" reaches **agents** (and skills, per RM569213). `@Spaarke AI` exists only with the declarative-agent fallback. |
| **Conversation starters** | **No** | Declarative agents only (up to 12) |

**v1 design (owner, O-18): the `/` skill first, and direct prompts.**
1. **The skill `spaarke-decisions`**, in the plugin package. The user types `/spaarke-decisions` (or picks it from **+**), or Copilot picks it automatically. Its description is written around "the decisions waiting on me"; it calls `spaarke_show_decisions`, which renders the Decide widget. The name is distinctive so it doesn't clash with built-in or Legal skills.
2. **Direct prompts.** The user asks in their own words ("what decisions are waiting on me in Spaarke?", "…about the Acme matter"). Copilot picks `spaarke_show_decisions` from the connector's display name, its description (§5.5) and the tool description. This works with or without the skill, so the tool descriptions are written and tested for it (T6, E3).
3. **Not v1:** the organisational-prompt CSV. It goes into the customer onboarding kit later; nothing in v1 depends on it.
4. **"@Spaarke AI"** only if the declarative-agent fallback is adopted (§5.2).

Phase 0 tests:
- **E1:** does the skill appear in Chat's `/` and `@` "Agents and Skills" tab, and under what label?
- **E2:** does `/spaarke-decisions` in Cowork call the tool and render the widget?
- **E3:** do direct prompts route to the connector without the skill, both with "…in Spaarke" and without it?
- **E4** (informational, not v1): do org prompts reach Cowork and Teams?
- **E5:** does an @-mentioned agent keep the conversation's context? (Fallback only.)

### 5.5 Branding a family of Spaarke capabilities in Copilot (approved, O-19)

The decisions capability is the first of several. Patent, Trademark and other capabilities may follow, so the name has to work for a family.

**What constrains the name:**
- **"Connector" already means something in Spaarke.** In the component model a Connector is the *inbound* mechanism that pulls data from Aderant, iManage and others (`ontology-component-model.md` §4.6), sold as **Spaarke Connect** (`Sprk.Connect.*`). Calling these "the Patent connector" or "Spaarke Connect for Copilot" would collide with both. Microsoft's term "federated connector" stays in engineering and admin-runbook text only.
- **What users see:** the display name in Copilot's Settings › Sources (where they press Connect once), on the write-approval card, and in source attributions; skill names under `/`.
- **The URL is hard to change.** Each customer's admin creates the connector against an MCP URL. Changing that URL later means every customer re-creates it, so the URL shape is decided now (B-6).

**How to package the family:**

| | One "Spaarke" connector for everything | One connector per capability (Decisions, Billing, …) | **One per product area (proposed)** |
|---|---|---|---|
| User Connect steps | 1 | many | 1 per product area the customer buys |
| Copilot routing | one 4,000-character description has to cover everything | sharp, but many near-identical choices | sharp per area |
| Licensing | server-side checks only | admin enables what was bought | admin enables what was bought, and the server still checks |
| Admin control | all or nothing | fine-grained | per area |

**Proposal:**
- **Members, named "Spaarke" + area:**
  - **Spaarke** (as proposed; the connector's display name is now **"Spaarke MCP"**, owner 2026-11-10): core legal operations (decisions, billing review and approval, matters). **v1 ships this one.** Keeping it short makes "…in Spaarke" route naturally (§5.4).
  - **Spaarke Patents** and **Spaarke Trademarks** later, as add-ons that match their SKUs.
  - The alternative "Spaarke Legal Operations" for the core reads more clearly in a list of sources, but "…in Spaarke" then matches less well.
- **Family name** in docs and marketing: "Spaarke for Microsoft 365 Copilot". It needs a check against Microsoft's trademark and marketplace naming rules before any public listing; that isn't needed for v1.
- **Plugins and skills** (agreed with `spaarke-legal-ai-accelerator-r1`, 2026-11-06):
  - one plugin per area, slug `spaarke-{area}` (core: `spaarke`) with an explicit display name;
  - a legal practice accelerator is an area (`spaarke-nda` "Spaarke NDA");
  - skills are `spaarke-{area}-{capability}`, with the area dropped only in the core plugin (`spaarke-decisions`), so two practice plugins never collide on a shared skill such as setup;
  - whichever project starts patents first owns `spaarke-patents`, and the other adds skills to it.
- **Route name:** `legal-operations`, not `core`, because "core" already means a *core record*. Functions inside an area (decisions now; invoice review and matters later) are tool groups and skills, not separate routes. Each route is a separate connector to create and Connect, so splitting by function would multiply admin and user steps.
- **Words in Copilot text:** "playbook" without its format where the format matters. Per D-209 (2026-11-06, ontology §3), a playbook is one concept in several formats: JPS, legal, and contract-review. **"Finding"** is the owner-approved user word for a billing Work Item in billing link-outs (D-208, 2026-11-06, ontology §3); generic rows stay "Work Item", and "Finding" is never a code name.
- **Tools:** `spaarke_<verb>_<object>` as in §7.1; later areas add their noun (`spaarke_show_patent_deadlines`).
- **Engineering:** one BFF MCP module, one OAuth registration and one widget pipeline. Each member gets its own route, `/mcp/{area}`; v1 is `/mcp/legal-operations` (B-6). Every tool is tagged with its area, and an area's route lists only its tools.
- **Terminology:** the ontology project has already written the product names into `ontology-component-model.md` **§1.1** (commit `1e9d16bdba`, docs branch; product names sit in §1.1, terms in §3). This project adds the matching CLAUDE.md §1.1 row, citing it. Full naming table: `notes/coordination/2026-11-06-copilot-naming-and-ownership.md`.

Phase 0 adds:
- **T13:** can SDK 2.3.1 serve different tool sets on two routes in one host? If not, the fallback is one route that filters the tool list by the request path.
- **T14:** how Sources and the approval card show the display name and description, and whether one Microsoft 365 app package can carry two `agentConnectors` entries.

---

## 6. Identity, tenancy and auth

### 6.1 The constraint that decides the auth route

ADR-028 A8 (proven live 2026-10-08): in **Model 1**, a per-customer stamp in Spaarke's tenant, licensed customer staff are **B2B guests** whose `systemuser` record is keyed on the **guest `oid` in Spaarke's tenant**. Only an authority **pinned to Spaarke's tenant** issues that `oid`. A token from the user's home tenant has a different `oid`, so it does not match any `systemuser`, and the BFF's credentials cannot exchange it OBO for Spaarke-tenant Dataverse.

- **Entra SSO through Copilot** issues from the **user's home tenant**. That works only when the user's home tenant **is** the BFF's tenant: v1's dev tenant (O-4) and Model 2 stamps. **It fails for Model 1 customers**, which is the main production shape.
- **OAuth 2.0** (a static registration in the Teams Developer Portal, authorize and token endpoints at `login.microsoftonline.com/{spaarke-tenant}`; a confidential client with a secret held only in Microsoft's registration, T5 2026-11-08/12) issues Spaarke-tenant tokens with the guest `oid`, in the shape A8 requires. The April package already used this shape (`spaarke-bff-openapi.yaml:1301-1302`), and April recorded "OAuth worked".

### 6.2 Decision (approved, O-24)

- **v1 uses OAuth 2.0 with the authority pinned to the BFF's tenant**, even though v1 users are same-tenant and SSO would work for them. That way the dev tenant tests the production route from day one, and later customer slices need no auth rework.
- **Entra SSO** stays an option for Model 2 stamps under a later decision.
- **Tokens:**
  - **audience:** the BFF app (no audience merge needed for OAuth);
  - **downstream:** OBO to Dataverse and Graph, exactly as the Console. Never pass a token through.
  - **authorization:** the existing `DecisionCallerRights` check.
- **Open (Phase 0, test T5):**
  - whether a connector created in the customer's tenant accepts an OAuth registration whose authority is another tenant (Spaarke's);
  - which app registration is the OAuth client;
  - where its credential lives (CLAUDE.md §9 Key Vault rules; client-secret writes need owner confirmation).
- **Answered (T5 passed 2026-11-12, OBO via "Spaarke MCP"):** the Dev Portal requires a client secret (owner-confirmed 2026-11-08), so the public client is not accepted and the connector client is confidential: public client flows off, redirect on the Web platform, the secret only in the Dev Portal registration, dev expiry up to 24 months (ADR-028 A10 Path A).

**ADR-028 review and amendment is a project deliverable (O-12, Phase A).** A Copilot connector is a **new client plane**: a Microsoft-hosted token store acting as an OAuth client for the BFF. A8 governs the SPA and Teams workforce clients and doesn't mention it. The project reviews ADR-028 against this plane and drafts the amendment. **Done: written as ADR-028 Amendment A10** (2026-11-05, owner-approved path B; `.claude/adr/ADR-028-spaarke-auth-architecture.md`, summarised in `.claude/constraints/auth.md`). It:
- adds a Copilot / Cowork connector client plane: Spaarke-tenant authority, a confidential client (the Dev Portal requires a secret), BFF audience, `user_impersonation` scope, tools as the caller (OBO), `systemuser` callers only;
- makes Entra SSO Model-2-only;
- requires a dedicated connector client (a confidential client, one per stamp, with its secret only in Microsoft's registration; the Dev Portal does not accept a public client, T5 2026-11-08/12);
- refuses CIAM and A9 home-directory tokens on `/mcp/*`.

Also rejected in A10: A9's home-directory token plus stored link, because that token can't be exchanged OBO into the Spaarke tenant, so every tool would have to run app-only.

Alternatives rejected:
- *Path A (project exception)*: this is not a one-project deviation, because every later slice inherits it.
- *Path C (comply)*: A8 does not cover this client.

### 6.3 What the auth work does and does not touch

- **`AgentToken:CopilotAudience`** (the Copilot SSO audience merge) is **not needed for OAuth**, because OAuth tokens carry the BFF's own audience. Leave it as it is (unprovisioned, O-15) until a Model 2 SSO decision.
- **The "DEBUG" auth-header logger** (`MiddlewarePipelineExtensions.cs:121-158`, marked "TODO: Remove before production"):
  - It is **not** compiled out. It runs in every build and writes a Warning on each `/api/*` request with the token's `aud`, `iss`, `appid` and `scp` claims (not the token itself).
  - `/mcp/*` is outside `/api`, so it does not interfere with this project.
  - It is a pre-existing defect (log noise, claim exposure in logs). It is **already filed as #1574**, owned by `unified-access-control-r2` and `spaarke-auth-system-of-record-r1`, so this project doesn't fix it.

---

## 7. Architecture

```
Copilot Chat / Cowork  (user's normal flow; Work IQ in the same turn)
   │  federated connector  (or plugin package / Spaarke AI agent; same server)
   │  OAuth token, Spaarke-tenant authority, aud = BFF
   ▼
BFF  POST /mcp/legal-operations   stateless · RequireAuthorization · rate-limit "mcp" · size limit · kill switch Mcp:Enabled
   ├─ spaarke_show_decisions        model-visible · readOnly   → DecisionCapability.ListDecideAsync(caller, category?)
   │       content: short text summary (+ Console deep links) · structuredContent: slim Decide-lane rows · _meta.ui → widget
   ├─ spaarke_get_decision_plan     app-only · readOnly         → plan + suppression + full row; writes the plan-read stamp
   ├─ spaarke_search_decision_lookup app-only · readOnly        → NEW server-side lookup search (people, budgets, firms), as caller
   ├─ spaarke_complete_decision     app-only · write            → DecisionCommitService (unchanged invariant owner)
   └─ resource ui://spaarke/decisions.html  (text/html;profile=mcp-app, embedded in the BFF)
            │
            ▼  rendered by the host in a sandboxed frame; talks only via callServerTool
   widget: single-file React + Fluent v9
     Decide lane (header, category cards, rows, Start review)  ← WorklistLanePanel, WorklistService over an MCP fetch shim
     DecisionWizardHost / DecisionItemWizard  ← ResponseFetchFn shim; lookup = spaarke_search_decision_lookup; no openLookup/openRecord
```

### 7.1 Tools

| Tool | Visibility | Hint | Input | Calls | Why separate |
|---|---|---|---|---|---|
| `spaarke_show_decisions` | model and app | readOnly | optional `category`, a closed enum mirroring the lane's category cards (`laneCountFilters.ts`) | the decision capability's Decide-lane list (§8 B-1) | The model's entry point. A small, closed input schema answers April issue 2 (invented parameters). |
| `spaarke_get_decision_plan` | app | readOnly | `signalId` | plan, suppression, the item row | The wizard needs it on open; it writes the stamp a commit requires |
| `spaarke_search_decision_lookup` | app | readOnly | `signalId`, `parameterCode`, `query` | new service (§8 B-3) | Without it, six action types cannot be completed. It must be readOnly so typing doesn't prompt for consent. |
| `spaarke_complete_decision` | app | **write** | `signalId`, `DecisionReview` | `DecisionCommitService` | The one write. Idempotent on `reviewId`. |

- **Naming:** a `spaarke_` prefix, because connector tools share Copilot's tool space with other vendors.
- **Descriptions:** at most 1,024 characters, with "use for / not for" wording.
- **Error results:** errors come back as tool results with `isError` and a message that says how to fix the call, never as transport errors (§8 B-2).
- **Commit warnings:** commit results keep `warnings[]` and each step's `warning` (D-216, for example `inquiry_email_not_archived`). `spaarke_complete_decision` returns `DecisionCommitResult` unchanged and the widget shim passes them through, so the Done step can show them (spec FR-08, FR-13).
- **Writes through the model:** a model-visible "complete" tool is **not in v1** (O-14).

### 7.2 Why tools call services, not HTTP routes

The tools run inside the BFF and call the same services the REST endpoints call. Two consequences:
- No second HTTP hop, and no second token exchange.
- One owner per invariant: the commit service validates whether the caller is the Console or Copilot.

---

## 8. BFF changes: rework where it is the better decision

The review found the decision services well shaped, but coupled to `HttpContext` and with their result-to-HTTP mapping inside endpoint statics. A second host (MCP) would fork both. The changes below make REST and MCP two thin adapters over one capability.

| # | Change | Problem it fixes (in code today) | v1? |
|---|---|---|---|
| **B-1** | **Decision capability layer:** in `Services/Signals/` (no new folder; ontology-platform owns this area and reviews every PR), self-authorising, transport-free: list, plan, lookup, commit. REST endpoints and MCP tools both call it. | `DecisionCommitService.CommitAsync(…, HttpContext, …)` (L432), `DecisionPlanReadStamp.StampAsync(HttpContext, …)` (L42) and `DecisionParameterSources.ForPlanAsync(httpContext, …)` (L109) take `HttpContext`. A tool would either pass `HttpContext` around or re-derive the caller. | v1 |
| **B-2** | **One result mapper:** `SignalReadResult` / `DecisionCommitResult` → ProblemDetails (REST) **or** an MCP tool error result, with a single `reasonCode` vocabulary | Mapping lives in endpoint statics (`DecisionCommitEndpoints.cs:60-118`, `SignalReadEndpoints.cs:46-83`); Signals say `reasonCode` and Finance says `errorCode` | v1 |
| **B-3** | **Server-side lookup search for workforce callers** (`IDecisionLookupSearch`), in scope per O-13. It searches contacts, budgets filtered to the matter, and organisations, read as the caller. **The decision plan drives it:** each parameter's `LookupEntity` and its scoping rule come from the plan, so the client can't widen a search. Exposed as a REST route **and** a tool; the Console can switch to it later. **Precedent:** the external SPA's scoped read (`ExternalModuleDataEndpoints.cs` → `POST /api/v1/external/api/dataverse/fetch`) already does server-side lookups. It runs on the **external collaboration plane** (dual-scheme policy, external access guard), which must not serve workforce or Copilot callers. So B-3 follows its pattern (server-built query, scoped, fail-closed) on the workforce plane rather than reusing that route. | The rule "a budget belongs to the matter" (D-18) is enforced only by the Console's browser-side picker (`worklistLookup.ts:54-74`; `DecisionActionExecutors.cs:380-381`). No workforce-plane equivalent exists, so any other host can't search at all: revise-budget, reassign, add-todo, assign-work, assign-task and the inquiry's outside firm can't be completed. | v1 |
| **B-4** | **Caller context:** a scoped `ICallerContext { Caller (Spaarke.Core.Auth.CallerIdentity), Principal, BearerToken, CorrelationId }`, populated once per request. `DataverseUserClient`, `CallerWhoAmIMemo` and the probes read it instead of `IHttpContextAccessor`. | 85 `IHttpContextAccessor` uses in 46 files and 82 `HttpContext` parameters in 43 service files. MCP works only because a tool happens to run inside the HTTP request; a non-HTTP continuation fails silently (`UserContextRequired`). `CallerResolution.cs:63-66` already forbids `HttpContext` overloads. | v1 for the decision slice; repo-wide later |
| **B-5** | **Slim list projection:** `WorklistItemSummary` without `EvidenceRefs` or `Steps`; the full row arrives with the plan on open | Rows are about 1–2 KB each, so 30–50 rows exceed Cowork's 64 KiB inline cap | v1 |
| **B-6** | **MCP placement:** `Api/Mcp/` + `AddMcpModule` + `MapMcpEndpoints` in `EndpointMappingExtensions` (ADR-010). An ADR-032 null-object kill switch `Mcp:Enabled`. Explicit `.RequireAuthorization()`, a dedicated rate-limit policy (partitioned per caller), and a request-size limit on `/mcp/*`. **Routes are per product area, `/mcp/{area}`, and v1 maps only `/mcp/legal-operations`** (§5.5). Each tool is tagged with its area. | The connector URL is set in each customer's tenant. A flat `/mcp` would force every customer to re-create the connector when the Patents or Trademarks area arrives. | v1 |
| **B-7** | **Tool authorization as policies:** `AddAuthorizationFilters()` plus per-tool `[Authorize(Policy = …)]`, reusing existing policies. Never `ExternalCollaboration`. | Checks held in endpoint filters (Finance, AI) don't reach tools | v1 |
| **B-8** | **Guard coverage:** extend `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.cs` (it recognises only `Map{Verb}`, `MapMethods` and `MapHealthChecks` via `ReadRegistrationVocabulary`) to cover `MapMcp` and assert every tool carries an explicit `ReadOnly` annotation. The `MapMcp` scanner vocabulary lands with the host (task 020), because the existing guard turns red on any unread `Map*` call; the ledger entries, tool policies and annotation checks follow in task 021 | New auth surface invisible to the ratchet | v1 |
| **B-9** | **The April 2026 Copilot surface stays (O-15).** That means the "Spaarke AI" agent package (`src/solutions/CopilotAgent/`, 28-function OpenAPI action), `/api/agent/*`, and the unused `AgentTokenService`, `SpaarkeAgentHandler`, `AdaptiveCardFormatterService` and `Microsoft.Agents.Hosting.AspNetCore` package. **One point of interference:** if the declarative-agent fallback (§5.2) is adopted, the Spaarke AI package is rebuilt on the MCP action, and its OpenAPI action must go, because the two would compete for the same prompts (April's routing problem). The unused code is filed as #2575 (and #1523). | The code is unused but harmless, so leave it | Only with the fallback |
| **B-10** | Accept `because` from the wizard | The server supports it (`DecisionCommitService.cs` `Because`); the client never sends it | v1 (trivial) |

Not changed: `SignalReadService`'s read rules, `DecisionCommitService`'s validation, idempotency and execution order, the Decision Record writer. Those are correct and stay the single owner.

---

## 9. The widget

### 9.1 Build

- A new code-page-style folder (proposed `src/client/widgets/spaarke-decisions/`) with a Vite + `vite-plugin-singlefile` build, following the pattern ten Spaarke code pages already use.
- It deep-imports `@spaarke/ui-components` **source** (not the barrel), so `pdfjs-dist`, `mammoth` and App Insights stay out of the bundle (`sideEffects: false`).
- **Patterns to adopt (all MIT):**
  - Microsoft's Copilot Studio Kit `mcp-apps/copilot-agent-kit`: Fluent v9, single-file build, tests;
  - `power-platform-skills/plugins/mcp-apps`: loading, empty and error states, Fluent tokens;
  - `microsoft/DataFactory.MCP`: HTML shipped as an embedded resource in a .NET 10 server.
- Use `@modelcontextprotocol/ext-apps` **2.0.4**. The samples use 1.x, so check for API drift.

### 9.2 Reuse map

| Need | Component | Adapter |
|---|---|---|
| Decide lane (header, category cards, rows, Start review) | `WorklistLanePanel` for the Decide lane only, from the `WorklistWidget` tree | `WorklistService` over an MCP fetch shim (`createWorklistService({ fetch: mcpFetch })`). Lane open/closed state in `localStorage` degrades safely. No `openLookup` / `openRecord`. The narrative briefing is disabled. Whether `WorklistLanePanel` mounts on its own outside `WorklistWidget` is checked in Phase B; if not, `WorklistWidget` gains a single-lane prop. |
| Decision modal | `DecisionWizardHost` / `DecisionItemWizard` | A `ResponseFetchFn` shim that maps the plan, suppression and decision routes onto tools. `lookup` = `spaarke_search_decision_lookup`. Steps that depend on navigation fall back to existing text rendering (`FoundStep.tsx:49-56`). |
| Email action step | `EmailComposer` (Lexical) | Bundle-size risk: measure. Recipients use the lookup tool plus free-text addresses (the executor accepts addresses). |
| Theme | `FluentProvider` driven by host context at init | Do **not** call `detectDarkModeFromNavbar`, and keep no theme in storage |
| Later charts and KPIs | `@spaarke/visuals` | Portable as-is. Data comes from a server tool that returns aggregated points, not the Visual Host PCF's `context.webAPI` FetchXML. |

**Rewrite fallback:** if the shimmed wizard misses the size or init budget, keep `decisionDraft.ts` (pure logic, no DOM or Xrm) and re-render only the step UI.

### 9.3 Host-impossible interactions and their replacements

| Interaction | Replacement |
|---|---|
| Lookup pane | Inline type-ahead |
| Opening a related record | Text plus an "Open in Spaarke" link (`openLink` to the Console record) |
| `navigateTo` | `openLink` |
| Host modal (`requestModal` is unsupported in Copilot) | An in-frame Fluent `Dialog` |

### 9.4 Modal behaviour (O-5)

Open the wizard as an **in-frame dialog** and request **fullscreen** on open (`requestDisplayMode`, fullscreen only in Copilot). Inline frames are at most about 640 px tall. Fallbacks, in order:
1. master-detail in fullscreen;
2. `openLink` to the Console's wizard for that item.

### 9.5 When no widget renders

If a route doesn't render widgets (the connector in Chat, per §5), the same tool call still answers in text:
- `content` carries a short summary: counts per view and the top items, each with a Console deep link;
- `structuredContent` carries the rows for the model.

The user can still ask questions in Chat and finish in Spaarke. Completing a decision then happens in the Console, or in the Spaarke AI agent if the fallback is adopted.

---

## 10. Security

- **Every tool authorizes as the caller.** No app-only reads of user data, except catalog and metadata reads the existing services already do app-only.
- **Writes stay where they are:** one owner (`DecisionCommitService`), rights checked server-side, idempotent. **Don't rely on the host to confirm a write.** The commit is an explicit button in the widget, `Confirmed` is set by that action, and the server requires a prior plan read (the stamp).
- **Treat record text as untrusted** in tool results. Copilot screens tool *definitions*, not results. Keep model-visible text to fields the user already sees.
- **The widget declares no network origins** (`_meta.ui.csp` empty) and holds no tokens; all data goes through `tools/call`.
- **Correction to the BFF review's hosting note.** `SecurityHeadersMiddleware` (`X-Frame-Options: DENY`, `frame-ancestors 'none'`) does not block the widget. The HTML travels inside the JSON-RPC `resources/read` result and the host renders it; the BFF never serves it as a framed page.
- **Two adversarial-review passes** (CLAUDE.md §8.5: auth, tenant isolation).

---

## 11. Hosting

- **Stateless mode** (SDK default). Session affinity off. Tools finish in under 30 seconds; long playbooks stay job-based (ADR-004).
- **Streaming:** the transport needs `Accept: application/json, text/event-stream` (the 406 trap). Disable response buffering on streamed responses, as `WorkspaceMatterEndpoints.cs:196-200` does.
- **Infrastructure:** there is no front door today. App Service Linux, HTTP/2. If a gateway is added later, it must forward the `Mcp-Method`, `Mcp-Name` and `MCP-Protocol-Version` headers.
- **CORS:** irrelevant; the traffic is server to server.
- **Telemetry:** per tool call, record the tool name, latency, row count, outcome or reason code, and the correlation id. No PII in the tool name dimension.

---

## 12. Testing (ADR-038)

| Level | What |
|---|---|
| Unit | The capability layer: as-caller pass-through, reason-code mapping, lookup scoping (budget belongs to the matter), slim projection under 64 KiB at the cap |
| Protocol | `/mcp/legal-operations` against an in-process test host: `server/discover`, tool list and annotations, `resources/read` MIME type, auth challenge (401 with `WWW-Authenticate`). Run the `modelcontextprotocol/conformance` suite (`--spec-version 2026-07-28`) in dev and CI with a test-token injector. |
| Arch | B-8 guard: `MapMcp` requires auth; every tool has an explicit `ReadOnly` annotation; tools inject no AI-internal types (ADR-013) |
| Widget | Adapter tests (shim ↔ tool result), wizard flow with a mocked `callServerTool`, bundle-size budget test |
| Live (tenant) | Phase 0 tests T1–T12 (§13). After the build: an end-to-end complete-decision run per route, and a 20-prompt routing eval |

---

## 13. Phasing and go/no-go gates

**Phase 0: tests in the dev tenant (days, not weeks).** The first three gate the rest.

| # | Test | Gate |
|---|---|---|
| T5 | OAuth (Spaarke-tenant authority) through a federated connector: decode the token (`aud`, `scp`, `tid`, `oid`); OBO → Dataverse WhoAmI returns the right `systemuser` | **Gates everything** |
| T0 | A hello-world `/mcp/legal-operations` on the dev BFF, called by the connector: `Accept` / 406, stateless calls | Gates Phase A |
| T1 | The same server as a federated connector: in plain Chat and in Cowork, does a `ui://` widget render? Also try Word and Outlook. | **Decides the Spaarke AI fallback** |
| T2 | As a plugin package in Cowork and in Chat: does the widget render? | Packaging |
| T3 | A widget calls the write tool (app-only, not readOnly): is there a confirmation card, and is it per call? | Write UX |
| T4 | The model calls a write tool: does the card show the parameters, and does "Allow for conversation" persist? | Q-4 |
| T6 | 20 prompts without the word "Spaarke": how often is the tool invoked? | Descriptions |
| E1–E5 | Entry points: the skill under `/` in Chat and Cowork, direct prompts, org prompts (informational), `@` (§5.4) | Entry-point design |
| T13–T14 | Two tool sets on two routes in one host; how the display name shows in Sources and on the approval card; two connectors in one app package (§5.5) | Route shape, branding |
| T7–T12 | MCP `instructions` honoured or not; Cowork elicitation; Work IQ plus our tool in the same turn; a skill naming a connector tool; behaviour without a licence; Cowork treating unannotated tools as destructive | Informational |
| Size | A single-file bundle at 1, 3 and 6 MB: init time in Chat and Cowork | Widget budget |

**Phase A: foundation.**
- the `/mcp/{area}` endpoint (B-6, B-7, B-8);
- the caller context and result mapper for the decision slice (B-4, B-2);
- auth hardening (§6.3), implementing ADR-028 A10;
- the family and member names (§5.5) and the Work Item terms added to `ontology-component-model.md` §3 and the CLAUDE.md §1.1 product-name table;
- the widget build pipeline and test harness.

**Phase B: the decisions slice.**
- the capability layer (B-1), lookup search (B-3) and slim projection (B-5);
- the four tools;
- the widget (the two views and the wizard);
- **the connector and plugin package, created and configured by this project (O-17):**
  - the Teams Developer Portal OAuth registration;
  - the federated connector in the dev tenant (admin center › Copilot › Connectors, connector description up to 4,000 characters);
  - the plugin package with its Cowork skill(s), deployed;
  - a repeatable runbook or script for each.

  The admin-center step needs Global Admin or AI Admin. **The owner holds Global Admin in the dev tenant (O-20)** and runs it with the project; a second admin can be designated for repeat testing. Tests also need **a licensed non-admin test user**, because an admin's consent and approval prompts can differ from a normal user's. Whether a connector can be created by API or script, so customer provisioning can automate it later, is checked in Phase 0.
- the Spaarke AI fallback package, if T1 says so;
- an end-to-end test in the tenant.

**Phase C: hardening.**
- publish-size and CVE checks;
- telemetry;
- two adversarial reviews;
- rewrite the `mcp-tool-handler` skill to match the real conventions;
- update docs: an MCP architecture doc and a "how to add a tool" guide.

---

## 14. Placement justification (CLAUDE.md §10, `bff-extensions.md`)

**In the BFF:**
- Every tool runs **as the caller** (OBO) over services that exist only in the BFF: `SignalReadService`, `DecisionCommitService` with its Redis idempotency ledger, `DecisionCallerRights`. ADR-052 B1 places live caller-identity work in the BFF.
- A separate MCP deployable would have to call the BFF over HTTP with a second token exchange, and would duplicate the invariant or proxy it.
- ADR-013 allows a separate MCP deployable "when the technical criteria are met". Here they are not: the server is not a bounded engine, it is a second front door to BFF capabilities. Path C (comply), recorded here.
- No AI-internal types are injected; the decision tools are CRUD over the capability layer.

**Publish size:**
- adds `ModelContextProtocol.AspNetCore` and its extensions (about 1.6 MB of DLLs), plus the embedded widget HTML (budget 1–3 MB, measured in Phase 0);
- `Microsoft.Extensions.AI` moves from 10.3.0 to the SDK's 10.11 dependency.

The unused `Microsoft.Agents.Hosting.AspNetCore` package stays (O-15; removal tracked in #2575), so nothing offsets the increase. Measure the net figure against a fresh master build on every BFF task, and check for new HIGH CVEs.

**The `Microsoft.Extensions.AI` bump touches the AI pipeline.** Regression scope includes the AI unit and integration suites, not just a size check.

## 15. Component justification (CLAUDE.md §11)

| New component | 1. Existing overlap | 2. Extend instead? | 3. Cost of doing nothing |
|---|---|---|---|
| `/mcp/{area}` endpoint + `AddMcpModule` | The April OpenAPI plugin and `/api/agent/*`; Dataverse's built-in `/api/mcp` | Can't extend: OpenAPI actions can't carry MCP App widgets or join a federated connector, and Dataverse MCP can't apply the Signal core-record check (`SignalReadEndpoints.cs:15-19`) | Spaarke has no presence in Copilot or Cowork beside Work IQ, and no route to the legal ISV connector gallery |
| Four `spaarke_*` tools | REST routes for the same operations | They **are** the extension: thin adapters over the capability layer | Copilot can't list or complete a decision |
| Decision capability layer (B-1) | `SignalReadService`, `DecisionCommitService`, the plan endpoints | Extends them by moving the `HttpContext`-free core out of the endpoints; no new rules | A second host forks the caller resolution and result mapping, giving the invariant a second owner |
| `IDecisionLookupSearch` + route (B-3) | Console's `worklistLookup.ts` (browser-side, `Xrm.WebApi`) | Cannot extend a browser-side `Xrm` helper to a server; this moves the rule server-side | Six action types cannot be completed outside the Console, and D-18 stays enforced only in the browser |
| `ICallerContext` (B-4) | `CallerResolution`, `CallerWhoAmIMemo`, `IHttpContextAccessor` | Extends: wraps what `CallerResolution` already resolves | MCP works only by running inside an HTTP request; a later non-HTTP path fails silently (`UserContextRequired`) |
| Widget build (`src/client/widgets/spaarke-decisions/`) | Code-page Vite builds; `knowledge/mcp-apps` samples | Same toolchain, new entry point; no fork of components | No UI to complete a decision in Copilot or Cowork |

## 16. Hot-path declaration (CLAUDE.md §10)

```xml
<hot-path-declaration>
  <bff-api>YES — new Api/Mcp/ + AddMcpModule + MapMcpEndpoints; Services/Signals decision capability extraction (HttpContext-free overloads); new workforce lookup-search service + route; ICallerContext (DataverseUserClient, CallerWhoAmIMemo); AuthorizationModule only if the ADR-028 amendment changes accepted audiences; csproj package changes. Api/Agent untouched.</bff-api>
  <spaarke-ai>NO — src/solutions/SpaarkeAi/** untouched; shared-lib decision components may gain props (lookup/shim seams), consumed unchanged by the Console</spaarke-ai>
  <ci-workflows>YES — widget build step feeding the BFF embedded resource; MCP conformance run; possibly a connector/plugin package build</ci-workflows>
  <skill-directives>YES — rewrite .claude/skills/mcp-tool-handler; update declarative-agent and widget-design skills to current schemas</skill-directives>
  <root-CLAUDE-md>YES — §1.1 product-name table gains the "Spaarke for Microsoft 365 Copilot" family and its "Spaarke MCP" core connector (spec FR-18)</root-CLAUDE-md>
</hot-path-declaration>
```

Register in `projects/INDEX.md` at pipeline start. Run `/conflict-check` before each BFF PR. `AuthorizationModule.cs` and `Services/Signals/**` are shared with active projects.

## 17. Next slice: billing review and approval

> ⚠️ **Conflicts with ebilling EB-D-194 / ontology D-207 (found 2026-11-06):** invoice review and approval decisions happen **only in the Invoice Review app**. Billing review inside Copilot therefore needs an owner decision before this slice is planned. In v1, billing Work Items appear in the Copilot Decide lane only as a link out to `sprk_invoicereviewpage`.

The pattern fits directly:
- **Routes exist:**
  - `GET /api/finance/invoices/{id}/review`;
  - `POST …/lines/{lineId}/decision`, `…/lines/decisions`, `…/lines/accept-clean`;
  - `GET …/approval`, `POST …/approve`, `/reject`, `/return`.
- **One Finance owner writes billing Decision Records** (`BillingDecisionRecorder.cs:70-89`) and closes Signals with the same `DecisionCallerRights`.
- **No invoice-review UI exists anywhere**, so its widget would be the first UI. That is simpler than the decision wizard: choices only, no lookups.

What v1 should anticipate:
- The capability-layer pattern (B-1) must also absorb **filter-held authorization**. Finance checks `RequireInternalFinanceCaller`, `AddFinanceAuthorizationFilter` and `RequireEbillingEnabled` in route metadata, not in the service.
- Finance results must go through the same mapper (B-2), so the `errorCode` vs `reasonCode` drift has to be fixed there.
- Expected tools: `spaarke_get_invoice_review`, `spaarke_record_line_decisions`, `spaarke_approve_invoice`.

## 18. ADR tensions (CLAUDE.md §6.5)

| ADR | Tension | Proposed path |
|---|---|---|
| **ADR-028** (A8) | A new connector client plane is not covered; Entra SSO through Copilot breaks Model 1's guest-`oid` rule | **B: amendment, approved and written as A10** (§6.2, O-24). |
| ADR-013 / `bff-extensions` | MCP as a separate deployable is "consider, needs successor ADR" | **C: comply** (in the BFF; §14) |
| ADR-008 / 010 / 030 / 032 | Tools must not bypass filter-held checks; module registration; kill switch | **C: comply** via B-6 and B-7 |
| ADR-002 / write path | The write from a new host | **C: comply**: same single owner; the widget previews and the server decides |

## 19. Decisions

### Resolved (owner, 2026-11-05)

O-1 to O-21 (§0). The second review round settled:
- the scope (Decide lane plus the wizard);
- production auth, with the ADR-028 revision in scope;
- server lookups in scope;
- no model-callable completion;
- the April surface left alone;
- the widget budget;
- connector creation in scope.

The third round settled:
- **Q-8:** the `/` skill plus direct prompts; org prompts later (O-18, §5.4);
- **Q-9:** branding as proposed, core member "Spaarke" (O-19); the core connector's display name is now "Spaarke MCP" (owner 2026-11-10);
- **Q-10:** the owner runs the admin steps as Global Admin (O-20);
- **Q-11:** issues filed. The April agent code is **#2575** (new, `spaarkeai-mcp-bff-api-r1`, `notes/defer-issues.md` ISS-001). The "DEBUG" token logger was **already filed as #1574**, and `AgentTokenService` as **#1523**, so neither was duplicated.

### Open

| # | Question | Status |
|---|---|---|
| — | None. The connector description and skill text are drafted in the spec. | — |

## 20. Risks

| Risk | Mitigation |
|---|---|
| No widget through the connector in Chat (documented only for agents) | §5.2 fallback (Spaarke AI agent); §9.5 text path; re-check at Ignite (17–20 November) |
| OAuth from a customer-tenant connector to a Spaarke-tenant authority not supported | T5 before any build; Entra SSO stays possible for Model 2 |
| The wizard's bundle (Fluent, Lexical) exceeds the init budget | Deep imports, a size test, the `decisionDraft.ts` rewrite fallback |
| Hosts ask for write consent inconsistently, or not at all | The server never relies on host consent (§10) |
| The `Microsoft.Extensions.AI` bump regresses the AI pipeline | Run the AI suites; pin; measure |
| First of its kind: no public .NET + Entra + widget + connector sample | Phase 0 tests; adopt the PnP `mcp-cs-customers-secure-server` auth pattern (SDK 1.2.0 → 2.3.1) |
| Contention on `AuthorizationModule.cs` and `Services/Signals/**` | `/conflict-check`, small PRs. **Agreed with ontology-platform (2026-11-06):** B-1, B-2 and B-5 start only after its PR #2580 (D-207 billing refusal, held until ebilling task 112) reaches master; B-3 and B-4 can start now. Every PR touching `Services/Signals/**`, `Api/Signals/**` or the worklist route goes to ontology for review. Auth diffs go to unified-access-control-r2. |
| #2580 is held on ebilling's Invoice Review deployment | Phase A starts with B-3, B-4 and the MCP foundation. Until #2580 lands, billing Work Items are omitted from the Copilot list or shown without a link. |
| A second MCP server is proposed elsewhere (`Sprk.Insights.Mcp`, agent-framework-fit-assessment) | Any further MCP surface is a new `/mcp/{area}` on this host, not a second server. Raise it if that project starts. |

## 21. References

- **Research:**
  - `notes/mcp-project-research-starter.md`;
  - `notes/copilot-as-spaarke-surface-2026-11-05.md`;
  - researcher notes `mcp-server-dotnet-bff-copilot-widget-2026-11-05.md`, `copilot-as-spaarke-surface-and-frontier-2026-11-05.md`, `federated-connector-widgets-writes-2026-11-05.md`;
  - accelerator sweep `github-hf-delta-sweep-cowork-mcp-2026-11-05.md` §B (in the `spaarke-legal-ai-accelerator-r1` worktree).
- **Microsoft Learn (2026-11-05):**
  - federated connectors: overview, custom setup, submit;
  - `plugin-mcp-apps`, `plugin-confirmation-prompts`, `plugin-authentication`;
  - Cowork `mcp-apps-support` and plugin development;
  - Work IQ overview;
  - MC1476316.
- **Code:**
  - `Api/Signals/*`, `Services/Signals/*`;
  - `Infrastructure/DI/AuthorizationModule.cs`, `Infrastructure/Dataverse/DataverseUserClient.cs`;
  - `Api/Agent/*`;
  - `src/client/shared/Spaarke.UI.Components/src/components/{Worklist,DecisionRecords,DecisionWizard}`;
  - `src/client/shared/Spaarke.Visuals`;
  - `src/solutions/CopilotAgent/`.
- **Samples (MIT):**
  - `pnp/copilot-pro-dev-samples` `mcp-cs-customers-secure-server`;
  - `microsoft/DataFactory.MCP`;
  - `microsoft/Power-CAT-Copilot-Studio-Kit` `mcp-apps/copilot-agent-kit`;
  - `microsoft/power-platform-skills` `plugins/mcp-apps`;
  - `modelcontextprotocol/conformance`.
