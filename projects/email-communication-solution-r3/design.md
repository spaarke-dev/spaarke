# Email Communication Solution R3 — Design

> **Status**: Draft — ready for `/design-to-spec`
> **Date**: 2026-06-05
> **Parent project**: [email-communication-solution-r2](../email-communication-solution-r2/) (server-side, completed 2026-04)
> **Driver document**: [Communication Architecture Assessment 2026-06-05](../../docs/assessments/communication-architecture-assessment-2026-06-05.md)
> **Scope owner**: Spaarke platform

---

## 1. Background

### 1.1 What R2 delivered

R2 unified the **server-side** Communication Service:
- `sprk_communicationaccount` entity as the source of truth for mailbox configuration (replaced static appsettings).
- Graph subscriptions replaced Server-Side Sync for inbound.
- OBO send path added (User mode in addition to SharedMailbox mode).
- `.eml` archival + `sprk_document` linkage + association resolver.
- 13 services in `Services/Communication/`, gated by `CommunicationModule` (DI).

The server-side foundation is stable. R3 does NOT touch it.

### 1.2 What R2 did not address (and R3 must)

The client side. The 2026-06-05 architecture assessment found:

- **6 distinct client implementations** of `POST /api/communications/send` (only 1 uses the typed wrapper).
- **3 different error contracts** for the same endpoint.
- **5 different recipient-normalization implementations** across the callers.
- **LegalWorkspace solution has stale duplicate forks** of 8+ wizards — shared-lib copies are 6–85 days newer.
- **No canonical email composer UI**. At least 7 different `*EmailStep` / `*EmailDialog` / `*EmailWizard` components exist in the shared library, each with its own UX subset.
- **Dataverse standard form for `sprk_communication`** is the form users see when they navigate to a communication record — currently the auto-generated Power Apps form, not aligned with Spaarke UX.
- **Latent bug in the canonical path**: `DocumentEmailWizard` sends `sprk_document` GUIDs to `attachmentDocumentIds`, but the BFF expects SPE driveItem IDs.

### 1.3 Recurring cost

Each fragmented implementation has produced at least one production bug:
- 2026-06-05: SummarizeFilesDialog hardcoded `bodyFormat: 'Text'`, BFF rejected.
- 2026-05 (per FAILURE-MODES.md AP-4): FilePreviewDialog URL drift (missing `/api`).
- Latent: `attachmentDocumentIds` semantics mismatch in DocumentEmailWizard.
- Recurring: LegalWorkspace forks lag fixes that land in shared lib.

R3 ends this class of bug at its source.

---

## 2. Goals & Non-goals

### 2.1 Goals

1. **One canonical email composer component** (`<EmailComposer />`) in `@spaarke/ui-components`, used by every email-send surface in Spaarke.
2. **One canonical Code Page** that mounts `<EmailComposer />` as a standalone full-page form, replacing the auto-generated Dataverse standard form for `sprk_communication`.
3. **One canonical client send wrapper** (`sendCommunication()` typed wrapper), with documented semantics for `attachmentDocumentIds`, `associations`, error handling.
4. **Retire the 6 ad-hoc send-email implementations** by migrating their callers to the canonical surfaces.
5. **Retire the LegalWorkspace solution's duplicate component forks** for email-touching components.
6. **One Communication ADR** codifying the canonical pattern so future surfaces don't re-fragment.
7. **Documentation updates** that close the gaps the assessment identified.

### 2.2 Non-goals (R3 explicitly does NOT do)

- Touch the server-side Communication Service. R2's pipeline stays as-is.
- Build an "inbox" or "thread browser" UI. Server produces `sprk_communication` records; reading them back at scale is a separate project.
- Address the Outlook add-in. Out of scope.
- Touch `/api/v1/emails/*` (the Power Apps Email-activity subsystem). Out of scope — see §3.3.
- Implement non-email `CommunicationType` values (TeamsMessage, SMS, Notification). Email only.
- Refactor every wizard's overall structure. We swap the email step / dialog only.

### 2.3 Success criteria

- Exactly **one** client function (`sendCommunication`) handles every email send across the platform.
- Exactly **one** React component (`<EmailComposer />`) renders the email-send UX across the platform.
- Zero callers send inline-`fetch` to `/api/communications/send`.
- LegalWorkspace solution contains zero forked copies of email-touching shared-lib components.
- `sprk_communication` form in Dataverse opens our Code Page, not the auto-generated form.
- The Communication ADR exists and is referenced from `bff-extensions.md` and `CLAUDE.md`.

---

## 3. In-scope vs. Out-of-scope (with rationale)

### 3.1 In scope

| Item | Why |
|---|---|
| Canonical `<EmailComposer />` component | The fragmentation root. Without this, ad-hoc implementations will keep appearing. |
| Code Page mounting `<EmailComposer />` for `sprk_communication` form | Replaces auto-generated Dataverse form with a Spaarke UX. Also gives ribbon entry points and "compose new" a host. |
| `sendCommunication()` typed wrapper semantics fix | Documents `attachmentDocumentIds` driveItem-vs-document drift; aligns single error contract. |
| Migration of 5 create-record wizards (Matter, Project, Event, Todo, WorkAssignment) | Their `SendEmailStep` is the canonical wizard touchpoint. |
| Migration of SummarizeFilesDialog (shared lib + LegalWorkspace fork) | Today's bug source. |
| Migration of FilePreviewDialog (LegalWorkspace) | Has inline fetch, no canonical client. |
| Migration of DocumentEmailWizard | Already uses `sendCommunication`, but has the `attachmentDocumentIds` latent bug. |
| Webresource `sprk_communication_send.js` alignment | Ribbon entry on `sprk_communication` form; uses MSAL bootstrap (not `@spaarke/auth`). Either retire (replaced by Code Page) or align. |
| LegalWorkspace solution duplicate forks (email-touching components) | Retire the forks; re-export from `@spaarke/ui-components`. |
| Communication ADR | Codify the canonical pattern. |
| Documentation updates | Close gaps from the assessment. |

### 3.2 Out of scope — Outlook add-in

The Outlook add-in's "save email to Spaarke" creates a `sprk_document` (with `.eml` to SPE). It does NOT create a `sprk_communication` record. R3 does not unify these two archival paths. If the add-in is touched later, that's a separate project.

### 3.3 Out of scope — `/api/v1/emails/*` subsystem

Investigation confirmed:
- Project name: "Email-to-Document Automation"
- CLAUDE.md states it converts **Power Platform Email activities** into `sprk_document` records.
- Triggered by (a) Dataverse Service Endpoint webhook firing when an email activity is created, or (b) ribbon button on the Power Apps Email entity form (`sprk_emailactions.js`).
- **Power Platform Email activities are not used by Spaarke** (no Server-Side Sync, no Outlook→Activity sync per user direction).
- Both triggers therefore never fire. The subsystem is **functionally dead in production usage** even though built and deployed.

R3 does NOT touch this subsystem. A separate backlog item recommends investigating its retirement (file as a referral, do not block R3 on it).

### 3.4 Out of scope — other CommunicationType values

`sprk_communication` supports Email / TeamsMessage / SMS / Notification. Only Email is implemented today. R3 designs `<EmailComposer />` for Email only. The component's contract should not preclude future Teams/SMS/Notification composers — they would be sibling components with the same shape, but R3 builds none of them.

### 3.5 Out of scope — `sprk_communication` browse / thread / inbox UI

R3 builds the **form** for a single `sprk_communication` record (compose / view / reply / forward). It does NOT build a list view, inbox surface, or thread browser. Those are separate projects.

### 3.6 Out of scope — server-side Communication Service refactor

