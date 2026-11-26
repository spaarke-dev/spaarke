/**
 * The Billing firm field of the Invoice Review header (ebilling-r1 task 095d-3). It stands in for the firm's name ONLY while the invoice has no
 * firm, the approval state says the firm is needed and the caller can write; the page decides that and mounts this component, so nothing is
 * requested for any other invoice.
 *
 * The firms offered are the ones linked to the invoice's core record (GET core-records/{type}/{id}/firms, contract section 8). One linked firm
 * is still a choice the reviewer makes: nothing is saved until Set firm is pressed. The save itself is the page's (`onSet`): it is one write
 * through `runWrite`, the same busy / refusal / re-read path as every other write, and the server decides (ADR-002; the firm of an invoice
 * that has one is never changed, `ebilling.assign_firm.firm_already_set`).
 */

import * as React from 'react';
import { Button, Caption1, Dropdown, Field, MessageBar, MessageBarActions, MessageBarBody, Option, Spinner, makeStyles, tokens } from '@fluentui/react-components';
import { firmsFailureMessage, type InvoiceReviewApi, type ReviewCoreRecord, type ReviewFirm } from '../services/invoiceReviewApi';
import { CORE_KIND_LABEL, SET_FIRM_EFFECT_WORDS } from '../services/reviewModel';

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalXS, maxWidth: '520px' },
  row: { display: 'flex', alignItems: 'flex-end', gap: tokens.spacingHorizontalS, flexWrap: 'wrap' },
  field: { flex: 1, minWidth: '220px' },
  muted: { color: tokens.colorNeutralForeground3 },
});

type FirmList = { kind: 'loading' } | { kind: 'ready'; firms: ReviewFirm[] } | { kind: 'failed'; message: string };

export interface SetFirmFieldProps {
  api: Pick<InvoiceReviewApi, 'listFirms'>;
  /** The invoice's core record: the firms offered are the ones linked to it. */
  core: ReviewCoreRecord | null;
  /** A write is out (or the invoice needs nothing more from the page): the field and the button are disabled. */
  busy: boolean;
  /** The reviewer pressed Set firm with this firm chosen. */
  onSet: (firmId: string) => void;
  /** The reviewer chose another firm: the page drops the last refusal it showed. */
  onChoose: () => void;
}

/** "this matter", "this project", "this work assignment" (a record the page does not know the kind of is "this record"). */
function recordWords(core: ReviewCoreRecord | null): string {
  return core ? `this ${(CORE_KIND_LABEL[core.type] ?? 'record').toLowerCase()}` : 'this record';
}

export function SetFirmField({ api, core, busy, onSet, onChoose }: SetFirmFieldProps) {
  const s = useStyles();
  const [list, setList] = React.useState<FirmList>(core ? { kind: 'loading' } : { kind: 'ready', firms: [] });
  const [chosen, setChosen] = React.useState<string | null>(null);
  const [attempt, setAttempt] = React.useState(0);
  const coreType = core?.type ?? null;
  const coreId = core?.id ?? null;

  React.useEffect(() => {
    if (coreType === null || coreId === null) {
      setList({ kind: 'ready', firms: [] });
      return undefined;
    }
    let live = true;
    setList({ kind: 'loading' });
    void api.listFirms(coreType, coreId).then(r => {
      if (!live) return;
      if (r.ok) {
        setList({ kind: 'ready', firms: r.value });
        // A firm chosen before a retry stays chosen only while it is still on offer.
        setChosen(prev => (prev !== null && r.value.some(f => f.firmId === prev) ? prev : null));
      } else {
        setList({ kind: 'failed', message: firmsFailureMessage(r.failure) });
      }
    });
    return () => {
      live = false;
    };
  }, [api, coreType, coreId, attempt]);

  if (list.kind === 'loading') {
    return (
      <div className={s.root} data-testid="set-firm" data-state="loading">
        <Spinner size="tiny" label="Loading the firms linked to this record…" labelPosition="after" />
      </div>
    );
  }

  if (list.kind === 'failed') {
    return (
      <div className={s.root} data-testid="set-firm" data-state="failed">
        <MessageBar intent="error" data-testid="set-firm-load-error">
          <MessageBarBody>{list.message}</MessageBarBody>
          <MessageBarActions>
            <Button size="small" onClick={() => setAttempt(n => n + 1)} data-testid="set-firm-retry">
              Retry
            </Button>
          </MessageBarActions>
        </MessageBar>
      </div>
    );
  }

  if (list.firms.length === 0) {
    return (
      <div className={s.root} data-testid="set-firm" data-state="empty">
        <MessageBar intent="warning" data-testid="set-firm-empty">
          <MessageBarBody>No firm is linked to {recordWords(core)}. Add one on the record first.</MessageBarBody>
        </MessageBar>
      </div>
    );
  }

  const chosenFirm = list.firms.find(f => f.firmId === chosen) ?? null;
  return (
    <div className={s.root} data-testid="set-firm" data-state="ready">
      <div className={s.row}>
        <Field label="Billing firm" className={s.field}>
          <Dropdown
            aria-label="Billing firm"
            placeholder="Choose the billing firm"
            value={chosenFirm?.name ?? ''}
            selectedOptions={chosenFirm ? [chosenFirm.firmId] : []}
            disabled={busy}
            onOptionSelect={(_, d) => {
              if (d.optionValue) {
                setChosen(d.optionValue);
                onChoose();
              }
            }}
            data-testid="set-firm-dropdown"
          >
            {list.firms.map(f => (
              <Option key={f.firmId} value={f.firmId} text={f.name}>
                {f.name}
              </Option>
            ))}
          </Dropdown>
        </Field>
        <Button appearance="primary" disabled={busy || chosenFirm === null} onClick={() => chosenFirm && onSet(chosenFirm.firmId)} data-testid="set-firm-save">
          Set firm
        </Button>
      </div>
      <Caption1 className={s.muted} data-testid="set-firm-effect">
        {SET_FIRM_EFFECT_WORDS}
      </Caption1>
    </div>
  );
}
