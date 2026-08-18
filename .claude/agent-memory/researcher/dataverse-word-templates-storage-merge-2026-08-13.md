---
name: dataverse-word-templates-storage-merge-2026-08-13
description: How Dataverse stores/applies Word templates (documenttemplate entity, template entity, File columns) — for deciding where Compose .dotx templates live and content-control vs token merge
metadata:
  type: project
---

# Dataverse Word templates: storage + merge (for Compose template home decision)

**Date**: 2026-08-13
**Question**: Where does Dataverse store native Word-template bytes, does it need Notes/Activities, how is merge encoded/applied, and is a custom File column a better home for Compose .dotx templates than native `documenttemplate` or the email `template` entity?

## Findings (Learn-verified unless noted)

**`documenttemplate` entity** — "Used to store Document Templates in database in binary format."
- Bytes live INLINE in the `content` column (Type=String/TextArea, MaxLength 1073741823 ≈1GB, `RequiredLevel=SystemRequired`, desc "Bytes of the document template"). It is base64 text on the record itself — NOT a Note/annotation, NOT a File column, NOT blob. So storing the template needs no Notes/Activities table.
- `documenttype` choice: 1=Excel, 2=Word. `status` bool (Draft/Activated). `associatedentitytypecode` = the single entity the template targets. OrganizationOwned.
- CRUD messages only (Create/Retrieve/Update/Delete/Upsert). `POST /documenttemplates` with base64 `content` works programmatically — BUT the .docx must already contain the per-environment CRM customXml schema part (see merge), so you can't meaningfully author merge fields without the Word round-trip.

**Merge encoding (native)** = Word **content controls** (Plain Text or Picture ONLY) bound via the **XML Mapping Pane** to a custom XML part whose namespace starts `urn:microsoft-crm/document-template/`. NOT `{{placeholder}}` text. Authoring requires Word DESKTOP 2013/2016 + Developer tab + a template DOWNLOADED from that specific environment (schema is env-embedded). Learn: "Environment-to-environment migration for Word or Excel templates isn't supported." Known Word-freeze bug if you insert any control other than Plain Text/Picture or edit control text (AutoCorrect capitalization triggers it).

**Apply mechanism**: model-driven "Word Templates" ribbon command = `SetWordTemplate` action. Programmatic/headless = unbound Web API action `POST /api/data/v9.2/ExportWordDocument` (body: EntityTypeCode, SelectedTemplate={type documenttemplate, GUID}, SelectedRecords=[GUIDs]) → returns merged doc bytes. Also callable from Power Automate. So it CAN run headless from a backend. GOTCHA (community-reported, not Learn-verified: stuffandtacos 2024): the TARGET table must be enabled for Attachments (notes/files) or SetWordTemplate errors — relevant since Spaarke org avoids Notes.

**`template` entity** (email template): body in `body` (Memo ~1GB) + `presentationxml`/`safehtml`/`subject`; `templatetypecode`=target entity; merge via `{!entity.field}` slugs resolved at send via `InstantiateTemplate`/`SendEmail`. UserOwned. HTML/text only — NO binary content column. WRONG tool for a .dotx binary.

**Custom entity + File column** (e.g. `sprk_composetemplate.sprk_templatefile`): YES. File columns store binary in Dataverse File blob storage (NOT relational — better perf/capacity), independent of the Notes/annotation table. Default MaxSizeInKB=32768 (32MB), max 10GB. Retrieval: single-request `GET .../sprk_composetemplate(id)/sprk_templatefile/$value` for ≤128MB; chunked InitializeFileBlocksDownload/DownloadBlock above that (single-request cap 128MB). App-only backend token fetches fine. BYOK caveat: self-managed-key tenants store File data in relational storage, 128MB/file cap, single-chunk only.

## Recommendation for Spaarke Compose
Skip BOTH native `documenttemplate` (env-locked, desktop-Word-only authoring, content controls + customXml don't round-trip into TipTap/mammoth, generation path reportedly needs Notes on target) AND the email `template` entity (no binary). Store Compose .dotx/.docx bytes in a **File column on a custom `sprk_composetemplate`** (no Notes/Activities dependency, blob-backed, app-only retrievable) and use a **simple `{{token}}` merge** substituted by Spaarke's existing OOXML-server-authoritative engine — portable, env-agnostic, renders cleanly into TipTap (tokens are plain text), and consistent with the Compose R4/R4.5 server-owns-authoring architecture. Native content-control binding only earns its keep for repeating relationship/subgrid tables, which the boilerplate+few-fields use case doesn't need.

## Sources
- learn.microsoft.com/power-apps/developer/data-platform/reference/entities/documenttemplate (MOST authoritative — column schema, content column)
- learn.microsoft.com/power-platform/admin/using-word-templates-dynamics-365 (authoring: XML Mapping Pane, content controls, env-lock, Developer tab)
- learn.microsoft.com/power-apps/developer/data-platform/reference/entities/template (email template schema)
- learn.microsoft.com/power-apps/developer/data-platform/file-attributes (File column sizes/BYOK) + file-column-data (retrieval)
- ExportWordDocument/SetWordTemplate: web search (stuffandtacos.azurewebsites.net 2024, linnzawwin.blogspot.com) — action shape + target-notes constraint; NOT on the Learn entity page (only CRUD listed)

## Open questions
- The "target table must have Notes/Attachments enabled for SetWordTemplate/ExportWordDocument" constraint is community-sourced; not yet found on a Learn page. Verify empirically if native path is ever reconsidered.
