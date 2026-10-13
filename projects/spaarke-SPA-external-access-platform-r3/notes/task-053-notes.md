# Task 053 - Intake wizards part 2: Invention Submission + Trademark Search (FR-05)

**Status (2026-10-12):** implemented (rigor FULL, model tier sonnet, effort high). Not committed (the main session commits by path). POML steps 1 (owner approval; the field sets arrived approved with the brief) and 6 (TASK-INDEX / POML status) were not done by this agent. The POML `<ui-tests>` (live site, dark mode) were not run: no live session from this agent.

## Field sets (as built)

Source: `notes/intake-form-design.md` section 3.2 (Invention), 4.2 (Trademark), 5 (envelope and caps), modified by the owner answers of 2026-10-12 (section 6.1). The POML's list ("title, inventors, description, first public disclosure date, related project" / "mark, classes, jurisdictions, intended use date") is the older draft; the design note supersedes it (see Deviations).

Envelope (both wizards, as 052): a flat object, `schemaVersion: 1` first, option CODES not labels, ISO `yyyy-mm-dd` dates, a pure yes/no as a boolean and yes/no/unsure as a string code, and NO key for a hidden or empty field (a stale answer behind a closed branch is dropped). Text is trimmed; caps count the trimmed value in UTF-16 units. The client never sends `requesterDepartment` or any requester key.

### Invention Submission (`intake/inventionModel.ts`, `InventionSubmissionWizard.tsx`) - 4 steps

| Step | JSON key | Control | Required | Rule / caps | Shown when |
|---|---|---|---|---|---|
| 1 The invention | `shortTitle` | text | yes | 5-150; hint "Appears in the request title; keep it generic." + describe the area, not the idea | always |
| | `problemSolved` | multiline | yes | 20-2000 | always |
| | `description` | multiline | yes | 50-8000 | always |
| | `knownAlternatives` | multiline | no | 0-3000 | always |
| 2 Inventors | `submitterIsInventor` | radio | yes | `true`/`false` in the JSON (`yes`/`no` in the form) | always |
| | `coInventors` | list of `{name, email, organization}` | at least 1 row when `submitterIsInventor` is no | max 20 rows; `name` 2-150 required; `email` optional, email format, <= 254; `organization` optional, <= 200 | always (optional when the submitter is an inventor) |
| | `conceptionDate` | date | no | 1900-01-01 <= date <= today | always |
| 3 Disclosure, use, outside involvement | `publicDisclosure` | radio | yes | `none` `done` `planned` | always |
| | `disclosureDate` | date | yes when shown | `done`: <= today; `planned`: today <= date <= today + 3650 days | `publicDisclosure` is `done` or `planned` |
| | `disclosureMethod` | select | yes when shown | `publication` `presentation` `onlinePost` `productSale` `demo` `openSource` `other` | same |
| | `disclosureVenue` | text | no | 0-300; "venue or journal name only" | same |
| | (warning banner) | - | - | "Public disclosure can limit or end patent protection, especially outside the US. Legal will contact you about timing." | `done`; or `planned` with a valid date <= 60 days ahead |
| | `sharedExternally` | radio | yes | `yes` `no` `unsure` | always |
| | `sharedWith` | text | yes when shown | 1-500 | `sharedExternally = yes` |
| | `productUse` | radio | yes | `no` `planned` `released` | always |
| | `productName` | text | yes when shown | 1-200 | `productUse` is `planned` or `released` |
| | `productDate` | date | no | 1900-01-01 .. today + 3650 days | same |
| | `externalInvolvement` | radio | yes | `yes` `no` `unsure` | always |
| | `externalInvolvementDetails` | text | yes when shown | 1-1000 | `externalInvolvement = yes` |
| 4 Review and confirm | `notes` | multiline | no | 0-2000 | always |
| | (summary) | read-only labels | - | - | always |
| | `accuracyConfirmed` | checkbox | yes (must be true) | stored as `true` | always |

Title (`sprk_name`, unsecured): `Invention - {shortTitle}` (en dash), whole title truncated to 850. Never the problem, description or alternatives.
Success: "Invention disclosure submitted"; thank-you with the short title; "Next step: add drawings, slides, lab notes or a draft paper under Documents"; "Keep it confidential ..."; when a disclosure date was given, "Legal has your disclosure / planned disclosure date of {date} and will prioritise accordingly." Buttons: Done, Service Requests, Open request (primary).
Not built (nice to have, design note): `inventorContributionPct`, `commercialApplications`, `competitors`, `keywords`, `thirdPartyCode`, `labNotebookRef`. No `neededBy` (urgency is implied by the disclosure dates, design note 5.1).

