# Application Insights - How to Find the HTTP 500 Error

**Date**: 2025-10-16
**App**: spe-api-dev-67e2xz
**Application Insights**: AppId `6a76b012-46d9-412f-b4ab-4905658a9559`
**Goal**: Find the actual error causing HTTP 500 on file upload

---

## Method 1: Azure Portal - Failures Blade (EASIEST)

### Step 1: Navigate to Application Insights

1. Open Azure Portal: https://portal.azure.com
2. Search for "Application Insights" in the top search bar
3. Click on the Application Insights resource (should match instrumentation key `09a9beed-0dcd-4aad-84bb-3696372ed5d1`)
   - **Name**: Likely named something like `spe-api-dev-insights` or similar
   - **Resource Group**: `spe-infrastructure-westus2` or `SharePointEmbedded`

### Step 2: Open Failures Blade

1. In the left navigation panel, under **Investigate** section
2. Click **Failures**
3. Set time range to **Last 24 hours** (top right)

### Step 3: Look for HTTP 500 Errors

**In the main chart area**:
- Look for spikes in the "Failed requests" chart
- Look for red bars indicating failures
- Note the timestamp of when you last tried to upload a file

**In the "Top 3" sections below**:
- **Response codes**: Look for `500` in the list
- **Failed operations**: Look for PUT requests to `/api/obo/containers/...`
- **Top 3 exception types**: Look for:
  - `Microsoft.Graph.ServiceException`
  - `System.Net.Http.HttpRequestException`
  - Any exception mentioning "Access" or "Forbidden"

### Step 4: Click on a Failure to See Details

1. Click on the `500` response code in the list
2. In the panel that opens on the right, you'll see:
   - **Operation name**: Should be `PUT /api/obo/containers/{containerId}/files/{path}`
   - **Exception type**: What kind of exception occurred
   - **Exception message**: The actual error message
   - **Stack trace**: Where in the code it failed

**What to Look For**:
```
Exception type: Microsoft.Graph.ServiceException
Message: Code: AccessDenied
         Message: Insufficient privileges to complete the operation.
```

---

## Method 2: Azure Portal - Logs (Kusto Query)

### Step 1: Navigate to Logs

1. In Application Insights, click **Logs** (left panel under **Monitoring**)
2. Close the welcome dialog if it appears
3. You'll see a query editor

### Step 2: Query for Exceptions

**Copy and paste this query**:

```kusto
exceptions
| where timestamp > ago(24h)
| where cloud_RoleName == "spe-api-dev-67e2xz"
| where outerMessage contains "Graph" or outerMessage contains "Access" or outerMessage contains "Forbidden" or outerMessage contains "privileges"
| project
    timestamp,
    operation_Name,
    type,
    outerMessage,
    innermostMessage,
    problemId,
    details
| order by timestamp desc
| take 50
```

**Click "Run"**

### Step 3: Look at Results

**Columns to examine**:
- **timestamp**: When the error occurred
- **operation_Name**: Should be `PUT /api/obo/containers/{containerId}/files/{path}`
- **type**: Exception type (e.g., `Microsoft.Graph.ServiceException`)
- **outerMessage**: The main error message
- **innermostMessage**: The root cause error
- **details**: JSON with full stack trace

**What You're Looking For**:
- Any row mentioning "FileStorageContainer"
- Any row mentioning "AccessDenied" or "Insufficient privileges"
- Any row mentioning "scope" or "permission"

### Step 4: Expand Details

1. Click on any row in the results
2. Look at the **details** column (JSON)
3. Expand to see full exception details

---

## Method 3: Azure Portal - Transaction Search

### Step 1: Navigate to Transaction Search

1. In Application Insights, click **Transaction search** (left panel under **Investigate**)
2. Set time range to **Last 24 hours**

### Step 2: Filter for Failed Requests

**Add filters**:
1. Click **+ Add filter**
2. Select **Result code** → Choose **500**
3. Click **+ Add filter**
4. Select **Event types** → Choose **Exception**

### Step 3: Look for Upload Requests

**In the results list**:
- Look for `PUT` requests
- Look for URLs containing `/api/obo/containers/`
- Look for recent timestamps (when you last tried upload)

### Step 4: Click on a Transaction

1. Click on any failed PUT request
2. In the detail panel:
   - **Timeline**: Shows request flow
   - **Related items**: Shows associated exceptions
   - **Custom properties**: Shows request details
   - **Exception**: Click to see full exception details

**Look for**:
- **Exception type**: `Microsoft.Graph.ServiceException`
- **Message**: Should contain "Access" or "Forbidden" or "privileges"
- **Status code**: 403 (the Graph API response) that becomes 500 (the BFF API response)

---

## Method 4: Check for Dependencies (Graph API Calls)

### Step 1: Navigate to Performance Blade

1. Click **Performance** (left panel under **Investigate**)
2. Select **Dependencies** tab (top of main area)

### Step 2: Look for Graph API Calls

