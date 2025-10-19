# Sprint 7: Dataset Grid to SDAP Integration

**Quick Start Guide for AI-Directed Coding Sessions**

---

## 📋 Documentation Structure

This sprint is organized for maximum efficiency in AI-directed coding sessions. Each task is self-contained with all necessary context, code patterns, and validation criteria.

### 🎯 Start Here

1. **[SPRINT-7-OVERVIEW.md](SPRINT-7-OVERVIEW.md)** - Executive summary and task roadmap
2. **[SPRINT-7-MASTER-RESOURCE.md](SPRINT-7-MASTER-RESOURCE.md)** - Central reference (architecture, API specs, patterns)

### 📁 Task Files (Sequential)

Each task file contains:
- ✅ AI Coding Prompt (copy/paste ready)
- ✅ Objective and context
- ✅ Step-by-step implementation with code examples
- ✅ Validation criteria
- ✅ Troubleshooting guide

| # | Task | Time | File |
|---|------|------|------|
| 1 | API Client Setup | 1-2 days | [TASK-1-API-CLIENT-SETUP.md](TASK-1-API-CLIENT-SETUP.md) |
| 2 | File Upload | 1-2 days | [TASK-2-FILE-UPLOAD.md](TASK-2-FILE-UPLOAD.md) |
| 3 | File Download | 0.5-1 day | [TASK-3-FILE-DOWNLOAD.md](TASK-3-FILE-DOWNLOAD.md) |
| 4 | File Delete | 1 day | [TASK-4-FILE-DELETE.md](TASK-4-FILE-DELETE.md) |
| 5 | File Replace | 0.5-1 day | [TASK-5-FILE-REPLACE.md](TASK-5-FILE-REPLACE.md) |
| 6 | Field Mapping | 0.5 day | [TASK-6-FIELD-MAPPING.md](TASK-6-FIELD-MAPPING.md) |
| 7 | Testing & Deployment | 1-2 days | [TASK-7-TESTING-DEPLOYMENT.md](TASK-7-TESTING-DEPLOYMENT.md) |

**Total Estimate**: 6-10 days

---

## 🚀 Quick Start for AI Coding

### For Each Task:

1. **Open task file** (e.g., TASK-1-API-CLIENT-SETUP.md)
2. **Copy AI Prompt** from top of file
3. **Paste into AI session** (Claude, GPT, etc.)
4. **Reference Master Resource** when needed for:
   - API endpoint specifications
   - Field mappings
   - Code patterns
   - Authentication flow
5. **Follow implementation steps** with provided code examples
6. **Validate** against criteria checklist
7. **Move to next task** when complete

### Example Workflow:

```bash
# Task 1: API Client Setup
1. Read: TASK-1-API-CLIENT-SETUP.md
2. AI Prompt: "Create a TypeScript API client for SDAP BFF API..."
3. Implement: SdapApiClient.ts, SdapApiClientFactory.ts
4. Validate: TypeScript compiles, token retrieval works
5. Next: TASK-2-FILE-UPLOAD.md

# Task 2: File Upload
1. Read: TASK-2-FILE-UPLOAD.md
2. AI Prompt: "Implement file upload functionality..."
3. Implement: FileUploadService.ts, update CommandBar.tsx
4. Test: File picker, upload, grid refresh
5. Next: TASK-3-FILE-DOWNLOAD.md
```

---

## 📚 Master Resource Quick Links

From **[SPRINT-7-MASTER-RESOURCE.md](SPRINT-7-MASTER-RESOURCE.md)**:

