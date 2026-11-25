# Deferred issues (spaarkeai-mcp-bff-api-r2)

## From task 002 (2026-11-25)

### D-002-1: Coordination: retire T257 per-customer Copilot agent plumbing in customer-provisioning-orchestration-r1

- **GitHub issue**: https://github.com/spaarke-dev/spaarke/issues/3173 (filed by the main session 2026-11-25)
- **Title**: `customer-provisioning-orchestration-r1: remove the retired "Spaarke AI" Copilot agent (T257) plumbing`
- **Body**:
  The legacy "Spaarke AI" declarative agent was retired on 2026-11-25 by `spaarkeai-mcp-bff-api-r2` (task 002 deleted `src/solutions/CopilotAgent/`, `publish-copilot-agent-template.yml`, `scripts/copilot-agent/`, `scripts/Deploy-CopilotAgent.ps1`, `infrastructure/bot-service/`). Task 002 deliberately did not touch the T257 files owned by this project. They now reference deleted things:
  - `infrastructure/bicep/platform-controlplane.bicep` (:195, :507), `modules/controlplane-worker-app-service.bicep` (:208, :234), `parameters/platform-controlplane-dev.bicepparam` (:125), compiled `platform-controlplane.json`: `copilotAgentClientAppId` / the shared Spaarke Copilot Agent app pre-authorization.
  - `scripts/provisioning-prereqs/prereqs.yaml` :231 (pointer to t257-copilot-agent-design.md).
  - `scripts/Extend-DataverseEnvironmentSchema-v3.3.ps1` :45, :186 and `SpaarkeMaster/Entities/sprk_dataverseenvironment/Entity.xml`: the Copilot OAuth registration id column and its description (names the deleted render script).
  - `.claude/skills/provision-environment/SKILL.md` post-Ready Copilot gate (~:2546) will fail at the render step (task 003 of the r2 project edits `.claude/`).
  - `docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` section 7.12 (task 004 of the r2 project).
  Decide: remove the column and bicep param (needs a Dataverse schema change and a control-plane redeploy), or keep them dormant. Tenant resources (Registry rows, Azure Bot, Entra app) are the owner's runbook E1/E2.
- **Labels**: project label for customer-provisioning-orchestration-r1; Type=Task.

## From task 005 (2026-11-25)

### D-005-1: Runs left "Running" after a restart or failed final write
- **GitHub issue**: https://github.com/spaarke-dev/spaarke/issues/3178
- Staleness limit after which a stored Running run reads as failed; decide in task 030 / FR-14.

### D-005-2: Retention for sprk_playbookrun / sprk_playbooknoderun
- **GitHub issue**: https://github.com/spaarke-dev/spaarke/issues/3179
- ADR-015; mechanism likely a Dataverse bulk-delete job.
