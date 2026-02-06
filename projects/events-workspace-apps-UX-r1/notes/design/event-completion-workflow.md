# Event Completion Workflow - Design Document

> **Created**: 2026-02-04
> **Updated**: 2026-02-05
> **Status**: Implementation In Progress
> **Category**: Post-Project Enhancement

---

## Overview

This document captures the design decisions for enhancing the Events system with:
1. Task completion workflow (Complete, Cancel, Reschedule, Reassign)
2. Event History tracking
3. Notes entity with polymorphic lookups
4. Event Type-specific side pane forms

---

## 1. Event History (JSON Field Approach)

### Design Decision: JSON Field on Event Entity

After evaluating options (separate entity vs. JSON field), the **JSON field approach** was selected for:
- Simpler implementation (no additional entity to manage)
- All history travels with the Event record
- Sufficient for audit trail needs
- Easier to display in a timeline control

### Field Schema: `sprk_eventhistory` (Multiline Text on Event)

The field stores a JSON array of history entries:

```json
[
  {
    "timestamp": "2026-02-04T10:30:00Z",
    "action": "completed",
    "userId": "00000000-0000-0000-0000-000000000000",
    "userName": "John Smith",
    "details": "Task completed successfully"
  },
  {
    "timestamp": "2026-02-03T14:15:00Z",
    "action": "rescheduled",
    "userId": "00000000-0000-0000-0000-000000000000",
    "userName": "Jane Doe",
    "details": "Due date changed from 2026-02-05 to 2026-02-10"
  }
]
```

### Action Type Values

| Action | Description |
|--------|-------------|
| `completed` | Task marked complete |
| `cancelled` | Task cancelled |
| `rescheduled` | Due date changed |
| `reassigned` | Owner changed |
| `note_added` | Memo/note recorded |
| `status_changed` | Other status updates |

### Implementation Notes

- JavaScript appends new entries to the JSON array (prepend for most recent first)
- `userId` and `userName` captured from current user context
- `details` is optional - used for reasons/comments
- Field should be read-only on forms (populated by ribbon commands only)

---

## 2. Memo Entity (Polymorphic with EntityResolver)

### Design Decision: Uses Existing EntityResolver Pattern

The Memo entity (`sprk_memo`) follows the same polymorphic pattern as Event, using the existing `AssociationResolver` PCF control and `sprk_recordtype_ref` reference table.

### Entity Schema: `sprk_memo`

#### Entity-Specific Lookup Fields (Already Created)

| Field | Type | Target Entity |
|-------|------|---------------|
| `sprk_regardingbudget` | Lookup | Budget |
| `sprk_regardingevent` | Lookup | Event |
| `sprk_regardinginvoice` | Lookup | Invoice |
| `sprk_regardingmatter` | Lookup | Matter |
| `sprk_regardingproject` | Lookup | Project |
| `sprk_regardingworkassignment` | Lookup | Work Assignment |

#### Denormalized Fields (Required for Resolver)

| Field | Type | Description |
|-------|------|-------------|
| `sprk_regardingrecordname` | Single Line Text (200) | Parent record display name |
| `sprk_regardingrecordid` | Single Line Text (50) | Parent record GUID |
| `sprk_regardingrecordtype` | Lookup to `sprk_recordtype_ref` | Which entity type the parent is |
| `sprk_regardingrecordurl` | URL | Clickable link to parent record |

#### Other Memo Fields

| Field | Type | Description |
|-------|------|-------------|
| `sprk_memoid` | GUID | Primary Key |
| `sprk_name` | String | Memo title/subject |
| `sprk_description` | Multi-line Text | Memo content |
| `sprk_memotype` | Choice | Memo type (General, Follow-up, etc.) |

### Reference Table Setup (`sprk_recordtype_ref`)

Ensure these records exist in the reference table:

| sprk_recordlogicalname | sprk_recorddisplayname | sprk_regardingfield |
|------------------------|------------------------|---------------------|
| `sprk_event` | Event | `sprk_regardingevent` |
| `sprk_matter` | Matter | `sprk_regardingmatter` |
| `sprk_project` | Project | `sprk_regardingproject` |
| `sprk_budget` | Budget | `sprk_regardingbudget` |
| `sprk_invoice` | Invoice | `sprk_regardinginvoice` |
| `sprk_workassignment` | Work Assignment | `sprk_regardingworkassignment` |

### PCF Control Integration

