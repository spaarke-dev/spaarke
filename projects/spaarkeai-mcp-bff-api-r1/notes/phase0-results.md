# Phase 0 results (task 003, FR-19)

> Test list and gates: `design.md` §13. Every result comes from a live run in the dev tenant against the deployed spike. Evidence names a screenshot, an App Insights record or a transcript excerpt; no tokens, claim values or privileged record text (ADR-015).
>
> **Spike deployed:** results before 2026-11-12 20:30Z ran on spike `755987b385` (dev BFF `6dbe69b9e2`). From 2026-11-12 ~20:30Z the dev BFF is `4e64670ea0` (uac-r2 local merge of master `350ca3da66` + spike `755987b385` + spike `d7369fecf1`, owner per-SHA go); verified 20:34Z: `tools/list` has all seven legal-operations tools and `initialize` returns the T7 instructions. Route `https://spaarke-bff-dev.azurewebsites.net/mcp/legal-operations`; second route `/mcp/spike-area-b` (T13).
> **Connector:** federated connector "Spaarke MCP" (dev tenant), OAuth registration and client in `phase0-registration-facts.md`.
> **Test user (consent and approval tests):** `demo@…` (licensed non-admin; Microsoft 365 Copilot licence; no Entra roles; verified 2026-11-12, `phase0-registration-facts.md` §d). The owner's admin account is not used for user-behaviour results.
> **Owner review:** _pending_ (step 7).

## Gate summary

| Gate | Test | State | Decision |
|---|---|---|---|
| Gates everything | T5 | ✅ passed 2026-11-12 | Project continues |
| Gates Phase A (020+) | T0 | ✅ passed 2026-11-12 | 020+ may start |
| Spaarke AI fallback (043) | T1 (Chat) | ✅ widget renders in Chat 2026-11-12 | 043 cancelled; re-check after Ignite (17–20 Nov) |
| Route mechanism (020) | T13 | 🔲 local pass only; live pending | — |
| Widget budget (030) | Size | 🔲 pending (spike change needed) | — |
| Wizard copy (032) | T3 / T4 | 🔲 pending (spike change needed) | — |

## Results

