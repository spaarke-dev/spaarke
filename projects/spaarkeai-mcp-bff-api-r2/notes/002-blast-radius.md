# Task 002 blast radius (2026-11-25)

Grep: `api/agent|CopilotAgent|copilot-agent|bot-service|AgentTokenService|Spaarke AI agent`, excluding `src/server`, `tests`, `.claude`, `projects`.

| Path | Class | Action |
|---|---|---|
| src/solutions/CopilotAgent/** | delete | removed |
| .github/workflows/publish-copilot-agent-template.yml | delete | removed (no other workflow referenced a deleted path) |
| scripts/copilot-agent/**, scripts/Deploy-CopilotAgent.ps1, tests/scripts/CopilotAgentPackage.Tests.ps1 | delete | removed |
| infrastructure/bot-service/** | delete | removed |
| infrastructure/ai-foundry/agents/bff-function-tools.yaml | delete | both tools called `/api/agent/tools/*`; README and spaarke-legal-agent.yaml references fixed |
| scripts/master-deploy/build-all-vite-solutions.mjs | edit | comment only |
| docs/guides/M365-COPILOT-ADMIN-GUIDE.md, M365-COPILOT-DEPLOYMENT-GUIDE.md, DECLARATIVE-AGENT-BUILD-AND-DEPLOY-GUIDE.md | delete | agent-only; INDEX rows removed |
| docs/guides/M365-COPILOT-USER-GUIDE.md, COPILOT-AGENT-CUSTOMER-IT-ONBOARDING.md | edit | Retired banner (kept: the onboarding note is linked from task 004's SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md); INDEX rows marked retired |
| docs/guides/COPILOT-KNOWLEDGE-CONFIGURATION-GUIDE.md, CONFIGURATION-MATRIX.md, GITHUB-ACTIONS-AZURE-OIDC-SETUP.md, REPOSITORY-NAVIGATION-GUIDE.md, INDEX.md, SPAARKE-COPILOT-CONNECTOR-RUNBOOK.md | edit | agent references removed or retired line |
| docs/procedures/ci-cd-workflow.md, production-release.md | edit | workflow entries and the Deploy-CopilotAgent step removed |
| docs/architecture/{AI-ARCHITECTURE, ai-architecture-playbook-runtime, client-resources-inventory, sdap-bff-api-patterns, sdap-component-interactions, SPAARKE-AI-SURFACES-AND-PACKAGING}.md | edit | retired lines |
| docs/assessments/** | history | unchanged |
| docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md | out of scope | task 004 |
| infrastructure/bicep/**, scripts/provisioning-prereqs/prereqs.yaml | out of scope | customer-provisioning-orchestration-r1 (T257) |
| scripts/Extend-DataverseEnvironmentSchema-v3.3.ps1 (:45, :186), SpaarkeMaster sprk_dataverseenvironment Entity.xml | out of scope | T257 column description text |
| knowledge/** | not ours | generic Microsoft sample manifests matching "copilotAgents"; unrelated |
