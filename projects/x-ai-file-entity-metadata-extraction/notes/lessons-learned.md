# Lessons Learned: AI File Entity Metadata Extraction

> **Project**: AI File Entity Metadata Extraction
> **Completion Date**: 2025-12-11
> **Duration**: December 9-11, 2025 (3 days)

---

## 1. What Went Well

### 1.1 Phased Approach
Breaking the project into three distinct phases (1a, 1b, 2) with deployment gates proved effective:
- Phase 1a focused on service rename + structured output
- Phase 1b added Dataverse integration + email support
- Phase 2 introduced Azure AI Search + record matching

This allowed for incremental deployment and validation.

### 1.2 POML Task Files
Using structured POML task files with clear acceptance criteria enabled:
- Parallel task execution where dependencies allowed
- Clear tracking of progress in TASK-INDEX.md
- Reproducible execution across AI sessions

### 1.3 Service Naming Convention
Renaming "Summarize" to "DocumentIntelligence" created a cleaner abstraction:
- Single service handles all AI document analysis features
- Configuration is centralized in DocumentIntelligenceOptions
- New features (record matching) fit naturally into the namespace

### 1.4 Custom Confidence Scoring
The weighted scoring algorithm for record matching worked well:
- Reference numbers (50%) provide strong signals
- Organization/People matching (40%) adds context
- Keywords (10%) as supporting evidence
- Jaccard similarity for fuzzy matching

---

## 2. Challenges Encountered

### 2.1 Azure AI Search Configuration
**Issue**: Deployment returned 503 after enabling RecordMatchingEnabled=true
**Root Cause**: Missing AiSearchEndpoint and AiSearchKey settings
**Resolution**: Added all three settings together (enabled + endpoint + key)
**Lesson**: Service startup validation should provide clearer error messages

### 2.2 Integration Test Configuration
**Issue**: Integration tests failed locally due to missing Service Bus connection string
**Root Cause**: WebApplicationFactory requires all DI dependencies to resolve
**Lesson**: Integration tests need mock/stub configuration for services not under test

### 2.3 Endpoint URL Mismatch
**Issue**: Test used `/api/admin/record-matching/stats` but endpoint was `/status`
**Resolution**: Updated test to use correct URL
**Lesson**: Endpoint naming should be validated during code review

### 2.4 TypeScript Type Updates
**Issue**: Fluent UI v9 types changed (SelectionEvents, OptionOnSelectData)
**Resolution**: Updated PCF hooks to use new type signatures
**Lesson**: Pin Fluent UI versions or track breaking changes

---

## 3. Technical Insights

### 3.1 Azure AI Search Capabilities Used
From the full Azure AI Search feature set, we utilized:
- **Full-text search**: Keyword matching on record names/descriptions
- **Filtering**: Record type filtering (sprk_matter, sprk_project, sprk_invoice)
- **Custom scoring**: Our own confidence calculation (not Azure's scoring profiles)

**Not used** (available for future):
- Vector search / semantic search
- Synonyms
- Autocomplete / suggestions
- Geospatial search

### 3.2 SSE Streaming Pattern
The Server-Sent Events pattern for AI streaming works well:
- Real-time feedback during analysis
- Structured JSON at completion for persistence
- Client can disconnect early without losing work (via enqueue fallback)

### 3.3 Configuration Hierarchy
`DocumentIntelligenceOptions` effectively manages:
- Feature flags (Enabled, RecordMatchingEnabled)
- Azure service endpoints and keys
- Prompt templates
- File type support configuration

---

## 4. Recommendations for Future Projects

### 4.1 Pre-Deployment Checklist
Before enabling new features in production:
1. Verify all related configuration settings are present
2. Test service startup with new flags enabled locally
3. Add health check endpoints that validate dependencies

### 4.2 Documentation Strategy
Maintain two documentation tracks:
- **Implementation Status**: What's actually deployed (AI-IMPLEMENTATION-STATUS.md)
- **Architecture Vision**: Target state and roadmap (SPAARKE-AI-ARCHITECTURE.md)

### 4.3 Test Configuration
For integration tests:
- Create stub configuration file for CI/CD
- Document required local config for developers
- Consider test containers for Azure dependencies

### 4.4 AI Search Index Population
The index needs to be populated before record matching works:
- Manual sync: POST /api/admin/record-matching/sync
- Future: Background worker for incremental updates
- Consider initial data seeding script

---

## 5. Metrics

| Metric | Value |
|--------|-------|
| Total Tasks | 30 |
| Tasks Completed | 30 |
| New API Endpoints | 6 |
| New Dataverse Fields | 12+ |
| PCF Version | 3.5.0 |
| Azure Resources | 3 (OpenAI, Doc Intel, AI Search) |

---

## 6. Files Created/Modified

### Key New Files
- `Services/Ai/DocumentIntelligenceService.cs`
- `Services/RecordMatching/RecordMatchService.cs`
- `Api/Ai/RecordMatchEndpoints.cs`
- `docs/ai-knowledge/guides/AI-IMPLEMENTATION-STATUS.md`

### Key Modified Files
- `Configuration/DocumentIntelligenceOptions.cs` (extended)
- `Program.cs` (new endpoint registrations)
- `UniversalQuickCreate/` (PCF v3.5.0)

---

*Document preserved for future reference*
