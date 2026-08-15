# Spaarke Templates R8 — Unified Template System (Compose Word + Email), Notes-Free Storage

> **Project**: `spaarkeai-compose-templates-r8`
> **Created**: 2026-08-13 · **Author**: Ralph Schroeder + Claude (Opus 4.8)
> **Status**: DESIGN — **second draft** (split out of `spaarkeai-compose-r7` on 2026-08-13; §2 open questions + §7-step-0 decisions now CLOSED per the best-practice investigation 2026-08-13; **re-aligned 2026-08-15** against merged code-quality-and-assurance-r3 + net10 — new God-class ratchet / ADR-013 boundary / naming gates folded into §5–§6; substance unchanged, still aligned; hand-authored input to `/design-to-spec` → `/project-pipeline`)
> **Split rationale**: what began as R7's "Templates tab" use case (UC-1) is really a **cross-surface template subsystem** — storage, merge model, a picker, *and* it should also cover **email templates**. That is bigger than R7's editor-UX scope, so it is its own project. R7 keeps the editor UX (save/autosave/hotkeys/PDF-import); R8 owns templates end-to-end.
> **Governing constraints**: root CLAUDE.md §10 (BFF Hygiene), §11 (Component Justification), ADR-050 (modal shell), [ASSISTANT-SURFACE-LAUNCH-MECHANISM.md](../../docs/architecture/ASSISTANT-SURFACE-LAUNCH-MECHANISM.md) (template → Compose tab).
> **Research grounding**: [`notes/dataverse-template-storage-findings.md`](notes/dataverse-template-storage-findings.md) (storage) · [`notes/merge-field-and-unified-template-best-practice.md`](notes/merge-field-and-unified-template-best-practice.md) (merge model, token representation, entity shape, **authoring UX**).

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

### RESOLVED design decisions (2026-08-13 investigation — do not re-litigate)

**(a) One entity or two? → ONE unified `sprk_template`.** Single catalog, one picker, one merge seam (§11).
`sprk_type` optionset (Word | Email). **File column** (`sprk_payload`) holds the Word `.dotx`; **memo**
columns (`sprk_subject` / `sprk_body`, HTML) hold the email payload — memo (not File) for email makes the OOB
`template` migration a trivial `body→sprk_body` copy and keeps email body editable in a form. Full column
table: [`notes/merge-field-and-unified-template-best-practice.md`](notes/merge-field-and-unified-template-best-practice.md) §7.

**(b) Merge model → Model A (resolve-at-open) for v1, everywhere.** Tokens resolve to text **server-side**
before either editor mounts — exactly what the shipped code does (`ComposeTemplateSource` merges OOXML before
projection; `EmailTemplateService` renders HTML in the BFF). **No live editable `{{token}}` text in any
editor.** Live merge-field "pills" (Model B) are a **deferred, Lexical-first** enhancement — NOT a Word/TipTap
feature, because an inline atom node desyncs the Compose `(paraId,runIndex,offset)` offset table (ADR-049;
`composeNumberAtomExtension.ts` documents why Compose uses a decoration, not a node). `{{token}}` stays the
single canonical wire syntax across both surfaces.

**(c) Token vocabulary → one shared framework-agnostic `TokenRegistry`** (`{id,label,category,sampleValue}`)
— the one new abstraction (§11-justified: no registry exists; vocabulary otherwise drifts between the Word
and email merge contexts). Feeds the server merge context, validation, and the future picker.

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

### UC-6 — Template authoring / management *(RESOLVED — the confusing part)*
**Users never see a separate "template builder" — they promote something they already authored, in the
editor they already use.** (Full rationale: [`notes/merge-field-and-unified-template-best-practice.md`](notes/merge-field-and-unified-template-best-practice.md) §7.5.)

- **Primary — "Save as template" in-context.** Compose doc → **"Save as Word template"** (new thin BFF action:
  current resolved OOXML bytes → `sprk_template.sprk_payload`, reusing the `documents/{id}/save` path).
  EmailComposer draft → **"Save as email template"** (composer HTML → `sprk_body`/`sprk_subject`). For v1
  (boilerplate-only, no tokens) this is write-it-and-name-it — zero syntax, zero upload.
- **Who authors** (owner decision 2026-08-13): **any user** saves **personal** templates freely
  (`sprk_scope=Personal`); **firm/org-shared** (`sprk_scope=Org`) is **role-gated**. A "Save as template ▾"
  split offers "Save as my template" (always) / "Save as firm template" (role-gated).
- **Secondary — "Import Word file"** (power path for pre-built firm `.dotx`): a small Import button on the
  Templates tab uploads a file into a new record. **Accept `.docx` too** (resolver already does) — no
  "Save As Template" ceremony.
- **Management** (rename / recategorize / delete / toggle personal↔org): a plain list — "manage" mode on the
  Quick Start Templates tab OR the model-driven grid. No custom admin app (§11 default-to-reuse).

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
  **Current baseline: ~44.96 MB** (dotnet-10-upgrade-r1 task 031, net10 re-baseline). Reuse
  `Services/Ai/Delivery/*` + `Services/Compose/ComposeDocxProjectionBuilder`; no new subsystem.
- **Component Justification (§11)**: the new `sprk_template` entity is justified — the email `template`
  entity is memo-only (no binary), native `documenttemplate` is env-locked content-control, and R6's
  Note-attachment storage is being *removed*; none can store a Notes-free Word `.dotx`. Reuse
  `WordTemplateService`/`EmailTemplateService`/`ITemplateEngine`/projection — do **not** fork.
