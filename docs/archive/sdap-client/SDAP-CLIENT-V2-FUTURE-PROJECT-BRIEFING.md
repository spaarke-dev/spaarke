# SDAP Shared Client - Future Project Briefing

**Package:** `@spaarke/sdap-client`
**Current Status:** ✅ Built, Tested & Documented | ⏸️ Awaiting Integration
**Created:** October 4, 2025
**Documented:** December 2, 2025

---

## Quick Start (For Future Developers)

**If you're picking up this project in the future, start here:**

### What is This?

A **reusable TypeScript library** for file operations (upload, download, delete) that works across:
- ✅ PCF Controls (Dataverse)
- ✅ Office.js Add-ins (Word/Excel/PowerPoint)
- ✅ Web Applications (React/Angular/Vue)

### Why Does This Exist?

**Problem:** Each PCF control implements its own HTTP client for the BFF API (~300-400 lines each)
**Solution:** Centralize into a single, tested, platform-agnostic npm package

### Key Features

| Feature | Benefit |
|---------|---------|
| **Chunked Upload** | Upload files up to 250GB (320KB chunks) |
| **Progress Tracking** | Updates every 320KB (not just per-file) |
| **Platform Agnostic** | Works in browsers, no PCF/Dataverse dependencies |
| **Type Safe** | Full TypeScript definitions |
| **Tested** | 80%+ coverage with Jest |
| **Zero Dependencies** | Pure browser APIs (fetch, Blob, File) |

---

## Current State (December 2025)

### Files & Documentation

```
packages/sdap-client/               ← CURRENT LOCATION (orphaned)
├── src/                           # Source code (~15KB)
│   ├── auth/TokenProvider.ts      # Token abstraction
│   ├── operations/                # Upload, Download, Delete
│   ├── types/index.ts             # TypeScript types
│   ├── SdapApiClient.ts           # Main client class
│   └── __tests__/                 # Unit tests
├── dist/                          # Compiled output
├── docs/                          # 📚 YOU ARE HERE
│   ├── PACKAGE-OVERVIEW.md        # Complete overview (what/why/how)
│   ├── INTEGRATION-GUIDE.md       # How to use in each platform
│   ├── MIGRATION-PLAN.md          # Step-by-step migration from custom clients
│   └── FUTURE-PROJECT-BRIEFING.md # This document
├── package.json
├── README.md
└── spaarke-sdap-client-1.0.0.tgz  # npm package (37KB)

PLANNED LOCATION:
src/shared/sdap-client/             ← MOVE HERE (in src/ repo)
```

### Status Summary

| Component | Status | Notes |
|-----------|--------|-------|
| **Package Code** | ✅ Complete | Built, tested, packaged |
| **Documentation** | ✅ Complete | 4 comprehensive guides |
| **Git Integration** | ❌ Not committed | Still in packages/ folder |
| **PCF Integration** | ❌ Not used | Custom clients still in use |
| **Phase 8 Support** | ❌ Missing | No preview/editor operations |

---

## Documentation Guide

### 📘 Read These Documents in Order

#### 1. [PACKAGE-OVERVIEW.md](./SDAP-CLIENT-V2-PACKAGE-OVERVIEW.md) - **START HERE**
**Read this if:** You need to understand what this package is, why it exists, and how it relates to the platform

**Contents:**
- Executive summary
- Architecture diagrams
- Current state analysis
- Use cases (current & future platforms)
- Relationship to existing PCF controls
- Future development roadmap
- Technical specifications
- File structure
- Testing & quality assurance

**Time to read:** 30 minutes

---

#### 2. [INTEGRATION-GUIDE.md](./SDAP-CLIENT-V2-INTEGRATION-GUIDE.md)
**Read this if:** You're ready to integrate the package into a platform (PCF, Office.js, web app)

**Contents:**
- PCF control integration pattern
- Office.js add-in integration
- React/Angular/Vue web app integration
- Token provider implementations
- Common patterns (cancellation, retry, error handling)
- Troubleshooting

**Time to read:** 20 minutes

---

#### 3. [MIGRATION-PLAN.md](./SDAP-CLIENT-V2-MIGRATION-PLAN.md)
**Read this if:** You're migrating Universal Quick Create or SpeFileViewer to use the shared package

**Contents:**
- Step-by-step migration for Universal Quick Create
- Code examples (before/after)
- Testing strategy
- Rollback plan
- Post-migration validation
- Metrics to track

