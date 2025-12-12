# Task Index - AI Document Intelligence R1

> **Project**: ai-document-intelligence-r1  
> **Last Updated**: December 12, 2025  
> **Total Tasks**: 178

## Task Status Legend

- 🔲 Not Started
- 🔄 In Progress
- ✅ Complete
- ⏸️ Blocked
- ❌ Cancelled

---

## Phase 1: Core Infrastructure (Week 1-2)

### Multi-Tenant Parameterization

| ID | Task | Status | Dependencies | Estimated Hours |
|----|------|--------|--------------|-----------------|
| 001 | Create Environment Variables in Dataverse Solution | ✅ | none | 4h |
| 002 | Create Bicep Parameter Template | ✅ | 001 | 3h |
| 003 | Create Token-Replacement appsettings.json Template | ✅ | 001 | 2h |
| 004 | Update BFF API Configuration to Use Environment Variables | ✅ | 001, 003 | 4h |

### Azure AI Foundry Infrastructure

| ID | Task | Status | Dependencies | Estimated Hours |
|----|------|--------|--------------|-----------------|
| 005 | Create Parameterized Bicep Template for AI Foundry Hub | ✅ | 002 | 6h |
| 006 | Deploy AI Foundry Hub and Project | ✅ | 005 | 4h |
| 007 | Create Prompt Flow: analysis-execute | ✅ | 006 | 8h |
| 008 | Create Prompt Flow: analysis-continue | ✅ | 006 | 6h |
| 009 | Configure AI Foundry Evaluation Pipeline | ✅ | 006 | 4h |

### Dataverse Entities

| ID | Task | Status | Dependencies | Estimated Hours |
|----|------|--------|--------------|-----------------|
| 010 | Create sprk_analysis Entity with Fields and Relationships | ✅ | none | 4h |
| 011 | Create sprk_analysisaction Entity | ✅ | none | 2h |
| 012 | Create sprk_analysisskill Entity | ✅ | none | 2h |
| 013 | Create sprk_analysisknowledge Entity | ✅ | none | 3h |
| 014 | Create sprk_knowledgedeployment Entity | ✅ | none | 3h |
| 015 | Create sprk_analysistool Entity | ✅ | none | 2h |
| 016 | Create sprk_analysisplaybook Entity with N:N Relationships | ✅ | 010-015 | 4h |
| 017 | Create sprk_analysisworkingversion Entity | ✅ | 010 | 3h |
| 018 | Create sprk_analysisemailmetadata Entity | ✅ | 010 | 2h |
| 019 | Create sprk_analysischatmessage Entity | ✅ | 010 | 2h |
| 020 | Create Security Roles for Analysis Feature | 🔲 | 010-019 | 3h |
| 021 | Export Dataverse Solution Package | ✅ | 001, 010-020 | 2h |

### BFF API Implementation

| ID | Task | Status | Dependencies | Estimated Hours |
|----|------|--------|--------------|-----------------|
| 022 | Create AnalysisEndpoints.cs with POST /execute | ✅ | 010, 004 | 4h |
| 023 | Create AnalysisEndpoints.cs with POST /continue | ✅ | 022 | 3h |
| 024 | Create AnalysisEndpoints.cs with POST /save | ✅ | 022 | 3h |
| 025 | Create AnalysisEndpoints.cs with POST /export | ✅ | 022 | 3h |
| 026 | Create AnalysisOrchestrationService | ✅ | 022-025 | 8h |
| 027 | Create ScopeResolverService with Redis Caching | ✅ | 026 | 6h |
| 028 | Create AnalysisContextBuilder with Prompt Flow Integration | ✅ | 007, 008, 026 | 6h |
| 029 | Create WorkingDocumentVersionService with SPE Storage | ✅ | 026 | 6h |
| 030 | Create AnalysisAuthorizationFilter | ✅ | 022-025 | 3h |
| 031 | Add Unit Tests for Analysis Services | ✅ | 026-029 | 8h |
| 032 | Add Integration Tests for Analysis Endpoints | ✅ | 022-025, 026-029 | 6h |

