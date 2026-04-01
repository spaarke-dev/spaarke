# Reporting Module — E2E Smoke Test Plan

> **Module**: Reporting (Power BI Embedded R1)
> **Test Type**: End-to-end smoke tests (manual + Playwright-automatable)
> **Framework**: Playwright (extends existing PCF E2E framework in `tests/e2e/`)
> **Last Updated**: 2026-03-31

---

## Overview

This document defines end-to-end smoke tests for the Spaarke Reporting module. Tests cover the full stack from the `sprk_reporting` Code Page UI through the BFF `ReportingEmbedService` to the Power BI service and Dataverse.

All test cases map to functional requirements (FR-XX) from `projects/spaarke-powerbi-embedded-r1/spec.md` and validate acceptance criteria for the R1 release.

---

## Prerequisites

Before running these tests:

1. All Phase 1–4 tasks (030–043) must be complete and deployed to the dev environment.
2. F-SKU capacity (minimum F2) must be provisioned and active.
3. Entra ID app registration must have Power BI API permissions (`Dataset.ReadWrite.All`, `Content.Create`, `Workspace.ReadWrite.All`).
4. Service principal must be added to the customer's Power BI workspace as Admin/Member.
5. `sprk_ReportingModuleEnabled` environment variable must be set to **Yes** in the dev Dataverse org.
6. At least one `sprk_report` catalog record must exist.
7. Five standard product reports must be deployed via `Deploy-ReportingReports.ps1`.
8. Three test user accounts configured:
   - `viewer@testcustomer.com` — has `sprk_ReportingAccess` (Viewer privileges only)
   - `author@testcustomer.com` — has `sprk_ReportingAccess` (Author privileges)
   - `admin@testcustomer.com` — has `sprk_ReportingAccess` (Admin privileges)
   - `noaccess@testcustomer.com` — does NOT have `sprk_ReportingAccess` role
9. Two test users in different Business Units (BU-1 and BU-2) with data seeded for each BU.
10. Redis cache accessible and connected to the BFF API.

---

## Environment Setup

```env
# tests/e2e/config/.env (extend existing config)
POWER_APPS_URL=https://spaarkedev1.crm.dynamics.com
DATAVERSE_API_URL=https://spaarkedev1.api.crm.dynamics.com/api/data/v9.2
BFF_API_URL=https://spe-api-dev-67e2xz.azurewebsites.net
TENANT_ID=<entra-tenant-id>
CLIENT_ID=<app-registration-client-id>
CLIENT_SECRET=<app-registration-client-secret>

# Reporting-specific
REPORTING_VIEWER_USERNAME=viewer@testcustomer.com
REPORTING_VIEWER_PASSWORD=<viewer-password>
REPORTING_AUTHOR_USERNAME=author@testcustomer.com
REPORTING_AUTHOR_PASSWORD=<author-password>
REPORTING_ADMIN_USERNAME=admin@testcustomer.com
REPORTING_ADMIN_PASSWORD=<admin-password>
REPORTING_NOACCESS_USERNAME=noaccess@testcustomer.com
REPORTING_NOACCESS_PASSWORD=<noaccess-password>
REPORTING_BU1_USERNAME=bu1user@testcustomer.com
REPORTING_BU1_PASSWORD=<bu1-password>
REPORTING_BU2_USERNAME=bu2user@testcustomer.com
REPORTING_BU2_PASSWORD=<bu2-password>
```

---

## Test Cases

---

### TC-001 — Report Rendering

**ID**: TC-001
**FR Reference**: FR-01, NFR-02
**ADR References**: ADR-006 (Code Page), ADR-021 (Fluent v9)
**Priority**: Critical
**Automatable**: Yes (Playwright)

**Description**
Verifies that opening the Reporting page causes the embedded Power BI report to render with data within the 3-second performance budget.

**Preconditions**
- Authenticated as `viewer@testcustomer.com` (Viewer role)
- `sprk_ReportingModuleEnabled` = Yes
- At least one `sprk_report` record with `isDefault = true`
- Power BI workspace contains the default report with semantic model data loaded

**Steps**
1. Navigate to the Spaarke MDA application: `https://spaarkedev1.crm.dynamics.com/main.aspx`
2. Click the **Reporting** item in the left navigation menu.
3. Wait for the `sprk_reporting` Code Page to load.
4. Start timing from page navigation complete.
5. Observe the Power BI embed iframe.
6. Wait for the report to display visible data (charts, tables, or KPI tiles populated).

**Expected Result**
- The Reporting page opens without errors.
- The Power BI iframe renders with a report visible.
- Data (charts, tables, or KPI values) is visible — not a blank or loading state.
- Total time from page open to data visible is **< 3 seconds** (NFR-02).
- No JavaScript console errors related to the embed or token acquisition.
- The BFF call to `/api/reporting/embed-token` returns HTTP 200 with a valid embed token.