**In the list of dependencies**:
- Look for calls to `graph.microsoft.com`
- Look for operations to `/drives/{containerId}/...`
- Filter by **Failed** (toggle at top)

### Step 3: Check Dependency Failures

1. Click on any failed Graph API dependency
2. Look at:
   - **Result code**: Should be 403 (Forbidden)
   - **Duration**: How long before it failed
   - **Target**: `graph.microsoft.com`

**This confirms**:
- The request reached Graph API ✅
- Graph API rejected it with 403 ✅
- The 403 became 500 when returned to client ❌

---

## What You Should Find

### Expected Error Pattern #1: Missing Scope

```
Exception: Microsoft.Graph.ServiceException
Message: Code: AccessDenied
         Message: Insufficient privileges to complete the operation.
StatusCode: Forbidden (403)

Inner Exception:
  The operation requires the 'FileStorageContainer.Selected' permission scope.
```

**This confirms**: OBO token is missing the required scope.

---

### Expected Error Pattern #2: Unregistered Application

```
Exception: Microsoft.Graph.ServiceException
Message: Code: AccessDenied
         Message: The application is not registered for this container type.
StatusCode: Forbidden (403)

Inner Exception:
  Application '1e40baad-e065-4aea-a8d4-4b7ab273458c' does not have permission to access container type '8a6ce34c-6055-4681-8f87-2f4f9f921c06'.
```

**This would mean**: Container Type registration is not complete (but user said it is).

---

### Expected Error Pattern #3: User Lacks Permission

```
Exception: Microsoft.Graph.ServiceException
Message: Code: AccessDenied
         Message: User does not have access to this resource.
StatusCode: Forbidden (403)

Inner Exception:
  User 'ralph.schroeder@spaarke.com' does not have permission to access this container.
```

**This would mean**: User permissions in Dataverse are not configured correctly.

---

## If You Don't Find Anything

### Possible Reasons

1. **Application Insights not capturing exceptions**
   - Check if exceptions blade shows ANY exceptions at all
   - If empty, Application Insights might not be configured correctly

2. **Error happened too long ago**
   - Extend time range to **Last 7 days**
   - Look for older upload attempts

3. **Error not making it to Application Insights**
   - The crash happens so early that Application Insights can't capture it
   - This would be unusual but possible

4. **No upload attempts were made recently**
   - Verify you actually triggered an upload
   - Check the timestamp of your last attempt

---

## Quick Checklist for Azure Portal

```
□ Open Azure Portal
□ Navigate to Application Insights resource
□ Check Failures blade:
  □ Look for HTTP 500 errors
  □ Look for Microsoft.Graph.ServiceException
  □ Read exception message
□ Run Kusto query in Logs:
  □ Look for "Access" or "Forbidden" in messages
  □ Check innermostMessage for root cause
□ Check Transaction search:
  □ Filter by result code 500
  □ Find PUT /api/obo/containers/ requests
  □ Read exception details
□ Check Dependencies:
  □ Look for failed graph.microsoft.com calls
  □ Check if 403 responses exist
```

---

## Screenshot Locations (What to Look For)

### Failures Blade
- **Look for**: Red bars in the chart
- **Look for**: "500" in the response codes list
- **Look for**: Exception type "Microsoft.Graph.ServiceException"

### Logs Query Results
- **Look for**: Rows with "AccessDenied" or "Insufficient privileges"
- **Look for**: Any mention of "FileStorageContainer" or "scope"
- **Look for**: Stack trace mentioning "GraphClientFactory" or "UploadSessionManager"

### Transaction Details
- **Look for**: "PUT /api/obo/containers/{containerId}/files/{path}"
- **Look for**: Exception tab showing full error details
- **Look for**: Related items showing Graph API dependency failure

---

## What to Tell Me

**If you find an exception**, copy and paste:
1. **Exception type**: (e.g., Microsoft.Graph.ServiceException)
2. **Exception message**: The full error message
3. **Inner exception**: If there is one
4. **Timestamp**: When it occurred
5. **Operation name**: The endpoint that failed

**If you don't find anything**, tell me:
1. Is the Failures blade empty?
2. Does the Kusto query return any rows?
3. What time range did you check?
4. When was the last time you tried to upload a file?

---

## Alternative: CLI Query (If Portal is Difficult)

Run this from your terminal:

```bash
az monitor app-insights query \
  --app 6a76b012-46d9-412f-b4ab-4905658a9559 \
  --analytics-query "exceptions | where timestamp > ago(24h) | where cloud_RoleName == 'spe-api-dev-67e2xz' | project timestamp, type, outerMessage, innermostMessage | order by timestamp desc | take 20" \
  --output table
```

**Look for**: Any row mentioning "Graph", "Access", "Forbidden", or "privileges"

---

**Document Created**: 2025-10-16 06:30 AM
**Goal**: Find the actual exception message from Application Insights
**Expected Finding**: Microsoft.Graph.ServiceException with message about missing FileStorageContainer.Selected scope
