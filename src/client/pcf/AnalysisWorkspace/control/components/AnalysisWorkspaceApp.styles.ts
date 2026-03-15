/**
 * Styles for AnalysisWorkspaceApp component
 *
 * Three-column layout styles using Fluent UI v9 makeStyles + semantic tokens.
 * Extracted from AnalysisWorkspaceApp.tsx to keep the component focused on
 * hook composition and layout JSX.
 */

import { makeStyles, tokens } from '@fluentui/react-components';

export const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    // Use viewport height minus header space (~180px for Dataverse form header/tabs)
    // This ensures the control fills available vertical space
    height: 'calc(100vh - 180px)',
    minHeight: '500px', // Ensure minimum height for usability
    width: '100%',
    backgroundColor: tokens.colorNeutralBackground1,
    overflow: 'hidden',
  },
  content: {
    display: 'flex',
    flex: 1,
    overflow: 'hidden',
  },
  // Left Panel - Analysis Output (resizable)
  leftPanel: {
    flex: '1 1 40%',
    minWidth: '250px',
    display: 'flex',
    flexDirection: 'column',
    overflow: 'hidden',
    position: 'relative' as const,
  },
  // Center Panel - Source Document (resizable, collapsible)
  centerPanel: {
    flex: '1 1 35%',
    minWidth: '200px',
    display: 'flex',
    flexDirection: 'column',
    overflow: 'hidden',
    position: 'relative' as const,
    transition: 'flex-basis 0.2s ease, min-width 0.2s ease, opacity 0.2s ease',
  },
  // Right Panel - Conversation (collapsible)
  rightPanel: {
    flex: '0 0 350px',
    minWidth: '300px',
    maxWidth: '450px',
    display: 'flex',
    flexDirection: 'column',
    overflow: 'hidden',
    transition: 'flex-basis 0.2s ease, min-width 0.2s ease, opacity 0.2s ease',
  },
  rightPanelCollapsed: {
    flex: '0 0 0px',
    minWidth: '0px',
    maxWidth: '0px',
    opacity: 0,
    overflow: 'hidden',
  },
  centerPanelCollapsed: {
    flex: '0 0 0px',
    minWidth: '0px',
    opacity: 0,
    overflow: 'hidden',
  },
  // Resize handle between panels
  resizeHandle: {
    width: '4px',
    cursor: 'col-resize',
    backgroundColor: tokens.colorNeutralStroke1,
    transition: 'background-color 0.15s ease',
    '&:hover': {
      backgroundColor: tokens.colorBrandBackground,
    },
    '&:active': {
      backgroundColor: tokens.colorBrandBackgroundPressed,
    },
  },
  panelHeader: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: tokens.spacingHorizontalS,
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`,
    borderBottom: `1px solid ${tokens.colorNeutralStroke1}`,
    backgroundColor: tokens.colorNeutralBackground3,
    minHeight: '40px',
  },
  panelHeaderLeft: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  panelHeaderActions: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    flexShrink: 0,
    // Prevent layout shifts on hover by ensuring consistent dimensions
    '& button': {
      minWidth: '32px',
      minHeight: '32px',
    },
  },
  editorContainer: {
    flex: 1,
    overflow: 'auto',
    padding: tokens.spacingHorizontalM,
  },
  documentPreview: {
    flex: 1,
    overflow: 'auto',
    padding: tokens.spacingHorizontalM,
    backgroundColor: tokens.colorNeutralBackground1,
  },
  documentPreviewEmpty: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    height: '100%',
    color: tokens.colorNeutralForeground3,
    gap: tokens.spacingVerticalM,
  },
  chatContainer: {
    flex: 1,
    display: 'flex',
    flexDirection: 'column',
    overflow: 'hidden',
  },
  chatMessages: {
    flex: 1,
    overflow: 'auto',
    padding: tokens.spacingHorizontalM,
  },
  chatMessage: {
    marginBottom: tokens.spacingVerticalM,
    padding: tokens.spacingHorizontalS,
    borderRadius: tokens.borderRadiusMedium,
  },
  chatMessageUser: {
    backgroundColor: tokens.colorBrandBackground2,
    marginLeft: '20%',
  },
  chatMessageAssistant: {
    backgroundColor: tokens.colorNeutralBackground3,
    marginRight: '10%',
  },
  chatMessageRole: {
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground2,
    marginBottom: tokens.spacingVerticalXS,
  },
  chatMessageContent: {
    fontSize: tokens.fontSizeBase300,
    lineHeight: '1.5',
    whiteSpace: 'pre-wrap' as const,
  },
  chatInputContainer: {
    padding: tokens.spacingHorizontalM,
    borderTop: `1px solid ${tokens.colorNeutralStroke1}`,
    backgroundColor: tokens.colorNeutralBackground2,
  },
  chatInputWrapper: {
    display: 'flex',
    gap: tokens.spacingHorizontalS,
    alignItems: 'flex-end',
  },
  chatTextarea: {
    flex: 1,
  },
  loadingContainer: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    height: '100%',
    gap: tokens.spacingVerticalL,
  },
  errorContainer: {
    padding: tokens.spacingHorizontalL,
  },
  statusIndicator: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
    padding: tokens.spacingHorizontalS,
    minHeight: '24px',
  },
  savedIndicator: {
    color: tokens.colorStatusSuccessForeground1,
  },
  unsavedIndicator: {
    color: tokens.colorStatusWarningForeground1,
  },
  versionFooter: {
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground3,
    textAlign: 'center' as const,
    padding: tokens.spacingVerticalXS,
    borderTop: `1px solid ${tokens.colorNeutralStroke1}`,
    backgroundColor: tokens.colorNeutralBackground2,
  },
  streamingIndicator: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    padding: tokens.spacingHorizontalS,
    color: tokens.colorBrandForeground1,
    minHeight: '24px',
  },
  sprkChatWrapper: {
    flex: 1,
    display: 'flex',
    flexDirection: 'column',
    overflow: 'hidden',
  },
  // Choice Dialog Styles (ADR-023)
  choiceDialogContent: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  choiceOptionsContainer: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    marginTop: tokens.spacingVerticalM,
  },
  choiceOptionButton: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'flex-start',
    gap: tokens.spacingHorizontalM,
    padding: tokens.spacingVerticalM,
    width: '100%',
    textAlign: 'left' as const,
    minHeight: '64px',
  },
  choiceOptionIcon: {
    fontSize: '24px',
    color: tokens.colorBrandForeground1,
    flexShrink: 0,
  },
  choiceOptionText: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    overflow: 'hidden',
  },
  choiceOptionTitle: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  choiceOptionDescription: {
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase200,
  },
});
