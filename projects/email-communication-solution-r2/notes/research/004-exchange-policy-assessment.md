# ECS-004: Exchange Access Policy Setup — Assessment

> **Date**: 2026-03-09
> **Task**: ECS-004 — Document Exchange Access Policy Setup
> **File Assessed**: `docs/guides/COMMUNICATION-DEPLOYMENT-GUIDE.md` (lines 488-615)

---

## Summary

The deployment guide already contains a **complete and well-structured** Exchange Online Application Access Policy section. All acceptance criteria are met. No changes are required.

---

## Section Coverage Assessment

### 1. Security Group Creation — PASS

- **Lines 509-519**: Creates mail-enabled security group named "SDAP Mailbox Access" (correct name per corrections).
- Uses `New-DistributionGroup -Name "SDAP Mailbox Access" -Type Security`.
- Includes idempotency check: "If the group already exists, skip this step" with `Get-DistributionGroup` verification command.

### 2. Application Access Policy Creation — PASS

- **Lines 540-552**: Creates the restrictive policy with `New-ApplicationAccessPolicy`.
- Uses parameterized placeholder `{API_APP_ID}` instead of hardcoded values.
- Explicit instruction: "Replace `{API_APP_ID}` with the actual Client ID of the BFF API app registration."
- Policy scope references the security group by name ("SDAP Mailbox Access"), not by hardcoded GUID.

### 3. Verification Commands — PASS

- **Lines 556-564**: Two `Test-ApplicationAccessPolicy` commands provided:
  - Positive test: mailbox in the group returns "Granted".
  - Negative test: random mailbox returns "Denied".
- Both use `{API_APP_ID}` placeholder consistently.

### 4. Adding New Mailboxes to Existing Group — PASS

- **Lines 572-594**: Complete "Adding New Mailboxes" subsection with a 4-step procedure:
  1. Add mailbox to Exchange security group (`Add-DistributionGroupMember`).
  2. Create `sprk_communicationaccount` record in Dataverse.
  3. Update `Communication__ApprovedSenders` in App Service config (with `az webapp config appsettings set`).
  4. Wait 30 minutes for Exchange policy propagation.
- Uses `N` as a placeholder for the next array index.

### 5. Multi-Tenant Readiness (No Hardcoded IDs) — PASS

- No hardcoded tenant IDs, app IDs, or GUIDs anywhere in the section.
- All identity values use parameterized placeholders: `{API_APP_ID}`.
- Security group referenced by display name, not GUID.
- Mailbox addresses are example values (`mailbox-central@spaarke.com`) consistent with the rest of the guide.

### 6. Propagation Warning — PASS (Bonus)

- **Lines 566-570**: Explicit warning about 30-minute propagation delay, with guidance to re-run `Test-ApplicationAccessPolicy` to verify.

### 7. Graph API Permissions by Phase — PASS (Bonus)

- **Lines 596-608**: Clear table mapping permissions to phases (Mail.Send Application for Phase 1-6, Mail.Read Application for Phase 8, Mail.Send Delegated for Phase 7).
- Explains that Phase 7 delegated auth does NOT require an Application Access Policy.

### 8. Disconnect Instructions — PASS (Bonus)

- **Line 612**: `Disconnect-ExchangeOnline -Confirm:$false` cleanup step.

---

## Acceptance Criteria Checklist

| Criterion | Status | Notes |
|-----------|--------|-------|
| Parameterized PowerShell commands (no hardcoded IDs) | PASS | `{API_APP_ID}` used consistently |
| Security group creation | PASS | "SDAP Mailbox Access" with idempotency check |
| Application access policy creation | PASS | `New-ApplicationAccessPolicy` with RestrictAccess |
| Verification commands | PASS | Positive + negative `Test-ApplicationAccessPolicy` |
| Adding new mailboxes to existing group | PASS | 4-step procedure covering Exchange, Dataverse, and App Service |
| Multi-tenant readiness | PASS | No hardcoded tenant/app IDs |
| Field name `sprk_sendenabled` (not `sprk_sendenableds`) | PASS | Correct at line 757 |
| Security group name "SDAP Mailbox Access" | PASS | Correct throughout (lines 514, 519, 525, 528, 537, 548, 577) |

---

## Recommended Changes

**None.** The existing section is comprehensive, correctly parameterized, and covers all required scenarios including the day-2 operation of adding new mailboxes. The corrections (security group name, field name) have already been applied.

---

## Conclusion

Task ECS-004 acceptance criteria are fully satisfied by the existing deployment guide content at lines 488-615. No modifications to `COMMUNICATION-DEPLOYMENT-GUIDE.md` are needed.
