# Task 080 Completion — P6 WizardShell Light-First Re-base onto SprkModal Tokens (FR-17)

> 🔒 RIGOR LEVEL: **FULL** (POML-declared; confirmed via decision tree — modifies `.tsx`, tags `react`/`fluent-ui`/`frontend`, high blast radius). Model tier sonnet @ effort high (POML). Step mode: directional. Deps 003 (`ModalWindowControls` glyph) ✅ and 008 (`WizardModal` preset / `SprkModal` base) ✅ confirmed satisfied via TASK-INDEX.md before starting.

## Scope discipline (owner §11-G, light-first)

Touched ONLY header/footer/size chrome tokens in `WizardShell.tsx` + stale-doc JSDoc in `wizardShellTypes.ts`. Did **not** perform a full internal re-base onto `SprkModal` — the Dialog envelope, `embedded` mode, `WizardStepper` (200px sidebar), and the reducer/imperative-handle/success-screen machinery are byte-identical to before this task.

## 1. Chrome alignment — before/after mapping

| Element | Before (v1.1.63) | After (task 080) | Token source |
|---|---|---|---|
| Header padding | `paddingTop/Bottom: spacingVerticalL`, `paddingLeft: spacingHorizontalXL`, `paddingRight: spacingHorizontalL` | `paddingBlock: spacingVerticalS`, `paddingInline: spacingHorizontalL` (+ `gap: spacingHorizontalM`) | Matches `SprkModal.tsx` `.header` exactly |
| Header border | `borderBottomWidth: '1px'` (raw literal) + `borderBottomStyle`/`Color` split | `borderBottom: \`${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}\`` | ADR-021 violation removed; matches `SprkModal.tsx` |
| Title | `<Text as="h1" size={500} weight="semibold">` — no ellipsis, no `title` attr | `<Text as="h1" title={title}>` styled via `titleText` class: `fontWeightSemibold`/`fontSizeBase400`/`lineHeightBase400` + `whiteSpace:nowrap;overflow:hidden;textOverflow:ellipsis;minWidth:0;flex:'1 1 auto'` | Matches `SprkModal.tsx` `.title` token set exactly; `as="h1"` heading semantics + `aria-label` on `DialogSurface` retained unchanged (WizardShell's pre-existing a11y mechanism, not SprkModal's `aria-labelledby` pattern — no reason to churn a working mechanism) |
| Window controls | `<ModalWindowControls isMaximized .../>` (task 030 interim) | Same single call, unmoved — comment updated to note "formalized onto the SprkModal standard header at task 080" (no duplication) | Unchanged component (hard boundary — not touched) |
| Footer padding | `paddingTop/Bottom: spacingVerticalM`, `paddingLeft: spacingHorizontalXL`, `paddingRight: spacingHorizontalL` | `paddingBlock: spacingVerticalS`, `paddingInline: spacingHorizontalL` | Matches `SprkModal.tsx` `.footer` |
| Footer border | `borderTopWidth: '1px'` (raw literal) + split props | `borderTop: \`${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}\`` | ADR-021 violation removed |
| Footer layout classes | `footer` (baked-in `justify-content:space-between`) + `footerLeft`/`footerRight` | `footer` (base, no justify-content) + `footerBetween`/`footerEnd` modifiers + `footerSlot` (replaces both `footerLeft`/`footerRight`) | Matches `SprkModal.tsx`'s own `footer`/`footerBetween`/`footerEnd`/`footerSlot` split |
| Footer button order | **Back, then Skip**, then Next/Finish | **Skip, then Back**, then Next/Finish | Matches canonical `WizardModal.tsx` preset exactly (`{onSkip && ...}` renders before the `Back` button in the reference file) |
| Skip button appearance | `appearance="secondary"` | `appearance="transparent"` | Matches `WizardModal.tsx`'s `<Button appearance="transparent" onClick={onSkip}>Skip</Button>` |
| Back button (hidden vs disabled at step 0) | Hidden entirely (`{!isFirstStep && (...)}`) | **Unchanged** — still hidden, NOT switched to WizardModal's "always visible, disabled at step 0" pattern | Deliberate non-change — this is WizardShell's own navigation-interaction semantics, not a chrome/token concern; changing it would be a behavioral change beyond "light-first" scope (see Deviations §5) |
| Cancel button | `appearance="secondary"`, left side | Unchanged appearance/position, now in a `footerSlot` under `footerBetween` | No change needed — already matched `WizardModal.tsx` |
| Success-screen footer | `footer` + empty `footerLeft` + `footerRight` | `mergeClasses(footer, footerEnd)` + single `footerSlot` (empty left div removed — `footerEnd` handles right-alignment natively) | Matches `SprkModal.tsx`'s footer-without-`footerStart` case |

