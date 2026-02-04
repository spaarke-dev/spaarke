# Events Sitemap - Custom Page Configuration

> **Purpose**: Replace OOB Events entity view with Events Custom Page in sitemap navigation
> **Created**: 2026-02-04
> **Project**: Events Workspace Apps UX R1

---

## Overview

This solution configures the Dataverse Model-Driven App sitemap to point to the Events Custom Page instead of the default Events entity grid view.

**Key Requirements:**
- Navigation label: "Events" (NOT "My Events")
- Replace existing entry (not add new nav item)
- Preserve navigation group/area position
- Preserve existing icon

---

## Original Configuration (Before)

The OOB Events entity sitemap entry typically looks like:

```xml
<SubArea Id="sprk_event"
         Entity="sprk_event"
         Title="Events"
         Icon="/_imgs/navbar/[10000]/sprk_event.svg" />
```

This opens the standard entity grid view.

---

## New Configuration (After)

Replace with Custom Page URL:

```xml
<SubArea Id="sprk_eventspage"
         Url="/main.aspx?pagetype=custom&amp;name=sprk_eventspage"
         Title="Events"
         Icon="/_imgs/navbar/[10000]/sprk_event.svg" />
```

**URL Pattern**: `/main.aspx?pagetype=custom&name={custom_page_logical_name}`

**Note**: The Custom Page logical name must match the actual deployed Custom Page name in Dataverse. Common formats:
- `sprk_eventspage` (if created with sprk_ prefix)
- `cr123_eventspage` (if different publisher prefix)
- Check actual name in Power Apps Solution after Custom Page is created

---

## Deployment Steps

### Prerequisites

1. Events Custom Page (`src/solutions/EventsPage/`) must be built and deployed
2. Custom Page must be registered in a Dataverse solution
3. PAC CLI authenticated: `pac auth list`

### Step 1: Create/Deploy the Custom Page

```powershell
# Build the Events Custom Page
cd src/solutions/EventsPage
npm run build

# The built assets are in dist/ folder
# Deploy via make.powerapps.com:
# 1. Navigate to solution
# 2. Add existing > Page > Custom page
# 3. Upload the built HTML/JS assets
```

### Step 2: Update Sitemap via App Designer (Recommended)

The easiest method is to use the Power Apps App Designer:

1. Open https://make.powerapps.com
2. Navigate to your environment
3. Open the Model-Driven App (e.g., "Spaarke")
4. Click "Edit" to open App Designer
5. In the left navigation panel, find the "Events" entry
6. Click "..." menu > "Edit"
7. Change the **Page type** from "Entity" to "Custom page"
8. Select the Events Custom Page from the dropdown
9. Verify Title is "Events" (not "My Events")
10. Save and Publish the app

### Step 3: Verify Sitemap Update (Alternative - Export/Modify/Import)

If you need to export and modify the sitemap XML directly:

```powershell
# Export the solution containing the App Module
pac solution export --name YourAppSolution --path ./exported/ --include-sitemap

# The sitemap XML is typically in:
# exported/AppModuleSiteMaps/{AppModuleId}/SiteMap.xml

# After modifying the sitemap, repack and import:
pac solution pack --zipfile ./YourAppSolution_updated.zip --folder ./exported/
pac solution import --path ./YourAppSolution_updated.zip --publish-changes
```

### Step 4: Test Navigation

1. Refresh the Model-Driven App
2. Click "Events" in the left navigation
3. Verify: Events Custom Page loads (not entity grid)
4. Verify: Title shows "Events"
5. Verify: Calendar + Grid components render

---

## Sitemap XML Reference

### Entity-based SubArea (OOB - to be replaced)

```xml
<SubArea Id="nav_sprk_event"
         ResourceId="SitemapDesigner.NewSubArea"
         VectorIcon=""
         Client="All,Outlook,OutlookLaptopClient,OutlookWorkstationClient,Web"
         AvailableOffline="true"
         PassParams="false"
         Sku="All,OnPremise,Live,SPLA"
         Entity="sprk_event"
         GetStartedPanePath=""
         GetStartedPanePathAdmin=""
         GetStartedPanePathOutlook=""
         GetStartedPanePathAdminOutlook="">
  <Titles>
    <Title LCID="1033" Title="Events" />
  </Titles>
</SubArea>
```

### Custom Page SubArea (New)

```xml
<SubArea Id="nav_sprk_eventspage"
         ResourceId="SitemapDesigner.NewSubArea"
         VectorIcon=""
         Client="All,Outlook,OutlookLaptopClient,OutlookWorkstationClient,Web"
         AvailableOffline="false"
         PassParams="false"
         Sku="All,OnPremise,Live,SPLA"
         Url="/main.aspx?pagetype=custom&amp;name=sprk_eventspage"
         GetStartedPanePath=""
         GetStartedPanePathAdmin=""
         GetStartedPanePathOutlook=""
         GetStartedPanePathAdminOutlook="">
  <Titles>
    <Title LCID="1033" Title="Events" />
  </Titles>
</SubArea>
```

**Key Changes:**
- Removed `Entity="sprk_event"` attribute
- Added `Url="/main.aspx?pagetype=custom&amp;name=sprk_eventspage"` attribute
- Set `AvailableOffline="false"` (Custom Pages don't support offline)
- Changed `Id` to avoid collision with original entry

---

## Troubleshooting

| Issue | Cause | Solution |
|-------|-------|----------|
| Custom Page not in dropdown | Page not deployed to solution | Deploy Custom Page first |
| "Page not found" error | Wrong Custom Page name in URL | Verify exact logical name |
| Entity grid still shows | Sitemap not published | Publish App customizations |
| Offline mode error | Custom Pages can't work offline | Set AvailableOffline="false" |
| Icon missing | VectorIcon/Icon not set | Copy icon path from original entry |

---

## Rollback

To revert to OOB Events entity view:

1. Open App Designer
2. Edit Events navigation entry
3. Change Page type back to "Entity"
4. Select "sprk_event" entity
5. Save and Publish

Or restore original sitemap XML from backup.

---

## Related Files

- `src/solutions/EventsPage/` - Events Custom Page source code
- `infrastructure/dataverse/ribbon/ThemeMenuRibbons/` - Theme menu for Events entity
- `projects/events-workspace-apps-UX-r1/spec.md` - Project specification

---

*Created for Events Workspace Apps UX R1 project - Task 067*
