# SDAP Development Scripts

**Purpose:** Utility scripts for SDAP development, deployment, and operations.

**Last Updated:** December 2, 2025

---

## Script Registry

This registry tracks all scripts in this directory, their purpose, usage frequency, and lifecycle status.

### Legend

**Usage Frequency:**
- 🟢 **Active** - Used regularly (weekly/monthly)
- 🟡 **Occasional** - Used as needed (quarterly/annually)
- 🔴 **One-time** - Historical/setup scripts (rarely used)

**Lifecycle Status:**
- ✅ **Maintained** - Actively maintained and updated
- ⚠️ **Deprecated** - Superseded by better tooling, kept for reference
- 📦 **Archive** - One-time setup scripts, kept for new environments

---

## Active Development Scripts

### PCF & Custom Page Deployment

#### `Deploy-PCFWebResources.ps1`
**Purpose:** Deploy PCF control web resources to Dataverse
**Usage:** 🟢 Active - Deploy after PCF build
**Lifecycle:** ✅ Maintained
**Dependencies:** PAC CLI, Dataverse connection
**Owner:** Development Team
**Last Used:** Phase 8 (File Viewer deployment)

**When to Use:**
- After building PCF control (`npm run build`)
- Deploying updates to existing controls
- Testing PCF changes in Dataverse environment

**Command:**
```powershell
.\Deploy-PCFWebResources.ps1 -ControlName "UniversalQuickCreate" -Environment "dev"
```

**Alternatives:**
- PAC CLI: `pac pcf push`
- Power Platform Build Tools (CI/CD)

---

#### `Deploy-CustomPage.ps1`
**Purpose:** Deploy custom pages to Dataverse solution
**Usage:** 🟡 Occasional - Deploy new/updated custom pages
**Lifecycle:** ✅ Maintained
**Dependencies:** PAC CLI, solution context
**Owner:** Development Team
**Last Used:** Phase 7 (Custom page implementation)

**When to Use:**
- Creating new custom pages
- Updating custom page metadata
- Deploying custom page bundles

**Command:**
```powershell
.\Deploy-CustomPage.ps1 -PageName "UniversalDocumentUpload" -SolutionName "SpaarkeCore"
```

---

#### `create-custom-page.ps1`
**Purpose:** Scaffold new custom page structure
**Usage:** 🔴 One-time - Create new custom page
**Lifecycle:** 📦 Archive (generator script)
**Dependencies:** None
**Owner:** Development Team

**When to Use:**
- Starting new custom page feature
- Generating boilerplate for custom pages

**Command:**
```powershell
.\create-custom-page.ps1 -Name "MyNewPage" -OutputPath "./src/pages"
```

---

## Testing & Validation Scripts

### `Test-SdapBffApi.ps1`
**Purpose:** Test SDAP BFF API endpoints (health, auth, file operations)
**Usage:** 🟢 Active - Validate API after deployment
**Lifecycle:** ✅ Maintained
**Dependencies:** Azure CLI (az login), API deployed
**Owner:** Development Team

**When to Use:**
- After API deployment
- Troubleshooting API issues
- Validating configuration changes

**Command:**
```powershell
.\Test-SdapBffApi.ps1 -Environment "dev" -Verbose
```

**Tests:**
- Health endpoint
- Authentication flow
- File upload/download
- OBO token exchange

---

### `test-sdap-api-health.js`
**Purpose:** Node.js health check for SDAP API
**Usage:** 🟡 Occasional - Quick health validation
**Lifecycle:** ✅ Maintained
**Dependencies:** Node.js
**Owner:** Development Team

**When to Use:**
- Quick API health check
- Monitoring scripts
- Integration tests

**Command:**
```bash
node test-sdap-api-health.js https://your-api-url.azurewebsites.net
```

---

### `test-dataverse-connection.cs`
**Purpose:** Test Dataverse ServiceClient connectivity
**Usage:** 🔴 One-time - Troubleshoot connection issues
**Lifecycle:** 📦 Archive (diagnostic)
**Dependencies:** .NET 8, Dataverse SDK
**Owner:** Development Team