Add `AssociationResolver` PCF control to the Memo Main form:
- Bound property: `sprk_regardingrecordtype`
- The control will auto-detect pre-populated lookups and handle denormalized field population

---

## 3. UX Patterns

### Pattern Summary

| Interaction | UX Pattern | Implementation |
|-------------|------------|----------------|
| Regarding link click | **Dialog** | Open record in modal dialog |
| Row click (grid) | **Side Pane** | Open Event in side pane form |
| New record | **Quick Create** | Open quick create form |
| Actions (Complete, etc.) | **Ribbon Button** | Execute action with optional dialog |

### Side Pane Form Mapping by Event Type

The side pane should open the appropriate form based on Event Type:

| Event Type | Form Name | Form ID (TBD) |
|------------|-----------|---------------|
| Action | Event - Action Form | `{guid}` |
| Due Date | Event - Due Date Form | `{guid}` |
| Meeting | Event - Meeting Form | `{guid}` |
| Reminder | Event - Reminder Form | `{guid}` |
| Default | Event - Default Form | `{guid}` |

**Implementation**: Update `openEventDetailPane()` to use `pageType: "entityrecord"` with form mapping:

```typescript
// Pseudo-code for form-based side pane navigation
const formMapping: Record<string, string> = {
  'action-type-guid': 'action-form-guid',
  'duedate-type-guid': 'duedate-form-guid',
  // ... etc
};

async function openEventInSidePane(eventId: string, eventTypeId: string) {
  const formId = formMapping[eventTypeId] || defaultFormId;

  await pane.navigate({
    pageType: "entityrecord",
    entityName: "sprk_event",
    entityId: eventId,
    formId: formId
  });
}
```

---

## 4. Ribbon Buttons

### Command Bar Buttons (Event Entity)

| Button | Label | Icon | Action |
|--------|-------|------|--------|
| Complete | Complete | CheckmarkCircle | Set status to Complete, create history |
| Cancel | Cancel | DismissCircle | Set status to Cancelled, create history |
| Reschedule | Reschedule | CalendarEdit | Open reschedule dialog, create history |
| Reassign | Reassign | PersonSwap | Open assign dialog, create history |
| Add Note | Add Note | NoteAdd | Open quick create note form |

### Implementation: Following docs/guides/RIBBON-WORKBENCH-HOW-TO-ADD-BUTTON.md

#### Step 1: Create Web Resource

File: `sprk_event_ribbon_commands.js`

```javascript
// Event Ribbon Commands - Task Completion Workflow

/**
 * Complete Event
 * Sets status to Complete and creates Event History record
 */
function Spaarke_CompleteEvent(formContext) {
    const eventId = formContext.data.entity.getId().replace(/[{}]/g, '');
    const eventName = formContext.getAttribute("sprk_name")?.getValue() || "Event";

    // Confirm action
    Xrm.Navigation.openConfirmDialog({
        title: "Complete Event",
        text: `Mark "${eventName}" as complete?`,
        confirmButtonLabel: "Complete",
        cancelButtonLabel: "Cancel"
    }).then(function(result) {
        if (result.confirmed) {
            // Update Event status
            const updateData = {
                statuscode: 100000001, // Complete status
                sprk_completeddate: new Date().toISOString()
            };

            Xrm.WebApi.updateRecord("sprk_event", eventId, updateData)
                .then(function() {
                    // Create Event History record
                    return Xrm.WebApi.createRecord("sprk_eventhistory", {
                        "sprk_eventid@odata.bind": `/sprk_events(${eventId})`,
                        sprk_actiontype: 100000000, // Completed
                        sprk_actiondate: new Date().toISOString()
                    });
                })
                .then(function() {
                    formContext.data.refresh(false);
                    Xrm.Utility.showNotification("Event completed successfully", "INFO");
                })
                .catch(function(error) {
                    Xrm.Utility.alertDialog("Error: " + error.message);
                });
        }
    });
}

/**
 * Cancel Event
 * Sets status to Cancelled and creates Event History record
 */
function Spaarke_CancelEvent(formContext) {
    const eventId = formContext.data.entity.getId().replace(/[{}]/g, '');
    const eventName = formContext.getAttribute("sprk_name")?.getValue() || "Event";

    // Open dialog to get cancellation reason
    Xrm.Navigation.openDialog("sprk_cancellationdialog", {
        // ... dialog options
    }).then(function(result) {
        if (result.confirmed) {
            const reason = result.parameters?.reason || "";

            // Update Event status
            const updateData = {
                statuscode: 100000002 // Cancelled status
            };

            Xrm.WebApi.updateRecord("sprk_event", eventId, updateData)
                .then(function() {
                    return Xrm.WebApi.createRecord("sprk_eventhistory", {
                        "sprk_eventid@odata.bind": `/sprk_events(${eventId})`,
                        sprk_actiontype: 100000001, // Cancelled
                        sprk_actiondate: new Date().toISOString(),
                        sprk_details: reason
                    });
                })
                .then(function() {
                    formContext.data.refresh(false);
                    Xrm.Utility.showNotification("Event cancelled", "INFO");
                });
        }
    });
}

/**
 * Add Note to Event
 * Opens Quick Create form for Note entity
 */
function Spaarke_AddEventNote(formContext) {
    const eventId = formContext.data.entity.getId().replace(/[{}]/g, '');

    Xrm.Navigation.openForm({
        entityName: "sprk_note",
        useQuickCreateForm: true,
        createFromEntity: {
            entityType: "sprk_event",
            id: eventId
        }
    });
}
```

