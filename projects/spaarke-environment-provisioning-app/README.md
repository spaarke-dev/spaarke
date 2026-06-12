# Spaarke Environment Provisioning App

> **Status**: Complete (2026-06-12) — one criterion carried to r2, live-provisioning sign-off pending
> **Branch**: `work/spaarke-environment-provisioning-app`
> **Started**: 2026-04-06

## Overview

Create a reusable `sprk_dataverseenvironment` entity in Dataverse as the platform's central environment registry. Refactor the self-service registration approval flow to read environment config from Dataverse instead of hardcoded `DemoProvisioningOptions` in appsettings. This eliminates config drift between environments and allows admins to manage environments through the MDA.

## Graduation Criteria

- [x] Environment entity exists in Dev Dataverse with correct schema (16 cols; Demo Dataverse deferred — registry lives in admin env only)
- [x] Admin can CRUD environment records via MDA form
- [x] Registration request has Target Environment lookup (blank by default)
- [x] Form approve validates environment is selected (FR-06, static-verified)
- [x] Grid approve fails individual records missing environment (FR-07, static-verified)
- [x] BFF reads config from Dataverse at approval time (no caching, NFR-01)
- [x] Malformed license JSON returns clear error (unit-tested 2026-06-12)
- [x] Dev + Demo 1 seed data populated incl. real license SKU IDs (D-040-02 fix)
- [ ] `DemoProvisioning__Environments__*` removed from Azure config — **BLOCKED, carried to r2**: still required by `DemoExpirationService` (see notes/e2e-test-results.md)
- [x] Bulk approve uses per-record environment; missing ones fail individually (static-verified; live bulk run = manual sign-off item)

See [notes/e2e-test-results.md](notes/e2e-test-results.md) for full E2E disposition (7/11 verified, 4 manual sign-off items, 1 carried).

## Architecture

- **Entity**: `sprk_dataverseenvironment` — 16 columns, OrganizationOwned
- **Lookup**: `sprk_dataverseenvironmentid` on `sprk_registrationrequest`
- **New Service**: `DataverseEnvironmentService` — reads environment config via Dataverse Web API
- **Refactored**: `RegistrationEndpoints.ApproveRequest` — reads environment from lookup instead of config
- **Refactored**: `sprk_registrationribbon.js` — removes environment picker dialog, validates lookup

## Key Constraints

- No Dataverse plugins (ADR-002, NFR-02)
- No environment config caching (NFR-01)
- Concrete DI registration only (ADR-010)
- ProblemDetails for all errors (ADR-019)
- Endpoint filters for auth (ADR-008)

## Quick Links

- [Spec](spec.md) | [Plan](plan.md) | [Task Index](tasks/TASK-INDEX.md)
- [Design](design.md) | [Project CLAUDE.md](CLAUDE.md)
