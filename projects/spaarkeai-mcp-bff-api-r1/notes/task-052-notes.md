# Task 052 notes: MCP architecture doc, add-a-tool guide, connector runbook

Written 2026-11-24. Nothing committed; the main session reviews.

## Written

| File | Change |
|---|---|
| `docs/guides/SPAARKE-COPILOT-CONNECTOR-RUNBOOK.md` | New. 042 step 8: the 002 draft finalised against `notes/phase0-registration-facts.md`, `notes/decisions.md`, `notes/phase0-results.md`. Confidential client, Web redirect, secret handling and rotation, Developer Portal field values, connector, plugin package build and upload, user prerequisites, verification, kill switch and rollback, troubleshooting. Public-client/PKCE-only steps dropped. |
| `docs/architecture/SPAARKE-MCP-SERVER-ARCHITECTURE.md` | New. Every claim tied to a file confirmed to exist. |
| `docs/guides/HOW-TO-ADD-AN-MCP-TOOL.md` | New. Add a tool, add an area, tests for each. |
| `docs/INDEX.md`, `docs/architecture/INDEX.md`, `docs/guides/INDEX.md` | One row each (the two sub-indexes because docs-architecture and docs-guide require it). |
| `docs/guides/M365-COPILOT-DEPLOYMENT-GUIDE.md` | L35 only: "the Spaarke Copilot agent" became "the Spaarke AI agent (the declarative agent for Microsoft 365 Copilot)". |

## doc-drift result

Checked the three new docs: every backticked file path, directory and relative markdown link resolved against the worktree, plus the named test methods, classes and constants (grep). No stale reference. The only checker misses were files that are not yet tracked by git (the new docs link each other), build outputs (`dist/index.html`, the embedded resource name) and the `{Area}/{Group}` placeholder path. M365 guide: one-line diff, git reports only the CRLF normalisation warning.

## L3 and L102 of M365-COPILOT-DEPLOYMENT-GUIDE.md (decision for the main session)

- **L102** (`az ad app create --display-name "Spaarke Copilot Bot Dev"`) is unchanged: it is the display name of an existing app registration.
- **L3 is also unchanged, a deviation from the POML.** It reads: the secret-free "Spaarke Copilot Agent" client. That is the display name of an existing Entra client (dev: "Spaarke Copilot Agent - Dev", `3a36eac4-…`, in `SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` L2257-2273 and `.claude/skills/provision-environment/SKILL.md`), the same string appears in the same banner in `DECLARATIVE-AGENT-BUILD-AND-DEPLOY-GUIDE.md` and `M365-COPILOT-ADMIN-GUIDE.md`. Renaming it in one guide would make that guide name an app that does not exist. If the owner wants it changed, rename the Entra app and all four docs together.
- The Entra app "Spaarke Copilot Connector – dev" is likewise a registration name (naming note §1) and appears in the runbook as such, not as a product name.

## Code versus design disagreements

1. **RFC 9728 challenge.** Spec FR-02 says the 401 challenge points to protected-resource metadata. The code sends plain `Bearer` (`invalid_token` on A10 refusals); already recorded in `notes/decisions.md` (task 021). Documented as the code does it.
2. **`PerSessionExecutionContext`.** Design and `CLAUDE.md` §2 say task 010 pins it to protect `IHttpContextAccessor`; the SDK reads it only in the stateful branch, so it is inert on `/mcp/*` (decisions.md). The architecture doc says statelessness is what keeps the accessor populated.
3. **Connector name and secret lifetime.** Naming note says connector `Spaarke`; owner renamed it `Spaarke MCP` (2026-11-10). A10 says secret expiry at most 180 days; dev uses 24 months by Path A (2026-11-08). Documented as built; customer stamps keep 180 days.
4. **Plugin package.** The 040 POML planned `agentConnectors`; owner decision 2026-11-23 made it skills-only, one registration. Documented as built.
5. **ADR-028 A10 prefers a public client with PKCE.** Built as a confidential client (Developer Portal requires a secret). Runbook states why.

## Suspected defect (for the owner to judge, not changed)

`spaarke_get_decision_plan` is annotated `readOnlyHint = true`, but its description and `GetDecisionPlanTool.cs` say it "records that the user has read the plan" (the plan-read stamp, a write; the route-guard ledger entry also says so). Copilot may therefore treat it as a pure read. It is app-only, so the model never calls it, and the stamp is idempotent in intent, but the hint is not literally true. The startup rule (non-read-only tools must be app-only) would still pass. Flagged only; architecture doc lists it as read-only because that is what the code declares.

## Not verified

- No second admin has followed the runbook from scratch (042 step 8 asks for this). The runbook says so in its status line.
- Whether editing the client secret in an existing Developer Portal registration keeps the registration ID; the runbook marks this as unverified and gives a fallback.
- Admin center menu paths (Copilot › Connectors, Agents › Tools › Registry) are as recorded in the Phase 0 notes, not re-checked in the live UI.
- No live call was made; the `tools/list` check in the runbook rests on the 041 record and the CI test.
- TASK-INDEX.md and the POML status were not touched (the main session applies those).

## Main-session review (2026-11-25)
- Reviewed: no secret value, token or user email in the runbook (key ID and dates only; app and tenant ids were already in the repo).
- L3 left unchanged (accepted): "Spaarke Copilot Agent" there is the display name of an existing Entra app, like L102; renaming it is a separate decision across several guides and the provision-environment skill.
- "`spaarke_get_decision_plan` readOnlyHint=true but stamps the plan read": not a defect. `DecisionPlanReadStamp` is a best-effort tenant-cache entry with a 4-hour TTL (server bookkeeping for the commit's review-start time); the tool changes no Dataverse record or anything the user sees. Known limit, nothing filed.
- Deviation: ran before 042 closed (owner: test in parallel and continue). If 042's live results change a fact stated here (e.g. NFR-01), update the architecture doc at 042 close.
