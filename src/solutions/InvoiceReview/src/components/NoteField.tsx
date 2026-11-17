/**
 * The optional Note box (ebilling-r1 task 095f-2ui; the pattern of RecordProcedures' WizardFrame Note, copied rather than imported across
 * solutions). One Fluent v9 textarea with a count against the limit and a field message under it, used by every decision form of the page:
 * the decision wizard, the blocked-submission Override / Return, the line decision, the bulk decision, the correction and confirm forms and
 * the invoice adjustment (add and remove).
 *
 * It has NO `maxLength` (ISS-027): a browser cuts a paste at `maxLength` without a word, and the reviewer would save a different note from
 * the one they read. The box keeps what is typed, shows `n / 2,000 characters`, and says so when the note is over the limit; the form
 * holds its submit off for that, and the server's 400 (`ebilling.note_too_long`, `ebilling.note_invalid_characters`) is the backstop, shown
 * here too through `serverError`.
 */

import { Caption1, Field, Textarea, makeStyles, tokens } from '@fluentui/react-components';
import { noteCounterText, noteProblem } from '../services/noteModel';

const useStyles = makeStyles({
  count: { color: tokens.colorNeutralForeground3 },
});

export interface NoteFieldProps {
  value: string;
  onChange: (value: string) => void;
  /** The server's refusal of the note (a 400 note code), shown as the field message until the note is edited. */
  serverError?: string | null;
  /** The note is kept but cannot be edited (a saved-in-part decision is repeated with the same note). */
  readOnly?: boolean;
  disabled?: boolean;
  /** Names the box for assistive technology and tests; the visible label stays "Note". */
  ariaLabel?: string;
  testId?: string;
  rows?: number;
}

export function NoteField({ value, onChange, serverError = null, readOnly, disabled, ariaLabel = 'Note', testId = 'note', rows = 3 }: NoteFieldProps) {
  const s = useStyles();
  const problem = noteProblem(value) ?? serverError;
  return (
    <div data-testid={`${testId}-field`}>
      <Field label="Note" hint="Optional. Recorded with this decision." validationState={problem ? 'error' : 'none'} validationMessage={problem ?? undefined}>
        <Textarea
          value={value}
          onChange={(_, d) => onChange(d.value)}
          readOnly={readOnly}
          disabled={disabled}
          resize="vertical"
          rows={rows}
          aria-label={ariaLabel}
          data-testid={testId}
        />
      </Field>
      <Caption1 className={s.count} data-testid={`${testId}-count`}>
        {noteCounterText(value)}
      </Caption1>
    </div>
  );
}
