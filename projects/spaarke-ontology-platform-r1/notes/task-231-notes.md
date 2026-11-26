# Task 231 notes: appnotification priority and toast type valid values (#1697)

Branch `fix/ontology-231-appnotification-priority` from `origin/master` c438119bca. Revives the intent of PR #1494
(`work/132-notification-delivery-correctness`, c86ff04dd) on a fresh branch, without the retired playbook path (D-100).

## Findings (step 1)

- Valid `appnotification` option values: priority Normal 200000000 / High 200000001; toasttype Timed 200000000 /
  Hidden 200000001; icontype 100000000..100000005. Source: the published Microsoft Dataverse table reference
  (global choices `notification_priority`, `notification_toasttype`). The repo solution source
  (`src/dataverse/solutions/SpaarkeMaster/Entities/appnotification/Entity.xml`) carries only the custom `sprk_`
  columns, so it does not hold these system option sets; the Dataverse MCP server was not connected in this session, so
  no live metadata read was made here (PR #1494 recorded one on 2026-10-09 that agrees with the published table).
  Nothing was written to Dataverse.
- Defects on master c438119bca:
  - `GrantExpiryReminderJob` sent 200000002 ("Critical") for `DaysLeft <= 1`: the last-day reminder write failed.
  - `IncomingCommunicationJobHandler` sent 200000002 for the held-email admin alert: the write failed, the catch logged it.
  - `NotificationService` documented a Critical value and accepted any int.
  - `NotificationActionCore` compared Hidden as 100000000, so a Hidden toast still got the Open action.
  - `ActionSeam` passed an LLM- or binding-supplied priority / toastType to Dataverse unchecked.

## Change

- New `Services/AppNotificationOptions.cs`: Priority / ToastType / IconType constants, `IsValidPriority`,
  `IsValidToastType`, `Validate`.
- `NotificationService`: priority default and icon map use the constants; an invalid priority throws
  `ArgumentOutOfRangeException` before any write. Public signature unchanged.
- `GrantExpiryReminderJob`: `DaysLeft <= 7` is High, otherwise Normal (the same reminders at the same times; Warning and
  Critical both map to High). `IsTransient` is `internal` and treats `ArgumentException` as permanent, so a request
  the writer built wrongly is not retried.
- `IncomingCommunicationJobHandler`: held-email alert is High.
- `NotificationActionCore`: Hidden constant is 200000001 (from `AppNotificationOptions`).
- `ActionSeam` / `IActionSeam`: validates priority and toastType and returns a failed `CreateNotificationResult` naming
  the value (no throw, no write); request defaults use the constants.

## Decision: validation is NOT in `NotificationActionCore`

PR #1494 also made `NotificationActionCore.CreateAsync` throw on an invalid value. On this base that moves 4
`Issue1452_NotificationPlaybookFetchXmlShapeTests` cases (notification-matter-activity / new-emails / new-events /
tasks-overdue) from pass to fail: the retired daily-update-service playbook JSONs still carry `priority: 100000000`
and go through the retired `CreateNotificationNodeExecutor` into that core. The validation is therefore kept at the
non-playbook entry (ActionSeam) and NotificationService, and the executor path behaves exactly as before. Only the
Hidden constant is changed in the core, which is a correctness fix for every caller.

## Tests

- New `tests/unit/Sprk.Bff.Api.Tests/Services/Notifications/AppNotificationOptionsTests.cs` plus
  `Services/Fixtures/appnotification-options.snapshot.json` (compared with the constants; csproj copies it to output).
- Extended: `GrantExpiryReminderJobTests` (priority per DaysLeft, `IsTransient`), `SecureChildOwnershipTests`
  (held-email alert is High), `DispositionRoutabilityNotificationSeamTests` (valid LLM priority, invalid one fails loudly).

## Not done on purpose (D-106)

- `CreateNotificationNodeExecutor` (retired path, D-100): its XML comments and the `priority` / `toastType` field
  descriptions still list 100000000 / 300000000 values (`CreateNotificationNodeExecutor.cs:31,115,121,756,763`). Not
  touched. Tracking item: open PR #1507 (D-104b), which deletes the retired executor.
- `CommunicationArrivedProducer.ToastTypeTimed` and `CommunicationRiActionService` priority literal: already valid
  values (200000000); swapping them for the constants was cosmetic.

## Live check (review round 1)

The unit test compares the constants with a hand-kept snapshot, so it catches drift between two files, not a change by
Microsoft. `tests/integration/Sprk.Bff.Api.IntegrationTests/Notifications/AppNotificationOptionsLiveTests.cs` reads the
`priority`, `toasttype` and `icontype` option sets from Dataverse metadata (GET only) and compares them with the
snapshot and the constants. It is skipped unless `SPAARKE_LIVE_PLAYBOOKS_DATAVERSE_URL` is set (the gate of
`NotificationPlaybookFetchXmlLiveTests`) and needs an `az login` session. The gate was not set on this machine, so the
live test has been built and shown as skipped but not run against Dataverse.

## Known limits

- K4: `GrantExpiryReminderJob.IsTransient` now treats an `ArgumentException`, including one wrapped in another exception
  (the loop walks `InnerException`), as permanent. The reminder is then not retried in the run; the failure is logged and
  counted as before.