**Failure Indicators**
- Blank iframe after 3 seconds.
- "Something went wrong" Power BI error banner.
- Console error: `TokenExpiredError` or `401 Unauthorized`.
- Network request to `/api/reporting/embed-token` returns non-200.

---

### TC-002 — Report Switching via Dropdown

**ID**: TC-002
**FR Reference**: FR-02
**ADR References**: ADR-012 (shared components), ADR-021 (Fluent v9)
**Priority**: Critical
**Automatable**: Yes (Playwright)

**Description**
Verifies that the report selector dropdown lists all catalog entries grouped by category and that selecting a different report causes the embed to switch to that report.

**Preconditions**
- Authenticated as `viewer@testcustomer.com`
- At least 3 `sprk_report` records across 2 different categories (e.g., Financial and Operational)
- Default report is currently rendered (TC-001 passes)

**Steps**
1. Navigate to the Reporting page.
2. Wait for the default report to render (TC-001 passes).
3. Click the report selector dropdown (located in the Reporting page header).
4. Verify the dropdown displays reports grouped by category labels.
5. Select a report from a **different** category than the currently displayed report.
6. Wait for the new report to render.

**Expected Result**
- Dropdown opens and displays `sprk_report` catalog entries.
- Reports are grouped under category headers (Financial, Operational, Compliance, Documents, Custom).
- After selecting a different report, the Power BI iframe refreshes and renders the new report.
- The new report's data is visible within 3 seconds of selection.
- The page does not perform a full page reload (navigation history unchanged).
- No embed token errors in console.

**Failure Indicators**
- Dropdown shows flat list (no category grouping).
- Selecting a report does not change what is displayed in the iframe.
- Page performs a full reload on report switch.

---

### TC-003 — Token Auto-Refresh (Silent Refresh at 80% TTL)

**ID**: TC-003
**FR Reference**: FR-04, NFR-03
**ADR References**: ADR-009 (Redis caching)
**Priority**: Critical
**Automatable**: Partially (requires time manipulation or short TTL test token)

**Description**
Verifies that the embed token auto-refreshes silently at 80% of its TTL using `report.setAccessToken()` without causing a page reload or `tokenExpired` event.

**Preconditions**
- Authenticated as `viewer@testcustomer.com`
- Report is rendered and visible
- Ability to either: (a) configure a short-TTL token for testing, or (b) wait until 80% of standard 1-hour TTL (~48 minutes), or (c) use browser DevTools to mock time
- Browser DevTools Network tab open, console open

**Steps**
1. Navigate to the Reporting page.
2. Wait for default report to render.
3. Open browser DevTools → Console tab.
4. Add a listener to confirm `tokenExpired` event does NOT fire:
   ```javascript
   window._tokenExpiredFired = false;
   // The embed component subscribes; check this flag if accessible
   ```
5. **Option A (preferred — configure short TTL)**: Set token TTL to 5 minutes in the BFF config for testing. Wait 4 minutes (80% of 5 minutes).
6. **Option B (production timing)**: Wait 48 minutes from initial page load.
7. Observe the Network tab for a call to `/api/reporting/embed-token` occurring without user interaction.
8. After the token refresh call, verify the report is still displayed with data.
9. Verify no full page reload occurs (check navigation timing API).

**Expected Result**
- A background HTTP call to `/api/reporting/embed-token` occurs at approximately 80% of the token TTL.
- The Power BI embed continues displaying the report without interruption.
- No `tokenExpired` event fires (verifiable via console or report event listener).
- No page reload occurs — the MDA navigation history is unchanged.
- The call uses `report.setAccessToken()` (visible in the embed component source or via DevTools XHR interception).

**Failure Indicators**
- `tokenExpired` event fires and report shows an error banner.
- Page reloads to re-acquire a token.
- Token refresh call only occurs after the token has already expired (> 100% TTL).

---

### TC-004 — Edit Mode (Authoring Toolbar)

**ID**: TC-004
**FR Reference**: FR-07
**ADR References**: ADR-006 (Code Page), ADR-021 (Fluent v9)
**Priority**: High
**Automatable**: Partially (toolbar presence automatable; authoring interaction is manual)

**Description**
Verifies that clicking Edit in the Reporting page toolbar switches the embed to edit mode, displays the Power BI authoring toolbar, and allows adding/resizing visuals and binding data fields.

**Preconditions**
- Authenticated as `author@testcustomer.com` (Author role)
- An existing report is displayed in view mode
- The semantic model has at least one data field (table/column)

