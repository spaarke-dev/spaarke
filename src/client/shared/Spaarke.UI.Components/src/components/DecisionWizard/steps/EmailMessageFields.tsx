/**
 * EmailMessageFields - a message step's `to`, `subject` and `body` in the shared EmailComposer (task 160; D-179, owner 2026-10-23).
 *
 * Used when the plan marks the action `composer: "email"`. The composer is COMPOSE-ONLY: no Send, no From row, no External /
 * Internal Only tags, and it never sends or saves anything itself; the commit route sends the email, from the deciding user's
 * mailbox, at Record decision. Nothing is written here: the composer's state flows into the step's values through `onStateChange`,
 * in the shape the executors already accept (`to` = the addresses joined with "; ", plain-text subject and body), so the
 * wizard's own rules (Next disabled until required fields are present; the discard check) are unchanged.
 *
 * - Starting values are the step's current values (typed, else the defaults: the templated subject and body, D-53, and the plan's
 *   To pre-fill). The composer is mounted with them and is uncontrolled from then on.
 * - The To lookup opens the host's side-pane lookup for contacts (task 158's `openLookup`) and adds the pick by its email address;
 *   a pick with no usable address adds nothing and the step says so (a warning: type one in To); a side pane that fails to open says so the same way. Both warnings clear when the next lookup starts or To changes. Typed addresses are accepted as the
 *   composer accepts them; Next stays disabled while any address in To is not one (`stepValid`).
 * - The body is committed as plain text: a body the user switched to rich text is converted back.
 * - Cc and Bcc are not part of the action: when the user adds any, the step says they will not be sent.
 *
 * Fluent v9 tokens only (ADR-021). No Xrm, no auth import (ADR-012, ADR-028).
 */

import * as React from 'react';
import { MessageBar, MessageBarBody, makeStyles, tokens } from '@fluentui/react-components';
import { EmailComposer } from '../../EmailComposer/EmailComposer';
import { EMAIL_RE } from '../../EmailComposer/EmailComposer.reducer';
import type { EmailComposerState, IRecipient } from '../../EmailComposer/EmailComposer.types';
import type { AuthenticatedFetchFn } from '../../../services/EntityCreationService';
import { fieldValue, withValue } from '../decisionDraft';
import type { StepScope } from '../decisionDraft';
import { useDecisionItem } from '../decisionItemContext';

/** The parameter codes the composer edits. */
export const EMAIL_MESSAGE_CODES: readonly string[] = ['to', 'subject', 'body'];

const SEPARATORS = /[;,\s]+/;

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', rowGap: tokens.spacingVerticalS },
});

/** The composer never sends in compose-only mode; a fetch that refuses makes that certain. */
const refuseFetch = (() =>
  Promise.reject(new Error('The decision wizard never sends from the composer.'))) as unknown as AuthenticatedFetchFn;

/** Plain text of a body the user switched to rich text: block ends become line breaks, tags are dropped. */
function toPlainText(body: string, format: EmailComposerState['bodyFormat']): string {
  if (format !== 'HTML' || !/[<&]/.test(body)) return body;
  // DOMParser builds an inert document: nothing in the markup runs (an `innerHTML` write on a detached element would run
  // <img onerror>, and the body carries the record name).
  const withBreaks = body.replace(/<\s*br\s*\/?>/gi, '\n').replace(/<\s*\/\s*(p|div|li|h[1-6])\s*>/gi, '\n');
  const text = new DOMParser().parseFromString(withBreaks, 'text/html').body.textContent ?? '';
  return text.replace(/\n{3,}/g, '\n\n').trim();
}

