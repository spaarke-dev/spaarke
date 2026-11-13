# BFF split re-assessment in light of the MCP server (2026-11-06)

> Independent, read-only assessment. Not a project decision; changes nothing in `spaarkeai-mcp-bff-api-r1`.
> Evidence at `spike/spaarkeai-mcp-phase0` @ `2cb47f6b4f`.

## Question

Does adding the MCP server (`/mcp/{area}`, design §7, §14) to `Sprk.Bff.Api` trigger the deferred decision to split the BFF into more than one deployable?

## Prior decision (quoted, with source paths)

**Assessment, 2026-05-20** (`docs/assessments/bff-ai-extraction-assessment-2026-05-20.md`, header: "AI assessment", status "Findings for owner review"):

> "**Recommendation: Defer full extraction with re-assessment after Insights Engine Phase 1 lands (≈3–6 months).**" (§Summary)

> "**Set a re-assessment date** — 2026-11-20 … Re-run this exact prompt; compare findings." (§8) and "Re-assessment date (2026-11-20) is unconditional" (§9)

**Owner acceptance**: `projects/sdap-bff-api-remediation-fix/design.md` §2.2 "**Conclusion: keep AI in the BFF.**"; line 172 "**2026-05-24: ACK'd**". ADR-013's change log credits "Spaarke Engineering" (`docs/adr/ADR-013-ai-architecture.md` line 687); no individual owner is named.

**Binding rule** (`.claude/adr/ADR-013-ai-architecture.md`, Decision): "Default: extend `Sprk.Bff.Api` … in-process." A separate deployable is permitted only "when ALL of the following hold". "MUST NOT create a separate AI microservice without documented evidence that all four exception criteria are met AND a successor ADR amends this one."

**About MCP specifically**: "An MCP server (e.g., `Sprk.Insights.Mcp`) … DESIGN-TIME consideration, not pre-decided" (ADR-013 concise). `.claude/constraints/bff-extensions.md` line 354: "thin facade exposing capabilities to EXTERNAL consumers (e.g., MCP for M365 Copilot) | (consider) | (consider — needs successor ADR)".

**Inconsistency to note**: `projects/sdap-bff-api-remediation-fix/spec.md` line 240 records "Open-ended reassessment, no calendar deadline". That contradicts the assessment's unconditional 2026-11-20 date.

## Re-evaluation criteria

1. **ADR-013's four criteria**, all required: no latency coupling; no transactional coupling with BFF session, safety or audit state; a bounded surface; no duplication. In its decision table, three or four "Separate" answers lead to a successor ADR.
2. **The assessment's flip conditions** (§8). It flips to "MCP-only extraction now" if `Sprk.Insights.Mcp` ships, p95 degrades by 30% or more, Engine compute reaches 40% or more of BFF spend, or "a new external surface needs Engine access without the full BFF". Its trigger 5: "MCP server … has zero or minimal CRUD-code dependencies".
3. **Insights §22.2** (`docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md` line 2114 ff.), including "The MCP server (§15) graduates to its own deployment and the BFF-co-hosted shape becomes architecturally awkward."
4. **ADR-052 signals**: F1–F5 favour moving out; B1–B4 favour the BFF.
5. **Publish thresholds** (ADR-029; CLAUDE.md §10): a review at 55 MB, a hard stop at 60 MB.

## Current facts

**Size.** These are raw grep counts over `src/server/api/Sprk.Bff.Api`, so they are not comparable with the 2026-05-20 method:

| Measure | Count |
|---|---|
| `*Endpoints.cs` files | 134 |
| `Map…Endpoints` methods | 145 |
| `Map{Verb}` call sites | 532 |
| `*Module.cs` files | 40 |
| `Add…Module` methods | 35 |
| `Add/TryAdd{Lifetime}` call sites | 782 |
| Distinct `AddHostedService<>` | 18 |
| `IJobHandler` files | 22 |
| `.cs` files | 1,875 |
| `Services/Ai` share of `Services/` (raw lines) | 171k of 423k |

