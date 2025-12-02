# Form Height Configuration for SpeFileViewer PCF

**Issue**: The SpeFileViewer PCF control doesn't expand to full vertical space in the model-driven app form.

**Section**: `tab_general_section_document`
**Field Type**: Single-line text (not multiline)
**Problem**: Power Apps Form Designer doesn't offer "Number of rows" setting for single-line text fields.

**Solution**: Edit Form XML directly to configure section and field height.

---

## Option 1: Export Solution and Edit XML

### Step 1: Export Form as Unmanaged Solution

```bash
# Export the solution containing your form
pac solution export \
  --name <YourSolutionName> \
  --path C:\temp\solution.zip \
  --managed false

# Extract the solution
cd C:\temp
unzip solution.zip -d solution_extracted
```

### Step 2: Locate Form XML

**Path**: `solution_extracted\FormXml\{entity-name}\{form-id}.xml`

Example: `solution_extracted\FormXml\sprk_document\{guid}.xml`

### Step 3: Find Section in XML

Search for `tab_general_section_document`:

```xml
<section id="tab_general_section_document" showlabel="false" showbar="false" columns="1">
  <rows>
    <row>
      <cell id="{cell-guid}" showlabel="false">
        <control id="sprk_documentid" classid="{your-pcf-guid}" ...>
          ...
        </control>
      </cell>
    </row>
  </rows>
</section>
```

### Step 4: Modify Section and Row Height

**Replace the section with:**

```xml
<section
  id="tab_general_section_document"
  showlabel="false"
  showbar="false"
  columns="1"
  height="100%">
  <rows>
    <row height="100%">
      <cell
        id="{cell-guid}"
        showlabel="false"
        rowspan="30"
        colspan="1">
        <control id="sprk_documentid" classid="{your-pcf-guid}" ...>
          <parameters>
            <!-- Existing parameters -->
            ...
            <!-- Add height parameter -->
            <Height xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xsi:type="IntegerParameter">
              <Value>600</Value>
            </Height>
          </parameters>
        </control>
      </cell>
    </row>
  </rows>
</section>
```

**Key Changes**:
1. **Section**: Added `height="100%"` to expand section vertically
2. **Row**: Added `height="100%"` to expand row within section
3. **Cell**: Added `rowspan="30"` to make cell take multiple row heights
4. **Control Parameters**: Added `<Height>600</Height>` to set minimum PCF height in pixels

### Step 5: Re-package and Import Solution

```bash
# Navigate to extracted solution folder
cd C:\temp\solution_extracted

# Re-zip the solution
pac solution pack \
  --zipfile C:\temp\solution_modified.zip \
  --folder .

# Import back to Dataverse
pac solution import \
  --path C:\temp\solution_modified.zip \
  --activate-plugins
```

---

## Option 2: Direct Form Metadata Update via PowerShell

If you want to avoid solution export/import, you can update form metadata directly using Dataverse Web API.

### Prerequisites

```powershell
# Install required modules
Install-Module Microsoft.PowerApps.Administration.PowerShell
Install-Module Microsoft.Xrm.Data.PowerShell
```

### Script: Update Form Height

**Save as**: `C:\code_files\spaarke\dev\projects\spe-file-viewer\file-edit-refactor\Update-FormHeight.ps1`

```powershell
<#
.SYNOPSIS
    Updates form section height for SpeFileViewer PCF control
.DESCRIPTION
    Directly modifies form XML via Dataverse Web API to expand section height
.PARAMETER EnvironmentUrl
    Dataverse environment URL (e.g., https://spaarkedev1.crm.dynamics.com)
.PARAMETER FormName
    Name of the form to update (e.g., "Document Information")
.PARAMETER EntityLogicalName
    Logical name of the entity (e.g., "sprk_document")
#>

param(
    [Parameter(Mandatory=$true)]
    [string]$EnvironmentUrl,

    [Parameter(Mandatory=$true)]
    [string]$FormName,

    [Parameter(Mandatory=$true)]
    [string]$EntityLogicalName
)

# Connect to Dataverse
Connect-CrmOnline -ServerUrl $EnvironmentUrl

# Retrieve form
$fetchXml = @"
<fetch top='1'>
  <entity name='systemform'>
    <attribute name='formid' />
    <attribute name='formxml' />
    <filter>
      <condition attribute='name' operator='eq' value='$FormName' />
      <condition attribute='objecttypecode' operator='eq' value='$EntityLogicalName' />
    </filter>
  </entity>
</fetch>
"@

$forms = Get-CrmRecordsByFetch -conn $conn -Fetch $fetchXml

if ($forms.CrmRecords.Count -eq 0) {
    Write-Error "Form '$FormName' not found for entity '$EntityLogicalName'"
    exit 1
}

$form = $forms.CrmRecords[0]
$formId = $form.formid
$formXml = [xml]$form.formxml

# Find section
$section = $formXml.form.tabs.tab.columns.column.sections.section |
    Where-Object { $_.id -eq 'tab_general_section_document' }

if ($null -eq $section) {
    Write-Error "Section 'tab_general_section_document' not found"
    exit 1
}

# Modify section attributes
$section.SetAttribute('height', '100%')

# Find row and modify
$row = $section.rows.row | Select-Object -First 1
$row.SetAttribute('height', '100%')

# Find cell and modify rowspan
$cell = $row.cell | Where-Object { $_.control.classid -ne $null }
if ($cell) {
    $cell.SetAttribute('rowspan', '30')

    # Add Height parameter to control if not exists
    $control = $cell.control
    $heightParam = $control.parameters.Height

    if ($null -eq $heightParam) {
        $heightNode = $formXml.CreateElement('Height', $control.NamespaceURI)
        $heightNode.SetAttribute('type', 'http://www.w3.org/2001/XMLSchema-instance', 'IntegerParameter')

        $valueNode = $formXml.CreateElement('Value', $control.NamespaceURI)
        $valueNode.InnerText = '600'

        $heightNode.AppendChild($valueNode) | Out-Null
        $control.parameters.AppendChild($heightNode) | Out-Null
    }
}

# Update form
$updateRecord = @{
    'formxml' = $formXml.OuterXml
}

Set-CrmRecord -conn $conn -EntityLogicalName 'systemform' -Id $formId -Fields $updateRecord

Write-Host "Form '$FormName' updated successfully!" -ForegroundColor Green
Write-Host "Please publish the form to apply changes." -ForegroundColor Yellow
```

