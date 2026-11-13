# Copilot / Cowork naming and cross-project ownership (2026-11-06)

> **Owner-approved** 2026-11-06 (decisions 1–8 on the naming review). This note is the project's naming
> source of truth until the terms land in `ontology-component-model.md` §3 and CLAUDE.md §1.1 (FR-18).
> Evidence: two read-only sweeps on 2026-11-06, one of master conventions and one of 15 project worktrees
> and all `origin/work/*` branches.

## 1. Naming conventions (approved)

| Thing | Convention | v1 value | Why |
|---|---|---|---|
| Family (docs, marketing) | "Spaarke for Microsoft 365 Copilot" | — | Free on all branches. Check Microsoft trademark rules before any public listing. |
| Copilot connector (one per **product area**) | Display "Spaarke" or "Spaarke {Area}" | **"Spaarke"** (core legal operations) | The unit users Connect and admins create. |
| BFF route (one per connector) | `/mcp/{area-slug}`, kebab-case | **`/mcp/legal-operations`** | "core" is taken: *core record* = matter, project or work assignment (`/api/finance/core-records/…`, `SignalCoreRecordAccess`, ontology §3). |
| Functions within an area | **Tool groups and skills, not routes** | decisions (v1); invoice review, matters later | Each route is a separate connector to create and Connect, so splitting by function multiplies admin and user steps. Functions are organised in code (`Api/Mcp/LegalOperations/Decisions/…`) and exposed through skills. |
| Plugin package (Cowork / Copilot), one per area (a legal practice is an area, e.g. `spaarke-nda`) | slug `spaarke-{area}` (or `spaarke` for core) with an **explicit** display name | slug `spaarke`, display "Spaarke" | Cowork title-cases a slug ("spaarke-nda-mvp" → "Spaarke Nda Mvp"). |
| Skill | `spaarke-{area}-{capability}`, kebab-case; the area is dropped only in the core plugin (agreed with legal-ai-accelerator-r1) | `spaarke-decisions` | Avoids clashes with Microsoft's built-in Legal for Cowork skills, and between two practice plugins (for example, two setup skills). Build gate `^spaarke(-[a-z0-9]+)+$`, at most 64 characters. |
| MCP tool | `spaarke_{verb}_{object}`, snake_case | `spaarke_show_decisions` and three app-only tools | Copilot shares one tool space across vendors. No in-repo precedent: the April OpenAPI used camelCase. This rule is recorded in the `mcp-tool-handler` skill. |
| Widget resource | `ui://spaarke/{widget}.html` | `ui://spaarke/decisions.html` | Matches the `knowledge/mcp-apps` samples. |
| Widget source folder | `src/client/widgets/{skill-name}/` | `src/client/widgets/spaarke-decisions/` | Existing `widget-design` skill convention. |
| Widget size | Aim < 1 MB; hard ceiling 3 MB | — | `widget-design` says "no 1+ MB bundles"; O-16 set 3 MB. Phase 0 measures, and the skill text follows the result. |
| Declarative agent (fallback only) | "Spaarke AI" (existing) | unchanged | The @-mentionable name. |
| Retired term | "Spaarke Copilot" (used for the April agent in `docs/guides/M365-COPILOT-DEPLOYMENT-GUIDE.md:33`) | — | Overlaps the family name. |
| Connector OAuth client (A10) | "Spaarke Copilot Connector – {stamp}" | dev: "Spaarke Copilot Connector – dev" | Distinct from "Spaarke Copilot Agent client" (`copilotAgentClientAppId`), the A8 client and the A9 client. |
| "Connector" | Spaarke meaning stays the inbound Connection Engine (Spaarke Connect, `Sprk.Connect.*`) | — | Say "Copilot connector" in admin text; never "Spaarke Connect". |
| Server code | `Api/Mcp/`, `AddMcpModule`, `MapMcpEndpoints`, `Mcp:Enabled` (default `false`) | — | Matches `Api/Ai`, `AddAiModule`, `SectionName` + `Enabled` options. |
| Capability layer | In `Services/Signals/`, not a new `Services/Decisions/` | — | All decision code already lives there. |
| `ICallerContext` | Carries `Spaarke.Core.Auth.CallerIdentity`; does not duplicate it | — | Avoid a third caller type beside `CallerIdentity` and the dead `ExternalCallerContext`. |

