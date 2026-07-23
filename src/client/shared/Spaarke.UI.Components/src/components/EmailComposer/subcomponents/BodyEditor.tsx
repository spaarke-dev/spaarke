/**
 * BodyEditor.tsx
 *
 * HTML rich-text + PlainText body editor with a format toggle (design §5.6.3).
 * Sends what was authored — no conversion on toggle (each format keeps its own
 * buffer so switching back and forth doesn't lossily round-trip content).
 *
 * Owner UAT 2026-07-22: default is rich text (HTML); the Rich text / Plain text
 * toggle floats at the top-right, over the RTF toolbar (rich) or the textarea's
 * top-right (plain). Plain mode inherently drops the RTF formatting tools (it
 * renders a bare Textarea). No "Message" field label. The editor is vertically
 * resizeable.
 *
 * Reuses the existing `RichTextEditor` (Lexical, Code-Pages-only per ADR-012's
 * PCF-import table) for HTML mode — component justification: EmailComposer is
 * explicitly React-18/19-only with no PCF mount (task 020 §5.7), the exact
 * condition under which RichTextEditor is already sanctioned for reuse
 * elsewhere in the shared lib. A bespoke HTML editor would duplicate existing,
 * tested functionality for no benefit.
 */
import * as React from 'react';
import { Tooltip, Button, Textarea, makeStyles, tokens, mergeClasses } from '@fluentui/react-components';
import { TextFont20Regular, TextT20Regular } from '@fluentui/react-icons';
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
    position: 'relative',
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    flex: 1,
    minHeight: 0,
  },
  // Editor area — `position: relative` anchors the floating format toggle; the
  // whole area is vertically resizeable (owner UAT #6). overflowX hidden removes
  // the horizontal scrollbar (owner UAT round 3 #3).
  editorArea: {
    position: 'relative',
    display: 'flex',
    flexDirection: 'column',
    flexGrow: 1,
    minHeight: 0,
    resize: 'vertical',
    overflowY: 'auto',
    overflowX: 'hidden',
  },
  // Rich/Plain toggle floats at the top-right, sitting at the right end of the RTF
  // toolbar row (owner UAT #4). The RTF tools are left-aligned so this never overlaps them.
  toggleFloat: {
    position: 'absolute',
    top: tokens.spacingVerticalXXS,
    right: tokens.spacingHorizontalS,
    zIndex: 1,
    display: 'flex',
    gap: tokens.spacingHorizontalXS,
  },
  editorFill: {
    flexGrow: 1,
    minHeight: 0,
  },
  plainTextarea: {
    flexGrow: 1,
    minHeight: '180px',
    fontFamily: tokens.fontFamilyMonospace,
  },
  errorText: {
    color: tokens.colorPaletteRedForeground1,
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
  errorMessage,
  minHeight = 200,
}) => {
  const styles = useStyles();

  return (
    <div className={styles.wrapper} role="region" aria-label="Message body">
      {!readOnly && (
        // Single icon toggle (owner UAT round 3 #3): shows the CURRENT mode's glyph;
        // click switches. Rich = formatting glyph; Plain = plain-"T" glyph.
        <div className={styles.toggleFloat} role="group" aria-label="Body format">
          <Tooltip
            content={format === 'HTML' ? 'Rich text — switch to plain text' : 'Plain text — switch to rich text'}
            relationship="label"
          >
            <Button
              appearance="subtle"
              size="small"
              icon={format === 'HTML' ? <TextFont20Regular /> : <TextT20Regular />}
              aria-label={format === 'HTML' ? 'Switch to plain text' : 'Switch to rich text'}
              onClick={() => onFormatChange(format === 'HTML' ? 'PlainText' : 'HTML')}
            />
          </Tooltip>
        </div>
      )}

      <div className={styles.editorArea} style={{ minHeight }}>

        {format === 'HTML' ? (
          <div className={styles.editorFill}>
            <RichTextEditor
              value={value}
              onChange={onChange}
              readOnly={readOnly}
              minHeight={minHeight}
              placeholder="Compose your message..."
            />
          </div>
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
      </div>

      {errorMessage && (
        <span className={styles.errorText} role="alert">
          {errorMessage}
        </span>
      )}
    </div>
  );
};

BodyEditor.displayName = 'BodyEditor';
