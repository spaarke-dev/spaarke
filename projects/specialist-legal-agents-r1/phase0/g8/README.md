# Phase 0 test G8: can a declarative agent use the tenant's Spaarke MCP connector?

> **Program:** specialist-legal-agents-r1 (design.md §8 G8). Same test as copilot-platform-coordination **S-3**.
> **Owner go:** 2026-11-23 ("Yes, run G8 early").
> **Throwaway.** Both test agents are removed as soon as the result is recorded here.
> **Who does what:**
> - **The owner** creates, uploads and removes the agents. Claude sessions never click tenant or admin actions (inventory rule).
> - **The owner or the test user** sends the prompts.
> - **This session** checks App Insights and records the result.

## The question

Can an agent with **no MCP action of its own** call tools from the "Spaarke MCP" federated connector already registered in the tenant (inventory C1, `/mcp/legal-operations`)?

**What the docs say.** Learn lists "Agents" among the experiences that support federated connectors. It also says an agent's own MCP actions are private to that agent. It says nothing about how an agent attaches a tenant connector.

**If the agent cannot use it**, every persona agent needs its own registration of the server. That breaks rule R1 (one registration per MCP URL) and goes to the owner before Phase 1.

## What is tested

| Step | Agent | Configuration | What it shows |
|---|---|---|---|
| **A1 (control)** | `spaarke-legal-g8-ab-test`, built in Agent Builder | No knowledge, no actions | Does an agent reach the tenant connector **by default**? |
| **A2** | the same agent, edited | Knowledge › Copilot connectors › **Spaarke MCP**, if the picker lists it | Can Agent Builder attach the federated connector, and do the calls then work? |
| **P1** | `spaarke-legal-g8-pkg-test`, packaged (`package/`) | `GraphConnectors` capability with no `connections` list (Learn: "all connectors the user can access") | Does the packaged route reach it, with no `agentConnectors` and no action? |

Run every step in **Chat**. Also run P1 in **Word (web)** and **Teams** where the agent is offered. Cowork cannot call declarative agents (design §3.11).

## Before you start

1. **"Spaarke MCP" is connected for the test user:** Chat › … › Settings › Sources › Spaarke MCP › Connected.
2. **The spike plugin (C2) is still installed.** It must stay until mcp-r1 runs S-1, so it is **not removed** for this test.
   - Its connector A shares the URL `/mcp/legal-operations`, and the BFF cannot tell the two registrations apart.
   - Two facts from earlier tests help: C2's package connectors failed in Chat before reaching the BFF (mcp-r1 T2/T13), and C2 is not an agent action.
   - So a BFF call during an agent conversation in Chat came through a **tenant** registration, not the agent's own action. That is what G8 asks.
   - Which tenant registration served it (C1 or C2-A) is recorded as "C1 probable". If it matters, repeat one prompt after C2 is removed.
3. **The dev BFF serves `spike_echo` and `spike_whoami`.** Dev build `a48398746d` includes spike-v2.
   - When mcp-r1 deploys without the spike (task 041), use a real read tool instead, for example the read tool listed first in `tools/list`.
4. **Send the inventory rows below to copilot-platform-coordination before creating anything (R4).** This is done (see "Inventory rows").

## Steps for the owner

### A1: Agent Builder, no knowledge

1. Open Microsoft Copilot › **New agent** › **Configure**.
2. Fill in:
   - **Name:** `spaarke-legal-g8-ab-test`
   - **Description:** `Phase 0 test G8. Not for real work.`
   - **Instructions:** paste the `instructions` text from `package/declarativeAgent.json`. The two agents must behave the same.
   - **Knowledge:** none. **Capabilities:** none.
3. Click **Create**. Keep it private: do not share it.
4. In the agent, as the test user, send these prompts in order, each in a new chat with the agent:
   1. `Call the Spaarke MCP tool spike_echo with the message g8-A1`
   2. `List every tool and source you can use in this conversation`
5. Note what the agent says, and the time.

### A2: the same agent with the connector as knowledge

1. Edit the agent › **Configure** › **Knowledge**. Is **Spaarke MCP** offered under Copilot connectors?
   - Note **yes or no**, and **what the picker lists** (screenshot).
2. If yes, add it, then **Update**.
3. Send `Call the Spaarke MCP tool spike_echo with the message g8-A2`, then `Call the Spaarke MCP tool spike_whoami`.

### P1: packaged agent

1. In PowerShell, from `projects/specialist-legal-agents-r1/phase0/g8/`. Files go at the zip root; the zip is gitignored.

   ```powershell
   Compress-Archive -Path .\package\* -DestinationPath .\spaarke-legal-g8-pkg-test.zip -Force
   ```

