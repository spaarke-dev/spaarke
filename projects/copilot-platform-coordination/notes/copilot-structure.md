# How the Spaarke MCP server fits with Copilot's building blocks

> Written 2026-11-23 because the owner asked how our connector relates to plugins, skills and tools, and shared an answer from Copilot about duplicate registrations.
> Labels: **[D]** documented on Microsoft Learn (checked 2026-11-23) · **[O]** observed in our dev tenant (mcp-r1 Phase 0, `projects/spaarkeai-mcp-bff-api-r1/notes/phase0-results.md`) · **[U]** unknown; a test is named.

## 1. The pieces, bottom up

| Piece | What it is | Where it lives | Spaarke example |
|---|---|---|---|
| **Tool** | One operation the model can call (`tools/call`), with a name, description and input schema | On our server | `spaarke_show_decisions`, spike `spike_echo` |
| **MCP server** | An HTTPS endpoint that lists tools and runs them. **It runs outside every package** [D] | The BFF, one route per product area | `https://spaarke-bff-dev…/mcp/legal-operations` |
| **Registration** (how Copilot learns the server exists) | A pointer to the server URL plus a sign-in. There are **four different kinds**, each with its own admin controls, and they are not interchangeable [D] | see §2 | "Spaarke MCP" (federated connector) |
| **Skill** | Instructions (`SKILL.md`): when to activate, steps, output format. It **does not contain tools**; it names them ("use the `search_case_law` tool") and the host links them at runtime [D] | Inside a plugin package, or a user's Cowork skills folder | `nda-review`, planned `spaarke-decisions` |
| **Plugin (package)** | The installable box: any mix of skills, connector registrations (`agentConnectors`) and agents. The admin installs, uninstalls, blocks or deletes the whole box [D] | Admin center › Agents › Tools, or a user's Cowork upload | "Spaarke MCP spike"; planned `spaarke` (040) |
| **Declarative agent** | A persona: instructions, knowledge and its own actions. MCP tools added as the agent's actions are usable **only through that agent** [D] | Packaged, or built in Agent Builder | "Spaarke AI" |
| **Copilot Studio agent** | A separately built agent; connects to MCP servers through Copilot Studio's own connection [D] | Copilot Studio (Power Platform) | "Spaarke Dataverse MCP", "Spaarke NDA Review" |

The short version: **the server and its tools exist once, on the BFF. Everything in the tenant is a pointer to it (a registration) or instructions that use it (a skill or agent).** A plugin is just a box for skills, registrations and agents.

## 2. The four ways to register the same server

| Registration | Who sees the tools | Admin control | Ours |
|---|---|---|---|
| **Federated connector** (admin center › Copilot › Connectors) | Users in Chat and Cowork, under Sources / Connected | Copilot connector settings [D] | **"Spaarke MCP"** (C1) — the one we keep |
| **Package connector** (`agentConnectors` in a plugin) | Users who have the plugin. Cowork: works [O]. Chat: our call failed before reaching the BFF [O] | The plugin's package controls [D] | Spike A and B (C2), to be removed |
| **Declarative-agent action** (MCP plugin manifest inside an agent) | Only that agent [D] | The agent's controls | none |
| **Copilot Studio connection** | Only that Copilot Studio agent [D] | Copilot Studio / Power Platform [D] | possibly "Spaarke Dataverse MCP" (a different server: Dataverse's) |

Microsoft documents these as separate artifacts with separate controls. Blocking or deleting a plugin does **not** stop another registration of the same server, and does not revoke sign-in or consent [D]. Learn does **not** say whether Copilot merges two registrations of the same URL [U].

## 3. Copilot's answer on duplicates, checked against our evidence

| Copilot said | Our evidence | Verdict |
|---|---|---|
| A tool call is one request to one server; nothing sends a request to both | T2: one Cowork prompt ran `spike_echo` on **both** registrations, and Copilot said "there are now two of them, so I ran both" [O]. That is the **model choosing to call each copy**, not the platform fanning out | **Right about the mechanism.** Our rule text "every tool runs twice" overstated it; the accurate statement is "the model sees two copies and may call one, the other, or both — and in our test it called both" |
| Tools appear twice under different prefixes, and the model picks one | Not documented [D: silent]. We did not record the tool names the model saw in T2 | **Unverified [U].** Test S-1 below |
| Copies can be signed in separately, as different accounts | Spike A, spike B and the federated connector use the same Dev Portal OAuth registration; A and B share one sign-in [O, T14]. Whether the federated connector shares that sign-in is not recorded | Plausible; in our setup the risk is low but not zero [U] |
| Duplicates waste context | Each copy adds every tool description again | Right (no test needed) |
| Different versions → the model may call the wrong one | Same URL, so same tool versions | Not our risk today; it would be if a package pinned an older tool list |
| Retries can repeat writes, so writes should be safe to repeat | Our write tools: widget writes have no host card (T3); model writes get a per-call card (T4). Neither is idempotent by design today | **Right, and it applies to us even with one registration.** Passed to mcp-r1 as a server-design point |

**What follows for the rules:** one registration per server (R1) still stands. Duplicates bring ambiguity, repeat calls (seen), extra context, and possible identity mismatch, and they add nothing. Only the stated reason changes.

## 4. Open structure questions and the test for each

| # | Question | Why it matters | Test | Who |
|---|---|---|---|---|
| S-1 | With two registrations of one URL, does the model see each tool twice (and under what names), or are they merged? | Settles Copilot's prefix claim; tells us whether duplicates are *visible* to admins and users | While the spike (C2) and "Spaarke MCP" (C1) are **both still connected**, ask Cowork and Chat to list their Spaarke tools; record the exact names, and the App Insights `tools/list` calls per registration | mcp-r1, before C2 is removed |
| S-2 (= T10) | Can a skill in one package drive the tools of a server registered elsewhere (the federated connector)? Learn documents only "tools from the same package" | Decides R2: skills-only packages vs one core package that carries the single registration | mcp-r1 T10 on the 040 package | mcp-r1 |
| S-3 | Can a declarative agent (packaged or Agent Builder) use the tenant's federated connector, or only its own actions? | Decides how the specialist persona agents and "Spaarke AI" reach our tools without a second registration | Add "Spaarke MCP" as a knowledge/source to a test agent; ask it to call `spike_echo` | specialist-legal-agents-r1 Phase 0 (row first, R4) |
| S-4 | Why does a package connector fail in Chat but work in Cowork? | Chat users can't rely on package connectors | Re-test after the next Copilot update; already in mcp-r1's list | mcp-r1 |
