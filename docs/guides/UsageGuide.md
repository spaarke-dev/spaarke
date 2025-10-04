# Usage Guide

Complete guide to using the Universal Dataset Grid PCF component in your Power Apps model-driven applications.

---

## Overview

The Universal Dataset Grid is a **universal**, **configuration-driven** PCF control that replaces the standard Dataverse grid with:

- **Multiple View Modes**: Grid, List, Card
- **Rich Command Toolbar**: Built-in and custom commands
- **High Performance**: Virtualization for large datasets (1000+ records)
- **Accessibility**: WCAG 2.1 AA compliant
- **Keyboard Shortcuts**: Power-user productivity
- **Responsive Design**: Works on desktop, tablet, mobile

---

## Getting Started

### Prerequisites

- Power Apps model-driven app
- Universal Dataset Grid solution installed
- System Customizer or System Administrator role

### Adding Control to a Form

1. Open your model-driven app in the app designer
2. Navigate to a form (e.g., **Account > Main Form**)
3. Select a section or create new section
4. Click **Component** > **Get more components**
5. Search for "Universal Dataset Grid"
6. Click **Add**
7. Configure properties (see [Configuration](#configuration))
8. **Save** and **Publish**

### Adding Control to a View

1. Open your model-driven app in the app designer
2. Navigate to a view (e.g., **Account > Active Accounts**)
3. Click **Edit view**
4. Select **Advanced** > **Custom Controls**
5. Click **Add control**
6. Select "Universal Dataset Grid"
7. Enable for **Web**, **Phone**, **Tablet**
8. **Save** and **Publish**

---

## View Modes

The grid supports three view modes that can be switched at runtime.

### Grid View (Default)

**Best for**: Tabular data with multiple columns

**Features**:
- Sortable columns
- Column resizing
- Row selection (single/multiple)
- Virtualization (automatic for >100 records)

**Use Cases**:
- Accounts list
- Contacts list
- Opportunities list
- Any tabular data

**Appearance**:
```
┌─────────────────────────────────────────────────┐
│ [New] [Open] [Delete] [Refresh]   [Grid ▼]     │
├──────────┬──────────────┬──────────────┬────────┤
│ ☐ Name   │ Email        │ Phone        │ City   │
├──────────┼──────────────┼──────────────┼────────┤
│ ☐ Acme   │ info@acme    │ 555-1234     │ NYC    │
│ ☐ Contoso│ hi@contoso   │ 555-5678     │ Seattle│
│ ☐ Fabrikam│ sales@fabri │ 555-9012     │ Austin │
└──────────┴──────────────┴──────────────┴────────┘
```

---

### List View

**Best for**: Simple lists with key information

**Features**:
- Compact display
- Primary field prominent
- Quick scanning
- Mobile-optimized

**Use Cases**:
- Task lists
- Activity feeds
- Mobile views
- Simple entity lists

**Appearance**:
```
┌─────────────────────────────────────────────────┐
│ [New] [Open] [Delete] [Refresh]   [List ▼]     │
├─────────────────────────────────────────────────┤
│ ☐ Acme Corp                                     │
│   info@acme.com | 555-1234 | NYC                │
├─────────────────────────────────────────────────┤
│ ☐ Contoso Ltd                                   │
│   hi@contoso.com | 555-5678 | Seattle           │
├─────────────────────────────────────────────────┤
│ ☐ Fabrikam Inc                                  │
│   sales@fabrikam.com | 555-9012 | Austin        │
└─────────────────────────────────────────────────┘
```

---

### Card View

**Best for**: Visual, content-rich records

**Features**:
- Responsive grid layout (1-4 columns based on screen width)
- Rich content display
- Thumbnail/image support
- Ideal for documents, products, contacts

**Use Cases**:
- Product catalog
- Document library
- Photo gallery
- Contact cards

**Appearance**:
```
┌─────────────────────────────────────────────────┐
│ [New] [Open] [Delete] [Refresh]   [Card ▼]     │
├──────────────┬──────────────┬──────────────────┐
│ ┌──────────┐ │ ┌──────────┐ │ ┌──────────┐   │
│ │ ☐ [Icon] │ │ │ ☐ [Icon] │ │ │ ☐ [Icon] │   │
│ │ Acme Corp│ │ │ Contoso  │ │ │ Fabrikam │   │
│ │ info@... │ │ │ hi@...   │ │ │ sales@.. │   │
│ │ NYC      │ │ │ Seattle  │ │ │ Austin   │   │
│ └──────────┘ │ └──────────┘ │ └──────────┘   │
└──────────────┴──────────────┴──────────────────┘
```

---

## Command Toolbar

### Built-in Commands

The toolbar includes standard commands:

| Command | Icon | Shortcut | Requires Selection | Description |
|---------|------|----------|-------------------|-------------|
| **New** | Plus | Ctrl+N | No | Create new record |
| **Open** | Document | Enter | Yes (single) | Open selected record |
| **Delete** | Delete | Delete | Yes | Delete selected record(s) |
| **Refresh** | Arrow Rotate | Ctrl+R | No | Reload grid data |

### Custom Commands

Add your own commands via configuration:

**Example: Approve Invoice**
```json
{
  "customCommands": {
    "approve": {
      "label": "Approve",
      "icon": "Checkmark",
      "actionType": "customapi",
      "actionName": "sprk_ApproveInvoice"
    }
  }
}
```

See [Custom Commands Guide](./CustomCommands.md) for details.

---

### Compact Toolbar Mode

Enable compact mode to save space:

```json
{
  "compactToolbar": true
}
```

**Compact Mode**:
- Icons only (no labels)
- Labels shown as tooltips on hover
- Better for mobile/tablet

**Normal Mode**:
- Icon + Label
- Better for desktop

---

## Selection

### Single Selection

Click a row to select it:
- Selected row highlighted
- Selection-dependent commands enabled (Open, Delete)

### Multiple Selection

Use checkboxes or keyboard shortcuts:

**Checkboxes**:
- Click checkbox on each row
- Click header checkbox to select all

**Keyboard**:
- **Ctrl+Click**: Toggle individual row
- **Shift+Click**: Select range
- **Ctrl+A**: Select all rows

### Selection Counter

Toolbar displays selection count:
```
[New] [Open] [Delete] [Refresh]        2 selected
```

---

## Keyboard Shortcuts

### Navigation

| Shortcut | Action |
|----------|--------|
| **Arrow Keys** | Navigate rows |
| **Home** | First row |
| **End** | Last row |
| **Page Up** | Scroll up one page |
| **Page Down** | Scroll down one page |

### Commands

| Shortcut | Command |
|----------|---------|
| **Ctrl+N** | Create new record |
| **Enter** | Open selected record |
| **Delete** | Delete selected record(s) |
| **Ctrl+R** | Refresh grid |
| **Ctrl+A** | Select all records |

### Selection

| Shortcut | Action |
|----------|--------|
| **Space** | Toggle row selection |
| **Ctrl+Click** | Toggle individual row |
| **Shift+Click** | Select range |

---

## Configuration

### Basic Configuration

Add to form property or environment variable:

```json
{
  "schemaVersion": "1.0",
  "defaultConfig": {
    "viewMode": "Grid",
    "enabledCommands": ["open", "create", "delete", "refresh"],
    "compactToolbar": false,
    "enableVirtualization": true,
    "enableKeyboardShortcuts": true,
    "enableAccessibility": true
  }
}
```

### Entity-Specific Configuration

Override defaults for specific entities:

```json
{
  "schemaVersion": "1.0",
  "defaultConfig": {
    "viewMode": "Grid"
  },
  "entityConfigs": {
    "account": {
      "viewMode": "Card"
    },
    "sprk_document": {
      "viewMode": "Card",
      "compactToolbar": true
    }
  }
}
```

See [Configuration Guide](./ConfigurationGuide.md) for complete reference.

---

## Performance

### Virtualization

**What is it?**
- Only renders visible rows in viewport
- Dramatically improves performance for large datasets
- Uses `react-window` library

**When does it activate?**
- Automatically when record count > `virtualizationThreshold` (default: 100)

**Configuration**:
```json
{
  "enableVirtualization": true,
  "virtualizationThreshold": 100
}
```

**Performance Impact**:

| Records | Without Virtualization | With Virtualization |
|---------|----------------------|---------------------|
| 50 | <100ms | <100ms |
| 100 | ~200ms | ~100ms |
| 500 | ~1000ms | ~100ms |
| 1000 | ~2000ms | ~100ms |
| 5000 | ~10000ms (10s) | ~100ms |

**Recommendation**: Always keep enabled (default)

---

### Dataset Size Limits

| Scenario | Recommended Max | Notes |
|----------|----------------|-------|
| **Desktop (Grid view)** | 5000 records | With virtualization |
| **Desktop (Card view)** | 1000 records | More complex rendering |
| **Mobile/Tablet** | 500 records | Limited screen space |
| **Without Virtualization** | 100 records | Performance degrades |

**Best Practice**: Use view filters to limit dataset size.

---

## Accessibility

### WCAG 2.1 AA Compliance

The component meets WCAG 2.1 AA standards:

- **Keyboard Navigation**: All features accessible via keyboard
- **Screen Reader Support**: ARIA labels and roles
- **Focus Management**: Visible focus indicators
- **Color Contrast**: Meets 4.5:1 ratio
- **Responsive Text**: Supports text scaling up to 200%

### Screen Reader Support

**Tested with**:
- NVDA (Windows)
- JAWS (Windows)
- VoiceOver (macOS, iOS)
- TalkBack (Android)

**Announcements**:
- Row selection: "Row selected, 2 of 10 selected"
- Command execution: "Refresh complete, 10 records loaded"
- View mode change: "Switched to Card view"
- Errors: "Error: Select at least 1 record"

### Keyboard-Only Usage

**Complete workflow without mouse**:
1. **Tab** to grid
2. **Arrow keys** to navigate rows
3. **Space** to select rows
4. **Tab** to toolbar
5. **Enter** to execute command

---

## Mobile & Tablet

### Responsive Design

The grid adapts to screen size:

**Desktop (>1024px)**:
- Full Grid view with all columns
- Normal toolbar (icon + label)
- 4-column Card view

**Tablet (768px - 1024px)**:
- Compact Grid with fewer columns
- Compact toolbar (icon only)
- 2-column Card view

**Mobile (<768px)**:
- List view recommended
- Compact toolbar
- 1-column Card view

### Touch Gestures

- **Tap**: Select row
- **Long Press**: Context menu (if enabled)
- **Swipe**: Scroll (vertical/horizontal)

### Configuration for Mobile

```json
{
  "defaultConfig": {
    "viewMode": "List",
    "compactToolbar": true,
    "enabledCommands": ["open", "create", "refresh"],
    "enableKeyboardShortcuts": false
  }
}
```

---

## Common Scenarios

### Scenario 1: Replace Standard Grid

**Goal**: Replace default grid on Account form

**Steps**:
1. Open **Account > Main Form** in form designer
2. Add Universal Dataset Grid control
3. Configure view mode: `"Grid"`
4. Enable default commands
5. Publish

**Configuration**:
```json
{
  "schemaVersion": "1.0",
  "defaultConfig": {
    "viewMode": "Grid",
    "enabledCommands": ["open", "create", "delete", "refresh"]
  }
}
```

---

### Scenario 2: Document Library

**Goal**: Display documents with upload/download commands

**Steps**:
1. Create custom entity: `sprk_document`
2. Add Universal Dataset Grid to view
3. Configure Card view with custom commands
4. Create Custom APIs for upload/download

**Configuration**:
```json
{
  "schemaVersion": "1.0",
  "entityConfigs": {
    "sprk_document": {
      "viewMode": "Card",
      "compactToolbar": true,
      "customCommands": {
        "upload": {
          "label": "Upload",
          "icon": "CloudUpload",
          "actionType": "customapi",
          "actionName": "sprk_UploadDocument"
        },
        "download": {
          "label": "Download",
          "icon": "CloudDownload",
          "actionType": "customapi",
          "actionName": "sprk_DownloadDocument",
          "requiresSelection": true
        }
      }
    }
  }
}
```

---

### Scenario 3: Approval Workflow

**Goal**: Bulk approve/reject invoices

**Steps**:
1. Add grid to `sprk_invoice` view
2. Add Approve and Reject custom commands
3. Create Custom APIs for approval logic

**Configuration**:
```json
{
  "schemaVersion": "1.0",
  "entityConfigs": {
    "sprk_invoice": {
      "viewMode": "Grid",
      "customCommands": {
        "approve": {
          "label": "Approve",
          "icon": "Checkmark",
          "actionType": "customapi",
          "actionName": "sprk_ApproveInvoice",
          "requiresSelection": true,
          "minSelection": 1,
          "maxSelection": 10,
          "confirmationMessage": "Approve {selectedCount} invoice(s)?",
          "refresh": true
        },
        "reject": {
          "label": "Reject",
          "icon": "Dismiss",
          "actionType": "customapi",
          "actionName": "sprk_RejectInvoice",
          "requiresSelection": true,
          "confirmationMessage": "Reject {selectedCount} invoice(s)?",
          "refresh": true
        }
      }
    }
  }
}
```

---

### Scenario 4: Mobile Task List

**Goal**: Optimized task list for mobile field workers

**Configuration**:
```json
{
  "schemaVersion": "1.0",
  "entityConfigs": {
    "task": {
      "viewMode": "List",
      "compactToolbar": true,
      "enabledCommands": ["open", "create", "refresh"],
      "enableKeyboardShortcuts": false,
      "customCommands": {
        "complete": {
          "label": "Complete",
          "icon": "Checkmark",
          "actionType": "customapi",
          "actionName": "sprk_CompleteTask",
          "requiresSelection": true,
          "refresh": true
        }
      }
    }
  }
}
```

---

## Troubleshooting

### Grid Not Rendering

**Symptom**: Blank area where grid should appear

**Solutions**:
1. Check browser console for errors
2. Verify solution imported successfully
3. Refresh browser cache (Ctrl+Shift+R)
4. Ensure dataset property is bound

---

### Commands Not Working

**Symptom**: Command buttons disabled or not appearing

**Solutions**:
1. **Disabled**: Select appropriate number of records
2. **Not appearing**: Check configuration syntax
3. **No response**: Check browser console, verify Custom API registered
4. **Permission error**: Check user security role

---

### Performance Issues

**Symptom**: Grid slow to load or scroll

**Solutions**:
1. Enable virtualization:
   ```json
   { "enableVirtualization": true }
   ```
2. Reduce dataset size (use view filters)
3. Reduce number of columns
4. Use Grid view instead of Card view
5. Check network latency (browser DevTools > Network)

---

### Mobile Display Issues

**Symptom**: Grid doesn't fit on mobile screen

**Solutions**:
1. Use List or Card view (not Grid)
2. Enable compact toolbar
3. Reduce number of visible columns
4. Test on actual device (not just browser resize)

---

## Best Practices

### 1. Choose the Right View Mode

- **Grid**: Tabular data, desktop users
- **List**: Simple lists, mobile users
- **Card**: Visual content, documents, products

### 2. Limit Dataset Size

- Use view filters to reduce record count
- Keep datasets <5000 records for best performance
- Consider pagination for very large datasets

### 3. Use Compact Toolbar on Mobile

```json
{
  "compactToolbar": true
}
```

### 4. Enable Only Needed Commands

```json
{
  "enabledCommands": ["open", "refresh"]  // Remove unused commands
}
```

### 5. Add Confirmation for Destructive Actions

```json
{
  "customCommands": {
    "delete": {
      "confirmationMessage": "Delete {selectedCount} record(s)?"
    }
  }
}
```

### 6. Test Accessibility

- Tab through all features
- Test with screen reader
- Verify keyboard shortcuts work
- Check color contrast

---

## Next Steps

- [Configuration Guide](./ConfigurationGuide.md) - Deep dive into configuration options
- [Custom Commands Guide](./CustomCommands.md) - Create custom commands
- [Developer Guide](./DeveloperGuide.md) - Architecture and extension
- [API Reference](../api/UniversalDatasetGrid.md) - Complete API documentation
- [Examples](../examples/) - More usage examples
