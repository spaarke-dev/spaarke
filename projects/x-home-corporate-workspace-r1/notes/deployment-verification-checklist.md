# Deployment Verification Checklist — Legal Operations Workspace (Task 041)

> **Task**: 041 — Custom Page Deployment to MDA
> **Phase**: 5 — Deployment & Wrap-up
> **Created**: 2026-02-18
> **Feeds into**: Task 043 — Post-Deployment Verification

---

## Environment Reference

| Item | Value |
|------|-------|
| Dataverse Dev | `https://spaarkedev1.crm.dynamics.com` |
| BFF API | `https://spe-api-dev-67e2xz.azurewebsites.net` |
| Power Apps Maker | `https://make.powerapps.com` |
| Solution Name | `SpaarkeLegalWorkspace` (unmanaged — ADR-022) |
| Custom Page Name | `sprk_LegalOperationsWorkspace` |
| PCF Control | `sprk_Spaarke.Controls.LegalWorkspace` v1.0.1 |

---

## Phase 1: Post-Import Verification

Run these checks immediately after `Deploy-LegalWorkspaceCustomPage.ps1` completes.

### 1.1 Solution Visibility (PAC CLI)

```powershell
pac solution list --environment https://spaarkedev1.crm.dynamics.com | Select-String SpaarkeLegalWorkspace
```

- [ ] Output shows `SpaarkeLegalWorkspace` with version `1.0.1`
- [ ] No error messages in pac solution list output

### 1.2 PCF Control Registration (Dataverse Web API)

```http
GET https://spaarkedev1.crm.dynamics.com/api/data/v9.2/customcontrols?$filter=name eq 'sprk_Spaarke.Controls.LegalWorkspace'&$select=name,version
Authorization: Bearer <your-token>
```

- [ ] Response contains one record: `sprk_Spaarke.Controls.LegalWorkspace`
- [ ] `version` field shows `1.0.1`
- [ ] Record count is exactly 1 (no duplicates)

### 1.3 Custom Page Registration (Dataverse Web API)

```http
GET https://spaarkedev1.crm.dynamics.com/api/data/v9.2/canvasapps?$filter=name eq 'sprk_LegalOperationsWorkspace'&$select=name,displayname,canvasapptype
Authorization: Bearer <your-token>
```

- [ ] Response contains `sprk_LegalOperationsWorkspace`
- [ ] `displayname` is `Legal Operations Workspace`
- [ ] `canvasapptype` is `3` (Custom Page type)

### 1.4 Customizations Published

- [ ] `pac solution publish-all` ran without errors
- [ ] No stale customizations warning in the pac output

---

## Phase 2: MDA Navigation Verification

### 2.1 Sitemap SubArea Present

After updating the sitemap (App Designer or XML editor) and publishing the app:

- [ ] MDA left navigation shows **"Legal Workspace"** item
- [ ] The nav item is in the correct navigation group (Legal Operations or Workspace group)
- [ ] No duplicate navigation items (check for old/stale entries)
- [ ] Nav item is visible without scrolling (Order attribute is correct)

### 2.2 Custom Page Loads

1. Click **Legal Workspace** in the MDA navigation
2. Main content area should begin loading the Custom Page

- [ ] Custom Page iframe loads without browser security error
- [ ] Power Apps loading spinner appears (initial load only)
- [ ] No browser "net::ERR_BLOCKED_BY_CLIENT" or similar frame errors
- [ ] Page title in MDA header shows "Legal Operations Workspace"

---

## Phase 3: All 7 Blocks Rendering Check

Hard-refresh before this check: `Ctrl+Shift+R`

Navigate to the Legal Workspace in the MDA and verify each block renders:

### Block 1: Get Started + Quick Summary (top row)

- [ ] "Get Started" header is visible
- [ ] At least 4 action cards are visible:
  - [ ] "Create New Matter" card (purple/primary)
  - [ ] "Matter Analysis" card (links to Analysis Builder)
  - [ ] "Contract Review" card (links to Analysis Builder)
  - [ ] Other AI action cards visible
- [ ] Quick Summary panel on the right side (or bottom in 1-column) shows:
  - [ ] Active Matters count
  - [ ] At Risk Matters count
  - [ ] Budget utilization percentage
  - [ ] Overdue events count

### Block 2: Portfolio Health Strip

- [ ] Portfolio Health bar renders below the Get Started row
- [ ] Shows grade pills (e.g., Budget: A, Guidelines: B, Outcomes: C)
- [ ] Color-coded: green for A, yellow for B, red for C/D
- [ ] "View details" or expand control is present

### Block 3: Updates Feed (Activity Feed)

- [ ] Updates/Activity Feed section is visible
- [ ] At least a loading state or empty state message renders
- [ ] If data available: feed items show with matter name, event type, timestamp
- [ ] Filter pills are visible (All, Alerts, Updates, etc.)
- [ ] "Flag as To Do" toggle visible on applicable items (Block 3D)
- [ ] "AI Summary" button visible on applicable items (Block 3E)

