# E2E Individual Send (User Mode) Test Plan

> **Task**: 064 - Phase 7 Gate Test
> **Date**: 2026-02-22
> **Scope**: Outbound email via individual user send (OBO) alongside shared mailbox regression

## Pre-requisites

- [ ] BFF API deployed to `spe-api-dev-67e2xz.azurewebsites.net`
- [ ] Phase 7 code deployed (SendMode branching in CommunicationService, OBO flow)
- [ ] User has valid Azure AD session with `preferred_username` and `oid` claims
- [ ] Microsoft Graph `Mail.Send` delegated permission granted and admin-consented for OBO
- [ ] Azure AD app registration configured for On-Behalf-Of (OBO) token exchange
- [ ] Exchange Online mailbox exists for the test user
- [ ] Shared mailbox (`mailbox-central@spaarke.com`) still configured per Phase 6

## Test Scenarios

### T1: Shared Mailbox Send (Regression)

**Action**: `POST /api/communications/send` with `sendMode: "sharedMailbox"` (or omit sendMode for default).
**Expected**: Email sends from `mailbox-central@spaarke.com`. Response contains `status: "Send"`, `from: "mailbox-central@spaarke.com"`.

**sprk_communication record**:
| Field | Expected Value |
|-------|---------------|
| `sprk_from` | `mailbox-central@spaarke.com` |
| `sprk_sentby` | (not set) |
| `statuscode` | `659490002` (Send) |
| `sprk_direction` | `100000001` (Outgoing) |
| `sprk_communiationtype` | `100000000` (Email) |

### T2: User Mode Send (OBO)

**Action**: `POST /api/communications/send` with `sendMode: "user"`. User is authenticated with a valid bearer token.
**Expected**: 200 OK. Email sends from user's own mailbox via `/me/sendMail`. Response contains `from: "<user-email>"`.

**sprk_communication record**:
| Field | Expected Value |
|-------|---------------|
| `sprk_from` | User's email (from `preferred_username` claim) |
| `sprk_sentby` | User's Azure AD object ID (from `oid` claim) |
| `statuscode` | `659490002` (Send) |
| `sprk_direction` | `100000001` (Outgoing) |
| `sprk_communiationtype` | `100000000` (Email) |

### T3: User Mode Without Authentication Context

**Action**: `POST /api/communications/send` with `sendMode: "user"` but no valid HttpContext (unauthenticated request or null context).
**Expected**: 400 Bad Request. Response body contains `code: "OBO_CONTEXT_REQUIRED"`, `detail` mentions HttpContext.

### T4: User Mode Without Email Claim

**Action**: `POST /api/communications/send` with `sendMode: "user"`. User is authenticated but token lacks `email`, `preferred_username`, and `ClaimTypes.Email` claims.
**Expected**: 400 Bad Request. Response body contains `code: "USER_EMAIL_NOT_FOUND"`, `detail` mentions email claim.

### T5: OBO Token Exchange Failure

**Action**: `POST /api/communications/send` with `sendMode: "user"`. User's session has expired or OBO token exchange fails.
**Expected**: Error response with `code: "GRAPH_SEND_FAILED"`, `detail` contains "OBO" context. Status code 500 or 502.

### T6: User Mode with Associations

**Action**: `POST /api/communications/send` with `sendMode: "user"` and `associations` array containing a `sprk_matter` reference.
**Expected**: Email sends successfully. Dataverse record has both user-specific fields AND association fields:
- `sprk_from` = user email
- `sprk_sentby` = user oid
- `sprk_regardingmatter` = EntityReference to the matter
- `sprk_associationcount` = 1

### T7: Shared Mailbox with Associations (Regression)

**Action**: `POST /api/communications/send` with `sendMode: "sharedMailbox"`, associations, CC recipients.
**Expected**: All Phase 2-6 features still work. Dataverse record has `sprk_from` = shared mailbox, associations correctly mapped, CC field populated.

### T8: Default SendMode is SharedMailbox

