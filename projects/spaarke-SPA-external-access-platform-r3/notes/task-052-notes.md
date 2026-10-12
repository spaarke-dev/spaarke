# Task 052 - Intake wizards part 1: wiring + NDA Assessment + Policy Question (FR-05)

**Status (2026-10-12):** implemented (rigor FULL, model tier sonnet, effort high). Not committed (the main session commits by path). POML steps 1 (owner approval) and 7 (TASK-INDEX/POML status) were not done by this agent; the owner field-set decisions arrived with the brief (see below). The browser UI tests in the POML (`<ui-tests>`) were not run: no live session from this agent.

## Owner-approved field sets (as built)

Source: `notes/intake-form-design.md` section 1.2 (NDA), 2.2 (Policy), 5 (envelope and caps), modified by the owner answers of 2026-10-12 (section 6.1): no department field (the server stamps `requesterDepartment` from the contact); no incident routing or link in the Policy wizard (DEF-002); Policy topics are a fixed code list in R3; the NDA wizard covers review and issue; no document generation (DEF-001).

**JSON envelope (both wizards).** A flat object. `schemaVersion: 1` first. Option CODES, not labels (the detail view maps codes to labels). Dates are ISO `yyyy-mm-dd`. A key exists only when its field is shown and non-empty (hidden-branch and blank keys are omitted; switching a branch back and forth never leaves a stale key). Text is trimmed; caps count the trimmed value in UTF-16 units. The client never sends `requesterDepartment` or any requester/owner key. Largest possible NDA answers are about 7 KB, far under the 100 KB cap.

### NDA Assessment (`intake/ndaModel.ts`, `NdaAssessmentWizard.tsx`) - 3 steps

| Step | JSON key | Control | Required | Rule / caps | Shown when |
|---|---|---|---|---|---|
| 1 Counterparty | `ndaAction` | radio | yes | `review` Review the other party's NDA (they sent their own draft) / `issue` Issue our standard NDA | always |
| | `counterpartyName` | text | yes | 2-200 | always |
| | `counterpartyType` | select | yes | `customer` `vendor` `partner` `investment` `individual` `other` | always |
| | `counterpartyTypeOther` | text | yes | 2-200 | `counterpartyType = other` |
| | `counterpartyCountry` | text | no | 0-100 | always |
| 2 Purpose and terms | `purpose` | multiline | yes | 10-1000 | always |
| | `disclosureDirection` | radio | yes | `mutual` `weDisclose` `theyDisclose` `unsure` | always |
| | `sensitiveInfo` | checkboxes | no | `personalData` `sourceCode` `financial` `regulated` `none`; `none` excludes the others | always |
| | (banner) | - | - | "After you submit, open the request and add the other party's draft under Documents. Legal can't review it without the file." | `ndaAction = review` |
| | `counterpartySignerName` | text | yes | 2-150 | `ndaAction = issue` |
| | `counterpartySignerEmail` | email | yes | email format, <= 254 | `issue` |
| | `term` | radio | yes | `oneYear` `twoYears` `threeYears` `fiveYears` | `issue` |
| | `governingLaw` | text | yes | 2-200 | `issue` |
| 3 Timing and review | `neededBy` | date | **required on `issue`**, optional on `review` | today <= date <= today + 365 days | always |
| | `neededByReason` | text | no | 0-300 | `neededBy` is a valid date within 3 business days |
| | `notes` | multiline | no | 0-2000 | always |
| | (summary) | read-only list of labels | - | - | always |

Title (`sprk_name`, unsecured): `NDA - {counterpartyName}` (en dash), whole title truncated to 850. Nothing else (never purpose or notes).
Success: "NDA request sent to Legal". Review path: "Next step: open the request and add the other party's draft under Documents." Issue path: "Legal will use your details to issue our standard NDA to {signer}." Both: do not share confidential information until signed. Buttons: Open request (primary; the record detail view), Service Requests (the list tab), Done.

### Policy Question (`intake/policyModel.ts`, `policyTopics.ts`, `PolicyQuestionWizard.tsx`) - 2 steps

