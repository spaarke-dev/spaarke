# Phase 0 live test checklist (task 003)

Run as the **licensed non-admin test user** (`demo@…`) unless a step says "owner (admin)". Start a **new chat** for each test, and select **Spaarke MCP** via **+ › Sources** unless the step says not to. For each test, note what you saw and take a screenshot. Save screenshots to `projects/spaarkeai-mcp-bff-api-r1/notes/spikes/evidence/` named `T<n>-<host>.png`, or paste them in the session. Results go in `phase0-results.md`.

## Batch A — runnable now (current spike, dev BFF `6dbe69b9e2`)

| # | Where | Do | Record |
|---|---|---|---|
| T1 Cowork | Cowork | "Run a connectivity check with the spike echo tool." | Does the widget render? Does **Call echo** work? Init ms shown in the widget |
| T1 Word | Copilot in Word (web), any document | Same prompt | Widget renders / text only / tool not offered |
| T1 Outlook | Copilot in Outlook (web) | Same prompt | Same |
| T1 dark | Chat, Microsoft 365 set to dark theme | Same prompt | Does the widget follow the dark theme (no white panel)? |
| T5 Connect | (memory) | — | At first Connect, did the user see only a sign-in, or a consent screen too? |
| T6 | Chat | The 20 prompts below, one new chat each, source selected | For each: was a Spaarke tool called (yes/no, which one)? |
| T6b | Chat | Prompts 1, 3, 5, 7 and 9, source **not** selected | Same |
| T9 | Chat | "Find my most recent email about a contract, then run a connectivity check with the spike echo tool." | Did Work IQ (mail) and our tool both run in one answer? |
| E1 | Chat | Type `/` | Are there any Spaarke entries (skills, prompts)? List what appears |
| E2 | Cowork | Type `/` | Same |
| E4 | Chat | Prompt gallery / org prompts | Anything Spaarke-related offered? (expected: no; informational) |
| E5 | Chat | Type `@` | Does "Spaarke MCP" appear as an `@` target? If so, `@Spaarke MCP echo hello` |
| T8 | Cowork | "Use the spike echo tool, and ask me what message to send before calling it." | Did Cowork show a form or question (elicitation) or just ask in chat? |
| T10 | Cowork | Create a skill whose instructions say "call SpaarkeMCP_spike_echo with the user's text", then run it | Did the skill call the connector tool? |

### T6 prompts (none contain "Spaarke")

1. Run a connectivity check.
2. Echo back the phrase "matter intake ready".
3. What is my Dataverse user id?
4. Check that the legal operations connection is working.
5. Test the legal operations tools with the message "ping".
6. Who am I in the legal operations system?
7. Send "hello" through the echo tool.
8. Verify my sign-in to the legal operations backend.
9. Is the legal ops connector reachable?
10. Repeat after me using your tools: "decision pending".
11. Summarise the key risks in a standard NDA. *(control: should not call)*
12. Draft a short email asking a vendor for an updated W-9. *(control)*
13. What is the capital of Australia? *(control)*
14. List my meetings tomorrow. *(control; Work IQ)*
15. Explain what a limitation-of-liability clause does. *(control)*
16. Run the phase zero platform test.
17. Show me the system id the backend sees for me.
18. Do a round trip to the legal operations server with "test 18".
19. Which tools do you have for legal operations?
20. Confirm the backend is up by echoing "all good".

## Batch B — owner (admin) step, any time

| # | Do | Record |
|---|---|---|
| T13 setup | Admin center › Copilot › Connectors: add a second federated connector named **"Spaarke MCP B"** for `https://spaarke-bff-dev.azurewebsites.net/mcp/spike-area-b`, same OAuth registration if the portal accepts it | Whether the same registration was accepted for the second URL |
| T13 | As the test user, new chat, select both sources; "Use the B echo tool with 'route b'" | In **Sources**: how both display names show. Which tools each lists (A: `spike_echo`, `spike_whoami`; B: `spike_echo_b` only) |

## Batch C — runnable now (spike `d7369fecf1` deployed 2026-11-12 ~20:30Z in dev BFF `4e64670ea0`)

| # | Where | Do | Record |
|---|---|---|---|
| Size | Chat, then Cowork | "Use the spike echo 3mb tool with 'size'", then the same with 6mb | Widget renders? Init ms shown in the widget; how long until it appears (stopwatch is fine) |
| T3 | Chat, then Cowork | Run spike echo, then click **Call write (app-only)** in the widget, twice | Was there a confirmation card? Did it ask again on the second click (per call)? |
| T4 | Chat, then Cowork | "Use the spike write tool to record title 'Phase 0' and note 'T4 check'." Then a second write in the same chat | Does the card show the title and note? Choose "Allow for conversation"; did the second write skip the card? |
| T7 | Chat, then Cowork | "Echo 'instructions check' with the spike echo tool." | Did the message start with `SPK-T7:` (instructions honoured)? |
| T12 | Cowork | "Use the spike unannotated tool with 'x'." | Did Cowork treat it as destructive (warning or confirmation)? |
| T2 | Owner uploads the **`connectors/`** app package (`src/solutions/CopilotPlugins/spike-phase0/` on the spike branch, tip `d7369fecf1`; zip commands in its README). Optional second run with the `declarative-agent/` package. Test user in Chat and Cowork | Run the T1 prompt through the package rather than the federated connector | Widget renders in Chat? in Cowork? |
| T14 | Same `connectors/` package (two `agentConnectors` in one app) | Ask for both A and B echo tools | Do both appear and work from one package? If only B fails to sign in, the OAuth registration's Base URL likely covers `/mcp/legal-operations` only |

