# Task 095 — compact Related-to row, "Save as new document" link, task pane width

Owner UAT round 4 (2026-10-04), items 1-5. Record: `notes/042-uat-round4-2026-10-04.md`.

## 1. What shipped

| Item | Change |
|---|---|
| 2-4 Related-to | `RelatedToPicker.tsx`: the "Related to" label + icon are gone. Row 1 = type pills (left) with "+ New" right-aligned (`marginLeft: auto`, wraps instead of clipping). Row 2 = lookup box (search icon inside, placeholder `Look up related {Type}...`, follows the selected pill) + a subtle **icon** button (accessible name/title "Search", spinner while searching). Enter still searches. No "Search" text pill. "+ New" turns the lookup row into the create form (name box + Create + Cancel) and the pill-row New hides while creating. The radiogroup aria-label is now "Related to record type" so the group keeps an accessible name without the visible label. |
| 5 Save as | `SaveModeSection.tsx`: the "Save as" radio group and its helper text are removed. Version mode (a document already in Spaarke, the default) renders nothing here; the primary button just saves a new version (`existingDocumentId` + `isNewVersion`, unchanged path). The quiet **"Save as new document"** link is the existing 094 unlock in the locked name box (`renderDocumentDetails('locked')`, now a transparent link-style button instead of an outlined button) - one unlock path pre- and post-save. In create mode the section shows the "separate document" hint + a **"Keep as version"** link (`onChoiceChange('version')`). A document not in Spaarke (identity new / not applicable) renders neither. Conflict / undetermined / denied views are unchanged. |
| 1 Width | See section 2. `shared/taskpane/services/taskPaneWidthService.ts` (new) + one call in `word/taskpane/index.tsx` after `Office.onReady`. |

Placement/justification (CLAUDE.md 11): no BFF change. One new tiny client service (width), justified because no existing module owns host-pane sizing and it has a distinct, independently tested rule (per-platform defaults + gating). Everything else extends existing components.

## 2. Item 1 — width verdict: a supported RUNTIME API exists (the expected "no" holds only for the manifest)

Verified on Microsoft Learn 2026-10-04 (researcher subagent):

- **Runtime: YES.** `Office.extensionLifeCycle.taskpane.setWidth(width)` — requirement set **TaskPaneApi 1.1**, released (not preview). Word on the web; Office on Windows **2507+ (Build 19029.20004)**; Mac **16.100.4+**. Limits: web (Word) 330-500 px; Windows 86 px to 50% of the client window; Mac 270 px to 50%. Out-of-range values are silently ignored (no error, returns void). Documented defaults: Word web 330, Windows 320, Mac 270.
- **Manifest: NO in a released schema.** XML `RequestedWidth`/`DefaultSettings` is content add-ins only. The unified-manifest `actions[].taskpane.preferredWidth` exists only in the **preview** schema (`m365-app-prev`), described as a hint the host may ignore. No min/max setting anywhere. `Office.addin.showAsTaskpane()` takes no size.
- Sources: https://learn.microsoft.com/en-us/javascript/api/office/office.taskpane ; https://learn.microsoft.com/en-us/javascript/api/office/office.extensionlifecycle?view=common-js ; https://learn.microsoft.com/en-us/javascript/api/requirement-sets/common/task-pane-api-requirement-sets ; https://learn.microsoft.com/en-us/javascript/api/manifest/defaultsettings ; https://learn.microsoft.com/en-us/microsoft-365/extensibility/schema/extension-runtimes-actions-item-taskpane . Researcher memory: `.claude/agent-memory/researcher/office-addin-taskpane-width-2026-10-04.md`.

Applied per the POML ("if yes, apply +75 px"): target = default + 75 -> **web 405, Windows 395, Mac 345**; gated on `isSetSupported('TaskPaneApi','1.1')` and a known platform; best effort, never throws (older builds keep the default). Decided by requirement set + platform, never `hostType` (NFR-10). `@types/office-js` 1.0.568 does not expose `extensionLifeCycle` (its `TaskPane` interface carries stale defaults), so the call goes through a narrow structural cast. Applied in the Word pane entry only (the owner's item was the Word pane); the Outlook entry is untouched.

Unknowns (the docs do not say; confirm live): whether the width persists across reopen, whether a user's manual resize later wins, and behaviour if called before the pane is visible. Windows builds older than 2507 get no change.

## 3. Deviations (directional mode)

- The "Save as new document" link was not re-added to `SaveModeSection` (it already exists in the locked name box; a second copy would be the duplicate 094 explicitly removed). The section owns only "Keep as version".
- The placeholder uses three ASCII dots ("...") exactly as the acceptance criterion states (was a single ellipsis character).

## 4. Gates

| Gate | Result |
|---|---|
| Gated jest (every path in `ci-gated-suites.txt`, `--runTestsByPath`) | **74 / 74 suites, 1016 / 1016 tests** green. (`DocumentProfileSection` showed red once in a full-package parallel run; 20/20 green in isolation - load flake, file untouched.) |
| New suites (added to `ci-gated-suites.txt`) | `RelatedToPicker.layout.test.tsx` (5), `taskPaneWidthService.test.ts` (8) |
| Updated suites | `SaveModeSection.test.tsx`, `SaveFlow.versionMode.test.tsx` (no radios, link, Keep as version, new round-trip test), `SaveFlow.test.tsx` (no "Related to", new placeholder) |
| `npm run lint` | 0 problems (`--max-warnings 0`) |
| `npm run typecheck` | 68 total / **0 production** (unchanged baseline; all in pre-existing test/mock files) |
| `npm run build` | Not run (parallel-wave rule; the main session builds) |

## 5. Step 9.5 — code-review and adr-check (inline pass)

| # | Finding | Severity / confidence | Action |
|---|---|---|---|
| 1 | Two near-identical link-style `makeStyles` classes (`SaveFlow.secondaryLink`, `SaveModeSection.linkBtn`) | Suggestion / high | Accepted: the two files share no style module today; extracting one for 8 lines adds more surface than it removes |
| 2 | `requestWiderTaskPane` uses a structural cast because the installed typings lack `extensionLifeCycle` | Suggestion / high | Accepted, commented; the call is guarded (`typeof setWidth === 'function'`) and try/catch'd |
| 3 | `SaveModeSection` header comment table still says "switch to 'a new document'" | Nit | Still true (via the link); left |
| 4 | Pill-row "+ New" is hidden during create; Cancel lives on the lookup row | Observation / high | Intentional, tested |
| ADR-021 | Fluent v9 only; semantic tokens (`colorBrandForegroundLink`/`Hover`); icon button has an accessible name (+ title) and the default Fluent focus ring; pill row wraps at 320 px (not verified in a browser) | Compliant | none |
| NFR-10 | No `hostType` branch added | Compliant | none |
| ADR-038 | Tests in `__tests__` KEEP paths; new suites gated; pinning tests updated | Compliant | none |

No ADR conflict; no exception or amendment needed.

## 6. Live acceptance — OPEN

Needs a deploy (the main session builds and deploys): round-4 layout; a saved document shows no radios, "Save" saves a version, "Save as new document" unlocks the name and creates a new document, "Keep as version" returns; pane width +75 px on Word web / Windows 2507+ / Mac. No Office host in this session.
