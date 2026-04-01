# Content Accuracy Audit Plan

> **Purpose**: Review remaining docs/architecture/ and docs/guides/ files for substantive accuracy.
> The R1 refactoring addressed structure (trimming, consolidation). This audit addresses content — is what remains actually correct?
>
> **Method**: Review files in module groups so related content can be cross-checked. Each group should be reviewed against the current codebase.

---

## Module Groups for Review

### Group 1: AI Platform (8 files, ~3,570 lines)

**Architecture:**
| File | Lines | Content |
|------|-------|---------|
| `AI-ARCHITECTURE.md` | 315 | Four-tier architecture, tool framework, scope system |
| `AI-CHAT-STRATEGY-M365-COPILOT-VS-SPRKCHAT.md` | 244 | Two-plane strategy (M365 Copilot vs SprkChat) |
| `ai-document-summary-architecture.md` | 305 | Document creation flows (upload, email, Outlook, Word) |
| `ai-semantic-relationship-graph.md` | 224 | Multi-modal discovery design (structural + semantic) |
| `playbook-architecture.md` | 277 | Node type system, execution engine, canvas model |

**Guides:**
| File | Lines | Content |
|------|-------|---------|
| `JPS-AUTHORING-GUIDE.md` | 1,389 | JPS schema, prompt design, action definitions |
| `SCOPE-CONFIGURATION-GUIDE.md` | 1,241 | Scope model, pre-fill, builder |
| `AI-MODEL-SELECTION-GUIDE.md` | 246 | Model selection rules, deployment status |

**Review focus**: Are the AI architecture decisions still current? Has the tool framework, scope system, or playbook engine changed since these were written?

---

### Group 2: Authentication & Authorization (5 files, ~2,131 lines)

**Architecture:**
| File | Lines | Content |
|------|-------|---------|
| `auth-azure-resources.md` | 852 | Azure AD app registrations, GUIDs, permissions |
| `auth-AI-azure-resources.md` | 438 | AI resource endpoints, models, subscriptions |
| `auth-performance-monitoring.md` | 289 | Latency baselines, TTL decisions, thresholds |
| `auth-security-boundaries.md` | 226 | Seven trust boundary definitions |
| `sdap-auth-patterns.md` | 122 | Nine auth pattern taxonomy |

**Guides:**
| File | Lines | Content |
|------|-------|---------|
| `DATAVERSE-AUTHENTICATION-GUIDE.md` | 984 | Dataverse auth setup (TODO-marked: stale package versions) |

**Review focus**: Are Azure resource GUIDs, endpoints, and app registrations still correct? Are the auth patterns still the ones in use?

---

### Group 3: BFF API & Core Services (6 files, ~1,399 lines)

**Architecture:**
| File | Lines | Content |
|------|-------|---------|
| `sdap-overview.md` | 582 | Platform overview, seven functional domains |
| `sdap-bff-api-patterns.md` | 135 | SPE container model, Redis TTL decisions |
| `sdap-component-interactions.md` | 111 | Cross-component impact reference |
| `sdap-workspace-integration-patterns.md` | 94 | Entity-agnostic creation, fire-and-forget analyze |
| `uac-access-control.md` | 123 | Three-plane access control model |

**Guides:**
| File | Lines | Content |
|------|-------|---------|
| `HOW-TO-ADD-SDAP-TO-NEW-ENTITY.md` | 740 | Entity onboarding procedure (TODO-marked: stale paths) |
| `SHARED-UI-COMPONENTS-GUIDE.md` | 572 | Component inventory (TODO-marked: 30+ paths) |
| `SERVICE-DECOMPOSITION-GUIDE.md` | 141 | Service decomposition principles |
| `INTERFACE-SEGREGATION-GUIDE.md` | 138 | Interface segregation principles |

**Review focus**: Does sdap-overview still describe the actual platform? Are the BFF patterns still the ones in use? Is the component inventory current?

---

### Group 4: Communication & Email (4 files, ~2,384 lines)

