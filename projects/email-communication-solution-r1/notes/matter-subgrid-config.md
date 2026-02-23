# Matter Form: Communications Subgrid Configuration

**Last Updated**: February 20, 2026
**Environment**: Dataverse / Power Apps
**Target Form**: Matter (sprk_matter) - Main Form

---

## Overview

This guide walks you through manually configuring a subgrid component on the Matter form to display all related communication records (emails, messages, etc.) for a given matter.

**What you'll configure:**
- A subgrid control on the Matter form that displays sprk_communication records
- A filtered view showing only communications related to the current matter
- Columns: Subject, Status, To, Sent At, From
- Sort: Newest communications first (by Sent At, descending)

---

## Prerequisites

- Access to the Power Apps Maker Portal
- Permissions to edit the Matter table and forms
- The `sprk_communication` table already created with:
  - `sprk_regardingmatter` lookup field (targets sprk_matter)
  - `sprk_subject`, `statuscode`, `sprk_to`, `sprk_sentat`, `sprk_from` columns

---

## Part 1: Create the "Matter Communications" View

### Step 1: Navigate to the Communication Table

1. Open [Power Apps Maker Portal](https://make.powerapps.com/)
2. Select your **environment** (Dev/Prod)
3. In the left sidebar, go to **Tables**
4. Search for and open the **Communication** (sprk_communication) table

### Step 2: Create a New View

1. In the Communication table details, scroll down to **Views**
2. Click **+ New view**
3. Select **Create new table view** (or **Edit in grid** if preferred for simplicity)
4. Name the view: **Matter Communications**
5. Click **Create**

### Step 3: Configure Columns

Once the view editor opens:

1. **Add/reorder columns** to match this list (in order):
   - Subject (sprk_subject)
   - Status (statuscode)
   - To (sprk_to)
   - Sent At (sprk_sentat)
   - From (sprk_from)

2. To adjust columns:
   - Click the **Column** icon in the toolbar
   - Remove unwanted columns (e.g., Created On, Created By)
   - Drag columns to reorder

### Step 4: Set Sort Order

1. Click the **Sort** icon in the toolbar
2. Select **Sort by**: `Sent At` (sprk_sentat)
3. Select **Order**: **Descending** (newest first)
4. Click **OK**

### Step 5: Add Filter (Optional at View Level)

The filter will be enforced by the subgrid's relationship binding, but you can optionally set a static filter here:

1. Click the **Filter** icon
2. Set: `Regarding Matter (sprk_regardingmatter)` **is not empty**
3. Click **OK**

This ensures the view only shows communications that have a matter assigned.

### Step 6: Save the View

1. Click **Save** in the top-right corner
2. View name should now be **Matter Communications**

---

## Part 2: Configure the Subgrid on the Matter Form

### Step 1: Open the Matter Form

1. In the Power Apps Maker Portal, navigate to **Tables**
2. Open the **Matter** (sprk_matter) table
3. Under **Forms**, find and open the **Main** form (or create a new form if needed)

### Step 2: Add a Subgrid Component

1. In the form designer, click the section where you want the subgrid to appear
   - Recommendation: Create or use a **Communications** section/tab
2. From the **Components** panel on the left, search for **Subgrid**
3. Drag the **Subgrid** component into the section
4. A dialog will open: **Select records**

### Step 3: Configure the Subgrid Properties

In the **Select records** dialog:

#### Entity Selection
- **Entity**: Select **Communication** (sprk_communication)

#### Relationship Configuration
- **Relationship**: Select **Regarding Matter** (sprk_regardingmatter)
  - This is the N:1 relationship from Communication to Matter
  - The subgrid will automatically filter to show only communications related to the current matter

#### View Selection
- **Default view**: Select **Matter Communications** (the view you just created)

#### Layout & Behavior
- **Records per page**: 10 (or your preference)
- **Show related records**: ✓ (checked)
- **Enable search**: ✓ (checked, so users can find communications)

### Step 4: Customize Subgrid Display (Optional)

After placing the subgrid, you can further customize in the properties panel:

1. **Display Name**: "Communications" (or "Related Communications")
2. **Label**: Show label (helpful for users)
3. **Visible by default**: ✓ (checked)

---

## Part 3: Advanced Configuration (FetchXML)

If you need to apply a custom FetchXML query beyond what the view provides, follow these steps:

### Step 1: Enable FetchXML Editor

1. In the subgrid properties panel, find **Advanced** section
2. Look for **Use FetchXML query** option (toggle it on)
3. Click **Edit FetchXML**

### Step 2: Paste the FetchXML

Use this FetchXML query:

```xml
<fetch>
  <entity name="sprk_communication">
    <attribute name="sprk_subject" />
    <attribute name="statuscode" />
    <attribute name="sprk_to" />
    <attribute name="sprk_sentat" />
    <attribute name="sprk_from" />
    <order attribute="sprk_sentat" descending="true" />
    <filter>
      <condition attribute="sprk_regardingmatter" operator="eq" value="{matterId}" />
    </filter>
  </entity>
</fetch>
```

**Important Notes:**
- `{matterId}` is automatically replaced by Dataverse with the current matter's ID when the form loads
- The sort order ensures newest communications appear at the top
- The filter restricts results to only communications related to this specific matter

### Step 3: Save

1. Click **Save** in the FetchXML editor
2. Close the editor

---

## Part 4: Save and Publish

### Step 1: Save the Form

1. Click **Save** in the form designer toolbar
2. Wait for the save to complete

### Step 2: Publish the Form

1. Click **Publish** in the form designer toolbar
2. A confirmation message will appear
3. Wait for publishing to complete (typically 30-60 seconds)

### Step 3: Test the Configuration

1. In the Power Apps maker portal, navigate to a Matter record
2. Scroll to the **Communications** section
3. Verify:
   - ✓ All communications for this matter are displayed
   - ✓ Columns match: Subject, Status, To, Sent At, From
   - ✓ Sort order is newest-first (most recent Sent At at top)
   - ✓ Search works (if enabled)
   - ✓ No communications from other matters are shown

---

## Troubleshooting

| Issue | Solution |
|-------|----------|
| Subgrid appears empty | Verify that communications exist for this matter in the Communication table. Check the `sprk_regardingmatter` lookup field is populated. |
| "Regarding Matter" relationship not visible | Ensure the `sprk_regardingmatter` lookup field is created on sprk_communication and points to sprk_matter. Refresh the form designer. |
| Wrong columns displayed | Edit the **Matter Communications** view and verify columns match the list in Part 1, Step 3. |
| Sort order incorrect | Check the view's sort settings (Part 1, Step 4). Ensure "Descending" is selected for Sent At. |
| FetchXML not applying | Verify syntax and ensure `{matterId}` is lowercase. Test the FetchXML in the Advanced Find tool first. |
| Performance slow with large datasets | Consider adding a date range filter (e.g., last 6 months) or paginating with 5-10 records per page. |

---

## Reference: Entity & Field Details

### sprk_matter (Matter Table)
- **Display Name**: Matter
- **Schema Name**: sprk_matter
- **Primary Key**: sprk_matterid

### sprk_communication (Communication Table)
- **Display Name**: Communication
- **Schema Name**: sprk_communication
- **Lookup to Matter**: sprk_regardingmatter

### Key Fields in sprk_communication
| Field | Schema Name | Type | Usage |
|-------|-------------|------|-------|
| Subject | sprk_subject | Text | Display in subgrid |
| Status | statuscode | Choice | Display in subgrid (Draft=1, Send=659490002, Failed=659490003, Archived=659490004) |
| To | sprk_to | Text | Display in subgrid |
| Sent At | sprk_sentat | DateTime | Sort by (descending) |
| From | sprk_from | Text | Display in subgrid |
| Regarding Matter | sprk_regardingmatter | Lookup | Filter relationship |

---

## Related Documentation

- [Dataverse Subgrid Control Documentation](https://learn.microsoft.com/en-us/power-apps/maker/model-driven-apps/sub-grid-component)
- [Using FetchXML in Subgrids](https://learn.microsoft.com/en-us/power-apps/maker/model-driven-apps/fetchxml-syntax)
- [Relationship-based Filtering](https://learn.microsoft.com/en-us/power-apps/maker/model-driven-apps/create-and-edit-views)

---

**Status**: Complete
**Last Tested**: February 20, 2026
