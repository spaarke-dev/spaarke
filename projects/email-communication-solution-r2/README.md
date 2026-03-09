# Email Communication Solution R2

> **Status**: In Progress
> **Branch**: `work/email-communication-solution-r2`
> **Created**: 2026-03-09
> **Priority**: High — required for send/receive testing

## Overview

Unified Communication Service replacing fragmented email infrastructure with Dataverse-managed `sprk_communicationaccount` entity and Microsoft Graph API. Migrates from Server-Side Sync to Graph subscriptions. Pre-launch project — no backward compatibility.

## Problem

1. No central mailbox management (appsettings.json-only)
2. No incoming email processing for `sprk_communication` records
3. No individual user sending (shared mailbox only)
4. Fragmented configuration across Exchange/Graph/BFF
5. Email-to-document pipeline coupled to Server-Side Sync

## Solution

Five-phase implementation building on existing R1 infrastructure:

| Phase | Deliverable | Dependencies |
|-------|-------------|-------------|
| **A** | Communication Account entity + outbound config migration | None (foundation) |
| **B** | Individual user outbound via OBO auth | Phase A |
| **C** | Inbound shared mailbox via Graph subscriptions | Phase A |
| **D** | Verification & admin UX | Phases A-C |
| **E** | Email-to-document migration (Graph-based) | Phase C |

**Execution order**: A → B+C (parallel) → E → D

## Key Decisions

- **Graph subscriptions** replace Server-Side Sync entirely
- **Pre-launch clean cutover** — no migration of existing data
- **Multi-tenant deployment** readiness for all components
- **`@spaarke/auth`** for all client-side authentication
- **Existing `sdap-jobs` queue** with new `ProcessIncomingCommunication` job type
- **EML archival default ON** for both directions with opt-out config

## Existing Infrastructure (From R1)

Many components already partially implemented:
- `CommunicationService.cs`, `CommunicationEndpoints.cs`, `CommunicationAccountService.cs`
- `GraphSubscriptionManager.cs`, `IncomingCommunicationProcessor.cs`
- `InboundPollingBackupService.cs`, `CommunicationModule.cs`
- Models: `SendMode`, `AccountType`, `AuthMethod`, `CommunicationAccount`, etc.
- `sprk_communicationaccount` entity deployed in Dataverse

## Graduation Criteria

1. [ ] Outbound shared mailbox send works end-to-end
2. [ ] Individual user "Send as me" works
3. [ ] Inbound monitoring auto-creates communication records (< 60s)
4. [ ] Association resolution links incoming to entities
5. [ ] Admin manages accounts entirely via Dataverse UI
6. [ ] Verification endpoint confirms mailbox access
7. [ ] Missed webhooks caught by polling (< 15 min)
8. [ ] Inbound/outbound .eml archival to SPE with child attachments
9. [ ] Server-Side Sync fully retired
10. [ ] Legacy components deleted

## Quick Links

- [Spec](spec.md) | [Plan](plan.md) | [Design](design-communication-accounts.md)
- [Task Index](tasks/TASK-INDEX.md)
- [Project CLAUDE.md](CLAUDE.md)
