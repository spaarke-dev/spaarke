# Reporting Module — Deployment Smoke Test Checklist

> **Purpose**: Quick-run verification checklist for post-deployment validation of the Reporting module.
> **Time to complete**: ~30–45 minutes
> **Full test definitions**: See `README.md` in this directory.

---

## Pre-Flight: Environment Setup

Before starting, confirm:

- [ ] `sprk_ReportingModuleEnabled` = **Yes** in Dataverse environment variables
- [ ] BFF API health check passes: `GET https://{bff-url}/healthz` → HTTP 200
- [ ] Redis connection is live (BFF startup logs show no Redis errors)
- [ ] Power BI workspace is accessible by the service principal
- [ ] `Deploy-ReportingReports.ps1` has been run — all 5 standard reports deployed
- [ ] At least one `sprk_report` Dataverse record exists with `isDefault = true`
- [ ] F-SKU capacity is active and not throttled

---

## SECTION 1 — Core Rendering (Critical Path)

Run as: **Viewer role user**

### 1.1 — Report Renders on Page Load

- [ ] Navigate to Reporting page in MDA
- [ ] Power BI iframe is visible within 3 seconds
- [ ] Report displays data (not blank, not spinner-stuck)
- [ ] Zero console errors related to embed or token

**Result**: PASS / FAIL / BLOCKED

---

### 1.2 — Report Selector Dropdown

- [ ] Report selector dropdown is visible in page header
- [ ] Dropdown shows reports grouped by category (Financial, Operational, etc.)
- [ ] Default report is pre-selected
- [ ] Switching to a different report renders the new report

**Result**: PASS / FAIL / BLOCKED

---

### 1.3 — All 5 Standard Reports Render

Check each standard report loads without error:

- [ ] **Matter Pipeline** — renders with data
- [ ] **Financial Summary** — renders with data
- [ ] **Document Activity** — renders with data
- [ ] **Task Overview** — renders with data
- [ ] **Compliance Dashboard** — renders with data

**Result**: PASS / FAIL / BLOCKED

---

## SECTION 2 — Token Management

Run as: **Viewer role user**

### 2.1 — Token Auto-Refresh (Silent)

> Full verification per TC-003 (requires short TTL config or 48-min wait). Smoke test uses abbreviated check.

- [ ] Open Reporting page and wait 2 minutes
- [ ] DevTools Network tab: confirm `/api/reporting/embed-token` is NOT called repeatedly (no polling)
- [ ] Report continues to display data throughout (no error banner)
- [ ] If short-TTL test configured: token refresh call fires before `tokenExpired` event

**Result**: PASS / FAIL / SKIP (if full TTL test not feasible in this environment)

---

## SECTION 3 — Authoring (Author Role)

Run as: **Author role user**

### 3.1 — Edit Mode

- [ ] **Edit** button is visible in toolbar
- [ ] Clicking Edit switches embed to edit mode
- [ ] Power BI authoring toolbar (Visualizations, Fields, Filters panes) appears
- [ ] Can add a visual from Visualizations pane
- [ ] Can bind a data field to the visual

**Result**: PASS / FAIL / BLOCKED

---

### 3.2 — New Report

- [ ] **New Report** button is visible in toolbar
- [ ] Clicking New Report shows a naming dialog
- [ ] Entering a name and confirming opens a blank report in edit mode
- [ ] New `sprk_report` Dataverse record is created
- [ ] New report appears in the dropdown after creation

**Result**: PASS / FAIL / BLOCKED

---

### 3.3 — Save

- [ ] In edit mode, make a small change (add text box)
- [ ] Click **Save**
- [ ] Brief save indicator appears
- [ ] Navigate away and return to the same report
- [ ] The change is persisted (text box visible in view mode)

**Result**: PASS / FAIL / BLOCKED

---

### 3.4 — Save As

- [ ] In edit mode, make a change
- [ ] Click **Save As** → naming dialog appears
- [ ] Enter a new name and confirm
- [ ] A new catalog entry is created (visible in dropdown under Custom category)
- [ ] Original report is unchanged

**Result**: PASS / FAIL / BLOCKED

---

## SECTION 4 — Export

Run as: **Viewer role user** (export available to all roles)

### 4.1 — Export to PDF

- [ ] **Export** button is visible
- [ ] Clicking Export → PDF shows a progress indicator
- [ ] A `.pdf` file downloads (within 60 seconds)
- [ ] Opening the PDF shows report content (not blank)

**Result**: PASS / FAIL / BLOCKED

---

### 4.2 — Export to PPTX

- [ ] Clicking Export → PPTX shows a progress indicator
- [ ] A `.pptx` file downloads
- [ ] Opening the file shows report slides