The summary in `.claude/constraints/bff-extensions.md` line 13 ("~120 endpoints, ~244 DI registrations … ~16 hosted services") is stale. Code wins.

**Publish size.** Fresh master build: 39.232 MB. Spike delta: +0.703 MB. The 734 KiB widget is included in both. Source: `notes/spikes/phase0-spike-measurements.md` §6. The last recorded baseline was 45.42 MB on 2026-09-02 (`.claude/constraints/azure-deployment.md` line 101). Master is about 15 MB below the review line.

**Clients.** PCFs, Code Pages, the External SPA, the Office Add-ins, the M365 Copilot plugin and Dataverse plugins (`bff-extensions.md` line 13). The existing Copilot gateway `/api/agent/*` already lives in the BFF as a delegating front door (`docs/architecture/M365-COPILOT-INTEGRATION-ARCHITECTURE.md` line 96).

**Existing deployables.** `Sprk.Bff.Api`, plus `Sprk.Provisioning.ControlPlane.Api` and `.Worker` (`src/server/services/`). The shared libraries are `Spaarke.Core`, `Spaarke.Dataverse` and `Spaarke.Scheduling`. No Azure Functions project exists under `src/` (no `host.json`).

**What the MCP tools depend on.**
- The `DecisionCommitService` constructor takes 15 dependencies, including the Dataverse writer, the decision-record writer, the `ITenantCache` ledger, the job lease and the plan-read stamp (`Services/Signals/DecisionCommitService.cs:335-350`).
- `Services/Signals/**` imports Dataverse, Access, Finance, ExternalAccess, Workspace, Jobs and `Ai.PublicContracts` (grep of `using` lines).
- The tools inject no AI-internal types (`spec.md` line 297).

**Isolation already in the design** (design §7, §11): stateless mode, `RequireAuthorization`, a rate-limit partition `"mcp"`, a request size limit and a kill switch, `Mcp:Enabled`.

**Insights MCP status.** `Sprk.Insights.Mcp` is not built. Insights Phase 2 (r3) is "⏸ PAUSED" (`projects/ai-spaarke-insights-engine-r3/README.md` line 5). Phase 1.5 (r2) shipped on 2026-06-04.

## Assessment per criterion

| Criterion | Applied to the MCP server | Result |
|---|---|---|
| ADR-013 (1): latency coupling | Request/response tools under 30 s; no streaming TTFB budget (design §11) | Met |
| ADR-013 (2): transactional coupling | `spaarke_get_decision_plan` writes the plan-read stamp; `spaarke_complete_decision` writes through `DecisionCommitService` (Redis idempotency ledger, Decision Record) in the same request | **Not met** |
| ADR-013 (3): bounded surface | Four tools with closed schemas (design §7.1) | Met |
| ADR-013 (4): no duplication | A second host "would fork" caller resolution and result mapping (design §8; plan.md line 57) | **Not met** |
| ADR-013 decision table | At most 2 of 5 answers point to "Separate" | BFF |
| Flip: `Sprk.Insights.Mcp` ships | Not built; Insights r3 paused | Not fired |
| Flip: trigger 5, minimal CRUD dependencies | This server is CRUD- and invariant-heavy (see Current facts) | Not met: this server is not the "natural first extraction unit" the assessment had in mind |
| Flip: new external surface needs the Engine without the full BFF | Copilot needs the Signals capability *with* BFF-only services, not the Engine | Not fired |
| Flip: p95 ≥ 30%; compute ≥ 40% | Not measured in this read-only pass; v1 is user-initiated and low-volume | No evidence |
| ADR-052 | B1 (caller OBO), B2 (heavy use of BFF domain code), B3 (low volume). F1–F5 not material | BFF |
| Publish size | About 39.9 MB with MCP; widget ceiling 3 MB | Far below 55 MB |

**Does the single deployable cause any concrete failure for MCP?**

