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

## Batch C — after the new spike SHA is deployed (owner per-SHA go in uac-r2)

| # | Where | Do | Record |
|---|---|---|---|
| Size | Chat, then Cowork | "Use the spike echo 3mb tool with 'size'", then the same with 6mb | Widget renders? Init ms shown in the widget; how long until it appears (stopwatch is fine) |
| T3 | Chat, then Cowork | Run spike echo, then click **Call write (app-only)** in the widget, twice | Was there a confirmation card? Did it ask again on the second click (per call)? |
| T4 | Chat, then Cowork | "Use the spike write tool to record title 'Phase 0' and note 'T4 check'." Then a second write in the same chat | Does the card show the title and note? Choose "Allow for conversation"; did the second write skip the card? |
| T7 | Chat, then Cowork | "Echo 'instructions check' with the spike echo tool." | Did the message start with `SPK-T7:` (instructions honoured)? |
| T12 | Cowork | "Use the spike unannotated tool with 'x'." | Did Cowork treat it as destructive (warning or confirmation)? |
| T2 | Owner uploads the spike plugin package (`src/solutions/CopilotPlugins/spike-phase0/` on the spike branch); test user in Chat and Cowork | Run the T1 prompt through the plugin rather than the connector | Widget renders in Chat? in Cowork? |
| T14 | Same package (two plugins in one app) | Ask for both A and B echo tools | Do both plugins appear and work from one package? |
