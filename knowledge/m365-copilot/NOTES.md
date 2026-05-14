> ⚠️ STUB — senior engineer review pending

Stub headings derived from the directive's "NOTES.md guidance" bullets for the `m365-copilot` topic. Each section below is a placeholder for the substantive annotation pass; the curated samples in this directory are the inputs the reviewer should read first.

## Declarative agent manifest structure

_TODO: walk through key fields (`$schema`, `version`, `name`, `description`, `instructions`, `capabilities[]`, `actions[]`, `conversation_starters[]`) using `declarative-agent-basic/` → `declarative-agent-onedrive-sharepoint/` → `declarative-agent-api-plugin/` as a progressive complexity ladder; call out which schema version Spaarke targets._

## Knowledge sources

_TODO: enumerate the source types declarative agents support (SharePoint, OneDrive, embedded files, Copilot connectors, web, meetings, Teams messages, people, Dataverse, MCP) per `docs/whats-new.md` history; note which are GA vs. preview as of refresh date; map to Spaarke's three intended sources (SPE via SharePoint knowledge source, Dataverse MCP, Foundry IQ KB)._

## Pointing SharePoint knowledge source at SPE

_TODO: document the `OneDriveAndSharePoint.items_by_url` pattern from `declarative-agent-onedrive-sharepoint/appPackage/declarativeAgent.json` and explain how Spaarke targets an SPE container's webUrl; cross-reference `knowledge/sharepoint-embedded/` once that topic is curated; note the page-size and file-count limits from `docs/optimize-content-retrieval.md`._

## Admin approval flow

_TODO: capture the Agent Builder publish → tenant admin approval → user availability flow; describe what changes trigger re-approval (manifest version bump, new capabilities, new actions, auth changes); note the Package Management API additions from `docs/whats-new.md` (April–May 2026)._

## API plugin / action wiring

_TODO: explain the `declarativeAgent.json` → `ai-plugin.json` → OpenAPI spec chain visible in `declarative-agent-api-plugin/`; document auth modes (`OAuthPluginVault`, `ApiKeyPluginVault`, `None`); call out the `OpenApi` vs `LocalPlugin` vs MCP-server runtime types and which Spaarke uses; reference the TypeSpec-first approach demonstrated in `copilot-camp-path-e/RepairServiceAgent/src/agent/actions/actions.tsp`._

## Where Spaarke diverges from generic samples

_TODO: describe Spaarke-specific shape — matter context binding, Dataverse MCP wiring for case/playbook lookup, custom Spaarke MCP server bindings (redline, compare, citation-check tools), the three-knowledge-source composition (SPE substrate + Dataverse + Foundry IQ KB); reference ADR-013 (AI Architecture) and the AI Tool Framework; note where the generic Path E lab patterns must be adapted._

## Tooling notes

_TODO: Agents Toolkit (`m365agents.yml`) vs. Teams Toolkit (`teamsapp.yml`) — both appear in the curated samples; capture current naming/positioning and which one we standardize on for Spaarke's declarative agent CI pipeline._

## Schema version drift

_TODO: the curated samples span schemas v1.2 → v1.6. Note the v1.7 May 2026 release (`docs/whats-new.md`) and the `editorial_answers` + `default_response_mode` + `depends_on` additions; pin Spaarke's target schema version and what triggers an upgrade._
