# Spaarke Templates R8 — Unified Template System (Compose Word + Email), Notes-Free Storage

> **Project**: `spaarkeai-compose-templates-r8`
> **Created**: 2026-08-13 · **Author**: Ralph Schroeder + Claude (Opus 4.8)
> **Status**: DESIGN — **first draft** (split out of `spaarkeai-compose-r7` on 2026-08-13; hand-authored input to `/design-to-spec` → `/project-pipeline`)
> **Split rationale**: what began as R7's "Templates tab" use case (UC-1) is really a **cross-surface template subsystem** — storage, merge model, a picker, *and* it should also cover **email templates**. That is bigger than R7's editor-UX scope, so it is its own project. R7 keeps the editor UX (save/autosave/hotkeys/PDF-import); R8 owns templates end-to-end.
> **Governing constraints**: root CLAUDE.md §10 (BFF Hygiene), §11 (Component Justification), ADR-050 (modal shell), [ASSISTANT-SURFACE-LAUNCH-MECHANISM.md](../../docs/architecture/ASSISTANT-SURFACE-LAUNCH-MECHANISM.md) (template → Compose tab).
> **Research grounding**: [`notes/dataverse-template-storage-findings.md`](notes/dataverse-template-storage-findings.md).

---

## 1. Why this project exists

Spaarke has **three half-overlapping template mechanisms** and no coherent system:

1. **Compose Word templates** — R6 built `ComposeTemplateSource`, but stores the `.dotx` as a **Note
   (annotation) attachment** on the OOB `template` entity. **This org does not use OOB Notes/Activities**,
   so that storage is unacceptable. The Compose empty-state "Open template" button today just mounts a
   single hard-coded HTML scaffold (`COMPOSE_BLANK_TEMPLATE_HTML`) — there is no real picker.
2. **Email templates** — `EmailTemplateService` reads the OOB **email `template` entity** (`{!field}`
   slugs, HTML body); the `EmailComposer` (Lexical) already has a working template picker against it.
3. **Native `documenttemplate`** (OOB Word templates) — env-locked content-control model, rejected.

Users need **one** template experience: pick a template card → it opens as an editable document (Compose)
or fills the email body (email composer), with a **handful of merge fields** (client, matter, date, today),
stored in a way that **fits Spaarke's conventions (no Notes)** and is **portable across environments**.

---

## 2. THE DECISION (anchor — do not re-litigate)

**Custom entity + File column + `{{token}}` merge.** (Full rationale + the rejected alternatives with
sources: [`notes/dataverse-template-storage-findings.md`](notes/dataverse-template-storage-findings.md).)

- **Storage** = a custom Dataverse entity with a **File column** for the payload — **independent of the
  Notes/annotation table**. `.dotx` for Word templates; HTML/body for email templates. Backend fetch via
  `GET .../{entity}({id})/{filecolumn}/$value`.
- **Merge** = the existing **`{{token}}`** substitution (`WordTemplateService` for OOXML,
  `EmailTemplateService`/`ITemplateEngine` for HTML). No content controls; no `{!field}` slug lock-in.
- **Reject**: native `documenttemplate` (env-locked, desktop-Word content controls, no TipTap round-trip),
  the email `template` entity's memo-only body for Word, and R6's Note-attachment storage.

### Open design question — one entity or two?
Should Word + email templates share **one** entity (e.g. `sprk_template` with a `type` = Word | Email and a
File column that holds either payload) or **two** (`sprk_composetemplate` + keep email on the OOB `template`
entity)? **Recommendation to validate in `/design-to-spec`:** **one unified `sprk_template` entity** — a
single catalog, one picker component, one merge seam, matching §11 "one component that works exceptionally
well." Migrating email off the OOB `template` entity is the cost; weigh it.

---

## 3. Scope — use cases