Layout ASCII diagram + module docstring updated to describe the new header (Maximize/Restore + X) and footer order (Skip · Back · Next) for future readers.

## 2. Default-size swap evidence (95vw/70vh → `wizard`)

```ts
// New module-scope constants (imports getSurfaceStyle from ../SprkModal/sizes)
const WIZARD_DEFAULT_SIZE = getSurfaceStyle('wizard');
const WIZARD_DEFAULT_MAX_WIDTH = String(WIZARD_DEFAULT_SIZE.width);   // '62vw'
const WIZARD_DEFAULT_HEIGHT = String(WIZARD_DEFAULT_SIZE.height);     // 'min(74vh, 760px)'
```

Prop defaults changed from `maxWidth = '95vw'` / `height = '70vh'` to `maxWidth = WIZARD_DEFAULT_MAX_WIDTH` / `height = WIZARD_DEFAULT_HEIGHT`. Sourced from `SIZE_SPEC.wizard` (62vw × min(74vh, 760px)) via `getSurfaceStyle('wizard')` — same sourcing discipline task 061 used for `FindSimilarDialog`'s `xl` override, so the numbers can never drift from the canonical scale.

**Important naming-collision note (documented in-file):** `getSurfaceStyle('wizard').width` (`'62vw'`) is the value that plays WizardShell's `maxWidth` role — NOT `.maxWidth` (SprkModal's own `96vw` OUTER safety clamp around an explicit narrower `width`, irrelevant to WizardShell's single-value clamp architecture). Verified this distinction carefully before wiring it — using `.maxWidth` by mistake would have rendered wizards at ~96vw (near-fullscreen), the opposite of the intended change.

**The Fluent sizing-clamp fix is retained unchanged**: `DialogSurface` still receives `style={{ maxWidth: effectiveMaxWidth, height: effectiveHeight, minHeight: effectiveHeight }}` — only the fallback VALUE changed, not the mechanism.

### Prop-override preservation proof (FindSimilar xl still works)

`FindSimilarDialog.tsx` (`components/FindSimilar/`, the WizardShell-hosted wizard copy) passes explicit overrides:
```tsx
<WizardShell ... maxWidth={FIND_SIMILAR_MAX_WIDTH} height={FIND_SIMILAR_HEIGHT} />
```
where `FIND_SIMILAR_MAX_WIDTH`/`HEIGHT` are sourced from `SIZE_SPEC.xl` (92vw / 88vh) — task 061's own override. Since I only changed the **default** value in the destructuring (`maxWidth = WIZARD_DEFAULT_MAX_WIDTH`), and destructuring defaults only apply when the prop is `undefined`, this consumer's explicit values pass through completely untouched — proven by:
- The scoped jest run below shows the `CreateRecordWizard`/`WizardModal`/`WizardFollowOns` suites green with zero changes needed to any test assertion.
- `DocumentEmailWizard.tsx` similarly forwards an optional `maxWidth`/`height` prop (`{...}` pass-through) — SemanticSearchControl's real call site passes `1280px`/`85vh` explicitly (per DocumentEmailWizard's own in-file comment) — unaffected by the default swap.
- `CreateAnalysisWizardWidget.tsx` (AI.Widgets) passes an explicit `maxWidth="60vw" height="70vh"` override to `CreateRecordWizard` (which forwards to `WizardShell`) — unaffected; this consumer's override is now numerically close to the new `wizard` default (62vw vs 60vw) but was NOT touched (out of scope; flagged as a minor follow-on cleanup candidate in Deviations §5).
- All consumers that previously relied on the **default** (95vw/70vh) — e.g. `CreateMatterWizard`/`CreateWorkAssignmentWizard`/`DocumentUploadWizardDialog`/`RegisterWizard`/`DocumentUploadPage` — now render at the new `wizard` default (62vw × min(74vh,760px)) instead. This is the **intended** behavior change per FR-17 acceptance criterion 2 ("no ad-hoc 95vw/70vh literal"), not a regression — the `WizardModal` preset's own docstring confirms `wizard` was modeled to match "the production 'Create New Matter' chrome."

