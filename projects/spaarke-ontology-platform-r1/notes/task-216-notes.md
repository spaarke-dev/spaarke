# Task 216 notes - own-mailbox send honours ArchiveOutgoingOptIn (#2672, DI-149)

## Findings (step 1)
- `CommunicationService.SendAsUserAsync` (Step 6 "Archive to SPE") read the sender account's `ArchiveOutgoingOptIn` only under
  `if (archive && request.FromMailbox is not null)`. A send as user without `FromMailbox` (the user's own mailbox) never read it, so it always archived.
- The shared-mailbox/app-only path resolves the account from `senderResult.Email` whenever `archive` is true, defaults true on no row, and on a
  read failure keeps true with a warning log. Both paths set the same `archivalWarning` text.
- `CommunicationAccountService.GetSendAccountByEmailAsync` is cache-first and swallows cache/Dataverse faults (returns no accounts), so a read
  failure surfaces as "no row" and archives. Only send-enabled, active accounts are returned: a user with no send-enabled account row archives.
- The send in user mode goes out as `userEmail` (`ResolveUserEmail(httpContext.User)`, from the token claims); `request.FromMailbox` is NOT used
  by the send itself in user mode (`FromAddress = userEmail`).

## Change
`optInMailbox = request.FromMailbox ?? userEmail`; the lookup now runs whenever `archive` is true. The own-mailbox address is the token-claim
address, never a request value. Default true, failure-default true and the warning text are the existing ones. Cost: one extra (cache-first)
account read per send-as-user, only when the send will be archived.

## Tests (OutgoingFiledEmailArchiveTests)
Must-fire (fail on origin/master, 2 of 2): own mailbox + opted out + filed -> not archived with the app-only warning; same with `ArchiveToSpe=true`.
Must-not-fire (pass on master and after): opted in while another account is opted out; no account row; account read fails; record-less send
with the switch off gets no warning. Harness `Build` gained `accountAddresses` and `accountReadFails`.
Before: 2 failed / 44 passed in the class. After: Communication filter 3493 passed, 0 failed, 2 skipped.

## Task 177 default
Task 177 notes: a filed send archives by default; the switch only subtracts. Not inverted: no row and true both archive.

## Defect found, not fixed (out of scope; the POML says FromMailbox sends are unchanged)
In user mode, when the request names `FromMailbox`, the opt-out is read from THAT (client-supplied) address's account, although the send goes out
as the signed-in user. A caller can name an account that is opted in (or out) and so choose whether the archive runs, independent of their own
switch. Failure scenario: user opted out sends with `FromMailbox` = an opted-in mailbox -> the email is archived against their choice.
Suggested fix: always read the switch from `userEmail` in user mode (a behaviour change for callers that pass FromMailbox: needs owner decision).

rework: none task 216

## Verification
- `dotnet test ... --filter "FullyQualifiedName~Communication"`: 3493 passed, 0 failed, 2 skipped. Before the fix, 2 of the new tests failed on master code.
- `tests/Spaarke.ArchTests` (full, built first): 2793 passed, 0 failed.
- BFF build: 0 warnings, 0 errors.
- Publish size (`dotnet publish -c Release -r linux-x64 --self-contained false`, fresh short-path worktrees, `Compress-Archive -CompressionLevel Optimal`
  over `deploy/api-publish/*`, PDBs included): master 43,578,743 bytes, branch 43,578,831 bytes, delta +88 bytes; 194 files on both sides.
- No package added or changed, so no new CVE. Self-review (code-review + adr-check): no F-class finding; the unfixed defect above is filed in the report.

## Section 11 answers
No new service, interface, endpoint, DI registration, package or column; this edits an existing branch condition in `CommunicationService`.