**Steps**
1. Navigate to the Reporting page.
2. Wait for the default report to render in view mode.
3. Click the **Edit** button in the Spaarke page toolbar.
4. Wait for the edit mode to activate.
5. Verify the Power BI authoring toolbar appears (Visualizations pane, Fields pane, Filters pane on the right).
6. From the Visualizations pane, click a visual type (e.g., Clustered bar chart).
7. A placeholder visual appears on the report canvas — verify this.
8. From the Fields pane, drag a field onto the Values well.
9. Verify the visual renders with data from the field.
10. Resize the visual by dragging its corner — verify resize works.

**Expected Result**
- Edit button is visible and clickable for Author role users.
- Power BI embed transitions to edit mode — authoring toolbar (Visualizations, Fields, Filters panes) is visible.
- Adding a visual creates a placeholder on the canvas.
- Binding a data field updates the visual with data.
- Resizing a visual works via drag handle.
- No errors in console or Network tab.
- The embed mode is confirmed as `Edit` (not `View`).

**Failure Indicators**
- Edit button is absent for Author role users.
- Clicking Edit does not show the authoring toolbar.
- Fields pane is empty (semantic model not bound to embed token).
- Adding a visual or binding data causes a console error.

---

### TC-005 — New Report Creation

**ID**: TC-005
**FR Reference**: FR-06
**ADR References**: ADR-006 (Code Page)
**Priority**: High
**Automatable**: Partially

**Description**
Verifies that an Author user can create a new blank report, name it, and have it appear in the report catalog.

**Preconditions**
- Authenticated as `author@testcustomer.com` (Author role)
- Customer workspace has a bound semantic model
- Report catalog accessible via Dataverse `sprk_report` entity

**Steps**
1. Navigate to the Reporting page.
2. Click the **New Report** button in the page toolbar.
3. A dialog appears prompting for a report name — enter `E2E Test Report {timestamp}`.
4. Click **Create** (or equivalent confirm button).
5. Observe the embed area — a blank report canvas should open in edit mode.
6. Navigate away and return to the Reporting page.
7. Open the report selector dropdown.
8. Verify `E2E Test Report {timestamp}` appears in the dropdown.

**Expected Result**
- New Report button is visible for Author role users.
- A naming dialog appears and accepts input.
- After confirming, a blank report opens in edit mode in the embed.
- A `sprk_report` Dataverse record is created with:
  - `sprk_name` = entered report name
  - `sprk_isCustom` = true
  - `sprk_workspaceId` = customer workspace ID
  - `sprk_reportId` = newly created Power BI report GUID
- The report appears in the dropdown on next catalog load.
- The BFF call to create the report returns HTTP 201.

**Failure Indicators**
- New Report button is absent for Author role.
- Naming dialog does not appear.
- Blank canvas does not open (stays in view mode or shows error).
- `sprk_report` record is not created in Dataverse.
- Report does not appear in dropdown.

---

### TC-006 — Save Report Changes

**ID**: TC-006
**FR Reference**: FR-08
**ADR References**: ADR-006 (Code Page)
**Priority**: High
**Automatable**: Partially

**Description**
Verifies that saving changes in edit mode persists the report to the customer workspace and updates the catalog timestamp.

**Preconditions**
- Authenticated as `author@testcustomer.com` (Author role)
- Report is open in edit mode (TC-004 passes)
- A visual has been added or modified

**Steps**
1. Navigate to the Reporting page and open an existing report in edit mode.
2. Add a new text box with content "E2E Save Test" to the report canvas.
3. Click the **Save** button in the Spaarke page toolbar (or use Power BI built-in save).
4. Observe the save operation — a progress/spinner should briefly appear.
5. After save completes, navigate away from the Reporting page.
6. Return to the Reporting page and select the same report.
7. Verify the text box "E2E Save Test" is visible in view mode.

**Expected Result**
- Save button is visible and clickable in edit mode.
- A brief save indicator (spinner or "Saving..." toast) appears during the operation.
- After successful save, the report is visible in view mode with the added text box.
- The `sprk_report` Dataverse record `modifiedon` timestamp is updated.
- `report.save()` completes without error (verifiable in console).
- No "unsaved changes" prompt when navigating away after save.

**Failure Indicators**
- Save button is absent or disabled.
- Save spinner appears but completes with an error toast.
- Changes are not visible after returning to the report.
- `modifiedon` timestamp is unchanged on the `sprk_report` record.

---

### TC-007 — Save As (Create Named Copy)

**ID**: TC-007
**FR Reference**: FR-08
**ADR References**: ADR-006 (Code Page)
**Priority**: High
**Automatable**: Partially

**Description**
Verifies that Save As in edit mode creates a new named catalog entry with `isCustom = true`.

