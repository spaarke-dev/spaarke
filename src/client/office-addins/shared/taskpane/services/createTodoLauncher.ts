/**
 * createTodoLauncher.ts
 *
 * Builds a navigation URL that opens the CreateTodo wizard from the SmartTodo
 * Code Page with a pre-filled `initialRegarding`, per the FR-16 / FR-27
 * launch-context contract.
 *
 * Per smart-todo-decoupling-r3 spec FR-27 + the launch contract in
 * `projects/smart-todo-decoupling-r3/notes/createtodo-launch-contract.md`:
 *
 *   When the Outlook ribbon "Create To Do" action fires, the click handler:
 *     1. Ensures the email is saved as `sprk_communication` (uses the existing
 *        save flow if not already saved).
 *     2. Builds the launch URL via this service, with `initialRegarding`
 *        carrying the `sprk_communication` triple.
 *     3. Opens the URL in a new browser window. The SmartTodo Code Page (which
 *        hosts `CreateTodoWizard`) reads the URL params on init and opens the
 *        wizard with the matching `initialRegarding`. The wizard creates a
 *        `sprk_todo` via `applyResolverFields` (ADR-024) with
 *        `sprk_regardingcommunication` populated atomically.
 *
 * Why a separate window instead of mounting the wizard inside the Outlook
 * taskpane?
 *   - The Outlook taskpane is 250-450px wide; the CreateTodo wizard requires a
 *     dialog-sized surface (per its `CreateRecordWizard` shell).
 *   - `CreateTodoWizard` requires `IDataService` + `INavigationService` (Xrm
 *     Web API + lookup dialogs). These are only available inside the Power
 *     Apps / Dataverse host that runs the SmartTodo Code Page.
 *   - The existing Outlook add-in pattern for "open a Dataverse form" is
 *     `window.open(url, ...)` (see `SaveView.tsx` Quick Create handler).
 *
 * Product-portability (CLAUDE.md §16): The SmartTodo Code Page base URL is
 * supplied via the `SMARTTODO_CODEPAGE_URL` env var at build time. No hardcoded
 * org URLs / tenant ids in source.
 *
 * @see projects/smart-todo-decoupling-r3/spec.md FR-27
 * @see projects/smart-todo-decoupling-r3/notes/createtodo-launch-contract.md
 * @see projects/smart-todo-decoupling-r3/notes/outlook-ribbon-create-todo.md
 */

/**
 * URL query-parameter keys used by the SmartTodo Code Page to read the
 * Outlook launch context. Stable contract — the Code Page reads these names
 * verbatim, so changing them is a breaking change.
 *
 * Exported for use by the SmartTodo Code Page's URL-param parser (when it's
 * built in a follow-up task) and by tests in this workspace.
 */
export const CREATE_TODO_LAUNCH_PARAMS = {
  /** Action discriminator — distinguishes from kanban or other deep-links. */
  ACTION: 'action',
  /** `sprk_communicationid` of the saved email. */
  REGARDING_ID: 'regardingId',
  /** Logical name of the regarding entity (always `sprk_communication` from Outlook). */
  REGARDING_TYPE: 'regardingType',
  /** Display name (email subject) — drives the AssociateToStep selected-record card. */
  REGARDING_NAME: 'regardingName',
} as const;

/**
 * Action value indicating the SmartTodo Code Page should open the CreateTodo
 * wizard immediately on load with the supplied launch context.
 */
export const CREATE_TODO_ACTION = 'createTodo';

/**
 * Inputs for building the launch URL.
 */
export interface BuildCreateTodoLaunchUrlInput {
  /** SmartTodo Code Page base URL — typically derived from `SMARTTODO_CODEPAGE_URL` env. */
  codePageBaseUrl: string;
  /** sprk_communicationid (GUID — server-format is fine; this service lowercases). */
  communicationId: string;
  /** Display name (typically email subject). */
  recordName: string;
  /**
   * Entity logical name. Defaults to `sprk_communication` per FR-27. Exposed
   * as a param so the same builder can power task 040 (parent-form ribbon)
   * with a different `entityType` if reused.
   */
  entityType?: string;
}

/**
 * Normalise a GUID to the lowercased, no-braces form used by the launch
 * contract (matches `IInitialRegarding.recordId`).
 */
function normaliseGuid(id: string): string {
  return id.replace(/[{}]/g, '').toLowerCase();
}

/**
 * Build the launch URL that opens the CreateTodo wizard pre-filled with the
 * given regarding record.
 *
 * The returned URL is safe to pass to `window.open()`. Throws when required
 * inputs are missing — callers are expected to validate upstream.
 *
 * @throws {Error} when `codePageBaseUrl` or `communicationId` is missing.
 */
export function buildCreateTodoLaunchUrl(input: BuildCreateTodoLaunchUrlInput): string {
  if (!input.codePageBaseUrl || input.codePageBaseUrl.trim().length === 0) {
    throw new Error('buildCreateTodoLaunchUrl: codePageBaseUrl is required (set SMARTTODO_CODEPAGE_URL env var)');
  }
  if (!input.communicationId || input.communicationId.trim().length === 0) {
    throw new Error('buildCreateTodoLaunchUrl: communicationId is required');
  }

  const entityType = input.entityType ?? 'sprk_communication';
  const recordName = input.recordName ?? '';

  // Use URL + URLSearchParams so any existing query string in the base URL is
  // preserved (and so the param values are encoded once, correctly).
  const url = new URL(input.codePageBaseUrl);
  url.searchParams.set(CREATE_TODO_LAUNCH_PARAMS.ACTION, CREATE_TODO_ACTION);
  url.searchParams.set(CREATE_TODO_LAUNCH_PARAMS.REGARDING_TYPE, entityType);
  url.searchParams.set(CREATE_TODO_LAUNCH_PARAMS.REGARDING_ID, normaliseGuid(input.communicationId));
  url.searchParams.set(CREATE_TODO_LAUNCH_PARAMS.REGARDING_NAME, recordName);
  return url.toString();
}

/**
 * Window-open features for the CreateTodo launch popup. Sized for the
 * `CreateRecordWizard` dialog (matches the Quick Create popup in `SaveView`).
 * Exported so callers can override and tests can assert against the default.
 */
export const CREATE_TODO_WINDOW_FEATURES = 'width=720,height=820,resizable=yes,scrollbars=yes';

/**
 * Open the CreateTodo wizard in a new browser window with the supplied launch
 * context. Convenience wrapper around `buildCreateTodoLaunchUrl` + `window.open`.
 *
 * Returns the opened `Window` reference (or `null` if the browser blocked the
 * popup — caller should surface a "popup blocked" message).
 */
export function openCreateTodoWizard(
  input: BuildCreateTodoLaunchUrlInput,
  windowOpen: (url: string, target: string, features: string) => Window | null = (u, t, f) => window.open(u, t, f)
): Window | null {
  const url = buildCreateTodoLaunchUrl(input);
  return windowOpen(url, '_blank', CREATE_TODO_WINDOW_FEATURES);
}
