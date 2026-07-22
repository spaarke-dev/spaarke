# UAT Round 3 Feedback — email-communication-solution-r4 (2026-07-21)

Source: owner UAT on dev after W10 import (Attachments v1.1.0, Connections v1.3.0, Actions v1.1.3).
Wave: **W11**. All items are client-side PCF refinements.

Shared design spec referenced repeatedly = the task-101 standard [`docs/standards/UI-DESIGN-STANDARDS.md`](../../../docs/standards/UI-DESIGN-STANDARDS.md):
**section header/title = Segoe UI 14px (`fontSizeBase300`), semibold 600 (`fontWeightSemibold`), color #242424 (`colorNeutralForeground1`), 20px height, 4px top/bottom padding (`spacingVerticalXS`)** — theme tokens only (ADR-021), never hardcoded.

---

## A. CommunicationAttachments PCF (v1.1.0 → v1.2.0)

- **A11-1 (bug)** Preview modal shows a **double header** (title rendered twice — see screenshot). Confirm we mount `RichFilePreviewDialog` EXACTLY as `SemanticSearchControl` does (owner suspects we don't). Remove the duplicate title.
- **A11-2** Add an **upload-status indicator** per row: green SharePoint icon = file uploaded to SPE; red SharePoint icon = not uploaded.
- **A11-3** Remove the **attachment type icon** (Doc/PDF) — type is conveyed by the filename extension + the Details "Type" row in the preview. *(scope clarified via question — see decision)*
- **A11-4** Make the **PCF title a property/setting**, default **"ATTACHMENTS"**.
- **A11-5** Title styling = all caps + the UI-DESIGN-STANDARDS spec (Segoe 14px/600/#242424/20px/4px).

## B. CommunicationConnections PCF (v1.3.0 → v1.4.0)

- **B11-1** Card: add **space between record number and record name**.
- **B11-2** Make the **PCF title a property/setting**, default **"RELATED RECORDS"**.
- **B11-3** Title styling = UI-DESIGN-STANDARDS spec (Segoe 14px/600/#242424/20px/4px).
- **B11-4** Remove the **"open" icon** on the last line item (purpose unclear to owner).
- **B11-5** Remove the **"matched because …" subtitle** on rows (not needed).
- **B11-6** Connections **modal content should fill the full modal viewport** (currently content only fills top region).
- **B11-7** Change **"Done" → "Save"** (save-on-change semantics preserved).
- **B11-8** Move the **Save button to the modal bottom footer**.

## C. CommunicationActions PCF (v1.1.3 → v1.2.0)

- **C11-1** Increase tool **icon size to 20×20** to match OOB.
- **C11-2** For **icon-only buttons** (separator line, save-to-SharePoint, create event, etc.) make them **right-aligned**.
- **C11-3** **Create Event / Create To Do / Link Invoice** should open as **modals**, not navigate to a new page/tab. *(pattern confirmed via question — see decision)*

---

## Decisions (from owner clarification, 2026-07-21)
- **A11-2/A11-3:** LEFT row slot = upload-status SharePoint icon (green = SPE file uploaded, red = not). **KEEP** the right-side type pill (PDF/Email). The removed "type icon" = the left file-type glyph (replaced by the upload icon).
- **C11-3:** Prefer an EXISTING custom Fluent v9 create dialog for Create Event / Create To Do / Link Invoice if one exists. If none exists, use OOB `Xrm.Navigation.navigateTo(..., {target:2})` (in-app dialog) **behind a launch seam/abstraction** so a custom Fluent dialog can replace it later WITHOUT touching call sites. Executor must first check whether custom dialogs already exist.
