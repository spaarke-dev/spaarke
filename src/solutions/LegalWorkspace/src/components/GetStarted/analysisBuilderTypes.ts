/**
 * analysisBuilderTypes.ts
 *
 * Type definitions for launching the AI Playbook Analysis Builder (AiToolAgent PCF)
 * from the Legal Operations Workspace action cards.
 *
 * The Analysis Builder is the existing AiToolAgent PCF control embedded in the MDA
 * sidebar. The LegalWorkspace Custom Page communicates with it by posting a structured
 * message to the parent MDA frame, which routes the message to the embedded control.
 *
 * Integration pattern:
 *   Custom Page (iframe)
 *     → window.parent.postMessage(IAnalysisBuilderLaunchMessage, "*")
 *       → MDA host shell
 *         → AiToolAgent PCF (embedded control)
 *           → opens with pre-configured context/intent
 */

// ---------------------------------------------------------------------------
// Core context/intent interface
// ---------------------------------------------------------------------------

/**
 * Context payload sent to the Analysis Builder when launching from an action card.
 *
 * The Analysis Builder reads this payload to pre-configure the initial workflow:
 * - `intent` identifies which AI workflow to activate
 * - `displayName` is shown in the Analysis Builder header/title
 * - `initialPrompt` is pre-filled as the opening user message
 * - `entityContext` optionally scopes the analysis to a specific Dataverse record
 * - `metadata` carries arbitrary key-value pairs for intent-specific parameters
 */
export interface IAnalysisBuilderContext {
  /**
   * Stable string identifier for the intent — maps to an AI workflow in the
   * AiToolAgent / AI Playbook platform.
   *
   * Supported values (defined in AI Playbook intent registry):
   *   "new-project"        — Create a new project
   *   "assign-counsel"     — Assign counsel to a matter
   *   "document-analysis"  — Analyze a document
   *   "document-search"    — Search for documents
   *   "email-compose"      — Compose an email
   *   "meeting-schedule"   — Schedule a meeting
   */
  intent: string;

  /**
   * Human-readable label shown in the Analysis Builder header.
   * Matches the action card label for UX consistency.
   */
  displayName: string;

  /**
   * Optional pre-configured prompt injected as the opening user message.
   * The Analysis Builder displays this as if the user had typed it, giving
   * the AI immediate context for the workflow.
   */
  initialPrompt?: string;

  /**
   * Optional reference to a specific Dataverse record that the workflow
   * should operate on (e.g. a matter, project, or document).
   */
  entityContext?: IEntityContext;

  /**
   * Arbitrary key-value pairs for intent-specific parameters not covered
   * by the structured fields above.
   *
   * Example: { "documentType": "contract", "language": "en-US" }
   */
  metadata?: Record<string, string>;
}

/**
 * A reference to a specific Dataverse record, used to scope an AI workflow
 * to a particular entity instance.
 */
export interface IEntityContext {
  /** Logical name of the Dataverse entity (e.g. "sprk_matter", "sprk_project"). */
  entityType: string;
  /** GUID of the Dataverse record. */
  entityId: string;
}

// ---------------------------------------------------------------------------
// postMessage contract
// ---------------------------------------------------------------------------

/**
 * The message posted to `window.parent` when launching the Analysis Builder.
 *
 * The MDA host shell listens for messages with `action: "openAnalysisBuilder"`
 * and routes them to the embedded AiToolAgent PCF control.
 */
export interface IAnalysisBuilderLaunchMessage {
  /** Discriminator used by the MDA host to identify this message type. */
  action: "openAnalysisBuilder";
  /** The pre-configured context/intent payload for the Analysis Builder. */
  context: IAnalysisBuilderContext;
}

// ---------------------------------------------------------------------------
// Pre-defined context payloads for the 6 action cards
// ---------------------------------------------------------------------------

/**
 * Pre-defined Analysis Builder context payloads indexed by action card ID.
 *
 * These are the canonical intent definitions for the 6 non-Create-Matter cards.
 * Each payload is immutable — handlers create shallow copies when adding
 * runtime entity context (e.g. matter ID from current record).
 */
export const ANALYSIS_BUILDER_CONTEXTS: Readonly<
  Record<string, IAnalysisBuilderContext>
> = {
  "create-new-project": {
    intent: "new-project",
    displayName: "Create New Project",
    initialPrompt: "Create a new project",
  },
  "assign-to-counsel": {
    intent: "assign-counsel",
    displayName: "Assign to Counsel",
    initialPrompt: "Assign counsel to a matter",
  },
  "analyze-new-document": {
    intent: "document-analysis",
    displayName: "Analyze New Document",
    initialPrompt: "Analyze a document",
  },
  "search-document-files": {
    intent: "document-search",
    displayName: "Search Document Files",
    initialPrompt: "Search for documents",
  },
  "send-email-message": {
    intent: "email-compose",
    displayName: "Send Email Message",
    initialPrompt: "Compose an email",
  },
  "schedule-new-meeting": {
    intent: "meeting-schedule",
    displayName: "Schedule New Meeting",
    initialPrompt: "Schedule a meeting",
  },
} as const;
