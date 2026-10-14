# Task 068 notes - Internal Only switch on the compose surfaces

Date: 2026-10-14. Scope executed: steps 0-5. Step 6 (dev import) is NOT done and belongs to the main session after owner approval. Nothing is committed.

## What it does

A single switch labelled exactly "Internal Only" (helper text "Only people in your organization can see this") on the message and email compose surfaces. The value always goes to `POST /api/communications/send` as an explicit boolean `isInternalOnly` (never omitted by these surfaces; default `false`). It starts on for a reply or forward whose source is known internal-only. It is shown on and disabled when the surface knows the target thread is Private. The BFF (task 065) still floors it: it only ever raises the flag (Private, no-privacy or unreadable thread, reply or forward of an internal source), so a client that is wrong or silent cannot leak.

## Files changed

Shared library `src/client/shared/Spaarke.UI.Components/src/`:
- `services/communicationApi.ts` - `SendCommunicationOptions.isInternalOnly?`, copied into `requestBody` (the one request shape; no default, so only non-switch callers can omit it).
- `components/EmailComposer/subcomponents/InternalOnlySwitch.tsx` (NEW) - the shared switch (Fluent v9 `Switch`, tokens only, `useId` from Fluent so it is React 16 safe). Exports `INTERNAL_ONLY_LABEL`, `INTERNAL_ONLY_HINT`.
- `components/EmailComposer/index.ts` - exports the switch, constants, `initialInternalOnly`.
- `components/EmailComposer/EmailComposer.types.ts` - `EmailComposerState.isInternalOnly`, action `SET_INTERNAL_ONLY`, props `sourceIsInternalOnly?`, `threadIsPrivate?`.
- `components/EmailComposer/EmailComposer.reducer.ts` - `initialInternalOnly(props)`, state init, reducer case, `mapStateToSendRequest(state, threadId?, threadIsPrivate?)` sets `isInternalOnly`.
- `components/EmailComposer/EmailComposer.tsx` - renders the switch (not in read-only/view mode), passes `threadIsPrivate` into the request mapping.
- `components/EmailComposer/wrappers/SendEmailPage.tsx` - `sourceIsInternalOnly?` (reaches the engine through the existing props spread).
- `components/CommunicationTimeline/subcomponents/TimelineComposeBox.tsx` - `ITimelineSendPayload.isInternalOnly: boolean` (always explicit), prop `threadIsPrivate?`, switch in the actions row, reset after send.
- `components/CommunicationTimeline/CommunicationTimeline.types.ts` - `CommunicationTimelinePrefill.isInternalOnly?`, `CommunicationTimelineThreadModeProps.threadIsPrivate?`.
- `components/CommunicationTimeline/CommunicationTimeline.tsx` - passes `threadIsPrivate`; `handleSend` forwards `payload.isInternalOnly` to `sendTimelineMessage`; quote-into-message prefill raises the switch when the quoted message is internal-only.
- `components/ConversationView/ConversationView.tsx` - `ConversationComposeBar` gets the switch (no hint, no Private lock: that bar does not know privacy) and sends `isInternalOnly`.
- Tests: NEW `CommunicationTimeline/__tests__/TimelineComposeBox.internalOnly.test.tsx`, NEW `EmailComposer/__tests__/EmailComposer.internalOnly.test.tsx`, edited `ConversationView/__tests__/ConversationView.compose.test.tsx`, edited `services/__tests__/communicationApi.test.ts`.

