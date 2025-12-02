# SPE File Viewer - Implementation Guide

**Version**: 1.0
**Created**: 2025-01-21
**Status**: Ready for Implementation

---

## 📋 Overview

This guide provides step-by-step instructions for implementing the SharePoint Embedded (SPE) File Viewer solution in Spaarke. The solution enables users to view and edit SharePoint Embedded files directly within Dataverse Document forms using a modern PCF control.

**Key Features**:
- ✅ Server-side authentication (no MSAL.js complexity)
- ✅ Office Online integration (editable files)
- ✅ Auto-refresh before URL expiration
- ✅ App-only authentication (simplified security)
- ✅ Full audit logging with correlation IDs
- ✅ PCF control (ADR-006 compliant)
- ✅ Thin plugin proxy (ADR-001 compliant)

**Repository Structure**: See **[REPOSITORY-STRUCTURE.md](REPOSITORY-STRUCTURE.md)** for complete organization and file locations.

---

## 🏗️ Architecture Summary

```
┌──────────────────────────────────────────────────────────────────┐
│                        User's Browser                             │
│  ┌────────────────────────────────────────────────────────────┐  │
│  │  Dataverse Document Form                                    │  │
│  │  ┌──────────────────────────────────────────────────────┐  │  │
│  │  │  PCF Control (SpeFileViewer)                         │  │  │
│  │  │  - React + Fluent UI v9                             │  │  │
│  │  │  - Calls Custom API                                  │  │  │
│  │  │  - Displays iframe with preview URL                  │  │  │
│  │  │  - Auto-refreshes before expiration                  │  │  │
│  │  └──────────────────────────────────────────────────────┘  │  │
│  └────────────────────────────────────────────────────────────┘  │
└──────────────────────┬───────────────────────────────────────────┘
                       │ Xrm.WebApi.execute()
                       ▼
┌──────────────────────────────────────────────────────────────────┐
│                       Dataverse                                   │
│  ┌────────────────────────────────────────────────────────────┐  │
│  │  Custom API: sprk_GetFilePreviewUrl                        │  │
│  │  - Bound to sprk_document entity                          │  │
│  │  - No input params (uses entity ID)                       │  │
│  │  - Returns: PreviewUrl, FileName, ExpiresAt, etc.         │  │
│  └────────────────┬───────────────────────────────────────────┘  │
│                   │ Triggers                                      │
│                   ▼                                               │
│  ┌────────────────────────────────────────────────────────────┐  │
│  │  Plugin: GetFilePreviewUrlPlugin                           │  │
│  │  - Thin proxy (no Graph logic)                            │  │
│  │  - Validates inputs                                        │  │
│  │  - Calls SDAP BFF API with app-only token                │  │
│  │  - Returns ephemeral preview URL                          │  │
│  │  - Logs to audit table                                    │  │
│  └────────────────┬───────────────────────────────────────────┘  │
└────────────────────┼───────────────────────────────────────────┘
                     │ HTTPS + Bearer token
                     ▼
┌──────────────────────────────────────────────────────────────────┐
│                    SDAP BFF API (Azure)                           │
│  ┌────────────────────────────────────────────────────────────┐  │
│  │  Endpoint: /api/documents/{id}/preview-url                 │  │
│  │  - Extracts userId from JWT claims                        │  │
│  │  - Validates access via Spaarke UAC                       │  │
│  │  - Calls Graph API with service principal (app-only)      │  │
│  │  - Returns ephemeral preview URL                          │  │
│  └────────────────┬───────────────────────────────────────────┘  │
└────────────────────┼───────────────────────────────────────────┘
                     │ App-only access token
                     ▼
┌──────────────────────────────────────────────────────────────────┐
│              Microsoft Graph API                                  │
│  POST /drives/{driveId}/items/{itemId}/preview                   │
│  - Returns ephemeral preview URL (expires ~10 min)               │
│  - No per-user ACLs needed (SPE is headless)                    │
└──────────────────────────────────────────────────────────────────┘
```

**Authentication Flow**:
1. Plugin uses **app-only service principal** to call BFF API
2. BFF API validates user access via **Spaarke UAC** (not SPE)
3. BFF API uses **app-only service principal** to call Graph API
4. Graph API returns **ephemeral preview URL** (expires ~10 min)
5. PCF control displays file in iframe
6. Auto-refresh scheduled 1 minute before expiration

---

## 📚 Implementation Steps

Follow these step documents **in order**:

### Step 1: Backend Updates (~2 hours)
**File**: [STEP-1-BACKEND-UPDATES.md](STEP-1-BACKEND-UPDATES.md)

**What You'll Do**:
- Verify/create SpeFileStore service
- Update BFF endpoint (route change, UAC validation)
- Update plugin (rename, simplify to thin proxy)
- Build and test plugin assembly

**Key Changes**:
- Route: `/preview` → `/preview-url`
- Plugin: `GetDocumentFileUrlPlugin` → `GetFilePreviewUrlPlugin`
- Add correlation ID support
- Add UAC validation to BFF