- **Latency and isolation:** no failure. In-process avoids a second hop and a second token exchange (design §7.2). The rate-limit partition and the kill switch contain a misbehaving client.
- **Scaling:** no failure. There is no evidence of independent scaling needs. Long work stays job-based (ADR-004).
- **Blast radius:** one real cost. The SDK moves `Microsoft.Extensions.AI` from 10.3.0 to 10.11 for the whole process (design §14). This is mitigated by running the AI suites (design §20). An MCP-only host would avoid the bump, but nothing else would improve.
- **Deploy coupling:** real friction, but not caused by MCP. A separate App Service for the spike (option b) was rejected because "that copy would run the whole BFF, including its scheduled reconcilers and Service Bus consumers" (`notes/decisions.md` line 63). Grep found no host-wide switch that disables background work. That gap belongs to the modular monolith. An MCP host would still depend on the B-1 to B-5 changes deployed to the same dev BFF.
- **Auth surface:** in-BFF is smaller. A split adds a second resource, a second audience and an OBO hop to the BFF.
- **Team contention:** the contention is on files: `AuthorizationModule.cs` and `Services/Signals/**` (design §20). It survives any split, because Signals stays in the BFF. Author overlap was "HIGH" (assessment §8, reason 4).

**Cost of the smallest split.** The smallest split is an MCP-only host that calls the BFF. It needs a deployable per stamp (ADR-052 cost list), a new app registration and audience, an OBO exchange to the BFF on every call, tools rewritten as HTTP clients, and duplicated result mapping. The invariants stay in the BFF, so it isolates nothing that matters. An analogous estimate for re-hosting is 1–2 weeks (`projects/agent-framework-fit-assessment-r1/notes/05-deployment-and-migration.md` line 387). Extracting the AI services is irrelevant here, because the tools touch no AI internals.

## Recommendation

**No split now.** MCP meets neither ADR-013's all-four test nor any of the assessment's flip conditions. The project's B-1/B-2 work (one capability with REST and MCP as thin adapters, `AddMcpModule`) is already the right modular-monolith hardening for this seam.

Optional follow-ups, all outside this project:
1. Run the full structural re-assessment on 2026-11-20 as the assessment scheduled, with App Insights and cost data. This note covers MCP only and does not substitute for it.
2. Correct `bff-extensions.md` line 13 and the "no calendar deadline" contradiction in the remediation spec.
3. Consider a host-role switch, so a second BFF instance can run web-only. This is the cheapest fix for the dev-deploy friction.

## What would change it

- **A second MCP area whose tools need no BFF-only services**, for example a read-only Insights area heavy on AI and light on CRUD. Re-run ADR-013 for that area; it could be the first legitimate separate deployable.
- **MCP traffic** from model-driven tool loops that raises p95 on non-MCP routes by 30% or more, or needs independent scale-out.
- **A second team** owning the Copilot/MCP surface on its own release cadence.
- **Repeated SDK-driven bumps** of shared AI packages that destabilise the AI pipeline.
- **A customer or SLA requirement** to host or isolate the Copilot surface separately.
- **Publish size** nearing 55 MB.

## Effect on the MCP project

None. **Nothing in the MCP plan needs to change.** The Path C placement (design §14; spec line 402) agrees with this assessment.

One optional wording refinement, with no effect on tasks: risk R9 (plan.md line 388; design §20) could read "default to a new `/mcp/{area}` here; re-run the ADR-013 criteria for an area that needs no BFF-only services".

## Sources

Every path is cited inline above. The primary sources are `docs/assessments/bff-ai-extraction-assessment-2026-05-20.md`, `.claude/adr/ADR-013-ai-architecture.md` with `docs/adr/ADR-013-ai-architecture.md`, ADR-029, ADR-052, `.claude/constraints/bff-extensions.md`, `.claude/constraints/azure-deployment.md`, `docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md` §22.2, and this project's `design.md`, `plan.md`, `notes/decisions.md` and `notes/spikes/`.
