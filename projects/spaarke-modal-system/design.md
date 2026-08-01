# Spaarke Modal System — Design Document

> **Created**: 2026-07-31
> **Status**: Design — **Mode-1 prototype validated 2026-07-31**; ready for `/design-to-spec`
> **Author**: investigation + synthesis session 2026-07-31
> **Predecessors / inputs**:
> - [`docs/standards/MODAL-DECISION-CRITERIA.md`](../../docs/standards/MODAL-DECISION-CRITERIA.md) (2026-07-01) — the *decision* layer this project builds the *component* layer beneath.
> - Owner UAT 2026-07-31 item 4 — "we are standardizing to use the expand/collapse buttons and 'x' on all modals; these should be in the shared UI components" (already produced [`ModalWindowControls`](../../src/client/shared/Spaarke.UI.Components/src/components/ModalWindowControls/ModalWindowControls.tsx)). This project generalizes that same-day mandate into a full modal system.
> - Owner UAT 2026-07-22 / 2026-07-30 — mid-size modal rectangle + `modalType="alert"` decisions embedded in `EmailComposer`.
> - [`projects/spaarke-iframe-wizard-pattern-enhancement/design.md`](../spaarke-iframe-wizard-pattern-enhancement/design.md) — related project; relationship analyzed in §9.

---

## 0. Decisions locked by the Mode-1 prototype (2026-07-31)

A greenfield UX spike (`c:/code_files/spaarke-prototype/projects/2026-07-sprk-modal-system/`, run under owner review at 1080p/1440p/2560) resolved every visual open question. These are binding for the spec:

- **Name (locked)**: base component is **`SprkModal`** (matches the `Sprk*` family); presets are `ConfirmModal` / `ChoiceModal` / `FormModal` / `PreviewModal` / `BrowseModal` / `WizardModal`. (Earlier working name "ModalShell" in this doc = `SprkModal`.) → closes §11-A.
- **Size scale**: the 7-size scale confirmed. **`md`/`lg` heights must be capped** — `min(72vh, 720px)` / `min(85vh, 880px)` — or they read square on tall (1440p/2560) monitors. Verified fixed. → closes §11-D.
- **Footer**: **Cancel always left** (`footerStart`), navigation/primary actions **right**, `space-between`. Right slot holds multiple buttons (Skip · Back · Next). Matches the production "Create New Matter" wizard.
- **Window controls**: use the **Dataverse expand/collapse glyph** (`FullScreenMaximize`/`FullScreenMinimize`) + `×`, not four-corners `ArrowMaximize`. Reconcile the shipped `ModalWindowControls` in P1.
- **Centering**: Fluent `Dialog` + portal-above-transform is transform-robust (verified against a transformed ancestor). Keep Fluent `Dialog` as the envelope; P5 is a validation, not a rewrite. → closes §11-F.
- **Scale mechanism**: **scaled Fluent theme**, NOT CSS `zoom`. Zoom under-scaled the portaled `position:fixed` dialog at 4K (owner saw theme fonts larger than zoom); scaled theme changes the tokens the dialog reads, so it is portal-independent and correct. → resolves the §11-I mechanism (ownership of the app-shell control stays open).
- **Body scroll**: **modern native thin scrollbar** (Fluent 2 / Win11 look), which is the Microsoft Fluent standard (native scroll + thin/overlay bar; Fluent reserves chevron scroll buttons for *horizontal* carousels/overflow, never vertical content). The up/down-chevron overlay was prototyped and **rejected** (covers content, no position/length cue, non-standard). "Lazy load" = a separate content/virtualization concern, not a modal-frame feature.
- **OOB vs custom**: `SprkModal` by default for everything; OOB `navigateTo` (85%×85%) reserved for the full maker-authored main form; hybrid "Open full form" escalation from a light-edit `SprkModal`.

Full decision log: the prototype's `README.md` "Decisions" section.

---

## 1. Problem statement

Spaarke opens modals from **eight distinct surfaces** — wizards, file preview, email records, message records, quick-start / "New" menus, OOB entity main forms (ribbon commands), dataset grids, and code-page wrappers (Smart To Do, SpaarkeAi). Across these surfaces there are **two supported modal families** plus one **de-facto anti-pattern family**:

1. **OOB Dataverse modal** — `Xrm.Navigation.navigateTo({ pageType }, { target: 2, … })` / `openForm`. Microsoft renders the chrome; we only choose the size.
2. **Custom Fluent v9 modal** — React `Dialog` / `DialogSurface` / `Drawer` components in the shared libraries and consumers.
3. **Hand-rolled overlay** (anti-pattern) — `position: fixed; inset: 0` divs or raw `createElement` that reinvent the surface, backdrop, header, and dismissal.

We already have a **decision layer** that says *which family* to use ([`MODAL-DECISION-CRITERIA.md`](../../docs/standards/MODAL-DECISION-CRITERIA.md), the "Two-Layout Standard", ADR-021, ADR-023). **What we do not have is a canonical component / design-system layer** — a single shell that unifies the *envelope, sizing, header, footer, window controls, theming, and naming* for the custom family. As a result every custom dialog rebuilds its own chrome, and the results have drifted badly:

