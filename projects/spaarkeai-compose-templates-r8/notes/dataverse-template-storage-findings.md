# Dataverse Template Storage & Merge — Research Findings (for r8 design)

> **Compiled**: 2026-08-13 (researcher agent + code review of R6's `ComposeTemplateSource`).
> **Purpose**: ground the r8 template-system design. Sources cited inline.

## The decision in one line

**Store Spaarke templates (Word `.dotx` for Compose; HTML for email) in a custom entity with a
Dataverse *File column* — NOT in the Notes/annotation table, NOT the native `documenttemplate`
content-control model, NOT the email `template` entity's memo body — and merge with a simple `{{token}}`
substitution over the existing OOXML/HTML engines.**

## Why each OOB option fails for us

| Model | Byte storage | Field-merge encoding | TipTap/HTML round-trip | Verdict |
|---|---|---|---|---|
| **Native `documenttemplate`** (OOB Word templates) | Inline base64 in a `content` column (String/TextArea, ~1 GB) — *not* Notes | **Word content controls** bound to a per-ENV CRM custom-XML part (`urn:microsoft-crm/document-template/…`); authored only in **desktop Word 2013/2016**; **"env-to-env migration isn't supported"** (Learn) | ❌ content controls + customXml don't survive docx→HTML; the binding is lost | **Reject** — env-locked, desktop-authored, and its apply action (`ExportWordDocument`/`SetWordTemplate`) is reported to require **Notes enabled on the target table** (community-sourced, not Learn-verified, but a direct strike for a no-Notes org) |
| **Email `template` entity** | `body` memo (HTML/text, ~1 GB); `presentationxml`/`safehtml`/`subject` — **no binary column** | `{!entity.field}` slugs resolved via `InstantiateTemplate`/`SendEmail` | N/A — cannot hold a `.dotx` | **Reject for Word**; it *is* the current home for OOB email merge, but ties us to `{!…}` slug semantics + send-time instantiation |
| **R6 `ComposeTemplateSource`** (shipped) | `.dotx` as a **Note (annotation) attachment** on a `template` record (`annotations`, `isdocument eq true`, base64 `documentbody`) | none, or `{{token}}` via `WordTemplateService` | ✅ via `ComposeDocxProjectionBuilder` | **Reject the storage** — it is exactly the **Notes dependency** the org avoids. Code: `Services/Ai/Delivery/ComposeTemplateSource.cs:161-183` |

## The recommended model — custom entity + File column + `{{token}}`

- **Storage**: a custom entity (working name **`sprk_composetemplate`**, but r8 should evaluate a more
  general name if it also serves email — e.g. `sprk_template`) with a **File column** for the payload
  (`.dotx` for Word; the HTML/body for email can be a memo or a File). File columns live in **Dataverse
  File blob storage, independent of the Notes/annotation table**. Backend fetch: single-request
  `GET .../{entity}({id})/{filecolumn}/$value` for ≤128 MB; chunked `InitializeFileBlocksDownload` above.
  Default `MaxSizeInKB` 32768 (32 MB), max 10 GB. App-only token fetches fine. **BYOK caveat**:
  self-managed-key tenants store File data relationally, capped 128 MB, single-chunk only.
- **Merge**: keep the **`{{token}}` substitution** `WordTemplateService` already performs over OOXML runs
  (it handles the split-run gotcha where a token spans multiple `<w:r>`). Trivial to author (any editor),
  fully portable/env-agnostic, and TipTap-renderable. Content controls win only for **repeating
  relationship/subgrid data**, which the legal boilerplate + few-fields (client, matter, date, today)
  use case does not need.
- **Metadata columns** for the picker cards: name, category, description, (optional) thumbnail/preview.

## Native-template mechanics (for completeness / if ever reconsidered)

- Byte storage confirmed inline in `documenttemplate.content` (Learn entity ref) — no Notes to *store*.
- Apply = model-driven **"Word Templates" ribbon** (`SetWordTemplate`) OR the unbound Web API action
  **`POST /api/data/v9.2/ExportWordDocument`** (`EntityTypeCode`, `SelectedTemplate`, `SelectedRecords`
  → merged doc bytes; also Power-Automate-callable). Headless-capable, **but** lightly documented (not on
  the Learn entity page) — treat the exact contract as needing an empirical confirm before relying on it.
- Programmatic create via `POST /documenttemplates` with base64 `content` works, **but** the `.docx` must
  already embed the env-specific CRM customXml schema, so you can't upload an arbitrary docx and get
  working binds.

## Caveats to verify empirically if native path is ever revisited
- The **"target table must have Notes/Attachments enabled or `ExportWordDocument` errors"** constraint is
  **community-sourced, not Learn-verified**. Directly relevant to a no-Notes org — confirm in a dev env
  before trusting it.
- `ExportWordDocument` request/response contract — confirm empirically.

## Sources
- [documenttemplate table reference](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/reference/entities/documenttemplate)
- [Use Word templates to create standardized documents](https://learn.microsoft.com/en-us/power-platform/admin/using-word-templates-dynamics-365)
- [Email Template (template) reference](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/reference/entities/template)
- [Work with file column definitions](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/file-attributes)

## Reusable code already in the repo (r8 extends, does not fork — root CLAUDE.md §11)
- `Services/Ai/Delivery/WordTemplateService.cs` — OOXML `{{token}}` merge (headers/footers, split-run consolidation) → docx `byte[]`.
- `Services/Ai/Delivery/EmailTemplateService.cs` — email `template` entity fetch + `{{token}}` render → subject/body HTML.
- `Services/Ai/Delivery/ComposeTemplateSource.cs` — the R6 fetch+merge (REPLACE its Notes-based storage).
- `Services/Compose/ComposeTemplatePartMergeEngine.cs` — OOXML part-merge (apply house-style chrome to an existing body; distinct from new-from-template).
- `Services/Compose/ComposeDocxProjectionBuilder.cs` — docx → TipTap-shaped HTML (the projection that makes a template editable).
- `Services/Ai/ITemplateEngine.cs` / `TemplateEngine.cs` — shared Handlebars-style string renderer.
- Client: `QuickStartModal.tsx` (add a "Templates" tab), `ComposeEmptyState.tsx` ("Open template"), `EmailComposer` template picker (`onListEmailTemplates`/`onRenderEmailTemplate`).
