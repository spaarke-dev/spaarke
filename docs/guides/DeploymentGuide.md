# Deployment Guide

Complete guide for deploying the Universal Dataset Grid PCF component to your Dataverse environment.

---

## Prerequisites

### Development Tools
- ✅ Node.js 18+ and npm
- ✅ Power Apps CLI (pac CLI)
- ✅ .NET SDK 6.0+
- ✅ Visual Studio Code (recommended)

### Power Platform
- ✅ Dataverse environment (Dev, Test, or Prod)
- ✅ System Administrator or System Customizer role
- ✅ PCF components enabled in environment

### Accounts
- ✅ Power Platform account
- ✅ Azure AD credentials (if using service principal)

---

## Deployment Options

### Option A: Unmanaged Solution (Development/Testing)
- Quick deployment
- Easy updates
- For dev/test environments

### Option B: Managed Solution (Production)
- Controlled deployment
- Version management
- For production environments

---

## Step 1: Build the PCF Solution

### 1.1 Clone Repository

```bash
git clone https://github.com/your-org/spaarke.git
cd spaarke
```

### 1.2 Install Dependencies

```bash
# Install root dependencies
npm install

# Install component dependencies
cd src/shared/Spaarke.UI.Components
npm install
cd ../../..

# Install PCF control dependencies
cd src/controls/UniversalDatasetGrid
npm install
cd ../../..
```

### 1.3 Build Component Library

```bash
cd src/shared/Spaarke.UI.Components
npm run build
cd ../../..
```

### 1.4 Build PCF Control

```bash
cd src/controls/UniversalDatasetGrid
npm run build
cd ../../..
```

---

## Step 2: Create Solution Package

### 2.1 Initialize Solution

```bash
# Create solution folder
mkdir solutions
cd solutions

# Initialize PCF solution
pac solution init --publisher-name Spaarke --publisher-prefix spaarke

# Add reference to PCF control
pac solution add-reference --path ../src/controls/UniversalDatasetGrid
```

### 2.2 Build Solution

```bash
# Build managed solution (for production)
msbuild /t:build /p:configuration=Release

# Or build unmanaged solution (for development)
msbuild /t:build /p:configuration=Debug
```

This creates:
- `bin/Debug/SpaarkeSolution.zip` (Unmanaged)
- `bin/Release/SpaarkeSolution.zip` (Managed)

---

## Step 3: Deploy to Dataverse

### Method 1: Using Power Apps CLI

```bash
# Authenticate to environment
pac auth create --url https://yourorg.crm.dynamics.com

# Import solution
pac solution import --path bin/Release/SpaarkeSolution.zip
```

### Method 2: Using Power Platform Admin Center

1. Navigate to https://admin.powerplatform.microsoft.com
2. Select your environment
3. Go to **Solutions**
4. Click **Import**
5. Upload `SpaarkeSolution.zip`
6. Follow the wizard
7. Click **Import**

### Method 3: Using make.powerapps.com

1. Navigate to https://make.powerapps.com
2. Select your environment
3. Go to **Solutions** in left navigation
4. Click **Import solution**
5. **Browse** and select `SpaarkeSolution.zip`
6. Click **Next**, then **Import**

---

## Step 4: Configure the Control

### 4.1 Add Control to Form

1. Open your model-driven app
2. Navigate to a form (e.g., Account main form)
3. Select a section
4. Click **Component** > **Get more components**
5. Search for "Universal Dataset Grid"
6. Click **Add**
7. Configure control properties:
   - **Dataset**: Select entity dataset
   - **Config JSON** (optional): Entity configuration

### 4.2 Add Control to View

1. Open your model-driven app
2. Navigate to a view (e.g., Active Accounts)
3. Click **Edit view**
4. Select **Advanced** > **Custom Controls**
5. Click **Add control**
6. Select "Universal Dataset Grid"
7. Configure for Web, Phone, Tablet
8. **Save** and **Publish**

---

## Step 5: Configure Entity Settings (Optional)

### 5.1 Create Entity Configuration JSON

Create a JSON configuration for entity-specific settings:

```json
{
  "schemaVersion": "1.0",
  "defaultConfig": {
    "viewMode": "Grid",
    "enabledCommands": ["open", "create", "delete", "refresh"],
    "compactToolbar": false,
    "enableVirtualization": true
  },
  "entityConfigs": {
    "account": {
      "viewMode": "Card",
      "compactToolbar": true
    },
    "sprk_document": {
      "enabledCommands": ["open", "upload", "download"],
      "customCommands": {
        "upload": {
          "label": "Upload to SharePoint",
          "actionType": "customapi",
          "actionName": "sprk_UploadDocument",
          "parameters": {
            "ParentId": "{parentRecordId}"
          },
          "refresh": true
        }
      }
    }
  }
}
```

