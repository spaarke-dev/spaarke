# Claude for Word: sign-in modes (2026-11-07)

> Quick lookup prompted by the owner's screenshots of the add-in's sign-in screen ("Log in" vs "Cloud provider or
> gateway" with Gateway / Vertex / Bedrock / Azure tabs). Labels: [C] cited verbatim, [S] from the fetch tool's summary
> of the unread tail of the third-party-platforms page (verify against the live page), [I] inferred.

Sources: https://claude.com/docs/office-agents/third-party-platforms.md,
https://claude.com/docs/office-agents/word, https://claude.com/docs/office-agents/connectors-and-skills.md.
(support.claude.com/en/articles/13945233 returned 404.)

## One add-in, two sign-in modes

There is one add-in, **Claude for Microsoft 365** (Word, Excel, PowerPoint, Outlook) on AppSource. There is **no
separate "Claude for Legal" Word add-in** [C: none named on any page]. Claude for Legal is a set of plugins for chat,
Cowork and Claude Code. The Word add-in has legal-oriented built-in behaviour: counterparty redline review, legal
numbering, defined terms, cross-references, a "Legal contract review" use case [C].

| | Log in (Claude account) | Cloud provider or gateway |
|---|---|---|
| Account | Pro, Max, Team, Enterprise [C] | No Claude account; admin chooses Bedrock, Vertex AI, Azure AI Foundry or an LLM gateway [C] |
| Who sets it up | User installs from AppSource, or M365 admin deploys | IT admin, with the `claude-for-msft-365-install` Claude Code plugin, which generates `manifest.xml`; uploaded in M365 Admin Center › Integrated apps [C] |
| Where prompts go | Anthropic, under the account's terms | "only to your chosen inference provider"; Anthropic serves UI, feature config and telemetry from `pivot.claude.ai`, not content [C] |
| Skills | "Skills you've enabled in your Claude settings" [C] | Not settings skills; built-in, admin-provided (bootstrap endpoint, first 20) and user-uploaded skills [C] |
| Plugins (e.g. Claude for Legal) | Not documented; only "skills" are named [C]. Needs a live test | Not available (no Claude account) [I] |
| Connectors | Org connectors | Only remote MCP servers the admin configures; called from the user's machine, CORS for `https://pivot.claude.ai` [C] |
| Code execution | Not documented for Word | Foundry direct (and gateways to Foundry or Anthropic API) only; not Bedrock or Vertex; uploaded files not available to code execution [S] |
| Web search | — | Vertex, Foundry, some gateways; not Bedrock [S] |
| Retention | Inputs/outputs deleted within 30 days; org retention not inherited; Compliance API covers it [C] | Per the provider's terms; Foundry: Anthropic is a processor for Microsoft; usage metadata and safety-flagged content only [S] |

## Azure AI Foundry specifics

- The admin wizard captures `azure_resource_name` and `azure_api_key` and pre-fills them in the manifest; otherwise
  each user types them (the screenshot) [C].
- **Keyless option:** per-user Entra tokens with `gateway_auth_source=entra`; "No key is stored on the device and no
  key is embedded in the manifest" [C]. Prefer this over a shared API key.
- Deployment names must match default model IDs; custom names not yet supported [secondary, Japanese support page].
- Config precedence: bootstrap endpoint › Entra ID extension attributes › manifest parameters [C].
- The **bootstrap endpoint** (`/claude-for-msft-365-install:bootstrap`) returns per-user MCP servers, skills (HTTPS
  URLs or inline content, ≤ 20) and config; the `X-Claude-User-Agent` header (`claude-word`, …) allows per-app lists [C].
- Admins can block user skill uploads (`disabled_features: skills.authoring`) without affecting provisioned skills [C].
- Your own audit trail: `otlp_endpoint` to an OpenTelemetry collector [C].

## Relevance to Spaarke [I]

- Foundry mode puts Claude-in-Word inside the customer's Azure contract with no Claude seats, billed per token, which
  suits Spaarke customers already on Azure. It needs an admin step, so it is not the individual "email a zip" path.
- In Foundry mode the Spaarke skills reach Word by user upload (v1, from the zip) or from a bootstrap endpoint (v2: the
  BFF could serve Spaarke skills plus the Spaarke MCP connector per user). Claude for Legal's plugins don't come along;
  only its Apache-2.0 skill files could be uploaded as skills.
- r1's Python engines: only possibly on Foundry direct, and not over uploaded files [S]. Keep Word skills
  instruction-only.

## Live tests to add

1. Log-in mode: do skills from an uploaded plugin (Spaarke, Claude for Legal) appear under `/` in Word?
2. Foundry mode: upload a Spaarke skill zip in the add-in; does it run? Does code execution run on the open document?
3. Foundry mode with `gateway_auth_source=entra`: works with a Spaarke dev Foundry deployment?