**Action**: `POST /api/communications/send` with NO `sendMode` field in the JSON body.
**Expected**: Defaults to SharedMailbox behavior. Email sends via `ForApp()`. `sprk_from` = shared mailbox email.

## Expected sprk_communication Record Field Values

### Shared Mailbox Mode (SendMode = 0)

| Field | Type | Value |
|-------|------|-------|
| `sprk_from` | string | Approved sender email from config/Dataverse |
| `sprk_sentby` | string | (not set) |
| `sprk_communiationtype` | OptionSetValue | `100000000` (Email) |
| `statuscode` | OptionSetValue | `659490002` (Send) |
| `statecode` | OptionSetValue | `0` (Active) |
| `sprk_direction` | OptionSetValue | `100000001` (Outgoing) |
| `sprk_bodyformat` | OptionSetValue | `100000001` (HTML) |

### User Mode (SendMode = 1)

| Field | Type | Value |
|-------|------|-------|
| `sprk_from` | string | User's email from `preferred_username` claim |
| `sprk_sentby` | string | User's Azure AD `oid` claim |
| `sprk_communiationtype` | OptionSetValue | `100000000` (Email) |
| `statuscode` | OptionSetValue | `659490002` (Send) |
| `statecode` | OptionSetValue | `0` (Active) |
| `sprk_direction` | OptionSetValue | `100000001` (Outgoing) |
| `sprk_bodyformat` | OptionSetValue | `100000001` (HTML) |

## OBO Error Scenarios

| Scenario | Error Code | Status | Detail |
|----------|-----------|--------|--------|
| No HttpContext provided | `OBO_CONTEXT_REQUIRED` | 400 | HttpContext is required for User send mode |
| No email claim in token | `USER_EMAIL_NOT_FOUND` | 400 | Token must include email or preferred_username |
| OBO token exchange failure | `GRAPH_SEND_FAILED` | 500/502 | OBO token acquisition failed |
| Graph sendMail returns 403 | `GRAPH_SEND_FAILED` | 403/502 | Graph API error (OBO) |
| User mailbox not found | `GRAPH_SEND_FAILED` | 404/502 | Graph cannot resolve /me for user |

## Regression Verification

These items verify that Phase 7 changes did not break existing shared mailbox functionality:

- [ ] Shared mailbox send still uses `ForApp()` (app-only auth)
- [ ] Approved sender validation still runs for shared mailbox mode
- [ ] Approved sender validation is skipped for user mode
- [ ] Default `SendMode` is `SharedMailbox` (value 0)
- [ ] Associations, CC/BCC, and correlation IDs work in both modes
- [ ] Dataverse record creation is best-effort in both modes
- [ ] Graph failure handling works in both modes (GRAPH_SEND_FAILED)

## Status

| Layer | Status | Notes |
|-------|--------|-------|
| Unit tests (CommunicationServiceTests) | PASS | 8 tests in Phase 7 region |
| Integration tests (CommunicationIntegrationTests) | PASS | 5 tests in Phase 7 E2E region |
| Manual E2E against deployed environment | PENDING | Requires API deployment with Phase 7 code |

## Automated Test Coverage

The following integration tests validate the Phase 7 individual send flow with mocked infrastructure:

1. `SharedMailbox_Send_CreatesRecord_WithSharedMailboxFrom` -- shared mailbox E2E: verifies ForApp path, sprk_from = shared email, no sprk_sentby
2. `UserMode_Send_CreatesRecord_WithUserFrom` -- user mode E2E: verifies ForUserAsync path, sprk_from = user email, sprk_sentby = user oid
3. `UserMode_Send_WithoutHttpContext_ReturnsError` -- missing HttpContext produces OBO_CONTEXT_REQUIRED
4. `SharedMailbox_Regression_AttachmentsStillWork` -- regression: shared mailbox with associations and CC still works
5. `OboTokenError_ReturnsGracefulError` -- OBO token failure wrapped as GRAPH_SEND_FAILED with OBO context in detail