### UC-1 — Template catalog (the `sprk_template` entity + File column)
Define the entity: File column (payload), name, category, description, type (Word | Email), optional
preview/thumbnail, org-shared vs personal. Seed a **starter set** of firm templates. Provide a maker path
to add templates (upload `.dotx`/HTML into a record) — see UC-6.

### UC-2 — Templates tab in Quick Start → opens a Compose tab (app-wide) *(the moved R7 UC-1)*
Add a **"Templates" tab** to `QuickStartModal` (today Create / Analysis) showing **template cards**.
Selecting a **Word** template **opens (or focuses) a Compose workspace tab** with the template mounted —
**wherever** in the SpaarkeAi app Quick Start is opened (surface-launch: `consumerType` → registry →
`handleSurfaceLaunch` `workspace-tab`). The Compose empty-state **"Open template"** button re-points to this
same surface (one template surface, not two).

### UC-3 — New-from-template (the Compose mount path)
"Open template" = the template **becomes** a new document — **not** a merge onto an existing body. Flow:
`sprk_template` File column (`.dotx`) → optional `{{token}}` merge → **`ComposeDocxProjectionBuilder`**
(docx → TipTap HTML) → **mount born-in as a new Compose tab** → name on first save (R7 UC-3 name modal).
**New server piece**: a **resolve-only endpoint** (`template id → projected HTML`, no create/save), reusing
R6's projection + token engine. (Distinct from R6's `ComposeTemplatePartMergeEngine`, which grafts an
*existing* body into a template's chrome — that "apply house style" feature stays available but is not this.)

### UC-4 — Token merge (tier-2)
Support a small, closed set of merge tokens (client, matter, date, today, author) resolved from host
context at resolve time. **v1 may ship boilerplate-only** (no tokens) and add merge as a fast sub-phase —
the engine already exists. Decide the token schema + source.

### UC-5 — Email templates on the same system
Unify the email composer's template picker onto the `sprk_template` catalog (type = Email). `EmailComposer`
keeps **Lexical** (per R7 decision — no TipTap for email); only the template **source** changes. Decide
whether to migrate existing OOB email `template` records or dual-read during transition.

### UC-6 — Maker authoring / management (scope TBD)
How templates get created/curated: a light admin surface, or just makers creating `sprk_template` records +
uploading the File column via a model-driven form. **Recommendation**: start with the model-driven form
(no custom admin UI); revisit if curation needs grow (§11 default-to-reuse).

---

## 4. Goals / Non-goals

**Goals**
- One **Notes-free** template catalog (`sprk_template` + File column), portable across environments.
- A **Templates tab** in Quick Start whose Word cards open a Compose tab, app-wide.
- **New-from-template** resolve path (template → editable Compose doc), reusing R6 projection/merge.
- **Email** templates served from the same catalog (Lexical composer unchanged).
- A small, closed **`{{token}}`** merge set (tier-2; boilerplate-only acceptable for v1).

**Non-goals**
- Native `documenttemplate` content controls (rejected).
- TipTap for the email composer (stays Lexical).
- Word-template *authoring inside Compose* (makers author `.dotx` in Word; Spaarke stores/serves it).
- The R7 editor-UX features (save/autosave/hotkeys/PDF-import) — those are R7.

---

## 5. Constraints

- **BFF Hygiene (§10)**: new/changed endpoints (template list, resolve-only, email-template read) need a
  **Placement Justification** + **publish-size verification** (≤60 MB compressed; report absolute + delta).
  Reuse `Services/Ai/Delivery/*` + `Services/Compose/ComposeDocxProjectionBuilder`; no new subsystem.
- **Component Justification (§11)**: the new `sprk_template` entity is justified — the email `template`
  entity is memo-only (no binary), native `documenttemplate` is env-locked content-control, and R6's
  Note-attachment storage is being *removed*; none can store a Notes-free Word `.dotx`. Reuse
  `WordTemplateService`/`EmailTemplateService`/`ITemplateEngine`/projection — do **not** fork.