## 2. Cross-project coordination (replies received 2026-11-06)

| Project | Topic | Ask | Status |
|---|---|---|---|
| `spaarke-legal-ai-accelerator-r1` | Plugin and skill naming (OD-20) | **Agreed 2026-11-06, with two changes adopted here.** (A) Skills are `spaarke-{area}-{capability}`; the area is dropped only in the core plugin (`spaarke-decisions`), so two practice plugins never collide on a shared setup skill. (B) One plugin per area: a practice is an area (`spaarke-nda` "Spaarke NDA"), and whichever project starts patents first owns `spaarke-patents`. They add a build gate `^spaarke(-[a-z0-9]+)+$` (≤ 64 characters) and rename at the next plugin build after OD-20; installed today only in the owner's dev tenant (`spaarke-nda-mvp`). "Playbook" was settled by D-209 (2026-11-06, ontology §3 `80b3098373`): one concept in several formats (JPS, legal, contract-review); qualify the format where it matters. | Agreed |
| `spaarke-ontology-platform-r1` / r2 | `Services/Signals/**`, ontology terms | **Agreed 2026-11-06.** B-1, B-2 and B-5 wait for PR #2580 (D-207: `ontology.decision.decided_in_invoice_review` 409, `invoiceReview` on worklist items; held until ebilling task 112); B-3 and B-4 can start now. The B-1 PR states each commit invariant (spec MUST rules). Ontology reviews every PR on `Services/Signals/**`, `Api/Signals/**` and the worklist route. B-4 is auth: uac-r2 reviews, two adversarial passes, and task 170's WhoAmI count is watched. D-207 applies through the extracted capability, with no second check and one MCP test. Ontology wrote the product names into component model **§1.1** (commit `1e9d16bdba`); this project adds the CLAUDE.md §1.1 row citing it. **Owner ruling D-208 (2026-11-06):** "Finding" is the user word for a billing Work Item in billing link-outs. Generic rows stay "Work Item", and it is never a code name (ontology §3, commit `644904d5c3`). | Agreed |
| `ebilling-r1` | EB-D-194; Invoice Review link | **Agreed 2026-11-06.** v1 shows invoice decisions only as links to `sprk_invoicereviewpage` with `invoiceId` and `lineId` (bare lower-case guids). `lineId` isn't read yet; the shared `parseDataParams` ignores it safely, and line focus turns on when EB-D-194 lands (ontology check, 2026-11-06). The rule is two surfaces, one Decision Record, no decision type on both. The reason (UX or audit) is not recorded, so ask the owner. Any Copilot billing slice also needs uac-r2 and ebilling review. "Finding" is deliberate inside Invoice Review (its glossary, D-58). | Agreed; next slice is an owner question |
| `unified-access-control-r2` | `AuthorizationModule.cs`; ADR-028 numbering | **Agreed 2026-11-06.** A10 keeps its number; they have no open AuthorizationModule changes. Auth diffs go to them before merge, checking four things: FallbackPolicy covers `/mcp/*`; a must-fire guard test on an unauthenticated `/mcp/{area}`; the CIAM and A9 refusal happens server-side before any tool runs, and is tested; no app-only fallback on a failed OBO (spec FR-02). | Agreed |
| `spaarke-auth-system-of-record-r1` | Its planned ADR-028 amendment (§12b) | Number it **A11 or higher**: A10 is taken by this project | No live session; recorded here, tell at next contact |
| `agent-framework-fit-assessment` (no worktree) | Proposed separate "Insights Engine MCP server" (`Sprk.Insights.Mcp`) | Any further MCP area is a new `/mcp/{area}` on this host, not a second MCP server | Recorded as a risk (design §20) |