export const EmailMessageFields: React.FC<{ scope: StepScope }> = ({ scope }) => {
  const { draft, update, openLookup } = useDecisionItem();
  const styles = useStyles();
  const latest = React.useRef(draft);
  latest.current = draft;
  const [otherRecipients, setOtherRecipients] = React.useState(false);
  // The contact picked in the side pane that gave no usable address: said out loud, never a silent no-op.
  const [noAddressFor, setNoAddressFor] = React.useState<string | null>(null);
  // The side pane threw (not a cancel): said out loud too, never a silent no-op.
  const [lookupFailed, setLookupFailed] = React.useState(false);

  // The composer is uncontrolled: it starts from the step's values as they are when the step opens.
  const [initial] = React.useState(() => ({
    to: fieldValue(draft, scope, 'to')
      .split(SEPARATORS)
      .map(a => a.trim())
      .filter(Boolean),
    subject: fieldValue(draft, scope, 'subject'),
    body: fieldValue(draft, scope, 'body'),
  }));

  const onStateChange = React.useCallback(
    (state: EmailComposerState) => {
      setOtherRecipients(state.cc.length > 0 || state.bcc.length > 0);
      const next: Record<string, string> = {
        to: state.to
          .map(r => r.email.trim())
          .filter(Boolean)
          .join('; '),
        subject: state.subject,
        body: toPlainText(state.body, state.bodyFormat),
      };
      // Only a real change is written, so mounting the composer over the starting values is not a choice (discard check).
      const changed = Object.keys(next).filter(code => fieldValue(latest.current, scope, code) !== next[code]);
      if (changed.length === 0) return;
      // The user has now changed To (typed an address, or removed one): "type one in To" no longer applies.
      if (changed.includes('to')) {
        setNoAddressFor(null);
        setLookupFailed(false);
      }
      update(d => changed.reduce((acc, code) => withValue(acc, scope, code, next[code]), d));
    },
    [scope, update]
  );

  const paneForContacts =
    openLookup != null && (openLookup.supportedEntities == null || openLookup.supportedEntities.includes('contact'));
  const onLookupRecipients = React.useMemo(() => {
    if (!paneForContacts || !openLookup) return undefined;
    return async (field: 'to' | 'cc' | 'bcc'): Promise<IRecipient[] | null> => {
      setNoAddressFor(null);
      setLookupFailed(false);
      try {
        const picked = await openLookup({ kind: 'Person', entity: 'contact', parameterCode: field });
        if (!picked) return null; // the pane was cancelled
        const email = picked.email?.trim();
        // A contact with no usable address (none on file, or the read failed) cannot receive the inquiry: nothing is added,
        // and the step says so.
        if (!email || !EMAIL_RE.test(email)) {
          setNoAddressFor(picked.name?.trim() || 'That contact');
          return null;
        }
        return [{ email, displayName: picked.name, resolved: true, sourceId: picked.id, entityType: 'contact' }];
      } catch {
        setLookupFailed(true);
        return null;
      }
    };
  }, [openLookup, paneForContacts]);

  return (
    <div className={styles.root} data-testid="email-message-fields">
      <EmailComposer
        mode="compose"
        mount="inline"
        hostOwnsChrome
        composeOnly
        sendMode="user"
        authenticatedFetch={refuseFetch}
        initialTo={initial.to}
        initialSubject={initial.subject}
        initialBody={initial.body}
        initialBodyFormat="PlainText"
        showAssociations={false}
        attachmentSources={[]}
        onStateChange={onStateChange}
        onLookupRecipients={onLookupRecipients}
      />
      {noAddressFor ? (
        <MessageBar intent="warning" data-testid="no-email">
          <MessageBarBody>{noAddressFor} has no email address that could be read; type one in To.</MessageBarBody>
        </MessageBar>
      ) : null}
      {lookupFailed ? (
        <MessageBar intent="warning" data-testid="lookup-failed">
          <MessageBarBody>The contact lookup could not be opened; type the address in To.</MessageBarBody>
        </MessageBar>
      ) : null}
      {otherRecipients ? (
        <MessageBar intent="warning" data-testid="cc-not-sent">
          <MessageBarBody>Cc and Bcc are not sent with this step. Add every recipient to To.</MessageBarBody>
        </MessageBar>
      ) : null}
    </div>
  );
};
