# Events and Workflow Automation - User Guide

## Introduction

This guide explains how to use the Events and Workflow Automation features in Dataverse. Events are scheduled activities, deadlines, reminders, and notifications that are associated with your business records (such as Matters, Projects, Invoices, Accounts, and Contacts).

With the Events system, you can:
- Create events and link them to any supported business record
- Let the system automatically populate event details from the parent record
- Manually refresh values from the parent record when needed
- Track all events in a unified view across your organization

---

## Getting Started

### What is an Event?

An Event represents a scheduled activity, deadline, reminder, or notification tied to your work. Each event can be linked to a parent record (Regarding), and the system can automatically populate certain fields based on your parent record's information.

**Key Benefits:**
- **Centralized View**: See all events across all business entities in one place
- **Automatic Population**: Let the system fill in details from the parent record (e.g., client name from a Matter)
- **Flexible Linking**: Link events to any supported record type with a single search interface
- **Status Tracking**: Track event progress from planned to completed or cancelled

### Supported Record Types

You can link Events to any of these business entities:

| Record Type | Example Usage |
|------------|---------------|
| **Matter** | Create deadline event for case work |
| **Project** | Create milestone event for project phases |
| **Invoice** | Create payment reminder event |
| **Analysis** | Create review completion event |
| **Account** | Create business development event |
| **Contact** | Create follow-up event |
| **Work Assignment** | Create task completion event |
| **Budget** | Create budget review event |

---

## Creating an Event

### Step 1: Open the Event Form

1. Navigate to **Events** in the left navigation menu
2. Click **+ New** to create a new event

### Step 2: Select the Parent Record (Regarding)

1. In the **Regarding** section, look for the field that lets you select the parent record
2. Click the lookup button to search for a record
3. In the search dialog:
   - **Select the record type** you want to link to (Matter, Project, Account, etc.)
   - **Type the record name** to search
   - Select the correct record from the results

**Tip**: The search works across all supported record types, so you don't need to know which type of record you're looking for—just start typing the name.

### Step 3: Fill in Required Fields

Once you've selected a parent record, the system will automatically fill in certain fields based on that record's data. You still need to complete:

- **Event Name**: A brief description of what this event is about (e.g., "Client Call", "Deadline")
- **Event Type**: Select from dropdown options (Meeting, Deadline, Reminder, Task, etc.)
- **Due Date** or **Base Date**: When this event is scheduled (depending on the Event Type you selected)

**What Gets Auto-Populated?**

When you select a parent record, the system automatically pulls information like:
- Client or Account name
- Project description
- Other relevant details from the parent record

These auto-populated fields save you time and ensure consistency across your records.

### Step 4: Add Optional Details

You can add additional information to your event:

- **Priority**: Mark as Low, Normal, High, or Urgent
- **Description**: Add notes or details about the event
- **Related Events**: Link this event to other events (for connected activities)

### Step 5: Save the Event

1. Click **Save** to create the event
2. The system records the event creation in the Event Log automatically

**Note**: Once saved, you can continue editing the event by clicking **Edit** if you need to make changes.

---

## Understanding Event Types

Different Event Types show different fields and have different requirements. Your administrator configures which fields are required for each type.

### Common Event Types

| Event Type | Typical Use | Key Fields |
|-----------|------------|-----------|
| **Meeting** | Schedule a meeting or call | Start Date, End Date, Location |
| **Deadline** | Important deadline date | Due Date (required) |
| **Reminder** | Reminder to follow up | Remind At date |
| **Task** | Task to complete | Due Date, Priority |
| **Notification** | Notification to send | Date, Description |