### Block 4: Smart To Do List

- [ ] Smart To Do section is visible
- [ ] Tab bar shows: To Do / In Progress / Completed (or similar)
- [ ] Items render with priority badge (High/Medium/Low)
- [ ] Effort badge visible (Low/Medium/High/Very High)
- [ ] Checkbox to complete a to-do is clickable
- [ ] "Add manually" input or button is present
- [ ] "AI Summary" button visible (Block 4D — opens scoring grid dialog)

### Block 5: My Portfolio (Matters tab)

- [ ] My Portfolio section is visible
- [ ] Matters tab is active by default
- [ ] Matter list renders with columns: Name, Practice Area, Status, Budget Grade
- [ ] Grade pills are color-coded
- [ ] Projects tab and Documents tab are also present and clickable
- [ ] Empty state shows if no matters (should not happen in dev environment with seed data)

### Block 6: Create Matter Dialog

- [ ] Launched by clicking "Create New Matter" card in Block 1
- [ ] Dialog opens as a modal overlay
- [ ] Step 1: File upload area (drag-and-drop or click-to-upload)
- [ ] Step indicator shows: 1 / 2 / 3
- [ ] Step 2 (after upload): AI pre-fill form with matter fields
- [ ] Step 3: Next steps and follow-on actions
- [ ] Dialog closes without page reload when cancelled (X button or Cancel)

### Block 7: Notification Panel

- [ ] Notification Bell icon visible in page header (top-right area)
- [ ] Clicking the bell opens the Notification Panel
- [ ] Panel shows notification items or empty state
- [ ] Panel closes when clicking outside or pressing Escape
- [ ] Notification badge count visible on bell icon (if notifications exist)

---

## Phase 4: BFF Endpoint Connectivity Test

### 4.1 Network Tab Verification

In browser DevTools (F12) → Network tab:

1. Clear the network log
2. Navigate to the Legal Workspace (or reload the page)
3. Wait for all blocks to load (~3-5 seconds)
4. Filter network requests by: `api/workspace`

Expected requests and responses:

| Endpoint | Method | Expected Status | Notes |
|----------|--------|-----------------|-------|
| `/api/workspace/portfolio` | GET | 200 OK | Portfolio Summary data |
| `/api/workspace/health` | GET | 200 OK | Health Metrics for Block 2 |
| `/api/workspace/briefing` | GET | 200 OK | Quick Summary narrative |
| `/api/workspace/calculate-scores` | POST | 200 OK | Batch scoring for To Do items |

- [ ] All 4 workspace endpoints return `200 OK`
- [ ] No requests to `api/workspace/*` return `401 Unauthorized`
- [ ] No requests return `403 Forbidden`
- [ ] No requests return `500 Internal Server Error`
- [ ] Response times are under 3 seconds for GET endpoints
- [ ] Response times are under 5 seconds for POST scoring endpoint

### 4.2 Portfolio Response Validation

Click the portfolio endpoint response in DevTools → Preview:

- [ ] Response has shape:
  ```json
  {
    "activeMatters": <number>,
    "mattersAtRisk": <number>,
    "totalSpend": <number>,
    "budgetTotal": <number>,
    "utilizationPercent": <number>,
    "overdueEvents": <number>,
    "cachedAt": "<ISO timestamp>"
  }
  ```
- [ ] `activeMatters` > 0 (dev environment should have seed data)
- [ ] `cachedAt` is present (confirms Redis caching is active — ADR-009)

### 4.3 Health Response Validation

- [ ] Response has health grade indicators for each matter
- [ ] At least one health indicator (budget, guidelines, or outcomes) is present

### 4.4 MSAL Token Verification

In DevTools → Network → click any `api/workspace/*` request → Headers:

- [ ] `Authorization` header is present in the request
- [ ] Value starts with `Bearer ` followed by a JWT token string
- [ ] Token is not empty or malformed

---

## Phase 5: Theme Toggle Test

### 5.1 Light Mode (Default)

- [ ] Workspace renders with light theme (white/light gray background)
- [ ] Text is dark (high contrast on light background)
- [ ] Cards use Fluent v9 neutral palette (no hardcoded hex colors)
- [ ] Grade pill colors are visible and distinct

### 5.2 Dark Mode

1. In the MDA, change the theme to dark mode:
   - Settings gear → **Personalization Settings** → **Theme** → Dark
   - Or toggle dark mode in the MDA app settings

- [ ] Workspace background changes to dark theme
- [ ] All text remains readable (high contrast on dark background)
- [ ] Cards maintain visual structure in dark mode
- [ ] Grade pill colors remain visible in dark mode
- [ ] No hardcoded white or black backgrounds appear (would look jarring)

### 5.3 High Contrast Mode (Accessibility)

1. Enable Windows High Contrast mode (Alt+Left Shift+Print Screen)
   or use browser DevTools → Rendering → Emulate CSS media: forced-colors: active

