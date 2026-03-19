/**
 * IntentWizardFlow.tsx
 *
 * Streamlined 3-step wizard flow for intent-based playbook execution.
 *
 * When a PlaybookLibraryShell receives an `intent` prop with `mode === 'intent'`,
 * this component renders a focused Upload Files -> Analysis -> Results flow
 * instead of the full browse/custom-scope UI.
 *
 * The scope configuration is locked (read-only) because the intent fully
 * determines which playbook and scopes to use.
 */

import React from 'react';
import {
  Text,
  MessageBar,
  MessageBarBody,
  Spinner,
  Badge,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import {
  DocumentArrowUp24Regular,
  BrainCircuit24Regular,
  CheckmarkCircle24Regular,
} from '@fluentui/react-icons';
import { ScopeConfigurator } from '../Playbook/ScopeConfigurator';
import type {
  IPlaybook,
  IAction,
  ISkill,
  IKnowledge,
  ITool,
  IPlaybookScopes,
} from '../Playbook/types';

// ---------------------------------------------------------------------------
// Intent-to-playbook mapping
// ---------------------------------------------------------------------------

/**
 * Maps known intent strings to playbook identifiers.
 * When an intent is provided, the shell looks up the playbook ID here first,
 * then falls back to fuzzy name matching against available playbooks.
 */
export const INTENT_PLAYBOOK_MAP: Record<string, string> = {
  'email-compose': 'playbook-email-draft',
  'assign-counsel': 'playbook-counsel-assign',
  'meeting-schedule': 'playbook-meeting-prep',
};

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IIntentWizardFlowProps {
  /** The resolved playbook for this intent. */
  playbook: IPlaybook;
  /** Locked scope configuration for the resolved playbook. */
  playbookScopes: IPlaybookScopes;
  /** All available actions (for scope preview rendering). */
  actions: IAction[];
  /** All available skills (for scope preview rendering). */
  skills: ISkill[];
  /** All available knowledge items (for scope preview rendering). */
  knowledge: IKnowledge[];
  /** All available tools (for scope preview rendering). */
  tools: ITool[];
  /** Whether the analysis is currently being created. */
  isExecuting: boolean;
  /** Error message to display, if any. */
  error: string | null;
}

// ---------------------------------------------------------------------------
// Wizard step type
// ---------------------------------------------------------------------------

type IntentStep = 'upload' | 'analysis' | 'results';

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalL,
  },
  stepIndicator: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalM,
    paddingBottom: tokens.spacingVerticalM,
    borderBottomWidth: '1px',
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
  },
  stepDot: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    width: '28px',
    height: '28px',
    borderRadius: '50%',
    backgroundColor: tokens.colorBrandBackground,
    color: tokens.colorNeutralForegroundOnBrand,
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
  },
  stepDotInactive: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    width: '28px',
    height: '28px',
    borderRadius: '50%',
    backgroundColor: tokens.colorNeutralBackground3,
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
  },
  stepDotCompleted: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    width: '28px',
    height: '28px',
    borderRadius: '50%',
    backgroundColor: tokens.colorPaletteGreenBackground3,
    color: tokens.colorNeutralForegroundOnBrand,
  },
  stepConnector: {
    width: '32px',
    height: '2px',
    backgroundColor: tokens.colorNeutralStroke2,
  },
  stepConnectorActive: {
    width: '32px',
    height: '2px',
    backgroundColor: tokens.colorBrandBackground,
  },
  playbookHeader: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalM,
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    backgroundColor: tokens.colorNeutralBackground3,
    borderRadius: tokens.borderRadiusMedium,
  },
  scopeSection: {
    opacity: 0.85,
  },
  lockedLabel: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    marginBottom: tokens.spacingVerticalS,
  },
  stepContent: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    minHeight: '200px',
  },
  uploadPlaceholder: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    gap: tokens.spacingVerticalM,
    paddingTop: tokens.spacingVerticalXXL,
    paddingBottom: tokens.spacingVerticalXXL,
    borderWidth: '2px',
    borderStyle: 'dashed',
    borderColor: tokens.colorNeutralStroke2,
    borderRadius: tokens.borderRadiusMedium,
    color: tokens.colorNeutralForeground3,
  },
  analysisPlaceholder: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    gap: tokens.spacingVerticalM,
    paddingTop: tokens.spacingVerticalXXL,
    paddingBottom: tokens.spacingVerticalXXL,
  },
  resultsPlaceholder: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    justifyContent: 'center',
    gap: tokens.spacingVerticalM,
    paddingTop: tokens.spacingVerticalXXL,
    paddingBottom: tokens.spacingVerticalXXL,
  },
});

