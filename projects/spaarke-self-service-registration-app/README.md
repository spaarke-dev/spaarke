# Spaarke Self-Service Registration App

> **Status**: In Progress
> **Branch**: `work/spaarke-self-service-registration-app`
> **Created**: 2026-04-03

## Overview

Self-service demo access system: prospective users submit a public "Request Early Access" form on `spaarke.com`, an admin one-click approves via MDA ribbon button, and the system auto-provisions an internal Entra ID account (`user@demo.spaarke.com`) with licenses, Dataverse role, and SPE access. Welcome email with credentials sent automatically. Expired accounts disabled daily.

## Key Design Decisions

- **Internal accounts** (`demo.spaarke.com`) — not B2B guest — avoids cross-tenant session conflicts
- **Synchronous provisioning** — no Service Bus for Phase 1 (~10-30s per user)
- **MDA ribbon button** for admin approval — centralized, auditable, extensible
- **No MFA** for demo accounts — Conditional Access group exclusion
- **Multi-environment config** — add demo environments via appsettings array
- **14-day default expiration** — admin can adjust per record

## Architecture

```
spaarke.com/demo          BFF API                    Demo Dataverse
(public form)        (provisioning engine)        (sprk_registrationrequest)
     │                      │                           │
     ├─ Submit ────────────▶│ Create record ───────────▶│
     │                      │ Notify admin ─────────────│──▶ Email
     │                      │                           │
     │              Admin clicks "Approve" ◄────────────│
     │                      │                           │
     │                      ├─ Create Entra user        │
     │                      ├─ Assign licenses          │
     │                      ├─ Sync Dataverse user ────▶│
     │                      ├─ Add to Demo Team ───────▶│
     │                      ├─ Grant SPE access         │
     │                      ├─ Send welcome email       │
     │                      └─ Update status ──────────▶│
```

## Deliverables

| Stream | Description | Location |
|--------|-------------|----------|
| A. BFF API | Registration endpoints, provisioning, expiration service | `src/server/api/Sprk.Bff.Api/` |
| B. Website | Request Early Access form | `spaarke-website` repo |
| C. Dataverse | `sprk_registrationrequest` table, views, form | Demo environment solution |
| D. Ribbon | Approve/Reject buttons + JS webresource | Demo environment solution |
| E. Entra Scripts | Security group, Conditional Access, Graph permissions | `scripts/` |
| F. Email Templates | Admin notification, welcome, warning, expired | `src/server/api/` |

## Graduation Criteria

- [ ] Website form accepts and submits requests
- [ ] BFF API creates registration records in Demo Dataverse
- [ ] Admin notification email sent on new request
- [ ] MDA view shows pending requests with Approve/Reject ribbon buttons
- [ ] "Approve" provisions full account in < 60 seconds
- [ ] User receives welcome email with working credentials
- [ ] User can log in to demo environment with demo account
- [ ] No MFA prompt for demo accounts
- [ ] Expired accounts automatically disabled
- [ ] Pre-expiration warning email sent 3 days before expiry

## Quick Links

- [Specification](spec.md)
- [Implementation Plan](plan.md)
- [Task Index](tasks/TASK-INDEX.md)
- [Design Document](design.md)
