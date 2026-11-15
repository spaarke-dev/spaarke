# Task 013: collapsible sections remembered per user; invoice and document rows open a child record

**Owner, 2026-11-14 retest:**
- "sections should expand/collapse; and save settings after close for the user"
- An invoice should open "on top of this modal with invoice modal"
- Documents had no open action.

## What changed (src/client/external-spa)

### Section collapse
- **`record/recordSectionState.ts` (new).** Holds collapse state in localStorage under the key `sprk.ext.recordSection.v1.{cyrb53(contact id, else systemuser id)}.{entity}.{section}`, with the value `'1'`.
- **Storage failure is safe.** Every read and write is wrapped in try/catch, so a store that throws means all sections show expanded.
- **No user key yet, no read or write.** Nothing is stored under a shared key before `/me` answers.
- **No PII stored.**
- **`RecordSectionCard.tsx`.** Adds a chevron toggle with `aria-expanded` / `aria-controls`.
  - The header and its action icons stay usable when a section is collapsed.
  - The body stays mounted but hidden (`hidden` + `display:none`), because Upload's dialog and offered action live in it.

### Child records
- **`record/ChildRecordModal.tsx` + `record/childRecord.ts` (new).** The child is `RecordTemplateModal` rendered again with `nested` (the template is itself the SprkModal), as a descendant of the parent's body.
- **Each opening is fresh** (`key={seq}`).
- **One level only.** A nested modal provides no opener, and it shows no "1 of 1" nav.
- **Focus after Close or Esc.** Focus returns to the redrawn row (a `data-child-record` attribute is used to find it).
- **Refresh.** An invoice child refreshes the parent's Invoices section when it closes. A document child does not, because it can't change the list, and a refresh would remount the grid and lose its sort.
- **`InvoicesSection.tsx`.** Rows are buttons. A visually hidden "Open invoice " prefix makes the accessible name carry the number, status and amount.
- **`DocumentsSection.tsx` + `DocumentLibrary.tsx`.** The document name is a link-style button, but only when `onOpenDocument` is passed. ProjectPage is unchanged. A document with no file still opens (FR-15).

## Tests
- `RecordSectionCollapse.test.tsx` (9 tests)
- `RecordTemplateChildRecords.test.tsx` (10 tests), including:
  - invoice child → New message → Esc, where all three dialogs stay open (the 2026-11-11 rule);
  - the Documents sort is kept after closing a document;
  - nested matter vs not-nested control.
- 15 of the first 17 tests fail on the old code. The 2 that pass on it have controls that fail on it.
- Full suite: typecheck clean, lint clean, vitest 59 files / 1688 tests passed.

## Quality gates

### Adversarial review (opus, one full pass)
All acceptance criteria were met. Two F1 (low) defects, both fixed:
- Closing a document child re-read Documents and lost the sort.
- The invoice row `aria-label` hid the amount and status from screen readers.

Also fixed: the nested nav, and the Esc-in-child-dialog test.

### Known limits
- **K4.** The "no third level" test uses a nested matter; no real grandchild path exists today.
- **K.** The section-collapse state can flash while the child's own `/me` read loads.
- **K.** This is the third localStorage collapse helper, after `Worklist/useLaneOpen.ts` and SpaarkeAi `usePaneCollapse`. It is kept SPA-local because it is keyed per user, and ADR-012 / the POML require it to stay in the SPA. A CAPABILITY-INDEX row is a candidate.
- **K4.** A 53-bit hash collision would only make two users share collapse preferences.

### Other checks
- **Scoped re-check of the fix diff.** The 19 tests in the two suites pass. The rewritten test file was verified: all 8 original tests plus 2 new ones.
- **adr-check:**
  - ADR-012: no shared-lib change.
  - ADR-021: Fluent v9 icons and tokens.
  - ADR-050 / the project's MODAL-DECISION-CRITERIA Path C: the child is a SprkModal of the same family, with focus and Esc tested.
- **Production `vite build`:** no warning about the import cycle.
