/**
 * The Notify step of the decision wizard (ebilling-r1 task 132ui; FR-39, FR-50, EB-D-258): on Reject and Return, the reviewer sees the notice the
 * firm would be emailed (drafted by the server, 131), may edit its subject and message, and chooses whether to send it with the decision.
 *
 *   Reject / Return:   Decision -> Reason -> Notify (this step; skippable) -> Confirm
 *
 * Nothing is computed or decided here (ADR-002): the server drafts the text, screens the confirmed notice before it decides (a refusal is a
 * 400 shown under these fields) and sends the email only when the request carries the notice. The limits shown are hints that mirror the
 * server's caps. The findings table, the amounts and the clause quotes are added by Spaarke in the email and are never typed here.
 *
 * The state lives in the wizard's form (`form.notices`), one entry per kind, because this step unmounts whenever the reviewer leaves it:
 * the draft is asked for once per kind per opened wizard, and edits to one kind survive switching the decision and back. There is no HTML
 * preview: the server's `html` is not rendered here (it would need a sandboxed iframe; the text fields are what the reviewer edits).
 */

import * as React from 'react';
import { Button, Caption1, Field, Input, MessageBar, MessageBarActions, MessageBarBody, MessageBarTitle, Spinner, Switch, Text, Textarea, makeStyles, tokens } from '@fluentui/react-components';
import {
  NOTICE_PROSE_MAX,
  NOTICE_SUBJECT_MAX,
  noticeCounterText,
  noticeFieldProblems,
  noticeKindOf,
  noticeSourceText,
  proseCount,
  subjectCount,
} from '../../services/noticeModel';
import { useWiz } from './wizardContext';

const useStyles = makeStyles({
  step: { display: 'flex', flexDirection: 'column', rowGap: tokens.spacingVerticalL, maxWidth: '640px' },
  muted: { color: tokens.colorNeutralForeground3 },
  row: { display: 'flex', columnGap: tokens.spacingHorizontalM, alignItems: 'center', flexWrap: 'wrap' },
});

/** An aria-describedby value: the ids that exist, space-separated (Fluent's Field adds its own hint and message ids to it). */
const describedBy = (...ids: Array<string | null>) => ids.filter(Boolean).join(' ');

