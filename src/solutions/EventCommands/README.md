# Event Ribbon Commands

This folder contains the ribbon customization for Event entity command bar buttons.

## Contents

| File | Purpose |
|------|---------|
| `sprk_event_ribbon_commands.js` | JavaScript web resource for button actions |
| `EventRibbonDiffXml.xml` | Ribbon XML definition for Ribbon Workbench |

## Buttons Included

### Main Form Command Bar
| Button | Action | Enable Rule |
|--------|--------|-------------|
| **Complete** | Mark event as complete | Active events only |
| **Cancel** | Cancel event with history | Active events only |
| **Reschedule** | Change due date | Active events with due date |
| **Reassign** | Assign to another user | Active events only |
| **Add Memo** | Create memo for event | Always enabled |

### Subgrid Command Bar (Events tab on Matter/Project)
| Button | Action | Enable Rule |
|--------|--------|-------------|
| **Complete Selected** | Bulk complete events | At least one selected |

## Deployment Steps

### Step 1: Upload Web Resource

1. Open Power Apps maker portal: https://make.powerapps.com
2. Select your environment (e.g., spaarkedev1)
3. Navigate to **Solutions** → Open your solution
4. Click **+ New** → **More** → **Web resource**
5. Configure:
   - **Display name**: Event Ribbon Commands
   - **Name**: `sprk_event_ribbon_commands` (no `.js` extension)
   - **Type**: JavaScript (JScript)
   - Upload `sprk_event_ribbon_commands.js`
6. Click **Save** then **Publish**

### Step 2: Add Ribbon Customization

**Option A: Using Ribbon Workbench (Recommended)**

1. Install Ribbon Workbench from XrmToolBox
2. Open solution containing Event entity
3. Click **Import XML**
4. Paste contents of `EventRibbonDiffXml.xml`
5. Publish customizations

**Option B: Manual XML Editing**

1. Export the solution containing Event entity
2. Extract the solution zip
3. Edit `customizations.xml`
4. Find the `<Entity>` node for `sprk_event`
5. Add the `<RibbonDiffXml>` content inside the Entity node
6. Repackage and import the solution
7. Publish customizations

### Step 3: Configure Icons (Optional)

The ribbon XML references custom icon web resources. You can either:

**Option A: Create Custom Icons**
- Create SVG icons (16x16 and 32x32) and upload as web resources
- Use paths matching the XML: `sprk_/icons/{name}_{size}.svg`

**Option B: Use Standard Dynamics Icons**
Replace the image references in the XML with standard icon paths:
```xml
<!-- Instead of custom icons -->
Image16by16="/_imgs/ribbon/check16.png"
Image32by32="/_imgs/ribbon/check32.png"

<!-- Or use web resource icon placeholders -->
Image16by16="$webresource:sprk_/icons/placeholder_16.png"
```

**Option C: Use Fluent UI Icons (via web resource)**
Upload Fluent UI icon SVGs as web resources and reference them.

### Step 4: Test

1. Open an Event record
2. Verify buttons appear in command bar
3. Test each button:
   - Complete: Sets status, adds history entry
   - Cancel: Sets status, adds history entry
   - Reschedule: Prompts for new date, updates, adds history
   - Reassign: Opens user lookup, reassigns, adds history
   - Add Memo: Opens Quick Create form for Memo entity
4. Verify Event History JSON field is populated correctly
5. Test subgrid "Complete Selected" from Matter/Project Events tab

## Event History JSON Format

The `sprk_eventhistory` field stores a JSON array:

```json
[
  {
    "timestamp": "2026-02-05T10:30:00Z",
    "action": "completed",
    "userId": "00000000-0000-0000-0000-000000000000",
    "userName": "John Smith",
    "details": null
  },
  {
    "timestamp": "2026-02-04T14:15:00Z",
    "action": "rescheduled",
    "userId": "00000000-0000-0000-0000-000000000000",
    "userName": "Jane Doe",
    "details": "Due date changed from 2/5/2026 to 2/10/2026"
  }
]
```

### Action Types

| Action | Description |
|--------|-------------|
| `completed` | Event marked complete |
| `cancelled` | Event cancelled |
| `rescheduled` | Due date changed |
| `reassigned` | Owner changed |
| `memo_added` | Memo created for event |
| `status_changed` | Other status update |

## Troubleshooting

### Buttons not appearing
- Verify web resource is published
- Check browser console for JavaScript errors
- Ensure solution is imported and published
- Clear browser cache and reload form

### Enable rules not working
- Verify `statuscode` field values match `Spaarke.Event.StatusCode` constants
- Check `sprk_duedate` field exists on form (for Reschedule)
- Verify JavaScript functions are accessible in global scope

### Event History not updating
- Ensure `sprk_eventhistory` field is on the form (can be hidden)
- Check browser console for JSON parse errors
- Verify field permissions allow write access

## Related Files

- Design doc: `projects/events-workspace-apps-UX-r1/notes/design/event-completion-workflow.md`
- Events Page: `src/solutions/EventsPage/src/App.tsx`
- PCF Controls: `src/client/pcf/DueDatesWidget/`, `src/client/pcf/EventCalendarFilter/`