R2 already did this. R3 makes no server changes except:
- Fix `attachmentDocumentIds` field name / semantics drift (either rename or document; see §6.4).
- Possibly add server-side validation for known constants the canonical client now sends consistently.
- Add Communication ADR. ADR is a documentation artifact, not a code change.

---

## 4. Architectural shape

R3 establishes four pieces with crisp boundaries:

```
┌─────────────────────────────────────────────────────────────────┐
│  CALLERS                                                         │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────────────┐  │
│  │  Wizards     │  │  Code Page   │  │  Ribbon button on    │  │
│  │  (5 create-  │  │  (sprk_      │  │  sprk_communication  │  │
│  │  record +    │  │  communica-  │  │  form (or retire)    │  │
│  │  Summarize + │  │  tion form   │  │                       │  │
│  │  FilePreview)│  │  replacement)│  │                       │  │
│  └──────┬───────┘  └──────┬───────┘  └──────────┬───────────┘  │
└─────────┼─────────────────┼──────────────────────┼─────────────┘
          │                 │                      │
          ▼                 ▼                      ▼
┌─────────────────────────────────────────────────────────────────┐
│  PRESENTATION LAYER (one component)                              │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │  <EmailComposer />                                       │  │
│  │  - Modes: compose | view | reply | forward | draft       │  │
│  │  - Mount: inline-step | dialog | full-page              │  │
│  │  - Attachment sources: local | SPE | related | wizard   │  │
│  │  - Validation, error surfacing, dirty/cancel handling   │  │
│  │  in @spaarke/ui-components                              │  │
│  └──────────────┬───────────────────────────────────────────┘  │
└─────────────────┼───────────────────────────────────────────────┘
                  │
                  ▼
┌─────────────────────────────────────────────────────────────────┐
│  CLIENT API LAYER (one typed function)                           │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │  sendCommunication(opts, { authenticatedFetch, base })   │  │
│  │  - Documented attachmentDocumentIds = driveItem IDs       │  │
│  │  - Throws SendCommunicationError on non-2xx              │  │
│  │  - Single, parsed-ProblemDetails error contract           │  │
│  │  - in @spaarke/ui-components/services                     │  │
│  └──────────────┬───────────────────────────────────────────┘  │
└─────────────────┼───────────────────────────────────────────────┘
                  │
                  ▼  POST /api/communications/send
                  │
┌─────────────────────────────────────────────────────────────────┐
│  SERVER (existing — R2)                                          │
│  CommunicationService.SendAsync()  →  Graph SendMail             │
│  → sprk_communication record + .eml archive + sprk_document     │
│  → attachment sprk_documents + associations                     │
└─────────────────────────────────────────────────────────────────┘
```

Boundaries:
- **Callers** never call `fetch` to `/api/communications/send` directly. They mount `<EmailComposer />` OR call `sendCommunication()` (rare — when programmatic send is needed without UI).
- **`<EmailComposer />`** never owns business decisions about the send pipeline. It collects user input, validates, and calls `sendCommunication()`.
- **`sendCommunication()`** never owns UX decisions. It is a typed transport over the BFF endpoint.
- **Server** is untouched.

---

## 5. The canonical `<EmailComposer />` component + semantic wrappers

**Decision (design review 2026-06-05, revised)**: the canonical engine is `<EmailComposer />`, but callers do NOT typically import it directly. They import one of three thin semantic wrappers (`<SendEmailStep />` / `<SendEmailDialog />` / `<SendEmailPage />`), each fixing one `mount` value and exposing a tighter prop API for that context. Direct `<EmailComposer />` consumption is reserved for advanced cases.

### 5.1 Location & dependencies

- Path: `src/client/shared/Spaarke.UI.Components/src/components/EmailComposer/`
- Files:
  - `EmailComposer.tsx` — the canonical engine
  - `EmailComposer.types.ts` — props + sub-types (shared with wrappers)
  - `EmailComposerContext.tsx` — internal state machine
  - `attachments/` — attachment source pickers (local / SPE / related / wizard)
  - `wrappers/`
    - `SendEmailStep.tsx` — wizard step caller (`mount='inline'`)
    - `SendEmailDialog.tsx` — popup caller (`mount='dialog'`)
    - `SendEmailPage.tsx` — Code Page caller (`mount='page'`)
  - `index.ts` — barrel export (exports the engine + all three wrappers)
- Peer dependencies: Fluent UI v9 (per `ADR-021`), React 18.
- No direct import of `@spaarke/auth` (decoupling rule per `communicationApi.ts:21-23`). `authenticatedFetch` is injected via props.

### 5.1.1 The wrapper layer — caller-facing API

Each wrapper is ~30–60 LOC, sets its `mount` value, and exposes a semantic prop API for its context. The wrappers contain NO business logic — they delegate to `<EmailComposer />`.

```tsx
// SendEmailStep.tsx — wizard step caller
export interface ISendEmailStepProps {
  mode?: EmailComposerMode;  // default 'compose'
  composerRef: React.Ref<IEmailComposerHandle>;  // wizard owns the Send action
  authenticatedFetch: AuthenticatedFetchFn;
  bffBaseUrl: string;
  initialTo?: string[];
  initialSubject?: string;
  initialBody?: string;
  associations?: ICommunicationAssociation[];
  attachmentSources?: EmailAttachmentSource[];
  wizardContext?: IWizardContext;
  onStateChange?: (state: IEmailComposerState) => void;
}

export function SendEmailStep(props: ISendEmailStepProps) {
  return <EmailComposer mount="inline" mode={props.mode ?? 'compose'} {...props} />;
}

// SendEmailDialog.tsx — popup caller
export interface ISendEmailDialogProps {
  open: boolean;
  onClose: () => void;
  mode?: EmailComposerMode;  // default 'compose'
  authenticatedFetch: AuthenticatedFetchFn;
  bffBaseUrl: string;
  initialTo?: string[];
  initialSubject?: string;
  associations?: ICommunicationAssociation[];
  attachmentSources?: EmailAttachmentSource[];
  onSent?: (communicationId: string) => void;
  onError?: (err: SendCommunicationError) => void;
}

export function SendEmailDialog(props: ISendEmailDialogProps) {
  return (
    <EmailComposer
      mount="dialog"
      mode={props.mode ?? 'compose'}
      open={props.open}
      onCancel={props.onClose}
      onSent={(r) => { props.onSent?.(r.communicationId); props.onClose(); }}
      onError={props.onError}
      {...rest}
    />
  );
}

// SendEmailPage.tsx — Code Page caller
export interface ISendEmailPageProps {
  mode: EmailComposerMode;  // required — driven by URL parameter
  communicationId?: string;  // for view/reply/forward/draft
  authenticatedFetch: AuthenticatedFetchFn;
  bffBaseUrl: string;
  initialTo?: string[];
  initialSubject?: string;
  initialBody?: string;
  associations?: ICommunicationAssociation[];
  onSent?: (communicationId: string) => void;
  onClose?: () => void;  // close Code Page (e.g. navigate away)
}

export function SendEmailPage(props: ISendEmailPageProps) {
  return <EmailComposer mount="page" {...props} onCancel={props.onClose} />;
}
```

Caller call-site comparison:

```tsx
// Wizard:
<SendEmailStep composerRef={ref} initialSubject={subj} associations={a} ... />

// Dialog from FilePreview:
<SendEmailDialog open={open} onClose={close} initialTo={[recipient]} associations={a} ... />

// Code Page:
<SendEmailPage mode={modeFromUrl} communicationId={idFromUrl} associations={a} ... />
```

vs. direct engine use (allowed but verbose):

```tsx
<EmailComposer mount="inline" mode="compose" composerRef={ref} initialSubject={subj} associations={a} ... />
<EmailComposer mount="dialog" mode="compose" open={open} onCancel={close} onSent={...} initialTo={[recipient]} ... />
<EmailComposer mount="page" mode={modeFromUrl} communicationId={idFromUrl} associations={a} ... />
```

