/**
 * Props for the RelatedDocumentCount React component.
 *
 * These props are derived from the PCF manifest inputs
 * and passed from the ReactControl entry point.
 *
 * Note: tenantId and apiBaseUrl are no longer passed as props — they are
 * resolved at runtime from Dataverse environment variables inside the component.
 */

import { IInputs } from '../generated/ManifestTypes';

export interface IRelatedDocumentCountProps {
  /** PCF context for accessing framework APIs (including webAPI for env var resolution). */
  context: ComponentFramework.Context<IInputs>;
  /** Document ID to look up related document count. */
  documentId: string;
  /** Title displayed on the count card. Defaults to "RELATED DOCUMENTS". */
  cardTitle?: string;
  /** Whether the Fluent theme is dark mode. */
  isDarkMode: boolean;
}
