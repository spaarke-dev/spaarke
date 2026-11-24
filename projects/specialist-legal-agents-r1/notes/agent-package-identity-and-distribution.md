# Agent package identity, updates and customer distribution

> **Status:** v1, 2026-11-24. It comes from the G8-P2 live test (`phase0/g8/README.md` § "G8-P2 run log").
> **Applies to:** every Spaarke Copilot app package with a declarative agent and an action (MCP or OpenAPI). That covers the Practice agents (D-A1 default) and any Ask/Do plugin package that carries an agent.
> **Verified vs not:** each statement is tagged **[verified]** (we saw it, with App Insights evidence), **[Learn]** (Microsoft documentation, checked 2026-11-24) or **[hypothesis]** (fits the evidence; the test in §4 settles it).

## 1. What happened

- Five versions of one agent (app id `802fd9cc…`, 1.0.0–1.0.4) were uploaded through the admin center as **updates to the same app id**.
- None of them loaded its action. The model said the tool was "not exposed", and no request reached the BFF.
  - This held even for 1.0.4, which pointed the action at Microsoft's own public Learn MCP server with no auth.
  - The admin center still listed the action under the agent's Data & tools. **[verified]**
- The same packages with **fresh app ids**, uploaded the same way, worked at once. **[verified]**
  - The Learn control answered from Learn.
  - The real Spaarke agent rendered the decisions widget and made one `tools/call spaarke_show_decisions` per turn (client `Sydney`, 2026-11-24 14:34Z and 14:49Z).
- The first upload of a new app id ran the admin center's full first-publish wizard (Publish to users › Security policy › Accept permissions › Review). The updates never showed it.
  - The Learn control did not get the wizard either and still worked, so the wizard is not the cause. **[verified]**