- [ ] All UI elements remain visible
- [ ] Text maintains readability
- [ ] Interactive elements (buttons, checkboxes, toggles) are distinguishable
- [ ] No disappearing icons or invisible borders

---

## Phase 6: Console Error Check

In browser DevTools (F12) → Console tab:

After the workspace fully loads, check for errors:

- [ ] No `TypeError` or `ReferenceError` messages
- [ ] No `Cannot read properties of undefined` errors
- [ ] No `Failed to fetch` or network errors (all BFF requests succeeded above)
- [ ] No React `Warning: ...` messages about key props or unmounted state updates
- [ ] No Fluent UI deprecation warnings (should all use v9)
- [ ] No CORS errors (all `api/workspace` calls go through without CORS block)
- [ ] No CSP (Content Security Policy) violations

### Acceptable Warnings

These warnings can be ignored:
- Power Apps platform informational messages
- `ResizeObserver loop` warnings (harmless, known browser behavior)
- `[Warning] Unexpected ...` from Power Apps iframe host code (not your code)

### Errors That Must Be Fixed Before Task 043

If any of these errors appear, do not mark Task 041 complete:
- `401 Unauthorized` from BFF endpoints
- `CORS error` on any `api/workspace` request
- `React Error Boundary` triggered (shows fallback UI instead of workspace)
- `Cannot read properties of null (reading '...')` — null reference in PCF init
- Complete blank screen with no loading state

---

## Phase 7: Responsive Layout Verification

### 7.1 1024px — Minimum Width (1-Column Layout)

1. In DevTools → Toggle Device Toolbar (Ctrl+Shift+M)
2. Set width to 1024px
3. Or resize the browser window until the MDA content area is ~1024px wide

- [ ] Workspace switches to 1-column layout (blocks stack vertically)
- [ ] All 7 blocks are accessible by scrolling
- [ ] No horizontal scrollbar appears
- [ ] Content is not clipped or overflowing

### 7.2 1440px — Standard Width (2-Column Layout)

- [ ] Workspace shows 2-column layout (action cards + feed side-by-side)
- [ ] Left column: Block 1 (Get Started) + Block 3 (Feed) stacked
- [ ] Right column: Block 2 (Health) + Block 4 (To Do) + Block 5 (Portfolio)
- [ ] No excessive white space on either side

### 7.3 1920px — Wide Screen (2-Column, Max Width)

- [ ] 2-column layout maintained
- [ ] Content does not stretch beyond ~1800px (max-width CSS constraint)
- [ ] Centered layout with equal side margins on very wide screens

---

## Phase 8: Auth Flow Verification Summary

Final auth flow check (confirms ADR-008 compliance):

| Check | Expected Result | Status |
|-------|----------------|--------|
| MSAL token present in request headers | `Authorization: Bearer <jwt>` | [ ] |
| BFF portfolio endpoint returns data | `200 OK` with `PortfolioSummaryResponse` | [ ] |
| BFF health endpoint returns data | `200 OK` with health metrics | [ ] |
| BFF briefing endpoint returns data | `200 OK` with briefing narrative | [ ] |
| Unauthenticated request blocked | `401 Unauthorized` (test by clearing cookies) | [ ] |
| CORS headers present in BFF response | `Access-Control-Allow-Origin: https://spaarkedev1.crm.dynamics.com` | [ ] |

---

## Rollback Procedure

If deployment causes critical issues (workspace blank, auth broken, MDA navigation broken):

### Rollback PCF to Previous Version

```powershell
# Find previous ZIP in Solution/bin/
ls "src\client\pcf\LegalWorkspace\Solution\bin\" | Sort-Object LastWriteTime

# Import previous ZIP
pac solution import `
    --path "src\client\pcf\LegalWorkspace\Solution\bin\SpaarkeLegalWorkspace_v1.0.0.zip" `
    --force-overwrite `
    --publish-changes `
    --environment https://spaarkedev1.crm.dynamics.com
```

### Remove Custom Page from Sitemap

If the sitemap update caused issues:
1. Open App Designer → Navigation → remove the `sprk_legal_workspace` SubArea
2. Save and Publish the app
3. MDA nav item is removed, other functionality unaffected

### Delete and Recreate Custom Page (Last Resort)

1. In make.powerapps.com → Solutions → SpaarkeLegalWorkspace
2. Find `sprk_LegalOperationsWorkspace` under Apps → delete it
3. Follow Step 2 in `custom-page-registration.md` to recreate
4. Re-add to sitemap and republish

---

## Sign-Off

After all checks pass:

- [ ] Task 041 acceptance criteria satisfied (see POML file)
- [ ] This checklist reviewed by developer
- [ ] TASK-INDEX.md updated: Task 041 🔲 → ✅
- [ ] Ready to proceed to Task 043 (Post-Deployment Verification)

---

*Created by Task 041 — Custom Page Deployment to MDA*
*See also: mda-sitemap-config.md, custom-page-registration.md, solution-packaging-checklist.md*
*BFF verification reference: bff-deployment-checklist.md*