- **≥6 different "large modal" rectangles** are in the tree, and even the *declared* standard is contested — one comment calls `720px × 70vh` the "recurring mid-size across ~6 dialogs", then overrides it to `1040px × 72vh` because 720 "read as portrait." `NewThreadModal` independently also landed on `1040 × 72vh`. Two "canonical" mid-sizes that disagree.
- **~6 incompatible header patterns** (plain `DialogTitle` no-X · custom title bar with X · `DialogTitle` action-slot X · `RecordNavigationModalShell` nav header · renderer title + 3-dot menu · `ModalWindowControls` cluster).
- **Directly contradictory close rules**: `SendEmailDialog` *removed* its header X in v1.1.59 "for consistency across our shared modals", while `FindSimilarDialog`, `FilePreviewDialog`, `WizardShell`, `CloseProjectDialog`, and `EmailComposer` all *keep* one.
- **The Fluent v9 width-clamp bypass** (`block-size: fit-content`) is copy-pasted with near-identical comments in ≥3 components instead of being solved once.
- **Maximize/restore** — mandated 2026-07-31 for *all* modals — exists in exactly **one** component (`EmailComposer`, via `ModalWindowControls`) and a *second, different* way (`WizardShell`'s CSS `resize: both`). The shared `ModalWindowControls` primitive is adopted by **1 of ~13** dialogs.
- **Three hand-rolled overlays** bypass Fluent entirely (`ActionConfirmationDialog`, the Messages `ConversationModal`, and `sprk_DocumentOperations.js`), each reinventing surface + backdrop + dismissal.
- **OOB launch sizes** fragment across `85% / 80% / 70% / 60% / fixed-px`, spread over ~4 launcher hubs plus PCF/ribbon/solution-local bypasses.

This is the exact failure mode CLAUDE.md §11 (Component Justification — default to reuse) exists to prevent, playing out one dialog at a time. This project stops it.

## 2. Goals

1. **A canonical modal shell** in `@spaarke/ui-components` that owns the envelope + standard header + body + footer, parameterized by a **named size** and **named layout (landscape / portrait)**, composing the primitives that already exist (`RecordNavigationModalShell`, `ModalWindowControls`, `OrientationToggle`, the `WizardShell` sizing-clamp fix).
2. **A canonical design guide** (a standards doc) that defines: standard sizes with exact dimensions, standard header/footer layout + placement + styling, light/dark rules, standard **names** so any surface can say "use a `md` landscape `FormModal`", and the wiring recipe for building a feature on the shell.
3. **A resolved OOB-vs-custom boundary** — extend `MODAL-DECISION-CRITERIA.md` with the component layer; keep OOB (Layout 1) as OOB; centralize OOB launch sizes so "which OOB size" is also standardized.
4. **A conversion plan + inventory** — map every existing modal to a target preset, phased by risk, retiring the hand-rolled overlays and the duplicate chrome.
5. **Accessibility + theming parity by construction** — WCAG 2.1 AA, semantic tokens only, no hardcoded colors / `'1px'` literals, transform-robust centering, light/dark automatic.

### Non-goals

- **Not** replacing OOB `navigateTo` for full-form edit (Layout 1 stays OOB — see §5). The shell is the implementation of the *custom* family.
- **Not** a new BFF surface. This is client-only; publish-size impact is nil (no `Sprk.Bff.Api` change → CLAUDE.md §10 not triggered).
- **Not** Power Automate / Dataverse plugins (core-product constraint, shared with the iframe project).
- **Not** rebuilding the `PaneEventBus` or the cross-iframe mount transport — that is the sibling project (§9).
- **Not** a visual redesign of what's *inside* each modal — this standardizes the *frame*, not the content.

## 3. Current-state inventory

### 3.1 The two supported families + the anti-pattern

| Family | Entry point | Chrome owner | Standard size today |
|---|---|---|---|
| **OOB (Layout 1)** | `Xrm.Navigation.navigateTo({pageType:"entityrecord"}, {target:2})` | Microsoft | 85% × 85% (dataset grid row-open); drifts to 80/70/60 elsewhere |
| **Custom Fluent v9 (Layout 2 + all confirms/forms/previews/wizards)** | shared `@spaarke/ui-components` dialogs | us | fragmented (see §3.3) |
| **Hand-rolled overlay** (anti-pattern → retire) | `position:fixed`/`createElement` divs | us, badly | ad hoc |

### 3.2 Shared primitives that already exist (the nucleus to standardize on)

| Primitive | Path | What it already gives us | Adoption |
|---|---|---|---|
| **`RecordNavigationModalShell`** | [`…/RecordNavigationModalShell/`](../../src/client/shared/Spaarke.UI.Components/src/components/RecordNavigationModalShell/RecordNavigationModalShell.tsx) | Header (ellipsized title + `‹`/`›` + "N of M" tabular-nums counter + `actionBar` slot + `Divider`) + content slot + built-in discard-confirm dialog + cross-frame dirty-check protocol. **Strictest "semantic tokens only" discipline.** Deliberately does **not** own the envelope. | RichFilePreviewDialog, SmartTodoWidget |
| **`ModalWindowControls`** | [`…/ModalWindowControls/`](../../src/client/shared/Spaarke.UI.Components/src/components/ModalWindowControls/ModalWindowControls.tsx) | The blessed maximize/restore + close (×) cluster (owner mandate 2026-07-31). Both handlers optional; renders nothing when unused. | **1 of ~13** (EmailComposer only) |
| **`OrientationToggle`** | [`…/OrientationToggle/`](../../src/client/shared/Spaarke.UI.Components/src/components/OrientationToggle/OrientationToggle.tsx) | Horizontal↔vertical layout toggle (the raw material for a `landscape`/`portrait` affordance). | SmartTodoWidget |
| **`WizardShell`** | [`…/Wizard/WizardShell.tsx`](../../src/client/shared/Spaarke.UI.Components/src/components/Wizard/WizardShell.tsx) | Most complete envelope today: Dialog + title bar + close X + 200px stepper sidebar + footer nav + success screen + **`embedded` no-envelope mode** + configurable `maxWidth`/`height` **with the Fluent clamp-bypass already solved.** | all wizards |
| **`RichFilePreview` (renderer/envelope split)** | [`…/FilePreview/RichFilePreview.tsx`](../../src/client/shared/Spaarke.UI.Components/src/components/FilePreview/RichFilePreview.tsx) | Proven pattern: layout-agnostic renderer + thin modal wrapper, so content mounts modal *or* inline. | FilePreview widgets |

**Key insight:** a canonical shell essentially **already exists in pieces** — `RecordNavigationModalShell` (chrome) + `ModalWindowControls` (window buttons) + `WizardShell`'s inline-sizing clamp-bypass (sizing). The work is composing them into **one envelope-owning shell** with an agreed size scale, then retrofitting the bespoke dialogs.

### 3.3 Custom-dialog chrome variance (the motivation, quantified)

| Component | Path | Envelope | Size | Header | Footer | Notes |
|---|---|---|---|---|---|---|
| ChoiceDialog | [`ChoiceDialog`](../../src/client/shared/Spaarke.UI.Components/src/components/ChoiceDialog/ChoiceDialog.tsx) | Dialog | Fluent default ~600 | title, **no X** | Cancel only | ADR-023 |
| FindSimilarDialog (iframe) | [`FindSimilarDialog`](../../src/client/shared/Spaarke.UI.Components/src/components/FindSimilarDialog/FindSimilarDialog.tsx) | Dialog | 85vw × 85vh | custom bar + expand + X | none | |
| SendEmailDialog (legacy) | [`SendEmailDialog`](../../src/client/shared/Spaarke.UI.Components/src/components/SendEmailDialog/SendEmailDialog.tsx) | Dialog | 520px × auto | title, **X removed v1.1.59** | Cancel+Send | superseded |
| SendEmailDialog (EmailComposer) | [`EmailComposer/wrappers/SendEmailDialog`](../../src/client/shared/Spaarke.UI.Components/src/components/EmailComposer/wrappers/SendEmailDialog.tsx) | Dialog | **1040 × 72vh, maximizable** | `ModalWindowControls` | engine | canonical email; `modalType="alert"` |
| RichFilePreviewDialog | [`RichFilePreviewDialog`](../../src/client/shared/Spaarke.UI.Components/src/components/FilePreview/RichFilePreviewDialog.tsx) | Dialog | **1280 × 85vh** | nav-shell OR renderer bar | Close only | preferred preview |
| FilePreviewDialog | [`FilePreviewDialog`](../../src/client/shared/Spaarke.UI.Components/src/components/FilePreview/FilePreviewDialog.tsx) | Dialog | 880 × 85vh | bar + X + Toolbar | none | **`@deprecated`**, `'1px'` literals |
| NewThreadModal | [`NewThreadModal`](../../src/client/shared/Spaarke.UI.Components/src/components/NewThreadModal/NewThreadModal.tsx) | Dialog | min(1040,95vw) × 72vh | title | Cancel⟷Create | "page-mounted + centered" |
| ComposeConflictDialog | [`ComposeConflictDialog`](../../src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeConflictDialog.tsx) | Dialog(alert) | Fluent default | title | 3 buttons wrap | non-dismissible |
| PinnedMemoryEditDialog | [`PinnedMemoryEditDialog`](../../src/client/shared/Spaarke.AI.Widgets/src/components/memory/PinnedMemoryEditDialog.tsx) | Dialog(modal) | form minWidth 420 | title | Cancel+Save | |
| PinnedMemoryDeleteConfirmation | [`PinnedMemoryDeleteConfirmation`](../../src/client/shared/Spaarke.AI.Widgets/src/components/memory/PinnedMemoryDeleteConfirmation.tsx) | Dialog(alert) | Fluent default | title + warn icon | Cancel+Delete | |
| CloseProjectDialog | [`CloseProjectDialog`](../../src/client/shared/Spaarke.UI.Components/src/components/CreateProjectWizard/CloseProjectDialog.tsx) | Dialog | 520 / 90vw | title + action-slot X | phase-dependent | **inline button color styles** |
| WizardShell (+ all wizards) | [`WizardShell`](../../src/client/shared/Spaarke.UI.Components/src/components/Wizard/WizardShell.tsx) | Dialog / embedded | 95vw × 70vh, `resize:both` | custom bar + X | Cancel⟷Back/Skip/Next | `'1px'` literals |
| ActionConfirmationDialog | [`ActionConfirmationDialog`](../../src/client/shared/Spaarke.UI.Components/src/components/SprkChat/ActionConfirmationDialog.tsx) | **hand-rolled div** | 480 / 90% | own header | own actions | reinvents surface+backdrop |
| ConversationModal | [`ConversationModal`](../../src/client/pcf/CommunicationConversationPanel/CommunicationConversationPanel/ConversationModal.tsx) | **hand-rolled overlay** | min(1040,95vw)×72vh / 96vw×94vh | hand-coded | — | abandoned Fluent Dialog over a **transform-ancestor centering bug** |
| CalendarDrawer | [`CalendarDrawer`](../../src/client/shared/Spaarke.Events.Components/src/components/CalendarSection/CalendarDrawer.tsx) | OverlayDrawer | 340px end | DrawerHeader + X | none | only Drawer in scope |

Plus the popover tier (`MessageQuickView` 360px, `AiSummaryPopover` 480×400) — a lightweight non-modal family we keep but bring under the same token/naming discipline.

### 3.4 Consumer inventory by surface (the eight surfaces)

| # | Surface | Predominant family today | Representative call sites |
|---|---|---|---|
| 1 | **Wizards** | OOB `navigateTo(webresource)` @ **60%×70%** → iframe SPA hosting `WizardShell` | [`wizardLaunchers.ts`](../../src/client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/wizardLaunchers.ts), each `src/solutions/Create*Wizard/` |
| 2 | **File preview** | custom Fluent v9 (`RichFilePreviewDialog` + `RecordNavigationModalShell`) @ **1280×85vh** | [`RichFilePreviewDialog`](../../src/client/shared/Spaarke.UI.Components/src/components/FilePreview/RichFilePreviewDialog.tsx); PCF + widget consumers |
| 3 | **Email records** | mixed: OOB code-page @ **80%×80%**; OOB full-form @ **85%×85%**; custom compose (`SendEmailDialog`) @ **1040×72vh** | [`openEmailRecord.ts`](../../src/client/shared/Spaarke.UI.Components/src/components/EmailComposer/openEmailRecord.ts), [`OpenFullFormButton`](../../src/client/shared/Spaarke.Communication.Components/src/components/EmailComposeActions/OpenFullFormButton.tsx) |
| 4 | **Message records** | **hand-rolled `ConversationModal`**; custom `NewThreadModal`; OOB create @ **70%×80%** | [`ConversationModal`](../../src/client/pcf/CommunicationConversationPanel/CommunicationConversationPanel/ConversationModal.tsx), [`launchCreate.ts`](../../src/client/shared/Spaarke.Communication.Components/src/logic/actions/launchCreate.ts) |
| 5 | **Quick start / New menu** | custom Fluent v9 `Dialog` → wizard launchers | [`QuickStartModal`](../../src/solutions/SpaarkeAi/src/components/conversation/QuickStartModal.tsx), [`GetStartedExpandDialog`](../../src/solutions/LegalWorkspace/src/components/GetStarted/GetStartedExpandDialog.tsx) |
| 6 | **OOB main forms (ribbon)** | OOB `navigateTo`/`openForm` (+ one **hand-rolled DOM overlay**) | [`sprk_event_ribbon_commands.js`](../../src/solutions/EventCommands/sprk_event_ribbon_commands.js), [`sprk_DocumentOperations.js`](../../src/client/webresources/js/sprk_DocumentOperations.js) |
| 7 | **Dataset grids** | OOB `navigateTo` **unified @ 85%×85%** (best-behaved surface) | [`DataGrid.tsx` `buildRecordOpenNavArgs`](../../src/client/shared/Spaarke.UI.Components/src/components/DataGrid/DataGrid.tsx) |
| 8 | **Code-page wrappers** | OOB `navigateTo` @ 85% / 80%; in-app PaneEventBus mount (not a modal) | [`SmartTodoApp`](../../src/solutions/SmartTodo/src/SmartTodoApp.tsx), [`ConversationPane` `handleSurfaceLaunch`](../../src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx) |

**OOB launch hubs already exist** and should become the *only* sanctioned entry points: `xrmNavigationServiceAdapter` (records/dialogs; `openRecordModal` = 85%×85%) and `wizardLaunchers.ts` + `launchSurface.ts` (wizards / OOB create forms). Most size fragmentation lives in surfaces that **bypass** these (PCF `NavigationService`, raw ribbon JS, the two solution-local `navigation.ts` copies @ 80%).

## 4. Design principles

1. **Compose, don't create** (CLAUDE.md §11). The canonical shell *extends* `RecordNavigationModalShell` + `ModalWindowControls` + the `WizardShell` sizing fix. We do not introduce a parallel abstraction. Every existing bespoke dialog becomes a thin consumer or a preset — net component count goes **down**.
2. **One envelope, named sizes, named layouts.** A finite, documented size scale + `landscape`/`portrait` orientation. No component invents its own rectangle.
3. **One header contract, one footer contract.** Title left; `ModalWindowControls` (maximize + X) top-right on *every* modal (per the 2026-07-31 mandate); action buttons live in the footer. This resolves the "remove X vs keep X" contradiction in favor of **keep, standardized**.
4. **Renderer / envelope split** (the `RichFilePreview` pattern) is formalized so any content mounts modal *or* inline.
5. **Semantic tokens only, host-provided theme.** No hex, no `'1px'` literals, no inline color styles. Light/dark is inherited from the host `FluentProvider` (already the one consistent thing — preserve it). Standalone iframe hosts (wizards) keep mounting their own provider via `resolveCodePageTheme()`.
6. **Transform-robust centering.** The shell must center correctly even when an ancestor has a CSS `transform` (the bug that drove `ConversationModal` and `NewThreadModal` to "page-mounted + centered"). This is a hard requirement of the canonical envelope, not an afterthought.
7. **OOB stays OOB.** The shell never tries to reproduce a maker-authored main form. Layout 1 remains `navigateTo`; we only centralize its size.
8. **Accessibility is the default path.** Focus trap, ESC (except `alert`), announced title, `aria-modal`, keyboard-operable window controls — inherited from Fluent `Dialog` + the shell, so consumers can't accidentally ship an inaccessible modal.
9. **Responsive + scale-aware.** Every dimension is fluid (`min(px, vw)` + `vh`, cap-and-center) *and* expressed through a single UI-scale multiplier (`--sprk-ui-scale`) so the shell inherits app-level "make it bigger" on 2K/4K+ displays without clipping (§6.9). No fixed device-pixel assumptions.

## 5. OOB Dataverse modals vs custom modals — the boundary

This project does **not** collapse the two families; it draws a crisp line and standardizes *within* each.

**Use OOB (`navigateTo`, Layout 1) when** — the user needs the full maker-authored main form (business rules, subgrids, native ribbon, form scripts, Save & Close), or a record row-click in a grid. This is Microsoft's supported path for opening a Dataverse record as a modal from Code Pages / PCF / SPAs. The canonical *custom* shell must **not** try to replace it (iframe-hosting `main.aspx` is unsupported — `MODAL-DECISION-CRITERIA.md` anti-pattern #4).

**Use the canonical custom shell when** — the content is a preview, picker, confirm, light-edit form, a browse-across-a-set surface, a wizard, or any Fluent v9 surface that doesn't map to a single maker form. This is everything in §3.3.

**What this project adds to the boundary:**
- The custom shell is the concrete **implementation** of Layout 2 (today only informally embodied by `RichFilePreviewDialog`).
- **OOB launch sizes get standardized too.** Even though Microsoft owns OOB chrome, *we* choose the size. Today that's a 85/80/70/60/px spread across ≥4 hubs + bypasses. The standard names a small set (proposal in §6.3) and routes **all** OOB launches through the two sanctioned hubs, deprecating the solution-local `navigation.ts` copies and PCF/ribbon one-offs.
- `MODAL-DECISION-CRITERIA.md` gains a "component layer" section pointing at the shell + size scale, so the decision doc and the implementation stay in lockstep.

## 6. Proposed canonical modal system

### 6.1 Naming + component architecture

A **single base shell** + a small set of **presets**. Working names (final naming is Open Question §11-A):

```
ModalShell            ← base: owns Fluent Dialog envelope + standard header + body + footer,
  │                     parameterized by `size`, `layout`, `dismiss`, header/footer slots.
  │                     Composes ModalWindowControls (header) + the WizardShell sizing fix.
  ├─ ConfirmModal      ← yes/no + destructive/alert variant. Replaces ad-hoc confirms.
  ├─ ChoiceModal       ← existing ChoiceDialog, re-based on ModalShell (ADR-023 preserved).
  ├─ FormModal         ← light-edit form surface (title + fields + Cancel/Save footer).
  ├─ PreviewModal      ← content-shaped landscape surface (RichFilePreview re-based).
  ├─ BrowseModal       ← ModalShell + RecordNavigationModalShell (N-of-M, Layout 2).
  └─ WizardModal       ← WizardShell re-based on ModalShell header/footer/size tokens +
                          ModalWindowControls + retained `embedded` mode.
```

Each preset is a *thin* configuration of `ModalShell`, not a fork. `RecordNavigationModalShell`, `ModalWindowControls`, and `OrientationToggle` remain as internal primitives the shell consumes.

### 6.2 Canonical build resolution + standard size scale

**Device target (the canonical resolution we build and QA to).** Spaarke users run company-issued 15" laptops (many Macs) and 27" monitors — not high-end panels. Layout is driven by *effective* (CSS) resolution, not the physical panel, so OS scaling is what matters. The observed band:

| Class | Typical effective (CSS) resolution | Role |
|---|---|---|
| 15.6" Windows 1080p @125–150% · 13–15" Mac default | **1280×720 → 1536×864** | Floor + baseline |
| 27" 1080p/1440p @100% | 1920×1080 → 2560×1440 | Upper reference |

- **Canonical baseline (build + screenshot QA): `1440 × 900` CSS px** — the median laptop / Mac default. If a modal looks right here, it is right for the bulk of users.
- **Supported floor (hard constraint — nothing clips; footer + primary button always visible without scrolling the chrome): `1280 × 720` CSS px.**
- **Upper reference: `2560 × 1440` CSS px** — fixed-width modals must not look lost; full-bleed prose must not stretch to unreadable line lengths.

**This decides the sizing *technique*, not just the numbers.** Across a 1280→2560 span, pure `vw`/`vh` breaks both ends (an `85vw` modal is 1088px on the laptop but 2176px on the 27" — line lengths blow out). So the standard is **`width: min(fixedPx, N·vw)` + `height: N·vh`**, the fixed cap held ≤ ~92% of the floor width so it never clips. Solving this once in the shell also retires the copy-pasted Fluent `block-size: fit-content` bypass.

**Standard size scale** (derived from the target above; resolves the `720 vs 1040` mid-size conflict in favor of `md = 1040`, since 720 "read as portrait"):

| Name | Width (clamped) | Height | Layout | Use | Replaces today |
|---|---|---|---|---|---|
| `xs` | `min(480px, 92vw)` | content | portrait | confirms, deletes, HITL | ChoiceDialog default, PinnedMemory*, ActionConfirmationDialog, ComposeConflictDialog |
| `sm` | `min(560px, 92vw)` | auto | portrait | simple form / single choice | SendEmailDialog(legacy), CloseProjectDialog |
| `md` | `min(1040px, 92vw)` | `min(72vh, 720px)` | landscape | forms, compose, quick-start | EmailComposer, NewThreadModal, QuickStartModal, ConversationModal |
| `lg` | `min(1280px, 94vw)` | `min(85vh, 880px)` | landscape | rich content + sidebar (preview) | RichFilePreviewDialog |
| `xl` | `92vw` (near-full) | `88vh` | landscape | near-full iframe / app host (content self-lays-out) | FindSimilarDialog |
| `full` | `100vw` | `100vh` | — | maximized state of any size | (via `ModalWindowControls`) |
| `wizard` | 60% × 70% (OOB dialog dim) | — | landscape | iframe-deployed wizard web resources | all `sprk_*wizard` launches |

On the `1280` floor: `md`=1040 (81% width, comfortable), `lg`=1203 (94vw, ~38px side margins), `xl`=1178 — all fit with the footer visible at `≤88vh`. On the `2560` 27": `md`/`lg` hold their px cap (not stretched); `xl`/iframe hosts stay near-full because their content re-lays-out internally. **The `min(N·vh, maxPx)` height cap on `md`/`lg` is load-bearing** — without it, `72vh`/`85vh` on a 1440p/2560 panel grows tall enough to make the fixed-width modal read *square* (prototype-verified 2026-07-31). `portrait` vs `landscape` is an explicit prop driving aspect + the body's internal grid (e.g. preview's `1fr 320px`); `OrientationToggle` is the user affordance where a surface supports both.

### 6.3 OOB size scale (parallel, for `navigateTo`)

| Name | Dimensions | Use |
|---|---|---|
| `record` | 85% × 85% | entity record modal (Layout 1 — already the DataGrid standard) |
| `create-form` | 70% × 80% | OOB create form escalation |
| `wizard` | 60% × 70% | wizard web-resource dialog |

Everything currently at 80% collapses into `record` (85%) or `create-form` (70/80) with a one-time review. All OOB launches route through `xrmNavigationServiceAdapter` / `wizardLaunchers`; solution-local `navigation.ts` copies are deprecated.

### 6.4 Standard header

`justify-content: space-between`, bottom border `strokeWidthThin` / `colorNeutralStroke2`:
- **Left**: optional back/nav cluster (`‹`/`›` + "N of M" for `BrowseModal`) · ellipsized **title** (`Text weight="semibold"`) · optional status/subtitle.
- **Right**: optional per-surface actions (e.g. 3-dot menu, `OrientationToggle`) · **`ModalWindowControls`** (maximize/restore + **X**) — present on **every** modal per the 2026-07-31 mandate. Use the **Dataverse expand/collapse glyph** (`FullScreenMaximize20Regular` / `FullScreenMinimize20Regular`) to match the native OOB dialog, **not** `ArrowMaximize` (the shipped shared component's current icon — reconcile in P1).

The title-source rule (who renders it when a nav shell is present) is codified so we never get the "double header" the preview dialog had to guard against.

### 6.5 Standard footer (action bar)

Top border `strokeWidthThin` / `colorNeutralStroke2`, padding `spacingVerticalS` / `spacingHorizontalL`, `space-between` (prototype-locked 2026-07-31):
- **Cancel is ALWAYS left-aligned** (`footerStart` slot). Navigation / primary actions are **right-aligned** (`footer` slot). This is the standard for every modal, not just a variant — it matches the production "Create New Matter" wizard.
- The right slot holds **any number of buttons** — `Skip · Back · Next · Finish` for wizards, `Cancel⟷Save` for forms, `Close` for preview.
- **Destructive** primary uses `appearance` + a documented danger token class — **not** inline `backgroundColor` styles (the `CloseProjectDialog` anti-pattern).
- Footer may be **omitted** when window controls + inline actions suffice, but that is a shell option, not a per-surface reinvention.

### 6.6 Light / dark + theming

- No `FluentProvider` inside the shell — it portals into the host provider (the single consistent behavior today; preserved).
- Semantic tokens only; a lint/review gate bans hex, `'1px'` literals, and inline color styles in modal components.
- Standalone iframe hosts (wizards, code pages) continue to wrap their root in `FluentProvider theme={resolveCodePageTheme()}` + the storage listener — unchanged.

### 6.7 Dismiss semantics

Standardized `dismiss` prop: `light` (backdrop + ESC, default) · `explicit` (`modalType="modal"`, no light-dismiss) · `alert` (`modalType="alert"`, no ESC/backdrop — for destructive/blocking, e.g. compose conflict, delete-confirm, active email compose). Replaces the current ad-hoc mix (light vs alert vs non-dismissible vs hand-rolled ESC listener).

### 6.8 Wiring — how a feature is built on the shell

```tsx
// A light-edit form modal — the whole chrome is one preset.
<FormModal
  open={open}
  onClose={onClose}
  title="Edit pinned memory"
  size="sm"              // named size
  layout="portrait"
  dismiss="explicit"
  onSubmit={handleSave}  // renders the standard Cancel/Save footer
  submitLabel="Save changes"
>
  {/* just the fields — no Dialog, no header, no footer, no window controls */}
  <PinnedMemoryForm … />
</FormModal>
```

```tsx
// Browse-across-a-set (Layout 2) — envelope + nav shell in one.
<BrowseModal
  open={open} onClose={onClose}
  size="lg" layout="landscape"
  title={doc.name}
  currentIndex={i} navigationTotal={docs.length} onNavigate={go}
>
  <RichFilePreview … />
</BrowseModal>
```

The consumer supplies **content + intent**; the shell supplies **all chrome**. This is the property none of the ~13 current dialogs have.

### 6.9 Responsive behavior + scaling up on 2K / 4K+ monitors

**The reframe: "everything looks tiny on 4K" is an OS-scaling issue, not a resolution issue.** The browser lays out in *CSS pixels*; `devicePixelRatio` (DPR) reports the physical-to-CSS ratio. At OS defaults a 4K 27" runs at 150% (Windows) or a Retina "looks-like" mode (macOS), presenting as **~1920–2560 CSS px** and rendering *crisper*, not smaller. Apparent size only collapses when a panel is run at **100% native** — a deliberate OS choice. DPR gives us crispness for free (sharp SVG icons + text); OS scaling gives apparent size. So for the ~95% on OS defaults, 2K/4K already looks right and the §6.2 scale applies directly.

**Responsive is mandatory** and already in the sizing technique (`min(px, vw)` + `vh` + cap-and-center) — it solves *too wide*. It does **not** solve *too small* for the native-100%-4K case; that needs a scale lever.

**The scale lever — one multiplier, applied twice.** Pure fluid `rem` typography does **not** work here: Fluent v9 tokens are fixed px (`tokens.spacingHorizontalM`, `fontSizeBase300`, the modal chrome), so a root-font trick scales custom text but leaves Fluent components unchanged. Instead, drive everything from one `uiScale` value:

- **Our layout** reads a CSS variable: `width: min(calc(1040px * var(--sprk-ui-scale, 1)), 92vw)`. The `vw` cap still protects against overflow, so scaling up can never clip.
- **Fluent internals** get a **scaled theme** — numeric token values × the same factor — so buttons/inputs/spacing/text grow in lock-step.

**Mechanism locked to the scaled theme (NOT CSS `zoom`), prototype-verified 2026-07-31.** CSS `zoom` was prototyped and **rejected**: it does not reliably reach a portaled `position:fixed` dialog, so at 4K the modal *under-scaled* (owner observed scaled-theme fonts larger than zoom at the same %). The scaled theme changes the actual token values the dialog reads, so it scales correctly regardless of where the dialog portals. The one tradeoff — a handful of non-token fixed px inside Fluent won't scale — is minor and fixable per-component if it ever shows.

One `uiScale` value drives both layers, set by (a) an **auto large-viewport breakpoint** (e.g. `≥2560` CSS px → `1.15`) and (b) a **user-overridable** "Display size: Default / Large / Extra-large" persisted via the existing code-page theme-storage pattern.

**Scope:** UI-scale is an **app-shell** concern (SpaarkeAi, LegalWorkspace, each code page), not modal-only — the modal shell just *inherits* `--sprk-ui-scale` + the scaled theme from its host. This project's obligation is to be **scale-compatible**: express every size via `min(calc(px * var(--sprk-ui-scale)), vw)` and assume no fixed device pixel. Ownership of the app-shell scale control is Open Question §11-I.

### 6.10 Body scroll — modern native thin scrollbar (the Fluent standard)

**Standard: a modern native thin scrollbar** on the body (`overflow-y: auto`, `scrollbar-width: thin`, thin rounded inset thumb that darkens on hover, `scrollbar-gutter: stable` to avoid layout shift) — the Fluent 2 / Windows 11 look. This is also the **Microsoft Fluent convention**: Fluent v9 `Dialog` scrolls its content natively with the platform's thin/overlay scrollbar, and reserves chevron scroll buttons for *horizontal* movement only (`Carousel`; horizontal item overflow uses an overflow **menu**, `@fluentui/react-overflow`) — never vertical content.

An **up/down-chevron overlay was prototyped and rejected** (owner review 2026-07-31): floating chevrons cover the last line of content, give no position/length cue, and are a non-standard learned affordance. Left/right chevrons are literally the Fluent *carousel* pattern (horizontal), which is why they don't fit a vertically-scrolling body.

**"Lazy load" is a separate concern.** Virtualizing / incrementally loading a genuinely long list is a *content* responsibility (its own shared `ScrollArea`/virtualized list used inside `SprkModal` and in grids/panes alike) — not a feature of the modal frame. The frame just scrolls natively.

## 7. Deliverables

1. **Component**: `ModalShell` + presets (`ConfirmModal`, `ChoiceModal`, `FormModal`, `PreviewModal`, `BrowseModal`, `WizardModal`) in `@spaarke/ui-components`, with the sizing-clamp fix centralized and `ModalWindowControls` wired in by default.
2. **Standards doc**: `docs/standards/MODAL-DESIGN-SYSTEM.md` — the canonical design guide (sizes, layouts, header/footer, theming, names, wiring recipe, do/don't). Sibling to `MODAL-DECISION-CRITERIA.md`, which gets a "component layer" cross-link.
3. **Pattern pointer**: `.claude/patterns/ui/modal-shell.md` (25-line agent pointer) + update `record-modal-selection.md`.
4. **ADR**: either extend ADR-021 or add a focused ADR ("canonical modal shell") — Open Question §11-C. ADR-023 (`ChoiceDialog`) is preserved by re-basing.
5. **Conversion**: the ~13 bespoke dialogs + 3 hand-rolled overlays migrated (§8), OOB launches consolidated to two hubs.
6. **Tests**: shell render/chrome/sizing/dismiss/maximize + a11y snapshot; per-preset behavior.

## 8. Migration / conversion plan

Phased by risk; each phase is independently shippable. Ordered so quick wins and the mandated window-controls rollout come first, hand-rolled overlays (highest risk) come after the shell is proven.

| Phase | Scope | Targets | Risk |
|---|---|---|---|
| **P0 — Build** | `ModalShell` + size tokens + presets + standards doc + ADR + pattern pointer. No consumer changes yet. | new shell in `@spaarke/ui-components` | low (additive) |
| **P1 — Window-controls rollout** (satisfies 2026-07-31 mandate) | Drop `ModalWindowControls` into every existing dialog's header via an interim adapter, ahead of full re-basing. | all ~13 custom dialogs | low, high-visibility |
| **P2 — Confirms & choices** | Re-base onto `ConfirmModal` / `ChoiceModal`; **retire `ActionConfirmationDialog`** hand-rolled overlay. | ChoiceDialog, ComposeConflictDialog, PinnedMemory×2, CloseProjectDialog, ActionConfirmationDialog | low |
| **P3 — Forms & compose** | Re-base onto `FormModal`; resolve mid-size to `md` (1040×72vh); retire legacy `SendEmailDialog`. | NewThreadModal, SendEmailDialog×2, PinnedMemoryEditDialog, QuickStartModal | medium |
| **P4 — Preview & browse** | Re-base onto `PreviewModal` / `BrowseModal`; retire `@deprecated` `FilePreviewDialog`. | RichFilePreviewDialog, FilePreviewDialog, FindSimilarDialog(iframe) | medium (many consumers) |
| **P5 — Messages overlay** | Replace hand-rolled `ConversationModal` with `md` `ModalShell` — **requires the transform-robust centering** (§4.6) that drove the hand-roll. Validates the shell against its hardest case. | ConversationModal | high |
| **P6 — Wizards** | Re-base `WizardShell` internals on `ModalShell` header/footer/size tokens (keep `embedded` mode + stepper). Coordinate with §9. | WizardShell + all `Create*`/direct wizards | high (blast radius) |
| **P7 — OOB launch consolidation** | Route all `navigateTo` through the two hubs; apply the OOB size scale; deprecate solution-local `navigation.ts` + PCF/ribbon one-offs; convert the `sprk_DocumentOperations.js` DOM overlay to a supported path. | ~85–100 OOB call sites, ribbon JS | medium |

**Conversion mechanics** (per CLAUDE.md §11 + §3 sub-agent boundary): shared-lib components change in `@spaarke/ui-components`; because most surfaces already consume the shared dialogs, a re-based preset propagates to consumers on rebuild. PCF ↔ Code Page React-version drift ([[feedback_shared-lib-react-version-tension]]) is a known hazard for any shared-lib UI change — the shell must compile clean under both `@types/react` 18 (PCF) and 19 (Code Page).

## 9. Relationship to `spaarke-iframe-wizard-pattern-enhancement`

**Finding (2026-07-31): that project is largely overtaken by events, and is *not* a dependency of this one.**

Its focus is **cross-context mount transport** — getting a "mount a workspace tab" signal into SpaarkeAi's `PaneEventBus` from five surfaces *outside* its React tree (sibling iframe wizards, MDA main forms, BFF background jobs, external SPAs, Office add-ins), since `useDispatchPaneEvent()` is a no-op outside `<PaneEventBusProvider>`. It was drafted **2026-05-27**, *before* two mechanisms shipped that cover its flagship surfaces:

| Iframe-project surface | Status now | Covered by |
|---|---|---|
| **1. Iframe wizards → mount / return result** | **substantially solved** | Assistant surface-launch: `launchSurface` writes a `sessionStorage` hand-off envelope, awaits `navigateTo`, reads the **outcome** back on modal close; in-app tabs mount via `PaneEventBus widget_load`. ([ASSISTANT-SURFACE-LAUNCH-MECHANISM.md](../../docs/architecture/ASSISTANT-SURFACE-LAUNCH-MECHANISM.md), 2026-07-22) |
| **3. BFF background jobs → push** | **superseded** | The **Notification & Action Spine** ([ADR-047](../../.claude/adr/ADR-047-notification-action-spine.md)): outbox row → SignalR ping → suggestion card. The surface-launch doc explicitly says *"do not build a second push channel"* — which is exactly what the iframe project's BFF mount-pending Cosmos queue (its Phases 3/6) proposed. |
| **2. MDA form → SpaarkeAi (other tab)** | **still a gap** | none — the one genuinely remaining need |
| **4. External SPAs** | still a gap (niche) | none |
| **5. Office add-ins** | still a gap (niche) | none |

**So**: the iframe project's problem framing is valid, but ~two-thirds of its proposed build is now either shipped (surfaceHandoff) or **forbidden** (a second push channel — the sanctioned path is now "produce a Notification-Spine signal", not a new BFF queue). What remains (Surfaces 2/4/5 — "Open in Workspace" from non-Assistant, cross-tab/cross-origin surfaces) is narrower and lower-priority, and where built should ride the Notification Spine + `BroadcastChannel`, not the original transport layer.

**Recommendation**: **close or re-scope the iframe project** (Open Question §11-E) — it is not worthwhile as originally written, and it is **not a dependency** of this modal project. The only intersection is wizard *chrome*, which this project owns end-to-end via the `WizardModal` preset (P6). The shared `60%×70%` launch constant both once touched is centralized here in P7. As a bonus, a clean in-process `WizardModal` (P6) would make the iframe project's one still-relevant idea — Option 5, in-process wizard migration for Surface 2 — cheaper if it is ever re-scoped; but that is a convenience, not a coupling.

## 10. Constraints, ADRs, governance

- **ADR-012** — shell lives in `@spaarke/ui-components`; not duplicated per solution.
- **ADR-021** — Fluent v9 exclusively, semantic tokens only. This project *strengthens* it (bans `'1px'` literals + inline color styles in modals).
- **ADR-023** — `ChoiceDialog` pattern preserved via `ChoiceModal` re-base.
- **ADR-028** — no token snapshots in modal props; pass `authenticatedFetch` as a function (already a `MODAL-DECISION-CRITERIA.md` rule).
- **CLAUDE.md §10 (BFF hygiene)** — **not triggered**; client-only, zero BFF/publish-size impact. State this explicitly in the spec's Placement Justification (the addition is to a shared *client* lib, not `Sprk.Bff.Api`).
- **CLAUDE.md §11 (Component Justification)** — this project is the poster case *for* reuse: it reduces net component count and forbids the next bespoke dialog.
- **Core-product constraint** — no Power Automate / plugins (inherited from the iframe project's operator direction).
- **Accessibility** — WCAG 2.1 AA is a gate, not a nicety.

## 11. Open questions (for `/design-to-spec`)

- **A. Naming — ✅ RESOLVED (2026-07-31):** base = **`SprkModal`**; presets `ConfirmModal` / `ChoiceModal` / `FormModal` / `PreviewModal` / `BrowseModal` / `WizardModal`.
- **B. One shell or a family?** Confirm the base-shell + thin-presets model (recommended) vs a single mega-component with a `variant` prop. Presets read better at call sites and keep bundles tree-shakeable.
- **C. ADR strategy.** Extend ADR-021 with a modal section vs a new focused ADR. Recommendation: new concise ADR ("Canonical Modal Shell") that references ADR-021/023, so the decision is greppable.
- **D. Mid-size — ✅ RESOLVED (2026-07-31):** `md = min(1040px, 92vw) × min(72vh, 720px)` (height cap added so it holds landscape aspect on tall monitors). A genuine 720-portrait need uses `sm`.
- **E. Iframe-project disposition.** Given §9, **close it as superseded** (recommended), or re-scope it to just Surfaces 2/4/5 built on the Notification Spine + `BroadcastChannel`. Either way it is not a blocker for, or dependency of, this project.
- **F. Transform-robust centering — ✅ RESOLVED (2026-07-31):** keep Fluent `Dialog` — its portal escapes a transformed ancestor and centers correctly (verified). P5 is a validation, not a rewrite. (Invariant: the portal mount must sit above any transformed ancestor — it does, at the `FluentProvider` root.)
- **G. Wizard re-base depth (P6).** Full internal re-base of `WizardShell` on `ModalShell` vs only aligning tokens/size/window-controls while leaving the wizard envelope intact. Blast radius (all `Create*` wizards + iframe builds) argues for the lighter option first.
- **H. OOB size consolidation scope.** Is 80%→85% (record) / 80%→70-80 (create-form) acceptable UX for the surfaces currently at 80%, or do some need to stay? One-time visual review during P7.
- **I. UI-scale — mechanism ✅ RESOLVED (2026-07-31):** scaled Fluent theme, not CSS `zoom`. **Still open:** *ownership* — build **scale-compatible sizing** here (mandatory), and land the app-shell scale control + auto-breakpoint as a small sibling task or a Phase 0.5.

## 12. Preliminary phasing summary

P0 build → **P1 window-controls (mandate)** → P2 confirms → P3 forms → P4 preview/browse → P5 messages overlay (hardest custom case) → P6 wizards (coordinate w/ §9) → P7 OOB consolidation. Phases P0–P4 are low/medium risk and deliver the bulk of the consistency win; P5–P7 are the high-blast-radius tail.

---

*End of design document. Advance via `/design-to-spec` once Open Questions §11 A–H are directionally settled.*