**Preconditions**
- Authenticated as `author@testcustomer.com` (Author role)
- Report is open in edit mode
- At least one modification has been made to the report

**Steps**
1. Navigate to the Reporting page and open an existing report in edit mode.
2. Add a text box with "E2E SaveAs Test" to the report canvas.
3. Click the **Save As** button in the Spaarke page toolbar.
4. A naming dialog appears — enter `E2E SaveAs Report {timestamp}`.
5. Click **Save** (or equivalent confirm button).
6. Observe the embed — the new report (copy) should now be displayed in view mode with the added text.
7. Open the report selector dropdown.
8. Verify `E2E SaveAs Report {timestamp}` appears in the dropdown.

**Expected Result**
- Save As button is visible in edit mode for Author role.
- A naming dialog accepts a new report name.
- After Save As:
  - A new Power BI report is created in the customer workspace (new `reportId`).
  - A new `sprk_report` Dataverse record is created with `sprk_isCustom = true`.
  - The original report is unmodified.
- The new report appears in the dropdown in the Custom category.
- `report.saveAs({ name: "..." })` call visible in DevTools (XHR or SDK call).

**Failure Indicators**
- Save As button is absent.
- No naming dialog appears.
- New report is not created in Dataverse.
- New report does not appear in dropdown.
- Original report is modified (Save As should not affect the original).

---

### TC-008 — Export to PDF

**ID**: TC-008
**FR Reference**: FR-09
**ADR References**: ADR-001 (BFF Minimal API)
**Priority**: High
**Automatable**: Partially (export trigger automatable; file content verification is manual)

**Description**
Verifies that triggering a PDF export causes the BFF to invoke the Power BI REST export API, displays a progress indicator, and delivers a downloadable PDF file.

**Preconditions**
- Authenticated as `viewer@testcustomer.com` (Viewer role — export available to all roles)
- A report is displayed in view mode with data
- The report has at least one page with visible data

**Steps**
1. Navigate to the Reporting page.
2. Wait for a report to render with data.
3. Click the **Export** button in the page toolbar.
4. A dropdown or dialog appears — select **PDF**.
5. Observe the UI — a progress indicator (spinner, progress bar, or "Exporting..." message) should appear.
6. Wait for the export to complete (may take 10–60 seconds for server-side rendering).
7. A file download prompt appears or file downloads automatically.
8. Open the downloaded PDF and verify it contains the report content.

**Expected Result**
- Export button and PDF option are visible for Viewer role.
- A progress indicator appears and remains until export completes.
- A `.pdf` file is downloaded with a meaningful filename (e.g., `{ReportName}-{date}.pdf`).
- The PDF contains the report pages with data — not blank pages.
- The BFF call to `/api/reporting/export` (POST with format=PDF) returns HTTP 200 and streams the file.
- No timeout error within 60 seconds for a standard report.

**Failure Indicators**
- Export button or PDF option is absent.
- No progress indicator — UI appears frozen.
- Download prompt never appears.
- Downloaded file is corrupt, blank, or 0 bytes.
- BFF returns non-200 status for export request.

---

### TC-009 — Export to PPTX

**ID**: TC-009
**FR Reference**: FR-09
**ADR References**: ADR-001 (BFF Minimal API)
**Priority**: Medium
**Automatable**: Partially

**Description**
Verifies that selecting PPTX export delivers a downloadable PowerPoint file.

**Preconditions**
- Authenticated as `viewer@testcustomer.com`
- A report is displayed in view mode with data

**Steps**
1. Navigate to the Reporting page.
2. Wait for a report to render.
3. Click **Export** → Select **PPTX**.
4. Observe the progress indicator.
5. Wait for the download.
6. Open the downloaded `.pptx` file in PowerPoint or LibreOffice.

**Expected Result**
- PPTX option is available in the Export dropdown.
- Progress indicator appears during export.
- A `.pptx` file downloads successfully.
- Opening the file shows slides containing the report visuals.
- Filename is meaningful (e.g., `{ReportName}-{date}.pptx`).

**Failure Indicators**
- PPTX option is absent.
- File downloads but cannot be opened.
- File is blank or contains no slides.

---

### TC-010 — Dark Mode Rendering

**ID**: TC-010
**FR Reference**: FR-01 (visual quality), NFR-06
**ADR References**: ADR-021 (Fluent v9, dark mode, no hard-coded colors)
**Priority**: High
**Automatable**: Partially (color inspection requires visual review)

**Description**
Verifies that toggling dark mode in the MDA correctly applies Fluent v9 design tokens to the Reporting page UI and that the Power BI report background is transparent (so the dark theme shows through).

