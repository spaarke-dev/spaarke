# 118a InvoiceAttachmentMarking

Import: `@spaarke/communication-components/invoice-attachment-marking` -> `src/components/InvoiceAttachmentMarking/index.ts` (package.json `exports`).
Exports (exactly): `InvoiceAttachmentMarking`, `defaultInvoiceMarking`, `validateInvoiceMarking`; types `InvoiceAttachmentRole`, `MarkableAttachment`, `InvoiceAttachmentMarkingProps`.

## Deviation (one)
`validateInvoiceMarking(value, mode)` cannot know whether an attachment is a PDF (value is id -> role only), yet the contract
requires `invoice-not-allowed` for a non-PDF marked 'invoice'. Added an OPTIONAL third parameter `attachments?` to carry that
check; the two-argument call behaves exactly as agreed (counts + supporting-only). word-add-in-r1 should pass attachments.

## Notes
- PDF = contentType contains "pdf" OR fileName ends .pdf. A disabledReason attachment is never a candidate and is rejected as invoice when attachments are passed.
- Component ignores change events when `disabled` or the row is locked (Fluent RadioGroup still fires onChange for a programmatic click in jsdom).
- Subpath is a source file export; no dist build needed for Vite/webpack hosts. Hosts that use per-subpath aliases (office-addins tsconfig `paths` + webpack alias, exact `$` match) must add one for this subpath, like `logic/connections/provenance`. PCF hosts with `@spaarke/communication-components/*` -> src/* already resolve. Spaarke.AI.Widgets maps `/*` to dist (declaration-only build) which emits it.
- Hosts must provide `@fluentui/react-components` (>=9.46) and React 16-19; uses Fluent `useId`, no React-18 APIs.
- Jest note: this worktree's path contains `.claude`, so the package's testMatch finds 0 tests; ran with an override config (testMatch `**/*.test.tsx`). Normal worktrees are unaffected.
- Package `lint` (tsc --noEmit) has 70 pre-existing errors without built deps (`@spaarke/ui-components` dist missing); none in the new folder.
- Review gates: self-reviewed against ADR-012/021 (tokens only, no barrel). No separate code-review agent run given urgency.

rework: none task 118a
