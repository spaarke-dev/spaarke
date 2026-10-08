# Deploy log (spaarke-ontology-platform-r1)

## Task 111 - WizardShell consumer regression and deploy (2026-10-08)

Environment: **spaarkedev1** (https://spaarkedev1.crm.dynamics.com) only. Owner approval D-71 (2026-10-08). BFF and other environments untouched.
Build base: PR #1415 `feat/wizard-consumers-111`, head `be5ee3bf1`, 0 commits behind origin/master `23c359eaf` at deploy time.

### Deploys (all UTC, 2026-10-08)

| Surface | Method | Time | Result |
|---|---|---|---|
| sprk_spaarkeai (Console, incl. CreateAnalysisWizardWidget) | scripts/Deploy-SpaarkeAi.ps1 | 13:43 | Updated + published. Content byte-identical to dist/spaarkeai.html (5,910,332 B). Prior dev copy modified 2026-10-06 05:05Z |
| sprk_smarttodo | scripts/Deploy-SmartTodo.ps1 | 13:43 | Updated + published. Byte-identical (1,373,682 B). Prior: 2026-10-06 13:15Z |
| sprk_documentrelationshipviewer | scripts/Deploy-WebResourceInline.ps1 | 13:43 | Updated + published. Byte-identical (1,279,517 B). Prior: 2026-05-19 |
| External SPA (Power Pages `sprk-external-workspace`) | - | - | **NOT DEPLOYED** - see deviations |
| SemanticSearchControl PCF 1.1.82 (solution SpaarkeSemanticSearch) | build:prod via Invoke-PcfBuildProd.ps1; pack.ps1; `pac solution import --publish-changes` | 13:45 | Imported; dev solution now 1.1.82 (was 1.1.80). Note: `--publish-changes` publishes all customizations (PublishAll) |

PCF bundle: master source (1.1.81) 773,714 B -> this branch (1.1.82) 773,660 B (-54 B). Committed Solution bundle before: 770,149 B. Escalation trigger (growth) not hit.

### Regression rows (modal note 5.2) - live, light and dark, 100% and large

**NOT EXECUTED.** The session has no browser / UI automation tool (no Chrome or built-in browser MCP), and Dataverse SSO cannot be scripted. No row below has been verified live. Rows need a human (or a session with browser tooling).

| # | Row | Live result |
|---|---|---|
| 1 | CreateAnalysisWizardWidget (canary), SpaarkeAi | NOT VERIFIED live. Deployed + byte-verified; jest (CreateAnalysisWizardWidget 11/11) green |
| 2 | SmartTodo CreateTodoWizard | NOT VERIFIED live. Deployed + byte-verified |
| 3 | DocumentRelationshipViewer DocumentEmailWizard | NOT VERIFIED live. Deployed + byte-verified |
| 4 | external-spa DocumentUploadPage | NOT DEPLOYED, NOT VERIFIED |
| 5 | SemanticSearchControl PCF (preview -> Email stacked; bulk Email) | NOT VERIFIED live. Imported 1.1.82 |
| - | 8 embedded navigateTo wizard smoke opens | NOT VERIFIED live (embedded markup is locked by the characterization snapshot test, class hashes only changed) |

Dark-mode (ADR-021) check and Escape/close/finish checks: not executed live. Covered only by unit tests (explicit dismiss, snapshot).

### Expected user-visible changes to check when verifying
- Create Analysis wizard: 62vw x min(74vh,760px) (was 60vw x 70vh).
- SemanticSearch Email wizard: `lg` = min(1280px,94vw) x min(85vh,880px); height now capped at 880px on tall viewports.
- Wizard stepper hairline/ring widths now use strokeWidthThin/Thick tokens (visually same at 100%, scale with UI scale).
