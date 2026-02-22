# E2E Outbound Shared Mailbox Test Plan

> **Task**: 055 - Phase 6 Gate Test
> **Date**: 2026-02-22
> **Scope**: Outbound email via shared mailbox (mailbox-central@spaarke.com)

## Pre-requisites

- [ ] BFF API deployed to `spe-api-dev-67e2xz.azurewebsites.net`
- [ ] Dataverse `sprk_communicationaccount` record exists for `mailbox-central@spaarke.com` with:
  - `sprk_sendenableds = true`, `sprk_isdefaultsender = true`
  - `sprk_accounttype = 100000000` (SharedAccount)
- [ ] Exchange Application Access Policy configured for the BFF app registration
- [ ] Microsoft Graph `Mail.Send` application permission granted and admin-consented
- [ ] Redis cache accessible from API App Service

## Test Scenarios

### T1: Default Sender Resolution

**Action**: `POST /api/communications/send` with no `fromMailbox` field.
**Expected**: Email sends from `mailbox-central@spaarke.com`. Response contains `status: "Send"`, `from: "mailbox-central@spaarke.com"`.

### T2: Explicit Valid Sender

**Action**: `POST /api/communications/send` with `fromMailbox = "mailbox-central@spaarke.com"`.
**Expected**: 200 OK. Email sends successfully. `from` matches requested mailbox.

### T3: Invalid Sender Rejection

**Action**: `POST /api/communications/send` with `fromMailbox = "unauthorized@evil.com"`.
**Expected**: 400 Bad Request. Response body contains `code: "INVALID_SENDER"`, `detail` mentions the rejected address.

### T4: Dataverse Fallback

**Action**: Deactivate the `sprk_communicationaccount` record in Dataverse (set `statecode = 1`). Clear Redis cache. Send email with no `fromMailbox`.
**Expected**: Email still sends using `appsettings.json` config sender. Re-activate record after test.

### T5: sprk_communication Record Created

**Action**: After T1, query Dataverse for the `sprk_communication` record by `sprk_correlationid`.
**Expected**: Record exists with:
- `sprk_from = "mailbox-central@spaarke.com"`
- `sprk_to` contains recipient address
- `sprk_subject` matches request subject
- `statuscode = 659490002` (Send)
- `sprk_direction = 100000001` (Outgoing)

### T6: Redis Cache Behavior

**Action**: Send two emails in quick succession (< 5s apart). Check API logs.
**Expected**: First request logs Dataverse query for communication accounts. Second request logs "from cache" for account resolution (5-minute TTL).

## Status

| Layer | Status | Notes |
|-------|--------|-------|
| Automated integration tests (in-memory) | PASS | 4 tests in `CommunicationIntegrationTests` Phase 6 region |
| Manual E2E against deployed environment | PENDING | Requires API deployment with Phase 6 code |

## Automated Test Coverage

The following integration tests validate the Phase 6 CommunicationAccountService flow with mocked infrastructure:

1. `CommunicationAccountService_QuerySendEnabledAccounts_ResolvesViaValidator` -- default sender resolution via Dataverse
2. `CommunicationAccountService_FallbackToConfig_WhenDataverseUnavailable` -- graceful fallback to config
3. `CommunicationAccountService_InvalidSender_RejectsUnapprovedMailbox` -- INVALID_SENDER error for unapproved address
4. `CommunicationAccountService_DataverseOverridesConfig_ForSameEmail` -- Dataverse DisplayName wins on merge
