# Task 214 notes: Sent Items lookup no longer 400s (#2774, DI-150)

Branch `work/ontology-214-sent-items-lookup` from `origin/master` b005279c4d. Rigor FULL, Sonnet @ high, directional steps.

## Findings (step 1)

- `EmailChannelSender.TryCaptureInternetMessageIdAsync` read `mailFolders/sentitems/messages` with
  `$filter=subject eq '...'` and `$orderby=sentDateTime desc` in both the `Me` (user/OBO) and `Users[sender]` (app) branches.
- Graph's documented rule (Microsoft Learn, "List messages",
  https://learn.microsoft.com/graph/api/user-list-messages, section "Using filter and orderby in the same query"):
  1. properties in `$orderby` must also appear in `$filter`;
  2. they appear in the same order as in `$filter`;
  3. they appear in `$filter` before any property that is not in `$orderby`.
  Breaking it returns `InefficientFilter` / "The restriction or sort order is too complex for this operation." (HTTP 400).
  `subject eq ...` alone in the filter breaks rule 1, so the lookup could not succeed for any send.
- The "retry/delay for a message not yet in Sent Items" the POML mentions does not exist in this method on master: it reads
  once and returns null on not-found or any error. Nothing was added or removed there (out of scope).
- The consumer (`CommunicationService`) already stores a non-empty `ProviderMessageId` (`sprk_acsmessageid`, echo dedup, and
  the thread-continuity stamp); nothing downstream changed.

## Query shape chosen

`$filter=sentDateTime ge {send start - 2 min, UTC, yyyy-MM-ddTHH:mm:ssZ} and subject eq '{subject, quotes doubled}'`,
`$orderby=sentDateTime desc`, `$top=3`, `$select=internetMessageId,sentDateTime` (the last three unchanged).

Why this option and not "drop `$orderby`, sort client-side": it keeps the server ordering (newest first), keeps the read to
a handful of rows, and bounds the match to this send. The same filter shape is already used by
`InboundPollingBackupService` (`receivedDateTime ge ... and isRead eq false` with `$orderby=receivedDateTime asc`).

- The send start is captured once in `SendAsync` just before `sendMail`; the lower bound is `UtcNow - 2 minutes`
  (`SentItemsLookupMargin`) so a small BFF/Exchange clock difference cannot push the just-sent message below the bound.
- Subject escaping is unchanged (`'` doubled), now inside `BuildSentItemsFilter`.
- Not-found (empty page) still logs the same warning and returns null; any Graph or transport error is still swallowed and
  returns null, so nothing new escapes the send.

## Tests (step 2)

`tests/unit/domain/Communication/EmailChannelSenderSentItemsLookupTests.cs` (compiled into `Sprk.Bff.Api.Tests`).
The fake Graph transport answers 400 `InefficientFilter` to a query that breaks the Learn rule, so the assertion is on the
request the fake receives. Cases x both branches: filter/orderby shape + a found message's Internet-Message-Id returned;
subject escaping with the date bound first; not-found returns null; a Graph error is swallowed; a system-mail send still
skips the lookup. Plus a control theory proving the fake accepts a compliant query and refuses the old one.

- On master source (tests added, fix not applied): 15 tests, **6 failed**, 9 passed (the 9 are must-not-fire controls and
  the not-found/error/system-mail paths that already behaved correctly).
- With the fix: all 15 pass.

## Known limits (K-class, no fix round)

- K4: if Exchange has not yet stored the sent message when the single read runs, the read can return an earlier message
  with the same subject sent within the last 2 minutes, and its id is returned. This is the pre-existing behaviour (before
  this task it could return any older same-subject message, when the read worked at all); the window is now bounded to
  2 minutes. No retry exists on master and none was added.

## Fix-the-class grep (Step 9.5 rule 6)

`Orderby =` across `src/server`: `EmailChannelSender` (fixed, 2 places), `InboundPollingBackupService` (already compliant:
`receivedDateTime` leads the filter), `SpeAdminGraphService` security alerts (no `$filter`, not a messages query). No other match.

## Verification (steps 4)

- BFF build and `Sprk.Bff.Api.Tests --filter "FullyQualifiedName~Communication|FullyQualifiedName~EmailChannelSender"`:
  3502 passed, 0 failed, 2 skipped (includes the 15 new tests).
- Full `tests/Spaarke.ArchTests` (built explicitly first): 2793 passed, 0 failed.
- `dotnet list package --vulnerable --include-transitive` on Sprk.Bff.Api: no vulnerable packages; no package added.
- Publish size (BFF hygiene, fresh short-path worktrees `C:wm214` = master b005279c4d and `C:wb214` = branch 589639fbbf, both
  deleted with `Remove-WorktreeSafe.ps1`). Command `dotnet publish -c Release -o deploy/api-publish` (framework-dependent
  linux-x64 per the csproj), zipped with PowerShell `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`,
  PDBs included:
  | | files | raw bytes | zip bytes |
  |---|---|---|---|
  | master | 194 | 144,440,707 | 43,578,755 (41.56 MiB) |
  | branch | 194 | 144,441,327 | 43,578,336 (41.56 MiB) |
  | delta | 0 | +620 | -419 (compression noise) |

## Acceptance

See the PR for the build, filtered test counts, ArchTests and publish-size delta. The dev confirmation (the sentitems GET no
longer returns 400 after a send) is the main session's, after a deploy.