### Deployment & Verification

| ID | Task | Status | Dependencies | Estimated Hours |
|----|------|--------|--------------|-----------------|
| 033 | Test Bicep Deployment to External Test Subscription | 🔲 | 005, 006, 021 | 4h |
| 034 | Test Dataverse Solution Import to Clean Environment | 🔲 | 021 | 3h |
| 035 | Verify All Environment Variables Resolve Correctly | ✅ | 001, 004, 033 | 2h |
| 036 | Run Integration Tests Against Dev Environment | 🔲 | 032, 033 | 3h |
| 037 | Create Phase 1 Deployment Guide | ✅ | 033-036 | 4h |

**Phase 1 Total**: 37 tasks, ~165 hours

---

## Phase 2: UI Components (Week 3-4)

### Document Form Customizations

| ID | Task | Status | Dependencies | Estimated Hours |
|----|------|--------|--------------|-----------------|
| 038 | Add Analysis Tab to sprk_document Form | ✅ | Phase 1 Complete | 2h |
| 039 | Add Analysis Grid to Analysis Tab | ✅ | 038 | 2h |
| 040 | Add "+ New Analysis" Command Button | ✅ | 038 | 2h |
| 041 | Add Form Scripts for Navigation to Analysis Workspace | ✅ | 038-040 | 3h |

### Analysis Builder Custom Page

| ID | Task | Status | Dependencies | Estimated Hours |
|----|------|--------|--------------|-----------------|
| 042 | Create Analysis Builder PCF Component | ✅ | Phase 1 Complete | 4h |
| 043 | Add Action Selector Component | ✅ | 042 | 3h |
| 044 | Add Skills Multi-Select Component | ✅ | 042 | 3h |
| 045 | Add Knowledge Multi-Select Component | ✅ | 042 | 3h |
| 046 | Add Tools Multi-Select Component | ✅ | 042 | 3h |
| 047 | Add Output Options Component | ✅ | 042 | 2h |
| 048 | Add Playbook Selector Component | ✅ | 042 | 3h |
| 049 | Implement "Start Analysis" Button with API Call | ✅ | 042-048 | 4h |

### Analysis Workspace Custom Page & PCF

| ID | Task | Status | Dependencies | Estimated Hours |
|----|------|--------|--------------|-----------------|
| 050 | Create Analysis Workspace Custom Page (3-Column Layout) | ✅ | Phase 1 Complete | 4h |
| 051 | Create AnalysisWorkspace PCF Control Project | ✅ | none | 3h |
| 052 | Implement PCF Environment Variable Access Pattern | ✅ | 051 | 4h |
| 053 | Implement Three-Column Layout (Analysis + Source + Chat) | ✅ | 051, 052 | 6h |
| 054 | Integrate Monaco Editor for Working Document | ✅ | 053 | 4h |
| 055 | Integrate SpeFileViewer PCF for Source Preview | ✅ | 053 | 3h |
| 056 | Implement SSE Client for Chat Streaming | ✅ | 052 | 6h |
| 057 | Implement AI Chat Panel with Message History | ✅ | 056 | 4h |
| 058 | Implement Working Document Auto-Save | ✅ | 054 | 3h |
| 059 | Build and Package AnalysisWorkspace PCF | ✅ | 051-058 | 2h |
| 060 | Deploy PCF to Dev Environment | ✅ | 059 | 2h |
| 061 | Add AnalysisWorkspace PCF to Custom Page/Form | ✅ | 050, 060 | 2h |

### UI Testing & Documentation