### Run the Script

```powershell
# Execute with your environment details
.\Update-FormHeight.ps1 `
  -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com" `
  -FormName "Document Information" `
  -EntityLogicalName "sprk_document"

# Publish the form after update
pac solution publish
```

---

## Option 3: Manual Edit via Power Apps Form Designer (Workaround)

Even though the field is single-line text, you can force height using these steps:

### Step 1: Switch Field Control to PCF

1. Open form in Power Apps Form Designer
2. Select the field bound to SpeFileViewer PCF
3. Click **Components** in right pane
4. Select **SpeFileViewer** PCF control
5. In Properties, look for **Height** input parameter

### Step 2: Add Custom Height Parameter

If Height parameter exists in the PCF manifest:

1. In form designer, click the field
2. Right pane → **Properties** → **Additional properties**
3. Add custom property: `Height = 600`

**Note**: This requires the PCF manifest to expose a `Height` input parameter. Check `ControlManifest.Input.xml`.

---

## Option 4: Update PCF Manifest to Support Height

If you want the PCF itself to handle height dynamically, update the manifest.

### Modify ControlManifest.Input.xml

**File**: `C:\code_files\spaarke\src\controls\SpeFileViewer\SpeFileViewer\ControlManifest.Input.xml`

**Add this property after existing properties**:

```xml
<property name="Height"
          display-name-key="Height"
          description-key="Control height in pixels"
          of-type="Whole.None"
          usage="input"
          default-value="600" />
```

### Update index.ts to Apply Height

**File**: `C:\code_files\spaarke\src\controls\SpeFileViewer\SpeFileViewer\index.ts`

**In `updateView` method**, apply height from input property:

```typescript
public updateView(context: ComponentFramework.Context<ControlInputs>): void {
    // Get height from input parameter (or use default)
    const height = context.parameters.Height?.raw ?? 600;

    // Apply height to container
    if (this.container) {
        this.container.style.height = `${height}px`;
        this.container.style.minHeight = `${height}px`;
    }

    // Existing updateView logic...
}
```

**Rebuild and redeploy**:

```bash
cd C:/code_files/spaarke/src/controls/SpeFileViewer/SpeFileViewer
npm run build
pac pcf push --publisher-prefix sprk
```

---

## Recommended Approach

**For Immediate Fix**: Use **Option 1** (Solution Export/Edit XML)

**Reasons**:
- No code changes required
- Works immediately after import
- Full control over form layout
- Can set exact pixel height and rowspan

**For Long-Term**: Combine **Option 1** + **Option 4**

1. Add `Height` property to PCF manifest (Option 4)
2. Set section/cell height via Form XML (Option 1)
3. This gives both declarative control (form XML) and dynamic control (PCF manifest)

---

## Validation After Changes

### 1. Check Form in App

1. Open model-driven app
2. Navigate to a document record
3. Verify SpeFileViewer section expands to full available height
4. Test scrolling within PCF iframe (should scroll independently from form)

### 2. Inspect DOM

**In browser DevTools**:

```javascript
// Check section height
document.querySelector('[data-id="tab_general_section_document"]').offsetHeight

// Check PCF container height
document.querySelector('.spe-file-viewer').offsetHeight

// Expected: Both should be >= 600px
```

### 3. Test Responsiveness

- Resize browser window
- Check mobile/tablet views
- Verify section remains expanded

---

## Common Issues

### Issue 1: Section Still Not Expanding

**Symptom**: Section shows correct XML attributes but doesn't expand visually.

**Solution**: Check parent tab configuration. Ensure tab also has height settings:

```xml
<tab id="tab_general" height="100%">
  ...
</tab>
```

### Issue 2: Form Import Fails

**Symptom**: `pac solution import` fails with validation errors.

**Solution**:
1. Check XML is well-formed: `xmllint solution.xml`
2. Ensure all GUIDs match original form
3. Verify namespace declarations are intact

### Issue 3: Height Ignored in App

**Symptom**: XML changes saved but height not applied in runtime.

**Solution**: Clear browser cache and republish customizations:

```bash
pac solution publish
```

---

## Files to Modify

**For Option 1 (Export/Edit XML)**:
- Form XML in exported solution

**For Option 4 (PCF Manifest)**:
- `C:\code_files\spaarke\src\controls\SpeFileViewer\SpeFileViewer\ControlManifest.Input.xml`
- `C:\code_files\spaarke\src\controls\SpeFileViewer\SpeFileViewer\index.ts`

---

## Next Steps

1. Choose approach (recommended: Option 1)
2. Back up current solution before making changes
3. Apply XML modifications
4. Test in Dataverse environment
5. Document final rowspan/height values used

Let me know which option you'd like to proceed with, and I can help execute the specific steps.