| Test | Date | Route / host | Result | Evidence | Decision |
|---|---|---|---|---|---|
| **T5** OAuth via federated connector; OBO WhoAmI | 2026-11-12 | `/mcp/legal-operations`; Copilot Chat (web) | ✅ **Passed.** `SpaarkeMCP_spike_whoami` called as the test user: `aud` matches the BFF app, `scp` has `user_impersonation`, `tid` = Spaarke dev tenant, `oid` = the test user; OBO WhoAmI returned that user's own `systemuser` (not the owner's, not an app user). | `phase0-registration-facts.md` §d (owner screenshot of the tool JSON; oid and systemuserid cross-checked in Graph and Dataverse) | Gate open: the project continues. No ADR-028 A10 stop. |
| **T0** Stateless calls through the connector; `Accept` / 406 | 2026-11-10, 2026-11-12 | `/mcp/legal-operations`; connector `CopilotConnector 1.0.0` and `Sydney 1.0.0` | ✅ **Passed.** Through the connector: `initialize` and `tools/list` 200, `notifications/initialized` 202, GET 405 (no SSE stream: stateless), then `tools/call` from Chat (2026-11-12 16:12Z). Direct check 2026-11-12 19:07Z with an owner token: no token → 401 `WWW-Authenticate: Bearer`; `Accept: text/plain` → 406; `Accept: application/json, text/event-stream` → 200; GET → 405. | App Insights `spe-insights-dev-67e2xz` requests 2026-11-10 18:59Z (two handshakes) and 2026-11-12 15:42Z / 16:12Z; curl transcript above (status codes only) | Gate open: 020+ may start. Note for 020/§6.3: the 401 carries a bare `Bearer` challenge with no `resource_metadata` (RFC 9728). The connector does not need it (pre-registered OAuth), but the conformance suite may check for it. |
| **T1** Widget through the connector — Chat | 2026-11-12 | Copilot Chat (web), test user | ✅ **Renders.** `SpaarkeMCP_spike_echo` rendered `ui://spaarke/spike.html` inline (`connected: true`, init 30 ms, theme light, 296,336 pad bytes ≈ 1 MB widget). The widget's **Call echo** button made an app-initiated `callServerTool` round trip. | Owner screenshot (shared in session 2026-11-12, 16:12Z); App Insights 16:12:50Z `server/discover` 200 from `Sydney 1.0.0` (the `tools/call` rows were sampled out at 33%) | **043 cancelled** (FR-17: the fallback is built only if Chat cannot render the widget). Re-check after Ignite and record it here. |
| T1 — Cowork | | Cowork | 🔲 | | |
| T1 — Word | | Copilot in Word | 🔲 | | |
| T1 — Outlook | | Copilot in Outlook | 🔲 | | |
| T1 — dark theme (ADR-021) | | Chat, M365 dark theme | 🔲 | | |
| **T2** Plugin package: widget in Cowork and Chat | | plugin package | 🔲 needs a spike plugin package | | |
| **T3** Widget calls a write tool (app-only, not readOnly): confirmation card? per call? | | Chat, Cowork; test user | 🔲 needs spike write tool | | |
| **T4** Model calls a write tool: parameters on the card? "Allow for conversation" persists? | | Chat, Cowork; test user | 🔲 needs spike write tool | | |
| **T6** 20 prompts without "Spaarke": tool invocation rate | | Chat | 🔲 | | |
| **T7** MCP `instructions` honoured | | Chat, Cowork | 🔲 needs spike `instructions` | | informational |
| **T8** Cowork elicitation | | Cowork | 🔲 | | informational |
| **T9** Work IQ plus our tool in the same turn | | Chat | 🔲 | | informational |
| **T10** A skill naming a connector tool | | Cowork | 🔲 | | informational |
| **T11** Behaviour without a Copilot licence | 2026-11-12 | Copilot Chat (Basic), test user before licence | ⚠️ Observed, informal: with only Copilot Chat (Basic) the connector is not offered in Sources; after the licence and a new chat it is. | `phase0-registration-facts.md` licence-precondition notes | informational: user-setup docs (042) state the licence requirement |
| **T12** Cowork treating unannotated tools as destructive | | Cowork | 🔲 needs spike unannotated tool | | informational |
| **T13** Two tool sets on two routes; display name in Sources and on the approval card | local: 2026-11-10 | `/mcp/legal-operations`, `/mcp/spike-area-b` | Local in-process pass (each route lists only its own tools); live pending a second connector on `/mcp/spike-area-b` | `notes/spikes/phase0-spike-measurements.md` §4 | 🔲 |
| **T14** Two connectors in one app package | | plugin package | 🔲 needs a spike plugin package | | |
| **E1** Skill under `/` in Chat | | Chat | 🔲 (MC1493223 rollout) | | |
| **E2** Skill under `/` in Cowork | | Cowork | 🔲 | | |
| **E3** Direct prompt | 2026-11-12 | Chat | ✅ A direct prompt ("can you see the spaarke mcp") with the source selected invoked the tool. Earlier, in a chat opened before the licence refreshed and with no source selected, Copilot answered without the tool (two causes at once, so not a clean negative). | `phase0-registration-facts.md` 16:12Z entry | Users select Spaarke MCP via **+ › Sources**; record for 042's user guide |
| **E4** Org prompts | | Chat | 🔲 | | informational |
| **E5** `@` mention | | Chat | 🔲 | | |
| **Size** 1 / 3 / 6 MB init time | 1 MB: 2026-11-12 | Chat | 1 MB: init 30 ms in Chat. 3 and 6 MB: pending (not bound to a tool) | T1 Chat row | 🔲 budget for 030 |

## Open items for the owner

- What did the test user see at **Connect**: sign-in only, or a consent screen too? (T5 follow-up; record here.)
- Save the T1 Chat screenshot to `notes/spikes/evidence/` if you want a file path rather than "shared in session".