### Trademark Search (`intake/trademarkModel.ts`, `trademarkTerritories.ts`, `TrademarkSearchWizard.tsx`) - 3 steps

| Step | JSON key | Control | Required | Rule / caps | Shown when |
|---|---|---|---|---|---|
| 1 The proposed mark | `proposedMark` | text | yes | 1-200; hint says it appears in the request title | always |
| | `markForm` | radio | yes | `word` `logo` `wordAndLogo` `slogan` | always |
| | (info banner) | - | - | "After you submit, add the logo or design file under Documents." | `markForm` is `logo` or `wordAndLogo` |
| | `markMeaning` | text | no | 0-500 | always |
| 2 How and where | `goodsServices` | multiline | yes | 10-2000 | always |
| | `markRole` | select | no | `product` `service` `company` `feature` | always |
| | `territories` | checkboxes | yes | at least 1 code from `getTrademarkTerritories()` | always |
| | `territoriesOther` | text | yes when shown | 2-300 | `territories` contains `other` |
| 3 Status and timing | `currentUse` | radio | yes | `notYet` `internal` `public` | always |
| | `firstUseDate` | date | no | 1900-01-01 <= date <= today | `currentUse = public` |
| | `useLocations` | text | no | 0-300 | `currentUse = public` |
| | `launchDate` | date | no | today <= date <= today + 3650 days | always |
| | `neededBy` | date | no | today <= date <= `launchDate` when that is a valid date, else today + 365 days | always |
| | `notes` | multiline | no | 0-2000 | always |
| | (summary) | read-only labels | - | - | always |