PCFs `src/client/pcf/`:
- `CommunicationMessageActions/CommunicationMessageActions/hostContext.ts` (+ `__tests__/hostContext.test.ts`) - `resolveInternalOnlySeed()`, `THREAD_PRIVACY_PRIVATE`.
- `CommunicationMessageActions/CommunicationMessageActions/CommunicationMessageActionsApp.tsx` - reads thread-form `sprk_privacystate`, reads `sprk_isinternalonly` of the source (added to the existing `$select`), passes `prefill` and `threadIsPrivate`, forwards `payload.isInternalOnly`.
- `CommunicationTimeline/CommunicationTimeline/hostContext.ts` (+ test) - `resolveThreadIsPrivate()`.
- `CommunicationTimeline/CommunicationTimeline/CommunicationTimelineApp.tsx` - passes `threadIsPrivate` from the thread form attribute.
- `CommunicationActions/CommunicationActions/CommunicationActionsApp.tsx` - reads `sprk_isinternalonly` of the source (existing `$select`), passes `sourceIsInternalOnly` (reply/forward only) into `composerProps`.
- Version bump files and rebuilt `Solution/Controls/...` bundles (below).

Dataverse solution XML `src/dataverse/solutions/SpaarkeMaster/Entities/sprk_Communication/`:
- `Entity.xml` - both display-name occurrences for `sprk_isinternalonly` (the local option set and the attribute): "Is Internal Only" -> "Internal Only".
- `FormXml/main/{b58ec3d8-0982-f111-8076-7ced8ddc4cc6}.xml` (Message main form) - cell label -> "Internal Only".

No change under `src/server/**`, `src/client/external-spa/**`, `src/solutions/**`, `.claude/`, TASK-INDEX.md, current-task.md or the POML status.

## Surfaces wired

| Surface | Host | Switch | Starts on | Locked (Private) |
|---|---|---|---|---|
| `TimelineComposeBox` (thread compose / reply box) | CommunicationTimeline PCF, CommunicationMessageActions PCF, shared `CommunicationTimeline` | yes | quoting an internal message (`prefill.isInternalOnly`); MessageActions respond placement on an internal source | yes, from the thread form's `sprk_privacystate` (Private = 100000001) |
| `EmailComposer` (compose / reply / forward) | CommunicationActions PCF (via `SendEmailPage`), Console `SendEmailDialog` / `useEmailComposeActions`, `CommunicationPage`, Word add-in Email tab (next add-in build), DocumentUploadWizard `DocumentEmailStep` | yes (not in view mode) | reply or forward with `sourceIsInternalOnly` | only when a host passes `threadIsPrivate` (none of the email hosts does today) |
| `ConversationComposeBar` (`ConversationView`) | conversation page | yes, no hint | no | no (does not know privacy; BFF floor covers it) |

The Private lock is real only where the host already holds the thread's privacy (the thread-form attribute in the two PCFs). Everywhere else the BFF floor (065) is what protects a Private thread.

## Escalation triggers

1. **A surface that builds its own request:** none found. Every send goes through `sendCommunication()` in `communicationApi.ts` (`sendTimelineMessage` is a thin delegate; `DocumentEmailWizard` also calls `sendCommunication()`). No second request shape was added. Not fired.
2. **EmailComposer used by an auto-send flow with no composing person:** none found.
   - `DocumentUploadWizard` `DocumentEmailStep.tsx` (`src/solutions`, not touched) embeds an inline `EmailComposer` and calls `composerRef.current.send()` from the wizard Finish guard; the user composes in a visible composer, so the switch is visible there. Not fired.
   - Word add-in Email tab (`EmailView` -> `SendEmailPane`) picks up the switch on its next build.
   - `SendEmailDialog` / `useEmailComposeActions` (Console) and `CommunicationPage` do not pass `sourceIsInternalOnly`, so a reply there starts off. The BFF still raises it for a reply or forward of an internal source. See follow-ups.
   - `EmailStep/SendEmailStep` is a different, older component and was not changed.

## Decisions