### 5.2 Store Configuration

**Option A**: Form property
- Add as bound property in form configuration

**Option B**: Environment variable
- Create environment variable `spaarke_DatasetGridConfig`
- Store JSON in value

**Option C**: Custom table
- Create `spaarke_configuration` table
- Store JSON per entity

---

## Step 6: Configure Custom Commands (If Applicable)

### 6.1 Create Custom API (Example: Upload)

```bash
# Using Power Apps CLI
pac customapi create \
  --name "sprk_UploadDocument" \
  --displayname "Upload Document" \
  --boundentitylogicalname "sprk_document" \
  --executeprivileges "None"

# Add request parameters
pac customapi add-parameter \
  --customapiid <GUID> \
  --name "ParentId" \
  --type "Guid"
```

### 6.2 Implement Custom API Logic

Create plugin or Azure Function to handle Custom API execution.

---

## Step 7: Test Deployment

### 7.1 Verify Control Loads

1. Open model-driven app
2. Navigate to entity list/form with control
3. Verify control renders
4. Check browser console for errors

### 7.2 Test Commands

1. Click **New** - Verify form opens
2. Select record, click **Open** - Verify navigation
3. Click **Refresh** - Verify grid updates
4. Test custom commands (if configured)

### 7.3 Test Views

1. Switch between Grid/List/Card views
2. Verify data displays correctly
3. Check responsiveness on mobile

---

## Step 8: Enable for Users

### 8.1 Assign Security Roles

1. Go to **Settings** > **Security** > **Security Roles**
2. Open relevant role (e.g., Sales Manager)
3. Ensure permissions:
   - Read on PCF Control entity
   - Read/Write on target entities

### 8.2 Publish App

1. Open model-driven app in designer
2. Click **Publish**
3. Share app with users/security groups

---

## Step 9: Monitor and Validate

### 9.1 Check Telemetry

Monitor for errors:
- Application Insights (if configured)
- Dataverse plugin trace logs
- Browser console logs

### 9.2 Collect User Feedback

- Test with pilot users
- Monitor support tickets
- Gather performance metrics

---

## Rollback Procedure

### If Issues Occur

1. **Remove from forms**:
   - Edit form
   - Remove PCF control
   - Add back default grid
   - Publish

2. **Uninstall solution**:
   - Go to Solutions
   - Select solution
   - Click **Delete**
   - Confirm deletion

3. **Restore previous version**:
   - Import previous solution version
   - Reconfigure forms/views

---

## Environment-Specific Configurations

### Development Environment
```json
{
  "enabledCommands": ["open", "create", "delete", "refresh", "debug"],
  "compactToolbar": false,
  "debugMode": true
}
```

### Production Environment
```json
{
  "enabledCommands": ["open", "create", "delete", "refresh"],
  "compactToolbar": true,
  "debugMode": false
}
```

---

## Post-Deployment Checklist

- ✅ Control renders on all target forms/views
- ✅ All commands execute correctly
- ✅ Custom APIs respond as expected
- ✅ Performance is acceptable (<500ms render)
- ✅ No console errors
- ✅ Accessibility tested (keyboard, screen reader)
- ✅ Mobile/tablet rendering verified
- ✅ User permissions validated
- ✅ Documentation updated
- ✅ Training materials prepared

---

## Troubleshooting

### Control Not Appearing
- Verify solution imported successfully
- Check PCF components enabled in environment
- Ensure correct publisher prefix
- Refresh browser cache

### Commands Not Working
- Verify user security roles
- Check Custom API permissions
- Review plugin trace logs
- Validate JSON configuration

### Performance Issues
- Enable virtualization for large datasets
- Reduce number of visible columns
- Optimize Custom API queries
- Check network latency

---

## Support

### Resources
- [API Documentation](../api/UniversalDatasetGrid.md)
- [Configuration Guide](./ConfigurationGuide.md)
- [Common Issues](../troubleshooting/CommonIssues.md)

### Contact
- GitHub Issues: https://github.com/your-org/spaarke/issues
- Documentation: https://docs.spaarke.com

---

## Next Steps

- [Run E2E Tests](../../tests/e2e/README.md)
- [Monitor Performance](../troubleshooting/Performance.md)
- [Configure Custom Commands](./CustomCommands.md)