**Time to read:** 45 minutes (includes hands-on steps)

---

#### 4. [FUTURE-PROJECT-BRIEFING.md](./SDAP-CLIENT-V2-FUTURE-PROJECT-BRIEFING.md) - **YOU ARE HERE**
**Read this if:** You're picking up this project months/years later and need a quick overview

**Contents:**
- Quick start guide
- Current state summary
- Next steps roadmap
- Known limitations
- Decision points

**Time to read:** 10 minutes

---

## Next Steps Roadmap

### Immediate Actions (This Sprint) - 2 hours

**Goal:** Preserve the package for future use

✅ **Done:** Documentation complete (4 comprehensive guides)

🔲 **TODO:**
1. Move package from `packages/sdap-client` → `src/shared/sdap-client`
2. Commit to Git with detailed commit message
3. Add to SDAP Architecture Guide (reference in SDAP-ARCHITECTURE-GUIDE-10-20-2025.md)

**Commands:**
```bash
# 1. Move package
mv c:\code_files\spaarke\packages\sdap-client c:\code_files\spaarke\src\shared\sdap-client

# 2. Commit to Git
cd c:\code_files\spaarke
git add src/shared/sdap-client
git commit -m "feat(shared): add SDAP shared client library for multi-platform file operations

Add @spaarke/sdap-client package (v1.0.0) - a platform-agnostic TypeScript library
for file operations (upload/download/delete) with the SDAP BFF API.

Features:
- Automatic chunked upload for files ≥ 4MB (320KB chunks)
- Progress tracking with callback-based reporting
- Platform-agnostic (PCF, Office.js, web apps)
- Zero dependencies (pure browser APIs)
- Full TypeScript definitions
- 80%+ test coverage

Future Use Cases:
- Universal Quick Create migration (remove 100MB limit)
- Office.js add-ins for Word/Excel/PowerPoint
- Standalone web applications (React/Angular/Vue)
- SpeFileViewer integration (after Phase 8 enhancements)

Documentation:
- PACKAGE-OVERVIEW.md: Complete overview and architecture
- INTEGRATION-GUIDE.md: Platform-specific integration patterns
- MIGRATION-PLAN.md: Step-by-step migration guide
- FUTURE-PROJECT-BRIEFING.md: Quick reference for future developers

Status: Built & tested, not yet integrated into PCF controls
Location: src/shared/sdap-client (moved from packages/)

🤖 Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude <noreply@anthropic.com>"

# 3. Push to GitHub
git push origin master
```

---

### Phase 2: Universal Quick Create Integration (Next Sprint) - 1 week

**Goal:** Remove 10 file / 100MB limit, add chunked upload

**Prerequisites:**
- ✅ Package moved to src/shared/ and committed
- ✅ Development environment ready
- ✅ Test Dataverse environment available

**Tasks:**
1. 🔲 Install package in Universal Quick Create
2. 🔲 Create PcfTokenProvider implementation
3. 🔲 Update FileUploadService to use shared package
4. 🔲 Update MultiFileUploadService
5. 🔲 Remove old SdapApiClient.ts (374 lines)
6. 🔲 Update type imports
7. 🔲 Build & test (unit + integration)
8. 🔲 Manual testing (10MB, 50MB, 100MB, 250MB files)
9. 🔲 Deploy to test environment
10. 🔲 Production deployment

**Success Criteria:**
- ✅ Upload 250MB file successfully
- ✅ Upload >10 files successfully
- ✅ Progress updates every 320KB
- ✅ No regression in existing functionality

**Estimated Effort:** 1 week (includes testing)

**See:** [MIGRATION-PLAN.md](./SDAP-CLIENT-V2-MIGRATION-PLAN.md) for detailed steps

---

### Phase 3: Add Phase 8 Support (Next Quarter) - 2 weeks

**Goal:** Enable SpeFileViewer migration by adding preview/editor operations

**Prerequisites:**
- ✅ Universal Quick Create migration complete
- ✅ Lessons learned documented

**Tasks:**
1. 🔲 Add PreviewOperation class
   ```typescript
   class PreviewOperation {
       async getPreviewUrl(documentId: string, correlationId: string): Promise<FilePreviewResponse>
       async getOfficeUrl(documentId: string, correlationId: string): Promise<OfficeUrlResponse>
   }
   ```

