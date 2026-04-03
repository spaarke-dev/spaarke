# CLAUDE.md — Spaarke Self-Service Registration App

## Project Context

Self-service demo access provisioning system. Public form → admin approval → auto-provision internal Entra ID account with licenses, Dataverse role, SPE access.

**Demo Environment**: `https://spaarke-demo.crm.dynamics.com/`
**Account Domain**: `demo.spaarke.com`
**Branch**: `work/spaarke-self-service-registration-app`

## Applicable ADRs

| ADR | Constraint |
|-----|-----------|
| ADR-001 | Minimal API for endpoints; BackgroundService for expiration; NO Azure Functions |
| ADR-004 | Idempotent provisioning — each step checks state before acting |
| ADR-008 | Endpoint filters for admin authorization on approve/reject |
| ADR-010 | DI minimalism — register concretes, ≤15 non-framework registrations |
| ADR-019 | ProblemDetails for all API errors with correlation IDs |

## Canonical Implementations to Follow

| Pattern | Reference File |
|---------|---------------|
| Endpoint structure | `src/server/api/Sprk.Bff.Api/Ai/AnalysisEndpoints.cs` |
| Scheduled BackgroundService | `Services/Communication/DailySendCountResetService.cs` |
| Email sending | `Services/Communication/CommunicationService.cs` |
| Graph API app-only | `Infrastructure/Graph/GraphClientFactory.cs` → `ForApp()` |
| Dataverse S2S | `Spaarke.Dataverse/DataverseServiceClientImpl.cs` |
| DI module | `Infrastructure/DI/CommunicationModule.cs` |
| Error handling | `.claude/patterns/api/error-handling.md` |

## Key Patterns

| Pattern File | Use For |
|-------------|---------|
| `.claude/patterns/api/endpoint-definition.md` | Registration endpoints structure |
| `.claude/patterns/api/background-workers.md` | Expiration BackgroundService |
| `.claude/patterns/api/send-email-integration.md` | Welcome/notification emails |
| `.claude/patterns/api/service-registration.md` | DI module for registration services |
| `.claude/patterns/auth/service-principal.md` | App-only Graph calls for user creation |
| `.claude/patterns/dataverse/web-api-client.md` | Dataverse S2S for systemuser/team ops |

## Relevant Guides

- `docs/guides/COMMUNICATION-DEPLOYMENT-GUIDE.md` — Email pipeline setup
- `docs/guides/DATAVERSE-AUTHENTICATION-GUIDE.md` — S2S auth patterns
- `docs/guides/DATAVERSE-HOW-TO-CREATE-UPDATE-SCHEMA.md` — Schema creation
- `docs/guides/CUSTOMER-DEPLOYMENT-GUIDE.md` — Customer provisioning reference

## Relevant Scripts

- `scripts/Register-EntraAppRegistrations.ps1` — Reference for Graph permission setup
- `scripts/Provision-Customer.ps1` — Reference for provisioning workflow
- `scripts/Deploy-DataverseSolutions.ps1` — Solution deployment to demo env
- `scripts/Deploy-BffApi.ps1` — BFF API deployment

## 🚨 MANDATORY: Task Execution Protocol

**When executing tasks in this project, Claude Code MUST invoke the `task-execute` skill.**

See root CLAUDE.md for the complete protocol. Key trigger phrases:
- "work on task X" → invoke task-execute
- "continue" / "next task" → check TASK-INDEX.md, invoke task-execute
- "resume task X" → invoke task-execute

**DO NOT** read POML files directly and implement manually.