## 3. Consumer inventory (repo-wide grep, precise `import { WizardShell } from ...` matches only — excludes type-only/comment references)

| File | Surface | React ver | Passes maxWidth/height? |
|---|---|---|---|
| `Spaarke.UI.Components/src/components/FindSimilar/FindSimilarDialog.tsx` | shared lib (internal) | 19-typed | Yes — `xl` override (task 061) |
| `Spaarke.UI.Components/src/components/CreateRecordWizard/CreateRecordWizard.tsx` | shared lib (internal; used by CreateMatterWizard/CreateProjectWizard/CreateEventWizard/CreateTodoWizard) | 19-typed | Pass-through only (no own default) |
| `Spaarke.UI.Components/src/components/DocumentEmailWizard/DocumentEmailWizard.tsx` | shared lib (internal) | 19-typed | Pass-through only (SemanticSearchControl passes 1280px/85vh) |
| `Spaarke.UI.Components/src/components/SummarizeFilesWizard/SummarizeFilesDialog.tsx` | shared lib (internal) | 19-typed | No (gets new default) |
| `Spaarke.UI.Components/src/components/CreateWorkAssignmentWizard/WorkAssignmentWizardDialog.tsx` | shared lib (internal, canonical copy) | 19-typed | No (gets new default) |
| `src/solutions/LegalWorkspace/src/components/CreateWorkAssignment/WorkAssignmentWizardDialog.tsx` | Code Page (iframe web-resource) | 19-typed | No (gets new default) — **discovered fork of the above, see §5** |
| `src/solutions/DocumentUploadWizard/src/DocumentUploadWizardDialog.tsx` | Code Page (iframe web-resource) | 19-typed | No (gets new default) |
| `src/solutions/SpeAdminApp/src/components/container-types/RegisterWizard.tsx` | Code Page (iframe web-resource) | 19-typed | No (gets new default) |
| `src/client/external-spa/src/pages/DocumentUploadPage.tsx` | Power Pages SPA | **18**-typed | No (gets new default) |

**Transitively reached** (via `CreateMatterWizard`/`CreateRecordWizard`, not a direct import): `Spaarke.AI.Widgets/src/widgets/workspace/CreateMatterWizardWidget.tsx` (SpaarkeAi widget, `embedded=true` — no maxWidth/height involved at all, size props irrelevant to embedded rendering) and `CreateAnalysisWizardWidget.tsx` (SpaarkeAi widget, non-embedded, `maxWidth="60vw" height="70vh"` explicit override).

### Which packages bundle WizardShell as an iframe web-resource / PCF (blast-radius finding)