1. The label is exactly "Internal Only"; helper text exactly "Only people in your organization can see this" (owner wording 2026-10-14). The Conversation compose bar omits the helper text to keep the bar compact.
2. One shared `InternalOnlySwitch` component, so the label, helper text and Private behaviour exist once.
3. The client always sends an explicit boolean (`false` by default). `SendCommunicationOptions.isInternalOnly` stays optional so non-switch callers are unchanged and the BFF decides.
4. Private is shown as on and disabled, and the send value is `threadIsPrivate || switch`, so it cannot be turned off in the UI. The BFF floor remains the authority.
5. A compose (new) never starts on from `sourceIsInternalOnly`; only reply and forward do. A prefill only ever raises the switch.
6. The switch is user-dirtying in `EmailComposer` (`SET_INTERNAL_ONLY` sets `isDirty`).
7. React 16 safety: `useId` is imported from `@fluentui/react-components`, not `React.useId`.
8. Message form label and column display name renamed in the SpaarkeMaster solution XML only. The `_managed.xml` form copy has an empty label for this cell, so it was left alone (the two copies are not in step).
9. Step 9.5 quality gates (code-review, adr-check) were not run, since the brief was steps 0-5. The main session should run them.

## PCF versions bumped

| PCF | From | To |
|---|---|---|
| CommunicationActions | 1.3.19 | 1.3.20 |
| CommunicationMessageActions | 1.0.5 | 1.0.6 |
| CommunicationTimeline | 1.0.4 | 1.0.5 |

Locations (5 each, per the pcf-deploy skill), for `<P>` = each control:
1. `src/client/pcf/<P>/<P>/ControlManifest.Input.xml` (`version=`)
2. `src/client/pcf/<P>/<P>/index.ts` (`CONTROL_VERSION`)
3. `src/client/pcf/<P>/Solution/solution.xml` (`<Version>`)
4. `src/client/pcf/<P>/Solution/Controls/sprk_Spaarke.Controls.<P>/ControlManifest.xml` (copied from the build output)
5. `src/client/pcf/<P>/Solution/pack.ps1` (`$version`)

The rebuilt `bundle.js` was also copied into each `Solution/Controls/sprk_Spaarke.Controls.<P>/` (styles.css unchanged). `package-lock.json` changes made by npm install were reverted.

## Tests and builds

- Shared lib jest (`--maxWorkers=2`), affected set `CommunicationTimeline`, `ConversationView`, `EmailComposer`, `src/services`: 78 suites, 1122 tests, all pass.
- New or extended tests in the shared lib: TimelineComposeBox 6, EmailComposer 12, ConversationView 2, communicationApi 2 (22 new).
- Fail-before proof (HEAD sources restored, new tests kept): 4 suites failed, 19 tests failed, 35 passed (54 tests in those four files). Restored afterwards.
- PCF jest: CommunicationMessageActions 12 passed (6 new), CommunicationTimeline 10 passed (4 new), CommunicationActions 30 passed.
- Shared lib `tsc --noEmit`: clean (0 errors).
- `npm run build:prod` through `scripts/Invoke-PcfBuildProd.ps1`: all three succeeded (1.3.20, 1.0.6, 1.0.5). "Only people in your organization" is present in each bundle.
- Prettier: new and changed shared-lib files formatted. `communicationApi.test.ts` was already non-compliant at HEAD and left as is.
- Environment setup needed to run tests and builds (all gitignored output): `npm install` + `npm run build` in `Spaarke.SdapClient` and `Spaarke.Auth`; `npm install` in `Spaarke.Communication.Components` and the three PCFs.

## Follow-ups (not done here)

1. **Office add-in switch plus reply headers.** The Word add-in Email tab gets the switch automatically through `EmailComposer`. The Outlook add-in needs its own switch, and it should detect a reply to an internal message from the reply headers (`EmailMetadata.InReplyTo` / `References`, read with Office.js `getAllInternetHeadersAsync`) so a reply starts on when the original was internal.
2. **Bulk send** paths: not changed. They get the BFF default and floor only. Decide whether bulk send needs a switch or a fixed value.
3. **Console and code page hosts.** `SendEmailDialog` / `useEmailComposeActions` and `CommunicationPage` should pass `sourceIsInternalOnly` (and `threadIsPrivate` where known) for reply and forward, so the switch starts on there. They are under `src/solutions/**` or outside this task's scope.
4. **DocumentUploadWizard** (`src/solutions`) `DocumentEmailStep` uses the shared `EmailComposer`, so it now shows the switch with no code change. Confirm the owner wants it there.
5. **Messaging in the external SPA** (`src/client/external-spa`) is untouched; external users never see the switch.
6. **Managed form copy** `_managed.xml`: the cell label is empty there. Align with the main form if the owner wants the label shown in the managed copy.
7. **Step 9.5** code-review and adr-check on this change set.

