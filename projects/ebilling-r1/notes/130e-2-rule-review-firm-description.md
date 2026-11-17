# 130e-2: Rule Review "Firm description" field (client)

Task: `tasks/130e-2-rule-review-firm-description-field.poml`. Contract: `notes/130e-firm-description-writer.md`, "The 130e-2 contract (editor)".
Client only: `src/solutions/RuleReview/**` plus this note. No server, shared library, deploy or live call.

## What changed

| File | Change |
|---|---|
| `src/services/firmDescription.ts` (new) | The one client module for the field: the six reason codes, the 200 limit, the messages (the server's wording), `checkFirmDescription` (the server's checks in the server's order), `firmDescriptionToSend` (what the save path sends), `firmDescriptionRefusals` (reads a 422). Free of React. |
| `src/services/ruleSetApi.ts` | `ProposalView.firmDescription: string \| null`; `RuleEdit.firmDescription?: string`. |
| `src/services/reviewActions.ts` | `diffProposals` reports a changed firm description (the reload after a 409). |
| `src/components/ProposalDetailPane.tsx` | A Fluent v9 `Textarea` labelled "Firm description" in the "Proposed rule" card, an `n / 200` counter (no `maxLength`), the visible notice, the field errors, read-only on a Duplicate rule. The text joins the dirty state, Discard and Save. Save is disabled while a text that would be sent fails a client check. |
| `src/components/RuleSetDetailView.tsx` | Keeps the server's firm description refusals by rule (from a failed save, accept or publish) and passes the selected rule's to the pane, with the rule set's name. The existing reload after a save shows the new draft when the edit created one. |
| `src/__tests__/testKit.ts` | `proposal()` carries `firmDescription: null`. |

## Behaviour

- **Show:** the stored `proposals[].firmDescription` fills the field; null is an empty field.
- **Save:** `firmDescription` is on the PATCH body only when the admin changed it (compared after the server's trim): a changed text as typed, a cleared or blank field as `""`, an untouched field not at all. It is never null. A Duplicate rule never sends one.
- **Client checks** (early; the server decides), in the server's order: blank is valid; Duplicate; unpaired surrogate or a Control, Format, line or paragraph separator character; more than 200 Unicode scalars (`[...text].length`); `{` `}`; `<` `>`; the rule set's or the rule's name after NFKC, white-space runs folded to one space, case-insensitively. The trim uses the server's white-space set (so U+FEFF is not trimmed, as on the server).
- **Counter:** `n / 200`, turning to the error colour over the limit; no `maxLength`, so nothing is cut (ISS-027).
- **Notice** (by the field): "Firms see this text when this rule rejects their invoice. Do not name the rule set or the rule, and do not put internal notes here. The check for names does not catch letters from other alphabets that look the same, so read the text through before you save."
- **Duplicate:** the field is read-only and a reason replaces the notice and the counter.
- **Server refusal:** a 422 `ruleset.invalid` or `ruleset.publish_invalid` with `errors[].field === "firmDescription"` shows one message per reason code under the field (the server's wording), until the admin edits the text again. The top banner still lists the server's errors, as for every other failure.
- **After save:** unchanged path. `saveEdit` reloads the rule set; on a published working version the server creates a draft and the reload shows it (form re-keyed by version id), with the existing "Saved as a new version" notice.

## Evidence per criterion

Suite: `src/solutions/RuleReview` Jest. New files: `firmDescription.test.ts`, `firmDescriptionPane.test.ts`, `firmDescriptionPin.test.ts`.

| Criterion | Tests |
|---|---|
| Field shows the stored text; edit sends; clear sends `""`; untouched leaves the key out | `firmDescriptionPane`: "is labelled ... and shows the stored text", "is empty when the rule has none". `firmDescription`: "an edit sends the text as typed", "a cleared field over a stored text sends \"\"", "must-not-fire: an untouched field leaves the key out" (stored text, none, trimmed-equal, blank over none), "must-not-fire: a Duplicate rule never sends one", "the member is on the PATCH body only when set" (the real API client's JSON). |
| Each check fires and not on clean text; NFKC and white space; 200 astral accepted, 201 refused | `firmDescription` "checkFirmDescription": length (200/201 plain, 200/201 astral), braces (3), markup (3), invisible (11 cases incl. lone surrogates, TAG, separators), paired surrogate and no-break space not fired, Duplicate, names (any case, rule set name, fullwidth NFKC, 4 white-space forms, name with odd white space, rule-name-only and set-name-only, must-not-fire clean/partial/blank names, homoglyph not caught), order of checks, trim set. |
| Each 422 reason shows its message; Duplicate read-only | `firmDescription` "a 422 firm description refusal" (one test per reason code, six codes with six messages, publish form per rule, no policy id, unknown reason, must-not-fire for other field / 400 / other code / none, `saveEdit` hands the 422 back); `firmDescriptionPane` "a server refusal (422) under the field" (one message per reason code, no repeat, none when empty) and "a Duplicate rule" (read-only with reason, not on other types, no error). |
| Pin test against the C# source | `firmDescriptionPin`: the six codes against `FirmDescriptionRules.cs` constants (and the message set against the same constants), the 200 against `MaxRunes` and the rune enumeration, the request member against `ProposalEditBody.FirmDescription` and `FirmDescriptionField`, the GET member against `ProposalView.FirmDescription`, the two error codes, the 422 rule, the error JSON shape, and the order of the checks in `Validate`. |
| Full suite, typecheck, build | see Verification. |
| After save on a published rule | `firmDescription` "after a save on a published rule" (edit carries the member; reload returns the new draft with the new text; stale reload diff names the field). |

## Verification (2026-11-17, task worktree)

| Check | Result |
|---|---|
| `npx jest` in `src/solutions/RuleReview` | 140 passed, 0 failed in 6 suites; 1 suite (`assignmentPanelFirmPicker.test.ts`) cannot start: it mocks `@spaarke/ui-components` without `virtual`, and the shared library has no `dist` in a fresh worktree ("Cannot find module"). It fails the same way on the base commit (60 passed, 1 suite failed before this change) and is untouched here. |
| `npm run typecheck` | Surface-owned: 0 (99 pre-existing errors in shared libs, deferred as before). |
| `npm run build` | succeeds (Vite aliases the shared library to source). |
| `npm run lint` | not run: `eslint` is not installed in `src/solutions/RuleReview` (no eslint dependency in its `package.json`; the script cannot run). |

Fail-before: with the base `ProposalDetailPane.tsx` and `reviewActions.ts` restored, 13 tests failed (the 7 `the Firm description field in the rule detail` tests, 2 of the `a Duplicate rule` tests, 2 of the `a server refusal (422) under the field` tests, and in `after a save on a published rule` the stale-reload diff and the published-to-new-draft test); the pure-function and pin tests exercise a new module, so before the change their suites fail to load. Mutations of the new module (each reverted, byte-compared): counting UTF-16 units, no NFKC, no white-space fold, always sending, no `""` on a clear, no invisible check, case-sensitive names, and a renamed reason code each fail the tests named for them.

## Known limits

- Look-alike letters from another alphabet are not caught by the name check, as on the server; the notice says so.
- The name check upper-cases per character (keeping a letter whose upper case is longer, such as "ß"), which approximates the server's ordinal ignore-case comparison; the server stays authoritative and a text the client lets through can still be refused with a 422 (shown under the field).
- The page has no DOM test environment (node Jest, as the other page tests): the field's typing and the Save click are covered through the pure functions (`firmDescriptionToSend`, `checkFirmDescription`) the component calls, and its rendered output through static markup, not by simulating events.
- A refusal from Accept or Publish shows under the field of the rule it names when that rule is selected; the publish dialog's own banner still lists every error.
- `assignmentPanelFirmPicker.test.ts` fails to start in a worktree without a built `Spaarke.UI.Components` (pre-existing, above).
