# Task 212 notes — the two remaining sendMail callers do not retry on 5xx/408 (#2779, DI-153)

Branch `work/ontology-212-sendmail-no-retry` from origin/master `b005279c4d`. PR closes #2779.

## Findings (step 1)

`git grep "SendMail.PostAsync" -- src` on master listed four calls in three classes. Only `EmailChannelSender` (task 191)
passed `NoTransientRetryOption`; the other two used the standard Graph retry (`GraphHttpMessageHandler`: 5xx, 408, 429, three
retries, so up to four sends):

| Caller | Call | Failure contract (unchanged) |
|---|---|---|
| `Services/Ai/Nodes/SendEmailNodeExecutor.cs` (`Me.SendMail`, OBO) | no option | returns `NodeOutput.Error(..., "Failed to send email: <msg>", NodeErrorCodes.InternalError)` from a `ServiceException` / `Exception` catch |
| `Services/Communication/MailboxVerificationService.cs` (`Users[email].SendMail`, app-only) | no option | `TestSendCapabilityAsync` returns `(false, "Send test failed: ...")`; `VerifyAsync` sets `Status = Failed`, `SendCapabilityVerified = false`, `FailureReason` |

`EmailChannelSender` is untouched. `GraphHttpMessageHandler` defaults are untouched (the escalation trigger did not fire: no
handler change, no package, no result-shape change).

## Change (step 3)

- `SendEmailNodeExecutor`: one `NoTransientRetryOption` per execution, passed through `rc => rc.Options.Add(sendOption)`. The two
  catch blocks read the option (`CloseAndReadOutcomeUnknown()`, plus an `ApiException` 5xx/408 fallback, the same rule as
  `EmailChannelSender.OutcomeOf`). When the outcome is unknown the failure text gains `(the message may have been delivered)`;
  the message still starts `Failed to send email:` and the code is still `InternalError`. The result shape (`NodeOutput.Error`) is
  unchanged. Not-sent failures (4xx, nothing left) keep the exact old text.
- `MailboxVerificationService`: `rc => rc.Options.Add(new NoTransientRetryOption())` on the verification send. Its existing
  `(false, "Send test failed: ...")` path is unchanged; a 5xx now fails the verification once instead of after four sends.
  The outcome is not classified: a verification message that may have been delivered is harmless and the account is simply
  re-verified.

## What a caller now surfaces that retry used to hide

A transient 5xx/408 on a send used to be absorbed when one of the later attempts got a 2xx (the message then arrived once or up
to four times, depending on whether the earlier attempts had already been accepted). Now the first 5xx/408 is the final answer:
the playbook node reports a failed node (with the "may have been delivered" note) and a verification reports `Failed` for that
run. That is the intended trade (a lost-answer 5xx can no longer duplicate mail); the operator re-runs the node or the
verification. No other behaviour changes.

## Class check (Step 9.5 rule 6)

`git grep -n "SendMail.PostAsync" -- src` after the change lists four calls: `EmailChannelSender` (two, task 191) and the node
and the verification (this task); all four pass `NoTransientRetryOption`. No other BFF code calls Graph `sendMail`
(`EmailDispositionSender`, `ApExportDeliveryService`, `AssignTaskNotifier` all go through `CommunicationService` →
`EmailChannelSender`).

## Other non-idempotent Graph POSTs still on the standard retry (not changed; reported)

Seen by `git grep "\.PostAsync("` over `Services/` and `Infrastructure/`; each can in principle repeat a side effect after a
5xx whose answer was lost. Whether each is actually harmful depends on Graph's own de-duplication, which was not verified here.