When you select an Event Type, notice that:
- **Some fields appear or disappear** based on the type
- **Different fields become required** (you'll see a red asterisk *)
- The form adapts to what's needed for that type of event

---

## Linking to a Parent Record (Regarding)

### How the Regarding Selector Works

When you see the **Regarding** section on the Event form:

1. **Click the lookup button** to open the record picker
2. **Select the entity type** (Matter, Project, Account, etc.) from the dropdown
3. **Type the record name** in the search box (e.g., "Acme Corporation Matter" or "Project Alpha")
4. **Select the record** from the results list
5. The system **automatically fills in all entity-specific fields** (Regarding Matter, Regarding Account, etc.)

**Important**: Only one parent record can be selected. If you select a different record, the previous selection is cleared automatically.

### Changing the Parent Record

If you need to link the event to a different parent record:

1. Click the **X** or **Clear** button next to the Regarding field
2. Select a new parent record using the steps above

---

## Field Mapping Features

### Auto-Population on Creation

When you create an Event and link it to a parent record, the system automatically populates certain fields with information from that parent record.

**Example Scenario:**
- You create an Event for **Matter ABC**
- The system automatically copies:
  - Client name from the Matter
  - Matter description
  - Other configured fields from the Matter to the Event

This happens **once when you create the event** and ensures all information is current at that moment.

### Refreshing Values from Parent Record

If the parent record has been updated after you created the event, you can manually refresh the event's fields with the latest information from the parent.

#### How to Refresh from Parent

1. Open the Event form
2. Look for the **Refresh from Parent** button in the command bar
3. Click **Refresh from Parent**
4. Confirm the action (a dialog will ask if you want to proceed)
5. The system fetches the current values from the parent record and updates the Event

**What Gets Refreshed?**
All fields that were configured for automatic population from the parent record will be updated with current values.

**Important**: This overwrites any changes you've made to these fields. If you've manually edited a field after creating the event, the refresh will replace your changes with the parent's current values.

### Using the Update Related Feature (Admin Only)

The **Update Related** button allows administrators or authorized users to push updates to all Events that are linked to a parent record.

**When This Is Used:**
- An administrator updates a Matter's client information
- They want all Events linked to that Matter to reflect the new client information
- They click **Update Related** to push these changes to all Events at once

**Note**: This feature is typically available only to users with administrative permissions.

---

## Viewing Events

### All Events View

View all events across your organization in a unified list:

1. Navigate to **Events** in the left navigation
2. You'll see the **All Events** view showing events from all business entities
3. Click on an Event name to open it

**Features Available:**
- **Sort**: Click column headers to sort by Event Name, Due Date, Priority, or Status
- **Filter**: Use the filter options to show only specific Event Types or statuses
- **Search**: Use the search box to find events by name or linked record

### Events Linked to a Specific Record

To see events linked to a particular Matter, Account, Project, or other record:

1. Open the parent record (e.g., a Matter)
2. Look for the **Related Events** section (this may be labeled as "Events" or "Related Events subgrid")
3. You'll see a list of all events linked to that record
4. Click on an Event to open it

---

## Completing and Cancelling Events

### Marking an Event Complete

1. Open the Event form
2. Change the **Status Reason** to "Completed"
3. Enter the **Completed Date** if required
4. Click **Save**

The system automatically records this status change in the Event Log.

### Cancelling an Event

1. Open the Event form
2. Change the **Status Reason** to "Cancelled"
3. Click **Save** (add notes in the Description if you want to document why it was cancelled)

The system records the cancellation in the Event Log.

---

## Event Status and Tracking

### Event Statuses

| Status | Meaning | Can Be Edited |
|--------|---------|--------------|
| **Draft** | Event being prepared, not yet planned | Yes |
| **Planned** | Event is scheduled and confirmed | Yes |
| **Open** | Event is in progress | Yes |
| **On Hold** | Event is postponed temporarily | Yes |
| **Completed** | Event has been finished | Yes |
| **Cancelled** | Event is no longer happening | No |
| **Deleted** | Event has been deleted | No |

### Event Log

Every time an Event is created, updated, completed, cancelled, or deleted, the system automatically creates an entry in the **Event Log**.

**How to View Event Log:**
1. Open the Event form
2. Look for the **Related Event Log** section or tab
3. You'll see a chronological list of all changes to that Event
4. Click on an Event Log entry to see details

**What's Recorded:**
- Action (Created, Updated, Completed, Cancelled, Deleted)
- Who made the change (user name)
- When the change occurred (timestamp)
- Description of what changed (if applicable)

---

## Priority and Sorting

### Setting Event Priority

Events can be marked with different priority levels to help you focus on what's important:

| Priority | Use When |
|----------|----------|
| **Low** | Routine events, can be deferred |
| **Normal** | Standard events, on normal schedule |
| **High** | Important events, need attention soon |
| **Urgent** | Critical events, need immediate attention |

You can set priority when creating an event or by editing an existing event.

### Using Priority to Filter

1. In the All Events view, look for filter options
2. Select **Priority** filter
3. Choose the priorities you want to see (e.g., "High" and "Urgent")
4. The list updates to show only events with those priorities

---

## Tips and Tricks

### Efficient Event Creation

- **Start with the parent record**: If you're viewing a Matter or Account, look for a button to "Create Event" directly—this pre-selects that record as the parent
- **Copy from related events**: If you need to create similar events, open an existing event and save it as a copy, then modify the details
- **Use consistent naming**: Give events clear, descriptive names to make them easy to find later

### Finding Events Quickly

- **Use the search bar** in the All Events view to search by event name or linked record name
- **Filter by Event Type** to narrow down to specific kinds of events
- **Sort by Due Date** to see what's coming up next
- **Filter by Priority** to focus on urgent items first

### Keeping Data Consistent

- **Refresh from Parent** when the parent record changes and you want the Event details to match
- **Use the automatic field population** feature rather than manually entering duplicate information
- **Add descriptive notes** in the Event description to document decisions or important context

---

## FAQ

### Q: Why do some fields not show on the Event form?
**A:** Field visibility depends on the Event Type you selected. Different Event Types show different fields because they have different requirements and purposes. If you need a field that's not showing, check that you've selected the correct Event Type.

### Q: Can I change the parent record after saving the event?
**A:** Yes. You can clear the current Regarding selection and choose a new parent record. Be aware that clearing the old selection will clear all entity-specific lookup fields for that entity. If you had any manual entries in other fields, those will remain.

### Q: What happens if the parent record is deleted?
**A:** If a parent record (like a Matter or Account) is deleted, the Event will remain but will no longer be linked to that parent. The Event still exists with any data you've entered, but the Regarding fields will be empty.

### Q: Can I see all events for a user or team?
**A:** Yes. In the All Events view, you can filter by various criteria. Contact your administrator if you need a custom view for specific event assignments or responsible parties.

### Q: How does the "Refresh from Parent" feature work?
**A:** When you click "Refresh from Parent," the system looks at the parent record (e.g., the Matter you linked to) and copies the current values of the fields your administrator has configured for auto-population. This overwrites whatever values are currently in those Event fields, so any manual edits you made will be replaced.

### Q: What if I accidentally clicked "Refresh from Parent" and lost data?
**A:** The changes are saved, but you can manually re-enter the data. If you need to recover data from before the refresh, your administrator may be able to help from the audit or version history (depending on your system configuration).

### Q: Is there a way to create recurring events?
**A:** In the current version, events are individual records. To create multiple similar events, you'll need to create each one separately or ask your administrator if they've set up any automation tools.

### Q: Can I print an event or export to PDF?
**A:** Most events can be printed directly from your browser's Print function. Some organizations have custom export tools—check with your administrator for available options.

### Q: Who can see my events?
**A:** This depends on your security role and your organization's configuration. By default, you can see your own events and events shared with you. Contact your administrator if you need to share an event with others or need different access levels.

### Q: What does "Regarding Record" mean?
**A:** The "Regarding" field links the Event to its parent record (the business entity it's associated with, like a Matter, Project, or Account). It's the record "this event is regarding" or "about."

---

## Troubleshooting

### Issue: Lookup search isn't returning results

**Solution:**
- Check the spelling of the record name you're searching for
- Make sure you've selected the correct record type (Matter, Account, Project, etc.)
- Verify the record exists and isn't deleted
- If still no results, contact your administrator—the record might not have permission for you to see it

### Issue: Expected fields aren't being auto-populated

**Solution:**
- Check that you've selected a parent record (look at the Regarding field)
- Verify the parent record has values in the fields you expect to be copied
- The field mapping might have been deactivated by your administrator—check with them if this is unexpected

### Issue: "Refresh from Parent" button is disabled

**Solution:**
- Make sure you've selected a parent record (Regarding field is populated)
- The button might be disabled if there are no configured mappings for that parent/event combination
- Contact your administrator if you believe this is an error

### Issue: Event saves but doesn't appear in views

**Solution:**
- Refresh the page (press F5 or Cmd+R)
- Check that the Event hasn't been saved as "Inactive" (check the Status field)
- Verify you have permission to see the event—ask your administrator if uncertain
- The view might have filters that are hiding your event—check filter settings

---

## Getting Help

If you need assistance using the Events system:

1. **Check this guide** first—your question might be answered in the FAQ or Troubleshooting section
2. **Contact your team lead or manager** if you have questions about your organization's event processes
3. **Reach out to IT support** for technical issues or access problems
4. **Ask your administrator** for specific configuration questions or feature requests

---

## Related Topics

- **Dataverse Model-Driven Apps Basics** – Learning to navigate and use Dataverse
- **Record Linking and Relationships** – Understanding how records connect
- **View and Filter Options** – Advanced techniques for organizing your data
- **Advanced Searching** – Using search features effectively

---

*Last Updated: February 1, 2026*

*For feature requests or documentation improvements, contact your system administrator or product team.*