- **Cause:** **[hypothesis]**
  - The agent's action state was bound at the first upload of `802fd9cc` (1.0.0, built with dynamic tool discovery), and it did not load.
  - Later catalog updates to that app id did not rebuild it.
  - Microsoft documents no manual "replace a package" procedure for the registry. It says: "Don't assume that a new upload replaces an existing entry or preserves its availability settings" ([Monitor, update, and retire your plugin or agent](https://learn.microsoft.com/plugins/improve-plugin#update-your-plugin-or-agent)). **[Learn]**

What this is **not**:
- Not a tenant block, not a platform limit, not our MCP server, and not our OAuth registration. All four were ruled out by the fresh-id runs.
- Not the problem in [ATK #16643](https://github.com/OfficeDev/microsoft-365-agents-toolkit/issues/16643): our org-catalog installs work.

## 2. Build rules

**BR-1. An app package's identity is its app id (`manifest.json` `id`); treat it as permanent once uploaded.** The tenant keeps state against it: the action binding, the user scope, pins and the registry record. A new id is a new agent; an update keeps the old one's state.

**BR-2. In development, any change to an agent's actions goes on a fresh app id.** "Actions" means:
- the `actions` array;
- any `ai-plugin.json` runtime: type, URL, auth or `reference_id`;
- the function list, or dynamic vs pinned discovery;
- `mcp-tools.json`.

Instruction, starter, description and icon edits may go as a version bump on the same id. If a change doesn't show up, first rebuild it on a fresh id; don't debug the content. Record each dev id in the inventory (R4) and remove it when done (R7).

**BR-3. In production, the app id is stable and fixed per agent per customer; changes ship as version bumps.** Changing a production id strands users: they lose the agent, its pins and its scope, and the admin has to publish it anew. So production action changes must be proven to survive an **update** before they ship (test V1, §4). Until V1 passes:
- treat a production action change as a breaking release;
- plan it with the customer admin, including a fallback to publishing under a new id.

**V1 result (2026-11-24) [verified]: an update that changes the action takes the action offline for a while, then it loads.**
- **Steps on `d38f8df8`:**
  - 1.1.0 ✅.
  - Update 1.1.1 (URL gained `?pkg=1.1.1`): ❌ "action not exposed" when tested about 25 minutes after upload; no request reached the BFF.
  - Update 1.1.2 (byte-for-byte the 1.1.0 content): ❌ about 3 minutes after upload, then **✅ about 40 minutes after upload** (17:36Z).
- **Two lags stack:**
  - **The user's installed copy** lags the catalog version (re-adding the agent forces it).
  - **The action** then becomes available only after a delay, measured here as under 40 minutes.
- A first upload on a fresh id loaded at once in our runs. Only updates showed the delay.
- 802fd9cc's "permanent" failure was most likely five updates, each tested within minutes, so each probably restarted the delay. Hypothesis, untested.
- **Open:** whether `?pkg=` query strings work (H2). 1.1.1 was never given 40 minutes.
- **The rules this gives:**
  - **An action change via update is possible, but it causes an outage.** Plan it as a maintenance window: the action is unavailable for up to about 40 minutes (one observation), on top of the client lag.
  - **Wait at least an hour before judging any update**, and never stack updates to "fix" one that hasn't loaded yet.
  - **Prefer stable actions:** a frozen URL, auth and `reference_id`, with tool changes through the server (BR-4 plus **dynamic discovery**, V2). Then customers rarely need an action-changing update at all.
  - **No query strings in action URLs** until H2 is tested (V1a).

**BR-4. Keep action changes rare by keeping the package thin.** The package carries:
- the agent's identity, instructions and starters;
- one action pointing at the Spaarke MCP endpoint, with its `reference_id`;
- the tool pins.

Everything that changes often lives **behind** the MCP server (tool behaviour, playbooks, prompts, data and widgets). It ships through BFF deploys, which need no customer admin action and have no update risk.
- New tools are additive on the server.
- Tool names and input schemas are a **compatibility contract**. Older packages in customer tenants must keep working, so never rename or remove a tool in place.

**BR-5. Prefer pinned tools for production; adopt dynamic discovery only after it passes on a fresh id with OAuth.**
- Pinned tools (`functions` + `mcp_tool_description`) are the documented, deterministic shape, and they worked here. **[verified]**
- Dynamic discovery (`functions: []`, `run_for_functions: ["*"]`) would let new tools reach existing installs without a package update. That would be ideal for BR-4.
- But [ATK #16973](https://github.com/OfficeDev/microsoft-365-agents-toolkit/issues/16973) reports that admin-uploaded dynamic-discovery agents with OAuth show no sign-in card on first use, and our only dynamic-discovery upload was the broken 1.0.0.
- So test V2 decides this.

**BR-6. The OAuth registration stays "Any Teams app".** A registration bound to a specific Teams app id fails Copilot's pre-flight check, because Copilot presents a different id ([ATK #16643](https://github.com/OfficeDev/microsoft-365-agents-toolkit/issues/16643); Learn's warning on [OAuth auth config](https://learn.microsoft.com/plugins/plugin-authentication-oauth#use-the-teams-developer-portal)). **[Learn]** "Restrict usage by org" is a separate setting; see §3.2.

**BR-7. Every package upload is verified at the server, not from the chat.** For each agent and surface, a pass means one `tools/call` per turn in App Insights from client `Sydney`. The model's own account of what it called is not evidence: in 1.0.3 it claimed nothing, and in other runs it has claimed calls that never happened.

**Enforced by:** prose for now. It should be mechanised in the **package generator** (the Practice-agent generator in this program).
- (a) Hash the action section of each build. If the hash differs from the last uploaded build on that id, a dev build gets a fresh id and a production build is flagged for V1-style update verification.
- (b) Fail the build if the OAuth registration isn't "Any Teams app".
- (c) Write the R4 inventory row.

The guard ships with must-fire and must-not-fire cases, per root CLAUDE.md §16.

## 3. Distributing to a customer tenant

### 3.1 What a customer receives

- A Copilot app package (zip): `manifest.json`, the declarative agent, `ai-plugin.json` with the MCP URL and `reference_id`, the tool pins, and icons.
- **It is generated per customer.** Two values differ by customer:
  - the **BFF URL** (per stamp in Model 2; per region or stamp in Model 1);
  - the **`reference_id`** (the auth config lives in a tenant; see 3.2).
- The app id is minted once per customer per agent (BR-3) and recorded in that customer's provisioning record. This is the agent half of `customer-provisioning-orchestration-r1`: the same intake that stamps the BFF also generates and records the agent packages.

### 3.2 Auth config and app registration per deployment model

| | Model 2 (per-customer stamp in the customer's tenant) | Model 1 (shared Spaarke stamp; users from their home tenant) |
|---|---|---|
| BFF Entra app | Customer tenant (already so) | Spaarke tenant, used cross-tenant |
| OAuth auth config (`reference_id`) | Created **in the customer tenant** by provisioning, Restrict-by-org = **My organization only**, by app = **Any Teams app**. One per customer | Created once in the Spaarke tenant with Restrict-by-org = **Any Microsoft 365 organization**. Learn describes this setting for Store-published apps **[Learn]**. **Whether a customer's own org-catalog upload can use a Spaarke-tenant auth config is unverified** (test V3) |
| Token the BFF sees | Customer-tenant user token; A10 as written | Home-tenant user token for Spaarke's app. Needs ADR-028 A10's "OAuth pinned to the Spaarke tenant" squared with the A8/A9 home-directory rules: a **T-7 item** |
| Fit today | **Ready**: what we just tested, in one tenant | **Not proven**: needs V3, then an A10 decision |

Entra-protected MCP servers can't use dynamic client registration, so the static auth config is always needed ([Learn, Entra ID considerations](https://learn.microsoft.com/plugins/plugin-authentication-oauth#microsoft-entra-id-considerations)). **[Learn]** It must also have admin consent in the customer tenant for the BFF's delegated scope (`user_impersonation`) before the first user signs in. "Provisioning doesn't check whether the scope can be consented to." **[Learn]**

### 3.3 How the customer installs and updates it

- **Install:** the customer's admin uploads the zip in the admin center (Agents › Upload agent). They publish it to the practice groups (Agent 365; "distribution follows practice areas" in design.md) under the default security policy. That is the route verified here.
- **Update:**
  - We send a new zip with the same app id and a higher `version`.
  - The admin uploads it, and it shows as **Update pending** in Agents › All agents › Requests. Users keep the old version until the admin approves ([Learn](https://learn.microsoft.com/microsoft-agent-365/admin/agent-requests#requested-agents-pending-update)). **[Learn]**
  - **Observed 2026-11-24 (V1 run 1) [verified]:**
    - An **admin's own upload** of an update went straight into the catalog (Version 1.1.1 in agent details), with nothing in Requests. Update pending applies to submissions, not to the admin's own uploads.
    - Users' **installed copies lag**: the user's Copilot store card still showed 1.1.0, and the agent still called the 1.1.0 action. Plan for **version skew** after every update (hours, by Microsoft Q&A answers up to 24 h).
    - The BFF must accept the previous package version's calls (BR-4). The `?pkg=` marker measures how fast customers converge.
  - Users may have to consent to the update.
  - Pinned agents may need re-pinning ([Learn](https://learn.microsoft.com/microsoft-365/copilot/extensibility/convert-declarative-agent)). **[Learn]**
  - Our G8-P2 owner saw no pending request for the 802fd9cc updates. We need to confirm whether those updates were applied silently or never applied (V1).
- **Rollback:** none documented. The admin can block or unblock, but that doesn't restore an earlier package. **[Learn]** Our rollback lever is the server: keep the BFF backward-compatible, and revert server-side behaviour there.
- **We cannot push a package into a customer tenant.** Each package change costs the customer an admin action. That is why BR-4 keeps them rare.

### 3.4 Through Microsoft Marketplace instead (later option)

- An agent can be listed in the Microsoft 365 and Copilot store and **linked to a SaaS offer** in Partner Center ([Learn](https://learn.microsoft.com/partner-center/marketplace-offers/transacting-commercial-marketplace)). **[Learn]**
- Updates then flow through the store, with admin and user consent where permissions change.
- It brings Microsoft-managed licensing (below) and discovery.
- It also brings store validation, a multitenant auth story (Model 1-like, V3), and Microsoft's review cycle on every package change.
- **Recommendation:** customer-uploaded packages (3.3) for the first customers; Marketplace when we sell self-serve.

### 3.5 A second channel: SPFx `.sppkg` with Copilot UX components (added 2026-11-24)

The design covers this in §3.13. For distribution, the differences from 3.3 are:
- **Upload path:** the SharePoint App Catalog, then **Add to Teams**, which syncs the baseline agent. The admin-center zip upload isn't used. **[Learn]**
- **Updates:** redeploy the `.sppkg`. Component code, tool schemas and adding or removing components update **without** re-sync, through dynamic discovery (a few minutes). Agent instructions, knowledge, starters, icon and additional actions (our MCP action) **need** a re-sync. **[Learn]**
- **Identity:** BR-1 still applies. Keep the solution ID, component IDs and app ID stable; Learn says changing them "can create a separate agent or break the association". **[Learn]**
- **Rollback:** documented. Redeploy a known-good package, and re-sync the baseline if it changed. **[Learn]**
- **Store:** the SharePoint Store accepts these solutions from GA. That is an alternative to 3.4 for a Practice agent that ships components. **[Learn]**
- **Consent:** API access requests go to SharePoint's tenant-wide principal (design T-8). Each tenant still configures its own permissions, agent policy and licences. **[Learn]**
- **Open:** whether an MCP-action change in a `.sppkg` agent loads after re-sync. G11 tests this alongside V1.
- **Update, 24 Nov 2026: plugin packaging.**
  - Components can also ship in a **plugin ZIP** (admin center Agents › Tools › Registry), usable in Chat and Cowork without an agent. **[Learn]**
  - The ZIP points `agentConnectors` at `https://<tenant>.sharepoint.com/_api/mcp/spfx?solutionId=<id>` with authorization `None`, so the **ZIP is per tenant**. **[Learn]**
  - The `.sppkg` must stay deployed in the App Catalog. **[Learn]**
  - A customer therefore receives a **`.sppkg` (once per release) plus a per-tenant plugin ZIP**, which the package generator writes. **[Learn + inference]**
  - Packaging is manual until SPFx 1.25. **[Learn]**

## 4. Our control: access, licensing, updates

**The package is not a control point.**
- Once a customer has a zip, they can keep, copy or roll back to it.
- Who sees the agent is the customer admin's choice (scoping, blocking).

**Our control is the server every action call must reach: the BFF MCP endpoint.**
- Every call carries the user's Entra token (`tid`, `oid`, `aud`).
- So access, licensing and kill-switch decisions belong there, in one place, for every surface (Chat, Word, Teams, Cowork, the Console).

| Need | Mechanism | Where it lives |
|---|---|---|
| Only licensed customers' tenants | Tenant entitlement check on `tid` per call: stamp config in Model 2, an entitlement record in Model 1 | BFF auth layer (ADR-028; extends `WorkforceIdentity:CustomerTenantIds` thinking) |
| Only licensed users within a tenant | Per-user seat or licence check on (`tid`, `oid`) against the Spaarke user/licence record (the `systemuser` caller rule in A10 already requires a Spaarke user) | BFF, Dataverse |
| Which agents a firm has bought | Entitlement per agent or practice area: the BFF refuses tools outside the firm's licensed set; the tool list can be filtered per entitlement | BFF MCP tool registry |
| Sold through Marketplace | Microsoft license management + the **usageRights Graph API** to check whether the signed-in user holds a licence; SaaS Fulfillment API for the subscription lifecycle ([Learn](https://learn.microsoft.com/microsoftteams/platform/concepts/deploy-and-publish/appsource/prepare/include-saas-offer)). Or **BYOL**: Spaarke licence sold directly, stated in the listing ([Learn](https://learn.microsoft.com/microsoft-agent-365/developer/submit-agent-partner-center)) | BFF calls Graph; Partner Center |
| Stop service (non-payment, breach, offboarding) | Disable the tenant entitlement: every call fails closed at once on every surface, whatever packages are installed. Revoking admin consent or the auth config is a second lever | BFF config, Entra |
| Ship improvements | Server-side by default (BR-4): no customer action needed. Package changes only for agent identity, instructions or new pins, delivered as 3.3 updates | BFF deploy; customer admin for packages |
| Version skew | Tool names and schemas stay backward-compatible (BR-4). The BFF logs the calling package version where Copilot exposes it, so old packages can be found and retired | BFF |
| Copilot licence on the customer side | Users need a Microsoft 365 Copilot licence for agents with actions in Chat (T11 ⚠️) and credits in Cowork. That is the customer's Microsoft cost, separate from ours (D-A5) | Customer |

**Open design items this creates** (for `/design-to-spec`, not now):
- A **BFF entitlement check** for MCP calls, per tenant, user and agent. It is new surface under BFF hygiene §10/§11: one owner, fails closed.
- The **package generator plus per-customer package record** in provisioning.
- The **Model 1 agent auth** decision (V3, then T-7).
- **D-A5:** pricing, and Marketplace vs direct.

## 5. Tests that close the hypotheses

| # | Test | Settles | Cost |
|---|---|---|---|
| **V1** | Update the working agent (`d38f8df8`) with an action change (done as a URL change: `?pkg=1.1.1`) | BR-3: are updates with action changes safe in production? | **⚠️ 2026-11-24:** the action went offline after each update and came back about 40 minutes after 1.1.2. Updates work, with an outage. See BR-3 |
| **V1a / V1b** | (a) A fresh id whose first upload uses a `?pkg=` URL. (b) A fresh id, then an update changing only the description | Separate H1 from H2; are non-action updates safe? | 10 minutes each; 2 fresh ids |
| **V2** | A fresh-id agent with **dynamic discovery + OAuth**, through the org catalog, as a user who has not signed in yet | BR-5: can new tools reach installed agents without package updates? | 10 minutes |
| **V3** | An agent package uploaded in a **second tenant**, using a Spaarke-tenant auth config set to "Any Microsoft 365 organization" | Model 1 distribution (3.2) | Needs a second tenant |
| **V4** | Word and Teams on `d38f8df8` (G8-P2's remaining surfaces), plus the plain-Chat leak check | G8-P2 / R1′ | In progress |
| **V5** | The older "Spaarke AI" agent: its OpenAPI action is selected but fails before reaching the BFF (likely an auth-config app binding or a stale base URL) | A separate defect; check its auth config against BR-6 | 15 minutes |
