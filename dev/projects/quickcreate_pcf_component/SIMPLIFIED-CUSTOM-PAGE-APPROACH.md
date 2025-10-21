# Simplified Custom Page Approach - Without UI Parameters

**Reality Check:** The current Power Apps Custom Page interface may not support input parameters through the UI for canvas-based custom pages in the same way as model-driven forms.

---

## Alternative Approach: Use Context Variables

Instead of formal input parameters, we'll use **Param() function** which Custom Pages support natively when called via `Xrm.Navigation.navigateTo()`.

---

## Simplified Steps

### Step 1-4: Same as Before
Follow steps 1-4 from CUSTOM-PAGE-CREATION-CURRENT-UI.md:
- Create page with layout template
- Clear template content
- Name the page
- Configure screen name

### Step 5: SKIP Parameters (For Now)
**Skip the parameters step entirely.** The Param() function will work without explicitly defining them in the UI.

### Step 6: Configure as Dialog
1. Click **Settings** (⚙️)
2. Look for **"Display"** settings
3. Find **"App type"** or **"Form factor"**
4. You might see options for width/height here
5. If there's no Dialog option visible, that's OK - we'll set it via code

### Step 7: Add PCF Control

1. Click **"Insert"** (+ button)
2. **"Get more components"** → **"Code"** tab
3. Find **"UniversalDocumentUpload"**
4. Import it
5. Drag onto canvas
6. Resize to fill screen:
   - X: `0`
   - Y: `0`
   - Width: `Parent.Width`
   - Height: `Parent.Height`

### Step 8: Bind Properties Using Param()

Select the PCF control, then in the properties panel bind each property:

**parentEntityName:**
```
Param("parentEntityName")
```

**parentRecordId:**
```
Param("parentRecordId")
```

**containerId:**
```
Param("containerId")
```

**parentDisplayName:**
```
Param("parentDisplayName")
```

**sdapApiBaseUrl:**
```
"https://spe-api-dev-67e2xz.azurewebsites.net"
```

The `Param()` function will automatically receive values passed via `Xrm.Navigation.navigateTo()` from the ribbon button.

### Step 9: Save
- Name: `sprk_documentuploaddialog`
- Save

### Step 10: Publish
- Click Publish
- Wait for completion

---

## How Param() Works

When the ribbon button calls:
```javascript
Xrm.Navigation.navigateTo({
    pageType: 'custom',
    name: 'sprk_documentuploaddialog',
    data: {
        parentEntityName: 'sprk_matter',
        parentRecordId: '12345...',
        containerId: 'b!ABC...',
        parentDisplayName: 'Matter #12345'
    }
}, {...})
```

The Custom Page will receive these values, and `Param("parentEntityName")` will return `'sprk_matter'`, etc.

---

## Why This Works

Power Apps Custom Pages automatically support the `Param()` function for receiving data passed via navigation, even without explicitly defining parameters in the UI. This is the **standard pattern** for Custom Pages.

---

## Next Steps

1. **Try this simplified approach** (no parameters step)
2. **Save and Publish** the page
3. **Test with the test script** to verify it works

If this works (it should), then we don't need to find the parameters UI - the `Param()` function handles everything.

---

**Try this approach and let me know if you can add the PCF control and bind the properties using `Param()`!**