| ID | Task | Status | Dependencies | Estimated Hours |
|----|------|--------|--------------|-----------------|
| 062 | Test Analysis Creation from Document Form | 🔲 | 038-041, 042-049 | 2h |
| 063 | Test Analysis Workspace Navigation | 🔲 | 041, 050-061 | 2h |
| 064 | Test SSE Streaming in Custom Page | 🔲 | 056-057 | 3h |
| 065 | Test PCF Reads API URL from Environment Variable | 🔲 | 052, 060 | 2h |
| 066 | Export UI Solution Package | 🔲 | 038-061 | 2h |
| 067 | Create UI User Guide | 🔲 | 062-065 | 4h |

**Phase 2 Total**: 30 tasks, ~85 hours

---

## Phase 3: Scope System & Hybrid RAG (Week 5-6)

### Admin UI for Scope Entities

| ID | Task | Status | Dependencies | Estimated Hours |
|----|------|--------|--------------|-----------------|
| 068 | Create Model-Driven Forms for sprk_analysisaction | 🔲 | Phase 1 Complete | 2h |
| 069 | Create Model-Driven Forms for sprk_analysisskill | 🔲 | Phase 1 Complete | 2h |
| 070 | Create Model-Driven Forms for sprk_analysisknowledge | 🔲 | Phase 1 Complete | 3h |
| 071 | Create Model-Driven Forms for sprk_analysistool | 🔲 | Phase 1 Complete | 2h |
| 072 | Create Admin Views with Filtering and Search | 🔲 | 068-071 | 3h |
| 073 | Add Validation Rules for Scope Entities | 🔲 | 068-071 | 2h |

### Hybrid RAG Infrastructure

| ID | Task | Status | Dependencies | Estimated Hours |
|----|------|--------|--------------|-----------------|
| 074 | Create Bicep Template for Azure AI Search (Shared Index) | 🔲 | Phase 1 Complete | 4h |
| 075 | Deploy Azure AI Search Service | 🔲 | 074 | 3h |
| 076 | Create IKnowledgeDeploymentService Interface | 🔲 | none | 3h |
| 077 | Implement Shared Index Model with Tenant Filtering | 🔲 | 075, 076 | 6h |
| 078 | Implement Dedicated Index Model Per Customer | 🔲 | 075, 076 | 6h |
| 079 | Implement Cross-Tenant Auth for Customer-Owned Indexes | 🔲 | 076 | 8h |
| 080 | Create IRagService Interface with Hybrid Search | 🔲 | none | 4h |
| 081 | Implement Embedding Generation (text-embedding-3-small) | 🔲 | 080 | 4h |
| 082 | Implement Hybrid Search with Semantic Ranking | 🔲 | 075, 080, 081 | 6h |
| 083 | Add Redis Caching for RAG Results | 🔲 | 082 | 3h |

### Tool Handler Framework

| ID | Task | Status | Dependencies | Estimated Hours |
|----|------|--------|--------------|-----------------|
| 084 | Create IAnalysisToolHandler Interface | 🔲 | none | 2h |
| 085 | Implement Dynamic Tool Loading via Reflection | 🔲 | 084 | 4h |
| 086 | Create EntityExtractor Tool | 🔲 | 084 | 4h |
| 087 | Create ClauseAnalyzer Tool | 🔲 | 084 | 4h |
| 088 | Create DocumentClassifier Tool | 🔲 | 084 | 4h |
| 089 | Add Tool Configuration Validation | 🔲 | 084-088 | 2h |
| 090 | Add Tool Error Handling and Fallbacks | 🔲 | 084-088 | 3h |

### Seed Data & Testing

| ID | Task | Status | Dependencies | Estimated Hours |
|----|------|--------|--------------|-----------------|
| 091 | Create Seed Data: 5 Default Actions | 🔲 | Phase 1 Complete | 2h |
| 092 | Create Seed Data: 10 Default Skills | 🔲 | Phase 1 Complete | 2h |
| 093 | Create Seed Data: 5 Sample Knowledge Sources | 🔲 | 070, 075 | 3h |
| 094 | Create Seed Data: Default KnowledgeDeployment (Shared Model) | 🔲 | 075 | 2h |
| 095 | Test Prompt Construction with All Scope Combinations | 🔲 | 091-094 | 4h |
| 096 | Performance Test RAG Retrieval (<500ms P95) | 🔲 | 082-083 | 4h |
| 097 | Test Cross-Tenant RAG Authentication | 🔲 | 079 | 4h |