**Preconditions**
- Authenticated as `viewer@testcustomer.com`
- Report is displayed in view mode
- MDA application supports dark mode toggle (via user settings or theme switcher)

**Steps**
1. Navigate to the Reporting page in the default (light) theme.
2. Verify report renders in light mode.
3. Open MDA settings (gear icon → Personalization Settings → or theme switcher).
4. Switch to **Dark** theme.
5. Return to the Reporting page (or observe live if theme applies dynamically).
6. Inspect the following UI elements visually:
   - Page background color (should match dark theme — dark grey/near-black)
   - Reporting page header and toolbar background
   - Report selector dropdown colors
   - The Power BI iframe background (should be transparent — dark Fluent theme shows through)
   - Button hover states
7. Open DevTools → Elements tab and inspect the Power BI iframe's `background` CSS property.
8. Check for any hard-coded hex color values in the `sprk_reporting` UI elements.

**Expected Result**
- Page header and toolbar adopt Fluent v9 dark theme tokens (dark background, light text).
- Report selector dropdown uses dark theme colors.
- The Power BI iframe `background` is `transparent` (or `rgba(0,0,0,0)`), allowing the dark page background to show through the report.
- No UI elements have hard-coded color values (e.g., `#FFFFFF`, `#000000`, `rgb(255,255,255)` as static styles).
- Text remains readable with sufficient contrast in dark mode.
- No visual artifacts or unthemed "white boxes" surrounding the embed.

**Failure Indicators**
- Power BI iframe has a white/light background in dark mode.
- Toolbar or header retains light colors after theme switch.
- Hard-coded colors visible via DevTools Element inspection.
- Text becomes unreadable (insufficient contrast).

---

### TC-011 — Module Disabled State

**ID**: TC-011
**FR Reference**: FR-11
**ADR References**: ADR-001 (BFF returns 404 when module disabled)
**Priority**: Critical
**Automatable**: Yes (Playwright + API assertions)

**Description**
Verifies that when `sprk_ReportingModuleEnabled` is set to **No**, the Reporting navigation item is hidden, all BFF reporting endpoints return HTTP 404, and the Code Page displays a friendly "module not available" message.

**Preconditions**
- Authenticated as `admin@testcustomer.com` (Admin role — to verify even admins are blocked)
- Access to Dataverse to set the `sprk_ReportingModuleEnabled` environment variable

**Steps**
1. Set `sprk_ReportingModuleEnabled` = **No** in Dataverse (via Settings → Advanced Settings → Administration → System Settings, or via the Dataverse API).
2. Perform a hard refresh of the MDA application (`Ctrl+Shift+R`).
3. Observe the left navigation menu — the **Reporting** item should be absent.
4. Attempt to navigate directly to the `sprk_reporting` Code Page URL (bookmark or direct URL).
5. Observe the Code Page content.
6. In DevTools Network tab, make a direct request to `GET /api/reporting/catalog`.
7. Observe the HTTP response code.
8. Re-enable: Set `sprk_ReportingModuleEnabled` = **Yes** and verify Reporting menu item returns.

**Expected Result**
- After setting to **No**:
  - The **Reporting** menu item is hidden from MDA left navigation.
  - Direct navigation to `sprk_reporting` Code Page shows a friendly "Reporting module is not available" (or equivalent) message — NOT an unhandled error.
  - `GET /api/reporting/catalog` returns **HTTP 404** (not 403 or 500).
  - `GET /api/reporting/embed-token` returns **HTTP 404**.
  - No embed attempt is made from the Code Page.
- After re-enabling to **Yes**: Reporting menu item reappears.

**Failure Indicators**
- Reporting menu item remains visible when module is disabled.
- BFF endpoints return 403 or 500 instead of 404.
- Code Page shows an unhandled exception or blank page (instead of a friendly message).
- Code Page still attempts to load an embed token while disabled.

---

### TC-012 — Unauthorized User (No Security Role)

**ID**: TC-012
**FR Reference**: FR-12
**ADR References**: ADR-008 (endpoint filters for auth)
**Priority**: Critical
**Automatable**: Yes (Playwright + API assertions)

**Description**
Verifies that a user without the `sprk_ReportingAccess` security role is blocked at the BFF layer with HTTP 403 and sees an access denied message in the Code Page.

**Preconditions**
- Authenticated as `noaccess@testcustomer.com` (no `sprk_ReportingAccess` role assigned)
- `sprk_ReportingModuleEnabled` = Yes
- The Reporting menu item may still be visible in navigation (module is enabled; access is denied at user level)

**Steps**
1. Sign in as `noaccess@testcustomer.com`.
2. Navigate to the Reporting page (via navigation menu or direct URL).
3. Observe the Code Page content.
4. Open DevTools Network tab.
5. Observe any request to `/api/reporting/catalog` or `/api/reporting/embed-token`.

