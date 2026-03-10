# Entity Retirement Notes

> **Created**: 2026-03-09
> **Project**: email-communication-solution-r2 (Task 036)
> **Purpose**: Document Dataverse entities and services scheduled for retirement as part of the Communication Solution consolidation.

---

## Entities to Remove from Dataverse Solution

### 1. `sprk_emailprocessingrule`

**Current Purpose**: Stores email filtering rules that determine how incoming emails are processed (auto-save, ignore, review-required). Rules match on sender domain, subject patterns, and recipient addresses.

**Why Retiring**: The Communication Solution replaces rule-based filtering with the Communication Account model. Processing decisions are now driven by `sprk_communicationaccount` configuration (approved senders, default actions) rather than per-rule evaluation.

**Retirement Steps**:
1. Verify no active data in production `sprk_emailprocessingrule` table
2. Remove entity from Dataverse solution XML (`customizations.xml`)
3. Remove any views, forms, and sitemap references for this entity
4. Remove `FilterRuleCacheTtlMinutes` from `EmailProcessingOptions` (once rule evaluation code is fully removed)
5. Remove `AttachmentFilterService` rule-fetching logic that queries this entity
6. Delete entity definition from Dataverse environment after solution update

**Dependencies to Check**:
- `AttachmentFilterService.cs` — references filter rules from this entity
- `EmailProcessingOptions.FilterRuleCacheTtlMinutes` — cache TTL for rule lookups
- Redis cache keys prefixed with `email:filter:rules:` — clean up cached entries

---

### 2. `sprk_approvedsender`

**Current Purpose**: Stores approved email sender addresses in Dataverse for server-side validation. Phase 2 design intended to merge Dataverse-stored senders with config-based `CommunicationOptions.ApprovedSenders`.

**Why Retiring**: The Communication Solution Phase 1 uses `CommunicationOptions.ApprovedSenders` from `appsettings.json` exclusively. The Dataverse entity was planned for Phase 2 but is no longer needed — approved sender management will stay configuration-driven for simplicity and security (secrets/credentials should not be in Dataverse).

**Retirement Steps**:
1. Verify no active data in production `sprk_approvedsender` table
2. Remove entity from Dataverse solution XML
3. Remove any views, forms, and sitemap references
4. Confirm `ApprovedSenderValidator.cs` only reads from `CommunicationOptions` (no Dataverse queries)
5. Delete entity definition from Dataverse environment after solution update

**Dependencies to Check**:
- `ApprovedSenderValidator.cs` — should only use config, not Dataverse
- `CommunicationOptions.ApprovedSenders` — remains as the authoritative source

---

## Server-Side Sync (SSS) Retirement

### Background

Server-Side Sync was the original mechanism for detecting incoming emails in Dynamics 365 / Dataverse. It automatically tracked emails as `email` activity records, which the email-to-document automation system then processed via webhooks/polling.

### Why Retiring

The Communication Solution introduces direct Graph API integration for email operations:
- **Outbound**: Send via Graph API (`/me/sendMail` or `/users/{id}/sendMail`)
- **Inbound detection**: Graph subscriptions (webhooks) or polling via Graph API
- **No dependency on Dataverse email activity sync**

SSS adds complexity, latency, and unreliability (known issues with sync delays, duplicate detection, and mailbox configuration).

### SSS Retirement Steps

1. **Identify SSS mailbox configurations**:
   - Admin Center > Email Configuration > Mailboxes
   - Document which mailboxes have SSS enabled (incoming/outgoing/appointments)

2. **Disable SSS for affected mailboxes**:
   - Set incoming email delivery to "None" for mailboxes now handled by Communication Service
   - Keep SSS active for any mailboxes NOT migrated to Communication Service

3. **Remove Dataverse webhook registrations** that trigger on `email` entity create:
   - Service Endpoint registrations pointing to BFF API `/api/email/webhook`
   - Plugin steps registered on `email` entity `Create` message

4. **Clean up email activity records** (optional):
   - Bulk-delete old `email` activity records created by SSS (if no longer needed)
   - Retain records that have been converted to documents (cross-reference with SPE)

5. **Update monitoring**:
   - Remove SSS health check alerts
   - Add Communication Service health checks (Graph API connectivity, webhook subscription status)

### Risks and Mitigations

| Risk | Mitigation |
|------|-----------|
| Emails missed during SSS-to-Graph transition | Run both systems in parallel for 2 weeks before disabling SSS |
| Users relying on Dataverse email timeline | Communication records will populate timeline instead |
| SSS used by other Dynamics features (e.g., CRM email tracking) | Only disable SSS for mailboxes explicitly migrated; leave others untouched |

---

## Timeline

| Phase | Action | Status |
|-------|--------|--------|
| Phase C (current) | Document retirement plan | Done |
| Phase D | Remove entities from solution XML | Pending |
| Phase D | Disable SSS for migrated mailboxes | Pending |
| Phase E | Clean up Dataverse webhook registrations | Pending |
| Post-deployment | Monitor for 2 weeks, then hard-remove entities | Pending |
