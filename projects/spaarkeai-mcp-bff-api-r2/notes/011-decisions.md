# Task 011 - EmailComposer MCP host: notes and decisions

## Host props: v1 vs undefined (step 1)
v1 (provided by `createMcpEmailComposeHandlers` / `useMcpEmailHostProps`): typed To/Cc/subject/body (engine-owned, no host prop needed),
`associations` (the host-preset regarding matter), `attachmentSources: [{kind:'related'}]`, `recordLookupCatalog` (document only),
`onLookupRecord` (answers `sprk_document` only, via `spaarke_list_documents`; present only when the widget supplies `pickDocument`),
`authenticatedFetch` (Send adapter, D1), `idempotencyKey`.
Left undefined (engine hides the control): `onSearchRecipients`, `onLookupRecipients`, `onAddRelationship`, `onUploadLocalAttachment`,
`onListEmailTemplates`, `onRenderEmailTemplate`, `onDraftWithAi`. `attachmentSources` omits `local`, so "Add files" is hidden.
The escalation trigger did not fire: each undefined callback hides its control (harness test asserts it).

## Decisions
- D1 Send path: the engine has no send callback; it always sends through `sendCommunication()` over the injected `authenticatedFetch`.
  EmailComposer.tsx is not changed (escalation constraint), so the MCP host exports `authenticatedFetch`, an adapter for the one route
  `POST /api/communications/send` that calls `spaarke_send_email`. A tool `isError` becomes a thrown `SendCommunicationError(422, reasonCode, text)`,
  which `sendCommunication` rethrows and the composer maps through `toSendRequestError` -> `describeSendFailure`, so the tool's `reasonCode` is the error code.
  422 (a 4xx) means "refused before sending", so the engine frees the documents. A rejected `callTool` propagates and the engine classes it NETWORK. No automatic retry.
  Widget (task 040) wiring: spread `useMcpEmailHostProps(...)` into `EmailComposer` (`authenticatedFetch`, `associations`, `attachmentSources`, `recordLookupCatalog`, `onLookupRecord`).
- D2 The regarding matter comes from the host option, never from the request body's associations (a tampered association cannot redirect the archive).
- D3 Idempotency key: generated once per handler set (`crypto.randomUUID`, fallback time+random) and sent on every Send; the hook memoises the set on
  `caller` and the matter id/name, so a re-render keeps the key and a different matter starts a new compose session.
- D4 Tool argument contract (tasks 023/040 must match; their POMLs do not fix it): `spaarke_list_documents { regardingEntityType: 'sprk_matter', regardingId }`
  returning `structuredContent.documents[{id, name, sizeBytes?, fileType?, url?}]`; `spaarke_send_email { to, cc, subject, body, bodyFormat, regardingEntityType,
  regardingId, attachmentDocumentIds, idempotencyKey }` returning `structuredContent.communicationId`.
- D5 The picker UI is injected (`pickDocument`); the widget (task 040) owns it. The engine's attach menu ("Attach" -> "Link documents") is the entry point.
- D6 Test file is `useMcpEmailHostProps.test.tsx` (the POML lists `.ts`): it mounts JSX (the harness). Scope beyond the criteria, one line each:
  hook key stability across re-render and on matter change (the "once per session" contract); transport-failure propagation; non-send URL refusal.
- D7 Dark-mode UI test: the host adds no styling or colours, so there is nothing new to check; the engine's tokens are unchanged.

## For the main session
- CAPABILITY-INDEX row (not edited here): "Email compose host for a Copilot widget: `createMcpEmailComposeHandlers` / `useMcpEmailHostProps` / `McpToolCaller` in `@spaarke/ui-components` EmailComposer".
- No defects found outside scope; no issues to file.
- rework: none task 011