- **Coordination**: R8 touches `Services/Ai/Delivery/*` (shared with the AI-architecture project) and the
  `QuickStartModal`/`ComposeEmptyState` client. Run `/conflict-check` before BFF PRs. **Sequence after R7**
  (R7 owns the Compose client + the name-on-save modal R8's new-from-template depends on) or coordinate the
  shared `ComposeEmptyState`/`ComposeWorkspace` edits closely.
- Commit `--no-verify`; co-author trailer `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.

---

## 6. Key files / fault lines (grounded starting map)

Server — `src/server/api/Sprk.Bff.Api/`:
- `Services/Ai/Delivery/ComposeTemplateSource.cs` — **replace** the Note-attachment fetch (`:161-183`) with
  a File-column read on `sprk_template`.
- `Services/Ai/Delivery/WordTemplateService.cs` — reuse `{{token}}` OOXML merge.
- `Services/Ai/Delivery/EmailTemplateService.cs` + `ITemplateEngine.cs` — reuse for email; re-point source.
- `Services/Compose/ComposeDocxProjectionBuilder.cs` — docx → TipTap HTML (new-from-template resolve).
- `Services/Compose/ComposeTemplatePartMergeEngine.cs` — existing apply-house-style path (unchanged).
- `Api/ComposeEndpoints.cs` (apply-template `:117`) + a **new resolve-only endpoint**; template **list**.
- `Api/CommunicationTemplateEndpoints.cs` — email template list/render (re-point to `sprk_template`).

Client:
- `src/solutions/SpaarkeAi/src/components/conversation/QuickStartModal.tsx` — add "Templates" tab.
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeEmptyState.tsx` — re-point "Open template".
- `.../ComposeWorkspace.tsx` — `mountBornInEditor` (born-in mount for new-from-template).
- `src/client/shared/Spaarke.UI.Components/src/components/EmailComposer/*` — template picker source swap.

Dataverse:
- New `sprk_template` entity (File column + metadata) — schema via `dataverse-create-schema`.

---

## 7. Phasing sketch (to be refined by `/project-pipeline`)

0. Decisions: one-vs-two entities (§2); token schema/source; email migration vs dual-read; maker authoring.
1. **`sprk_template` entity** (File column + metadata) + seed starter templates.
2. **Storage swap**: `ComposeTemplateSource` reads the File column (retire the Note-attachment path).
3. **Resolve-only endpoint** (template → projected TipTap HTML) + **list endpoint** (cards).
4. **Templates tab** in Quick Start + re-point empty-state "Open template" → surface-launch a Compose tab.
5. **Token merge** (closed set) — tier-2.
6. **Email templates** onto `sprk_template` (Lexical composer picker source swap).
7. Wrap-up: anti-clobber deploy (BFF + `sprk_spaarkeai`), tests, docs (template system architecture).

---

## 8. Success criteria (closed set for spec authoring)

- Templates are stored in `sprk_template` via a **File column** — **zero** Notes/annotation dependency.
- Quick Start shows a **Templates** tab; a **Word** card opens/focuses a **Compose tab** with the template
  mounted, from anywhere Quick Start opens; empty-state "Open template" opens the same surface.
- New-from-template produces an **editable** Compose document (docx→TipTap projection), named on first save.
- The **email composer** lists/renders templates from the same `sprk_template` catalog (still Lexical).
- `{{token}}` merge fills the closed field set (or boilerplate-only if v1 defers merge).
- Publish size ≤60 MB; no new HIGH CVE; placement + component justifications recorded.

---

## 9. Next steps

1. Resolve §7-step-0 decisions (esp. one-vs-two entities; email migration).
2. `/design-to-spec` on this file → `spec.md`.
3. `/project-pipeline` → `plan.md` + tasks; create worktree `spaarke-wt-spaarkeai-compose-templates-r8`.
4. **Sequence relative to R7** (depends on R7's name-on-save modal + shared Compose client files).
