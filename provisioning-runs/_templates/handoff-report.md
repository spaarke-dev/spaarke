# Handoff Report — {customerId}-{runId}

> **Created by**: `customer-provisioning-orchestration-r1` task 203a per punch list row A06 (template).
> Written by `/provision-environment` skill Step 6 (Completion Handoff). Supersedes the free-floating `runs/{runId}.md` file the pre-r1 flow produced.
> This is the final operator-facing artifact. Includes everything a downstream operator or customer-success handoff needs.

## Executive summary

- **Customer**: {displayName} (`{customerId}`)
- **Tenancy model**: `{tenancyModel}`
- **Profile**: `{profile}`
- **Region**: `{region}`
- **Started**: `{startTime}` by `{operatorUpn}`
- **Completed**: `{endTime}` — total elapsed: `{HH:MM}`
- **Final status**: `{Ready | Failed | Partial-with-follow-up}`
- **SC #5 achieved**: `{Yes | No}` (env reached `Setup Status = Ready` end-to-end per FR-18)

## Resources provisioned

| Resource type | Count | Notes |
|---|---|---|
| Dataverse environment | 1 | `{dataverse-url}` |
| Azure resource group | `{N}` | `{rg-list}` |
| Bicep-provisioned Azure resources | `{M}` | per stack `{stack-name}` |
| KV secrets (shared + per-tenant) | `{P}` | per manifest `{manifest-path}` |
| App Service app-settings | `{Q}` | per per_env_settings manifest |
| AI Search indexes | `{7}` | 7 canonical |
| Solutions imported | `{8}` | per §11.1a dep-order |
| SPE container | 1 | container-type `{containerTypeId}` |
| Graph app permissions | `{R}` | admin-consent granted at Gate {n} |
| Optional users provisioned | `{U}` | at H11 gate |

## Endpoints (for consumer teams)

| Consumer | Endpoint | Auth |
|---|---|---|
| Model-Driven App (customer-facing) | `{mda-url}` | Dataverse SSO |
| BFF API | `{bff-url}` | `Bearer {aad-token}` — audience per H3 |
| SharePoint Embedded workspace | `{spe-workspace-url}` | via BFF |
| OpenAI (routed via BFF) | `{bff-url}/api/ai/*` | via BFF |

## KV secret ledger (per BINDING never-delete list)

- `BFF-API-ClientSecret` present ✓ (age: {days})
- `Dataverse-ClientSecret` present ✓ (age: {days})
- `Sidecar-Shared-Secret` present ✓ (age: {days})
- ...other secrets from the canonical secret catalog...

## Manual gates encountered

Cross-ref [`manual-gates.md`](manual-gates.md). Summary:

| gate | opened | closed | outcome |
|---|---|---|---|
| H0.5 admin-consent | `{ts}` | `{ts}` | `Success` |
| H8 SPE-container-type | `{ts}` | `{ts}` | `Success` |
| ... |

## Any failures / escalations

Full detail: [`handler-log.md`](handler-log.md) + [`manual-gates.md`](manual-gates.md). Summary:

- {Handler + one-line failure summary} — resolved via {mechanism}, cross-ref {file:line}
- OR: "No failures; nominal flow."

## Follow-up actions for downstream teams

- **Customer success**: {any first-run tasks for the customer — e.g., seed initial matter, invite second admin}
- **Ops**: {any monitoring alerts to enable; runbook links}
- **Engineering**: {any code follow-ons filed as GitHub issues — link}

## Registry entry (added to `provisioning-runs/INDEX.md`)

```markdown
| {runId} | {customerId} | {tenancyModel} | {profile} | {startTime} | {endTime} | {status} | {lessons-count} | {Y|N} | [folder]({customerId}-{runId}/) |
```

## `sprk_dataverseenvironment` registry update

- `sprk_setupstatus` → `Ready` (per H10)
- `sprk_currentrunid` → `{runId}` (immutable per ADR-044)
- `sprk_bffversion` → `{git-sha}` (from H9)
- `sprk_solutionversion` → `{version}` (from H6)

## Sign-off

- Skill completion timestamp: `{ts}`
- Operator: `{operatorUpn}`
- Cosmos run-doc ID: `{cosmos-doc-id}` (retained for audit)
- Postmortem obligation: [`lessons-learned.md`](lessons-learned.md) MUST be written before folder is committed (Skill Step 7 gate).