- `Services/Registration/GraphUserService.cs` (`Users.PostAsync` create user ~82; `AssignLicense.PostAsync` ~210; `Groups[..].Members.Ref.PostAsync` ~268) and `Services/Registration/CiamUserProvisioningService.cs` (`Users.PostAsync` ~95). A repeated create answers 4xx (user exists); the first answer is then a failure for a user that was created.
- `Services/Communication/GraphSubscriptionManager.cs` (~440), `MailboxVerificationService.TryCreateGraphSubscriptionAsync` (~356) and `Infrastructure/Graph/SpeFileStore.cs` (~407): `Subscriptions.PostAsync`; a repeat may create a second subscription (duplicate webhooks).
- `Infrastructure/Graph/SpeAdminGraphService.cs` (folder create `Children.PostAsync` ~1179/1186; permission/invite/other POSTs ~3815, 3952, 4153, 4342, 5190 container type create) and `DriveItemOperations.cs` (preview POSTs ~829/908, idempotent), `UploadSessionManager.cs` (~642, upload session, harmless to repeat). Container create (~1434) and `ContainerOperations` create already use the option; container-type create (~5190) does not (permanent, 25-per-tenant cap, per CLAUDE.md §17: worth a look by its owner).

## Tests (step 2, 4)

New: `tests/unit/domain/Communication/SendMailCallersNoTransientRetryTests.cs` (compiled into `Sprk.Bff.Api.Tests` through the
`domain` glob). Each test drives the caller's real code through the real `GraphHttpMessageHandler` over a counting fake transport
(`RetryCount = 3`).

Results:

- Fail-first (new test file against unfixed code, same commit otherwise): 12 tests, **7 failed** (node 503/408/500, node transport
  fault, verification 503/408/500: "Expected SendMailRequests to be 1 ... but found 4"), 5 passed (the must-not-fire controls:
  429 retried, success, node 400).
- After the fix: 12 of 12 pass.
- Filtered suite `SendMailCallersNoTransientRetryTests | SendEmailNode | MailboxVerification | Communication | NoTransientRetry |
  GraphHttpMessageHandler | Signals` (Signals includes the contract baselines): **8896 passed, 0 failed, 8 skipped** (live seam tests).
- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 warnings, 0 errors. Full `tests/Spaarke.ArchTests` (built explicitly): **2793 passed, 0 failed**.
- `dotnet list package --vulnerable --include-transitive`: none. No package or project file changed.

## Publish size (CLAUDE.md §10)

`dotnet publish -c Release <Sprk.Bff.Api> -o deploy/api-publish -r linux-x64 --self-contained false` (framework-dependent
linux-x64, Release, PDBs included), `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`, from fresh detached
worktrees at short paths (`C:\wm212` = origin/master `b005279c4d`, `C:\wb212` = this branch). The decisions-widget `npm build` was
skipped on both sides (the csproj embeds it only when present; it is identical on both sides, so the delta is unaffected).

| | files | raw bytes | zip bytes |
|---|---|---|---|
| master | 194 | 144,440,707 | 43,578,756 |
| branch | 194 | 144,441,923 | 43,579,289 |
| delta | 0 | +1,216 | **+533 (+0.0005 MB)** |

File counts match. Both worktrees removed with `Remove-WorktreeSafe.ps1`.

## Step 9.5 self-review

code-review of the three files: no F-class finding. K-class: the node's "may have been delivered" text depends on the handler's
record or an `ApiException` 5xx/408, the same rule as `EmailChannelSender.OutcomeOf` (a 400-class refusal never claims it).
adr-check: ADR-010 (no new registration, interface or package), ADR-013 (the `Services/Ai` change is the send call and its failure
text only; no core logic moved there, D-66), ADR-004/036 n/a, ADR-028 n/a (no auth change). No ADR tension.

## Section 11 answers (no new surface, answered for the record)

1. Existing: `NoTransientRetryOption` and the `GraphHttpMessageHandler` non-idempotent chain (task 191, #1755).
2. Extension: no new component; the existing option is passed at the two remaining sendMail call sites.
3. Cost of doing nothing: a 5xx/408 on sendMail is retried up to three more times, so a verification message or a playbook email can reach the recipient up to four times.

rework: none task 212