**Deliverables**:
- Updated plugin DLL
- Updated SDAP BFF API code

---

### Step 2: Custom API Registration (~1 hour)
**File**: [STEP-2-CUSTOM-API-REGISTRATION.md](STEP-2-CUSTOM-API-REGISTRATION.md)

**What You'll Do**:
- Create External Service Config record (SDAP_BFF_API)
- Register plugin assembly in Dataverse
- Create Custom API record (sprk_GetFilePreviewUrl)
- Create 6 output parameters
- Register plugin step
- Publish customizations

**Deliverables**:
- Custom API available in Dataverse
- Plugin registered and active
- External Service Config configured

---

### Step 3: PCF Control Development (~3 hours)
**File**: [STEP-3-PCF-CONTROL-DEVELOPMENT.md](STEP-3-PCF-CONTROL-DEVELOPMENT.md)

**What You'll Do**:
- Create PCF project with React template
- Install Fluent UI v9 dependencies
- Implement React components (FileViewer, LoadingSpinner, ErrorMessage)
- Create CustomApiService for API calls
- Build and test control locally
- Package solution for deployment

**Tech Stack**:
- React 17.x + TypeScript
- Fluent UI v9 (React components)
- PCF Framework
- Xrm.WebApi

**Deliverables**:
- PCF control solution package (.zip)
- Tested in local test harness

---

### Step 4: Deployment and Integration (~1.5 hours)
**File**: [STEP-4-DEPLOYMENT-INTEGRATION.md](STEP-4-DEPLOYMENT-INTEGRATION.md)

**What You'll Do**:
- Deploy SDAP BFF API to Azure
- Import PCF solution to Dataverse
- Add PCF control to Document form
- Configure control properties
- Publish form customizations

**Deliverables**:
- SDAP BFF API deployed to Azure
- PCF control available in Dataverse
- Document form updated with file viewer

---

### Step 5: Testing and Validation (~2 hours)
**File**: [STEP-5-TESTING.md](STEP-5-TESTING.md)

**What You'll Do**:
- End-to-end functional testing
- Custom API direct testing
- Audit log verification
- Performance testing
- Error scenario testing
- Security validation

**Test Coverage**:
- 25+ test cases
- Functional, performance, security, error handling
- User acceptance testing

**Deliverables**:
- Test report
- Sign-off for production deployment

---

## ⏱️ Time Estimate

| Phase | Duration | Cumulative |
|-------|----------|------------|
| Step 1: Backend Updates | 2 hours | 2 hours |
| Step 2: Custom API Registration | 1 hour | 3 hours |
| Step 3: PCF Control Development | 3 hours | 6 hours |
| Step 4: Deployment | 1.5 hours | 7.5 hours |
| Step 5: Testing | 2 hours | 9.5 hours |
| **Total** | **~10 hours** | **~10 hours** |

**Note**: Times are estimates for experienced developers. First-time implementation may take longer.

---

## 📖 Reference Documents

### Design and Architecture
- **[IMPLEMENTATION-PLAN-FILE-VIEWER.md](IMPLEMENTATION-PLAN-FILE-VIEWER.md)** - Comprehensive implementation plan
- **[GPT-DESIGN-FEEDBACK-FILE-VIEWER.md](GPT-DESIGN-FEEDBACK-FILE-VIEWER.md)** - Authoritative design guidance (app-only auth)
- **[TECHNICAL-SUMMARY-FILE-VIEWER-SOLUTION.md](TECHNICAL-SUMMARY-FILE-VIEWER-SOLUTION.md)** - Original technical summary

### Existing Documentation
- **[DEPLOYMENT-STEPS-CUSTOM-API.md](DEPLOYMENT-STEPS-CUSTOM-API.md)** - Original deployment guide
- **[CUSTOM-API-FILE-ACCESS-SOLUTION.md](CUSTOM-API-FILE-ACCESS-SOLUTION.md)** - Solution overview
- **[CUSTOM-API-REGISTRATION.md](CUSTOM-API-REGISTRATION.md)** - Registration guide

### ADRs (Architectural Decision Records)
- **ADR-001**: Avoid plugins unless only reasonable option and narrowly purposed ✅
- **ADR-006**: Prefer PCF controls over web resources ✅
- **ADR-003**: Authorization via Spaarke UAC (not SPE) ✅
- **ADR-005**: Flat storage model ✅
- **ADR-007**: Single facade pattern (BFF API) ✅
- **ADR-008**: Endpoint authentication ✅

---

## ✅ Prerequisites

Before starting, ensure you have:

### Tools
- [ ] Visual Studio Code or Visual Studio 2022
- [ ] .NET SDK 6.0+
- [ ] Node.js 16+ and npm
- [ ] PAC CLI (PowerApps CLI)
- [ ] Azure CLI
- [ ] PowerShell 7+ with Microsoft.Xrm.Data.PowerShell module
- [ ] Plugin Registration Tool

### Access
- [ ] Access to **SPAARKE DEV 1** Dataverse environment
- [ ] Admin rights to register plugins and Custom APIs
- [ ] Access to Azure subscription (for BFF API deployment)
- [ ] Access to Azure AD (for service principal creation)