2. Upload it in the **dev tenant only**, and install it for the **owner and the test user only, never All users** (coordination rule R7).
   - In admin center › Agents › Tools › Registry, or Integrated apps, choose **Specific users/groups** and pick those two accounts.
   - Alternatively, use Teams Developer Portal › Apps › Import app, then Preview/Install for yourself only.
   - If the route you use cannot be scoped this way, **don't upload**. Tell this session where it would land, so the removal step is right and coordination can be told.
3. In the agent (Chat), send:
   - `Call the Spaarke MCP tool spike_echo with the message g8-P1`
   - `Call the Spaarke MCP tool spike_whoami`
   - `List every tool and source you can use in this conversation`
4. If Word (web) or Teams offers the agent, repeat the echo there: `g8-P1-word`, `g8-P1-teams`.

**Afterwards:** tell this session that you are done, with the times and screenshots. This session checks App Insights for each `g8-…` call and records the result. Then remove both agents (Agent Builder › … › Delete; the package from where it was uploaded), and tell copilot-platform-coordination they are gone.

## G8-P2: a coded agent with its own MCP action

> **Owner go:** 2026-11-23 ("Yes, prep G8-P2"). **Status:** package built and schema-valid; **not uploaded**. The inventory row went to copilot-platform-coordination before any upload (R4).

**The question.** G8 showed an agent cannot use the tenant connector (C1). Copilot's recommended Practice vehicle is a **coded declarative agent whose own action calls the MCP server**. Three things to establish:
1. **Does it work?** It should run our tools in Chat, Word and Teams, signing the user in through the existing OAuth registration (A1).
2. **Does each call run exactly once?** That means once inside the agent, and no extra call in plain Chat because the agent is installed. This decides coordination's **R1′**: "one tenant-wide connector per URL; agent-private actions allowed".
3. **Is the caller right?** `spaarke_show_decisions` should return **the signed-in user's own** Decide lane, the same counts as their Console category cards. That shows the call ran on-behalf-of the caller (ADR-028 A10). An unauthenticated call returns `UserContextRequired`.

**The live server (2026-11-24).** The dev BFF runs master `22c42efad6` with MCP on (mcp-r1 041, live since 00:22Z per coordination), and **the spike tools are gone**.
- The only tool the model can see is **`spaarke_show_decisions`**: read-only, no required input, renders the decisions widget.
- `spaarke_complete_decision`, `spaarke_list_decision_page`, `spaarke_get_decision_plan` and `spaarke_search_decision_lookup` are **app-only** (`_meta.ui.visibility = ["app"]`): the widget can call them, the model cannot (verified in master source).