**Architecture:**
| File | Lines | Content |
|------|-------|---------|
| `communication-service-architecture.md` | 159 | Graph API choice, webhook design, dual send modes |
| `email-to-document-architecture.md` | 86 | Hybrid trigger design, idempotency |
| `email-to-document-automation.md` | 99 | Overlaps with above — should these merge? |

**Guides:**
| File | Lines | Content |
|------|-------|---------|
| `COMMUNICATION-DEPLOYMENT-GUIDE.md` | 1,040 | Communication service deployment |
| `COMMUNICATION-ADMIN-GUIDE.md` | 801 | Admin setup for communication accounts |
| `communication-user-guide.md` | 354 | End-user email features |

**Review focus**: Two architecture files cover the same feature — should they be merged? Are the deployment procedures still accurate?

---

### Group 5: UI Framework & PCF (6 files, ~1,624 lines)

**Architecture:**
| File | Lines | Content |
|------|-------|---------|
| `VISUALHOST-ARCHITECTURE.md` | 226 | Config-driven visualization framework |
| `SIDE-PANE-PLATFORM-ARCHITECTURE.md` | 150 | SidePaneManager pattern |
| `ui-dialog-shell-architecture.md` | 173 | Three-layer UI model, shell selection |
| `universal-dataset-grid-architecture.md` | 148 | Shared grid, config-driven views |
| `sdap-pcf-patterns.md` | 85 | PCF migration decisions (ADR-006) |
| `event-to-do-architecture.md` | 159 | Events + Todo unified code page |

**Guides:**
| File | Lines | Content |
|------|-------|---------|
| `VISUALHOST-SETUP-GUIDE.md` | 2,112 | VisualHost configuration (largest guide — drift risk) |
| `PCF-DEPLOYMENT-GUIDE.md` | 751 | PCF build/deploy procedures |
| `DOCUMENT-RELATIONSHIP-VIEWER-GUIDE.md` | 885 | Document relationship viewer |
| `DOCUMENT-UPLOAD-WIZARD-INTEGRATION-GUIDE.md` | 462 | Upload wizard integration |
| `WORKSPACE-ENTITY-CREATION-GUIDE.md` | 377 | Entity creation wizard |
| `WORKSPACE-AI-PREFILL-GUIDE.md` | 570 | AI pre-fill integration |
| `EVENT-TYPE-CONFIGURATION.md` | 422 | Event type setup |
| `RIBBON-WORKBENCH-HOW-TO-ADD-BUTTON.md` | 90 | Ribbon button how-to |
| `ai-assistant-theming.md` | 252 | AI assistant theming |

**Review focus**: Are the UI architecture decisions current with the latest workspace changes? Is the 2,112-line VisualHost guide still accurate?

---

### Group 6: Finance Intelligence (2 files, ~2,377 lines)

**Architecture:**
| File | Lines | Content |
|------|-------|---------|
| `finance-intelligence-architecture.md` | 102 | Hybrid VisualHost, structured output, idempotency |

**Guides:**
| File | Lines | Content |
|------|-------|---------|
| `finance-intelligence-user-guide.md` | 1,199 | Finance module user guide |
| `finance-spend-snapshot-visualization-guide.md` | 1,176 | Spend snapshot visualization |

**Review focus**: Are the finance decisions still current? Are the user guides accurate?

---

### Group 7: Infrastructure & Deployment (6 files, ~5,207 lines)

**Architecture:**
| File | Lines | Content |
|------|-------|---------|
| `AZURE-RESOURCE-NAMING-CONVENTION.md` | 367 | Naming standards (authoritative reference) |
| `SPAARKE-REPOSITORY-ARCHITECTURE.md` | 401 | Repo structure diagram |
| `INFRASTRUCTURE-PACKAGING-STRATEGY.md` | 191 | Multi-tenant deployment model |
| `multi-environment-portability-strategy.md` | 132 | Layered portability strategy |

