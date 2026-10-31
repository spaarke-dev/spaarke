# R3 issue triage before wrap-up (2026-10-22)

This is a read-only check of the unscheduled entries in `defer-issues.md`, made against origin/master `aa3dc49897`. Evidence (file:line, GitHub state) comes from a code-mapper pass; anything marked *inferred* is an owner or project assignment taken from file paths. All the GitHub issues are open. The proposed dispositions need the owner's OK before any change or hand-off.

| ID | GH | Still present at master? | Proposed | Size | Note |
|---|---|---|---|---|---|
| ISS-001 | #1623 | Yes: process-wide once-flag, `AuthorizationModule.cs:38`, `:120` | **Fix in R3** | S–M; auth, so two top-tier reviews | Make the Copilot audience merge per options instance; don't double-wrap `Events`. The file is shared with UAC, so tell UAC first. |
| ISS-004 | #1650 | Yes: internal DELETE uses `BestEffort`, `DocumentCheckoutService.cs:760`, `:849-880` | **Fix in R3, needs owner yes** | S | Switch to `VerifiedFileFirstKeepRowOnFailure` (already used by the external route). This changes internal behaviour: a failed file delete keeps the row and answers 503 instead of orphaning the file. |
| ISS-005 | #1673 | Partly: the WorkforceHome create has an owner team (`ContactIdentityStore.cs:793-809`); the licensed-user link create does not (`ContactIdentityBinder.cs:1446`) | **Fix in R3, needs owner decision** | M; tenant-isolation | The owner decides which BU or team owns a licensed user's contact. Whether `:1446` is reachable in prod is still undetermined. |
| ISS-011 | #1886 | Yes: no `hasFile` in the external DTO or the SPA | **Fix in R3** | S–M | Owner already decided (2026-10-14): show the row, hide download and versions when there is no file. BFF + SPA; it pairs with the R4 record modal. |
| ISS-012 | #1930 | Yes: test host doesn't clear logging providers, `AuthorizationModuleWorkforceHomeTests.cs:541` | **Fix in R3** | S | Test-only: `ClearProviders()` and filter `host.Failures` to auth exceptions. |
| ISS-003 | #1636 | Yes: `sanitizeEmailHtml.ts` allows `img` (`:131`) and `style` (`:154`); no position or remote-image filter | **Hand off**: shared UI / email-communication owner | M; security/privacy | Overlay phishing and read-tracking across 4 consumers. R3 contains it locally (`RecordMessages.tsx:189-199`). |
| ISS-006 | #1830 | Yes: generic catch logs Error, `SpeContainerMembershipSync.cs:371-376` | **Hand off**: unified-access-control-r2 | S–M | The dev noise: "Invalid hostname" every 5 min, about 288 a day. |
| ISS-007 | #1839 | Yes: `DocumentIndexParentResolver.cs:67-71`, `:362-367` | **Hand off**: search / AI-platform owner (*inferred*; none active) | M + backfill | A findability gap, not an exposure (search fails closed). |
| ISS-013 | #2001 | Yes: no `removeUnverifiedEmailClaim` anywhere | **Hand off (already)**: customer-provisioning-orchestration-r1 task 264 | hours | R3 runs a regression check once it lands. |
| ISS-002 | #1633 | Undetermined: failure never captured | **Hand off**: CI / test-infra (*inferred*) | M | Flaky under full-suite load. |
| ISS-008 | #1840 | Yes: dead `sprk_communication_send` web resource | **Keep deferred** (or small cleanup) | S | Ribbon retired; `sprk_fieldmapping_push.js:659`, `:780-785` still reference it, so check before deleting. |
| DEF-001 | #1736 | Not built | **Keep deferred** | — | New project (NDA template + e-signature). |
| DEF-002 | #1737 | Not built | **Keep deferred** | 1–2 days | Blocked on the per-customer mailbox (#1562). |

**Already with others (status only):**
- ISS-010, ISS-014 and ISS-016 → uac-r2 task 191 (#2179 reopened). ISS-016 is partly addressed by #2170 (`RetrievalAccessTrim.cs:317-325`); the `ChatDocumentEndpoints` communication path is unguarded.
- ISS-017, ISS-019 and DEF-003 → R4.
- DEF-004 → ebilling-r1 task 051.

**Proposed R3 fix batch** (before 090): ISS-011, ISS-012 and ISS-001 now. Add ISS-004 and ISS-005 once the owner decides. One BFF deploy for the batch, plus an SPA deploy for ISS-011.
