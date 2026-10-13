# Task 126 — external-flag script on a Model 1 stamp (2026-10-13)

## Decision

The owner picked option (a) on 2026-10-13. Their words: "yes it is possible that a system user with power app license outside the customer's environment". So a licensed guest who is not the customer's staff exists and must be flagged external. The two alternatives fail that case:

- (c), "every guest is internal", would leave that guest internal.
- (b), a recorded set, would need upkeep for every new guest.

Rule (`scripts/Set-ExternalFlagForB2BGuests.ps1 -CustomerTenantIds`):

- **Domain lookup.** Each guest's email domain is resolved to a home directory with Graph `findTenantInformationByDomainName`. The domain comes from two places: the `#EXT#` user name, split at the last `_`, and `internalemailaddress`. The result is compared with the stamp's `WorkforceIdentity__CustomerTenantIds__N`.
- **Customer staff.** A guest of a listed directory is never written. If one is already set to Yes, it is reported for review.
- **Other directory.** A guest of another directory that is not already Yes is set to Yes. Only a stored Yes counts as a decision. No is the column's default (`AppDefaultValue 0`), so a guest invited after the column existed carries No without anyone choosing it. This was review finding F1: the first version treated No as an administrator's choice, so new outsiders were never flagged and `-Verify` still passed.
- **Unresolved.** A guest is reported for review and never written when there is no address, the lookup fails, or the two addresses resolve to different directories. Already Yes: kept. A review row is settled in one of two ways:
  - an outsider: set Yes by hand;
  - customer staff: add their `systemuserid` to `-InternalUserIds` and pass that list on every run. Setting No does not settle it, because No cannot be told apart from the default.
- **Guards.** A listed id that is the stamp's own directory (the Dataverse token's `tid`) or the empty GUID is refused. `-Apply` is refused when there are guests to set but none resolves to the customer's directories. This catches only an id that matches no guest, so check the ids against the stamp BFF's settings before `-Apply`. Each PATCH carries the row's ETag, so an administrator's change made after the listing gives 412 instead of being overwritten.
- **Exit code.** `-Verify` and `-Apply` exit 0 only when nothing is left to set or to review.
- **One mode required.** `-AllGuestsExternal` keeps the task 114 behaviour for stamps where every guest is an outsider.

## ADR check (ADR-028 A8/A9, `.claude/constraints/auth.md` Model 1 block)

**§6.5 path pending the owner.** `auth.md:69` says workforce membership is keyed on `CustomerTenantIds`, "**never** on … an email domain or a `#EXT#` UPN". The script decides whether a licensed guest belongs to the customer by looking up the directory of their email domain. My first reading was "Path C, comply", on the grounds that the rule governs the BFF's run-time classification of workforce contacts. The second review (opus) pointed out that the rule's text is not limited to run time, so calling it compliance narrowed a MUST on my own reading. That is the silent path §6.5 forbids. Raised with the owner on 2026-10-13. The options:

- **Path B, recommended:** clarify the rule's scope in `auth.md:69`. The rule's purpose is that the BFF never grants membership from a spoofable claim at sign-in. This script is different:
  - it is an operator data step on `systemuser.sprk_isexternal`, dry-run and reviewed by the owner;
  - it compares home-directory ids, using the domain only as the lookup input;
  - it never writes a guest it cannot place;
  - the owner chose this exact method (option (a)).

  Clarifying the wording once also stops the next reader from hitting the same ambiguity.
- **Path A:** keep the rule's wording and record a project exception in this project's ADR Tensions table.

A9: workforce contacts are not guests, so they never reach this script.

## D-13 comment (`WorkforceIdentityOptions.cs`)

Not edited here. The comment still describes R3's FR-15 branch, which treats a registered workforce-contact guest as a member. A9 withdraws that branch: R3 coordination note `2026-10-13-model1-workforce-signin-cross-project.md`, item 6 and the file list at line 56. R3 owns that file and is rewriting its code under A9; editing the comment here would collide with that work. The task's acceptance criterion for D-13 is met by R3's plan, not by an edit here.

## Known limits (K-class)

- **Home directory comes from the email domain.** Dataverse holds no home-directory id for a guest. Sign-in logs carry `homeTenantId`, but they need AuditLog permission and a recent sign-in. Two consequences:
  - Customer staff whose address is on a domain verified in another directory would be proposed for "set Yes". The dry-run review catches this, and those users go in `-InternalUserIds`.
  - A customer staffer cannot be kept external on purpose: the review row stays until the flag goes back to No. Customer staff are internal by definition on Model 1 (A8).
  - Consumer addresses (gmail, hotmail) resolve to the Microsoft account directory, so they count as "other directory" and are set to Yes. That is correct for outsiders.
- The lookup uses the operator's `az login` Graph token. No new app permission is needed.

## Evidence

- Pester 3.4: `tests/scripts/Set-ExternalFlagForB2BGuests.Tests.ps1` passes 14/14.
  - The first version passed 7/7, and 0/7 against the task-114 script (`git show HEAD:`).
  - Each review fix was mutation-checked: putting the defect back fails at least one test. The defects re-introduced were: No treated as a decision, `If-Match: *`, no stamp-directory guard, and no `-Apply` refusal.
- **Re-check (sonnet):** a repeat `-Apply` with nothing to write was refused (fixed: refuse only when there is something to write). A token without `tid` skipped the guard (it now throws).
- **Second full pass (opus):** found two problems, both fixed:
  - the report wrote `[""]` for an empty id list (fixed, with a test assertion);
  - the ADR reading (moved to the owner, above).

  It also found two known limits, now documented:
  - the guard catches only an id that matches no guest. Another real directory with guests on the stamp passes it, so the guide says to check the ids against the stamp BFF's settings before `-Apply`;
  - `-Verify` cannot detect a wrong id once every guest is Yes. The report now records `noCustomerMatch`.
- **First independent review (adversarial-reviewer, opus)** found F1 (No is the default), a docs error on settling review rows, the wrong-id guard, the ETag, and test gaps (paging, dry run writes nothing, lowercase `#ext#`). All are fixed above.
- Dev dry run on spaarkedev1 with `-CustomerTenantIds bc3aa7f4-…` (deweycheatham), 0 to set and 0 to review:

  | Guest | Class | Action |
  |---|---|---|
  | Ralph Dewey | customer | none |
  | Eyal Iffergan | other | already Yes |
  | Ralph Schroeder (hotmail) | other | already Yes |

  Nothing was applied.
