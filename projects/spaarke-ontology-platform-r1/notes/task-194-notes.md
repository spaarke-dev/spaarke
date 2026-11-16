# Task 194 notes: Email documents lookup above the modal (owner 185 C1/C2)

PR [#2856](https://github.com/spaarke-dev/spaarke/pull/2856), head `35be4f643d`, branch `fix/ontology-194-document-email-lookup-layer` (implementer agent, Opus). Not merged, not deployed. Independent adversarial review (Opus) running 2026-11-16.

- **Mechanism:** the existing SprkModal `nonBlocking` + `yieldToSidePane` (`sidePaneLayering.ts`); no second layering mechanism.
- **WizardShell:** opt-in modal-only `nonBlocking`/`yieldToSidePane` passthrough.
- **DocumentEmailWizard:** opt-in `nonBlocking` (yields while its lookup is open) + `onSidePaneLookupChange`. Lookup wiring only; the Summary step is left to task 195.
- **Relationship Viewer (iframe in Find Similar):** new `sidePaneFrameSignal` (same-origin postMessage to the parent). `FindSimilarViewerDialog` gets opt-in `yieldToFrameSidePane`, which checks its own iframe source, the origin and the message shape; a reload resets it. Chosen over a top-level `navigateTo` open because it keeps the dialog chrome and "Open in new tab"; form and new-tab launches do nothing.
- **Preview** (PreviewModal/BrowseModal/RichFilePreviewDialog): opt-in `yieldToSidePane`, so a preview open under the wizard or Find Similar does not cover the pane (review F1).
- **SemanticSearchControl 1.1.87** in every location including the footer; the committed bundle is the branch's `build:prod` output.
- **Behaviour change:** Find Similar in Semantic Search is non-blocking (no backdrop, no click-outside close).
- **Tests:** 4 new suites, all failing on the old code (4/5, 3/10, 2/3) and all passing now; affected shared-library suites 375 pass; PCF 143 pass; tsc clean.
- **Review (implementer):** F1 preview layer (fixed); F2 lint in the new file (fixed). K4: equal z-index relies on portal DOM order. K2: if the pane renders before the message arrives, the existing fallback docks the dialog beside it. K4: Find Similar is non-blocking. ADR check: no violations.
- **Deviations:** the lookup is wrapped in DocumentEmailWizard, so SendEmailFollowOnStep is unchanged; three preview files were added (F1); index and POML updates are done by the main session.
- **Out of scope, to file:**
  - `RelatedDocumentCount.tsx:170` opens the same Find Similar dialog without the opt-in, so on a form the Relationship Viewer's email lookup opens behind it.
  - DocumentEmailWizard's Summary step sets state during render (~806, ~912; React "Cannot update a component while rendering a different component"). This is task 195's area.
- **Owed:** owner UI check (light/dark): Semantic Search Email documents, the preview path, Find Similar → DRV, and DRV from a form.

rework: fix-round-1 task 194
