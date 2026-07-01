# Modal Decision Criteria — OOB `navigateTo` vs Proprietary Fluent v9 Dialog vs Browse Shell

> **Status**: Active (binding)
> **Created**: 2026-07-01
> **Last sharpened**: 2026-07-01 by `ai-spaarke-ai-workspace-UI-r2` task 023 (FR-15) — added Two-Layout Standard framing + verbatim MS Learn 2025-05-07 quote + 2026 CSP tightening fact.
> **Audience**: Anyone opening a record, document, form, wizard, confirm, or preview as a modal from any Spaarke client surface (Code Pages, PCF controls, ribbon commands, SPAs)
> **Companion**: [`.claude/patterns/ui/record-modal-selection.md`](../../.claude/patterns/ui/record-modal-selection.md) (pattern pointer) · [`docs/standards/DATA-ACCESS-DECISION-CRITERIA.md`](DATA-ACCESS-DECISION-CRITERIA.md) (sibling standard for data access decisions)

---

## Two-Layout Standard (added 2026-07-01 by R2)

Spaarke supports exactly **two** modal layouts across every surface. Everything else is either a variant, a wizard (separate concern), or an anti-pattern.

- **Layout 1 (canonical default)** — `Xrm.Navigation.navigateTo({ pageType: "entityrecord", entityName, entityId, formId? }, { target: 2, position: 1, width: {value: 85, unit: '%'}, height: {value: 85, unit: '%'} })`. OOB Dataverse form dialog at a **single fixed size (85% × 85%)** for every entity — do NOT vary per-entity. Used for every entity record row-click across Spaarke workspaces (Documents, Matters, Projects, Invoices, Work Assignments, Communications, To Do). The Spaarke DataGrid framework's `defaultRecordOpen` emits exactly this shape (see [`SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md`](../architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md)).
- **Layout 2 (justified exception)** — `RecordNavigationModalShell` + proprietary Fluent v9 content. Dimensions are content-driven, NOT the 85% × 85% Layout 1 standard. Reference case: [`RichFilePreviewDialog`](../../src/client/shared/Spaarke.UI.Components/src/components/FilePreview/RichFilePreviewDialog.tsx) for document preview (portrait shape `max-width: 1280px; height: 85vh` — matches PDF/Word paper aspect ratio). Layout 2 is justified by the "browse-in-context" or "content-shaped surface" cases; Layout 1 alone cannot serve them.
- **Retired anti-pattern (Layout 3)** — iframe-hosted OOB `main.aspx` inside a proprietary shell. Contractually unsupported by Microsoft (see anti-pattern §4 below). The `SmartTodoModal` (retired 2026-07-01 by R2 FR-14) was the last Spaarke consumer.

If you are opening a record and it does NOT fit Layout 1 or Layout 2, you are proposing new surface — surface a design conversation before shipping.

---

## TL;DR — Decision tree (≤30 seconds)

Read the question. Pick the first matching row.

| Question | Modal to use |
|---|---|
| User needs the **full OOB main form** — business rules, subgrids, native ribbon, form scripts, save/save-and-close as authored by the maker? | **OOB `navigateTo`** (`Xrm.Navigation.navigateTo({ pageType: "entityrecord" }, { target: 2 })`). No browse-in-context. Skip the rest. |
| User needs to **page across a collection of records** (documents, tasks, matters) without close/reopen — read AND/OR light-edit? | **Proprietary Fluent v9 Dialog + [`RecordNavigationModalShell`](../../src/client/shared/Spaarke.UI.Components/src/components/RecordNavigationModalShell/README.md)**. Compose per shell README quick start. |
| User needs to **preview a single document / file** with metadata sidebar and (optionally) 3-dot actions? | **[`RichFilePreviewDialog`](../../src/client/shared/Spaarke.UI.Components/src/components/FilePreview/RichFilePreviewDialog.tsx)** — already consumes the shell when nav props are supplied. Passes-through to browse when caller provides a collection. |
| User needs to **choose between 2–4 mutually exclusive options** (e.g. "Create matter / Create project / Cancel")? | **[`ChoiceDialog`](../../src/client/shared/Spaarke.UI.Components/src/components/ChoiceDialog/ChoiceDialog.tsx)** (ADR-023 pattern). |
| User needs a **multi-step wizard** with progressive disclosure (Create Matter, Create Todo, Work Assignment)? | **`WizardShell` from `@spaarke/legal-workspace`** (CreateRecordWizard family). NOT this doc's concern — see wizard patterns. |
| User needs a **simple yes/no confirm**? | Fluent v9 `Dialog` with two buttons. Do NOT wrap `ChoiceDialog` for this. |
| Default if no row above matched | **Ask.** Do not "just pick one." |

