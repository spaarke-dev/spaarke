# SPE File Viewer Project

**Project**: SharePoint Embedded File Viewer for Dataverse Documents
**Status**: Ready for Implementation
**Created**: 2025-01-21

---

## 📁 Project Structure

This directory contains all documentation for implementing the SPE File Viewer solution in Spaarke.

### 🚀 Start Here

1. **[REPOSITORY-STRUCTURE.md](REPOSITORY-STRUCTURE.md)** ← Review first!
   - Complete repository organization
   - File locations for all components
   - Directory structure and patterns

2. **[SPE-FILE-VIEWER-IMPLEMENTATION-GUIDE.md](SPE-FILE-VIEWER-IMPLEMENTATION-GUIDE.md)**
   - Master implementation guide
   - Architecture overview
   - Time estimates and prerequisites
   - Links to all step documents

3. **[QUICK-REFERENCE.md](QUICK-REFERENCE.md)** 📌 Keep handy!
   - All file paths in one place
   - Common commands and snippets
   - Testing code
   - Quick validation checklists

---

## 📋 Implementation Steps (Follow in Order)

1. **[STEP-1-BACKEND-UPDATES.md](STEP-1-BACKEND-UPDATES.md)** (~2 hours)
   - Update SpeFileStore service
   - Update BFF endpoint
   - Update and build plugin

2. **[STEP-2-CUSTOM-API-REGISTRATION.md](STEP-2-CUSTOM-API-REGISTRATION.md)** (~1 hour)
   - Create External Service Config
   - Register plugin in Dataverse
   - Create Custom API with parameters

3. **[STEP-3-PCF-CONTROL-DEVELOPMENT.md](STEP-3-PCF-CONTROL-DEVELOPMENT.md)** (~3 hours)
   - Create PCF project with React
   - Implement components with Fluent UI v9
   - Build and test locally

4. **[STEP-4-DEPLOYMENT-INTEGRATION.md](STEP-4-DEPLOYMENT-INTEGRATION.md)** (~1.5 hours)
   - Deploy BFF API to Azure
   - Import PCF to Dataverse
   - Configure Document form

5. **[STEP-5-TESTING.md](STEP-5-TESTING.md)** (~2 hours)
   - 25+ test cases
   - Performance validation
   - Security testing

---

## 📖 Reference Documents

### Design & Architecture
- **[IMPLEMENTATION-PLAN-FILE-VIEWER.md](IMPLEMENTATION-PLAN-FILE-VIEWER.md)** - Comprehensive implementation plan with all tasks, code examples, and knowledge references
- **[GPT-DESIGN-FEEDBACK-FILE-VIEWER.md](GPT-DESIGN-FEEDBACK-FILE-VIEWER.md)** - Authoritative design guidance (app-only auth, SPE architecture)
- **[TECHNICAL-SUMMARY-FILE-VIEWER-SOLUTION.md](TECHNICAL-SUMMARY-FILE-VIEWER-SOLUTION.md)** - Original technical analysis and MSAL.js problem explanation

### Additional Documentation
- **[CUSTOM-API-FILE-ACCESS-SOLUTION.md](CUSTOM-API-FILE-ACCESS-SOLUTION.md)** - Solution overview
- **[DEPLOYMENT-STEPS-CUSTOM-API.md](DEPLOYMENT-STEPS-CUSTOM-API.md)** - Original deployment guide

---

## 🎯 Quick Start

1. Read **SPE-FILE-VIEWER-IMPLEMENTATION-GUIDE.md** completely
2. Verify prerequisites are met
3. Follow **STEP-1** through **STEP-5** in sequence
4. Complete validation checklists at each step
5. Run comprehensive tests (STEP-5)

---

## 📊 Key Architecture Points

- **Authentication**: App-only service principal (not OBO/delegated)
- **Access Control**: Enforced by Spaarke UAC (not SPE)
- **URLs**: Ephemeral preview URLs that expire in ~10 minutes
- **Auto-Refresh**: PCF control refreshes URL before expiration
- **Audit Logging**: All requests logged with correlation IDs
- **ADR Compliance**: PCF over web resources, narrowly purposed plugin

---

## ⏱️ Total Time Estimate

~10 hours for experienced developers (first-time may take longer)

---

## ✅ Success Criteria

- All 5 implementation steps completed
- All validation checklists passed
- 25+ test cases passed
- User acceptance testing successful
- Performance SLA met (< 3 second load)
- Security validation passed

---

**Ready to implement!** Start with the implementation guide. 🚀