#### Step 2: Ribbon XML Definition

```xml
<RibbonDiffXml>
  <CustomActions>
    <!-- Complete Button -->
    <CustomAction Id="Spaarke.Event.Complete.CustomAction"
                  Location="Mscrm.Form.sprk_event.MainTab.Actions.Controls._children"
                  Sequence="10">
      <CommandUIDefinition>
        <Button Id="Spaarke.Event.Complete.Button"
                Command="Spaarke.Event.Complete.Command"
                LabelText="Complete"
                Alt="Complete Event"
                ToolTipTitle="Complete Event"
                ToolTipDescription="Mark this event as complete"
                TemplateAlias="o1"
                Image16by16="/_imgs/ribbon/CheckmarkCircle_16.png"
                Image32by32="/_imgs/ribbon/CheckmarkCircle_32.png" />
      </CommandUIDefinition>
    </CustomAction>

    <!-- Cancel Button -->
    <CustomAction Id="Spaarke.Event.Cancel.CustomAction"
                  Location="Mscrm.Form.sprk_event.MainTab.Actions.Controls._children"
                  Sequence="20">
      <CommandUIDefinition>
        <Button Id="Spaarke.Event.Cancel.Button"
                Command="Spaarke.Event.Cancel.Command"
                LabelText="Cancel"
                Alt="Cancel Event"
                ToolTipTitle="Cancel Event"
                ToolTipDescription="Cancel this event"
                TemplateAlias="o1"
                Image16by16="/_imgs/ribbon/DismissCircle_16.png"
                Image32by32="/_imgs/ribbon/DismissCircle_32.png" />
      </CommandUIDefinition>
    </CustomAction>

    <!-- Add Note Button -->
    <CustomAction Id="Spaarke.Event.AddNote.CustomAction"
                  Location="Mscrm.Form.sprk_event.MainTab.Actions.Controls._children"
                  Sequence="30">
      <CommandUIDefinition>
        <Button Id="Spaarke.Event.AddNote.Button"
                Command="Spaarke.Event.AddNote.Command"
                LabelText="Add Note"
                Alt="Add Note"
                ToolTipTitle="Add Note"
                ToolTipDescription="Add a note to this event"
                TemplateAlias="o1"
                Image16by16="/_imgs/ribbon/NoteAdd_16.png"
                Image32by32="/_imgs/ribbon/NoteAdd_32.png" />
      </CommandUIDefinition>
    </CustomAction>
  </CustomActions>

  <CommandDefinitions>
    <CommandDefinition Id="Spaarke.Event.Complete.Command">
      <EnableRules>
        <EnableRule Id="Spaarke.Event.Complete.EnableRule" />
      </EnableRules>
      <DisplayRules />
      <Actions>
        <JavaScriptFunction Library="$webresource:sprk_event_ribbon_commands.js"
                           FunctionName="Spaarke_CompleteEvent">
          <CrmParameter Value="PrimaryControl" />
        </JavaScriptFunction>
      </Actions>
    </CommandDefinition>

    <CommandDefinition Id="Spaarke.Event.Cancel.Command">
      <EnableRules>
        <EnableRule Id="Spaarke.Event.Cancel.EnableRule" />
      </EnableRules>
      <DisplayRules />
      <Actions>
        <JavaScriptFunction Library="$webresource:sprk_event_ribbon_commands.js"
                           FunctionName="Spaarke_CancelEvent">
          <CrmParameter Value="PrimaryControl" />
        </JavaScriptFunction>
      </Actions>
    </CommandDefinition>

    <CommandDefinition Id="Spaarke.Event.AddNote.Command">
      <EnableRules />
      <DisplayRules />
      <Actions>
        <JavaScriptFunction Library="$webresource:sprk_event_ribbon_commands.js"
                           FunctionName="Spaarke_AddEventNote">
          <CrmParameter Value="PrimaryControl" />
        </JavaScriptFunction>
      </Actions>
    </CommandDefinition>
  </CommandDefinitions>

  <RuleDefinitions>
    <EnableRules>
      <!-- Only enable Complete/Cancel for active events -->
      <EnableRule Id="Spaarke.Event.Complete.EnableRule">
        <CustomRule Library="$webresource:sprk_event_ribbon_commands.js"
                    FunctionName="Spaarke_IsEventActive">
          <CrmParameter Value="PrimaryControl" />
        </CustomRule>
      </EnableRule>
      <EnableRule Id="Spaarke.Event.Cancel.EnableRule">
        <CustomRule Library="$webresource:sprk_event_ribbon_commands.js"
                    FunctionName="Spaarke_IsEventActive">
          <CrmParameter Value="PrimaryControl" />
        </CustomRule>
      </EnableRule>
    </EnableRules>
  </RuleDefinitions>
</RibbonDiffXml>
```