| Step | JSON key | Control | Required | Rule / caps | Shown when |
|---|---|---|---|---|---|
| 1 Your question | `topic` | select | yes | 13 fixed codes in `policyTopics.ts` (`ethics` `gifts` `antiBribery` `conflicts` `privacy` `infoSecurity` `communications` `signingAuthority` `employment` `competition` `tradeCompliance` `records` `other`) | always |
| | `topicOther` | text | yes | 2-200 | `topic = other` |
| | `question` | multiline | yes | 20-1000 | always |
| | `background` | multiline | no | 0-4000 | always |
| | `situation` | radio | yes | `planning` `happened` `general` | always |
| 2 Timing and review | `urgency` | radio | yes | `routine` `soon` `urgent` | always |
| | `neededBy` | date | yes when shown | today <= date <= today + 90 days | `urgency` is `soon` or `urgent` |
| | (summary) | read-only list of labels | - | - | always |

Not built: the design note's incident warning banner (owner: no incident routing or link, deferred to DEF-002); `policyReference` and `peopleInvolved` (nice to have).
Title: `Policy question - {topicLabel}`; for `other`, `Policy question - {topicOther}` with the typed part cut to 120 characters. Never question or background text.
Success: "Question sent to Legal"; urgent adds "contact Legal directly as well"; mentions Documents.

**Topic list seam.** `policyTopics.ts` is the one module that owns the list; the wizard reads it through `getPolicyTopics()`. The codes are the stored value and are stable. A later per-customer source replaces that function; stored answers need no migration.

## Files (all under `src/client/external-spa/`)
New: `src/api/intake-client.ts`, `src/intake/intakeRegistry.ts`, `intakeValidation.ts`, `intakeFormParts.tsx`, `intakeSuccess.tsx`, `ndaModel.ts`, `policyModel.ts`, `policyTopics.ts`, `NdaAssessmentWizard.tsx`, `PolicyQuestionWizard.tsx`, `tests/IntakeWizards.test.tsx`, `tests/IntakeModels.test.ts`.
Modified: `src/api/me-client.ts` (`intakeRequestTypes?: number[]`), `src/components/shell/QuickStartPane.tsx`, `src/registry/widgetRegistry.ts`, `src/widgets/ServiceRequestsWidget.tsx` (comment), `src/mocks/mock-service.ts` (dev-mock POST handler; dead code in a build, `check:dist` clean).

## Server contract (read from code)
`ExternalIntakeEndpoints.cs` + `ExternalIntakeDtos.cs`: body `{ requestType, title, answers }`; title 1-850; answers a JSON object <= 100 KB; 201 `{ sprk_servicerequestid }`. 403 module (`sdap.external.intake.module_not_entitled`), 403 type (`...invention_not_available`), 403 no contact; 400 `EXT_INTAKE_INVALID_*`; 413 body > 256 KB; 429; 500 `EXT_INTAKE_OWNER_UNRESOLVED`; 503 `EXT_INTAKE_DATAVERSE_UNAVAILABLE` / `EXT_INTAKE_CONTACT_UNREADABLE` (nothing created). Type codes (`ExternalDataService.ServiceRequestTypes`): NDA 100000000, Invention 100000001, Policy 100000002, Trademark 100000003. The submit client refuses locally (no request) a blank/over-850 title or answers over 100 KB.

## Entry-point visibility (review 011 K3)
`isIntakeTypeAvailable(me, requestType)` in `intakeRegistry.ts`:
- no `legal-front-door` in `me.entitlements` -> hidden (whatever `intakeRequestTypes` says);
- `intakeRequestTypes` is an array -> shown iff it lists the type (`[]` = none);
- `intakeRequestTypes` is `undefined` (older server, or the `PLANE_DEFAULTS` 5xx fallback - which deliberately leaves it unset) -> today's rule: NDA, Policy and Trademark for any legal-front-door caller; Invention only on the workforce plane. Undefined is read as neither "none" nor "all".
The widget registry no longer uses the plane to hide legal-front-door widgets: `my-requests`, `inventions`, `service-requests` and `nda` are `planes: ['workforce','ciam']` (`LEGAL_FRONT_DOOR_PLANES`) with `requiredEntitlement: 'legal-front-door'`, so the entitlement from `/me` decides. Tests cover both partner-switch states (switch on: partner holds `legal-front-door` and sees the cards, the library entry and the default tab; off: nothing).

