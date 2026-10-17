# Task 068 notes - Internal Only switch on the compose surfaces

Date: 2026-10-14. Scope executed: steps 0-5. Step 6 (dev import) is NOT done and belongs to the main session after owner approval. Nothing is committed.

## What it does

A single switch labelled exactly "Internal Only" (helper text "Only people in your organization can see this") on the message and email compose surfaces. The value always goes to `POST /api/communications/send` as an explicit boolean `isInternalOnly` (default `false`), except an untouched reply or forward of a source whose flag the host does not know, which omits it (see Fix round 1). It starts on for a reply or forward whose source is known internal-only. It is shown on and disabled when the surface knows the target thread is Private. The BFF (task 065) still floors it: it only ever raises the flag (Private, no-privacy or unreadable thread, reply or forward of an internal source), so a client that is wrong or silent cannot leak.

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
   - `SendEmailDialog` / `useEmailComposeActions` (Console) and `CommunicationPage` do not pass `sourceIsInternalOnly`, so a reply there starts off. The send omits `isInternalOnly` for an untouched reply or forward of an unknown source, so the BFF raises it (explicit false would win). See Fix round 1 and follow-ups.
   - `EmailStep/SendEmailStep` is a different, older component and was not changed.

## Decisions

1. The label is exactly "Internal Only"; helper text exactly "Only people in your organization can see this" (owner wording 2026-10-14). The Conversation compose bar omits the helper text to keep the bar compact.
2. One shared `InternalOnlySwitch` component, so the label, helper text and Private behaviour exist once.
3. The client sends an explicit boolean (`false` by default), except an untouched reply or forward of an unknown source, which omits the field so the BFF raises it (Fix round 1). `SendCommunicationOptions.isInternalOnly` stays optional so non-switch callers are unchanged and the BFF decides.
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

## Fix round 1 (review finding F1)

**Defect.** `EmailComposer` always sent an explicit `isInternalOnly`. The BFF (`CommunicationService.cs:2286`, pinned by `DataverseRecordCreationTests.cs:701-703`) raises a reply or forward of an internal source to true ONLY when `request.IsInternalOnly` is null; an explicit `false` is the sender's choice and wins. So a reply or forward whose source flag the host did not pass (Console Reply/Forward card `useEmailComposeActions.tsx:241-242`, CommunicationPage `?mode=reply` `EmailComposerSlot.tsx:100-101`, CommunicationActions PCF null flag mapped by `=== true`) got the switch off, sent explicit false, and switched the server raise off. A partner could then see a reply that quotes an internal email. The earlier statements "the BFF still raises it" (in `communicationApi.ts`, `EmailComposer.types.ts` and these notes) were wrong for an explicit false.

**Rule now.** Explicit false wins on the server. An untouched reply or forward (mode reply/forward with a `communicationId`, the same condition that sets `inheritRegardingFromCommunicationId`) of a source whose flag is unknown OMITS `isInternalOnly`, so the server raise applies. Anything else sends the explicit boolean. `threadIsPrivate === true` always sends `true`. The BFF is unchanged.

**Acceptance criterion wording (task notes only; the POML is not edited).** Original: "sendCommunication sends isInternalOnly: true when the switch is on and isInternalOnly: false when it is off. It never omits the field from these surfaces." Now: "... It omits the field only for an untouched reply or forward of an unknown source, so the server raise applies; otherwise it is explicit."

**Changes.**
- `EmailComposer.types.ts`: `EmailComposerState` gains `internalOnlyTouched: boolean` and `sourceIsInternalOnly?: boolean`; the `sourceIsInternalOnly` prop doc and the state doc now state the real rule.
- `EmailComposer.reducer.ts`: `initialState` seeds both; `SET_INTERNAL_ONLY` sets `internalOnlyTouched: true`; `RESET` (returns a fresh `initialState`) clears it; new `resolveIsInternalOnly()` and `mapStateToSendRequest` omit the key (no `isInternalOnly` property at all) for an untouched reply/forward of an unknown source with no Private thread.
- `communicationApi.ts`: `SendCommunicationOptions.isInternalOnly` doc corrected (the field was already optional; no type change).
- `CommunicationActionsApp.tsx`: the source flag is read three ways (true / false / undefined) through the new `sourceInternalOnly.ts` `readSourceIsInternalOnly()`; state starts `undefined`, so a null flag or a failed read is unknown and the field is omitted. New file justification: a pure helper the PCF jest suite can import without the React app (same pattern as `archiveFailure.ts`); without it a null flag is silently sent as explicit false.

**Tests.** `EmailComposer.internalOnly.test.tsx` (now 19): untouched reply (with `communicationId`, no source flag) has NO `isInternalOnly` property; untouched forward likewise; reply toggled on, off, on sends true; toggled on then off sends explicit false; source known false sends false; source known true starts on and sends true (existing test); Private thread with an unknown source still sends true; reducer: `SET_INTERNAL_ONLY` marks touched and `RESET` clears it. PCF `sourceInternalOnly.test.ts` (4): true, false, null and non-boolean mapping (null gives undefined, so the field is omitted).