**Expected Result**
- The Code Page loads but displays an access denied message (e.g., "You do not have permission to access Reporting. Contact your administrator.").
- No embed iframe is rendered.
- `GET /api/reporting/catalog` returns **HTTP 403** — not 200 or 500.
- `GET /api/reporting/embed-token` returns **HTTP 403** (if attempted).
- The 403 response body contains a `ProblemDetails` JSON object with a user-friendly `detail` message.
- No Power BI embed token is generated or returned to the browser.

**Failure Indicators**
- Code Page renders a report for a user without the security role.
- BFF returns HTTP 200 with embed token for unauthorized user.
- Code Page shows an unhandled exception instead of a friendly access denied message.
- BFF returns HTTP 401 instead of 403 (wrong error — user IS authenticated, just not authorized).

---

### TC-013 — Viewer Role Restrictions

**ID**: TC-013
**FR Reference**: FR-13
**ADR References**: ADR-008 (endpoint filters)
**Priority**: High
**Automatable**: Yes (UI element presence/absence)

**Description**
Verifies that a user with Viewer privileges sees only view and export controls — no Edit, New Report, or Delete buttons.

**Preconditions**
- Authenticated as `viewer@testcustomer.com` (Viewer privileges only)
- Report is displayed in view mode

**Steps**
1. Sign in as `viewer@testcustomer.com`.
2. Navigate to the Reporting page.
3. Wait for the report to render.
4. Inspect the Reporting page toolbar for the following buttons:
   - View (or view mode indicator) — should be present
   - Export — should be present
   - Edit — should be ABSENT
   - New Report — should be ABSENT
   - Delete — should be ABSENT

**Expected Result**
- **Present** for Viewer: Report selector dropdown, Export button (PDF/PPTX).
- **Absent** for Viewer: Edit button, New Report button, Delete button.
- Report renders in view-only mode — no authoring toolbar visible.
- Attempting to navigate to an edit mode URL directly (if applicable) should redirect to view mode or return an error.

**Failure Indicators**
- Edit button is visible to Viewer role.
- New Report button is visible to Viewer role.
- Delete button is visible to Viewer role.

---

### TC-014 — Author Role Permissions

**ID**: TC-014
**FR Reference**: FR-13
**ADR References**: ADR-008 (endpoint filters)
**Priority**: High
**Automatable**: Yes (UI element presence/absence)

**Description**
Verifies that a user with Author privileges sees View, Edit, New Report, and Export — but NOT Delete.

**Preconditions**
- Authenticated as `author@testcustomer.com` (Author privileges)
- Report is displayed in view mode

**Steps**
1. Sign in as `author@testcustomer.com`.
2. Navigate to the Reporting page.
3. Wait for the report to render.
4. Inspect the toolbar for all control buttons.

**Expected Result**
- **Present** for Author: Report selector dropdown, Export, Edit, New Report.
- **Absent** for Author: Delete button.
- Edit mode is accessible (clicking Edit shows authoring toolbar — TC-004 behavior).
- New Report button opens the naming dialog (TC-005 behavior).

**Failure Indicators**
- Edit button is absent for Author role.
- New Report button is absent for Author role.
- Delete button is visible for Author role.

---

### TC-015 — Admin Role Full Access

**ID**: TC-015
**FR Reference**: FR-13
**ADR References**: ADR-008 (endpoint filters)
**Priority**: High
**Automatable**: Yes (UI element presence/absence)

**Description**
Verifies that a user with Admin privileges sees all controls including Delete, and can perform a delete operation.

**Preconditions**
- Authenticated as `admin@testcustomer.com` (Admin privileges)
- At least one custom report (created in TC-005 or TC-007) exists in the catalog

**Steps**
1. Sign in as `admin@testcustomer.com`.
2. Navigate to the Reporting page.
3. Wait for the report to render.
4. Inspect the toolbar — verify Edit, New Report, Export, AND Delete buttons are present.
5. Select a custom report from the dropdown (one created in TC-005 or TC-007).
6. Click the **Delete** button.
7. A confirmation dialog should appear — confirm deletion.
8. Verify the deleted report no longer appears in the dropdown.
9. Verify the `sprk_report` Dataverse record is deleted or deactivated.

**Expected Result**
- **Present** for Admin: Report selector dropdown, Export, Edit, New Report, Delete.
- Clicking Delete shows a confirmation dialog (not immediate delete).
- After confirming, the report is removed from the dropdown.
- The `sprk_report` Dataverse record is deactivated or deleted.
- Standard product reports (non-custom) may have Delete disabled or hidden for safety.