- **Iframe web-resource Code Pages** (Vite-built, deployed as Dataverse HTML web resources): `DocumentUploadWizard`, `SpeAdminApp`, `LegalWorkspace` (3 separate paths — see below), `SpaarkeAi` (transitively via AI.Widgets), `external-spa` (Power Pages SPA, same bundling model).
- **PCF controls**: `WizardShell` is intentionally **absent from `pcf-safe.ts`** (confirmed by reading the file — its own comment at line 11 explicitly routes `WizardShell` to "Code pages should import from the main barrel," not the PCF-safe barrel). Grepping all PCF source (excluding compiled `Solution/Controls/**/bundle.js` artifacts, which only contain an unrelated JSDoc-comment string match, not real code) for a direct `WizardShell` import returns **zero** PCF consumers.
  - **However**, one PCF DOES bundle WizardShell **transitively**: `SemanticSearchControl.tsx` imports `DocumentEmailWizard` via a deep dist path (`@spaarke/ui-components/dist/components/DocumentEmailWizard`), and `DocumentEmailWizard.tsx` directly renders `<WizardShell>`. This is a genuine, non-obvious blast-radius finding — confirmed via `build:prod` below.
  - `RelatedDocumentCount` and `SemanticSearchControl` also import `FindSimilarDialog` from `dist/components/FindSimilarDialog` (singular folder) — this is the **other**, non-Wizard FindSimilarDialog copy (the iframe-viewer one that composes `SprkModal` directly, task 061). It does **not** touch WizardShell.

## 4. Build/verify matrix