### 5.1.2 When to use the engine directly vs. a wrapper

| Use case | Import |
|---|---|
| Standard wizard step | `<SendEmailStep />` |
| Standard popup dialog | `<SendEmailDialog />` |
| Standard Code Page mount | `<SendEmailPage />` |
| Non-standard mount needs (e.g. side panel, drawer, sheet) | `<EmailComposer />` directly + custom layout container |
| New mount context that should become a wrapper | Add a new wrapper in `wrappers/` |

The default expectation in code review: callers import a wrapper. Direct `<EmailComposer />` use requires a comment explaining why a wrapper is not appropriate.

### 5.2 Modes

```ts
type EmailComposerMode =
  | 'compose'       // blank form, user composes from scratch
  | 'view'          // read-only display of an existing sprk_communication record
  | 'reply'         // pre-filled reply to an existing record (single recipient = original From; subject prefixed "Re: ")
  | 'forward'       // pre-filled forward (no recipients; subject prefixed "Fwd: "; body wraps original; attachments carried by default)
  | 'draft';        // editable, loaded from a saved draft sprk_communication record (statuscode = Draft)
```

| Mode | Mount surfaces | Source data |
|---|---|---|
| `compose` | Wizard step, Code Page (compose-new ribbon entry), `DocumentEmailWizard` dialog | none (or wizard pre-fill) |
| `view` | Code Page (existing sprk_communication record open) | `sprk_communication` record via `Xrm.WebApi` |
| `reply` | Code Page (from `view` mode's "Reply" button), Code Page launched via URL with `?mode=reply&id={commId}` | `sprk_communication` record |
| `forward` | Same as reply | `sprk_communication` record |
| `draft` | Code Page (from `view` mode if statuscode=Draft), wizard step's "Save draft" | `sprk_communication` record |

### 5.3 Mount shapes

```ts
type EmailComposerMount =
  | 'inline'        // embedded in a wizard step. Layout fills parent height. No internal Send/Cancel buttons — wizard owns navigation.
  | 'dialog'        // mounted inside a Fluent Dialog. Internal action buttons (Send / Save Draft / Cancel).
  | 'page';         // full-page (Code Page). Internal action buttons + close behavior.
```

The wizard step shape needs `inline` because today's wizard step components render fields and the wizard frame owns the next/back/skip buttons. The composer must NOT render its own action buttons in `inline` mode.

### 5.4 Props contract

```ts
interface IEmailComposerProps {
  // — Mode & mount —
  mode: EmailComposerMode;
  mount: EmailComposerMount;

  // — Auth (injected per shared-lib decoupling rule) —
  authenticatedFetch: AuthenticatedFetchFn;
  bffBaseUrl?: string;  // host only, no /api — per [[feedback_bff-url-normalization]]

  // — Source data (required for view/reply/forward/draft) —
  communicationId?: string;  // sprk_communication GUID

  // — Pre-fill (compose mode) —
  initialTo?: string[];
  initialCc?: string[];
  initialSubject?: string;
  initialBody?: string;
  initialBodyFormat?: 'HTML' | 'PlainText';

  // — Associations —
  // These get stamped onto sprk_communication on send.
  // §5.6.6 explains how the composer surfaces them in the UI.
  associations?: ICommunicationAssociation[];
  // When true, composer shows the associations as Fluent Tags (read-only)
  // so users see what entities will be linked. Default: true.
  showAssociations?: boolean;

  // — Attachment sources —
  // Composer renders the source pickers in the order specified. Empty = no
  // attachment UI. Default: ['wizard', 'related', 'local', 'spe'] (when wizard
  // context is present), else ['local', 'related', 'spe'].
  attachmentSources?: EmailAttachmentSource[];

  // — Wizard context (only meaningful in 'inline' mount with attachmentSources
  //   including 'wizard') —
  wizardContext?: IWizardContext;  // see §5.5

  // — Send-side behavior —
  // sharedMailbox sends via the verified Spaarke mailbox (default). user
  // sends via the caller's OBO token (the user's own mailbox via Graph
  // /me/sendMail). Reveals a switcher in the UI when this prop is allowed.
  sendMode?: 'sharedMailbox' | 'user';
  // Optional approved-sender override (sharedMailbox mode only).
  fromMailbox?: string;
  // Archive sent .eml to SPE. Default: true.
  archiveToSpe?: boolean;

  // — Validation & feature gates —
  allowEmptyBody?: boolean;  // default false; some compose flows allow it (e.g. "just an attachment")
  maxRecipients?: number;    // default 50 (matches BulkSend cap)

  // — Callbacks (inline mount) —
  // Wizard owns the action buttons; it polls the composer for state via these:
  onStateChange?: (state: IEmailComposerState) => void;  // fires on every meaningful change

  // — Callbacks (dialog/page mount) —
  onSent?: (result: { communicationId: string }) => void;
  onCancel?: () => void;
  onError?: (err: SendCommunicationError) => void;
  onSaveDraft?: (result: { communicationId: string }) => void;

  // — Imperative handle (for wizard "Next" button to trigger send) —
  // Wizard captures this via React.useRef and calls handle.send() / handle.validate().
  composerRef?: React.Ref<IEmailComposerHandle>;
}

interface IEmailComposerHandle {
  validate(): IValidationResult;     // returns errors; does not throw
  send(): Promise<{ communicationId: string }>;
  saveDraft(): Promise<{ communicationId: string }>;
  getState(): IEmailComposerState;
}

interface IEmailComposerState {
  to: string[];
  cc: string[];
  bcc: string[];
  subject: string;
  body: string;
  bodyFormat: 'HTML' | 'PlainText';
  attachments: IAttachmentItem[];
  sendMode: 'sharedMailbox' | 'user';
  fromMailbox?: string;
  archiveToSpe: boolean;
  isDirty: boolean;
  validation: IValidationResult;
}
```

### 5.5 Wizard context (attachment source = 'wizard')

When a wizard hosts the composer and wants users to attach files the wizard already uploaded:

```ts
interface IWizardContext {
  // Files uploaded earlier in this wizard. The composer shows them as a
  // pre-checked attachment source. The user can deselect any.
  uploadedFiles: {
    documentId: string;
    driveItemId: string;        // ← this is what gets sent to the BFF
    fileName: string;
    mimeType: string;
    sizeBytes: number;
  }[];
}
```

This is how the canonical composer handles the "include the files I just uploaded" pattern used in the SummarizeFiles and document-upload flows today.

### 5.6 Sub-components (rendered by the composer)

#### 5.6.1 Recipient field (`<RecipientField />`)
- Used for To / Cc / Bcc.
- Accepts string-with-`;,`-separator input (matches today's de facto contract — recipients pasted from Outlook).
- Resolves against Spaarke user/contact directory (`searchUsersAndContacts` already exists in the shared lib — `services/userLookup.ts`).
- Renders Fluent Tags for resolved recipients; free-text email allowed (validated by regex on commit).
- Single canonical normalization (replaces 5 existing implementations).

#### 5.6.2 Subject field (`<TextField />`)
- Required (unless mode = `view`).
- Single line, no character cap below BFF's (BFF will reject >998 char single header per RFC 5322; composer warns at 200).

#### 5.6.3 Body editor (`<BodyEditor />`)
- Rich text editor with HTML output (uses existing wizard rich-text editor primitive if shared; otherwise Fluent's `<Textarea />` for plain text + lightweight HTML editor for HTML).
- Reflects `bodyFormat`. Toggle between HTML / PlainText.
- Plain text mode shows monospace `<Textarea />`. HTML mode shows the rich editor.
- On send, the composer sends what was authored — does NOT convert.

#### 5.6.4 Attachment list (`<AttachmentList />`)
- One section per `attachmentSources[]` entry.
- Each entry: `name`, `size`, source-badge, remove button.
- Enforces composer's caps (matches BFF: 150 attachments, 35 MB total; warns at 25 MB per AP-4 / `CHAT-ATTACHMENT-POLICY.md` analog).

#### 5.6.5 Send-mode switcher (`<SendModeRadio />`)
- Only rendered if `sendMode` is undefined in props (i.e., the host allows user choice).
- Two options: "Send from Spaarke" (sharedMailbox) vs "Send from my mailbox" (user).
- Defaults to `sharedMailbox` per `send-email-integration.md:25`.

#### 5.6.6 Associations chips (`<AssociationChips />`)
- Renders `associations[]` as Fluent Tags, e.g. "Matter: Smith v. Jones", "Document: NDA-v3.pdf".
- Read-only by default. Future: "Add association" button (out of scope for R3).

#### 5.6.7 Action bar (`<ComposerActionBar />`)
- Rendered only in `dialog` and `page` mounts.
- Buttons: Send, Save Draft, Cancel.
- In `view` mode: Edit (switches to draft if status=Draft, else read-only), Reply, Forward, Close.

### 5.7 React version target (React 18 only)

**Decision (design review 2026-06-05)**: `<EmailComposer />` is React 18 only.

The "Send Email" entry points launch a Code Page (React 18), not a PCF. No PCF mounts the composer directly — the existing PCFs (`SemanticSearchControl`, `SpeDocumentViewer`) launch wizard Code Pages for email, they don't host an email composer themselves. ADR-022 (PCF React 16) does not apply to this component.

Implications:
- Free to use `useId`, `useTransition`, `useDeferredValue`, `<Suspense />`, and any other React 18 hook/feature where useful.
- No dual-target build constraints.
- If a future PCF needs to embed the composer directly (rather than launching a Code Page), that's a separate decision — the composer's API is not currently designed for React 16.

### 5.8 Validation contract

`validate()` returns:

```ts
interface IValidationResult {
  ok: boolean;
  errors: {
    field: 'to' | 'subject' | 'body' | 'attachments' | 'from';
    code: string;
    message: string;
  }[];
}
```

Codes (canonical, used everywhere — not strings duplicated per caller):
- `TO_REQUIRED`, `TO_INVALID_EMAIL`, `TO_TOO_MANY`
- `SUBJECT_REQUIRED`
- `BODY_REQUIRED` (when `allowEmptyBody=false`)
- `ATTACHMENT_TOO_LARGE`, `ATTACHMENTS_TOO_MANY`, `ATTACHMENT_BLOCKED_TYPE`
- `FROM_REQUIRED` (user mode), `FROM_NOT_APPROVED` (sharedMailbox mode)

### 5.9 Error contract

`send()` throws on non-2xx:

```ts
class SendCommunicationError extends Error {
  readonly status: number;
  readonly code: string;          // BFF ProblemDetails code (e.g. 'DAILY_SEND_LIMIT_REACHED')
  readonly detail: string;
  readonly correlationId?: string;
}
```

This is the SINGLE error contract callers reason about. Replaces today's 3 different shapes (`{success, warning}`, plain `Error`, warnings-array push).

### 5.10 Styling — variants per mount

**Decision (design review 2026-06-05)**: the composer has explicit visual variants per mount, not a single "default Fluent styling adapts."

| Mount | Visual character | Rationale |
|---|---|---|
| `page` (Code Page) | **Form-replacement visual language**: full-width, generous spacing, section dividers, prominent action bar at bottom-right, page-level header showing entity context ("Email — Smith v. Jones Matter"). Targets the "this is the canonical Email record page" experience that replaces the standard Dataverse form. | Surface 2 (Form Component Control) makes this the default user-visible form for `sprk_communication`. It must feel like a primary entity form, not a popup. |
| `dialog` (Fluent Dialog) | **Compact dialog visual language**: bounded width (e.g., 600px), compressed spacing, dialog header + footer action bar. Body editor sized for shorter messages with scrollable overflow. | Popup-style use from PCFs and ribbon-launched dialogs (e.g., DocumentEmailWizard) — should feel like a focused task, not a page. |
| `inline` (wizard step) | **Wizard step visual language**: fills wizard step container, no internal header (wizard provides), no internal action bar (wizard navigation owns Send), matches existing wizard step typography and spacing. | Wizards already have a consistent step look; the composer must blend in. |

Shared design tokens (Fluent v9 semantic tokens) across all mounts. Variants differ in layout density and chrome, not color/typography palette. This keeps the `@spaarke/ui-components` theming pipeline intact.

A single styling primitive (`<ComposerLayout variant={'page' | 'dialog' | 'inline'}>...</ComposerLayout>`) wraps the composer body and applies mount-specific layout. Sub-components (RecipientField, BodyEditor, AttachmentList) render the same regardless of mount.

The page-mount visual design is the more demanding one — it needs to read as a first-class entity form. Wave 1 should land it as the primary visual deliverable; the dialog and inline mounts are simpler derivatives.

### 5.11 Threading & reply-chain associations (IN SCOPE per design review)

**Decision (design review 2026-06-05)**: Reply / forward / thread closure is IN SCOPE for R3.

The composer's `reply` and `forward` modes need to know what record the user is replying to so the new `sprk_communication` can be linked into the thread.

Client-side requirements:
- `reply` mode pre-fills `to` from the original record's `from`, prepends `Re: ` to subject, and stamps `sprk_inreplyto = <original sprk_communication GUID>` on the new record.
- `forward` mode leaves `to` blank, prepends `Fwd: ` to subject, wraps the original body, includes original attachments by default (user can deselect), and stamps `sprk_inreplyto`.
- Both modes pass through the original record's `associations` so the reply/forward stays linked to the same matter/document.

Server-side requirements (see §9.2):
- `CommunicationService.SendAsync()` must capture the REAL `Internet-Message-Id` of the just-sent message and stamp it onto `sprk_communication.sprk_internetmessageid`. Today this field is null for outbound records (assessment §3.3, observation 9 — `GraphMessageId` is faked from `correlationId`).
- Once the real `Internet-Message-Id` is stored, `IncomingAssociationResolver.cs:171` can match inbound replies back to the original outbound record via the `In-Reply-To` header, closing the thread loop.

Dataverse schema:
- Verify `sprk_communication.sprk_inreplyto` lookup exists pointing back to the parent `sprk_communication`. If not, add it.
- Verify `sprk_communication.sprk_internetmessageid` text column exists. If not, add it.

If schema additions are required, they happen in Wave 0 (foundation) so subsequent waves can rely on the columns.

---

## 6. Canonical `sendCommunication` typed wrapper

### 6.1 Current state

The wrapper exists at `src/client/shared/Spaarke.UI.Components/src/services/communicationApi.ts`. The contract is mostly correct. Issues:

1. **`attachmentDocumentIds` is mis-named**. The field name says "DocumentIds" but the BFF expects SPE driveItem IDs. The wrapper's docstring (lines 8-19) calls this out; callers ignore it; `DocumentEmailWizard.tsx:494` sends `sprk_document` GUIDs. Latent bug.
2. **Error extraction is correct but the error type is `Error`** — callers can't type-check by status code without re-parsing the message.
3. **No `bffBaseUrl` is required** — relative path works when `authenticatedFetch` is canonical, but explicit `bffBaseUrl` makes it host-pinnable and is now used by every caller in practice.

### 6.2 Refinements in R3

```ts
// Rename for clarity AND fix the latent bug:
//  - In the wrapper API: accept BOTH `attachmentDriveItemIds` (canonical going forward)
//    and `attachmentDocumentIds` (legacy alias, deprecated, logs a warn).
//  - In the BFF API: add `AttachmentDriveItemIds` as the canonical field; keep
//    `AttachmentDocumentIds` as a deprecated alias that maps to the same backing
//    field. This avoids a breaking change while making the canonical clear.
export interface SendCommunicationOptions {
  to: string[];
  cc?: string[];
  bcc?: string[];
  subject: string;
  body: string;
  bodyFormat?: 'HTML' | 'PlainText';

  /** SPE driveItem IDs. Max 150 attachments, 35 MB total (BFF enforced). */
  attachmentDriveItemIds?: string[];

  /**
   * @deprecated Use attachmentDriveItemIds. Historic field name says
   * "Document" but the BFF expects driveItem IDs. Will be removed in R4.
   */
  attachmentDocumentIds?: string[];

  archiveToSpe?: boolean;
  associations?: ICommunicationAssociation[];
  sendMode?: CommunicationSendMode;
  fromMailbox?: string;
  correlationId?: string;
}

// Typed error class (replaces string-message Error):
export class SendCommunicationError extends Error {
  readonly name = 'SendCommunicationError';
  readonly status: number;
  readonly code: string;
  readonly detail: string;
  readonly correlationId?: string;
  constructor(response: ProblemDetails, status: number) { /* ... */ }
}
```

### 6.3 Single canonical normalization

`sendCommunication` is also where recipient normalization should live (replacing the 5 different implementations). The function accepts arrays — but for callers passing string-with-`;,`, `<EmailComposer />` does the normalization once via `<RecipientField />` and passes an array. No string-splitting in the BFF wrapper.

### 6.4 BFF-side change: rename the field

The wrapper rename above is only half the story. The BFF DTO should also rename:

```csharp
public sealed record SendCommunicationRequest
{
    // ... unchanged fields ...

    /// <summary>
    /// SPE driveItem IDs (NOT sprk_document GUIDs) to attach. Max 150
    /// attachments, 35 MB total. Despite the legacy name AttachmentDocumentIds,
    /// these are driveItem IDs — see ADR-XXX (Communication).
    /// </summary>
    public string[]? AttachmentDriveItemIds { get; init; }

    /// <summary>
    /// @deprecated Use AttachmentDriveItemIds. Kept for backward compatibility
    /// during R3 migration. Will be removed in R4.
    /// </summary>
    [Obsolete("Use AttachmentDriveItemIds")]
    public string[]? AttachmentDocumentIds { get; init; }
}
```

Server-side mapping: if `AttachmentDocumentIds` is set and `AttachmentDriveItemIds` is null, copy across. If both set, prefer canonical and log a warning. After all migrations land (Wave D), R4 removes the deprecated alias.

This is ONE of two server-side changes R3 makes. The other is the `Internet-Message-Id` post-send capture for reply-thread closure (§9.2). Both are surgical and non-breaking.

---

## 7. The Code Page: standalone email form

### 7.1 Purpose

Replace the auto-generated Dataverse standard form for `sprk_communication` with a Code Page that mounts `<EmailComposer />` in `mount='page'`. Users navigating to a `sprk_communication` record (from a view, a related-record subgrid, the new-record button) get the Spaarke composer.

### 7.2 Path & registration

- Path: `src/client/code-pages/EmailComposer/`
- Files (per `code-page-deploy` skill conventions):
  - `src/index.tsx` — React 18 `createRoot` entry
  - `index.html` — HTML shell
  - `build-webresource.ps1` — inline bundle into HTML
  - `webpack.config.js` — bundles everything
  - `package.json`, `tsconfig.json`
- Deployable artifact: `out/sprk_emailcomposer.html` (single self-contained file)
- Dataverse web resource name: `sprk_emailcomposer`

### 7.3 URL parameter contract

The Code Page reads `data=` query string per Code Page convention:

```
sprk_emailcomposer?data=mode=compose
sprk_emailcomposer?data=mode=view&id={commGuid}
sprk_emailcomposer?data=mode=reply&id={commGuid}
sprk_emailcomposer?data=mode=forward&id={commGuid}
sprk_emailcomposer?data=mode=compose&to=alice@example.com&subject=Hi
sprk_emailcomposer?data=mode=compose&associatedTo=sprk_matter:{matterGuid}
```

`data=` parameters supported:
- `mode` (required): `compose | view | reply | forward | draft`
- `id`: `sprk_communication` GUID (required for view/reply/forward/draft)
- `to`, `cc`, `subject`, `body`: pre-fill compose
- `associatedTo`: `<entityType>:<entityGuid>` — stamps an association
- `bffBaseUrl`, `tenantId`, `scope`, `clientId`: standard `@spaarke/auth` env vars (per Code Page convention)

### 7.4 Entry surfaces — multiple, all canonical

**Decision (design review 2026-06-05)**: the Code Page is launched from THREE distinct surfaces, all valid:

1. **Ribbon button "+ New Email"** on `sprk_communication` views (Active Communications, My Communications, related-record subgrids). Launches the Code Page with `mode=compose`. Pre-fills `associatedTo` when launched from a related-record subgrid (e.g., from a Matter's Communications subgrid → `associatedTo=sprk_matter:{matterGuid}`).
2. **Form Component Control replacing the default `sprk_communication` form**. Navigating to an existing `sprk_communication` record opens the Code Page (`mode=view&id={recordGuid}`), not the auto-generated Dataverse form. The standard form is retained as a hidden / admin-only form for data-sheet inspection.
3. **Embeddable launch from other Code Pages and components**. E.g., a Matter Code Page's "Email this Matter" action launches the EmailComposer Code Page with pre-filled associations. Any client surface that wants email-send opens the same Code Page.

All three entry surfaces use the same Code Page, the same URL parameter contract (§7.3), and the same underlying `<EmailComposer />` component. The only variation is what URL parameters are passed at launch.

**Surface 2 — Form Component Control** is the higher-risk entry point because it changes the default behavior of clicking on a `sprk_communication` record from a view. The standard form must remain available as a fallback for admin / debugging use. Surface 2 lands in Wave 2 alongside Surface 1; Surface 3 is opportunistic (any code page or component can adopt it as needed, no central work required).

### 7.5 Behavior

| Action | URL parameter | Composer mode | Code Page action |
|---|---|---|---|
| New record from main form / view | `mode=compose` | `compose` | Send → close (navigate to created record) |
| Open existing record | `mode=view&id={id}` | `view` | Edit (if draft), Reply, Forward, Close |
| Reply button | `mode=reply&id={id}` | `reply` | Send → close (navigate to created record) |
| Forward button | `mode=forward&id={id}` | `forward` | Send → close (navigate to created record) |
| Edit draft | `mode=draft&id={id}` | `draft` | Send → close, Save → close (in place) |

### 7.6 Authentication

Standard `@spaarke/auth` v2 bootstrap (per `ADR-028` and the Code Page deploy skill):

```tsx
import { initAuth, authenticatedFetch } from '@spaarke/auth';
import { resolveRuntimeConfig } from '@spaarke/auth';

const cfg = resolveRuntimeConfig();
await initAuth({
  clientId: cfg.clientId,
  tenantId: cfg.tenantId,
  bffBaseUrl: cfg.bffBaseUrl,  // host only, NO /api
  bffApiScope: cfg.scope,
});

ReactDOM.createRoot(rootEl).render(
  <SendEmailPage
    mode={mode}
    communicationId={id}
    authenticatedFetch={authenticatedFetch}
    bffBaseUrl={cfg.bffBaseUrl}
    associations={associationsFromUrl}
    initialTo={toFromUrl}
    initialSubject={subjectFromUrl}
    onSent={(id) => Xrm.Navigation.openForm({ entityName: 'sprk_communication', entityId: id })}
    onClose={() => window.close()}
  />
);
```

---

## 8. Migration plan — wave by wave

Each wave is one PR. Order matters: build the canonical first, then migrate callers.

### Wave 0 — Foundation (1 PR)
1. Add Communication ADR (`.claude/adr/ADR-XXX-communication-architecture.md`) — see §10 below.
2. Add the BFF rename for `AttachmentDriveItemIds` (non-breaking; alias kept).
3. Add the new `SendCommunicationError` typed error to `communicationApi.ts`.
4. Update `communicationApi.ts` docstrings to mark `attachmentDocumentIds` as deprecated and document the driveItem semantic.
5. Add backlog referrals (§11).
6. **Dataverse schema check + additions for reply-thread support** (per §5.11): verify `sprk_communication.sprk_inreplyto` lookup (self-referencing to parent `sprk_communication`) and `sprk_communication.sprk_internetmessageid` text column exist. Add either if missing. These are required by Waves 1 (composer reply/forward UI) and downstream of the server `Internet-Message-Id` capture per §9.2.
7. Add the server-side `Internet-Message-Id` post-send capture in `CommunicationService.SendAsync()` per §9.2.

**No caller changes in Wave 0**. This unblocks the canonical surfaces without breaking existing callers. Wave 0 expanded from the initial draft to include the reply-thread server change so subsequent waves can rely on the schema and the captured ID.

### Wave 1 — Build the canonical composer + three semantic wrappers (1 PR, est. 2½ days)
1. Create `src/client/shared/Spaarke.UI.Components/src/components/EmailComposer/` with all §5 sub-components.
2. Build the recipient field, body editor, attachment list, action bar.
3. Build the three wrappers in `EmailComposer/wrappers/`:
   - `SendEmailStep.tsx` (mount='inline')
   - `SendEmailDialog.tsx` (mount='dialog') — **rewritten** from current 1-caller component to canonical wrapper
   - `SendEmailPage.tsx` (mount='page')
4. Unit tests for engine validation, normalization, attachment cap enforcement, mode switching.
5. Unit tests for each wrapper confirming it sets correct `mount` and prop pass-through.
6. Export engine + all three wrappers from `@spaarke/ui-components` main barrel (and `pcf-safe` barrel if applicable).
7. Visual validation for the page mount (the most demanding visual variant per §5.10).

**No caller changes in Wave 1**. Engine + wrappers exist but are unused. Wave 1 expanded from the initial 2-day estimate to include the wrapper layer.

### Wave 2 — Build the Code Page (1 PR, est. 1 day)
1. Create `src/client/code-pages/EmailComposer/`.
2. Wire URL parameter parsing, `Xrm.WebApi` read for view/reply/forward/draft modes, `@spaarke/auth` bootstrap.
3. Deploy to dev (`sprk_emailcomposer` web resource).
4. Add ribbon button on `sprk_communication` form/view that launches the Code Page (compose-new + open-existing-as-page).

After Wave 2, users can use the new composer for any `sprk_communication` interaction via the ribbon. Old form still exists; no migration of existing records.

### Wave 3 — Migrate SummarizeFilesDialog (1 PR, est. ½ day)
1. Replace inline `fetch` in `Spaarke.UI.Components/.../SummarizeFilesWizard/SummarizeFilesDialog.tsx` with `<SendEmailStep />` (the canonical wrapper for wizard step usage).
2. Wire `wizardContext.uploadedFiles`, `associations`, `initialSubject`, `initialBody` (from summary).
3. Use `composerRef.current.send()` from the wizard's Finish action.
4. Remove the `LegalWorkspace` duplicate copy of `SummarizeFilesDialog.tsx`; LegalWorkspace re-exports from `@spaarke/ui-components`.
5. Rebuild + redeploy SummarizeFilesWizard solution AND LegalWorkspace solution.

After Wave 3, today's `'Text'` vs `'PlainText'` bug class is gone — there's no string for callers to hardcode.

### Wave 4 — Migrate FilePreviewDialog onto canonical `<SendEmailDialog />` (1 PR, est. ½ day)
**Decision (design review 2026-06-05, revised)**: keep `SendEmailDialog` as a canonical wrapper around `<EmailComposer mount='dialog' />`. Update `FilePreviewDialog.tsx` to use the new wrapper's props. The wrapper is rewritten in Wave 1; Wave 4 only swaps the inline `fetch` for the wrapper.

1. In `LegalWorkspace/.../FilePreview/FilePreviewDialog.tsx`:
   - Remove the inline `handleSendEmail` function.
   - Replace the `<SendEmailDialog open onSend={handleSendEmail}>` invocation with `<SendEmailDialog open={emailDialogOpen} onClose={...} initialTo={[recipient]} associations={[{entityType:'sprk_document', entityId: file.id}]} authenticatedFetch={...} bffBaseUrl={...} onSent={...} onError={...} />`.
2. Update `ISendEmailDialogProps` to remove the legacy `onSend` callback (replaced by `onSent` + `onClose`).
3. Audit other shared lib / PCFs / solutions for any other `SendEmailDialog` consumers; migrate them with this PR.
4. Rebuild + redeploy LegalWorkspace solution; redeploy PCFs that bundle the shared lib.

Wave 4 no longer deletes `SendEmailDialog` — instead it becomes one of the canonical wrappers. Future popup callers import this wrapper.

### Wave 5 — Migrate the 5 create-record wizards (1 PR for all five, est. 1 day)
**Decision (design review 2026-06-05)**: ship all 5 wizards in a single PR to minimize CI/CD overhead.

1. Refactor `EntityCreationService.sendEmail()` to be a thin adapter over `sendCommunication()`:
   ```ts
   async sendEmail(input: ISendEmailInput): Promise<ISendEmailResult> {
     try {
       const result = await sendCommunication(
         { /* map input to SendCommunicationOptions */ },
         { authenticatedFetch: this._authenticatedFetch, bffBaseUrl: this._bffBaseUrl }
       );
       return { success: true };
     } catch (err) {
       const warning = err instanceof SendCommunicationError
         ? `Could not send email (${err.code}). ${err.detail}`
         : `Could not send email. Please send manually.`;
       return { success: false, warning };
     }
   }
   ```
2. CreateMatter, CreateProject, CreateEvent, CreateTodo, CreateWorkAssignment wizards all use `EntityCreationService.sendEmail()` — no change to their call sites.
3. Replace their bespoke `SendEmailStep` UIs (today's 3 different variants) with the canonical `<SendEmailStep />` wrapper (this is a UX change — surface for review).
4. Delete the LegalWorkspace duplicate copies of these 5 wizards' email steps and services; LegalWorkspace re-exports from `@spaarke/ui-components`.
5. Rebuild + redeploy each wizard solution + LegalWorkspace.

### Wave 6 — Retire `sprk_communication_send.js` webresource (1 PR, est. ½ day)
**Decision (design review 2026-06-05)**: option 6a — retire the webresource.

Steps:
1. Audit Dataverse for any ribbon button, command bar, or workflow that references `sprk_communication_send.js`. List the entry points.
2. Replace each entry point with a ribbon button that launches the EmailComposer Code Page (Wave 2 provides the Code Page; this wave swaps the handlers).
3. Delete `src/client/webresources/js/sprk_communication_send.js` (~600 LOC) and the duplicate copy at `infrastructure/dataverse/ribbon/EmailRibbons/WebResources/sprk_communication_send.js`.
4. Delete the web resource record in Dataverse.

This removes the only client surface that self-bootstraps MSAL (drift risk if `@spaarke/auth` evolves), and the only client implementation that fully supports `sendMode` / `fromMailbox` / multi-association payloads — those features now live in `<EmailComposer />`.

### Wave 7 — LegalWorkspace fork cleanup tail (1 PR, est. ½ day)
After Waves 3–6, audit `src/solutions/LegalWorkspace/src/components/` for any remaining email-touching duplicates. Retire them; ensure LegalWorkspace re-exports from `@spaarke/ui-components`. Resolve the cross-package source-path import flagged in the assessment (`WorkAssignmentWizardDialog.tsx:31`).

### Wave 8 — Documentation (1 PR, est. 1 day)
See §10 for the detailed list. Includes:
- Communication ADR (file written in Wave 0; this wave links + cross-references it).
- Pattern doc updates (`.claude/patterns/api/send-email-integration.md`).
- Component guide (`docs/guides/SHARED-UI-COMPONENTS-GUIDE.md`) — add EmailComposer section.
- Mark legacy email-to-document R1 docs as RETIRED.
- Add attachment policy doc analog to `CHAT-ATTACHMENT-POLICY.md`.
- Update `bff-extensions.md` to mention Communication as a sensitive surface.

### Wave-by-wave summary table

| Wave | Output | Risk | Reversible? |
|---|---|---|---|
| 0 | ADR + non-breaking BFF rename + reply-thread schema + Internet-Message-Id capture | Low–Medium (server change) | Yes |
| 1 | EmailComposer built (no callers yet) | Low | Yes — pure addition |
| 2 | Code Page deployed (3 entry surfaces incl. Form Component Control) | Medium (Form Component Control changes default record-open behavior) | Yes — revert to standard form |
| 3 | SummarizeFilesDialog migrated + LW fork deleted | Medium | Yes — small diff |
| 4 | FilePreviewDialog migrated + SendEmailDialog retired | Medium | Yes |
| 5 | 5 create-record wizards migrated + LW forks deleted | High (UX touch across 5 wizards in 1 PR) | Yes (PR revert) |
| 6 | Webresource retired | Medium | Yes |
| 7 | LegalWorkspace cleanup tail | Low | Yes |
| 8 | Documentation | Low | N/A |

Each wave can ship independently. Waves 0, 1, 8 are pure additions. Waves 2–7 are caller migrations with regression risk; each should ship with manual smoke tests in dev before merging.

---

## 9. Server-side scope (minimal)

R3 deliberately makes only ONE server-side code change:

### 9.1 BFF DTO field rename (Wave 0)
- Add `AttachmentDriveItemIds` as the canonical field name on `SendCommunicationRequest`.
- Mark `AttachmentDocumentIds` `[Obsolete]` with mapping logic (Wave 0 server code).
- Both fields point to the same backing store; mapping happens in the constructor / property accessor.
- The R4 follow-on removes the deprecated alias.

### 9.2 Reply-thread closure (IN SCOPE per design review)

In `CommunicationService.SendAsync()`, after Graph `SendMail` succeeds, fetch the just-sent message via `graphClient.Users[sender].Messages` (filtered by the deterministic `correlationId` or by `receivedDateTime` window + subject match) to retrieve the real `Internet-Message-Id`, then stamp it onto the `sprk_communication` record's `sprk_internetmessageid`.

Implementation notes:
- Cost: one extra Graph round-trip per send. Acceptable.
- Failure mode: if the post-send lookup fails (Graph latency, message not yet indexed), retry with backoff once, then proceed without the real ID and log a warning. The send itself remains the critical path; this enrichment is best-effort.
- Existing `IncomingAssociationResolver.cs:171` already matches inbound replies by `sprk_internetmessageid`. Once the outbound side stamps real IDs, the reply chain closes automatically.

This is in addition to §9.1 (the `AttachmentDriveItemIds` rename). R3 makes two server changes total, both surgical.

### 9.3 What R3 does NOT change server-side

- `CommunicationService.SendAsync()` overall flow remains as-is (only adds the post-send `Internet-Message-Id` capture per §9.2).
- All 13 services in `Services/Communication/` remain as-is.
- `CommunicationModule` DI remains as-is.
- All endpoints remain as-is.
- `/api/v1/emails/*` remains as-is (out of scope).
- Configuration model remains as-is.

---

## 10. Communication ADR (draft outline)

A new ADR `.claude/adr/ADR-XXX-communication-architecture.md` will codify the canonical pattern. Draft outline:

```markdown
# ADR-XXX: Communication Architecture (Client + Server Boundaries)

## Status
Accepted

## Context
The Spaarke platform has experienced repeated production bugs from
fragmented client-side implementations of email send. R2 unified the
server side. R3 unifies the client side. This ADR codifies the boundaries
so future development does not re-fragment.

## Decision

### Client surfaces
- MUST use `<EmailComposer />` from `@spaarke/ui-components` for any
  email-send UX. No bespoke email composers.
- MUST use `sendCommunication()` from `@spaarke/ui-components/services`
  for any programmatic email send. No inline fetch to
  `/api/communications/send`.
- MUST inject `authenticatedFetch` from `@spaarke/auth`; shared library
  components MUST NOT import `@spaarke/auth` directly.

### Server pipeline
- All email send/receive flows through `Services/Communication/`.
- New services in this domain MUST be registered via `CommunicationModule`.
- Outbound creates `sprk_communication` records; this is the canonical
  email record.
- The `.eml` archive + attachment `sprk_document` records are best-effort;
  Graph SendMail is the only critical path.

### Solution boundaries
- `@spaarke/ui-components` is the single source of email-touching
  components.
- `src/solutions/LegalWorkspace/` MUST NOT fork email components. It
  re-exports from the shared library.

### Out of scope (for this ADR)
- Outlook add-in flow.
- Power Apps Email-activity path (`/api/v1/emails/*`).
- Non-email `CommunicationType` values.

## Consequences
- Adding a new email-send feature anywhere in the platform means
  consuming `<EmailComposer />` or `sendCommunication()`. No new ad-hoc
  implementations.
- Code review for any PR touching email send MUST verify no new
  inline-fetch calls to `/api/communications/send`.
- Future Teams/SMS/Notification composers will be sibling components in
  `@spaarke/ui-components` following the same shape.

## Related
- ADR-028 (Spaarke Auth v2)
- ADR-024 (Polymorphic resolver)
- ADR-007 (SpeFileStore facade)
- communication-service-architecture.md (R2 architecture)
- This project's design.md
```

The final ADR is written in Wave 8.

---

## 11. Documentation updates

Wave 8 lands the following docs work:

1. **New**: `.claude/adr/ADR-XXX-communication-architecture.md` (draft above).
2. **New**: `docs/guides/EMAIL-COMPOSER-COMPONENT-GUIDE.md` — for developers consuming `<EmailComposer />`. Props, modes, mount shapes, attachment sources, worked examples.
3. **New**: `docs/standards/COMMUNICATION-ATTACHMENT-POLICY.md` — analog to `CHAT-ATTACHMENT-POLICY.md`. Server caps (150 / 35 MB), client warnings (25 MB), blocked MIME types.
4. **New**: `docs/data-model/sprk_communication-form.md` — documents that the canonical form is the Code Page, not the auto-generated Dataverse form.
5. **Update**: `.claude/patterns/api/send-email-integration.md` — point at `<EmailComposer />` as the canonical UI primitive in addition to `sendCommunication()`.
6. **Update**: `.claude/FAILURE-MODES.md` AP-4 — note that the `attachmentDocumentIds` → `attachmentDriveItemIds` rename closes the latent bug class.
7. **Update**: `.claude/constraints/bff-extensions.md` — add Communication as a sensitive surface; cite the new ADR.
8. **Update**: `docs/architecture/communication-service-architecture.md` — add the client-side section pointing at `<EmailComposer />` + `sendCommunication()`.
9. **Update**: `docs/guides/SHARED-UI-COMPONENTS-GUIDE.md` — add EmailComposer.
10. **Mark RETIRED**: `docs/architecture/email-to-document-architecture.md` and `docs/architecture/email-to-document-automation.md`. Add a clear "RETIRED — see [communication-service-architecture.md](communication-service-architecture.md)" banner at the top.
11. **Mark RETIRED or OUT-OF-SCOPE**: any doc explicitly about the Power Apps Email-activity automation, since that path is dead in production usage.
12. **Update**: `docs/architecture/sdap-overview.md` — fix the `sprk_email*` field references on `sprk_document` (these are leftover from before `sprk_communication` was the canonical record).
13. **Update**: `MEMORY.md` — add a feedback memory: "When fixing email-related code, default to `<EmailComposer />` / `sendCommunication()`; reject ad-hoc implementations in review."

---

## 12. Backlog referrals (file as part of Wave 0)

Add to `projects/_backlog/needs-a-project.md`:

- **#12 Investigate retirement of `/api/v1/emails/*` (Email-to-Document Automation subsystem)** — Built Jan 2026, operates on Power Apps Email activities, which Spaarke does not use. Confirmed dead in production usage. Either retire the BFF endpoints + Dataverse webhook + ribbon webresource, OR document the intent to keep for future activation.
- **#13 Build `sprk_communication` browse / thread / inbox UI** — R3 builds the form (single record). It does NOT build a list view or thread browser. Server already creates the records.
- **#14 Outlook add-in alignment with canonical composer** — When the add-in is next touched, evaluate whether it can mount `<EmailComposer />` (it currently has its own UI).
- **#15 Future `TeamsMessage` / `SMS` / `Notification` composers** — `sprk_communication` supports these types; today only Email is implemented. Build sibling composers in `@spaarke/ui-components` when the use case lands.

---

## 13. Risks

| Risk | Probability | Impact | Mitigation |
|---|---|---|---|
| `<EmailComposer />` UX regressions in wizards | Medium | High (user-facing) | Wave 5 ships with manual smoke tests on each wizard's send flow; LegalWorkspace + shared lib copies migrate in the same PR. |
| Form Component Control change confuses users opening existing records | Medium | Medium | Keep the standard Dataverse form available as a hidden / admin form for data-sheet inspection (§7.4). If user feedback is negative, the Form Component Control swap is reversible — fall back to ribbon-only entry. |
| `Internet-Message-Id` post-send capture adds latency | Low | Low | One extra Graph round-trip per send is acceptable (§9.2). On failure, retry once then proceed without enrichment; send remains critical path. |
| Outlook add-in or other unknown surface depends on `sprk_communication_send.js` | Low | Medium | Wave 6 audits ribbon button consumers; if multiple ribbons reference the webresource, decide retire vs. redirect per-ribbon. |
| `attachmentDocumentIds` → `attachmentDriveItemIds` server rename breaks an unknown caller | Low | Low | Non-breaking alias (Wave 0). Old field continues working; new code uses canonical. |
| Wave 5 wizard PR is too large to review | Medium | Medium | User accepted single-PR-for-all-5 trade-off for CI/CD overhead reduction. Can split per-wizard if review feedback demands. Each wizard is independent. |
| `sprk_inreplyto` / `sprk_internetmessageid` columns missing from Dataverse | Low | Medium | Wave 0 task verifies schema; if missing, the Dataverse customization is a small addition that ships in the same wave. |
| `<EmailComposer mount='page' />` styling does not feel like a primary entity form | Medium | Medium | Wave 1 prioritizes the page mount visual design (§5.10); validate in Wave 2 Code Page deploy to dev before any caller migrates. |

---

## 14. Decisions resolved during design review (2026-06-05)

The original 8 open questions were resolved before this draft moved to `/design-to-spec`:

1. ✅ **Reply-thread closure (§5.11)**: IN SCOPE. Server captures real `Internet-Message-Id` post-send (§9.2). Client implements `reply` and `forward` modes with `sprk_inreplyto` linkage.
2. ✅ **Code Page entry surfaces (§7.4)**: ALL THREE entry surfaces — ribbon button "+ New Email," Form Component Control replacing the default `sprk_communication` form, AND embeddable launch from other Code Pages / components.
3. ✅ **React version target (§5.7)**: React 18 only. No PCF hosts `<EmailComposer />` directly. ADR-022 does not apply.
4. ✅ **Wave 5 PR shape (Wave 5)**: single PR for all 5 wizards. Minimizes CI/CD overhead.
5. ✅ **Webresource retirement (Wave 6)**: option 6a — retire `sprk_communication_send.js`. Audit ribbon button references first.
6. ✅ **Semantic wrapper layer**: ADD THREE WRAPPERS (revised from initial "drop SendEmailDialog" recommendation). `<SendEmailStep />` / `<SendEmailDialog />` / `<SendEmailPage />` are thin (~30–60 LOC each) wrappers around `<EmailComposer />`, each locking in one `mount` value with a semantic caller API. Callers import wrappers; direct `<EmailComposer />` use is reserved for non-standard mount contexts.
7. ✅ **Component dev rig**: NOT required for this project. If anything, lightweight only (not a primary deliverable). Component validation happens via Wave 1 unit tests + Wave 2 Code Page deploy to dev.
8. ✅ **EmailComposer styling**: restyle for form-replacement use case (§5.10). Explicit visual variants per mount (`page` = primary entity form look; `dialog` = compact; `inline` = wizard step). Shared Fluent v9 tokens.

Any new questions surfaced during `/design-to-spec` should be added to this section with timestamps.

---

## 15. Effort estimate (rough)

| Wave | Estimate |
|---|---|
| 0 — Foundation (ADR + BFF rename + schema check + Internet-Message-Id capture) | 1½ days |
| 1 — Composer build (incl. page mount visual design) | 2½ days |
| 2 — Code Page (3 entry surfaces) | 1½ days |
| 3 — SummarizeFilesDialog + LW fork | ½ day |
| 4 — FilePreviewDialog + SendEmailDialog retirement | ½ day |
| 5 — 5 wizards + LW forks (single PR) | 1½ days |
| 6 — Webresource retirement (audit + replace) | ½ day |
| 7 — LW cleanup tail | ½ day |
| 8 — Documentation | 1 day |
| **Total** | **~9–11 days** of focused work |

This is the consolidation. The cost is bounded; the savings (no more "5 places to fix the same bug" / no more LegalWorkspace fork drift / no more `'Text'` vs `'PlainText'` class of regression) recur on every email-related change going forward.

---

## 16. Out-of-scope reminders (final restatement)

The following are deliberately NOT addressed by R3 and will remain whatever state they're in today:

- Outlook add-in (its own UI, its own send path, its own document-archive flow).
- `/api/v1/emails/*` (Power Apps Email-activity subsystem — dead in production usage).
- Teams / SMS / Notification composers (Email only).
- `sprk_communication` list / inbox / thread UI (server creates records; client doesn't read them back at scale).
- Server-side Communication Service refactor (R2 already did this).
- Configuration model changes.

---

## Appendix A — Canonical pattern, one-paragraph summary

> Every Spaarke surface that sends email mounts one of three semantic wrappers from `@spaarke/ui-components`: `<SendEmailStep />` (wizard step), `<SendEmailDialog />` (popup dialog), or `<SendEmailPage />` (full Code Page). All three delegate to the canonical `<EmailComposer />` engine, which calls `sendCommunication()` to POST to `/api/communications/send`. The server (`CommunicationService.SendAsync()` from R2) sends via Graph, creates a `sprk_communication` record, archives `.eml` to SPE, attaches associations. The standalone form (Dataverse `sprk_communication`) is a Code Page that mounts `<SendEmailPage />`. No new ad-hoc send paths are accepted in code review; new mount contexts get their own wrapper added next to the existing three.

---

*End of design.md. Next step: review, then `/design-to-spec`.*