If the answer is a proprietary Fluent v9 dialog, load [`.claude/patterns/ui/fluent-v9-component-authoring.md`](../../.claude/patterns/ui/fluent-v9-component-authoring.md) and the theming/portal patterns before writing new dialog code.

---

## Why this document exists

Two modal families coexist across Spaarke surfaces:

- **OOB Dataverse form dialog** — `Xrm.Navigation.navigateTo` / `openForm` with `target: 2`. Renders the full Power Apps main form as a modal. Screenshot: standard Task / Event edit dialog with native ribbon + Save & Close.
- **Proprietary Fluent v9 dialog** — components in [`@spaarke/ui-components`](../../src/client/shared/Spaarke.UI.Components/): `RichFilePreviewDialog`, `RecordNavigationModalShell`, `ChoiceDialog`, `FindSimilarDialog`, `FilePreviewDialog` (deprecated). Screenshot: document preview with the "1 of 25" prev/next navigator.

Without explicit criteria, new code drifts toward whichever pattern the author saw last. The result is inconsistent record navigation UX (some surfaces let the user browse; others force close/reopen for every record), duplicated dialog chrome, and the excellent `RecordNavigationModalShell` infrastructure sitting unused because nobody knows it's there.

This document settles the question **before** the modal is coded. It pairs with:

- **[`.claude/patterns/ui/record-modal-selection.md`](../../.claude/patterns/ui/record-modal-selection.md)** — 25-line pointer the agent loads per-task
- **[`docs/standards/DATA-ACCESS-DECISION-CRITERIA.md`](DATA-ACCESS-DECISION-CRITERIA.md)** — the sibling `Xrm.WebApi` vs BFF decision (many modal-triggering commands ALSO need this decision)
- **The `RecordNavigationModalShell` README** — the authoritative component reference (props, dirty-check protocol, iframe-side contract)

---

## The three modal families (identified by name)

Every Spaarke modal falls into one of these three families. Know which family a task belongs to BEFORE you start coding.

### Family 1 — OOB Dataverse form dialog

**Entry point**: `Xrm.Navigation.navigateTo({ pageType: "entityrecord", entityName, entityId }, { target: 2, position: 1, width: {...}, height: {...} })`

**Renders**: the exact main form the maker authored — form scripts, business rules, subgrids, ribbon, native Save & Save & Close. Standard Power Apps chrome including the "expand to full page" icon in the top-right.

**Capabilities**:
- ✅ Full form fidelity (whatever the maker built, the user sees)
- ✅ All entity-level automation runs (plugins, workflows, business rules)
- ✅ Ribbon commands available
- ❌ NO cross-record browse — each record opens as its own dialog instance
- ❌ NO control over chrome, title, or action bar
- ❌ NO way to embed inside a proprietary side panel or workspace layout

**Use when**: the user needs the full editing experience the OOB form was designed for. If you find yourself replicating a form scripted by a maker, stop — use `navigateTo`.

### Family 2 — Proprietary Fluent v9 dialog (single record / non-record)

**Entry point**: any custom Fluent v9 `<Dialog>` in `@spaarke/ui-components` or a consumer.

**Canonical examples**:
- [`RichFilePreviewDialog.tsx`](../../src/client/shared/Spaarke.UI.Components/src/components/FilePreview/RichFilePreviewDialog.tsx) — single-doc mode when nav props omitted
- [`ChoiceDialog.tsx`](../../src/client/shared/Spaarke.UI.Components/src/components/ChoiceDialog/ChoiceDialog.tsx) — 2–4 rich choices
- [`FindSimilarDialog.tsx`](../../src/client/shared/Spaarke.UI.Components/src/components/FindSimilarDialog/) — near-fullscreen iframe of a Code Page

