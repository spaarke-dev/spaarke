import {
  AddSquareRegular,
  FolderAddRegular,
  PersonAddRegular,
  DocumentSearchRegular,
  SearchRegular,
  MailRegular,
  CalendarAddRegular,
} from "@fluentui/react-icons";
import type { FluentIcon } from "@fluentui/react-icons";

/** Configuration for a single Get Started action card. */
export interface IActionCardConfig {
  /** Stable identifier used as React key and for wiring onClick in tasks 024/025. */
  id: string;
  /** Display label shown below the icon. */
  label: string;
  /** Fluent v9 icon component (FluentIcon). */
  icon: FluentIcon;
  /** Aria-label for the card button — more descriptive than the visible label. */
  ariaLabel: string;
}

/**
 * Ordered list of 7 action cards for the Get Started row.
 *
 * Click handlers are wired in WorkspaceGrid.tsx via the `onCardClick` prop:
 *   - "create-new-matter"         → opens WizardDialog (Create Matter, tasks 022-024)
 *   - "create-new-project"        → Analysis Builder, intent "new-project" (task 025)
 *   - "assign-to-counsel"         → Analysis Builder, intent "assign-counsel" (task 025)
 *   - "analyze-new-document"      → Analysis Builder, intent "document-analysis" (task 025)
 *   - "search-document-files"     → Analysis Builder, intent "document-search" (task 025)
 *   - "send-email-message"        → Analysis Builder, intent "email-compose" (task 025)
 *   - "schedule-new-meeting"      → Analysis Builder, intent "meeting-schedule" (task 025)
 *
 * See ActionCardHandlers.ts and analysisBuilderTypes.ts for the Analysis Builder
 * integration implementation.
 */
export const ACTION_CARD_CONFIGS: IActionCardConfig[] = [
  {
    id: "create-new-matter",
    label: "Create New Matter",
    icon: AddSquareRegular,
    ariaLabel: "Create a new legal matter",
  },
  {
    id: "create-new-project",
    label: "Create New Project",
    icon: FolderAddRegular,
    ariaLabel: "Create a new project",
  },
  {
    id: "assign-to-counsel",
    label: "Assign to Counsel",
    icon: PersonAddRegular,
    ariaLabel: "Assign a matter or task to counsel",
  },
  {
    id: "analyze-new-document",
    label: "Analyze New Document",
    icon: DocumentSearchRegular,
    ariaLabel: "Analyze a new document using AI",
  },
  {
    id: "search-document-files",
    label: "Search Document Files",
    icon: SearchRegular,
    ariaLabel: "Search across all document files",
  },
  {
    id: "send-email-message",
    label: "Send Email Message",
    icon: MailRegular,
    ariaLabel: "Compose and send an email message",
  },
  {
    id: "schedule-new-meeting",
    label: "Schedule New Meeting",
    icon: CalendarAddRegular,
    ariaLabel: "Schedule a new meeting",
  },
];