**When to Use:**
- Debugging Dataverse connection problems
- Validating ServiceClient configuration
- Testing authentication

**Command:**
```bash
dotnet run test-dataverse-connection.cs
```

---

## SharePoint Embedded Setup Scripts

**Status:** 📦 Archive - One-time environment setup

These scripts were used during initial SPE Container Type registration and are kept for reference when setting up new environments.

### Container Type Management

#### `Create-ContainerType-PowerShell.ps1`
**Purpose:** Create SPE Container Type using PowerShell
**Usage:** 🔴 One-time - Initial setup
**Lifecycle:** 📦 Archive
**Last Used:** Phase 1 setup

**When to Use:**
- Setting up new SPE environment
- Creating additional Container Types

---

#### `Create-NewContainerType.ps1`
**Purpose:** Alternative Container Type creation
**Usage:** 🔴 One-time
**Lifecycle:** 📦 Archive
**Note:** Prefer Microsoft's official tooling

---

#### `Check-ContainerType-Registration.ps1`
**Purpose:** Verify Container Type registration status
**Usage:** 🔴 One-time - Validate setup
**Lifecycle:** 📦 Archive

---

### Registration & Authentication

#### `Register-BffApi-WithCertificate.ps1`
**Purpose:** Register BFF API with SPE using certificate authentication
**Usage:** 🔴 One-time - Initial setup
**Lifecycle:** 📦 Archive
**Last Used:** Phase 1 SPE integration

**When to Use:**
- Initial SPE registration
- Certificate renewal
- New environment setup

---

#### `Register-BffApi-WithCertificate-Direct.ps1`
**Purpose:** Direct certificate registration (alternative approach)
**Usage:** 🔴 One-time
**Lifecycle:** 📦 Archive

---

#### `Register-BffApi-PnP.ps1`
**Purpose:** Register using PnP PowerShell
**Usage:** 🔴 One-time
**Lifecycle:** 📦 Archive
**Note:** Superseded by direct Graph API approach

---

#### `Register-BffApiWithContainerType.ps1`
**Purpose:** Link BFF API registration to Container Type
**Usage:** 🔴 One-time
**Lifecycle:** 📦 Archive

---

#### `Import-And-Register.ps1`
**Purpose:** Import certificate and perform registration
**Usage:** 🔴 One-time
**Lifecycle:** 📦 Archive

---

### Diagnostics & Troubleshooting

#### `Find-ContainerTypeOwner.ps1`
**Purpose:** Find Container Type owning application
**Usage:** 🔴 One-time - Troubleshooting
**Lifecycle:** 📦 Archive (diagnostic)

---

#### `Find-ContainerTypeOwner-AzCli.ps1`
**Purpose:** Find owner using Azure CLI
**Usage:** 🔴 One-time
**Lifecycle:** 📦 Archive

---

#### `Get-ContainerMetadata.ps1`
**Purpose:** Retrieve Container Type metadata
**Usage:** 🔴 One-time
**Lifecycle:** 📦 Archive

---

#### `Get-ContainerMetadata-PCFApp.ps1`
**Purpose:** Get metadata from PCF app context
**Usage:** 🔴 One-time
**Lifecycle:** 📦 Archive

---

#### `Debug-RegistrationFailure.ps1`
**Purpose:** Diagnose SPE registration failures
**Usage:** 🔴 One-time
**Lifecycle:** 📦 Archive

---

#### `Decode-SharePointPermissions.ps1`
**Purpose:** Decode SharePoint permission strings
**Usage:** 🔴 One-time
**Lifecycle:** 📦 Archive

---

#### `Test-SharePointToken.ps1`
**Purpose:** Test SharePoint token acquisition
**Usage:** 🔴 One-time
**Lifecycle:** 📦 Archive

---

## Script Maintenance

### Adding New Scripts

