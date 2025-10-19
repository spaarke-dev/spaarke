# File Verification Checklist - SDAP Components

**Purpose:** Ensure we're working on the correct, current files before making changes or deploying.

---

## 1. SPE BFF API (Azure Deployment)

### **Source Files Location:**
`c:\code_files\spaarke\src\api\Spe.Bff.Api\`

### **Verification Steps:**

#### A. Check File Last Modified Dates
```bash
cd /c/code_files/spaarke/src/api/Spe.Bff.Api
ls -lh --time-style=long-iso Infrastructure/Graph/GraphClientFactory.cs
ls -lh --time-style=long-iso Infrastructure/Graph/UploadSessionManager.cs
ls -lh --time-style=long-iso Api/OBOEndpoints.cs
```

**Expected:** Files should have recent modification dates if we just changed them.

#### B. Verify Build Output
```bash
ls -lh --time-style=long-iso publish/Spe.Bff.Api.dll
```

**Expected:** DLL date should match or be AFTER source file changes.

#### C. Check What's Currently Deployed to Azure
```bash
az webapp deployment list --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2 --query "[0].{status:status, start:start_time, end:end_time}" -o table
```

**Expected:** Should show recent successful deployment.

#### D. Verify Running Version in Azure
```bash
curl -s https://spe-api-dev-67e2xz.azurewebsites.net/healthz
```

**Expected:** Returns "Healthy"

---

## 2. PCF Controls (Dataverse Deployment)

### **Current Files - UniversalQuickCreate:**
`c:\code_files\spaarke\src\controls\UniversalQuickCreate\`

### **Verification Steps:**

#### A. Check Source File Dates
```bash
cd /c/code_files/spaarke/src/controls/UniversalQuickCreate
ls -lh --time-style=long-iso UniversalQuickCreate/index.ts
ls -lh --time-style=long-iso UniversalQuickCreate/services/auth/msalConfig.ts
ls -lh --time-style=long-iso UniversalQuickCreate/ControlManifest.Input.xml
```

**Expected:** These are the actual source files we should modify.

#### B. Check Build Output Date
```bash
ls -lh --time-style=long-iso out/controls/UniversalQuickCreate/bundle.js
```

**Expected:** Should be AFTER source file changes (means we rebuilt after editing).

#### C. Verify What's in Dataverse
```bash
cd /c/code_files/spaarke/src/controls/UniversalQuickCreate
pac solution list | grep -i "universal"
```

**Expected:** Should show "UniversalQuickCreate" solution exists.

#### D. Export Solution to Check Contents
```bash
pac solution export --name UniversalQuickCreate --path ./check_deployed.zip --overwrite
unzip -l check_deployed.zip | grep -i "bundle.js\|control"
```

**Expected:** Should list bundle.js and control manifest files.

---

## 3. Pre-Change Verification Checklist

**Before making ANY code changes, verify:**

- [ ] **File path is correct**: Is this `UniversalQuickCreate` or `UniversalDatasetGrid`?
- [ ] **File is source, not build artifact**: Is this in `UniversalQuickCreate/` folder (source) or `out/` folder (build)?
- [ ] **File has been modified recently**: Check `ls -lh --time-style=long-iso <filename>`
- [ ] **No duplicate files exist**: Search for same filename elsewhere in repo
- [ ] **We know the deployment target**: Azure (BFF API) or Dataverse (PCF control)?

---

## 4. Post-Change Verification Checklist

**After making code changes:**

### For BFF API:
- [ ] **Source file saved**: Check file modified timestamp
- [ ] **Project built**: Run `dotnet build` or `dotnet publish`
- [ ] **DLL updated**: Check `publish/Spe.Bff.Api.dll` timestamp
- [ ] **Deployed to Azure**: Run deployment script
- [ ] **Health check passes**: `curl https://spe-api-dev-67e2xz.azurewebsites.net/healthz`
- [ ] **Test the endpoint**: Try actual API call