## Batch D — owner-only remainder (2026-11-14)

Everything the test user could run is recorded in `phase0-results.md` (Chat, Cowork, Word, Outlook). What is left needs the owner: an admin step, a persistent change to the test user, or the owner's memory. Dev BFF `b100effac3` still carries the spike; uac-r2 pings before any swap.

| # | Who / where | Steps | Record |
|---|---|---|---|
| **T5** | Owner, memory | Nothing to run. | At the test user's first **Connect** to Spaarke MCP: sign-in only, or a consent screen too (which permissions did it list)? |
| **T4-always** (persistent) | Test user, Chat | New chat › "Use the spike write tool to record title 'Phase 0' and note 'always'." On the card open **Allow once ▾ › Always allow**. Then a **new** chat: "Use the spike write tool to record title 'Phase 0b' and note 'second'." | Did the second write skip the card? Where is the setting shown, and how do you undo it (look under Copilot settings / the Spaarke MCP source)? Undo it afterwards. Optional: Cowork's **Always allow Spike write** and Word's **Approve all in this chat**, same question. |
| **T10** (creates a saved skill) | Test user, Cowork | **+ › Customize** › create a skill (name `spike-echo-check`; instructions: "When the user asks for a spike check, call the Spaarke MCP tool spike_echo with the user's text as the message, then report the echo."). New Cowork task: `/spike-echo-check hello`. | Did the skill call `spike_echo` (a "Spike echo" step and the widget)? Delete the skill afterwards. |
| **T13 setup** (admin) | Owner, Teams Developer Portal + admin center | The current OAuth registration's Base URL is the full `/mcp/legal-operations` URL, so it will not cover route B. (1) Developer Portal › Tools › OAuth client registration › **new** registration for the same connector app, Base URL `https://spaarke-bff-dev.azurewebsites.net/mcp/spike-area-b`, restricted to **Any Teams app**; it needs a client secret (a new Entra secret on the connector app — your step, value never shared). (2) Admin center › Copilot › Connectors › add MCP connector **"Spaarke MCP B"**, URL as above, OAuth 2.0, Reference ID = the new registration's ID (copy button). | Was the second registration needed, or did the first one work for B? Any error at **Authorize**? |
| **T13** | Test user, Chat | New chat; check both sources are on (+ › Change data sources). "Use the B echo tool with 'route b'." then "Which tools does each Spaarke source have?" | How both names show in the sources list and on the card; B lists only `spike_echo_b`, A does not list it. |
| **T2** (admin upload) | Owner, then test user | In `C:\code_files\spaarke-wt-spaarkeai-mcp-phase0-spike\src\solutions\CopilotPlugins\spike-phase0\`: `Compress-Archive -Path .\connectors\* -DestinationPath .\spaarke-mcp-spike-connectors.zip -Force`. Upload via Teams Developer Portal › Apps › Import app (or admin center › Integrated apps › Upload custom apps), dev tenant only. Test user: Chat and Cowork, "Run a connectivity check with the spike echo tool." | Does the package show as its own plugin/source (vs the federated connector)? Widget renders in Chat? in Cowork? |
| **T14** | Test user, same package | "Use the spike echo tool with 'A' and the B echo tool with 'B'." | Do both connectors from one package sign in and work? If only B fails, it is the registration Base URL (see T13 setup). |

Afterwards: remove the uploaded package and the "Spaarke MCP B" connector, or leave them until task 042 — owner's choice. Tell the session the results (or paste screenshots); it records them.

Notes for batch C (from the spike build, 2026-11-12):
- Microsoft Learn's MCP Apps table lists only `readOnlyHint` as supported in Copilot; `destructiveHint`, `idempotentHint` and `openWorldHint` are marked unsupported. So T4 and T12 test what Copilot does with readOnly false or with no annotation, not with destructive.
- App-only visibility (`_meta.ui.visibility: ["app"]`) may not be honoured in Copilot. If the model can see or call `spike_write_app`, record that under T3.
- The Learn docs say MCP plugin manifests (schema v2.4) work only as declarative-agent actions; standalone plugins for Chat and Cowork use `agentConnectors` in the app manifest. Hence two packages.