- **God-class ratchet (NEW — code-quality-and-assurance-r3, `GodClassGuardTests`)**: per-file line freeze,
  **+100 grace**. `ComposeEndpoints.cs` is frozen at **2,651**, `ComposeService.cs` 3,573,
  `ComposeDocxProjectionBuilder.cs` 3,085, `CommunicationService.cs` 2,676. **R8 MUST put its new
  resolve-only + list endpoints in a NEW file** (e.g. `Api/TemplateEndpoints.cs`), **not** append to
  `ComposeEndpoints.cs` (a resolve + list + DTOs would blow the +100 grace and trip CI). Reuse the frozen
  services/projection by *calling* them — do not add code inside them. (This also reads cleaner and is §11-fine.)
- **ADR-013 linear-consumer boundary (NEW — CI-enforced by `ADR013_LinearConsumerBoundaryTests`)**: CRUD/
  consumer code reaches AI/template primitives **only** through a `Services/Ai/PublicContracts/` facade
  (`IComposeTemplateSource` already exists there). R8's resolve-only endpoint MUST call the facade — never
  inject delivery/executor internals directly. (Already the design intent; now a fitness function.)
- **Naming gate (NEW)**: `sprk_template` columns + new endpoints must satisfy the naming-conformance gate and
  [`docs/standards/ODATA-NAMING-CONVENTION.md`](../../docs/standards/ODATA-NAMING-CONVENTION.md) (gate now scans bicep + config too).
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
  **CALL only** — frozen God-class (3,085); do not add code inside it.
- `Services/Compose/ComposeTemplatePartMergeEngine.cs` — existing apply-house-style path (unchanged).
- **`Api/TemplateEndpoints.cs` (NEW file)** — the resolve-only endpoint + template **list** endpoint + the
  Word **save-as-template** action live here, NOT in `ComposeEndpoints.cs` (frozen God-class 2,651, +100
  grace — see §5). `ComposeEndpoints.cs` keeps its existing apply-template `:117` only.
- `Api/CommunicationTemplateEndpoints.cs` — email template list/render (re-point to `sprk_template`); NOT
  frozen, small — email-side changes stay here. Reach template resolution via the `PublicContracts` facade.

Client:
- `src/solutions/SpaarkeAi/src/components/conversation/QuickStartModal.tsx` — add "Templates" tab.
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeEmptyState.tsx` — re-point "Open template".
- `.../ComposeWorkspace.tsx` — `mountBornInEditor` (born-in mount for new-from-template).
- `src/client/shared/Spaarke.UI.Components/src/components/EmailComposer/*` — template picker source swap.

- `.../ComposeWorkspace.tsx` / `EmailComposer/*` — **"Save as template"** affordance (new authoring path, UC-6).
- `.../` shared `TokenRegistry` module — closed field-set vocabulary (deferred token layer, §2(c)).

Dataverse:
- New `sprk_template` entity: `sprk_type` (Word|Email), `sprk_payload` (**File**, Word `.dotx`), `sprk_subject`/
  `sprk_body` (**memo** HTML, email), `sprk_name`/`sprk_category`/`sprk_description`, `sprk_scope`
  (Personal|Org) — schema via `dataverse-create-schema`.

---

## 7. Phasing sketch (to be refined by `/project-pipeline`)

0. ~~Decisions~~ — **CLOSED** (§2 a/b/c + UC-6): one unified entity, Model A merge, shared `TokenRegistry`,
   in-context "Save as template" authoring (personal free / org role-gated). Remaining spike carried to §8:
   `MERGEFIELD` field codes vs literal `{{token}}` runs in the `.dotx`.
1. **`sprk_template` entity** (columns per §6) + seed starter templates.
2. **Storage swap**: `ComposeTemplateSource` reads the `sprk_payload` File column (retire the Note path).
3. **Resolve-only endpoint** (template → projected TipTap HTML) + **list endpoint** (cards).
4. **Templates tab** in Quick Start + re-point empty-state "Open template" → surface-launch a Compose tab.
5. **Authoring (UC-6)**: "Save as template" — Compose (new save-as-template BFF action) + EmailComposer;
   personal/org scope + role gate; "Import Word file" secondary path.
6. **Token merge** (closed set, Model A) — tier-2; boilerplate-only acceptable for v1.
7. **Email templates** onto `sprk_template` (Lexical composer picker source swap; dual-read → copy migration).
8. Wrap-up: anti-clobber deploy (BFF + `sprk_spaarkeai`), tests, docs (template system architecture).

---

## 8. Success criteria (closed set for spec authoring)

- Templates are stored in `sprk_template` via a **File column** — **zero** Notes/annotation dependency.
- Quick Start shows a **Templates** tab; a **Word** card opens/focuses a **Compose tab** with the template
  mounted, from anywhere Quick Start opens; empty-state "Open template" opens the same surface.
- New-from-template produces an **editable** Compose document (docx→TipTap projection), named on first save.
- The **email composer** lists/renders templates from the same `sprk_template` catalog (still Lexical).
- A user can **"Save as template"** from a Compose doc AND from the EmailComposer without leaving the editor;
  the result is a `sprk_template` record. **Personal** save works for any user; **org-shared** save is
  role-gated. An existing Word `.docx`/`.dotx` can be **imported** into a record.
- `{{token}}` merge fills the closed field set (or boilerplate-only if v1 defers merge); the `.dotx` `MERGEFIELD`
  vs literal-`{{token}}` fidelity spike is resolved before the maker authoring convention is documented.
- Publish size ≤60 MB; no new HIGH CVE; placement + component justifications recorded.

---

## 9. Next steps

1. Resolve §7-step-0 decisions (esp. one-vs-two entities; email migration).
2. `/design-to-spec` on this file → `spec.md`.
3. `/project-pipeline` → `plan.md` + tasks; create worktree `spaarke-wt-spaarkeai-compose-templates-r8`.
4. **Sequence relative to R7** (depends on R7's name-on-save modal + shared Compose client files).