### Knowledge
- [ ] Familiarity with Dataverse Custom APIs
- [ ] Experience with PCF control development
- [ ] Understanding of React and TypeScript
- [ ] Knowledge of Azure App Service deployment
- [ ] Understanding of OAuth 2.0 app-only authentication

---

## 🔐 Security Considerations

### App-Only Authentication
- Plugin uses **service principal** (not user context)
- BFF API uses **service principal** to call Graph API
- **Spaarke UAC** enforces user-level access control
- SPE is headless - no per-user ACLs needed

### Ephemeral URLs
- Preview URLs expire in ~10 minutes
- Auto-refresh ensures continuity
- Expired URLs cannot be reused

### Audit Logging
- All Custom API calls logged to `sprk_proxyauditlog`
- Correlation IDs enable request tracing
- Sensitive data redacted in logs

### CORS Protection
- BFF API only accepts requests from `*.dynamics.com` and `*.powerapps.com`

---

## 🐛 Troubleshooting

### Common Issues

Each step document includes a "Common Issues" section. Quick reference:

| Issue | Step | Solution |
|-------|------|----------|
| Build fails with NU1008 | Step 1 | Disable Directory.Packages.props temporarily |
| "External service config not found" | Step 2 | Verify SDAP_BFF_API record exists and is enabled |
| PCF control won't build | Step 3 | Run `npm install` and verify tsconfig.json |
| Custom API not found | Step 4 | Publish customizations, clear browser cache |
| "Unable to Load File Preview" | Step 5 | Check Custom API registration and BFF API deployment |

### Debug Tools

**Browser Console**:
```javascript
// Enable verbose logging
localStorage.setItem('DEBUG', 'true');

// Check Custom API response
// See console logs from CustomApiService
```

**Plugin Trace Logs**:
- Navigate to **Settings** → **Plug-in Trace Log** in Dataverse
- Filter by `sprk_GetFilePreviewUrl` message
- Check for errors and correlation IDs

**Azure Application Insights**:
- Monitor SDAP BFF API performance
- Query logs by correlation ID
- Set up alerts for errors

---

## 📞 Support and Questions

If you encounter issues during implementation:

1. **Check step document**: Each step has troubleshooting section
2. **Review reference docs**: Especially GPT-DESIGN-FEEDBACK-FILE-VIEWER.md
3. **Check audit logs**: Use correlation IDs to trace requests
4. **Review plugin traces**: Look for detailed error messages
5. **Test Custom API directly**: Isolate the issue (Step 5, Task 5.2)

---

## 🎯 Success Criteria

The implementation is **COMPLETE** when:

- ✅ All 5 steps executed successfully
- ✅ All validation checklists passed
- ✅ End-to-end testing successful (Step 5)
- ✅ User acceptance testing passed
- ✅ Performance meets SLA (< 3 second load)
- ✅ Security validation passed
- ✅ Documentation updated
- ✅ No critical or high severity bugs

---

## 🚀 Getting Started

1. **Read this guide completely** to understand the overall approach
2. **Review reference documents** to understand the architecture
3. **Verify prerequisites** are met
4. **Start with Step 1** and follow in sequence
5. **Complete validation checklists** at each step
6. **Test thoroughly** before moving to next step

---

## 📝 Implementation Checklist

Track your progress:

- [ ] **Step 1: Backend Updates**
  - [ ] SpeFileStore service verified/created
  - [ ] BFF endpoint updated
  - [ ] Plugin updated and built
  - [ ] All validations passed

- [ ] **Step 2: Custom API Registration**
  - [ ] External Service Config created
  - [ ] Plugin assembly registered
  - [ ] Custom API created
  - [ ] All 6 parameters created
  - [ ] Plugin step registered
  - [ ] Customizations published
  - [ ] Browser console test passed

- [ ] **Step 3: PCF Control Development**
  - [ ] PCF project created
  - [ ] Dependencies installed
  - [ ] Components implemented
  - [ ] Control builds successfully
  - [ ] Local test harness works
  - [ ] Solution package created

- [ ] **Step 4: Deployment**
  - [ ] SDAP BFF API deployed to Azure
  - [ ] PCF solution imported to Dataverse
  - [ ] Control added to Document form
  - [ ] Form published
  - [ ] User acceptance test passed

- [ ] **Step 5: Testing**
  - [ ] Functional tests passed (8/8)
  - [ ] API tests passed (3/3)
  - [ ] Audit tests passed (2/2)
  - [ ] Performance tests passed (4/4)
  - [ ] Error tests passed (5/5)
  - [ ] Security tests passed (3/3)
  - [ ] Test report completed

---

## 🎉 Ready to Begin!

You now have everything you need to implement the SPE File Viewer solution. Start with [STEP-1-BACKEND-UPDATES.md](STEP-1-BACKEND-UPDATES.md) and work through each phase systematically.

**Good luck with the implementation!** 🚀

---

**Document Version**: 1.0
**Last Updated**: 2025-01-21
**Status**: Ready for Implementation
