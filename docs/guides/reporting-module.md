# Reporting Module — User Guide

> **Document Version**: 1.0
> **Last Updated**: 2026-04-01
> **Module**: Reporting R1
> **Audience**: Business Users, Legal Operations, Matter Managers

---

## Table of Contents

1. [Overview](#overview)
2. [Getting Started](#getting-started)
3. [Viewing Reports](#viewing-reports)
4. [Creating Reports](#creating-reports)
5. [Editing Reports](#editing-reports)
6. [Saving Reports](#saving-reports)
7. [Exporting Reports](#exporting-reports)
8. [Dark Mode](#dark-mode)
9. [Troubleshooting](#troubleshooting)

---

## Overview

The Reporting module embeds interactive Power BI reports directly inside Spaarke — no separate Power BI license, no separate browser tab. Reports load from a report catalog, apply your organization's data filters automatically, and support in-browser editing for users with authoring access.

### Key Capabilities

| Capability | Description |
|-----------|-------------|
| **View reports** | Browse and view interactive reports with drilldown, filters, and bookmarks |
| **Category groups** | Reports organized by Financial, Operational, Compliance, Documents, and Custom |
| **In-browser authoring** | Create and edit reports without Power BI Desktop or any additional software |
| **Save and Save As** | Save changes to existing reports or create a named copy |
| **Export** | Download reports as PDF or PowerPoint (PPTX) files |
| **Dark mode** | Automatically adapts to your Spaarke theme (light or dark) |
| **Data security** | Reports show only data for your business unit — no cross-BU data leakage |

### Who Has Access

Access to the Reporting module is controlled by your administrator. You must be assigned the `sprk_ReportingAccess` security role (or higher) to see the Reporting page. If you cannot see Reporting in the navigation, contact your administrator.

| Role | What You Can Do |
|------|----------------|
| **Viewer** | View reports, interact with filters and drilldowns |
| **Author** | Everything a Viewer can do, plus create and edit reports |
| **Admin** | Everything an Author can do, plus delete reports |

---

## Getting Started

### How to Open the Reporting Module

1. In Spaarke, look for **Reporting** in the left navigation or site map.
2. Click **Reporting** to open the full-page report viewer.
3. The page loads with the default report selected automatically.

If the Reporting page is not visible in navigation, the module may be disabled for your organization, or you may not have the required security role. Contact your administrator.

### First-Time Experience

On your first visit, the page loads the default report for your category. The report data is filtered to your business unit automatically — you will only see records and metrics relevant to your team.

Report load times are typically under 3 seconds. If a report takes longer, see [Troubleshooting](#troubleshooting).

---

## Viewing Reports

### Selecting a Report

Use the **report dropdown** at the top of the page to switch between reports. Reports are grouped by category:

| Category | What It Contains |
|----------|-----------------|
| **Financial** | Financial Summary — billing, spend, budget variance |
| **Operational** | Matter Pipeline, Task Overview — matter stages, task completion |
| **Compliance** | Compliance Dashboard — deadline tracking, overdue items |
| **Documents** | Document Activity — upload volume, review status |
| **Custom** | Reports your team has created in the Reporting module |

Click on any report name to load it. The page refreshes the embedded report without a full page reload.

### Filters and Slicers

Most reports include built-in filters (slicers) displayed within the report canvas. Use these to narrow the report to a date range, matter type, responsible attorney, or other dimension. Your filter selections are remembered while you stay on the page.

> **Note**: All reports apply your business unit filter automatically. You cannot remove this filter — it ensures you only see data you are authorized to view.

### Drilldown and Drill-through

Reports support standard Power BI interactions:

- **Drilldown**: Click a bar, column, or data point to drill into more detail.
- **Drill-through**: Right-click a data point and choose a drill-through target page to jump to a detailed view.
- **Back button**: Use the in-report back button (top-left of the report canvas) to return after a drill-through.

### Cross-Highlighting

Clicking a visual element (e.g., a slice in a pie chart) highlights related data across other visuals on the same page. Click the same element again or click an empty area to clear the selection.

---

## Creating Reports

> **Requires Author or Admin role.**

To create a new report from scratch using your organization's data:

1. Click the **New Report** button in the Reporting toolbar.
2. Enter a name for the report in the dialog that appears.
3. Click **Create**.

The new report opens in edit mode with an empty canvas connected to your organization's data model. Use the **Fields** pane (right side) to drag data fields onto the canvas and build visuals.

Custom reports you create appear in the **Custom** category of the report dropdown.

> **Tip**: Reports are stored in your organization's Power BI workspace and cataloged in Spaarke automatically. Your administrator can see all reports — including custom ones.

---

## Editing Reports

> **Requires Author or Admin role.**

To edit an existing report:

1. Select the report you want to edit from the report dropdown.
2. Click the **Edit** button in the Reporting toolbar. The report switches to edit mode.
3. Use the editing toolbar that appears to modify the report:
   - Add new visuals from the **Visualizations** pane
   - Resize and reposition visuals by dragging
   - Bind data fields by dragging from the **Fields** pane into a visual
   - Set visual-level and page-level filters
   - Add or remove report pages using the page tabs at the bottom
4. When finished, save your changes (see [Saving Reports](#saving-reports)).

To exit edit mode without saving, click **Discard Changes** or navigate away.

> **Note**: Standard product reports (Financial Summary, Matter Pipeline, etc.) can be edited if you have the Author role, but consider using **Save As** to create a copy rather than modifying the standard version.

---

## Saving Reports

### Save (Overwrite)

To save changes to the current report, click the **Save** button in the Reporting toolbar while in edit mode. This overwrites the existing report with your changes.

### Save As (Create a Copy)

To save your changes as a new report without affecting the original:

1. Click the **Save As** button in the Reporting toolbar.
2. Enter a name for the new report.
3. Click **Save**.

The new report is created in your organization's workspace and added to the **Custom** category in the report dropdown with a copy of the original's content. The original report is unchanged.

Custom reports are marked internally so administrators can distinguish them from standard product reports.

---

## Exporting Reports

To download the current report as a file:

1. Click the **Export** button in the Reporting toolbar.
2. Select the export format:
   - **PDF** — a static document suitable for sharing or printing
   - **PowerPoint (PPTX)** — a presentation file with one slide per report page
3. A progress indicator appears while the export is prepared (this may take 15–60 seconds depending on report size).
4. When the export is ready, the file downloads automatically to your browser's default download location.

> **Note**: Exports are generated server-side using the current data in the report. Active filter selections are preserved in the export.

---

## Dark Mode

The Reporting module automatically detects your Spaarke theme. When you switch to dark mode in Spaarke, the Reporting page updates its chrome and background accordingly. The embedded report canvas uses a transparent background so it blends with the current theme.

No action is required — dark mode is applied automatically.

---

## Troubleshooting

### Report Does Not Load

**Symptom**: The report area shows a spinner that never resolves, or a generic error message.

**Steps to try**:
1. Refresh the page (F5).
2. Check your internet connection — the Reporting module connects to the Power BI service.
3. If the problem persists, note the error message and contact your administrator. Provide the report name and approximately what time the issue occurred.

### "Access Denied" or 403 Error

**Symptom**: A message indicates you do not have access to the Reporting module or a specific report.

**Cause**: You do not have the `sprk_ReportingAccess` security role assigned, or the module has been disabled.

**Resolution**: Contact your Spaarke administrator to request access.

### "Module Not Available" Message

**Symptom**: The Reporting page shows a "Module not available" message instead of a report.

**Cause**: The Reporting module has been disabled for your environment by an administrator.

**Resolution**: Contact your administrator to check whether the module is intended to be enabled.

### Report Shows Outdated Data

**Cause**: Reports use imported data that refreshes on a scheduled basis (typically 3 times per business day). Real-time data is not available in R1.

**Resolution**: Wait for the next scheduled refresh. Refresh times are set by your administrator. If data appears significantly out of date, contact your administrator to check the refresh schedule.

### Export Takes a Long Time or Fails

**Cause**: Large reports with many pages or visuals take longer to export. A timeout occurs if the export exceeds the allowed time.

**Resolution**: Try exporting a report with fewer pages, or try again later. If the error persists, contact your administrator.

### Token Expired Error

**Symptom**: A session or token expiry error appears while viewing a report.

**Cause**: Your session token expired while you were inactive. This should not happen under normal use (tokens refresh automatically), but may occur after extended inactivity.

**Resolution**: Refresh the page to re-authenticate and reload the report.

---

*Spaarke Reporting Module — User Guide v1.0*