// ---------------------------------------------------------------------------
// Step definitions
// ---------------------------------------------------------------------------

const INTENT_STEPS: { id: IntentStep; label: string }[] = [
  { id: 'upload', label: 'Upload Files' },
  { id: 'analysis', label: 'Analysis' },
  { id: 'results', label: 'Results' },
];

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const IntentWizardFlow: React.FC<IIntentWizardFlowProps> = ({
  playbook,
  playbookScopes,
  actions,
  skills,
  knowledge,
  tools,
  isExecuting,
  error,
}) => {
  const styles = useStyles();

  // Derive the current step from execution state.
  // In a full integration the step would advance as the analysis progresses;
  // for now we show "upload" by default, "analysis" while executing, and
  // "results" would be shown by the parent upon completion.
  const currentStep: IntentStep = isExecuting ? 'analysis' : 'upload';
  const currentStepIndex = INTENT_STEPS.findIndex(s => s.id === currentStep);

  return (
    <div className={styles.root}>
      {/* Step indicator */}
      <div className={styles.stepIndicator}>
        {INTENT_STEPS.map((step, idx) => (
          <React.Fragment key={step.id}>
            {idx > 0 && (
              <div
                className={
                  idx <= currentStepIndex
                    ? styles.stepConnectorActive
                    : styles.stepConnector
                }
              />
            )}
            <div
              className={
                idx < currentStepIndex
                  ? styles.stepDotCompleted
                  : idx === currentStepIndex
                    ? styles.stepDot
                    : styles.stepDotInactive
              }
              title={step.label}
            >
              {idx < currentStepIndex ? (
                <CheckmarkCircle24Regular />
              ) : (
                <Text size={200} weight="semibold">
                  {idx + 1}
                </Text>
              )}
            </div>
            <Text
              size={200}
              weight={idx === currentStepIndex ? 'semibold' : 'regular'}
              style={{
                color:
                  idx === currentStepIndex
                    ? tokens.colorNeutralForeground1
                    : tokens.colorNeutralForeground3,
              }}
            >
              {step.label}
            </Text>
          </React.Fragment>
        ))}
      </div>

      {/* Playbook header — shows which playbook is locked */}
      <div className={styles.playbookHeader}>
        <BrainCircuit24Regular />
        <div>
          <Text size={300} weight="semibold">
            {playbook.name}
          </Text>
          {playbook.description && (
            <Text
              size={200}
              block
              style={{ color: tokens.colorNeutralForeground3 }}
            >
              {playbook.description}
            </Text>
          )}
        </div>
        <Badge appearance="outline" size="small" color="informative">
          Locked
        </Badge>
      </div>

      {/* Error bar */}
      {error && (
        <MessageBar intent="error">
          <MessageBarBody>{error}</MessageBarBody>
        </MessageBar>
      )}

      {/* Step content */}
      <div className={styles.stepContent}>
        {currentStep === 'upload' && (
          <div className={styles.uploadPlaceholder}>
            <DocumentArrowUp24Regular />
            <Text size={300}>
              Drop files here or click to upload
            </Text>
            <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>
              File upload will be connected in integration tasks.
            </Text>
          </div>
        )}

        {currentStep === 'analysis' && (
          <div className={styles.analysisPlaceholder}>
            <Spinner size="large" label="Running analysis..." />
          </div>
        )}
      </div>

      {/* Locked scope preview */}
      <div className={styles.scopeSection}>
        <div className={styles.lockedLabel}>
          <Text size={200} weight="semibold">
            Scope Configuration
          </Text>
          <Badge appearance="outline" size="small" color="subtle">
            Read-only
          </Badge>
        </div>
        <ScopeConfigurator
          actions={actions}
          skills={skills}
          knowledge={knowledge}
          tools={tools}
          selectedActionIds={playbookScopes.actionIds}
          selectedSkillIds={playbookScopes.skillIds}
          selectedKnowledgeIds={playbookScopes.knowledgeIds}
          selectedToolIds={playbookScopes.toolIds}
          onActionChange={() => {}}
          onSkillChange={() => {}}
          onKnowledgeChange={() => {}}
          onToolChange={() => {}}
          readOnly
        />
      </div>
    </div>
  );
};

IntentWizardFlow.displayName = 'IntentWizardFlow';

export default IntentWizardFlow;