**Result**: PASS / FAIL / BLOCKED

---

## SECTION 5 — Visual / Theming

Run as: **Viewer role user**

### 5.1 — Dark Mode

- [ ] Switch MDA to Dark theme
- [ ] Reporting page header/toolbar adopts dark Fluent v9 colors
- [ ] Power BI iframe background is transparent (dark page color shows through)
- [ ] No "white box" artifact around the embed in dark mode
- [ ] Text remains readable

**Result**: PASS / FAIL / BLOCKED

---

## SECTION 6 — Access Control (Security)

### 6.1 — Module Disabled Gate

Run as: **Admin user** (to toggle the env var)

- [ ] Set `sprk_ReportingModuleEnabled` = **No** in Dataverse
- [ ] Hard refresh MDA — **Reporting** nav item is hidden
- [ ] Navigate directly to the Code Page URL — "module not available" message shown (no error)
- [ ] `GET /api/reporting/catalog` returns **HTTP 404**
- [ ] Re-enable: set `sprk_ReportingModuleEnabled` = **Yes** — Reporting nav item returns

**Result**: PASS / FAIL / BLOCKED

---

### 6.2 — No Security Role (Unauthorized)

Run as: **User without `sprk_ReportingAccess` role**

- [ ] Navigate to the Reporting page
- [ ] Access denied message is shown (user-friendly, not a stack trace)
- [ ] No embed iframe is rendered
- [ ] `GET /api/reporting/catalog` returns **HTTP 403**

**Result**: PASS / FAIL / BLOCKED

---

### 6.3 — Viewer Role Button Visibility

Run as: **Viewer role user**

- [ ] Export button: VISIBLE
- [ ] Edit button: HIDDEN
- [ ] New Report button: HIDDEN
- [ ] Delete button: HIDDEN

**Result**: PASS / FAIL / BLOCKED

---

### 6.4 — Author Role Button Visibility

Run as: **Author role user**

- [ ] Export button: VISIBLE
- [ ] Edit button: VISIBLE
- [ ] New Report button: VISIBLE
- [ ] Delete button: HIDDEN

**Result**: PASS / FAIL / BLOCKED

---

### 6.5 — Admin Role Button Visibility

Run as: **Admin role user**

- [ ] Export button: VISIBLE
- [ ] Edit button: VISIBLE
- [ ] New Report button: VISIBLE
- [ ] Delete button: VISIBLE

**Result**: PASS / FAIL / BLOCKED

---

## SECTION 7 — Business Unit RLS

Requires two test users in different BUs with distinct test data.

### 7.1 — BU Data Isolation

- [ ] Sign in as **BU-1 user** → verify only BU-1 data visible in report
- [ ] Sign in as **BU-2 user** → verify only BU-2 data visible in same report
- [ ] Neither user sees the other BU's data

**Result**: PASS / FAIL / SKIP (if BU test data not configured)

---

## SECTION 8 — Deployment Model Check

Only applicable if multiple deployment models are in scope for this release.

### 8.1 — Multi-Customer (SP Profile Isolation)

- [ ] `X-PowerBI-Profile-Id` header present in PBI API calls (confirms SP profile used)
- [ ] Customer A cannot access Customer B workspace

**Result**: PASS / FAIL / N/A

---

### 8.2 — Dedicated Deployment

- [ ] Reporting module works with dedicated capacity/workspace config
- [ ] Environment variables contain correct workspace ID (not hardcoded)

**Result**: PASS / FAIL / N/A

---

### 8.3 — Customer Tenant Deployment

- [ ] Token acquisition uses customer tenant's Entra ID
- [ ] Report renders correctly in customer-tenant environment

**Result**: PASS / FAIL / N/A

---

## Summary

| Section | Pass | Fail | Blocked | Skip |
|---------|------|------|---------|------|
| 1. Core Rendering | | | | |
| 2. Token Management | | | | |
| 3. Authoring | | | | |
| 4. Export | | | | |
| 5. Visual/Theming | | | | |
| 6. Access Control | | | | |
| 7. BU RLS | | | | |
| 8. Deployment Models | | | | |
| **TOTAL** | | | | |

---

## Sign-Off

| Role | Name | Date | Signature |
|------|------|------|-----------|
| QA Engineer | | | |
| Developer | | | |
| Product Owner | | | |

**Deployment approved for production**: YES / NO / CONDITIONAL

**Conditions (if any)**:

---

## Defect Log

| # | TC | Description | Severity | Status |
|---|----|-------------|----------|--------|
| | | | | |

---

*Full test case details: `tests/e2e/reporting/README.md`*
*Spec: `projects/spaarke-powerbi-embedded-r1/spec.md`*
