# SDAP Office Integration

> **Status**: Complete
> **Created**: 2026-01-20
> **Completed**: 2026-01-20
> **Progress**: 100%
> **Project**: 1 of 3 in Office + Teams Integration Initiative

---

## Quick Links

- [Implementation Plan](plan.md)
- [Task Index](tasks/TASK-INDEX.md)
- [Design Specification](spec.md)
- [Original Design](design.md)

---

## Overview

This project builds a unified Office Integration Platform enabling Outlook and Word add-ins to save content to Spaarke DMS and share documents from Spaarke. The platform uses a shared React task pane UI with host-specific adapters, backed by .NET Minimal API endpoints and async job processing.

---

## Problem Statement

Corporate counsel needs to manage email correspondence and document versions as part of their matter/project workflows, but current processes require manual copying and uploading:

- Emails and attachments must be manually saved and uploaded to the DMS
- No direct integration between Office applications and Spaarke document management
- Document sharing with external parties requires manual access provisioning
- No visibility into document processing status (profile summaries, indexing, etc.)

---

## Proposed Solution

Build Outlook and Word add-ins that provide:

1. **Save to Spaarke** - File emails, attachments, and Word documents directly to associated Matters/Projects/Invoices/Accounts/Contacts
2. **Share from Spaarke** - Insert document links or attach copies in Outlook compose
3. **Grant Access** - Create invitations for external recipients (stub for External Portal integration)
4. **Real-time Status** - SSE-based job status updates for document processing

### Technology Stack

| Layer | Technology |
|-------|------------|
| Add-in UI | React + Fluent UI v9 + Office.js |
| Authentication | NAA (Nested App Authentication) with MSAL.js 3.x |
| Manifest | Unified (Outlook) + XML (Word) |
| Backend | .NET 8 Minimal API |
| Background Jobs | BackgroundService + Azure Service Bus |
| File Storage | SharePoint Embedded via SpeFileStore facade |
| Data | Dataverse (EmailArtifact, AttachmentArtifact, ProcessingJob) |

---

## Scope

### In Scope

- Outlook Add-in (New Outlook + Outlook Web)
  - Read mode: "Save to Spaarke" for emails and attachments
  - Compose mode: "Share from Spaarke" (links and attachments)
  - Compose mode: "Grant access" for external recipients
- Word Add-in (Desktop + Web)
  - "Save to Spaarke" and "Save new version"
  - "Share / Insert link / Attach copy"
  - "Grant access" for external recipients
- Shared Office Integration Platform
  - React task pane with host adapters
  - Typed API client
  - Job status manager (SSE + polling fallback)
- Backend APIs (`/office/*` endpoints)
- Background workers (upload, profile, index, analysis)
- Dataverse schema extensions

### Out of Scope

- Classic Outlook (COM add-in)
- "Open from Spaarke" in Word
- Teams app integration (separate project: SDAP-teams-app)
- External portal (separate project: SDAP-external-portal)
- Mailbox automation (future, feature-flagged)
- Mobile Office apps
- "Document Only" saves (must have association target)

---

## Graduation Criteria

- [x] Outlook add-in installs and loads in New Outlook and Outlook Web
- [x] Word add-in installs and loads in Word Desktop and Word Web
- [x] NAA authentication works silently for supported clients
- [x] User can save email with attachments to a Matter, Project, Invoice, Account, or Contact
- [x] User can create entities inline via Quick Create
- [x] Job status updates via SSE within 1 second of stage change
- [x] Duplicate email detection returns existing document
- [x] Save without association target returns OFFICE_003 error
- [x] User can insert document links into Outlook compose
- [x] User can attach document copies from Spaarke
- [x] All endpoints return ProblemDetails on error
- [x] Dark mode displays correctly
- [x] Keyboard navigation works in task pane (WCAG 2.1 AA)

---

## Dependencies

### Prerequisites (Exist)

- Matter, Project, Invoice, Account, Contact entities
- Document entity
- SpeFileStore facade
- UAC module
- Azure Service Bus

### External Dependencies

- Office.js CDN
- MSAL.js 3.x (NPM)
- Microsoft 365 Admin Center (deployment)
- Azure AD App Registrations (2: add-in + BFF)

### Cross-Project Dependencies

- SDAP-external-portal: ExternalUser, Invitation, AccessGrant entities and POST /external/invitations API (stub until available)

---

## Related Projects

| Project | Relationship |
|---------|--------------|
| SDAP-teams-app | Consumes same backend APIs, different client |
| SDAP-external-portal | Provides invitation APIs consumed by "Grant access" feature |

---

*This README is generated from spec.md. For implementation details, see [plan.md](plan.md).*