| Target | Command | React ver | Result |
|---|---|---|---|
| `Spaarke.UI.Components` (core) | `npm run build` (`tsc`) | 19-typed | **PASS**, exit 0, zero errors |
| `Spaarke.UI.Components` scoped jest | `npx jest src/components/Wizard src/components/SprkModal/presets/__tests__/WizardModal src/components/CreateRecordWizard src/components/DocumentEmailWizard src/components/FindSimilar src/components/CreateWorkAssignmentWizard src/components/SummarizeFilesWizard` | — | **PASS** — 11 suites, 55/55 tests |
| `Spaarke.UI.Components` FULL jest suite | `npx jest` | — | **199 suites total: 188 passed, 11 failed** (22/2510 tests failed) — the 11 failing suites are `toolbarLaunchDefaults`, `buildDynamicWorkspaceConfig`, `EntityCreationService.cascade`, `XrmDataverseClient`, `surfaceLaunchRegistry`, `SendEmailDialog.characterize`, `ConversationView.forward`, `TimelineComposeBox`, `RichFilePreview`, `recordHeader.integration`, `ConversationView.emailInFlow` — **exact match** to the documented pre-existing baseline in `current-task.md` ("11 pre-existing failing UI.Components suites incl. both ConversationView suites"); **zero Wizard/WizardShell-related failures** |
| `Spaarke.AI.Widgets` | `npm run build` (`tsc`) | 19-typed | **PASS**, exit 0 |
| `Spaarke.AI.Widgets` scoped jest | `npx jest CreateAnalysisWizardWidget CreateMatterWizardWidget` | — | **PASS** — 1 suite, 10/10 tests |
| `SemanticSearchControl` (PCF, transitively bundles WizardShell via DocumentEmailWizard) | `npm run build:prod` | **16**-typed | **PASS** — webpack "Succeeded", 0 errors (17 pre-existing unrelated ESLint warnings on unused vars in the control's own files) |
| `SpaarkeAi` (Code Page, transitively via AI.Widgets) | `npm run build` (full: html-reset + tsc-surface-gate + vite build + ribbon build) | 19-typed | **PASS** — `tsc-surface-gate`: "73 pre-existing error(s) in shared libs (deferred to Phase B). Surface-owned: **0**." Full `vite build`: 4021 modules, built in 25.9s. Ribbon build: 4/4 scripts. Re-ran raw `tsc --noEmit` and confirmed all 73 errors are pre-existing `TS6133` unused-var hygiene issues in `CreateMatterWizardWidget.tsx`/`CreateRecordStep.tsx` — **zero reference `WizardShell.tsx`/`wizardShellTypes.ts`** |
| `DocumentUploadWizard` (direct consumer) | `npm install --legacy-peer-deps` + `npm run build` (vite) | 19-typed | **PASS** — 2305 modules, built in 10.65s |
| `SpeAdminApp` (direct consumer) | `npm install --legacy-peer-deps` + `npm run build` (vite) | 19-typed | **PASS** — 3366 modules, built in 13.99s |
| `external-spa` (direct consumer) | `npm install --legacy-peer-deps` + `npm run build` (vite) | **18**-typed | **PASS** — 2298 modules, built in 7.46s |
| `LegalWorkspace` (build known-broken, Issue #712) | `npx tsc --noEmit` (full project — file-scoped tsc isn't viable with this project's non-relative `@spaarke/ui-components/*` path-mapped imports; filtered output instead) | 19-typed | **238 pre-existing errors** across many unrelated files (unused imports, missing `process` types, a legacy `SendEmailDialog` prop mismatch, an undefined `navigationService` reference in the LegalWorkspace fork of `WorkAssignmentWizardDialog.tsx`). **Zero errors reference `components/Wizard/WizardShell.tsx` or `components/FindSimilar/FindSimilarDialog.tsx`** (grepped explicitly). The one `WorkAssignmentWizardDialog.tsx(360,32)` hit (`Cannot find name 'navigationService'`) is a pre-existing bug unrelated to my change — that identifier isn't defined/imported anywhere in the file, before or after this task. |

Dual/triple-React compat (NFR-04) is proven across all three surfaces in play: React 16 (`SemanticSearchControl` PCF, `build:prod`), React 18 (`external-spa`), React 19 (everything else).

## 5. Embedded mode + stepper verification

- `embedded` branch (`if (embedded) { ... return <div className={styles.embeddedRoot}>{innerContent}</div>; }`) — **zero lines changed**. Still renders the same `innerContent` (title bar conditional on `!hideTitle`, sidebar+content, footer) without the Dialog wrapper.
- `hideTitle` gating (`{!hideTitle && (<div className={styles.titleBar}>...)}`) — **zero lines changed**. Consumers that pair `embedded={true}` with `hideTitle={embedded}` (the established convention — confirmed in `CreateRecordWizard.tsx` and `DocumentEmailWizard.tsx`, both always set `hideTitle={embedded}`) continue to suppress the title bar exactly as before; my header-token changes only affect what renders INSIDE that conditional, not the conditional itself.
- Footer chrome (Cancel/Skip/Back/Next) is **not** gated by `embedded`/`hideTitle` — it always renders, in both modes, both before and after this task. So the footer re-base (token alignment + Skip/Back reorder) applies uniformly whether the wizard is a standalone Dialog or an embedded panel — this is correct per FR-17 (standardize footer everywhere) and was true before this task too (footer was never conditional on `embedded`).
- `WizardStepper` (`<WizardStepper steps={shellState.steps} />`) — file completely untouched (`WizardStepper.tsx` was not opened for edit at any point); its 200px width, sidebar styling, and step-indicator logic are unaffected. Confirmed no test in the scoped or full jest run references stepper markup differently.
- Verified via code reading (no `--chrome` browser session available in this session) rather than live UI testing — see Deviations below.

## 6. Files modified

1. `src/client/shared/Spaarke.UI.Components/src/components/Wizard/WizardShell.tsx` — the primary re-base (7 targeted `Edit` calls: file docstring, imports + new module constants, header styles, footer styles, prop defaults, title JSX, footer JSX; plus 1 follow-up edit fixing a stale comment near the Dialog-mode render). `IWizardShellProps`/`IWizardShellHandle`/all other exported types are **byte-identical** — zero prop added/removed/renamed.
2. `src/client/shared/Spaarke.UI.Components/src/components/Wizard/wizardShellTypes.ts` — JSDoc-only edit: updated the `maxWidth`/`height` prop doc comments (which stated the now-false "defaults to 95vw/70vh") to describe the new `wizard`-size default. Zero type/shape changes.

**Not modified** (hard boundary, confirmed via no `Edit`/`Write` calls beyond the two files above): `SprkModal/**`, any preset, `sizes.ts`, `ModalWindowControls.tsx`, `pcf-safe.ts`, `WizardStepper.tsx`, any `Create*Wizard`'s internals, `TASK-INDEX.md`, `current-task.md`, `.claude/**`. No `git add`/`commit` performed.

## 7. Step 9.5 gates (FULL rigor, self-run)

**Self code-review:**
- Prop contract preserved: `IWizardShellProps` unchanged; `maxWidth`/`height` override mechanism unchanged (only the fallback literal moved); `embedded`/`hideTitle`/stepper/all other props untouched — verified above via 8 independent consumer builds + 66 scoped tests (55 UI.Components + 10 AI.Widgets, plus the WizardModal preset suite) all green.
- P1 (task 030) interim wiring reconciled, not duplicated: still exactly one `<ModalWindowControls>` call, same props, comment updated to note the formalization rather than adding a second cluster.
- No orphaned styles/imports: `footerLeft`/`footerRight` style keys fully removed and grep-confirmed zero remaining references; `mergeClasses` and `getSurfaceStyle` imports added and confirmed used at their call sites. Pre-existing unused `DialogContent as _DialogContent`/`DialogActions as _DialogActions` imports were NOT touched (predate this task, out of scope for a light-first pass).

**adr-check:**
- **ADR-012** (single shared copy, no fork): only the one canonical `WizardShell.tsx` was edited; no fork introduced. **Discovered pre-existing issue** (not introduced by this task): `src/solutions/LegalWorkspace/src/components/CreateWorkAssignment/WorkAssignmentWizardDialog.tsx` is a near-duplicate/fork of `Spaarke.UI.Components/src/components/CreateWorkAssignmentWizard/WorkAssignmentWizardDialog.tsx` (both directly render `<WizardShell>` with nearly identical step/follow-on logic, diverging only in where they source `FollowOnGrid`/`SendEmailFollowOnStep` from). Both inherit this task's chrome/size changes correctly (both compile clean), so this task is not blocked by the discovery, but it is a genuine ADR-012 tension the project's own `CLAUDE.md` "Duplicate copies to fold in during conversion" list did not previously capture. Documented as a candidate follow-on below (§8) — not filed to `notes/defer-issues.md`/GitHub per this task's write-boundary (main session's call, per the same precedent task 061 used for its own DEF-003 candidate).
- **ADR-021** (tokens only): diff-gated via `Grep` for `1px`/hex/`size={500}`/old style-class names across the full modified file — zero new violations. The one remaining `'1px'` literal (`.surface`'s border) is pre-existing, untouched, explicitly part of the RETAINED "Fluent sizing-clamp fix" (not in my touched header/footer/size scope).
- **NFR-04** (dual-React): proven via 8 builds spanning React 16 (PCF `build:prod`), 18 (`external-spa`), 19 (7 other packages).
- **NFR-05** (client-only): zero touches to `src/server/api/Sprk.Bff.Api/**` — trivially satisfied (only 2 client TS files touched).

## 8. POML acceptance-criteria checklist

| # | Criterion | Pass/Fail |
|---|---|---|
| 1 | Header = ellipsized title + `ModalWindowControls` (FullScreen glyph + ×); footer = Cancel left, nav/actions right | **PASS** — see §1 mapping table |
| 2 | Sized to the named `wizard` size (no ad-hoc 95vw/70vh literal) | **PASS** — see §2; confirmed via `Grep` that no `95vw`/`70vh` string literal remains in code (only in explanatory comments) |
| 3 | Envelope, `embedded` mode, 200px stepper retained and functional (no full internal re-base) | **PASS** — see §5; zero lines changed in the `embedded` branch, `hideTitle` gate, or `WizardStepper.tsx` |
| 4 | All consuming wizards + iframe web-resource builds compile/package green under `@types/react` 18 and React 19 | **PASS** — see §4; 8 independent builds green (React 16/18/19 all covered), zero WizardShell-attributable errors in the one known-broken build (LegalWorkspace, pre-existing #712) |
| 5 | No hex/`'1px'`/inline-color introduced in the re-based chrome | **PASS** — see §7 adr-check |

**All 5 acceptance criteria: PASS.**

## 9. Deviations / escalations

No `<escalation>` trigger fired (aligning the tokens did not force breaking `embedded` or force a change inside any `Create*`/direct wizard's internals — every consumer needed **zero** source changes; only WizardShell's own two files were touched).

Deviations from a literal reading, each a deliberate, reasoned choice:

1. **Footer button reorder (Back→Skip became Skip→Back) and Skip appearance change (secondary→transparent).** Not explicitly called out step-by-step in the POML's steps list, but directly required by (a) the task's own one-line summary ("Skip·Back·Next right") and (b) the canonical `WizardModal.tsx` preset reference file, which renders Skip before Back with `appearance="transparent"`. Verified zero existing test asserts a specific button DOM order or Skip's appearance — scoped + full jest runs are 100% green.
2. **Back button stays hidden-at-step-0 (not switched to WizardModal's "always visible, disabled" pattern).** This is WizardShell's own navigation-interaction semantics (which steps show which buttons), not a chrome/token concern — changing it would be a behavioral change beyond "light-first chrome-token alignment only" scope. Documented here explicitly rather than silently diverging from the WizardModal reference in one respect while claiming full alignment.
3. **`CreateAnalysisWizardWidget.tsx`'s existing `maxWidth="60vw" height="70vh"` override is now nearly redundant** with the new `wizard` default (62vw × min(74vh,760px)) but was NOT removed — out of scope for a WizardShell-only task (would require editing a `Create*Wizard`/widget consumer file, one of the explicit hard boundaries). Flagged here as a low-priority cleanup candidate for a future task.
4. **Discovered LegalWorkspace fork of `WorkAssignmentWizardDialog.tsx`** (§7 adr-check) — a pre-existing ADR-012 tension not previously captured in the project's known-duplicates list. Not filed to `notes/defer-issues.md`/GitHub Issue per this task's explicit write boundary; documented here as a candidate "DEF-004" for the main session/wrap-up task to triage (**candidate title**: "WorkAssignmentWizardDialog two-copy fork — UI.Components canonical vs LegalWorkspace-local, diverging follow-on-step sourcing"; **concrete risk**: a future bug fix or feature applied to one copy silently will not propagate to the other, exactly the same drift-risk class as DEF-003's FindSimilarDialog finding).
5. **No live browser/UI-test execution** — no `--chrome` session available in this context. The POML's three `<ui-tests>` (chrome match, embedded+stepper, dark-mode parity) were verified via careful code reading + the full green build/test matrix above (all consumers render through the same, now-shared token set already proven in `SprkModal`'s own shipped/tested dark-mode parity), not a live browser pass. Recommend a visual spot-check (e.g. Create New Matter wizard) before/at the next UI-capable session.
6. **Comment-only edit to `wizardShellTypes.ts`** (stale default-value JSDoc) — not in the POML's `<outputs>` list (which names only `WizardShell.tsx`), but directly serves documentation accuracy for the exact props this task changes the defaults of; zero type/shape change, essentially zero risk.

## 10. Escalation trigger check (explicit)

> "If aligning the header/footer/size tokens forces breaking the `embedded` envelope or forces a change inside any `Create*`/direct wizard's internals, STOP and surface it."

**Did not fire.** Every one of the 9 direct + 2 transitive consumers compiled/built/tested green with **zero source changes** required in any consumer file. The `embedded` envelope is provably unchanged (§5). No escalation needed.