export function NotifyStep() {
  const s = useStyles();
  const { form, readOnly, loadNotice, patchNotice } = useWiz();
  const kind = noticeKindOf(form.outcome);
  const entry = kind ? form.notices[kind] : undefined;
  const locked = form.locked;
  const status = entry?.status;
  // Review round 1 K4: the counters and the server's refusal are tied to the fields they describe.
  const uid = React.useId();
  const subjectCountId = uid + '-subject-count';
  const proseCountId = uid + '-prose-count';
  const refusedId = uid + '-refused';

  // The draft is asked for when the step is first shown, once per kind (the wizard keeps the guard). A locked reopen holds the request as sent.
  React.useEffect(() => {
    if (kind && status === 'idle' && !readOnly) loadNotice(kind);
  }, [kind, status, readOnly, loadNotice]);

  if (!kind || !entry) return null;

  const problems = noticeFieldProblems(entry);
  const draft = entry.draft;
  const refused = entry.send ? form.noticeError : null;
  const edit = (patch: Parameters<typeof patchNotice>[1]) => patchNotice(kind, patch, true);

  return (
    <div className={s.step} data-testid="notify-step">
      <Text>
        Tell the firm about this decision. The email carries the notice below, then a findings table. Nothing is sent until you finish the decision, and you can send
        the decision without the notice.
      </Text>

      {(entry.status === 'idle' || entry.status === 'loading') && <Spinner size="small" labelPosition="after" label="Drafting the notice to the firm…" data-testid="notice-loading" />}

      {entry.status === 'failed' && (
        <MessageBar intent="warning" layout="multiline" data-testid="notice-draft-error">
          <MessageBarBody>
            <MessageBarTitle>The notice could not be drafted</MessageBarTitle>
            <Text as="p" data-testid="notice-draft-error-text">
              {entry.failure}
            </Text>
            <Text as="p">You can continue without it: the decision is recorded and the firm gets no email.</Text>
          </MessageBarBody>
          <MessageBarActions>
            <Button size="small" onClick={() => loadNotice(kind, true)} data-testid="notice-retry">
              Retry
            </Button>
          </MessageBarActions>
        </MessageBar>
      )}

      {entry.status === 'ready' && (
        <>
          <Switch
            checked={entry.send}
            disabled={locked}
            onChange={(_, d) => edit({ send: d.checked })}
            label="Send this notice to the firm"
            data-testid="notice-send"
          />

          {!entry.send && (
            <Text className={s.muted} data-testid="notice-off">
              The firm gets no email for this decision. The decision is still recorded.
            </Text>
          )}

          {entry.send && (
            <>
              {noticeSourceText(entry.draft) && (
                <Caption1 className={s.muted} data-testid="notice-source">
                  {noticeSourceText(entry.draft)}
                </Caption1>
              )}

              <div>
                <Field
                  label="Subject"
                  required
                  validationState={problems.subject ? 'error' : 'none'}
                  validationMessage={problems.subject ?? undefined}
                  hint={entry.subjectSuffix !== '' ? 'The tracking number in brackets is added by Spaarke and does not count toward the limit.' : undefined}
                >
                  <Input
                    value={entry.subject}
                    readOnly={locked}
                    onChange={(_, d) => edit({ subject: d.value })}
                    aria-label="Notice subject"
                    aria-describedby={describedBy(subjectCountId, refused ? refusedId : null)}
                    data-testid="notice-subject"
                  />
                </Field>
                <Caption1 id={subjectCountId} className={s.muted} data-testid="notice-subject-count">
                  {noticeCounterText(subjectCount(entry.subject, entry.subjectSuffix), NOTICE_SUBJECT_MAX)}
                </Caption1>
              </div>

              <div>
                <Field
                  label="Message"
                  required
                  validationState={problems.prose ? 'error' : 'none'}
                  validationMessage={problems.prose ?? undefined}
                  hint="Plain text. Do not type numbers, amounts, links or line numbers: the server refuses them."
                >
                  <Textarea
                    value={entry.prose}
                    readOnly={locked}
                    onChange={(_, d) => edit({ prose: d.value })}
                    resize="vertical"
                    rows={8}
                    aria-label="Notice message"
                    aria-describedby={describedBy(proseCountId, refused ? refusedId : null)}
                    data-testid="notice-prose"
                  />
                </Field>
                <Caption1 id={proseCountId} className={s.muted} data-testid="notice-prose-count">
                  {noticeCounterText(proseCount(entry.prose), NOTICE_PROSE_MAX)}
                </Caption1>
              </div>

              <Caption1 className={s.muted} data-testid="notice-added-by-spaarke">
                The findings table, the amounts and the clause quotes are added by Spaarke and cannot be edited here.
              </Caption1>

              {draft && (
                <div className={s.row}>
                  <Button appearance="subtle" disabled={locked} onClick={() => edit({ subject: draft.subject, prose: draft.prose })} data-testid="notice-restore">
                    Restore draft
                  </Button>
                </div>
              )}
            </>
          )}

          {refused && (
            <MessageBar id={refusedId} intent="error" layout="multiline" data-testid="notice-refused">
              <MessageBarBody>{refused}</MessageBarBody>
            </MessageBar>
          )}

          {locked && (
            <Caption1 className={s.muted} data-testid="notice-locked">
              This decision was saved in part, or no answer came back. Repeating it sends the same notice (or none), so it cannot be changed now. To decide differently, go
              Back and choose another decision.
            </Caption1>
          )}
        </>
      )}
    </div>
  );
}
