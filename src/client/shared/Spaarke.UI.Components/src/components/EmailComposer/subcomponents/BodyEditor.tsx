/**
 * BodyEditor.tsx
 *
 * HTML rich-text + PlainText body editor with a format toggle (design §5.6.3).
 * Sends what was authored — no conversion on toggle (each format keeps its own
 * buffer so switching back and forth doesn't lossily round-trip content).
 *
 * Reuses the existing `RichTextEditor` (Lexical, Code-Pages-only per ADR-012's
 * PCF-import table) for HTML mode — component justification: EmailComposer is
 * explicitly React-18/19-only with no PCF mount (task 020 §5.7), the exact
 * condition under which RichTextEditor is already sanctioned for reuse
 * elsewhere in the shared lib. A bespoke HTML editor would duplicate existing,
 * tested functionality for no benefit.
 */
import * as React from 'react';
import { Field, Textarea, ToggleButton, makeStyles, tokens, mergeClasses } from '@fluentui/react-components';
import { RichTextEditor } from '../../RichTextEditor';
import type { EmailComposerBodyFormat } from '../EmailComposer.types';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IBodyEditorProps {
  value: string;
  format: EmailComposerBodyFormat;
  onChange: (value: string) => void;
  onFormatChange: (format: EmailComposerBodyFormat) => void;
  readOnly?: boolean;
  required?: boolean;
  errorMessage?: string;
  minHeight?: number;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  wrapper: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    flex: 1,
    minHeight: 0,
  },
  formatRow: {
    display: 'flex',
    gap: tokens.spacingHorizontalXS,
  },
  field: {
    display: 'flex',
    flexDirection: 'column',
    flex: 1,
    minHeight: 0,
  },
  plainTextarea: {
    flex: 1,
    minHeight: '180px',
    fontFamily: tokens.fontFamilyMonospace,
  },
  errorText: {
    color: tokens.colorPaletteRedForeground1,
  },
  requiredMark: {
    color: tokens.colorPaletteRedForeground1,
  },
  labelRow: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXXS,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const BodyEditor: React.FC<IBodyEditorProps> = ({
  value,
  format,
  onChange,
  onFormatChange,
  readOnly,
  required,
  errorMessage,
  minHeight = 200,
}) => {
  const styles = useStyles();

  const renderLabel = (): React.ReactElement => (
    <span className={styles.labelRow}>
      Message
      {required && (
        <span aria-hidden="true" className={styles.requiredMark}>
          {' *'}
        </span>
      )}
    </span>
  );

  return (
    <div className={styles.wrapper} role="region" aria-label="Message body">
      <div className={styles.formatRow} role="group" aria-label="Body format">
        <ToggleButton
          checked={format === 'HTML'}
          onClick={() => onFormatChange('HTML')}
          disabled={readOnly}
          size="small"
          appearance={format === 'HTML' ? 'primary' : 'outline'}
        >
          Rich text
        </ToggleButton>
        <ToggleButton
          checked={format === 'PlainText'}
          onClick={() => onFormatChange('PlainText')}
          disabled={readOnly}
          size="small"
          appearance={format === 'PlainText' ? 'primary' : 'outline'}
        >
          Plain text
        </ToggleButton>
      </div>

      <Field label={renderLabel()} className={styles.field} validationState={errorMessage ? 'error' : 'none'}>
        {format === 'HTML' ? (
          <RichTextEditor
            value={value}
            onChange={onChange}
            readOnly={readOnly}
            minHeight={minHeight}
            placeholder="Compose your message..."
          />
        ) : (
          <Textarea
            className={mergeClasses(styles.plainTextarea)}
            value={value}
            onChange={e => onChange(e.target.value)}
            placeholder="Compose your message..."
            aria-label="Message body"
            disabled={readOnly}
            resize="vertical"
          />
        )}
      </Field>

      {errorMessage && (
        <span className={styles.errorText} role="alert">
          {errorMessage}
        </span>
      )}
    </div>
  );
};

BodyEditor.displayName = 'BodyEditor';