2. 🔲 Add correlation ID support to all operations
3. 🔲 Add RFC 7807 Problem Details error handling
4. 🔲 Add permission checking types (canEdit, role)
5. 🔲 Update tests
6. 🔲 Migrate SpeFileViewer to shared package
7. 🔲 Build, test, deploy

**Success Criteria:**
- ✅ SpeFileViewer uses shared package
- ✅ Preview and editor modes work
- ✅ Correlation ID tracking works
- ✅ Error handling preserved

**Estimated Effort:** 2 weeks

---

### Phase 4: Office.js Add-ins (6 months out) - 4 weeks

**Goal:** Create Word/Excel/PowerPoint add-ins using shared package

**Prerequisites:**
- ✅ Phase 3 complete (preview/editor support)
- ✅ Azure Artifacts feed configured (for npm publishing)

**Tasks:**
1. 🔲 Publish package to Azure Artifacts
2. 🔲 Create Office.js add-in scaffolds
3. 🔲 Implement OfficeTokenProvider
4. 🔲 Create task pane UIs
5. 🔲 Deploy add-ins to Microsoft 365

**Platforms:**
- Word: Upload contracts to Matters
- Excel: Upload financial reports to Invoices
- PowerPoint: Upload presentations to Projects

**Estimated Effort:** 4 weeks

---

### Phase 5: Web Portal (9 months out) - 6 weeks

**Goal:** Standalone React web application for external client access

**Prerequisites:**
- ✅ Phase 4 complete (Office.js proven)
- ✅ Package stable and battle-tested

**Tasks:**
1. 🔲 Create React/Next.js application
2. 🔲 Implement WebTokenProvider
3. 🔲 Create document management UI
4. 🔲 Deploy to Azure Static Web Apps

**Features:**
- Client portal for document access
- Drag-and-drop upload
- Document preview/download
- Mobile-responsive

**Estimated Effort:** 6 weeks

---

## Known Limitations & Gaps

### Current Limitations

| Limitation | Impact | Solution | Priority |
|------------|--------|----------|----------|
| No preview/editor operations | Can't migrate SpeFileViewer | Add PreviewOperation (Phase 3) | 🟡 Medium |
| No correlation ID support | Can't track requests end-to-end | Add header support | 🟡 Medium |
| No 401 retry logic | Auth failures not auto-recovered | Add retry with cache clear | 🟢 Low |
| No replaceFile() method | Can't replace files | Add method (delete + upload) | 🟢 Low |
| Token injection not ideal | Passing token to each method | Refactor to dependency injection | 🟢 Low |

### Missing Features (Not Blockers)

- Batch upload optimization
- Upload resume after network failure
- Compression before upload
- Virus scanning integration
- Duplicate file detection

---

## Decision Points

### Question 1: When Should We Migrate?

**Options:**

**A) Now (This Sprint)**
- ✅ Pro: Remove 100MB limit immediately
- ✅ Pro: Code consolidation now
- ❌ Con: Adds scope to current work

**B) Next Sprint**
- ✅ Pro: Focused migration effort
- ✅ Pro: More testing time
- ❌ Con: 100MB limit persists

**C) When We Need 100MB+ Files**
- ✅ Pro: Demand-driven
- ❌ Con: Reactive approach
- ❌ Con: Technical debt accumulates

**Recommendation:** **Option B (Next Sprint)** - Focused effort with proper testing

---

### Question 2: Should We Publish to Azure Artifacts Now?

**Options:**

**A) Yes, Publish Now**
- ✅ Pro: Proper npm workflow
- ✅ Pro: Versioning support
- ❌ Con: Overhead if not used soon

**B) No, Use Local Reference**
- ✅ Pro: Simpler for testing
- ✅ Pro: Faster iteration
- ❌ Con: Not scalable long-term

**Recommendation:** **Option B (Local Reference)** until Phase 4 (Office.js add-ins)

---

### Question 3: Should We Enhance for Phase 8 Now?

**Options:**

**A) Yes, Add Preview/Editor Now**
- ✅ Pro: Complete solution
- ✅ Pro: Migrate both controls together
- ❌ Con: 2-3 weeks extra work

**B) No, Wait Until Needed**
- ✅ Pro: Focus on Universal Quick Create first
- ✅ Pro: Learn from first migration
- ❌ Con: SpeFileViewer waits longer

**Recommendation:** **Option B (Wait)** - Prove value with Universal Quick Create first

---

## Technical Architecture Reference

### Core Classes

