# Spaarke Environment Provisioning App

> **Status**: In Progress
> **Branch**: `work/spaarke-environment-provisioning-app`
> **Started**: 2026-04-06

## Overview

Create a reusable `sprk_dataverseenvironment` entity in Dataverse as the platform's central environment registry. Refactor the self-service registration approval flow to read environment config from Dataverse instead of hardcoded `DemoProvisioningOptions` in appsettings. This eliminates config drift between environments and allows admins to manage environments through the MDA.

## Graduation Criteria

- [ ] Environment entity exists in Dev and Demo Dataverse with correct schema
- [ ] Admin can CRUD environment records via MDA form
- [ ] Registration request has Target Environment lookup (blank by default)
- [ ] Form approve validates environment is selected
- [ ] Grid approve fails individual records missing environment
- [ ] BFF reads config from Dataverse at approval time
- [ ] Malformed license JSON returns clear error
- [ ] Dev + Demo 1 seed data works with existing provisioning flow
- [ ] `DemoProvisioning__Environments__*` removed from Azure config
- [ ] Bulk approve uses per-record environment; missing ones fail individually

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