### For PCF Control:
- [ ] **Source file saved**: Check file modified timestamp
- [ ] **Control built**: Run `npm run build`
- [ ] **Bundle updated**: Check `out/controls/UniversalQuickCreate/bundle.js` timestamp
- [ ] **Solution packaged**: Run solution build (if using solution approach)
- [ ] **Deployed to Dataverse**: Run `pac pcf push` or solution import
- [ ] **Control appears in maker portal**: Check https://make.powerapps.com
- [ ] **Test in app**: Open form with control

---

## 5. Common Pitfalls to Avoid

### ❌ **Wrong Control**
```bash
# BAD - Working on Dataset Grid when we need Quick Create
cd src/controls/UniversalDatasetGrid
```
```bash
# GOOD - Correct control
cd src/controls/UniversalQuickCreate
```

### ❌ **Wrong File (Build vs Source)**
```bash
# BAD - Editing built output (gets overwritten on next build)
vim out/controls/UniversalQuickCreate/bundle.js
```
```bash
# GOOD - Editing source
vim UniversalQuickCreate/services/auth/msalConfig.ts
```

### ❌ **Forgot to Rebuild**
```bash
# BAD - Deploy without rebuilding
# (deploys old code)
pac pcf push
```
```bash
# GOOD - Rebuild then deploy
npm run build
pac pcf push
```

### ❌ **Wrong Solution**
```bash
# Verify you're not in duplicate/old solution folder
pwd  # Should show: src/controls/UniversalQuickCreate/UniversalQuickCreateSolution
     # NOT: src/solutions/UniversalQuickCreateSolution (if that exists)
```

---

## 6. Quick Verification Commands

### **Am I in the right directory?**
```bash
pwd && basename $(pwd)
```

### **What files did I just change?**
```bash
git status
git diff --name-only
```

### **When was this file last modified?**
```bash
stat -c '%y %n' <filename>  # Linux
# or
ls -lh --time-style=long-iso <filename>
```

### **Is my build fresh?**
```bash
# Compare source vs build timestamps
stat -c '%Y' UniversalQuickCreate/services/auth/msalConfig.ts
stat -c '%Y' out/controls/UniversalQuickCreate/bundle.js
# Build timestamp should be >= source timestamp
```

---

## 7. Deployment Verification Matrix

| Component | Source Location | Build Output | Deployment Target | Verification URL |
|-----------|----------------|--------------|-------------------|------------------|
| **SPE BFF API** | `src/api/Spe.Bff.Api/` | `publish/Spe.Bff.Api.dll` | Azure Web App | https://spe-api-dev-67e2xz.azurewebsites.net/healthz |
| **UniversalQuickCreate PCF** | `src/controls/UniversalQuickCreate/UniversalQuickCreate/` | `out/controls/UniversalQuickCreate/bundle.js` | Dataverse | https://make.powerapps.com |
| **UniversalDatasetGrid PCF** | `src/controls/UniversalDatasetGrid/UniversalDatasetGrid/` | `out/controls/UniversalDatasetGrid/bundle.js` | Dataverse | https://make.powerapps.com |

---

## 8. Before Each Work Session - Run This

```bash
echo "=== CURRENT WORK SESSION VERIFICATION ==="
echo ""
echo "1. What control are we working on?"
echo "   Answer: UniversalQuickCreate (file upload issue)"
echo ""
echo "2. What files need OAuth scope fix?"
echo "   File: src/controls/UniversalQuickCreate/UniversalQuickCreate/services/auth/msalConfig.ts"
echo ""
echo "3. Check current file date:"
ls -lh --time-style=long-iso src/controls/UniversalQuickCreate/UniversalQuickCreate/services/auth/msalConfig.ts
echo ""
echo "4. Check BFF API files (already fixed?):"
ls -lh --time-style=long-iso src/api/Spe.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs
echo ""
echo "5. Are we clear to proceed? (y/n)"
```

---

**RULE: Always run verification steps BEFORE making changes or deploying.**

