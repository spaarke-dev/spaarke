> ⚠️ STUB — senior engineer review pending

# NOTES — m365-copilot

Project-specific commentary on m365-copilot. Annotate from real Spaarke project experience; don't fabricate. Section structure:

- **§1. How this fits Spaarke's architecture** — when to reach for this, role/composition with other surfaces, what it replaces or composes with, preview/cost/licensing implications, decision criteria
- **§2. How we build with it** — manifest/code shape, auth wiring, gotchas, Spaarke divergence from canonical samples, code review checklist

Both sections required for "done"; honest TODOs are fine for what isn't yet known. When annotating, remove the `⚠️ STUB` banner above only after both §1 and §2 have substantive content (or honest TODOs).

Stub headings derived from the directive's "NOTES.md guidance" bullets for the `m365-copilot` topic. Each section below is a placeholder for the substantive annotation pass; the curated samples in this directory are the inputs the reviewer should read first.

---

## 1. How this fits Spaarke's architecture

## Where Spaarke diverges from generic samples

_TODO: describe Spaarke-specific shape — matter context binding, Dataverse MCP wiring for case/playbook lookup, custom Spaarke MCP server bindings (redline, compare, citation-check tools), the three-knowledge-source composition (SPE substrate + Dataverse + Foundry IQ KB); reference ADR-013 (AI Architecture) and the AI Tool Framework; note where the generic Path E lab patterns must be adapted._

## Admin approval flow (process/governance)

_TODO: capture the Agent Builder publish → tenant admin approval → user availability flow; describe what changes trigger re-approval (manifest version bump, new capabilities, new actions, auth changes); note the Package Management API additions from `docs/whats-new.md` (April–May 2026)._

## Knowledge sources (taxonomy/applicability)

_TODO: enumerate the source types declarative agents support (SharePoint, OneDrive, embedded files, Copilot connectors, web, meetings, Teams messages, people, Dataverse, MCP) per `docs/whats-new.md` history; note which are GA vs. preview as of refresh date; map to Spaarke's three intended sources (SPE via SharePoint knowledge source, Dataverse MCP, Foundry IQ KB)._

## Schema version pin (architectural decision)

_TODO: Spaarke pins a target declarative-agent schema version as an architectural decision — capture which version we target (currently spanning v1.2 → v1.6 in curated samples; v1.7 released May 2026 per `docs/whats-new.md`) and the upgrade governance. The "what changes when you bump" implementation details live in §2 under "Schema version drift"._

---

## 2. How we build with it

## Declarative agent manifest structure

_TODO: walk through key fields (`$schema`, `version`, `name`, `description`, `instructions`, `capabilities[]`, `actions[]`, `conversation_starters[]`) using `declarative-agent-basic/` → `declarative-agent-onedrive-sharepoint/` → `declarative-agent-api-plugin/` as a progressive complexity ladder; call out which schema version Spaarke targets._

## Pointing SharePoint knowledge source at SPE

_TODO: document the `OneDriveAndSharePoint.items_by_url` pattern from `declarative-agent-onedrive-sharepoint/appPackage/declarativeAgent.json` and explain how Spaarke targets an SPE container's webUrl; cross-reference `knowledge/sharepoint-embedded/` once that topic is curated; note the page-size and file-count limits from `docs/optimize-content-retrieval.md`._

## API plugin / action wiring

_TODO: explain the `declarativeAgent.json` → `ai-plugin.json` → OpenAPI spec chain visible in `declarative-agent-api-plugin/`; document auth modes (`OAuthPluginVault`, `ApiKeyPluginVault`, `None`); call out the `OpenApi` vs `LocalPlugin` vs MCP-server runtime types and which Spaarke uses; reference the TypeSpec-first approach demonstrated in `copilot-camp-path-e/RepairServiceAgent/src/agent/actions/actions.tsp`._

## Tooling notes

_TODO: Agents Toolkit (`m365agents.yml`) vs. Teams Toolkit (`teamsapp.yml`) — both appear in the curated samples; capture current naming/positioning and which one we standardize on for Spaarke's declarative agent CI pipeline._

## Schema version drift

_TODO: the curated samples span schemas v1.2 → v1.6. Note the v1.7 May 2026 release (`docs/whats-new.md`) and the `editorial_answers` + `default_response_mode` + `depends_on` additions; pin Spaarke's target schema version and what triggers an upgrade._
