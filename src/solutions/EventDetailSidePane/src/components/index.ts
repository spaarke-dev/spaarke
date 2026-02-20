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
  CollapsibleSection,
  type CollapsibleSectionProps,
} from "./CollapsibleSection";

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
  UnsavedChangesDialog,
  type UnsavedChangesDialogProps,
  type UnsavedChangesAction,
} from "./UnsavedChangesDialog";

export { MemoSection, type MemoSectionProps } from "./MemoSection";
export { TodoSection, type TodoSectionProps } from "./TodoSection";