- **Architecture**: [Section Link](SPRINT-7-MASTER-RESOURCE.md#architecture-overview)
- **SDAP API Endpoints**: [Section Link](SPRINT-7-MASTER-RESOURCE.md#sdap-api-endpoints-reference)
- **Field Mappings**: [Section Link](SPRINT-7-MASTER-RESOURCE.md#field-mappings-dataverse--sdap-api)
- **Code Patterns**: [Section Link](SPRINT-7-MASTER-RESOURCE.md#code-patterns--standards)
- **Workflows**: [Section Link](SPRINT-7-MASTER-RESOURCE.md#common-workflows)

---

## ✅ Success Criteria (Overall Sprint)

### Technical
- [ ] Bundle size < 550 KB
- [ ] All file operations < 2s response time
- [ ] Zero TypeScript compilation errors
- [ ] Zero runtime errors in production

### User Experience
- [ ] Single-click file operations
- [ ] Automatic metadata population
- [ ] Clickable SharePoint URLs
- [ ] Real-time grid updates

### Deployment
- [ ] All tasks complete (1-7)
- [ ] Manual testing 100% complete
- [ ] Production deployment successful
- [ ] Post-deployment validation passed

---

## 🎯 Current State

- ✅ Universal Dataset Grid v2.0.7 deployed (React 18 + Fluent UI v9)
- ✅ SDAP BFF API production-ready (8.5/10)
- ✅ All prerequisites met
- 🚀 Ready to begin Task 1

---

## 📖 Background Documentation

### Sprint Context
- [Sprint 2 Wrap-Up](../Sprint%202/SPRINT-2-WRAP-UP-REPORT.md) - SDAP API implementation
- [Sprint 5B Summary](../../UniversalDatasetGrid/SPRINT_5B_SUMMARY.md) - Grid v2.0.7 completion
- [SDAP Assessment](../SDAP-PROJECT-COMPREHENSIVE-ASSESSMENT.md) - Overall project status

### Component Documentation
- [Universal Grid README](../../UniversalDatasetGrid/README.md)
- [Component API Reference](../../UniversalDatasetGrid/COMPONENT_API.md)

---

## 🛠️ File Structure

```
Sprint 7_Dataset Grid to SDAP/
├── README.md                        # This file (quick start)
├── SPRINT-7-OVERVIEW.md             # Executive summary
├── SPRINT-7-MASTER-RESOURCE.md      # Central reference
├── TASK-1-API-CLIENT-SETUP.md       # TypeScript API client
├── TASK-2-FILE-UPLOAD.md            # Upload implementation
├── TASK-3-FILE-DOWNLOAD.md          # Download implementation
├── TASK-4-FILE-DELETE.md            # Delete with confirmation
├── TASK-5-FILE-REPLACE.md           # Replace/update files
├── TASK-6-FIELD-MAPPING.md          # Clickable URLs, metadata
└── TASK-7-TESTING-DEPLOYMENT.md     # Tests, validation, deploy
```

---

## 💡 Tips for Efficient AI Coding

### DO:
✅ Start with Task 1, complete sequentially
✅ Read entire task file before coding
✅ Use provided code examples as templates
✅ Reference Master Resource for detailed specs
✅ Validate after each task before moving on
✅ Follow existing code patterns (logger, React hooks)

### DON'T:
❌ Skip tasks (dependencies exist)
❌ Modify task files during implementation
❌ Guess API contracts (refer to Master Resource)
❌ Skip validation checklists
❌ Introduce new dependencies without approval

---

## 🐛 Common Issues

### TypeScript Errors
- **Issue**: "Cannot find module '../utils/logger'"
- **Fix**: Verify directory structure matches project
- **Reference**: [TASK-1 Troubleshooting](TASK-1-API-CLIENT-SETUP.md#troubleshooting)

### Bundle Size Too Large
- **Issue**: Bundle > 550 KB
- **Fix**: Check for duplicate dependencies, verify tree-shaking
- **Reference**: [TASK-7 Bundle Validation](TASK-7-TESTING-DEPLOYMENT.md#step-2-bundle-size-validation)

### API Authentication Fails
- **Issue**: Cannot retrieve access token
- **Fix**: Try multiple context properties (userSettings, page)
- **Reference**: [Master Resource Auth Flow](SPRINT-7-MASTER-RESOURCE.md#authentication-flow)

---

## 📞 Support

### Questions?
1. Check task file troubleshooting section
2. Review Master Resource for detailed specs
3. Check background documentation (Sprint 2, 5B)
4. Consult with project team

### Updates?
- Document any deviations from plan
- Update validation checklists as you progress
- Note any blockers or risks

---

**Last Updated**: 2025-10-05
**Sprint Status**: Ready to Begin
**Next Action**: Start [TASK-1-API-CLIENT-SETUP.md](TASK-1-API-CLIENT-SETUP.md)
