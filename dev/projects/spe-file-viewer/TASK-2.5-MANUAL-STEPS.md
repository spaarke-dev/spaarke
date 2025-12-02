# Task 2.5: Link Custom API to Plugin (Manual Steps)

## Issue
Custom APIs don't use traditional plugin steps. Instead, they link directly to the plugin type via the `plugintypeid` field on the Custom API record itself.

The Web API is having trouble with the update, so we'll use XrmToolBox Custom API Manager.

## Manual Steps

### Using XrmToolBox Custom API Manager

1. **Open XrmToolBox Custom API Manager**
   - You already have this open from creating the output parameters

2. **Find the Custom API**
   - Look for `sprk_GetFilePreviewUrl` in the list
   - Double-click to edit it

3. **Set the Plugin Type**
   - Find the field **"Plugin Type"** or **"plugintypeid"**
   - Click to select a plugin
   - Choose: `Spaarke.Dataverse.CustomApiProxy.GetFilePreviewUrlPlugin`
     - Assembly: `Spaarke.Dataverse.CustomApiProxy`

4. **Save the Custom API**

5. **Verify**
   - The Custom API should now show the linked plugin type
   - You should see: Plugin Type = `Spaarke.Dataverse.CustomApiProxy.GetFilePreviewUrlPlugin`

## What This Does
This tells Dataverse: "When someone calls the `sprk_GetFilePreviewUrl` Custom API function on a `sprk_document` record, execute the `GetFilePreviewUrlPlugin` plugin."

## After Completing This
- Proceed to **Task 2.6: Publish Customizations**
- Then test the Custom API

---

## Plugin Details (for reference)
- **Plugin Assembly**: Spaarke.Dataverse.CustomApiProxy
- **Plugin Type**: Spaarke.Dataverse.CustomApiProxy.GetFilePreviewUrlPlugin
- **Plugin Type ID**: d128fc10-99c6-4c07-b80b-e3d1447744d1
- **Custom API ID**: da4f5012-00c7-f011-8543-000d3a1a9353
