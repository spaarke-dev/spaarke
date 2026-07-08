/**
 * ScopeConfigEditorApp
 *
 * Root component that auto-detects the entity logical name (and, where it
 * matters, the bound column) and renders the appropriate editor variant:
 *   sprk_analysisaction      → ActionEditor (system prompt)
 *                              or SchemaJsonEditor when bound to
 *                              sprk_inputschema / sprk_outputschemajson (053)
 *   sprk_analysisskill      → SkillEditor
 *   sprk_analysisknowledge  → KnowledgeSourceEditor
 *   sprk_analysistool       → ToolEditor
 *   sprk_playbookconsumer   → BindingConfigEditor (Binding variant, FR-P4-04)
 *
 * ADR-021: All styling via makeStyles / Fluent v9 tokens. No hardcoded colors.
 * ADR-022: React 16 APIs only. No createRoot.
 */

import * as React from 'react';
import { makeStyles, tokens, shorthands, MessageBar, MessageBarBody, Text } from '@fluentui/react-components';
import { ActionEditor } from './ActionEditor';
import { SkillEditor } from './SkillEditor';
import { KnowledgeSourceEditor } from './KnowledgeSourceEditor';
import { ToolEditor } from './ToolEditor';
import { BindingConfigEditor } from './BindingConfigEditor';
import { SchemaJsonEditor } from './SchemaJsonEditor';

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export interface IScopeConfigEditorAppProps {
  /** Dataverse entity logical name (e.g., "sprk_analysisaction") */
  entityLogicalName: string;
  /** Logical name of the bound column (e.g., "sprk_chiptransitions"); '' when unavailable */
  boundAttributeName?: string;
  /** Current field value from the bound property */
  fieldValue: string;
  /** BFF API base URL for handler discovery (resolved at runtime from Dataverse env var) */
  apiBaseUrl: string;
  /** Error message if BFF API URL could not be resolved from Dataverse env var */
  apiBaseUrlError?: string;
  /** Callback when value changes — propagates to PCF output */
  onValueChange: (newValue: string) => void;
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    width: '100%',
    boxSizing: 'border-box',
    minHeight: '200px',
    padding: tokens.spacingVerticalS,
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
  },
  unknownEntity: {
    padding: tokens.spacingVerticalM,
  },
  versionFooter: {
    marginTop: tokens.spacingVerticalM,
    paddingTop: tokens.spacingVerticalXS,
    ...shorthands.borderTop('1px', 'solid', tokens.colorNeutralStroke2),
    color: tokens.colorNeutralForeground4,
    fontSize: tokens.fontSizeBase100,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

export const ScopeConfigEditorApp: React.FC<IScopeConfigEditorAppProps> = ({
  entityLogicalName,
  boundAttributeName = '',
  fieldValue,
  apiBaseUrl,
  apiBaseUrlError,
  onValueChange,
}) => {
  const styles = useStyles();

  const renderEditor = (): React.ReactElement => {
    const entity = entityLogicalName.toLowerCase();
    const attribute = boundAttributeName.toLowerCase();

    if (entity === 'sprk_analysisaction') {
      // Schema columns get the validated OpenAI-subset editor (task 053 —
      // the G-P3 round-1 outage shape must not be authorable on the form).
      if (attribute === 'sprk_inputschema' || attribute === 'sprk_outputschemajson') {
        return <SchemaJsonEditor boundAttributeName={attribute} value={fieldValue} onChange={onValueChange} />;
      }
      return <ActionEditor value={fieldValue} onChange={onValueChange} />;
    }

    if (entity === 'sprk_playbookconsumer') {
      return <BindingConfigEditor boundAttributeName={attribute} value={fieldValue} onChange={onValueChange} />;
    }

    if (entity === 'sprk_analysisskill') {
      return <SkillEditor value={fieldValue} onChange={onValueChange} />;
    }

    if (entity === 'sprk_analysisknowledge') {
      return <KnowledgeSourceEditor value={fieldValue} onChange={onValueChange} />;
    }

    if (entity === 'sprk_analysistool') {
      return <ToolEditor value={fieldValue} apiBaseUrl={apiBaseUrl} onChange={onValueChange} />;
    }

    // Fallback: show informational message
    return (
      <div className={styles.unknownEntity}>
        <MessageBar intent="warning">
          <MessageBarBody>
            ScopeConfigEditor: unknown entity type &quot;{entityLogicalName}
            &quot;. Expected one of: sprk_analysisaction, sprk_analysisskill, sprk_analysisknowledge, sprk_analysistool,
            sprk_playbookconsumer.
          </MessageBarBody>
        </MessageBar>
      </div>
    );
  };

  return (
    <div className={styles.root}>
      {renderEditor()}
      <Text className={styles.versionFooter}>v1.3.0 &bull; Built 2026-07-07</Text>
    </div>
  );
};
