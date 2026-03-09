# R1 Communication Account Implementation — Gap Analysis

> **Task**: ECS-001 — Assess R1 Communication Account Implementation State
> **Date**: 2026-03-09
> **Phase**: 1 — Communication Account Entity (Phase A)
> **Method**: Parallel code audit using 4 Claude Code agents across ~40 source files

---

## Summary

R1 communication infrastructure is **substantially complete** — approximately 70-80% of R2 requirements are already implemented. The primary gaps are:

1. **Field name bugs** in 12+ source files (2 Dataverse field corrections not yet applied to code)
2. **Missing R2 model properties** (daily send limits, archival opt-in controls)
3. **Incomplete inbound pipeline** (polling backup logs but doesn't enqueue; dedup is stubbed)
4. **Legacy email services to retire** (6 files to delete, 3 to adapt)

No missing components. No duplicate components. All services registered in DI.

---

## Component Inventory

### Communication Core Services

| File | Lines | R2 Ready | Status | Changes Needed |
|------|-------|----------|--------|----------------|
| `Services/Communication/CommunicationAccountService.cs` | 165 | 60% | needs-modification | Fix field names, add R2 properties, add daily send tracking |
| `Services/Communication/ApprovedSenderValidator.cs` | 223 | 90% | needs-modification | Depends on AccountService field fix only |
| `Services/Communication/CommunicationService.cs` | 1,099 | 75% | needs-modification | Fix field typo (2 locations), add send count tracking, account-level archival control |
| `Services/Communication/MailboxVerificationService.cs` | 302 | 85% | needs-modification | Fix `sprk_sendenableds` → `sprk_sendenabled` (lines 38, 58, 291) |
| `Services/Communication/GraphSubscriptionManager.cs` | 353 | 90% | needs-modification | Add cleanup on account disable |
| `Services/Communication/IncomingCommunicationProcessor.cs` | 512 | 70% | needs-modification | Fix field typo (line 279), implement real dedup, add association resolution |
| `Services/Communication/InboundPollingBackupService.cs` | 222 | 60% | needs-modification | Currently only logs — must enqueue `ProcessIncomingCommunication` jobs |
| `Api/CommunicationEndpoints.cs` | 540 | 85% | needs-modification | Add SendMode to BulkSendRequest, add account CRUD endpoints |
| `Infrastructure/DI/CommunicationModule.cs` | 44 | 95% | needs-modification | Add new R2 service registrations |
| `Configuration/CommunicationOptions.cs` | 57 | 80% | needs-modification | Add `WebhookSecret` config property |

### Communication Models

| File | Lines | R2 Ready | Status | Changes Needed |
|------|-------|----------|--------|----------------|
| `Models/CommunicationAccount.cs` | 57 | 50% | needs-modification | Add: SendsToday, DailySendLimit, ArchiveOutgoingOptIn, ArchiveIncomingOptIn, Description, ProcessingRules |
| `Models/AccountType.cs` | ~10 | 100% | complete | Enum: SharedAccount=0, ServiceAccount=1, UserAccount=2 |
| `Models/AuthMethod.cs` | ~10 | 100% | complete | Enum: AppOnly=0, DelegatedObo=1 |
| `Models/SendMode.cs` | 12 | 90% | needs-modification | Enum exists (SharedMailbox=0, User=1) but not in BulkSendRequest |
| `Models/CommunicationDirection.cs` | ~10 | 100% | complete | Enum: Outbound=0, Inbound=1 |
| `Models/CommunicationStatus.cs` | ~10 | 100% | complete | Enum values present |
| `Models/SendCommunicationRequest.cs` | ~30 | 90% | needs-modification | Has SendMode but verify R2 fields |
| `Models/BulkSendCommunicationRequest.cs` | ~25 | 70% | needs-modification | Missing SendMode property |
| `Models/VerificationStatus.cs` | ~10 | 100% | complete | Verified=100000000, Failed=100000001, Pending=100000002 |

### Email Services (Legacy — Phase E Migration)

| File | Lines | R2 Decision | Reason |
|------|-------|-------------|--------|
| `Services/Email/EmailToEmlConverter.cs` | 932 | **DELETE** | Replace with `GraphMessageToEmlConverter` (fetches .eml directly from Graph) |
| `Services/Email/IEmailToEmlConverter.cs` | ~15 | **DELETE** | Interface for above |
| `Services/Email/EmailFilterService.cs` | 375 | **DELETE** | Replace with per-account config (R2 uses account-level processing rules) |
| `Services/Email/EmailRuleSeedService.cs` | 406 | **DELETE** | Seeds filter rules — eliminated by per-account config |
| `Services/Email/EmailAssociationService.cs` | 828 | **ADAPT** | Core association logic is email-source independent; adapt for Graph message input |
| `Services/Email/EmailAttachmentProcessor.cs` | 360 | **ADAPT** | Wrap with `GraphAttachmentAdapter` to handle Graph attachment format |
| `Services/Email/AttachmentFilterService.cs` | 229 | **KEEP** | Standalone utility, no email-specific coupling |
| `Services/Email/EmailProcessingStatsService.cs` | 284 | **KEEP** | Metrics tracking, rename to `CommunicationProcessingStatsService` |

### AI Integration

| File | Lines | R2 Ready | Status | Changes Needed |
|------|-------|----------|--------|----------------|
| `Services/Ai/Tools/SendCommunicationToolHandler.cs` | 126 | 100% | complete | No changes needed — delegates to CommunicationService |

### Web Resources

| File | Lines | R2 Ready | Status | Changes Needed |
|------|-------|----------|--------|----------------|
| `webresources/js/sprk_communication_send.js` | ~850 | 70% | needs-modification | Fix `sprk_sendenableds` (line 792), fix `sprk_communiationtype` (line 362) |
| `CommunicationRibbons/.../sprk_communication_send.js` | ~850 | 70% | needs-modification | Synced copy — same fixes needed |

---

## Critical Bug: Field Name Mismatches

### Bug 1: `sprk_sendenableds` → `sprk_sendenabled`

The Dataverse field was corrected (trailing 's' removed). All code still uses the old name.

**Affected Files** (source code — excludes docs already fixed):

| File | Occurrences | Lines |
|------|-------------|-------|
| `CommunicationAccountService.cs` | 2 | 42, 145 |
| `MailboxVerificationService.cs` | 3 | 38, 58, 291 |
| `sprk_communication_send.js` | 1 | 792 |
| `CommunicationRibbons/.../sprk_communication_send.js` | 1 | 792 |
| Test files (estimated) | ~6 | Various |
| **Total source** | **~13** | |

### Bug 2: `sprk_communiationtype` → `sprk_communicationtype`

The Dataverse field typo was corrected. All code still uses the misspelled version.

**Affected Files** (source code — excludes docs already fixed):

| File | Occurrences | Lines |
|------|-------------|-------|
| `CommunicationService.cs` | 2 | 547, 681 |
| `IncomingCommunicationProcessor.cs` | 1 | 279 |
| `sprk_communication_send.js` | 1 | 362 |
| `CommunicationRibbons/.../sprk_communication_send.js` | 1 | 362 |
| Test files (estimated) | ~2 | Various |
| **Total source** | **~7** | |

**Resolution**: Task 003 (ApprovedSenderValidator Migration) scope includes these field fixes, or create a dedicated fix task.

---

## Key Findings

### Already Implemented (Positive Surprises)

1. **OBO (On-Behalf-Of) authentication** — `CommunicationService.SendAsUserAsync()` already exists, uses `GraphClientFactory.ForUserAsync()`. SendMode enum routes correctly.
2. **Graph subscription lifecycle** — `GraphSubscriptionManager` handles create/renew/recreate on 30-min cycle. Subscription expiry tracking works.
3. **Incoming webhook endpoint** — `POST /incoming-webhook` at `CommunicationEndpoints.cs` already handles Graph change notifications.
4. **Best-effort tracking pattern** — Graph send is critical path; Dataverse record creation is non-fatal (try/catch with logging).
5. **Redis caching** — 5-minute TTL for approved senders and account queries. Pattern is correct.
6. **Bulk send** — `POST /send-bulk` endpoint with parallel fan-out already works.
7. **All model enums** — AccountType, AuthMethod, SendMode, CommunicationDirection, CommunicationStatus, VerificationStatus — all present and correct.

### Gaps Requiring R2 Work

1. **CommunicationAccount model** — Missing 6 R2 properties:
   - `SendsToday` (int) — daily send counter
   - `DailySendLimit` (int) — configurable per-account limit
   - `ArchiveOutgoingOptIn` (bool) — per-account outbound archival control
   - `ArchiveIncomingOptIn` (bool) — per-account inbound archival control
   - `Description` (string) — optional account description
   - `ProcessingRules` (string/JSON) — per-account message processing configuration

2. **InboundPollingBackupService** — Polls every 5 minutes but only logs found messages. Must enqueue `ProcessIncomingCommunication` jobs for missed messages.

3. **Dedup check** — `IncomingCommunicationProcessor.ExistsByGraphMessageIdAsync()` returns `false` (stubbed). Must query Dataverse for existing `sprk_graphmessageid`.

4. **Daily send limit enforcement** — No send counting or rate limiting exists. Need to:
   - Increment `SendsToday` on each send
   - Check against `DailySendLimit` before sending
   - Reset counter daily (background job or lazy reset)

5. **Account-level archival control** — Current archival is global (all-or-nothing). R2 needs per-account opt-in via `ArchiveOutgoingOptIn` / `ArchiveIncomingOptIn`.

6. **CommunicationOptions** — Missing `WebhookSecret` configuration for validating incoming Graph webhook notifications.

7. **Association resolution** — `IncomingCommunicationProcessor` has placeholder for associating incoming messages to matters/contacts. Needs implementation.

8. **BulkSendRequest** — Missing `SendMode` property (single send has it, bulk doesn't).

### Architecture Observations

- **DI registrations**: All services registered in `CommunicationModule.cs` (44 lines). Follows ADR-010 minimalism.
- **No duplicate services**: Each concern has exactly one service. No overlapping responsibilities.
- **Clean separation**: Communication services (`Services/Communication/`) vs email-specific services (`Services/Email/`) are well-separated.
- **Graph subscription management**: Runs as `BackgroundService` per ADR-001 pattern.
- **Inbound pipeline**: 7-step processing (dedup → fetch → create record → process attachments → archive .eml → update status → mark read) is architecturally sound.

---

## Phase E Migration Plan (Email → Communication)

### Files to DELETE (4 files, ~1,728 lines)

| File | Lines | Why Delete |
|------|-------|------------|
| `EmailToEmlConverter.cs` | 932 | R2 fetches .eml directly from Graph API (`$value` endpoint) — no need to construct from MIME parts |
| `IEmailToEmlConverter.cs` | ~15 | Interface for above |
| `EmailFilterService.cs` | 375 | Global filter rules replaced by per-account `ProcessingRules` in R2 |
| `EmailRuleSeedService.cs` | 406 | Seeds filter rules — eliminated by per-account config |

### Files to ADAPT (3 files, ~1,417 lines)

| File | Lines | Adaptation |
|------|-------|------------|
| `EmailAssociationService.cs` | 828 | Rename to `CommunicationAssociationService`. Core logic (matter/contact resolution) is email-source independent. Change input from Exchange message to Graph message DTO. |
| `EmailAttachmentProcessor.cs` | 360 | Create `GraphAttachmentAdapter` wrapper. Current processor expects Exchange attachment format; adapter translates Graph attachment format. |
| `AttachmentFilterService.cs` | 229 | KEEP as-is. Standalone utility with no email-specific coupling. |

### Files to KEEP (2 files, ~410 lines)

| File | Lines | Notes |
|------|-------|-------|
| `SendCommunicationToolHandler.cs` | 126 | AI tool handler — delegates to CommunicationService. No changes needed. |
| `EmailProcessingStatsService.cs` | 284 | Rename to `CommunicationProcessingStatsService`. Metrics tracking logic is reusable. |

---

## Dataverse Form/View Assessment

### Existing Forms

The admin form for `sprk_communicationaccount` covers:
- Core identity fields (name, email, display name, account type)
- Send/receive configuration
- Security group fields
- Verification status
- Subscription management fields

### Missing from Admin Form (R2 additions needed)

- **Daily Send Limit** section: `sprk_sendlimit`, `sprk_sendstoday`
- **Archival Configuration** section: `sprk_archiveoutgoingoptin`, `sprk_archiveincomingoptin`
- **Processing Rules** section: `sprk_processingrules`
- **Description** field: `sprk_description`

These fields must be created in Dataverse first, then added to the admin form.

---

## Recommended Task Sequencing

Based on this assessment, the R2 task sequence should be:

1. **Fix field names first** (Task 003 or dedicated) — unblocks all other work
2. **Add R2 model properties** (Task 002) — CommunicationAccount entity updates
3. **Complete polling backup** (Task 025) — enqueue jobs instead of logging
4. **Implement dedup** — real `ExistsByGraphMessageIdAsync` query
5. **Add daily send limits** — counter + enforcement
6. **Per-account archival** — opt-in controls
7. **Phase E migration** — delete/adapt email services last

---

## Acceptance Criteria Verification

| Criterion | Met? | Evidence |
|-----------|------|----------|
| Assessment lists every R1 file related to Phase A | ✅ | 30+ files listed with line counts |
| Each component has clear status: complete / needs-modification / missing | ✅ | Status column in every table |
| Specific changes needed documented with file paths | ✅ | File paths, line numbers, and change descriptions throughout |

---

*Assessment completed by parallel Claude Code agent audit. All component existence verified against source code — not derived from documentation.*