Title (`sprk_name`, unsecured): `Trademark search - {proposedMark}` (en dash), whole title truncated to 850. Never the goods or services.
Success: "Trademark search requested"; "Next step: add the logo file under Documents" only for a logo; the "don't announce, file, register domains or order packaging until Legal confirms" paragraph. Same three buttons.
Not built: `alternatives` (nice to have, owner's call), `businessOwner`, `domainNeeded`, `usedWithHouseMark`, `searchDepth` (not recommended).

### Code list seam
`intake/trademarkTerritories.ts` is the ONE module that owns the territory list (`us` `ca` `eu` `uk` `cn` `jp` `in` `au` `worldwide` `other`; `other` last and asks for free text). The wizard and model read it only through `getTrademarkTerritories()` (and `trademarkTerritoryLabel(code)`), exactly as the Policy wizard reads `getPolicyTopics()`. Codes are the stored value and are stable; a later per-customer source replaces that function with no data migration.

## Entry points
- `intakeRegistry.ts` appends `{ inventionSubmission, 'Invention Submission' }` and `{ trademarkSearch, 'Trademark Search' }`.
- `QuickStartPane.tsx`: all four cards and More Services entries open a wizard. The `opensWidgetId` field and its launch branch (the Invention card used to open the placeholder `inventions` tab) are removed because no card uses them. The "Coming soon" badge for a request type with no registered wizard is kept (none today). The `inventions` widget itself (the Inventions list tab) and its `requiredIntakeType` gate in `widgetRegistry.ts` are unchanged.
- **Partner availability is the server's call.** No new rule: `isIntakeTypeAvailable` (unchanged) shows the Invention card / More Services entry / Inventions widget only when `/me` `intakeRequestTypes` lists the Invention type (`ExternalAccess:PartnerInventionIntakeEnabled`, enforced on the submit route too). Tests: a partner with `[NDA, POLICY, TRADEMARK]` has no Invention card or More Services entry and the Trademark card opens its wizard; the same partner with Invention listed sees the card and More Services entry and it opens the wizard.

## Server contract (read from code, not changed)
`ExternalIntakeEndpoints.cs` / its DTOs: `POST /api/v1/external/servicerequests` body `{ requestType, title, answers }`; title 1-850; answers a JSON object <= 100 KB; 201 `{ sprk_servicerequestid }`. Type codes (`ServiceRequestTypes`): NDA 100000000, Invention 100000001, Policy 100000002, Trademark 100000003. The largest possible Invention answers are 29,230 UTF-16 units in the free-text and inventor-row fields (8000 + 3000 + 2000 + 2000 + 1000 + 20 inventor rows, plus the other capped fields); at 3 bytes per unit plus keys that is about 89 KB, still under the 102,400-byte cap, and `submitServiceRequest` refuses locally if the cap is exceeded. Errors, the 401 sign-in message and answer-keeping are inherited from `intake-client.ts` / the shell exactly as in 052 (tests for 403 and 401 per new wizard).

## Deviations and decisions
1. **Field list: design note over POML.** The POML's "title, inventors, description, first public disclosure date, related project" and "mark, classes, jurisdictions, intended use date" are the older draft. Built per the design note: the Invention first-public-disclosure date is `disclosureDate` (with `publicDisclosure`, method, venue); "related project" is not asked (no such field in the design note); the Trademark "intended use date" is `launchDate` plus `neededBy`.
2. **No trademark-class field or list - NEEDS OWNER CONFIRMATION.** The brief says "territories (jurisdictions) and classes are FIXED code lists in R3". The approved design note (section 4.1 takeaway 2, section 4.2) deliberately does NOT ask business users for Nice classes: `goodsServices` is free text and Legal assigns the classes; section 6.1 answer 4 fixes only the territories. I built the approved field set, so there is no class list (an unused list would be dead code). If the owner wants an optional "classes, if you know them" multi-select, it is one fixed list module plus one optional field and one answers key (about an hour); say so and it is a small follow-up.
3. **Co-inventor email is optional (format-checked when given); organization optional.** The design note gives the row as `{name 2-150, email (email format, <= 254), organization <= 200}` and does not say which are required; only the name is. Co-inventor emails are personal data about third parties, so none is forced.
4. **Co-inventor rows are always shown and always validated**, also when the submitter is an inventor (a row the requester added is sent; blank-name rows must be removed). Only the "at least one" rule depends on `submitterIsInventor`.
5. **Date bounds the design note leaves open:** a planned disclosure, a product release date and a trademark launch date accept today up to 3650 days (10 years) ahead; a past date floor of 1900-01-01 is a sanity floor. Trademark `neededBy` has no upper bound in the note when there is no launch date: today + 365 days (as the NDA's `neededBy`).
6. **`productDate` is optional and bounded** (the note gives no validation); `disclosureVenue` is optional.
7. **Title hints.** Invention `shortTitle`: "Appears in the request title; keep it generic." (as 052's `topicOther`) plus the design note's "shown in request lists and notifications, describe the area not the idea". Trademark `proposedMark`: "The mark appears in the request title." (the mark IS the title by design; "keep it generic" does not apply, same wording pattern as the NDA counterparty hint).
8. **Defect fixed in scope (found, pre-existing from 052): `CheckboxRow` shared one control id.** A Fluent `Checkbox` inside a `Field` takes the Field's single control id and `aria-labelledby`, so every checkbox in a group had the same id (a label click toggles the FIRST box) and the same accessible name (the group label). It affected the NDA wizard's sensitive-information list and would have hit the territory list. `CheckboxRow` now wraps its checkboxes in `FieldContextProvider value={undefined}`. Test: the NDA checkboxes have distinct ids and a click toggles only its own box (fails on HEAD).
9. **New shared row `ConfirmRow`** (one checkbox that must be ticked) in `intakeFormParts.tsx`, for `accuracyConfirmed`.
10. **`WizardShell` mounted directly** (no `InAppWizardHost`), as 052.

## Gates (in `src/client/external-spa`)
- `npm run typecheck` exit 0; `npm run lint` exit 0 (0 problems); `npx vitest run` 986 passed / 35 files (925 before); `npm run build` exit 0 (`dist/assets/app.js` 1,461.34 kB, was 1,436.93 kB); `npm run check:dist` exit 0 ("1 file(s) clean").
- Fail-before (card wiring): with the HEAD `QuickStartPane.tsx`, `intakeRegistry.ts` and `intakeFormParts.tsx` restored (new wizard/model files kept), `IntakeWizards.test.tsx` = 40 failed / 46 passed of 86 (cards open the widget / nothing, registry has two entries, partner Invention and Trademark entry points, the checkbox-id defect, and every wizard test that opens a new wizard through its card); with the new wiring 86/86 and `IntakeModels.test.ts` 51/51.
- Dark mode: only Fluent v9 tokens and library components in `src/intake/` (no hex/rgb, grep-checked). The warning/info bars are Fluent `MessageBar`; the co-inventor list error uses `tokens.colorPaletteRedForeground1`.
