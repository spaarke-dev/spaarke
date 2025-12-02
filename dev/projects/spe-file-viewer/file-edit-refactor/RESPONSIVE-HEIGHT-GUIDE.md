# SpeFileViewer - Responsive Height Configuration Guide

**Version**: 1.0.5
**Date**: November 26, 2025
**Feature**: Responsive Height Parameter

---

## Overview

The SpeFileViewer PCF control now supports a **responsive height parameter** that allows you to configure the minimum height while enabling the control to expand and fill available form space.

### How It Works

**Responsive Behavior**:
- **Minimum Height**: The control will always be at least the configured pixel height
- **Expansion**: The control can grow beyond the minimum to fill available vertical space
- **Flexibility**: Works across desktop, tablet, and mobile screen sizes

**Technical Implementation**:
```css
minHeight: 600px  /* Configurable via parameter */
height: 100%      /* Responsive expansion */
display: flex     /* Flexbox layout */
```

---

## Configuration Steps

### Step 1: Open Form Designer

1. Navigate to [Power Apps](https://make.powerapps.com)
2. Open your solution containing the document form
3. Select the form where SpeFileViewer is used
4. Click **Edit** to open Form Designer

### Step 2: Select the SpeFileViewer Field

1. In the form canvas, click the field bound to SpeFileViewer PCF
2. The field properties pane will open on the right side

### Step 3: Configure Height Property

1. In the properties pane, scroll to find **Control Height (px)**
2. Enter your desired minimum height in pixels

**Recommended Values**:
- **Small forms**: 400px
- **Medium forms** (default): 600px
- **Large/full-screen forms**: 800px or higher

**Example Configuration**:
```
Control Height (px): 600
```

### Step 4: Save and Publish

1. Click **Save** in Form Designer
2. Click **Publish** to apply changes
3. Refresh your model-driven app to see the updated height

---

## Responsive Behavior Examples

### Desktop (1920x1080)

**Form Section Height**: 800px
**Control Height Parameter**: 600px
**Actual Height**: 800px (fills section)

The control expands to use the full 800px available in the section.

### Tablet (768x1024)

**Form Section Height**: 600px
**Control Height Parameter**: 600px
**Actual Height**: 600px (matches both)

The control uses exactly 600px as configured.

### Mobile (375x667)

**Form Section Height**: 400px
**Control Height Parameter**: 600px
**Actual Height**: 600px (respects minimum)

The control maintains the 600px minimum height and enables scrolling.

---

## Advanced Customization

### Option 1: Form XML (Manual Height Override)

If you need precise control over section height, you can edit the Form XML directly:

```xml
<section id="tab_general_section_document" height="100%">
  <rows>
    <row height="100%">
      <cell rowspan="30">
        <control id="sprk_documentid" classid="{pcf-guid}">
          <parameters>
            <controlHeight>800</controlHeight>
          </parameters>
        </control>
      </cell>
    </row>
  </rows>
</section>
```

See [FORM-HEIGHT-CONFIGURATION.md](./FORM-HEIGHT-CONFIGURATION.md) for detailed XML editing instructions.

### Option 2: Dynamic Height via Environment Variables

For multi-environment deployments, you can bind the height to an environment variable:

1. Create environment variable: `spaarke_fileviewer_height`
2. Set value per environment (Dev: 600, Prod: 800)
3. Bind in Form Designer: `{env:spaarke_fileviewer_height}`

---

## Troubleshooting

### Issue: Control Still Too Small

**Symptom**: Control height doesn't expand despite setting parameter.

**Solutions**:
1. **Check Section Height**: Ensure the form section has enough vertical space
2. **Verify Parameter**: Confirm the height parameter is set in Form Designer
3. **Clear Cache**: Hard refresh browser (Ctrl+Shift+R) to clear cached control
4. **Republish Form**: Ensure form changes were published

### Issue: Control Too Tall on Mobile

**Symptom**: Control takes too much vertical space on mobile devices.

**Solutions**:
1. **Lower Minimum Height**: Set parameter to 400px for mobile-friendly forms
2. **Use Responsive Form Layouts**: Enable responsive form design in Form Designer
3. **Hide Section on Mobile**: Use form logic to hide document section on small screens

### Issue: Scrolling Issues

**Symptom**: Double scrollbars or no scrolling within iframe.

**Solutions**:
- **Parent Form Scrolling**: Ensure form container allows overflow
- **PCF Internal Scrolling**: The iframe uses `overflow: auto` (already configured)
- **CSS Conflict**: Check for custom CSS overriding PCF styles

---

## Browser DevTools Verification

To verify the height is applied correctly:

1. Open browser DevTools (F12)
2. Inspect the PCF container element
3. Check computed styles:

```javascript
// Find PCF container
const container = document.querySelector('[data-control-name="sprk_documentid"]');

// Check computed height
console.log('Min Height:', window.getComputedStyle(container).minHeight);
console.log('Height:', window.getComputedStyle(container).height);
console.log('Actual Height:', container.offsetHeight + 'px');

// Expected output:
// Min Height: 600px
// Height: 100%
// Actual Height: 800px (or whatever the parent section provides)
```

---

## Changelog

### Version 1.0.5 (Nov 26, 2025)

**Added**:
- `controlHeight` input parameter to ControlManifest.Input.xml
- Responsive height styling in index.ts (minHeight + height: 100%)
- Resource strings for height parameter display and description

**Modified**:
- `ControlInputs` interface to include `controlHeight` property
- Solution version bumped from 1.0.4 to 1.0.5

**Default Value**: 600px

---

## Best Practices

### 1. Start with Default (600px)

The default 600px works well for most forms. Only customize if needed.

### 2. Consider Mobile Users

If your users access forms on mobile, use 400-500px to avoid excessive scrolling.

### 3. Match Section Height

Ensure the form section height is equal to or greater than the control height parameter.

### 4. Test Across Devices

Verify the responsive behavior on desktop, tablet, and mobile before publishing.

### 5. Use Environment Variables for Flexibility

Bind to environment variables if you need different heights per environment.

---

## Related Documentation

- [FORM-HEIGHT-CONFIGURATION.md](./FORM-HEIGHT-CONFIGURATION.md) - Advanced Form XML editing
- [OVERVIEW.md](./OVERVIEW.md) - Complete SpeFileViewer feature documentation
- [TASKS.md](./TASKS.md) - Implementation task breakdown

---

## Support

If you encounter issues with the responsive height feature:

1. Verify the PCF version is 1.0.5 or higher
2. Check the solution import was successful
3. Ensure customizations were published
4. Review browser console for errors

**Correlation ID**: Check browser DevTools console for `[SpeFileViewer]` logs containing the correlation ID for troubleshooting.