**Phase 3 Total**: 30 tasks, ~105 hours

---

## Phase 4: Playbooks & Export (Week 7-8)

### Playbook System

| ID | Task | Status | Dependencies | Estimated Hours |
|----|------|--------|--------------|-----------------|
| 098 | Create Model-Driven Forms for sprk_analysisplaybook | 🔲 | Phase 1 Complete | 3h |
| 099 | Implement "Save as Playbook" in Analysis Builder | 🔲 | Phase 2 Complete | 4h |
| 100 | Implement Playbook Loading in Analysis Builder | 🔲 | 099 | 3h |
| 101 | Add Playbook Sharing Logic (Private vs. Public) | 🔲 | 098 | 3h |
| 102 | Add Playbook Preview Functionality | 🔲 | 098 | 2h |
| 103 | Create 5 Default Playbooks | 🔲 | Phase 3 Complete | 3h |

### Export Infrastructure

| ID | Task | Status | Dependencies | Estimated Hours |
|----|------|--------|--------------|-----------------|
| 104 | Create IDocumentExportService Interface | 🔲 | none | 2h |
| 105 | Implement Markdown-to-DOCX Converter (OpenXML SDK) | 🔲 | 104 | 6h |
| 106 | Create Azure Function for PDF Conversion | 🔲 | none | 6h |
| 107 | Deploy PDF Converter Function | 🔲 | 106 | 2h |
| 108 | Implement Markdown-to-PDF Converter (Function Client) | 🔲 | 104, 107 | 4h |
| 109 | Add Export History Tracking | 🔲 | 104 | 2h |
| 110 | Add File Naming Conventions and Metadata | 🔲 | 104 | 2h |

### Email Integration

| ID | Task | Status | Dependencies | Estimated Hours |
|----|------|--------|--------------|-----------------|
| 111 | Create IEmailActivityService Interface | 🔲 | none | 2h |
| 112 | Implement Email Activity Record Creation | 🔲 | 111 | 4h |
| 113 | Add Email Composition Pre-Fill Logic | 🔲 | 112 | 3h |
| 114 | Add Attachment Handling (Analysis File + Source Link) | 🔲 | 112 | 4h |
| 115 | Create Email Template for Analysis Results | 🔲 | none | 2h |
| 116 | Add "Open in Email" Redirect to MDA | 🔲 | 112 | 2h |
| 117 | Test with Server-Side Sync Configuration | 🔲 | 112-116 | 4h |

### Teams Integration

| ID | Task | Status | Dependencies | Estimated Hours |
|----|------|--------|--------------|-----------------|
| 118 | Create ITeamsMessageService Interface | 🔲 | none | 2h |
| 119 | Implement Teams Message Posting via Graph API | 🔲 | 118 | 4h |
| 120 | Build Channel Selector UI Component | 🔲 | none | 3h |
| 121 | Add Adaptive Card Formatting for Analysis Summary | 🔲 | 119 | 4h |
| 122 | Implement Deep Link to Analysis Workspace | 🔲 | 119 | 2h |
| 123 | Add @mention Support for Stakeholders | 🔲 | 119 | 3h |

### Workflow Triggers

| ID | Task | Status | Dependencies | Estimated Hours |
|----|------|--------|--------------|-----------------|
| 124 | Design Workflow Trigger Payload Schema | 🔲 | none | 2h |
| 125 | Create IWorkflowTriggerService Interface | 🔲 | 124 | 2h |
| 126 | Implement Workflow Trigger Service | 🔲 | 125 | 3h |
| 127 | Create Sample Power Automate Flows | 🔲 | 126 | 4h |
| 128 | Add Trigger Configuration in Playbooks | 🔲 | 098, 126 | 3h |
| 129 | Document Custom Trigger Development Guide | 🔲 | 124-126 | 3h |