**Capabilities**:
- ✅ Full control of chrome, layout, action bar, sizing
- ✅ Fluent v9 theming + accessibility (WCAG 2.1 AA)
- ✅ Works inside Code Pages, PCF, SPA, Office Add-ins (per ADR-012)
- ❌ You build the content — no automatic form scripting, no ribbon

**Use when**: the content is a preview, a picker, a confirm, a custom UX surface, or an operation that doesn't map to a single Dataverse form.

### Family 3 — Proprietary Fluent v9 dialog + `RecordNavigationModalShell` (browse across a set)

**Entry point**: caller wraps [`RecordNavigationModalShell`](../../src/client/shared/Spaarke.UI.Components/src/components/RecordNavigationModalShell/README.md) inside a Fluent v9 `<Dialog>` (or hosts the shell in a `Xrm.Navigation.navigateTo` Code Page modal per FR-17 launch context).

**What the shell provides**: `<` / `>` navigator + "N of M" counter + title + optional action-bar slot + cross-frame dirty-check protocol (with 1000ms timeout fallback).

**What the shell does NOT own**: the modal envelope, the content, or the record source. Caller supplies:
- `currentIndex` + `navigationTotal` (from the caller's collection state)
- `onNavigate(dir)` (updates caller's state → child content rebuilds)
- Optional `dirtyCheckTargetWindow` (iframe `contentWindow`) if the child can veto navigation

**Capabilities**:
- ✅ Browse-in-context across a collection (documents, tasks, matters, events)
- ✅ Cross-frame dirty-check (unsaved-change prompt before navigation)
- ✅ Origin-validated postMessage protocol (default allow-list: `*.dynamics.com` + same-origin)
- ✅ Composable with proprietary content OR iframe-embedded surfaces
- ❌ Iframe-hosting an OOB `main.aspx` form for full editing is **unsupported territory** — see anti-pattern #4

**Use when**: the user wants to page through a set of records without close/reopen. This is the pattern the "1 of 25" browse UX in `RichFilePreviewDialog` uses.

---

## Decision criteria (the 5 dimensions)

Apply all 5 when the decision tree above does not give a clean answer.

### 1. Edit fidelity required

- **Full-form edit** (business rules, subgrids, all form scripts, ribbon commands): OOB `navigateTo`. Nothing else can match.
- **Light edit** (a handful of fields, straightforward save): Family 2 or 3. You author the form surface in Fluent v9 and use `Xrm.WebApi` (or BFF) to persist per the sibling data-access decision criteria.
- **Read only / preview**: Family 2 (single) or Family 3 (browse). Never `navigateTo` for read-only — it opens the full editable form which the user doesn't need.

### 2. Browse-in-context need

- **User will navigate a collection** (prev/next across ~5–100 items) as part of the same task: Family 3 mandatory. Do not force close/reopen for every record.
- **User opens one item at a time, closes, opens next from the list**: Family 1 or Family 2 is fine.
- **User needs both** — light-preview-with-browse AND deep-edit as an escalation: Family 3 for preview, with a top-right "Open full form" action that calls `navigateTo` to hand off to Family 1 for deep edit. This is the **hybrid pattern**.

### 3. Hosting surface

- **Code Page (SPA/React root)**: Any family. Family 3 works cleanly because the parent controls state.
- **PCF control**: Family 2 or 3 (dialog rendered inside PCF root); Family 1 via `context.navigation.openForm` for deep edit escalation.
- **Ribbon command → open a form as dialog**: Family 1 (native Xrm API is the natural fit).
- **Inside a workspace widget** (`SpaarkeAi`, LegalWorkspace dashboard): Family 2 or 3 (widgets do not typically launch OOB modals directly; if they need to, they hand off to Family 1 as an escalation).

### 4. Consistency with adjacent UX

- If the surrounding surface uses Fluent v9 chrome (SpaarkeAi widget, Code Page workspace), an OOB `navigateTo` modal will visually clash. Prefer Family 2 or 3.
- If the surrounding surface is a native MDA form (record page, dashboard), OOB `navigateTo` is visually consistent. Family 1 is the natural choice.

### 5. Support-surface risk

- OOB `main.aspx` iframes: **NOT officially supported** by Microsoft. Works today, can break on platform updates. Do not use iframe-hosted OOB forms as a standard — see anti-pattern #4.
- `Xrm.Navigation.navigateTo` dialog: fully supported, versioned in the Client API reference.
- Fluent v9 proprietary dialogs: fully supported, we own them.

---

## Worked example 1 — Family 1 (OOB `navigateTo`)

**Surface**: MDA record form; user clicks "Add To Do" ribbon command from a matter.
**Need**: create a new `sprk_event` (todoflag=true) with full form fidelity — the maker has authored business rules on the Event form (regarding validation, date defaults, priority cascading).

```js
// Ribbon command handler (JS web resource)
Xrm.Navigation.navigateTo(
  { pageType: "entityrecord", entityName: "sprk_event", formType: 2 /* create */ },
  { target: 2, position: 1, width: { value: 60, unit: "%" }, height: { value: 80, unit: "%" } }
);
```

**Why Family 1** (mapped to the criteria):

1. **Edit fidelity** — full-form edit is the entire point; the form scripts and business rules are what make the Event form usable.
2. **Browse-in-context** — no; user creates one Event at a time from a ribbon command.
3. **Hosting** — ribbon command runs in host MDA context; `Xrm.Navigation` is the native API.
4. **Consistency** — surrounding surface IS the MDA record form; native chrome matches.
5. **Support** — fully-supported API.

**When NOT to use this pattern**: if the user is browsing a matter's Events list and wants to view/edit them in sequence, escalate to Family 3 with a Family 1 "Open full form" hand-off.

---

## Worked example 2 — Family 3 (Proprietary + `RecordNavigationModalShell`)

**Surface**: LegalWorkspace matter dashboard; user clicks a document tile in the Documents widget.
**Need**: preview the document with metadata sidebar, prev/next browse across the 25 documents on the matter, close-only from the dialog (no deep edit here — the user views, browses, closes).

```tsx
// Consumer wraps RichFilePreviewDialog, which internally composes RecordNavigationModalShell
<RichFilePreviewDialog
  open={isOpen}
  onClose={handleClose}
  document={documents[currentIndex]}
  navigationTotal={documents.length}
  currentIndex={currentIndex}
  onNavigate={(dir) => setCurrentIndex(dir === 'next' ? currentIndex + 1 : currentIndex - 1)}
/>
```

**Why Family 3** (mapped to the criteria):

1. **Edit fidelity** — read/preview only; no editing needed at this level.
2. **Browse-in-context** — critical; the user wants to page across the 25 documents without close/reopen.
3. **Hosting** — SpaarkeAi widget → Code Page → Fluent v9 dialog. Family 3 composes cleanly.
4. **Consistency** — surrounding surface is Fluent v9; OOB chrome would clash.
5. **Support** — fully-supported proprietary component.

**Extension to hybrid**: to support deep-edit-from-preview, add an "Open full form" button in the shell's `actionBar` slot that calls `Xrm.Navigation.navigateTo` to hand off to Family 1. User browses lightly, escalates to full form when they need to edit.

---

## Worked example 3 — Family 2 (`ChoiceDialog`, no browse)

**Surface**: Corporate Workspace "New" button — user must pick what to create.
**Need**: present 3 choices (Matter / Project / Cancel) with icons, titles, descriptions. No navigation, no record surface.

```tsx
<ChoiceDialog
  open={open}
  onClose={onClose}
  title="Create new record"
  options={[
    { id: 'matter', icon: <BriefcaseRegular />, title: 'Matter', description: 'Legal engagement…' },
    { id: 'project', icon: <FolderRegular />, title: 'Project', description: 'Secure workspace…' },
  ]}
  onChoose={(id) => launchWizard(id)}
/>
```

**Why Family 2** (mapped to the criteria):

1. **Edit fidelity** — not a record surface; picker only.
2. **Browse-in-context** — no; a single selection.
3. **Hosting** — Code Page workspace; Fluent v9 chrome.
4. **Consistency** — matches surrounding chrome; ADR-023 pattern.
5. **Support** — owned component.

---

## Hybrid pattern — proprietary browse + OOB escalation

The user's canonical ask: **"browse records in context without close/reopen for lightweight viewing, escalate to full OOB form when the user actually needs to edit."**

The recommended composition:

1. Family 3 (`RecordNavigationModalShell`) hosts the browse UX. Prev/next chevrons + counter + a **proprietary lightweight form** (read/light-edit) as the child content.
2. The shell's `actionBar` slot renders an **"Open full form"** button.
3. Clicking it calls `Xrm.Navigation.navigateTo(...)` to launch Family 1 (the OOB form) for the current record.
4. On close of the OOB modal, the caller optionally refetches the current record's fields to reflect edits made in Family 1 back into the browse pane.

This composition avoids the unsupported iframe-hosted-OOB path (anti-pattern #4) while still giving the user a "browse then escalate" flow.

**Alternative**: if the entity is high-value and the user does most editing in-context, invest in building a proprietary form surface for that entity inside Family 3 — a well-authored Fluent v9 form for the top ~3–5 browseable entities (Documents, To Dos, Matters, Events) beats an iframe.

---

## Anti-patterns

These are the failure modes this document exists to prevent.

### 1. Do not use OOB `navigateTo` when browse-in-context is the actual UX need

If the user's task is "page through documents on this matter" and you open each one via `navigateTo`, you have forced the close/reopen pattern that Family 3 exists to eliminate. Use `RecordNavigationModalShell` — it is already built, tested, and shipped.

### 2. Do not rebuild the "1 of N + prev/next" chrome per surface

`RecordNavigationModalShell` provides it. Composing it takes a dozen lines. Rebuilding it per-surface fragments UX, duplicates accessibility work, and misses the dirty-check protocol.

### 3. Do not use `ChoiceDialog` for yes/no confirms

Use a plain Fluent v9 `Dialog` with two buttons. `ChoiceDialog` is for 2–4 rich options with icon + title + description (per ADR-023). A yes/no confirm has no icons and no descriptions — the visual weight is wrong.

### 4. Do not iframe-embed OOB `main.aspx` as a standard pattern

Microsoft's model-driven-apps iframe/web-resource documentation states verbatim (revision **2025-05-07**, [use-iframe-and-web-resource-controls-on-a-form](https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/use-iframe-and-web-resource-controls-on-a-form)):

> "Displaying a form within an IFrame embedded in another form is not supported"

This is a **support-contract statement**, not a Content-Security-Policy one — it applies regardless of same-origin passthrough. Separately, the model-driven CSP admin doc (revision **2026-02-10**, [content-security-policy](https://learn.microsoft.com/en-us/power-platform/admin/content-security-policy)) enforces `Content-Security-Policy: frame-ancestors 'self' https://*.powerapps.com` by default; strict-mode CSP for code apps rolled out **late January 2026** (per 2tolead's Power Apps CSP 2026 Setup Guide). The direction of travel is toward tightening, not relaxing, third-party embedding.

If a project needs iframe-OOB-in-shell as a temporary bridge, treat it as a **stopgap**: document the risk in the project spec's ADR Tensions section (per CLAUDE.md §6.5), name the migration path, and set a review date. Prefer Layout 1 (`Xrm.Navigation.navigateTo`) — that IS the supported path for opening a Dataverse record as a modal from Code Pages / PCFs / SPAs. The `SmartTodoModal` (retired 2026-07-01 by ai-spaarke-ai-workspace-UI-r2 FR-14) was Spaarke's last iframe-hosted-OOB consumer; the evidence trail lives in [`projects/ai-spaarke-ai-workspace-UI-r2/notes/researcher-iframe-main-aspx-2026-07-01.md`](../../projects/ai-spaarke-ai-workspace-UI-r2/notes/researcher-iframe-main-aspx-2026-07-01.md).

### 5. Do not launch OOB `navigateTo` from within a Fluent v9 Dialog

Nested modals from different chrome families create a confusing z-order / focus / dismissal experience. If a Fluent v9 dialog needs to escalate to an OOB form, **close the Fluent v9 dialog first**, then call `navigateTo`. On OOB close, the caller can reopen the Fluent v9 dialog if needed with the fresh state.

### 6. Do not add token snapshots or auth state to a modal component's props

Per ADR-028 invariants (also cited in [`DATA-ACCESS-DECISION-CRITERIA.md`](DATA-ACCESS-DECISION-CRITERIA.md) anti-pattern #4), pass `authenticatedFetch` as a function dependency where the modal needs to reach the BFF. Never snapshot tokens in props/state.

---

## Verification — 3 scenarios

The three checks below validate the criteria reach the right answer:

### Scenario A — Ribbon command on Matter form: "Add Task to To Do"

- Edit fidelity? Full main form needed (business rules on Event form).
- Browse-in-context? No.
- Hosting? Ribbon command in host MDA.
- Consistency? Native MDA chrome surrounds.
- Support? Native API.

**Verdict: Family 1 (`Xrm.Navigation.navigateTo`).** Screenshot 2 in the original conversation was correct usage for this scenario. **BUT** the modal in the screenshot showed a broken subgrid ("Something went wrong") — that's an implementation bug, not a modal-choice bug.

### Scenario B — Corporate Workspace: browse the 25 documents on a matter

- Edit fidelity? Read/preview.
- Browse-in-context? Critical.
- Hosting? Fluent v9 Code Page.
- Consistency? Fluent v9 surrounds.
- Support? Owned.

**Verdict: Family 3 (`RichFilePreviewDialog` + `RecordNavigationModalShell`).** Screenshot 1 in the original conversation was correct usage for this scenario.

### Scenario C — Corporate Workspace: user browses To Dos on a matter and needs to edit one

- Edit fidelity? Light edit in-context + occasional full edit.
- Browse-in-context? Yes.
- Hosting? Fluent v9 Code Page.
- Consistency? Fluent v9 surrounds; escalation to OOB is opt-in.
- Support? Owned + native for escalation.

**Verdict: Hybrid pattern.** Family 3 hosts a proprietary Fluent v9 form for the To Do; the shell's `actionBar` renders an "Open full form" button that closes the shell and calls `Xrm.Navigation.navigateTo` for deep edit.

---

## Cross-links

- **Record browser shell (component reference)** — [`RecordNavigationModalShell/README.md`](../../src/client/shared/Spaarke.UI.Components/src/components/RecordNavigationModalShell/README.md) — authoritative component doc including dirty-check protocol and iframe-side contract
- **Pattern pointer** — [`.claude/patterns/ui/record-modal-selection.md`](../../.claude/patterns/ui/record-modal-selection.md)
- **Sibling data-access decision** — [`DATA-ACCESS-DECISION-CRITERIA.md`](DATA-ACCESS-DECISION-CRITERIA.md) (often paired: modal-triggering commands also decide `Xrm.WebApi` vs BFF)
- **Shared-lib boundary** — [`ADR-012`](../../.claude/adr/ADR-012-shared-component-library.md) (shell components live in `@spaarke/ui-components`, NOT duplicated per solution)
- **Fluent v9 constraint** — [`ADR-021`](../../.claude/adr/ADR-021-fluent-ui-v9.md) (all modals use Fluent v9 exclusively; no v8)
- **Choice-dialog pattern** — [`.claude/patterns/ui/choice-dialog-pattern.md`](../../.claude/patterns/ui/choice-dialog-pattern.md) (Family 2 / ADR-023)
- **Fluent v9 portal gotcha** — [`.claude/patterns/ui/fluent-v9-portal-gotcha.md`](../../.claude/patterns/ui/fluent-v9-portal-gotcha.md) (dialog portal behavior)
- **Anti-patterns catalog** — [`docs/standards/ANTI-PATTERNS.md`](ANTI-PATTERNS.md)

---

*Maintained by the project owner. Updates that change the decision tree or add a new modal family MUST add a row to [`.claude/CHANGELOG.md`](../../.claude/CHANGELOG.md).*