**Seeding proof.** With the omission disabled (`resolveIsInternalOnly` never returning undefined) the EmailComposer file fails 3 of 19: "an untouched reply with a communicationId and no sourceIsInternalOnly sends NO isInternalOnly property", "an untouched forward of an unknown source also omits the field", "SET_INTERNAL_ONLY marks the switch touched and RESET clears it". Restored: 19 of 19 pass.

**Results.** Shared lib jest (four 068 files, `--maxWorkers=2`): 4 suites, 62 tests pass. PCF jest: CommunicationActions 34 (30 + 4 new), CommunicationMessageActions 12, CommunicationTimeline 10, all pass. `tsc --noEmit` clean for `Spaarke.UI.Components` and `Spaarke.Communication.Components`. Rebuilt with `npm run build:prod`: CommunicationActions only (1.3.20, not deployed yet, so no version bump); its bundle was copied into `Solution/Controls/sprk_Spaarke.Controls.CommunicationActions/bundle.js`. CommunicationMessageActions and CommunicationTimeline were NOT rebuilt: their bundles do not contain `EmailComposer`/its reducer (`SET_INTERNAL_ONLY`, `emailComposerReducer` absent), and the `communicationApi.ts` change is comment-only.

**AC7 test justifications (previously missing).**
- ConversationView (2): the conversation compose bar builds its own `sendCommunication` call, so its default-off explicit false and its on-then-reset-after-send behaviour are not covered by the TimelineComposeBox or EmailComposer tests; without them a regression there would reach the BFF unseen.
- communicationApi (2): `sendCommunication` is the single request shape every surface shares; one test pins that true and false are copied through, the other pins that a caller passing nothing leaves the key out (so the omit path added in this round reaches the BFF as a null, not as false).

**Known limits (recorded, not fixed).**
- K1: draft mode ignores the draft row's own flag (the switch starts from props only, off unless the host says otherwise).
- K2: thread privacy is read once at form mount (a thread made Private while the form is open keeps the old lock state; the BFF floor still applies on send).
- K2: the CommunicationTimeline PCF's own compose sends no `threadId` (`CommunicationTimeline.tsx:336-355`), so its client Private lock is the only Private guard on that path. That predates 068.
- Hosts that do not pass `sourceIsInternalOnly` (Console Reply/Forward card, CommunicationPage `?mode=reply`) now rely on the omit path, which is correct; passing the flag there remains follow-up 3 so the switch can start on.

## Fix round 1 re-check (2026-10-14, sonnet adversarial-reviewer): no F-class finding

EmailComposer 296/296 and CommunicationActions 34/34 pass. Known limits:
- K1: `sourceIsInternalOnly` is a snapshot taken at mount. No current host changes it after mount, and a stale `undefined` omits the field (fail-safe).
- K2: an untouched reply to a source whose flag is unknown shows the switch off, but the server raises it to internal. That is fail-closed. Follow-up 3 (hosts pass the flag) makes the switch start on.
- K3: the reply/forward predicate exists twice (`EmailComposer.reducer.ts:383` and `:439`). A future edit must change both; a candidate for a shared helper.
- K4: tests are missing for: a `sourceRecord` reply with no `communicationId` prop; a key change followed by RESET through the component; and the `CommunicationActionsApp` wiring.
- K5 (pre-existing): if `threadIsPrivate` arrives after mount, the switch shows off and locked, while the send still carries true.

**Deploy order:** the BFF with task 065 must be on dev before these PCFs. The dev BFF predates 065, so it would ignore `isInternalOnly`.

## Step 6 - dev deploy record (2026-10-14, owner-approved)

The owner approved all four dev changes: 044 apply, BFF from R3, three PCF imports, live label rename.

1. **BFF**:
   - Deployed R3 `4e23edeb2` (contains all of origin/master, including UAC `8a1f3bb11`) to `spaarke-bff-dev` with `pwsh -ExecutionPolicy Bypass -File scripts/Deploy-BffApi.ps1`.
   - Hash-verify matched, `/healthz` passed, CORS OK. Full BFF unit suite at that SHA: 21,491 passed, 0 failed.
   - Announced beforehand to UAC-r2, word-add-in-r1 and ontology-r1. The next master deploy by any of them removes R3's unmerged changes from dev; ontology will message before its 035 deploy.
   - Rollback: redeploy from master.
2. **PCFs**: imported with `scripts/Import-SolutionScoped.ps1` (scoped publish, no tenant-wide publish). Versions read back from `solutions`:
   - CommunicationActionsSolution 1.3.20
   - CommunicationMessageActionsSolution 1.0.6
   - CommunicationTimelineSolution 1.0.5

   The bundles committed with 068 were current, because the master merge changed nothing under `src/client/shared` or these PCFs. Rollback ZIPs (1.3.19 / 1.0.5 / 1.0.4) are packed in the scratch worktree `C:\wtr3-068rb` (detached at `ceb699193`), under each `src/client/pcf/<P>/Solution/bin/`.