**Failure Indicators**
- Delete button is absent for Admin role.
- No confirmation dialog before deletion.
- Report remains in dropdown after delete.
- Dataverse record persists after delete.

---

### TC-016 — Business Unit Row-Level Security

**ID**: TC-016
**FR Reference**: FR-05
**ADR References**: ADR-009 (Redis caching — embed tokens include RLS identity)
**Priority**: Critical
**Automatable**: Partially (requires visual data comparison — manual verification)

**Description**
Verifies that Business Unit RLS filters report data so that User A (BU-1) sees only BU-1 data and User B (BU-2) sees only BU-2 data in the same report.

**Preconditions**
- Two test users configured in different Business Units:
  - `bu1user@testcustomer.com` — member of Business Unit "BU-1"
  - `bu2user@testcustomer.com` — member of Business Unit "BU-2"
- Both users have `sprk_ReportingAccess` role
- The semantic model has an RLS role `BusinessUnitFilter` that filters data based on `USERNAME()` mapping to BU hierarchy
- Test data seeded: BU-1 has records A, B, C; BU-2 has records D, E, F (distinct, non-overlapping)
- Use the "Document Activity" or "Matter Pipeline" standard report (data includes BU association)

**Steps**
1. Sign in as `bu1user@testcustomer.com`.
2. Navigate to the Reporting page.
3. Open the **Matter Pipeline** (or **Document Activity**) report.
4. Note the data shown — record counts, specific names visible in tables.
5. Confirm only BU-1 records (A, B, C) are visible — BU-2 records (D, E, F) should NOT appear.
6. Sign out and sign in as `bu2user@testcustomer.com`.
7. Navigate to the Reporting page and open the same report.
8. Note the data shown.
9. Confirm only BU-2 records (D, E, F) are visible — BU-1 records (A, B, C) should NOT appear.
10. Verify the embed token in the BFF network request for `bu1user` contains `EffectiveIdentity` with BU-1 identifier; similarly for `bu2user`.

**Expected Result**
- User BU-1 sees exactly BU-1 data in the report.
- User BU-2 sees exactly BU-2 data in the report.
- Neither user sees the other BU's data.
- The `embed-token` API response body (if inspectable or logged) includes an `EffectiveIdentity.Username` set to the user's BU identifier.
- Redis cache stores separate tokens per user/BU combination (different cache keys).

**Failure Indicators**
- User BU-1 sees BU-2 data (or all data combined).
- User BU-2 sees BU-1 data.
- Both users see identical data regardless of BU.
- The embed token is missing `EffectiveIdentity` (RLS not applied).

---

### TC-017 — Multi-Deployment Model Verification

**ID**: TC-017
**FR Reference**: FR-17
**ADR References**: ADR-001 (BFF), ADR-009 (Redis caching)
**Priority**: High
**Automatable**: Partially (requires multi-environment test setup)