```typescript
// Main client
class SdapApiClient {
    constructor(config: SdapClientConfig)
    uploadFile(containerId, file, options): Promise<DriveItem>
    downloadFile(driveId, itemId): Promise<Blob>
    deleteFile(driveId, itemId): Promise<void>
    getFileMetadata(driveId, itemId): Promise<FileMetadata>
}

// Token abstraction (override per platform)
abstract class TokenProvider {
    abstract getToken(): Promise<string>
}

// Operations (internal)
class UploadOperation {
    uploadSmall(containerId, file): Promise<DriveItem>      // < 4MB
    uploadChunked(containerId, file): Promise<DriveItem>    // ≥ 4MB
}

class DownloadOperation {
    download(driveId, itemId): Promise<Blob>
}

class DeleteOperation {
    delete(driveId, itemId): Promise<void>
}
```

### API Endpoints Used

| Method | Endpoint | Purpose |
|--------|----------|---------|
| PUT | `/api/containers/{id}/files/{path}` | Upload file <4MB |
| POST | `/api/containers/{id}/upload` | Create upload session (≥4MB) |
| PUT | `{uploadUrl}` (from session) | Upload chunk (320KB) |
| GET | `/api/obo/drives/{id}/items/{id}/content` | Download file |
| DELETE | `/api/obo/drives/{id}/items/{id}` | Delete file |
| GET | `/api/obo/drives/{id}/items/{id}` | Get metadata |

---

## Contact & Support

### Questions?

**If you're picking up this project and have questions:**

1. **Read the docs first:**
   - Start with [PACKAGE-OVERVIEW.md](./SDAP-CLIENT-V2-PACKAGE-OVERVIEW.md)
   - Check [INTEGRATION-GUIDE.md](./SDAP-CLIENT-V2-INTEGRATION-GUIDE.md) for your platform
   - Review [MIGRATION-PLAN.md](./SDAP-CLIENT-V2-MIGRATION-PLAN.md) for migration steps

2. **Check the code:**
   - Source code is well-commented
   - Tests demonstrate usage patterns
   - Examples in each guide

3. **Check Git history:**
   ```bash
   git log --follow src/shared/sdap-client
   ```

4. **Check architecture docs:**
   - SDAP-ARCHITECTURE-GUIDE-10-20-2025.md (main architecture)
   - ADRs in `docs/adr/` (architectural decisions)

---

## Success Metrics (Post-Integration)

**Track these metrics after migration:**

| Metric | Before | After Target |
|--------|--------|--------------|
| Max file size | ~10MB | 250GB |
| Max files per upload | 10 | Unlimited |
| Max total upload size | 100MB | Unlimited |
| Custom client code | 664 lines | ~150 lines |
| Upload time (50MB) | N/A (failed) | <2 minutes |
| Progress granularity | Per-file | Every 320KB |
| Test coverage | 70% | 85%+ |
| Code duplication | High | Low |

---

## Final Checklist

**Before starting integration:**

- [ ] Read all 4 documentation files
- [ ] Understand current PCF control architecture
- [ ] Set up development environment
- [ ] Have test Dataverse environment ready
- [ ] Review BFF API endpoints
- [ ] Check MSAL authentication setup
- [ ] Plan testing strategy
- [ ] Prepare rollback plan

**After integration:**

- [ ] All tests pass (unit + integration)
- [ ] Manual testing complete (all file sizes)
- [ ] Documentation updated
- [ ] Architecture guide updated
- [ ] Deployment successful
- [ ] Metrics tracking in place
- [ ] Monitor for 30 days

---

**Document Version:** 1.0
**Last Updated:** December 2, 2025
**Status:** ✅ Complete - Package Fully Documented
**Next Step:** Move to `src/shared/` and commit to Git

---

## Quick Commands Reference

```bash
# Navigate to package
cd c:\code_files\spaarke\src\shared\sdap-client

# Install dependencies
npm install

# Build package
npm run build

# Run tests
npm test

# Run tests with coverage
npm test -- --coverage

# Lint code
npm run lint

# Create tarball
npm pack

# Install in PCF control (local)
cd ../client/pcf/UniversalQuickCreate
npm install ../../../shared/sdap-client

# Build PCF control
npm run build

# Test PCF control
npm test

# Deploy to Dataverse
pac pcf push --publisher-prefix sprk
```

---

**This concludes the SDAP Shared Client documentation package.**

All future developers have everything they need to understand, integrate, and extend this package. Good luck! 🚀