3. **Label, live**:
   - `sprk_communication.sprk_isinternalonly` display name changed "Is Internal Only" → "Internal Only": a PUT of the BooleanAttributeMetadata with `MSCRM.MergeLabels: true`.
   - The Message main form `{b58ec3d8-0982-f111-8076-7ced8ddc4cc6}` cell label changed to "Internal Only": a PATCH of `formxml`, exactly one cell.
   - `PublishXml` for `sprk_communication`. All three calls returned 204, and both labels read back as "Internal Only".
   - Rollback: the before-images (attribute JSON and form JSON) are in the session scratchpad. The reverse is the same PUT and PATCH with "Is Internal Only", then publish.

**Pending:** the owner checks on dev, using the "Verify after import" list above.

### Step 6b - the hosts the owner actually uses (2026-10-14, owner-approved)

**What the owner found.** The owner checked on dev and saw no switch. The two compose surfaces they use are hosts that 068 had not rebuilt:
- **The record form's "Messages" modal** is the `CommunicationConversationPanel` PCF (`ConversationModal.tsx:132`), which embeds the shared `ConversationView`.
- **The "New Email" modal on the Email page** is the `sprk_emailpage` code page (`src/solutions/EmailPage`): `useEmailComposeActions`, then `SendEmailDialog`, then `EmailComposer`.

**What was deployed** (owner approved "Deploy both"):
- **CommunicationConversationPanel:** bumped 1.18.2 → 1.18.3 (commit `be5c7257e`) and imported with `Import-SolutionScoped.ps1`. Dev reads back 1.18.3.
- **`sprk_emailpage`:** built from R3 `be5c7257e`, which contains master `3084f0ca1`, the source of word-add-in's 03:00Z deploy. Word-add-in's #1812 marker was present in the build. Deployed with `Deploy-WebResourceInline.ps1` at 21:48:17Z; it reads back byte-equal (1,654,422 bytes).
- **`sprk_communicationconversationpage.html`** (the full-page conversation page, same `ConversationView`): deployed at 21:48:24Z; it reads back byte-equal.

**Rollback:**
- Re-import `CommunicationConversationPanelSolution_v1.18.2.zip`, packed in `C:\wtr3-068rb\src\client\pcf\CommunicationConversationPanel\Solution\bin\`.
- For the two pages, re-upload the saved pre-deploy content (`wr-sprk_emailpage-before.json`, `wr-sprk_communicationconversationpage.html-before.json` in the session scratchpad), or rebuild them from master, then publish.

**Hosts not deployed.** These also get the switch on their next build: the Console (`sprk_spaarkeai`, last deployed by word-add-in) and DocumentUploadWizard (follow-up 4).

### Step 6c - owner check: "External | Internal Only" tags (2026-10-14, owner-approved)

**Owner check.** The switch works on dev (the Messages modal sent one Internal Only and one non-internal message). The owner asked for a better look and placement: "an on/off tag with the label in the tag, (External) | (Internal Only), red for Internal Only and green for External".

**What changed:**
- `InternalOnlySwitch` gained `variant="tags"`, extending the existing control rather than adding a new one. The tags variant:
  - is a two-option radio group with one tab stop; the arrow keys move between options;
  - colours the selected option with Fluent palette tokens, green or red, and adapts to dark mode;
  - when `locked`, keeps "Internal Only" selected and makes "External" unavailable;
  - moves the hints into tooltips. The "External" hint is new wording: "People outside your organization with access to this record can see this".
- **The Messages compose bar** (`ConversationView`) shows the tags in a row above the message box (commit `7419e8637`).
- **The email composer** uses the tags too (commit `247ce6a6e`; owner: "Also use tags in New Email"). The rules are unchanged, and re-selecting the current tag is not a change, so an untouched reply of an unknown source still omits the field.
- **The timeline compose box** keeps the switch.

**Tests:** EmailComposer and ConversationView, 370 passed. New tag-variant tests cover arrow keys, the locked state and the tooltip hint.

**Deployed** (approved "Also use tags in New Email"):
- CommunicationConversationPanel 1.18.4; reads back 1.18.4.
- `sprk_emailpage` at 01:53:16Z; byte-equal, and word-add-in's #1812 marker is present.
- `sprk_communicationconversationpage.html` at 01:53:24Z; byte-equal.

The 1.18.4 bundle contains no email composer, so the second commit did not change it.

**Rollback:**
- Re-import 1.18.3 (`src/client/pcf/CommunicationConversationPanel/Solution/bin/`, gitignored, or rebuild from `be5c7257e`).
- Re-upload `wr-*-before-tags.json` from the session scratchpad, or rebuild both pages from `be5c7257e`.

**Not yet rebuilt** (they show the tags on their next build): the CommunicationActions and CommunicationMessageActions PCFs, the Console and the Word add-in, all of which use EmailComposer.