**Phase 4 Total**: 32 tasks, ~95 hours

---

## Phase 5: Production Readiness & Evaluation (Week 9-10)

### Performance Optimization

| ID | Task | Status | Dependencies | Estimated Hours |
|----|------|--------|--------------|-----------------|
| 130 | Implement Redis Caching for Scopes | 🔲 | Phase 3 Complete | 4h |
| 131 | Implement Prompt Compression for Large Documents | 🔲 | Phase 1 Complete | 4h |
| 132 | Optimize Token Usage (Remove Redundant Instructions) | 🔲 | Phase 1 Complete | 3h |
| 133 | Implement Connection Pooling for Dataverse/Graph | 🔲 | Phase 1 Complete | 3h |
| 134 | Add CDN for PCF Static Assets | 🔲 | Phase 2 Complete | 2h |
| 135 | Run Load Testing (100+ Concurrent Users) | 🔲 | Phase 1-4 Complete | 6h |
| 136 | Profile and Optimize Hot Paths | 🔲 | 135 | 4h |

### Azure AI Foundry Evaluation

| ID | Task | Status | Dependencies | Estimated Hours |
|----|------|--------|--------------|-----------------|
| 137 | Configure Evaluation Metrics Collection | 🔲 | Phase 1 Complete | 3h |
| 138 | Set Up Nightly Batch Evaluation Runs | 🔲 | 137 | 3h |
| 139 | Create Evaluation Dashboard in Foundry Portal | 🔲 | 137-138 | 3h |
| 140 | Configure Alerts for Quality Degradation | 🔲 | 138 | 2h |
| 141 | Document Evaluation Results and Trends | 🔲 | 139 | 2h |
| 142 | Tune Prompts Based on Evaluation Data | 🔲 | 141 | 6h |

### Telemetry & Monitoring

| ID | Task | Status | Dependencies | Estimated Hours |
|----|------|--------|--------------|-----------------|
| 143 | Add Application Insights Custom Events | 🔲 | Phase 1 Complete | 4h |
| 144 | Implement Distributed Tracing (W3C Trace Context) | 🔲 | Phase 1 Complete | 4h |
| 145 | Add Cost Tracking Per Customer | 🔲 | Phase 1 Complete | 3h |
| 146 | Create Usage Dashboard | 🔲 | 143-145 | 3h |
| 147 | Create Performance Dashboard | 🔲 | 143-145 | 3h |
| 148 | Create Error Dashboard | 🔲 | 143-145 | 3h |
| 149 | Create Cost Dashboard | 🔲 | 143-145 | 3h |
| 150 | Configure Alerts (Error Rate, Latency, Token Budget) | 🔲 | 143-145 | 3h |
| 151 | Add User Journey Tracking | 🔲 | 143 | 3h |

### Error Handling & Resilience

| ID | Task | Status | Dependencies | Estimated Hours |
|----|------|--------|--------------|-----------------|
| 152 | Add Comprehensive User-Friendly Error Messages | 🔲 | Phase 1-4 Complete | 4h |
| 153 | Implement Circuit Breaker for AI Services | 🔲 | Phase 1 Complete | 4h |
| 154 | Add Graceful Degradation (Fallback Prompts) | 🔲 | Phase 1 Complete | 3h |
| 155 | Improve Rate Limit Handling | 🔲 | Phase 1 Complete | 2h |
| 156 | Add Retry Logic with Exponential Backoff | 🔲 | Phase 1 Complete | 3h |
| 157 | Test Failure Scenarios | 🔲 | 152-156 | 4h |

### Security & Compliance

