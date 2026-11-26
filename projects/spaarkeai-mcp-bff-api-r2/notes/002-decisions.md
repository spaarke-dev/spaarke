# Task 002 decisions (2026-11-25)

- Deleted `infrastructure/ai-foundry/agents/bff-function-tools.yaml`: both of its tools called `/api/agent/tools/*`, which task 001 removes. The Foundry README `--functions-file` lines and the `spaarke-legal-agent.yaml` comment were updated with a Retired line.
- Kept `M365-COPILOT-USER-GUIDE.md` and `COPILOT-AGENT-CUSTOMER-IT-ONBOARDING.md` with a Retired banner instead of deleting them: neither is in the POML file list, and the onboarding note is linked from task 004's `SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`. Delete them when task 004 lands.
- Wrote decisions here instead of `notes/decisions.md` to avoid merge conflicts with parallel tasks.
- Dropped the `CopilotAgent:*` rows from `CONFIGURATION-MATRIX.md` (the options are removed with the BFF module in task 001).