## Step 6 - dev import (for the main session to show the owner; NOT run)

Do nothing below without owner approval of the dev import.

Pack each PCF (run inside the `Solution` folder; the scripts use paths relative to the working directory):

```powershell
cd C:\code_files\spaarke-wt-SPA-external-access-platform-r3\src\client\pcf\CommunicationActions\Solution
powershell -ExecutionPolicy Bypass -File pack.ps1        # bin\CommunicationActionsSolution_v1.3.20.zip
cd ..\..\CommunicationMessageActions\Solution
powershell -ExecutionPolicy Bypass -File pack.ps1        # bin\CommunicationMessageActionsSolution_v1.0.6.zip
cd ..\..\CommunicationTimeline\Solution
powershell -ExecutionPolicy Bypass -File pack.ps1        # bin\CommunicationTimelineSolution_v1.0.5.zip
```

Import each ZIP with the scoped import script (it also publishes the bundle web resources, which an import alone does not serve):

```powershell
pwsh scripts/Import-SolutionScoped.ps1 -EnvironmentUrl <dev org url> -ZipPath "src/client/pcf/CommunicationActions/Solution/bin/CommunicationActionsSolution_v1.3.20.zip" -SolutionUniqueName CommunicationActionsSolution
pwsh scripts/Import-SolutionScoped.ps1 -EnvironmentUrl <dev org url> -ZipPath "src/client/pcf/CommunicationMessageActions/Solution/bin/CommunicationMessageActionsSolution_v1.0.6.zip" -SolutionUniqueName CommunicationMessageActionsSolution
pwsh scripts/Import-SolutionScoped.ps1 -EnvironmentUrl <dev org url> -ZipPath "src/client/pcf/CommunicationTimeline/Solution/bin/CommunicationTimelineSolution_v1.0.5.zip" -SolutionUniqueName CommunicationTimelineSolution
```

`pac solution import` exits 0 even when the import fails (#1591). After each import confirm from Dataverse: the solution version equals the ZIP `<Version>`; if not, read the newest `importjobs` row (`progress` below 100 means failure; `data` holds the `result="failure"` text).

Column and form label ("Is Internal Only" -> "Internal Only"): the repo change is in the SpaarkeMaster solution XML (`Entity.xml`, Message main form). On dev, either import that solution XML change and publish customizations, or apply the same two edits live (the `sprk_isinternalonly` attribute display name, and the Message main form cell label) and publish. Pick the route with the owner; the PCF ZIPs are independent of it.

Verify after import: hard refresh (Ctrl+Shift+R); on a Message form and a thread form the compose box shows the "Internal Only" switch, off by default; on a Private thread it is on and disabled; a reply or forward of an internal message starts on; a send with the switch on creates a communication with `sprk_isinternalonly = true` that an external caller cannot see.

### Rollback

1. Re-import the previous PCF versions: CommunicationActions 1.3.19, CommunicationMessageActions 1.0.5, CommunicationTimeline 1.0.4. Build the ZIPs from the previous commit's `Solution` folders (`git show HEAD:<path>` for the five version files plus the committed bundle, or check out the prior commit in a scratch worktree and run `pack.ps1`). A model-driven app only reloads a control on a version change, so import the old version as-is; if a PCF keeps showing new behaviour, `pac solution delete --solution-name <X>` and re-import.
2. Restore the "Is Internal Only" column display name and Message form label (revert the two SpaarkeMaster XML files, or set the labels back live, and publish customizations).
3. No BFF or data change is involved, so no data rollback is needed. Messages already sent keep their stored `sprk_isinternalonly` value.