## Deviations and decisions
1. **`ndaAction` replaces `paperSource`.** Review = their paper, issue = ours, so keeping both would let them disagree. The design note's `paperSource` is not asked or stored.
2. **`WizardShell` mounted directly, not through `InAppWizardHost`.** `InAppWizardHost` is Xrm-coupled (it registers a fixed set of Console wizards on the launcher seam and builds Xrm data/navigation adapters); the broker-only SPA must not import Xrm. `WizardShell` (modal mode = SprkModal wizard preset, ADR-050) is the same canonical shell the host mounts. No shared component was changed.
3. **Policy Question card gated by `legal-front-door`, not `policy-library`.** The submit route requires `legal-front-door` (the card used to be gated by `policy-library` because it opened the Policy Library tab). The Policy Library widget itself is unchanged.
4. **Service Requests is a DEFAULT tab for a CIAM partner too** (`defaultForRoles: ['workforce','ciam']`), but only when entitled, i.e. only with the partner switch on. Review 011 K3 says the partner "must see" it; making it a default tab is my reading. If the owner wants it library-only for partners, change that one array. A partner's outside-counsel default tabs are otherwise unchanged (tested).
5. **Invention and Trademark cards keep their present launch** (Invention opens the placeholder `inventions` widget; Trademark is "coming soon" in More Services) but now use the same visibility rule. `inventions` is allowed for CIAM so a partner who passes the invention switch is not shown a dead card. Task 053 appends two entries to `INTAKE_WIZARDS`; `QuickStartPane` needs no change for it.
6. **NDA card description** changed from "Upload an NDA and get an AI compliance assessment." to "Ask Legal to review an NDA or issue our standard one." (the wizard does no AI assessment and uploads no file). The `nda` widget stays registered (library only, placeholder).
7. **Counterparty-name hint** also says the name appears in the request title (the title is unsecured).
8. **Error display.** The shell's own error bar shows the message (`role=alert`); messages come from the server ProblemDetails `detail` where it is user-facing (403 contact, 400, 500 owner unresolved), fixed text for the type refusal, 413, 429, 503 and network/unknown ("could not be submitted. Please try again."). Answers and the step are kept (the form state lives in the wizard component, not the shell).
9. **No idempotency key on the server.** The Finish button is disabled while a submit is in flight, so a double-click cannot post twice. A response lost after the server created the row (network drop) would show an error although the request exists; a retry would then create a second one. Known limit; the 503 paths are safe (nothing created). Suggest a server idempotency key if this matters (not in scope).
10. Required-field messages appear once a field is left (blur) or is invalid while typed; the Next/Submit button stays disabled until the step is valid (the shell's `canAdvance`). There is no "try anyway" click.

## Gates (in `src/client/external-spa`)
- `npm run typecheck` exit 0; `npm run lint` exit 0 (0 problems); `npx vitest run` 920 passed / 35 files (849 before; 71 new: 44 in `IntakeWizards.test.tsx`, 27 in `IntakeModels.test.ts`); `npm run build` exit 0 (`dist/assets/app.js` 1,436.93 kB); `npm run check:dist` exit 0 ("1 file(s) clean").
- One full run timed out a single slow test (`App.deepLinkSignIn`, 60 s cap, machine under load); it passes alone (17 s) and the next full run passed 920/920.
- Fail-before: restoring the HEAD `QuickStartPane.tsx` and `widgetRegistry.ts` (new files kept) -> `IntakeWizards.test.tsx` 36 failed / 6 passed (card opens widget, no visibility rule, plane gate); with the new files 42/42 (then 44/44 with the two `/me` tests). The new modules did not exist before, so their tests cannot run against old code.
- Dark mode: only Fluent v9 tokens and library components in `src/intake/` (no hex/rgb, grep-checked).
