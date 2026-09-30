# Spike-3: Copilot Pane / Named Agent Launch

## Question
Can the Word Office add-in task pane use a documented Office.js API, URI scheme, protocol handler, web deep link, manifest/ribbon mechanism, or declarative-agent package identifier to open the Microsoft 365 Copilot pane or activate the named `Spaarke AI` declarative agent?

Research date: 2026-09-08. This is a bounded documentation and repository search, not an empirical Word-host probe.

## Timebox
- Start: 2026-09-08 09:38:00 -04:00
- End: 2026-09-08 09:46:22 -04:00
- Elapsed: approximately 8 minutes
- Limit: 2 hours
- Result: timebox respected; no promising undocumented lead was chased beyond the box.

## In-repo agent artifacts
- `src/solutions/CopilotAgent/declarativeAgent.json:1-8` has schema version `v1.2`, display name `Spaarke AI`, instructions, conversation starters, and an API-plugin action. It contains no URL or externally callable launch address; its manifest `id` field is absent.
- `src/solutions/CopilotAgent/appPackage/manifest.json:5-7` contains package GUID `f257a0a9-1061-4f9b-8918-3ad056fe90db`.
- `src/solutions/CopilotAgent/appPackage/manifest.json:26-31` maps package identifier `spaarkeAiAgent` to `declarativeAgent.json`. This is package wiring, not documented Office.js activation syntax.
- The repository `deepLinkUrl` matches are analysis-workspace handoff data, not Copilot or named-agent activation.

## Candidate 1: Office.js API
**Search method (2026-09-08):** Microsoft Learn Office.js reference pages and local `src/client/office-addins/**` search for `Office.context.ui`, `Office.addin`, `Office.actions`, `openPane`, `openBrowserWindow`, `openBrowserWindowAsync`, `Copilot`, `agent`, and `activate`.

**Sources and result:**
- [Office.UI interface](https://learn.microsoft.com/en-us/javascript/api/office/office.ui?view=word-js-preview), updated 2026-08-31: documented methods are dialog handling, `messageParent`, `closeContainer`, and `openBrowserWindow`; none opens Copilot or another installed extension. `openBrowserWindow` opens an external HTTP(S) URL.
- [Office.Addin interface](https://learn.microsoft.com/en-us/javascript/api/office/office.addin?view=word-js-preview), updated 2026-07-03: `showAsTaskpane` controls the current add-in only.
- [Office.Actions interface](https://learn.microsoft.com/en-us/javascript/api/office/office.actions?view=word-js-preview), updated 2026-04-24: `associate` binds this add-in's manifest action to this add-in's function.
- [Office.Context interface](https://learn.microsoft.com/en-us/javascript/api/office/office.context?view=word-js-preview), updated 2026-04-24: `ui` exposes the Office UI object above.

**Result:** No documented Office.js API opens the Microsoft 365 Copilot pane or activates a named declarative agent.

## Candidate 2: URI scheme or protocol handler
**Search method (2026-09-08):** Microsoft Learn searches for `Microsoft 365 Copilot URI scheme open pane`, `Copilot protocol handler`, `copilot://`, `ms-copilot:`, `ms-word:`, and `activate declarative agent`; repository search for `copilot://`, `ms-copilot`, `protocol`, `uri scheme`, and `deep-link`.

**Sources and result:** Microsoft Learn Copilot extensibility and declarative-agent documentation, the Office.js `openBrowserWindow` reference, and the local package search revealed no Microsoft-documented Copilot URI scheme or protocol handler. Any inferred `copilot://` or `ms-copilot:` value would be undocumented and does not qualify.

## Candidate 3: Web deep link to Copilot or a named agent
**Search method (2026-09-08):** Microsoft Learn searches for `Microsoft 365 Copilot deep link declarative agent`, `declarative agent URL activate`, `named agent URL`, and `open Copilot pane from Office add-in`; inspection of the documented Copilot web entry point and declarative-agent build flow.

**Sources and result:**
- [Create declarative agents using Microsoft 365 Agents Toolkit](https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/build-declarative-agents), updated 2026-08-11, documents opening `https://m365.cloud.microsoft/chat`, opening the conversation drawer, and selecting the agent. It documents no URL parameter or named-agent deep link.
- [Agents for Microsoft 365 Copilot](https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/agents-overview), updated 2026-08-11, describes agent channels but supplies no external activation URL.
- [Office.UI interface](https://learn.microsoft.com/en-us/javascript/api/office/office.ui?view=word-js-preview), updated 2026-08-31, permits generic browser navigation only.

**Result:** No documented deep link targets `Spaarke AI` or another named agent. Generic browser navigation is outside the Word-hosted Copilot pane and does not satisfy FR-20.

## Candidate 4: Manifest or ribbon mechanism
**Search method (2026-09-08):** Microsoft Learn and local manifest searches for `add-in command activate another add-in`, `ribbon activate Copilot`, `manifest target agent`, `Office.actions.associate`, `showAsTaskpane`, `Copilot`, and `declarative agent`.

**Sources and result:** [Office Add-ins manifest](https://learn.microsoft.com/en-us/office/dev/add-ins/develop/add-in-manifests), updated 2026-03-24, documents an add-in's own activation, commands, URLs, permissions, and UI, with no command target for another extension or Copilot. Local Word/Outlook command implementations register only this add-in's own actions. No documented cross-extension activation declaration was found.

## Candidate 5: Declarative-agent packaging identifier or activation URL
**Search method (2026-09-08):** Inspected `declarativeAgent.json`, `appPackage/manifest.json`, plugin manifest, cards, and the Microsoft Learn schema for `id`, `agentId`, `url`, `deepLink`, `activation`, `conversation`, and `launch`.

**Sources and result:** [Declarative agent schema 1.2](https://learn.microsoft.com/en-us/microsoft-365-copilot/extensibility/declarative-agent-manifest-1.2), updated 2026-07-29, defines optional `id`, name, capabilities, conversation starters, and plugin actions, but no task-pane launch URL or activation API. The local `spaarkeAiAgent` identifier is package metadata only; no externally callable activation address is exposed.

## Verdict
# NO MECHANISM FOUND
Within the two-hour bound, no documented mechanism was found that lets a Word task pane open the in-host Microsoft 365 Copilot pane or activate the named `Spaarke AI` declarative agent. Generic browser navigation and package/ribbon identifiers do not change this verdict.

## FR-20 disposition
FR-20 is **CLOSED as documented-negative** under `projects/spaarkeai-word-add-in-r1/spec.md:94`. No Copilot launch affordance will be built in this project. No button, icon, handler, manifest entry, or Copilot-related dependency was added.

## Open questions and leads not chased
- A live Word desktop probe could test undocumented host behavior, but the task rejects undocumented behavior as a positive mechanism; it was not pursued.
- Microsoft may introduce a future Copilot deep-link or host activation API; a later platform refresh would be a new investigation.
- Copilot Chat APIs can embed Copilot-powered responses, but that is a new in-pane integration rather than opening the existing Copilot pane or activating this named agent, and was outside this spike.