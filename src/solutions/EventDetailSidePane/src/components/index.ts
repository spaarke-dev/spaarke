/**
 * EventDetailSidePane Components
 *
 * Exports all components for the Event Detail Side Pane.
 */

export {
  HeaderSection,
  type IHeaderSectionProps,
} from "./HeaderSection";

export {
  StatusSection,
  STATUS_REASON_OPTIONS,
  getStatusReasonLabel,
  isValidStatusReason,
  type StatusSectionProps,
  type StatusReasonValue,
  type StatusReasonOption,
} from "./StatusSection";

export {
  KeyFieldsSection,
  PRIORITY_OPTIONS,
  DEFAULT_PRIORITY,
  getPriorityOption,
  getPriorityLabel,
  isValidPriority,
  type KeyFieldsSectionProps,
  type PriorityValue,
  type PriorityOption,
  type OwnerInfo,
} from "./KeyFieldsSection";

export {
  CollapsibleSection,
  type CollapsibleSectionProps,
} from "./CollapsibleSection";

export {
  DatesSection,
  type DatesSectionProps,
  type DateFieldValue,
} from "./DatesSection";

export {
  DescriptionSection,
  type DescriptionSectionProps,
} from "./DescriptionSection";

export {
  Footer,
  createSuccessMessage,
  createErrorMessage,
  createErrorMessageWithRollback,
  type FooterProps,
  type FooterWithMessageProps,
  type FooterMessage,
  type FooterMessageWithActions,
} from "./Footer";

export {
  HistorySection,
  type HistorySectionProps,
  type HistoryData,
  type UserInfo,
  type StatusChangeEntry,
} from "./HistorySection";

export {
  UnsavedChangesDialog,
  type UnsavedChangesDialogProps,
  type UnsavedChangesAction,
} from "./UnsavedChangesDialog";

export {
  RelatedEventSection,
  extractRelatedEventInfo,
  type IRelatedEventSectionProps,
  type IRelatedEventInfo,
} from "./RelatedEventSection";
