# Task 002: out-of-scope references to the retired agent (2026-11-25)

Left unedited on purpose; owned by other projects or tasks.

| File | What | Owner |
|---|---|---|
| infrastructure/bicep/platform-controlplane.bicep :195, :507 | `param copilotAgentClientAppId` ("shared Spaarke Copilot Agent app") | customer-provisioning-orchestration-r1 (T257) |
| infrastructure/bicep/modules/controlplane-worker-app-service.bicep :208, :234 | pre-authorized client app id | same |
| infrastructure/bicep/parameters/platform-controlplane-dev.bicepparam :125 | dev app id value | same |
| infrastructure/bicep/platform-controlplane.json | compiled ARM of the above | same |
| scripts/provisioning-prereqs/prereqs.yaml :231 | pointer to t257-copilot-agent-design.md | same |
| scripts/Extend-DataverseEnvironmentSchema-v3.3.ps1 :45, :186 | T257 Dataverse column description mentions `scripts/copilot-agent/Render-CopilotAgentPackage.ps1`; the column is schema | same |
| src/dataverse/solutions/SpaarkeMaster/Entities/sprk_dataverseenvironment/Entity.xml | same column | same |
| docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md :2252-2374, :3166-3168 | section 7.12 runbook; calls the deleted workflow, render script and Deploy-CopilotAgent.ps1; links to the deleted M365-COPILOT-DEPLOYMENT-GUIDE.md and DECLARATIVE-AGENT-BUILD-AND-DEPLOY-GUIDE.md | task 004 |
| .claude/skills/provision-environment/SKILL.md :2546, .claude/skills/ci-cd/SKILL.md :153, declarative-agent skill, adr-check refs, ADR-028:810 | skills and ADR | task 003 |

Escalation check: no workflow, master-deploy or provisioning script invoked the deleted scripts or workflow. The only callers were human runbook sections (above) and the provision-environment skill's post-Ready Copilot gate, which will fail at its render step until tasks 003/004 and the T257 owner remove it. Not a live automated deploy path, so the escalation trigger did not fire.

Tenant resources (Registry rows, Azure Bot, Entra app) are owner runbook E1/E2, pending the owner.
