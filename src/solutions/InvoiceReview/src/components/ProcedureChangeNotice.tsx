/**
 * ebilling-r1 task 113b: shows the server's read-time Procedure-change banner (task 113, `procedureChange` on the review read) above the
 * lines. The server decides the state; this only picks how loudly to say it:
 *   Changed           -> warning, the server's sentence ("... re-check to apply them"). There is no re-check action in R1 (ebilling-r2).
 *   CouldNotBeChecked -> warning: an unknown is never shown as "no change".
 *   NotYetChecked     -> info, titled so the server's short text reads as a sentence.
 *   A state this page does not know -> info with the server's text (nothing if it has no text), never silently "no change".
 *   NotChanged or absent -> nothing.
 */
import { MessageBar, MessageBarBody, MessageBarTitle } from '@fluentui/react-components';
import type { ProcedureChange } from '../services/invoiceReviewApi';

/** The server's sentences (ProcedureChangeBanner.cs), used only when a state arrives without its text. */
export const PROCEDURE_CHANGE_FALLBACK = {
  Changed: 'The procedures that apply to this invoice have changed; re-check to apply them.',
  NotYetChecked: 'Not yet checked',
  CouldNotBeChecked: 'Procedure changes could not be checked',
} as const;

export interface ProcedureChangeView {
  intent: 'warning' | 'info';
  title: string | null;
  text: string;
}

const textOr = (text: string | null | undefined, fallback: string) => (text && text.trim() ? text : fallback);

/** The banner to show for a review's `procedureChange`, or null for none. */
export function procedureChangeView(change: ProcedureChange | null | undefined): ProcedureChangeView | null {
  switch (change?.state) {
    case 'Changed':
      return { intent: 'warning', title: null, text: textOr(change.text, PROCEDURE_CHANGE_FALLBACK.Changed) };
    case 'CouldNotBeChecked':
      return { intent: 'warning', title: null, text: textOr(change.text, PROCEDURE_CHANGE_FALLBACK.CouldNotBeChecked) };
    case 'NotYetChecked':
      return { intent: 'info', title: 'Procedure changes', text: textOr(change.text, PROCEDURE_CHANGE_FALLBACK.NotYetChecked) };
    case 'NotChanged':
    case undefined:
      return null;
    default: {
      // A state this page does not know yet: show the server's text rather than read it as "no change"; without text there is nothing to say.
      const text = change?.text;
      return text && text.trim() ? { intent: 'info', title: 'Procedure changes', text } : null;
    }
  }
}

export function ProcedureChangeNotice({ change }: { change: ProcedureChange | null | undefined }) {
  const view = procedureChangeView(change);
  if (!view) return null;
  return (
    <MessageBar intent={view.intent} data-testid="procedure-change-notice" data-state={change?.state}>
      <MessageBarBody>
        {view.title && <MessageBarTitle>{view.title}</MessageBarTitle>}
        {view.text}
      </MessageBarBody>
    </MessageBar>
  );
}