**Process:**
1. Create script with clear naming convention
2. Add entry to this README
3. Include inline documentation (synopsis, parameters, examples)
4. Tag with usage frequency and lifecycle status
5. Update when usage patterns change

**Naming Convention:**
- **Action-Target-Method.ps1** (e.g., `Deploy-PCFWebResources.ps1`)
- Use PascalCase for PowerShell scripts
- Use kebab-case for JavaScript/Node scripts

### Deprecating Scripts

**When to deprecate:**
- Superseded by better tooling (PAC CLI, official tools)
- No longer relevant to current architecture
- One-time setup completed

**Process:**
1. Update script header with deprecation notice
2. Mark as ⚠️ Deprecated in this README
3. Document replacement tool/process
4. After 6 months with no usage: move to `archive/` or delete

### Archiving Scripts

**Criteria:**
- One-time setup scripts (SPE registration, etc.)
- Historical reference value
- May be needed for new environments

**Process:**
1. Mark as 📦 Archive in this README
2. Keep in `scripts/` folder for easy discovery
3. Note when it was last used

### Removing Scripts

**Criteria:**
- No historical value
- Can be recreated from documentation
- Superseded with no reference value

**Process:**
1. Verify in git history (permanent record)
2. Remove from `scripts/` folder
3. Remove from this README
4. Update any references in documentation

---

## Usage Patterns

### By Development Phase

**Development:**
- `Deploy-PCFWebResources.ps1` - After PCF changes
- `Test-SdapBffApi.ps1` - Validate API changes

**Deployment:**
- `Deploy-CustomPage.ps1` - Deploy custom pages
- CI/CD workflows (preferred over manual scripts)

**Troubleshooting:**
- `test-sdap-api-health.js` - Quick health checks
- `Test-SdapBffApi.ps1` - Comprehensive API validation

**New Environment Setup:**
- SPE Container Type scripts (one-time)
- Registration scripts (one-time)

### By Tool Category

**Deployment Tools:**
- PCF deployment scripts
- Custom page deployment scripts

**Testing Tools:**
- API health checks
- Connection validation scripts

**Setup Tools:**
- SPE registration scripts
- Container Type creation scripts

**Diagnostic Tools:**
- Debug and troubleshooting scripts
- Metadata inspection scripts

---

## Script Dependencies

### Required Tools

**PowerShell Scripts:**
- PowerShell 7+ (cross-platform)
- Azure CLI (`az`)
- PAC CLI (Power Platform CLI)
- .NET 8 SDK

**JavaScript/Node Scripts:**
- Node.js 18+
- npm packages (per script)

**C# Scripts:**
- .NET 8 SDK
- Required NuGet packages

### Credentials & Permissions

Most scripts require:
- Azure AD authentication (`az login`)
- Power Platform admin consent
- Appropriate RBAC roles:
  - Dataverse System Administrator
  - Azure AD Application Administrator (for SPE setup)
  - SharePoint Embedded Administrator

---

## Migration to Better Tooling

### Recommended Replacements

**Instead of custom deployment scripts:**
- Use **PAC CLI** directly
- Use **Power Platform Build Tools** in Azure DevOps/GitHub Actions
- Use **Solution Packager** for solution management

**Instead of custom testing scripts:**
- Use **automated test suites** (xUnit, Playwright)
- Use **API testing frameworks** (Postman, REST Client)
- Use **monitoring tools** (Application Insights, Azure Monitor)

**Instead of setup scripts:**
- Document setup in `docs/setup/`
- Use **Infrastructure as Code** (Bicep, ARM templates)
- Use **official Microsoft tooling** when available

---

## Related Documentation

- **Development Procedures:** `docs/development/`
- **Deployment Guide:** `docs/deployment/`
- **SPE Setup Guide:** `docs/setup/spe-setup.md`
- **API Documentation:** `docs/api/`

---

## Change Log

- **2025-12-02:** Initial script registry created. Removed 21 scripts (deprecated, bash, docs). Established maintenance process.