---

## 5. Implementation Checklist

### Prerequisites (Manual Steps)

- [x] Create `sprk_eventhistory` field on Event entity (Multiline Text - JSON)
- [x] Create `sprk_memo` entity with polymorphic lookups (created as Memo, not Note)
- [x] Add denormalized fields to `sprk_memo`:
  - [x] `sprk_RegardingRecordName` (Single Line Text)
  - [x] `sprk_RegardingRecordId` (Single Line Text)
  - [x] `sprk_RegardingRecordType` (Lookup to `sprk_recordtype_ref`)
  - [x] `sprk_RegardingRecordURL` (URL)
- [ ] Add records to `sprk_recordtype_ref` for each parent entity type (if not already present)
- [x] Create Event side pane form: "Event Task side pane form" (`c4d7c4ee-4502-f111-8406-7c1e525abd8b`)
- [ ] Create additional Event Type-specific forms (optional for MVP)

### Code Implementation

- [x] Create `sprk_event_ribbon_commands.js` web resource (uses JSON field approach)
- [x] Create `EventRibbonDiffXml.xml` ribbon customization definition
- [ ] Upload as web resource: `sprk_event_ribbon_commands`
- [ ] Import ribbon XML via Ribbon Workbench or solution XML
- [x] Update `EventsPage/App.tsx` to use native form navigation (v1.6.0)
  - Uses `pageType: "entityrecord"` with `formId: c4d7c4ee-4502-f111-8406-7c1e525abd8b`
- [ ] Add AssociationResolver PCF to Memo Main form
- [ ] Complete PCF controls on main records (Matter, Project forms)
- [ ] Test Complete/Cancel/Reschedule/Reassign buttons
- [ ] Test Add Memo with Quick Create form
- [ ] Verify Event History JSON is populated correctly

### Testing

- [ ] Complete action appends history entry to JSON field
- [ ] Cancel action captures reason in history details
- [ ] Reschedule action records old/new dates
- [ ] Reassign action records old/new owner
- [ ] Side pane opens correct form based on Event Type
- [ ] Memos can be queried across parent entities via resolver
- [ ] Ribbon buttons only enabled for active events

---

## 6. Future Considerations

### Event History Timeline View

Consider a custom PCF control that displays Event History as a timeline visualization on the Event form.

### Bulk Actions from Grid

The Events Custom Page grid could support bulk Complete/Reassign via multi-select and toolbar buttons.

### Notes Full-Text Search

Consider Azure Cognitive Search integration for full-text search across Notes content.

---

## 7. Created Files

| File | Purpose |
|------|---------|
| `src/solutions/EventCommands/sprk_event_ribbon_commands.js` | JavaScript for ribbon button commands |
| `src/solutions/EventCommands/EventRibbonDiffXml.xml` | Ribbon XML for Ribbon Workbench |
| `src/solutions/EventCommands/README.md` | Deployment instructions |
| `src/solutions/EventsPage/src/App.tsx` | Updated v1.6.0 with native form side pane |

---

*Design document for Event Completion Workflow enhancements.*
*Created: 2026-02-04*
*Updated: 2026-02-05*
