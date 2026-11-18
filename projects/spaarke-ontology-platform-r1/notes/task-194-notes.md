# Task 194 notes: Email documents lookup above the modal (owner 185 C1/C2)

PR [#2856](https://github.com/spaarke-dev/spaarke/pull/2856), head `120856474b`, branch `fix/ontology-194-document-email-lookup-layer` (implementer agent, Opus). Main-session sign-off 2026-11-16 after the independent adversarial review (Opus) and a scoped re-check of the fix diff; merged to master `e476e9b61b`. Not deployed.

- **Mechanism:** the existing SprkModal `nonBlocking` + `yieldToSidePane` (`sidePaneLayering.ts`); no second layering mechanism.
- **WizardShell:** opt-in modal-only `nonBlocking`/`yieldToSidePane` passthrough.
- **DocumentEmailWizard:** opt-in `nonBlocking` (yields while its lookup is open) + `onSidePaneLookupChange`. Lookup wiring only; the Summary step is left to task 195.
- **Relationship Viewer (iframe in Find Similar):** new `sidePaneFrameSignal` (same-origin postMessage to the parent). `FindSimilarViewerDialog` gets opt-in `yieldToFrameSidePane`, which checks its own iframe source, the origin and the message shape; a reload resets it. Chosen over a top-level `navigateTo` open because it keeps the dialog chrome and "Open in new tab"; form and new-tab launches do nothing.
- **Preview** (PreviewModal/BrowseModal/RichFilePreviewDialog): opt-in `yieldToSidePane`, so a preview open under the wizard or Find Similar does not cover the pane (review F1).
- **SemanticSearchControl 1.1.87** in every location including the footer; the committed bundle is the branch's `build:prod` output. The preview's `onClose` is ignored while the wizard or Find Similar is stacked on it (independent review F1-a).
- **RelatedDocumentCount 1.21.7:** the form-path FindSimilarViewerDialog gets `yieldToFrameSidePane` (independent review F1-b).
- **Behaviour change:** the Email documents wizard and Find Similar in Semantic Search are always non-blocking (no scrim, no focus trap; a trapped modalizer would pull focus back from the pane). The wizard was already dismiss='explicit'.
- **Tests:** shared library 419 pass; SemanticSearch PCF 146 pass (previewStack fails 2/3 on the old code); RelatedDocumentCount 30 pass (the opt-in test fails on the old code); tsc clean.
- **Review (implementer):** F1 preview layer (fixed); F2 lint in the new file (fixed). K4: equal z-index relies on portal DOM order. K2: if the pane renders before the message arrives, the existing fallback docks the dialog beside it. K4: Find Similar is non-blocking. ADR check: no violations. Independent review: F1-a stacked-preview close and F1-b form path (both fixed); K3 wizard non-blocking (disclosed); K4 preview × and Escape ignored while stacked; K4 boolean lookup flag (no overlap possible); K2 PCF wiring and DRV App.tsx line untested.
- **Deviations:** the lookup is wrapped in DocumentEmailWizard, so SendEmailFollowOnStep is unchanged; three preview files were added (F1); index and POML updates are done by the main session.
- **Out of scope, to file:**
  - DocumentEmailWizard's Summary step sets state during render (~806, ~912; React "Cannot update a component while rendering a different component"). This is task 195's area.
- **Owed:** owner UI check (light/dark): Semantic Search Email documents, the preview path, Find Similar → DRV, and DRV from a form.

rework: fix-round-2 task 194