**Description**
Verifies that the Reporting module works correctly across all three Spaarke deployment models: multi-customer (shared tenant), dedicated (single customer, shared capacity), and customer tenant (customer's own Azure tenant).

**Preconditions**
- Access to all three deployment model environments:
  - **Multi-customer**: `https://spaarkedev1.crm.dynamics.com` (shared BFF, shared PBI capacity, SP profiles per customer)
  - **Dedicated**: A dedicated customer environment (own BFF instance, own capacity or dedicated pool)
  - **Customer tenant**: A customer-tenant environment (BFF in customer's Azure, customer's Entra ID app registration)
- Each environment has at least one `sprk_report` catalog entry and the standard reports deployed

**Steps for each deployment model:**
1. Navigate to the Reporting page in the environment under test.
2. Verify the report renders (TC-001 criteria).
3. Verify the report selector shows catalog entries (TC-002 criteria).
4. Verify an embed token is generated via `/api/reporting/embed-token` (Network tab, HTTP 200).
5. Verify the embed token includes the correct `WorkspaceId` for this customer/environment.
6. Verify the SP profile header (`X-PowerBI-Profile-Id`) is present in the PBI API call log (if accessible) — confirms per-customer isolation.

**Expected Result — Multi-customer model**:
- SP profiles isolate Customer A workspace from Customer B workspace.
- `X-PowerBI-Profile-Id` header differs per customer.
- Customer A cannot access Customer B reports via any API call.

**Expected Result — Dedicated model**:
- BFF uses environment-specific workspace ID (from environment variables, not hardcoded).
- Token generation works with dedicated capacity.

**Expected Result — Customer tenant model**:
- BFF uses customer's own Entra ID tenant for service principal auth.
- PBI scope `https://analysis.windows.net/.default` acquired from customer tenant.

**Failure Indicators**
- Report fails to render in any deployment model.
- SP profile header is absent (multi-customer isolation not enforced).
- One customer's data is accessible from another customer's embed token.
- Hardcoded workspace or tenant IDs in any deployment model.

---

## Test Execution Tracking

Use the following table to record execution results:

| ID | Test Case | Tester | Date | Result | Notes |
|----|-----------|--------|------|--------|-------|
| TC-001 | Report Rendering | | | ⬜ PASS / ⬜ FAIL / ⬜ SKIP | |
| TC-002 | Report Switching | | | ⬜ PASS / ⬜ FAIL / ⬜ SKIP | |
| TC-003 | Token Auto-Refresh | | | ⬜ PASS / ⬜ FAIL / ⬜ SKIP | |
| TC-004 | Edit Mode | | | ⬜ PASS / ⬜ FAIL / ⬜ SKIP | |
| TC-005 | New Report Creation | | | ⬜ PASS / ⬜ FAIL / ⬜ SKIP | |
| TC-006 | Save Report | | | ⬜ PASS / ⬜ FAIL / ⬜ SKIP | |
| TC-007 | Save As | | | ⬜ PASS / ⬜ FAIL / ⬜ SKIP | |
| TC-008 | Export to PDF | | | ⬜ PASS / ⬜ FAIL / ⬜ SKIP | |
| TC-009 | Export to PPTX | | | ⬜ PASS / ⬜ FAIL / ⬜ SKIP | |
| TC-010 | Dark Mode | | | ⬜ PASS / ⬜ FAIL / ⬜ SKIP | |
| TC-011 | Module Disabled | | | ⬜ PASS / ⬜ FAIL / ⬜ SKIP | |
| TC-012 | Unauthorized User | | | ⬜ PASS / ⬜ FAIL / ⬜ SKIP | |
| TC-013 | Viewer Role | | | ⬜ PASS / ⬜ FAIL / ⬜ SKIP | |
| TC-014 | Author Role | | | ⬜ PASS / ⬜ FAIL / ⬜ SKIP | |
| TC-015 | Admin Role | | | ⬜ PASS / ⬜ FAIL / ⬜ SKIP | |
| TC-016 | BU RLS | | | ⬜ PASS / ⬜ FAIL / ⬜ SKIP | |
| TC-017 | Multi-Deployment | | | ⬜ PASS / ⬜ FAIL / ⬜ SKIP | |

---

## Automation Guidance (Playwright)

For automated test cases, extend the existing Playwright framework in `tests/e2e/`:

```
tests/e2e/reporting/
├── pages/
│   └── ReportingPage.ts          # Page object for sprk_reporting Code Page
├── specs/
│   ├── report-rendering.spec.ts  # TC-001, TC-002
│   ├── security.spec.ts          # TC-011, TC-012, TC-013, TC-014, TC-015
│   └── export.spec.ts            # TC-008, TC-009
└── README.md                     # This file
```

**ReportingPage.ts skeleton:**

```typescript
import { Page, Locator, expect } from '@playwright/test';

export class ReportingPage {
  readonly page: Page;
  readonly reportEmbed: Locator;
  readonly reportSelector: Locator;
  readonly editButton: Locator;
  readonly newReportButton: Locator;
  readonly deleteButton: Locator;
  readonly exportButton: Locator;

  constructor(page: Page) {
    this.page = page;
    // The Code Page is loaded in an iframe within MDA
    this.reportEmbed = page.frameLocator('[data-id="reporting-embed-container"]').first();
    this.reportSelector = page.getByRole('combobox', { name: 'Select report' });
    this.editButton = page.getByRole('button', { name: 'Edit' });
    this.newReportButton = page.getByRole('button', { name: 'New Report' });
    this.deleteButton = page.getByRole('button', { name: 'Delete' });
    this.exportButton = page.getByRole('button', { name: 'Export' });
  }

  async navigate(): Promise<void> {
    await this.page.goto('/main.aspx?pagetype=custom&name=sprk_reporting');
    await this.page.waitForLoadState('networkidle');
  }

  async waitForReportRender(timeoutMs = 3000): Promise<void> {
    // Wait for the PBI embed iframe to become visible and contain content
    await expect(this.page.locator('iframe[title*="Power BI"]'))
      .toBeVisible({ timeout: timeoutMs });
  }
}
```

---

## Related Files

- Spec: `projects/spaarke-powerbi-embedded-r1/spec.md`
- Project context: `projects/spaarke-powerbi-embedded-r1/CLAUDE.md`
- BFF endpoints: `src/server/api/Sprk.Bff.Api/Api/Reporting/`
- Code Page: `src/solutions/Reporting/`
- Deployment script: `scripts/Deploy-ReportingReports.ps1`
- Quick checklist: `tests/e2e/reporting/smoke-test-checklist.md`