**Guides:**
| File | Lines | Content |
|------|-------|---------|
| `PRODUCTION-DEPLOYMENT-GUIDE.md` | 1,672 | Production deployment (TODO-marked: tool versions) |
| `ENVIRONMENT-DEPLOYMENT-GUIDE.md` | 822 | Environment setup (TODO-marked: CLI commands) |
| `AI-DEPLOYMENT-GUIDE.md` | 1,117 | AI service deployment (TODO-marked: PCF versions) |
| `CUSTOMER-DEPLOYMENT-GUIDE.md` | 1,128 | Customer-facing setup |
| `CUSTOMER-ONBOARDING-RUNBOOK.md` | 599 | Onboarding procedure |
| `CUSTOMER-QUICK-START-CHECKLIST.md` | 158 | Quick start checklist |
| `SECRET-ROTATION-PROCEDURES.md` | 415 | Secret rotation |
| `GITHUB-ENVIRONMENT-PROTECTION.md` | 174 | GitHub env protection |

**Review focus**: Are deployment guides current with latest Azure/Dataverse configuration? TODO-marked files are known drift risks.

---

### Group 8: External Access & Office (3 files, ~2,078 lines)

**Architecture:**
| File | Lines | Content |
|------|-------|---------|
| `external-access-spa-architecture.md` | 75 | B2B guest auth model |
| `office-outlook-teams-integration-architecture.md` | 170 | Office add-in capability design |

**Guides:**
| File | Lines | Content |
|------|-------|---------|
| `EXTERNAL-ACCESS-ADMIN-SETUP.md` | 528 | External access admin setup |
| `EXTERNAL-ACCESS-SPA-GUIDE.md` | 391 | External SPA development |
| `office-addins-admin-guide.md` | 980 | Office add-in admin setup |
| `office-addins-deployment-checklist.md` | 420 | Office add-in deployment |

**Review focus**: Is the external access model still accurate? Are the Office add-in guides current?

---

### Group 9: RAG & Search (3 files, ~2,503 lines)

**Guides:**
| File | Lines | Content |
|------|-------|---------|
| `RAG-ARCHITECTURE.md` | 1,122 | RAG system architecture |
| `RAG-CONFIGURATION.md` | 690 | RAG configuration (TODO-marked: config keys) |
| `RAG-TROUBLESHOOTING.md` | 1,101 | RAG troubleshooting |
| `AI-EMBEDDING-STRATEGY.md` | 432 | Embedding strategy (TODO-marked: index inventory) |

**Review focus**: Is the RAG architecture current? Are the index names, config keys, and troubleshooting steps still valid?

---

### Group 10: Operations & Monitoring (3 files, ~1,546 lines)

**Guides:**
| File | Lines | Content |
|------|-------|---------|
| `MONITORING-AND-ALERTING-GUIDE.md` | 721 | Monitoring setup |
| `INCIDENT-RESPONSE.md` | 571 | Incident response procedures |
| `AI-MONITORING-DASHBOARD.md` | 254 | AI monitoring dashboard (TODO-marked) |

**Review focus**: Are the monitoring thresholds, alert rules, and response procedures current?

---

## Summary

| Group | Architecture Files | Guide Files | Total Lines | Priority |
|-------|-------------------|-------------|-------------|----------|
| 1. AI Platform | 5 | 3 | ~3,570 | HIGH (core feature) |
| 2. Auth | 5 + 1 guide | 1 | ~2,131 | HIGH (security) |
| 3. BFF/Core | 5 + 4 guides | 4 | ~1,399 | HIGH (daily use) |
| 4. Communication | 3 + 3 guides | 3 | ~2,384 | MEDIUM |
| 5. UI/PCF | 6 + 9 guides | 9 | ~1,624 | HIGH (daily use) |
| 6. Finance | 1 + 2 guides | 2 | ~2,377 | MEDIUM |
| 7. Infrastructure | 4 + 8 guides | 8 | ~5,207 | MEDIUM (deploy guides) |
| 8. External/Office | 2 + 4 guides | 4 | ~2,078 | LOW |
| 9. RAG/Search | 0 + 4 guides | 4 | ~2,503 | MEDIUM |
| 10. Operations | 0 + 3 guides | 3 | ~1,546 | MEDIUM |

**Recommended review order**: Groups 1, 2, 3, 5 first (high priority — AI, auth, core, UI). Then 4, 7, 9 (medium). Then 6, 8, 10 (lower).