| ID | Task | Status | Dependencies | Estimated Hours |
|----|------|--------|--------------|-----------------|
| 158 | Complete Security Review (Penetration Test) | 🔲 | Phase 1-4 Complete | 8h |
| 159 | Verify Data Isolation (Multi-Tenant) | 🔲 | Phase 1 Complete | 4h |
| 160 | Audit Authorization Checks | 🔲 | Phase 1-2 Complete | 4h |
| 161 | Review PII Handling (Content Safety Filters) | 🔲 | Phase 1 Complete | 3h |
| 162 | Document Compliance Controls | 🔲 | 158-161 | 4h |
| 163 | Test Cross-Tenant RAG Access Security | 🔲 | Phase 3 Complete | 4h |
| 164 | Verify Key Vault Secret Rotation | 🔲 | Phase 1 Complete | 2h |

### Production Deployment

| ID | Task | Status | Dependencies | Estimated Hours |
|----|------|--------|--------------|-----------------|
| 165 | Deploy Infrastructure to Production Azure | 🔲 | Phase 1-4 Complete | 4h |
| 166 | Deploy Dataverse Solution to Production Org | 🔲 | 165 | 3h |
| 167 | Deploy BFF API to Production App Service | 🔲 | 165 | 3h |
| 168 | Configure Production Key Vault and Secrets | 🔲 | 165 | 2h |
| 169 | Run Smoke Tests in Production | 🔲 | 165-168 | 3h |
| 170 | Enable Monitoring and Alerts | 🔲 | 146-150, 169 | 2h |
| 171 | Create Runbook for Incident Response | 🔲 | 169-170 | 4h |

### Documentation & Training

| ID | Task | Status | Dependencies | Estimated Hours |
|----|------|--------|--------------|-----------------|
| 172 | Create User Guide (Creating Analyses, Using Workspace, Exporting) | 🔲 | Phase 2-4 Complete | 6h |
| 173 | Create Admin Guide (Managing Scopes, Playbooks, RAG Deployments) | 🔲 | Phase 3-4 Complete | 6h |
| 174 | Create Customer Deployment Guide (CRITICAL) | 🔲 | Phase 1 Complete | 8h |
| 175 | Create Developer Guide (Adding Tools, Extending Prompt Flows) | 🔲 | Phase 3-4 Complete | 6h |
| 176 | Create Video Tutorials (3-5 minutes each) | 🔲 | 172-173 | 8h |
| 177 | Create Release Notes and Changelog | 🔲 | Phase 1-5 Complete | 3h |
| 178 | Validate Customer Deployment Guide with External User | 🔲 | 174 | 4h |

**Phase 5 Total**: 49 tasks, ~175 hours

---

## Summary

**Total Tasks**: 178  
**Total Estimated Hours**: ~625 hours  
**Total Weeks**: 10 weeks  

### Phase Breakdown

| Phase | Tasks | Hours | Weeks |
|-------|-------|-------|-------|
| Phase 1: Core Infrastructure | 37 | 165h | 2 |
| Phase 2: UI Components | 30 | 85h | 2 |
| Phase 3: Scope System & RAG | 30 | 105h | 2 |
| Phase 4: Playbooks & Export | 32 | 95h | 2 |
| Phase 5: Production Readiness | 49 | 175h | 2 |

---

## Critical Path

**Must Complete in Order:**
1. Task 001 (Environment Variables) - BLOCKS Phase 1
2. Tasks 010-020 (Dataverse Entities) - BLOCKS All Phases
3. Tasks 022-032 (BFF API) - BLOCKS Phase 2
4. Phase 1 Complete - BLOCKS Phase 2
5. Phase 1-3 Complete - BLOCKS Phase 4
6. Phase 1-4 Complete - BLOCKS Phase 5

**High Risk Tasks:**
- 006: Deploy AI Foundry Hub (unknown complexity)
- 079: Cross-tenant RAG auth (complex auth flow)
- 052: PCF environment variable access (multi-tenant critical)
- 178: Customer deployment guide validation (external dependency)

---

**Status**: Ready for Execution  
**Next Action**: Begin Task 001 - Create Environment Variables
