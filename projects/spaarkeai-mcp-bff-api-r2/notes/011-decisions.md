# Task 011 - EmailComposer MCP host: notes and decisions

## Host props: v1 vs undefined (step 1)
v1 (provided by `createMcpEmailComposeHandlers` / `useMcpEmailHostProps`): typed To/Cc/subject/body (engine-owned, no host prop needed),
`associations` (the host-preset regarding matter), `attachmentSources: [{kind:'related'}]`, `recordLookupCatalog` (document only),
`onLookupRecord` (answers `sprk_document` only, via `spaarke_list_documents`; present only when the widget supplies `pickDocument`),
`authenticatedFetch` (Send adapter, D1), `idempotencyKey`.
Left undefined (engine hides the control): `onSearchRecipients`, `onLookupRecipients`, `onAddRelationship`, `onUploadLocalAttachment`,
`onListEmailTemplates`, `onRenderEmailTemplate`, `onDraftWithAi`. `attachmentSources` omits `local`, so "Add files" is hidden.
The escalation trigger did not fire: each undefined callback hides its control (harness test asserts it). Bcc, Internal Only and the chip remove have no callback to leave undefined,
so fix round 1 added three optional engine props (D8).

## Decisions
- D1 Send path: the engine has no send callback; it always sends through `sendCommunication()` over the injected `authenticatedFetch`, so the MCP host
  exports `authenticatedFetch`, an adapter for the one route `POST /api/communications/send` that calls `spaarke_send_email`.
  Failure mapping (review fix round 1, mirrors `spaarke-decisions/src/mcp/mcpFetch.ts` `errorResponse`): a tool `isError` carries
  `structuredContent = { reasonCode, message, status, correlationId? }` (McpToolResults.cs) and becomes
  `SendCommunicationError(structured.status ?? 422, result.reasonCode ?? structured.reasonCode, structured.message ?? text, structured.correlationId)`, so a 403 stays a 403,
  a 500 stays a 500 (`mayHaveBeenSent` is then true) and the support reference reaches the failure dialog. An `isError` with NO `structuredContent` (bad arguments, a server
  exception) and a rejected `callTool` are an unknown outcome: `SendCommunicationError(0, 'NETWORK', ...)`, which the composer treats as "may not have been sent" (no delete, check Sent Items).
  No automatic retry.
  Widget (task 040) wiring: spread `useMcpEmailHostProps(...)` into `EmailComposer`: `authenticatedFetch`, `associations`, `attachmentSources`, `recordLookupCatalog`, `onLookupRecord`
  AND the four switches `sendMode`, `showBcc`, `showInternalOnly`, `associationsRemovable` (see D8). `idempotencyKey` is a live getter, not an `EmailComposer` prop: do not copy it.
- D2 The regarding matter comes from the host option, never from the request body's associations (a tampered association cannot redirect the archive).
- D3 Idempotency key (review fix round 1): one key per compose session (`crypto.randomUUID`, fallback time+random), sent on every Send. In `useMcpEmailHostProps` it lives in a
  ref that resets only when `regarding.id` changes; `caller` and `pickDocument` are read through refs, so a new `callTool` object, an inline picker or a late-arriving matter
  name never mints a new key (a retry after a timeout must replay the same key, or it sends twice). A Send that returned a `communicationId` spends the key: the next
  deliberate Send gets a new one (otherwise the server would replay the first email). A failed Send, or a success with no id ("sent, unconfirmed"), keeps the key.
- D4 Tool argument contract (tasks 023/040 must match; their POMLs do not fix it): `spaarke_list_documents { regardingEntityType: 'sprk_matter', regardingId }`
  returning `structuredContent.documents[{id, name, sizeBytes?, fileType?, url?}]`; `spaarke_send_email { to, cc, subject, body, bodyFormat, regardingEntityType,
  regardingId, attachmentDocumentIds, idempotencyKey }` returning `structuredContent.communicationId`. Both tools report a failure as `isError: true` with
  `structuredContent = { reasonCode, message, status, correlationId? }` (McpToolResults.cs).
  - Idempotency-key refusal semantics (task 040 implements): the server stores only SUCCESSFUL or OUTCOME-UNKNOWN attempts under a key. A definite 4xx refusal (validation,
    permission, not found) does NOT consume the key, so the user can correct the email and press Send again with the same key; a replayed key whose first attempt succeeded
    returns that first result instead of sending again.
  - `attachmentDocumentIds` pre-filled from a model draft skip the picker (the picker only lists the matter's documents), so the picker is not the control that keeps an attachment
    inside the matter. Task 040's same-matter check on `attachmentDocumentIds` (every id must belong to the regarding matter) is that control; the host does not enforce it.
- D5 The picker UI is injected (`pickDocument`); the widget (task 040) owns it. The engine's attach menu ("Attach" -> "Link documents") is the entry point.
  `pickDocument` receives a `McpDocumentListOutcome`: `{ documents }` or `{ error, reference? }`; the widget MUST show `error` (a refused or unanswered list call is otherwise
  a menu item that does nothing). A rejected `callTool` is caught and becomes an `error` outcome.
- D6 Test file is `useMcpEmailHostProps.test.tsx` (the POML lists `.ts`): it mounts JSX (the harness). Scope beyond the criteria, one line each:
  hook key stability across re-render and on matter change (the "once per session" contract); transport-failure propagation; non-send URL refusal.
- D7 Dark-mode UI test: the host adds no styling or colours, so there is nothing new to check; the engine's tokens are unchanged.
- D8 Controls the send tool cannot honour (review fix round 1, F1) - ADR-012 SHARED-COMPONENT CHANGE to `EmailComposer`: three optional, additive props, each defaulting to today's behaviour
  (every existing EmailComposer test passes unchanged): `showBcc` (default true; false hides the Bcc toggle and field), `showInternalOnly` (default true; false hides the
  External | Internal Only control), `associationsRemovable` (default true; false renders the "Related to" chips without the remove x). The MCP host returns
  `showBcc: false`, `showInternalOnly: false`, `associationsRemovable: false` and a fixed `sendMode: 'sharedMailbox'` (an existing prop; it hides the From switcher), so a control is never
  shown whose value `spaarke_send_email` would drop. Backstop in the adapter: a request that still carries `bcc`, `isInternalOnly: true`, `sendMode: 'User'`, or associations that no longer
  include the matter is refused BEFORE any tool call with a 422 `SendCommunicationError` (codes `McpHostRefusal.*`, a clear sentence, `mayHaveBeenSent` false), never dropped silently.
  `isInternalOnly: false` and an empty `bcc` pass (that is what an untouched composer sends).
  The "Related to" chips stay visible (the matter is a fixed label, not removable); `showAssociations` is unchanged. Other associations in a request beyond the matter are not refused
  (the host presets none and offers no way to add one).
- D9 Harness assertions (fix round 1): the old "absent" checks (`queryByLabelText(/template/i)`, etc.) could never fail. They are now exact-label checks (Apply template, Draft with AI,
  From mailbox, Bcc, Internal Only / External radios, the chip remove, Add files after opening the Attach menu), and a control mount without the host switches proves From, Bcc, Internal Only and
  the chip remove DO render, so each "absent" assertion can fail.

## For the main session
- CAPABILITY-INDEX row (not edited here): "Email compose host for a Copilot widget: `createMcpEmailComposeHandlers` / `useMcpEmailHostProps` / `McpToolCaller` in `@spaarke/ui-components` EmailComposer".
- No defects found outside scope; no issues to file.
- rework: task 011 review fix round 1 (F1 x4, F2 x2, K2): D1, D3, D4, D5, D8, D9 above
