# Sprint 3: PCF Migration & Production Deployment

**Status:** 🔄 Planning
**Sprint Goal:** Migrate to PCF control, automate container creation, deploy to Azure
**Estimated Effort:** 80-110 hours

---

## 📋 Sprint 3 Objectives

### Primary Goals

1. **Container Creation Automation** - Power Platform plugin to automate SPE container creation with correct ID format
2. **PCF File Management Control** - Modern React/TypeScript component for all file operations
3. **Azure Deployment** - Production infrastructure setup and CI/CD pipelines

### Secondary Goals

4. **Large File Upload** - Resumable upload for files > 4MB
5. **Command Bar Integration** - Ribbon buttons for file operations
6. **Document Versioning** - Version history and rollback capabilities

---

## 🎯 Proposed Tasks

### High Priority

#### Task 3.1: Container Creation Plugin
**Effort:** 8-12 hours
**Dependencies:** Sprint 2 complete

**Objective:** Automate SPE container creation when Dataverse Container records are created

**Deliverables:**
- Power Platform plugin (PreCreate on sprk_container)
- Calls BFF API to create SPE container
- Updates sprk_specontainerid with correct Graph API format (b!...)
- Follows ADR-003 thin plugin pattern
- Strong-name signed and deployed

**Success Criteria:**
- Container records automatically get correct SPE Container ID
- No manual ID entry required
- File operations work without workaround

#### Task 3.2: PCF File Management Control
**Effort:** 24-32 hours
**Dependencies:** Sprint 2 complete

**Objective:** Create modern reusable component for file management across all Power Platform touchpoints

**Deliverables:**
- **Field Control PCF** - Bind to sprk_documentid lookup for single file
- **Dataset Control PCF** - Show as subgrid for related documents
- React/TypeScript implementation
- Drag-drop upload UI
- Progress indicators
- File preview/thumbnails
- Configurable modes (single/multi file)

**Success Criteria:**
- Works in main forms, subgrids, and custom pages
- Better UX than JavaScript web resource
- Reusable across all scenarios

#### Task 3.3: Azure Deployment & DevOps
**Effort:** 16-24 hours
**Dependencies:** Sprint 2 complete

**Objective:** Deploy BFF API and background services to Azure with CI/CD

**Deliverables:**
- Azure App Service deployment (DEV/UAT/PROD)
- Key Vault integration for secrets
- Managed Identity configuration
- CI/CD pipeline (GitHub Actions or Azure DevOps)
- Environment-specific configuration
- Update JavaScript/PCF with production URLs

**Success Criteria:**
- APIs accessible from Power Platform
- Automated deployments working
- Secrets secured in Key Vault
- Multi-environment support

### Medium Priority

#### Task 3.4: Large File Upload Enhancement
**Effort:** 12-16 hours
**Dependencies:** Task 3.2 (PCF Control)

**Objective:** Support files > 4MB with resumable upload

**Deliverables:**
- Chunked upload with retry
- Progress tracking
- Client-side file validation
- Integration with PCF control

#### Task 3.5: Command Bar Integration
**Effort:** 4-6 hours
**Dependencies:** Task 3.2 (PCF Control)

**Objective:** Add ribbon buttons for file operations

**Deliverables:**
- Upload/Download/Replace/Delete buttons
- Enable rules based on context
- Integration with PCF control

### Low Priority

#### Task 3.6: Document Versioning
**Effort:** 16-20 hours
**Dependencies:** Task 3.2 (PCF Control)

**Objective:** Track file versions with rollback capability

**Deliverables:**
- Version history entity
- Rollback functionality
- Version comparison UI

---

## 📊 Sprint Planning

### Critical Path
1. Container Creation Plugin (8-12h)
2. PCF File Management Control (24-32h)
3. Azure Deployment (16-24h)

**Minimum Sprint 3:** 48-68 hours
**Full Sprint 3:** 80-110 hours

### Parallel Work Opportunities
- Task 3.1 (Plugin) can run in parallel with Task 3.3 (Deployment)
- Task 3.4 and 3.5 can run after Task 3.2 completes

---

## ⚠️ Known Issues from Sprint 2

See [Sprint 2 Wrap-Up Report](../Sprint 2/SPRINT-2-WRAP-UP-REPORT.md) for full details.

### 1. SPE Container ID Format (CRITICAL)
- **Issue:** Manual entry of incorrect format
- **Sprint 3 Solution:** Task 3.1 (Container Creation Plugin)

### 2. Development Environment URL (MEDIUM)
- **Issue:** JavaScript points to localhost
- **Sprint 3 Solution:** Task 3.3 (Azure Deployment)

### 3. JavaScript vs PCF (LOW)
- **Issue:** Limited UI capabilities
- **Sprint 3 Solution:** Task 3.2 (PCF Migration)

---

## 📚 Reference Documentation

- [Sprint 2 Wrap-Up Report](../Sprint 2/SPRINT-2-WRAP-UP-REPORT.md)
- [ADR-003: Power Platform Plugin Guardrails](../../adrs/ADR-003-Power-Platform-Plugin-Guardrails.md)
- [PCF Documentation](https://docs.microsoft.com/en-us/power-apps/developer/component-framework/overview)

---

**Next Action:** Create detailed task files for Sprint 3.1, 3.2, and 3.3