**The package** (`package-p2/`, app id `802fd9cc-feae-425a-aaef-8e97aba87ff5`, short name `spaarke-legal-g8p2-test`):
- one declarative agent (v1.6) with one action, `ai-plugin.json`;
- the action uses plugin manifest v2.4 with runtime `RemoteMCPServer` on `https://spaarke-bff-dev.azurewebsites.net/mcp/legal-operations`;
- auth is `OAuthPluginVault` with A1's `reference_id`;
- **dynamic tool discovery** (`functions: []`, `run_for_functions: ["*"]`, Learn's default): the agent sees whatever the server lists to the model;
- all three manifests validate against Microsoft's published schemas.

**Hazard (accepted for a throwaway):** the widget can call the app-only write `spaarke_complete_decision`, and Copilot shows **no** confirmation for widget-initiated writes (mcp-r1 T3). So **do not decide, complete or dismiss anything in the widget.** Only look at it. Installation is the owner and test user only, and the agent is removed by 2026-11-30.

### Steps for the owner

1. **Baseline, before uploading.** In **plain Chat** (no agent), as the test user, with Spaarke MCP connected and C2 switched off as now: `Show my Spaarke decisions` (it should call `spaarke_show_decisions`). Note the time and the counts shown.
2. **The zip** is already built here (`spaarke-legal-g8p2-test.zip`, gitignored). To rebuild it, run in PowerShell from this folder:

   ```powershell
   Compress-Archive -Path .\package-p2\* -DestinationPath .\spaarke-legal-g8p2-test.zip -Force
   ```

3. **Upload in the dev tenant only, installed for the owner and the test user only, never All users (R7).** Use the same routes as before: admin center › Agents › Registry (or Integrated apps) with **Specific users/groups**, or Teams Developer Portal › Apps › Import app and install for yourself. If the route can't be scoped that way, don't upload; tell this session instead.
4. **In the agent (Chat)**, as the test user, each prompt in a new chat with the agent:
   1. `Call spaarke_show_decisions` and note the time, the counts, whether the widget rendered, and whether a **sign-in** prompt appeared. A1 is shared with C1, so the user may already be signed in.
   2. `List every tool and source you can use in this conversation`
5. **Plain Chat again** (agent installed but not selected): `Show my Spaarke decisions`. Note the time.
6. **Word (web):** open a **blank test document**, never a real NDA or anything in `answer-key/`. In the Copilot pane, pick the agent (agent list or @mention) and send `Call spaarke_show_decisions`.
7. **Teams:** open the agent's chat and send `Call spaarke_show_decisions`.

**If an approval card appears at any step, click Cancel.** None of these prompts should need one. Note the step and the tool the card names, because that is a result too.

**Afterwards:** tell this session you are done, with the times and screenshots. This session checks App Insights per minute and records the result. Then remove the agent where you uploaded it (by **2026-11-30**, latest) and tell copilot-platform-coordination it is gone.

### Pass criteria

| Check | Pass |
|---|---|
| Works in Chat | Step 4.1: exactly **one** `tools/call` in that minute (plus any widget `resources/read`), and the agent shows the decisions |
| Caller | Step 4.1: the counts match the user's Console Decide lane, not someone else's and not an auth error |
| No leak into plain Chat | Step 5: the same number of `tools/call` as the step 1 baseline |
| Word / Teams | Steps 6 and 7: one `tools/call` each, or a clear "agent not offered here" |

### G8-P2 run log (2026-11-24, owner + this session)

**Why it matters:** this is the build path for every Practice agent (design §3.12, D-A1 default). If the action doesn't attach, the default Practice vehicle doesn't work, so the cause has to be found. Recording "it didn't work" is not enough.

| Version | Change | Result |
|---|---|---|
| 1.0.0 | Dynamic tool discovery | Agent installed through the **admin center / org catalog** (owner confirmed 2026-11-24; this row earlier said "Teams custom upload" in error), as were 1.0.1–1.0.4. In the agent: "Tools I can see: none". **No MCP traffic at the BFF** (no `initialize`, `tools/list` or 401) |
| 1.0.1 | `spaarke_show_decisions` pinned in the package (`mcp-tools.json`) | Same: the tool is not visible even though it is pinned, and no BFF traffic. So **the action is not attaching**; this is not a discovery or sign-in step |
| 1.0.2 | Server URL with a trailing slash (`…/mcp/legal-operations/`). The BFF serves both forms, and the OAuth Base URL still prefix-matches | ❌ same. Rules out the shared-URL theory. (Tested whether the tenant ties an action to the existing **"Spaarke MCP" MCP-server object for the same URL** (admin center shows C1 as an MCP server with "Dependent plugins: Spaarke MCP") |
| 1.0.3 | Instructions point at the action, plain-language starters, canonical URL (Learn: no debug card is returned when the orchestrator skips the agent's skills) | ❌ The model: "the Spaarke MCP G8-P2 action is not currently exposed among my available tools". The new instructions were live. Still no debug card (the owner has an M365 Copilot Premium licence) |
| 1.0.4 **control** (`control-learn/`, same app id) | The same agent with its action on **Microsoft's public Learn MCP server** (`https://learn.microsoft.com/api/mcp`, auth None, dynamic discovery) | ❌ "Microsoft Learn search is not available in this session". So **the fault is not our server or OAuth**: MCP actions in declarative agents don't reach the model in this tenant. Admin center › Agents › Requests and Agents › Tools › Requests (preview) show **no pending requests**. Entra permissions don't apply: the control has auth None. **Superseded by the 1.1.0 org control: the conclusion was wrong** |
| 1.1.0 **org control** (`control-learn-org/` id `0421c980-…`) | The Learn control again with its own **fresh app id**, installed through the **org catalog** (admin center), the same route as 1.0.0–1.0.4 | ✅ **Works** (2026-11-24, owner screenshot). The agent answered from Microsoft Learn and named its connection `agent/learnorg`, which is this package's namespace. So **the org catalog route works and MCP actions work in this tenant**; what failed was app id `802fd9cc`. (Model self-report; Learn's server isn't ours, so there is no App Insights check. The Spaarke run below is the verified one) |
| 1.1.0 **real action** (`package-p2-sideload/` id `d38f8df8-…`) | The real G8-P2 package with a fresh id, installed through the **org catalog** like the control (the "sideload" folder name is historical). This upload ran the admin center's **full first-publish wizard** (Upload › Publish to users › Security policy: default template, no custom policies › Accept permissions: "No required permissions" › Review); the earlier updates to `802fd9cc` never showed it. The Learn org control did **not** get this wizard and still worked, so the wizard is not the cause; the fresh app id is | ✅ **PASS in Copilot Chat (2026-11-24, verified).** "What decisions are waiting for me in Spaarke?" → the agent rendered the real **decisions widget** (191 / 185 approve-or-rebudget / 6 invoice, matching the live lane) and summarised the counts. App Insights: client **Sydney** `tools/list` → `resources/read` (the widget) → **one** `tools/call spaarke_show_decisions` (5.2 s, ok) per turn, then the widget's own app-only `spaarke_list_decision_page` page loads. Runs at 14:34Z and 14:49Z each show exactly one `spaarke_show_decisions`. No sign-in card was needed (the A1 OAuth token was already held). **R1′ one call per turn: ✅ in Chat** |
| ~~1.1.0 sideload Learn control~~ (`control-learn-sideload/` id `125c3b10-…`) | Not needed: the org-catalog control already passed | Not uploaded |

**G8-P2 Chat result: ✅ the default Practice build path works** (coded declarative agent + its own MCP action on the BFF, existing OAuth registration, org-catalog install).

**Surfaces (2026-11-24 ~14:58–15:00Z, owner screenshots + App Insights):**

| Surface | What ran | Calls per turn | Result |
|---|---|---|---|
| **Word**, agent `spaarke-legal-g8p2-sl` (found through agent search in the Copilot pane) | "what invoice related decisions are in spaarke" → the agent rendered the **decisions widget** inside Word | 1 `spaarke_show_decisions` (14:59:24Z), then the widget's 4 app-only page loads | ✅ |
| **Plain Chat / Copilot in Word without the agent** (leak check) | The **`/spaarke-decisions` skill** from mcp-r1's plugin package. The trace reads "Used Spaarke Decisions". It calls through the tenant connector **"Spaarke MCP"** (C1), whose host approval card names that connector, not this agent's action | 1 `spaarke_show_decisions` (14:57:58Z) | ✅ **No leak.** With the agent installed, plain Copilot still makes exactly one call through its own path; the agent's action is not pulled in |
| **Teams** | The Copilot app inside Teams is the same M365 Copilot app as Chat (owner) | Same as Chat | ✅ by identity with Chat. The agent was not separately opened from a Teams agent chat |

Attribution: the owner gave no times. The two calls are told apart by order and by the widget page loads that only the agent turn produced. Both came from client `Sydney`, and App Insights does not record which action path made each call. **R1′ (one call per turn): ✅ in Chat, Word and plain Copilot.**

**V1 update test (prepared 2026-11-24; see `notes/agent-package-identity-and-distribution.md` §5):**
- **Package:** `package-p2-v1/` → `spaarke-legal-g8p2-sl-1.1.1.zip`. It is the **same app id `d38f8df8-…`**, version **1.1.1**.
- **The one change is in the action:** the MCP URL gets `?pkg=1.1.1` (`…/mcp/legal-operations?pkg=1.1.1`). The BFF routes it the same way; an unauthenticated probe gives 401 on all three URL forms. The OAuth Base URL still prefix-matches.
- **Steps:**
  - (1) Admin center: upload it as an **update** to the existing `spaarke-legal-g8p2-sl` (no new app). Approve it if it appears as **Update pending** in Agents › All agents › Requests, and note whether it did.
  - (2) Wait 5 minutes.
  - (3) New chat with the agent: "Show my Spaarke decisions". Note the time.
- **Read the result in App Insights `requests.url`:**
  - **`…?pkg=1.1.1` ✅:** the update carried the action change, so BR-3 (production updates) is safe.
  - **URL without `?pkg` ❌:** the old action is still bound; updates do not refresh actions. BR-3 becomes "action changes need a new app id or a re-publish", and that is a D-A1 deployment cost.
  - **No call at all ❌❌:** the update broke the action, as in 802fd9cc.
- **Side benefit:** `?pkg=<version>` in the action URL is a cheap way for the BFF to log **which package version** is calling (the note's version-skew row).
- **Run 1 (2026-11-24): 1.1.0 still running, update not applied yet.**
  - The owner uploaded 1.1.1 (~16:2xZ). The agent details still show **1.1.0**.
  - At ~16:35Z the agent rendered the widget in a new chat (owner screenshot). The BFF logged 3 Sydney `tools/call` (16:35:30 `spaarke_get_decision_plan`, then 2 × `spaarke_list_decision_page`), all on the **plain URL**, with no `?pkg`.
  - The catalog and the server agree, so this is not yet a V1 verdict. Next: check Requests for **Update pending**, approve it, and re-run.
  - **Follow-up (owner, ~16:45Z):** the admin center agent details show **Version 1.1.1**; the user's Copilot store card shows **1.1.0**; nothing in Requests. So the **admin catalog accepted the update with no approval step**, and the **user's installed copy lags**. That is propagation to the client, not a V1 verdict. Microsoft Q&A answers cite up to 24 hours (rarely longer) for app changes to reach clients, which cache. Next: retry in Copilot on the web in a new chat. If it still shows 1.1.0, remove the agent from your Copilot and add it again from Built by your org, then ask again.
  - **Run 2 (~16:50Z): 1.1.1 installed, action gone ❌.**
    - The owner removed and re-added the agent (it went through the multi-step new-agent flow). Copilot then showed **1.1.1**.
    - "show my decisions" got: "the required `spaarke_show_decisions` … action is not currently exposed to me".
    - App Insights from 16:38:52Z to 16:52Z: **no request from Sydney at all**, on any URL, not even a 401. Only a Cowork `initialize`/`tools/list` at 16:49 (the tenant connector). So the action failed **before** reaching the BFF, the same pattern as 802fd9cc.
    - **Two causes fit:**
      - **(H1)** a catalog update that changes an action leaves it unloaded;
      - **(H2)** Copilot rejects the `?pkg=` query string in an MCP action URL, so my test change itself broke it.
  - **Run 3 (prepared): 1.1.2 separates H1 and H2.**
    - `package-p2-v1b/` → `spaarke-legal-g8p2-sl-1.1.2.zip`: same id, **content identical to working 1.1.0** (diff checked), version only.
    - **If it works:** updates do carry action changes (1.1.0 → 1.1.1 → 1.1.2 each changed the URL), and **query strings break MCP actions** (H2). BR-3 holds, plus a new rule: no query string in action URLs.
    - **If it still says not exposed:** an action broken on an id stays broken through updates (H1, as 802fd9cc). BR-3 fails: action changes need a fresh id.
  - **Run 3 result (~16:55Z): ❌. 1.1.2 (identical to working 1.1.0) also says "not exposed".** Copilot shows 1.1.2; App Insights shows no MCP request of any kind, and no 401/403, from 16:52Z on.
    - ~~Established: later updates do not repair it.~~ **Wrong, corrected the same hour (see Run 4).**
  - **Run 4 (~17:36Z): ✅ 1.1.2 works.** About 40 minutes after the 1.1.2 upload (~16:53Z), and roughly 40 minutes after the failed run 3, the agent rendered the widget (owner screenshot). The BFF logged Sydney `tools/call` `spaarke_list_decision_page` at 17:36:41 and 17:37:03, on the plain URL. `show_decisions` was missed by the 25% sampling.
    - **Corrected finding:** after an update that changes the action, the action is **unavailable for a while**, then loads. Observed: still unavailable about 3 minutes after upload (run 3, plus 1.1.1 at about 25 minutes), working at about 40 minutes. It is a **delay, not a permanent break**.
    - **Likely also explains 802fd9cc:** five updates in close succession, each tested within minutes, so each probably restarted the delay. That is a hypothesis, not tested.
    - **Still open:** whether 1.1.1 (`?pkg=`) would also have loaded given time (H2). It was tested only about 25 minutes after upload, then replaced.
    - **Discriminators, if wanted (each needs a fresh id and an R4 row):**
      - **(a)** a fresh id whose **first** upload uses the `?pkg=` URL. If it works, query strings are fine and H1 holds.
      - **(b)** a fresh id, then an update that changes **only** the description or version. If the action survives, non-action updates are safe.
  - **V2 dynamic discovery (prepared 2026-11-24):**
    - **Package:** `package-p2-v2-dd/` → `spaarke-legal-g8p2-dd-1.0.0.zip`. Fresh id `cb5afd1c-…`, same server and auth config as the working agent. `functions: []`, `run_for_functions: ["*"]`, no `mcp-tools.json`.
    - The model can only see `spaarke_show_decisions`. The four other tools are widget-only, and the server refuses to start with a model-callable write tool.
    - **Steps:**
      - (1) Admin center: upload as a **new** agent; owner + test user only.
      - (2) **Data & tools** tab: note whether the dynamic-discovery banner shows.
      - (3) Owner, new chat: "Which Spaarke tools can you use?", then "Show my Spaarke decisions". Note the times.
      - (4) **Test user** (`demo@…`, never signed in to this agent): the same two questions. Note whether a **sign-in card** appears (#16973 says it may not).
    - **Pass:**
      - App Insights shows `tools/list` from Sydney, then `tools/call spaarke_show_decisions`;
      - the widget renders, and its own calls (`spaarke_list_decision_page` etc.) work;
      - the agent names only `spaarke_show_decisions`;
      - the test user gets a sign-in card, then data.
    - **Partial:** the owner passes but the test user gets no sign-in card. Then dynamic discovery works, but the OAuth first-run is broken (#16973), and BR-5 keeps pinned tools until a fix.
    - **Fail:** no `tools/list` from Sydney.
  - **V2 result, owner (2026-11-24 ~18:04Z): ✅** (owner report + App Insights)
    - The **Data & tools** tab showed the banner "This MCP server uses other tools, which may be changed by the publisher: Spaarke MCP V2", which is the documented dynamic-discovery marker.
    - App Insights:
      - 18:04:31 **`tools/list` from Sydney**: the runtime discovery, absent from the pinned agents' runs;
      - 18:04:34 **one** `tools/call spaarke_show_decisions` (ok, 50 rows, 5.6 s);
      - 18:04:42–18:04:50, 4 × `spaarke_list_decision_page`: the widget's own calls to app-only tools, which still work under dynamic discovery.
    - It worked on the first upload with no delay, consistent with the fresh-id runs.
  - **V2 result, test user `demo@demo.spaarke.com` (~18:10Z): ✅** (owner report + App Insights)
    - 18:09:59 `tools/list` from Sydney, then 18:10:01 **one** `spaarke_show_decisions` (ok, 50 rows, 8.8 s), then a widget `spaarke_list_decision_page`.
    - The BFF doesn't log caller identity on these requests, so the user is attributed by the owner's report and the timing.
    - Not proven: the **first sign-in card** (#16973). This user may already have a token for the A1 auth config from mcp-r1 task 042.
  - **Measurement caveat:** BFF requests are now sampled at **25%**, so not every call is logged. The `show_decisions` call for this turn is missing. Any logged call carrying `?pkg=1.1.1` is still proof; the absence of one needs several turns.

**Cause (working hypothesis):** something stored against app id `802fd9cc` in the tenant kept the action from loading. Most likely the first upload (1.0.0) bound a broken action state, and later **catalog updates of the same app id did not refresh the action**. Every change we tested (pinned tools, URL, instructions, even pointing at Learn) went in as an update to that id, so none of them could show. **The lesson for building agents:** test action changes on a fresh app id, and before customers rely on it, verify that an org-catalog **update** of an existing agent picks up changed actions (a D-A1 deployment check). Coordination S-3 called this correctly.

**Coordination S-3 (2026-11-24) challenges "platform limit"; points taken:**
1. The 1.0.4 control reused app id `802fd9cc`, so it was not independent. Hence the two fresh-id Learn controls above, one per route.
2. **Which route was 1.0.0–1.0.4 installed by?** Owner confirmed 2026-11-24: **admin center / org catalog**, all versions. So a personal sideload has never been tried.
3. Tenant checks to screenshot: Agents › Settings › **Agent and plugin access** (is "your organization" allowed?); Agents › Tools › **Registry** (any MCP-server object for `learn.microsoft.com/api/mcp` or the BFF URL, and its state); the agent's **Users** scope includes the test user; the **developer-mode debug card** Actions section.
4. D1's failure differs in kind: its function **is selected and executed** and fails at a pre-flight; G8's function is **never loaded**. Not one cause, though both are worth fixing.
5. Strongest discriminator: a **Microsoft-generated** sample agent (ATK "Start with an MCP server" / WIQD) with a fresh id through the org catalog. Needs the owner's OK to install the tooling.

**Probable cause found (2026-11-24, GitHub):** [OfficeDev/microsoft-365-agents-toolkit#16643](https://github.com/OfficeDev/microsoft-365-agents-toolkit/issues/16643), comment by kuldeepkeshwar (ATK 1.1.19). A byte-identical `RemoteMCPServer` agent with **auth None** works when **sideloaded** and fails when **published to the org catalog** and approved in admin center. The org copy shows "no … capability is available in the connected tools", zero MCP requests, an `enabledPlugins` entry with no name, and empty `functionsSelectedForInvocation`. Its admin-center Data & tools tab still lists the action. That matches every row above, including the Learn control. The issue is open with no Microsoft answer on whether org-catalog MCP agents are supported. Related: [#16973](https://github.com/OfficeDev/microsoft-365-agents-toolkit/issues/16973) (admin-uploaded MCP agent gets no sign-in card and no request), [#15592](https://github.com/OfficeDev/microsoft-365-agents-toolkit/issues/15592) (dynamic discovery, since shipped in ATK 6.12).
- **If the sideload works:** our package and server are right; the block is org-catalog distribution. That is a **D-A1 deployment risk** (customers install through the org catalog), not a build-path failure. Track #16643.
- **Secondary lesson from #16643:** an OAuth registration bound to a specific app id fails at Copilot's pre-flight with "The App ID used in the request does not match …" and sends nothing. Ours is **Any Teams app**, so keep it that way.
- **The older "Spaarke AI" agent (D1, OpenAPI action, 2026-11-24 ~05:30Z):** the model **selected and called** `spaarke_searchRecords`, which failed with "encountered an error when processing", and no `/api/` request reached the BFF. So non-MCP actions do load in this tenant; its call fails before leaving Copilot (likely the same pre-flight auth check or a stale base URL). Separate from G8-P2.

**Ruled out (owner screenshots):**
- OAuth registration "Spaarke Copilot Connector – dev": Base URL = the MCP URL, Restrict usage by org = my organization only, by app = **Any Teams app**.
- Package: Developer Portal **App validation passed, no errors** (v1.0.1).
- Admin center lists the agent as **Available**.

**Not yet seen:** the developer-mode debug card. The Edge sidebar doesn't show it; use `https://m365.cloud.microsoft/chat` in a normal tab.

**If 1.0.2 also fails, in order:**
1. The agent's **Data & tools** tab in the admin center: is the action listed?
2. The debug card's **Actions** section.
3. Regenerate the agent with Microsoft's own tooling (Agents Toolkit / Work IQ Dev Tools, "Start with an MCP server") and diff it against ours.
4. A Microsoft support case with the package and the no-traffic evidence.

**Fallback for D-A1:** Copilot Studio with an MCP tool (G10), where the tool is the agent's own.

## App Insights check (this session)

Use PowerShell. The `az` query fails with `BadArgumentError` when the KQL has `name=` aliases, so use the form below.

```powershell
$id = az resource show -g spe-infrastructure-westus2 -n spe-insights-dev-67e2xz --resource-type Microsoft.Insights/components --query properties.AppId -o tsv
az monitor app-insights query --apps $id --offset 3h --analytics-query "requests | where url contains '/mcp/' | summarize count() by name, resultCode, bin(timestamp, 1m) | order by timestamp desc"
```

**Results:**
- **Pass:** a `tools/call` lands in the minute of the prompt.
- **Fail:** nothing reaches the BFF, and the agent says it has no such tool.
- **Neither:** a model that claims a call ran when nothing reached the BFF has its claim recorded separately, as mcp-r1 T4 saw.

## Inventory rows (sent to copilot-platform-coordination before creation, R4)

| # | Artifact | Kind | Owner project | Purpose | Status | Removal trigger / who |
|---|---|---|---|---|---|---|
| G8-a | `spaarke-legal-g8-ab-test` | Agent Builder declarative agent, private to its creator. No actions. Knowledge: none (A1), then Spaarke MCP if offered (A2) | specialist-legal-agents-r1 | Phase 0 G8 / S-3: can an agent use the tenant connector? | **REMOVE** (throwaway) | Owner deletes it in Agent Builder the same day G8 is recorded; latest **2026-11-30** |
| G8-p | `spaarke-legal-g8-pkg-test` (app id `ac7da782-71bc-4c13-ba90-44eea8ef48b8`) | App package with one declarative agent (v1.6). `GraphConnectors` capability, **no `agentConnectors`, no actions** | specialist-legal-agents-r1 | As G8-a, packaged route | **DROPPED** (never uploaded) | Nothing to remove |
| G8-p2 | `spaarke-legal-g8p2-test` (app id `802fd9cc-feae-425a-aaef-8e97aba87ff5`) | App package with one declarative agent (v1.6) and **one action**: MCP plugin v2.4, `RemoteMCPServer` on `/mcp/legal-operations`, `OAuthPluginVault` = A1 (shared, no new registration), dynamic tool discovery. Installed for owner + test user only (R7) | specialist-legal-agents-r1 | Phase 0 G8-P2: does a coded agent with its own MCP action work in Chat/Word/Teams, and does each call run once (R1′)? | **PLANNED → REMOVE** (throwaway) | Owner removes it where uploaded the day G8-P2 is recorded; latest **2026-11-30** |
| G8-p2-sl | `spaarke-legal-g8p2-sl` (app id `d38f8df8-d248-4ab9-9c3d-4f7e6b513b19`) | Same as G8-p2 (action on `/mcp/legal-operations`, `OAuthPluginVault` = A1), namespace `spaarkeg8sl`, pinned `spaarke_show_decisions`. **Personal sideload, owner only** (not org catalog) | specialist-legal-agents-r1 | G8-P2 with a fresh app id. **Route changed 2026-11-24: org catalog, owner + test user only (R7)**, not a sideload | **PLANNED → REMOVE** (throwaway) | Owner removes it in admin center by **2026-11-30** |
| G8-p2-sl-learn | `spaarke-legal-g8p2-sl-learn` (app id `125c3b10-1833-468f-8141-6c54b8ab2085`) | Control: same agent, action on Microsoft's public Learn MCP server, auth None. **Personal sideload, owner only** | specialist-legal-agents-r1 | Same, with no Spaarke server or OAuth involved | **DROPPED** (not needed, never uploaded) | Nothing to remove |
| G8-p2-org-learn | `spaarke-legal-g8p2-org-learn` (app id `0421c980-686d-4953-b817-c4b4960c8d17`) | Same Learn control, namespace `learnorg`. **Org catalog, owner + test user only (R7)** | specialist-legal-agents-r1 | Route discriminator: org catalog with a fresh id | **PLANNED → REMOVE** (throwaway) | Owner removes it in admin center by **2026-11-30** |
| G8-p2-dd (V2) | `spaarke-legal-g8p2-dd` (app id `cb5afd1c-c0ef-409a-a2a7-fe7e00ddafa5`) | Own RemoteMCPServer action on `/mcp/legal-operations`, A1 `reference_id`, **dynamic discovery** (`functions: []`, `run_for_functions: ["*"]`), namespace `spaarkeg8dd`. **Org catalog, owner + test user only (R7)** | specialist-legal-agents-r1 | Test V2: runtime tool discovery with OAuth | **PLANNED → REMOVE** (throwaway). R4 row sent 2026-11-24 | Owner removes it in admin center by **2026-11-30** |

## Result (filled in after the run)

| Step | Surface | Time (UTC) | Agent's answer | BFF `tools/call` seen? | Verdict |
|---|---|---|---|---|---|
| A1 | Chat | ~20:45–20:50 | "I cannot see or call the Spaarke MCP `spike_echo` tool … `spike_echo` did **not** run." It listed M365 search, web, Python, image generation, tasks and document skills, but **no Spaarke tools**. Asked "can you see the Spaarke MCP connector": "No … none are exposed to me." Spaarke MCP **was** connected for the user | **No `tools/call`.** Chat (`Sydney 1.0.0`) did run `initialize` + `tools/list` three times (20:47:25, 20:49:17, 20:49:42Z), so discovery reached the server, but no tool was invoked (these may come from the plain-Copilot prompt below) | ❌ |
| A2 picker | — | same | Agent Builder › Knowledge › **Copilot connectors: "No connectors available"**. The federated connector is **not offered** as agent knowledge (that list shows synced connectors only) | — | ❌ (not attachable) |
| A2+ (owner's extra) | Chat | same | The owner added two skills to the agent (`spaarke-mcp-operations`, `spaarke-tool-test`) that tell it to call Spaarke MCP. Agent: "The Spaarke operations skill is available, but the underlying Spaarke MCP (`mcp__spaarke`) tool catalog is **not exposed to me in this session** … No Spaarke operation was executed" | No `tools/call` | ❌ A skill inside an agent does not bring the connector's tools in |
| Control | Chat, **plain Copilot** (no agent) | same | Copilot listed "discover available connector tools through `api_tool.list_resources` … a Spaarke MCP tool such as `spike_whoami` or a Spike echo tool". So outside an agent, the connector is reachable by discovery (as mcp-r1 T1/T5 showed) | (the `tools/list` calls above) | ✅ control |
| P1 | — | — | **Dropped** when G8 became final (its `GraphConnectors` capability is the knowledge list A2 showed empty) | — | — |
| P2-1 baseline | Chat, plain | | | | |
| P2-4.1 show decisions | Chat, agent | | | | |
| P2-4.2 list | Chat, agent | | | | |
| P2-5 leak check | Chat, plain | | | | |
| P2-6 | Word (web), agent | | | | |
| P2-7 | Teams, agent | | | | |

**Interim conclusion (2026-11-23, Agent Builder route):**
- **An Agent Builder agent cannot use the tenant's federated "Spaarke MCP" connector.**
  - The connector is connected for the user and works in plain Chat.
  - Inside the agent, its tools are not exposed.
  - Agent Builder does not offer it as knowledge.
  - Skills in the agent don't bring the tools in.
- **Consequence:** an agent reaches our tools only through **its own MCP action**, which Learn says is private to that agent. The packaged route (P1, `GraphConnectors`) is still to run, but it is unlikely to differ, since the Agent Builder knowledge list is that same capability.
- **What this means for R1:**
  - The double-firing in T2 came from **two tenant-wide connectors** on one URL, both visible in the same Chat.
  - An agent-private action is visible only inside that agent, and the tenant connector is **not** visible there (A1).
  - So an agent action pointing at `/mcp/legal-operations` should not double-fire. R1 could be refined to "one **tenant-wide** connector per URL; agent-private actions allowed".
  - This needs one more test (G8-P2): mcp-r1's existing spike agent package `spaarke-mcp-spike-agent.zip` has actions only, and its call should run once inside the agent. **Owner decision; proposed to copilot-platform-coordination, not decided.**
