/**
 * Rule Review page shell (ebilling-r1 task 077, FR-28): the rule set list, "New rule set", and the review of one rule set.
 *
 * No router (ADR-026): the open rule set is state. Access is decided by the server (D-76, admin-only): the page does not guess.
 * The billing-admin 403 (reasonCode `sdap.access.deny.insufficient_privilege`) turns the page read-only (write controls hidden, a notice
 * shown); a list that is itself refused that way shows the notice alone. Any other 403 (a Procedure's `insufficient_rights`, a
 * `system_failure`) refuses that one call: it shows its own notice and leaves the access as it was. A 503 `ebilling.disabled` shows the feature-disabled notice.
 */

import * as React from 'react';
import { Button, Spinner, makeStyles, tokens } from '@fluentui/react-components';
import { NewRuleSetModal, type NewRuleSetValues } from './components/NewRuleSetModal';
import { NoticeBar, PageNotice, failureNotice, type Notice } from './components/Notices';
import { RuleSetDetailView } from './components/RuleSetDetailView';
import { RuleSetListView } from './components/RuleSetListView';
import { useFirmNames } from './components/useFirmNames';
import type { FirmLookup } from './services/firmLookup';
import {
  accessAfterFailure,
  isBillingAdminRefusal,
  isEbillingDisabled,
  type ApiFailure,
  type PageAccess,
  type RuleSetApi,
  type RuleSetSummary,
} from './services/ruleSetApi';

const useStyles = makeStyles({
  root: {
    display: 'flex', flexDirection: 'column', height: '100%', overflow: 'hidden',
    backgroundColor: tokens.colorNeutralBackground1, color: tokens.colorNeutralForeground1,
  },
  banner: { padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalL} 0` },
  fill: { flexGrow: 1, minHeight: 0 },
  centered: { display: 'flex', alignItems: 'center', justifyContent: 'center', height: '100%' },
  retry: { marginTop: tokens.spacingVerticalM },
});

export interface AppProps {
  api: RuleSetApi;
  firmLookup: FirmLookup;
}

export const App: React.FC<AppProps> = ({ api, firmLookup }) => {
  const s = useStyles();
  const [access, setAccess] = React.useState<PageAccess>('unknown');
  const [disabled, setDisabled] = React.useState(false);
  const [listRefused, setListRefused] = React.useState(false);
  const [ruleSets, setRuleSets] = React.useState<RuleSetSummary[] | null>(null);
  const [listNotice, setListNotice] = React.useState<Notice | null>(null);
  const [openId, setOpenId] = React.useState<string | null>(null);
  const [newOpen, setNewOpen] = React.useState(false);
  const [creating, setCreating] = React.useState(false);
  const [createNotice, setCreateNotice] = React.useState<Notice | null>(null);
  const firmNames = useFirmNames(firmLookup, (ruleSets ?? []).map(r => r.firmId));

  /** Every failure of every call comes through here: the billing-admin 403 is read-only, a disabled flag is its own notice. */
  const onFailure = React.useCallback((failure: ApiFailure): void => {
    if (isEbillingDisabled(failure)) setDisabled(true);
    else setAccess(prev => accessAfterFailure(prev, failure));
  }, []);

  const loadList = React.useCallback(async (): Promise<void> => {
    const result = await api.list();
    if (result.ok) {
      setRuleSets(result.value);
      setListNotice(null);
      setListRefused(false);
      setAccess(prev => (prev === 'readOnly' ? prev : 'admin'));
      return;
    }

    onFailure(result.failure);
    setRuleSets([]);
    if (isBillingAdminRefusal(result.failure)) setListRefused(true);
    else setListNotice(failureNotice(result.failure, 'The rule sets could not be loaded'));
  }, [api, onFailure]);

  React.useEffect(() => {
    void loadList();
  }, [loadList]);

  const canWrite = access !== 'readOnly';

  const create = async (values: NewRuleSetValues): Promise<void> => {
    setCreating(true);
    setCreateNotice(null);
    const result = await api.create({
      name: values.name, sourceKind: values.sourceKind, scope: values.scope, firmId: values.firmId,
      file: values.file, fileName: values.file.name,
    });
    setCreating(false);
    if (result.ok) {
      setNewOpen(false);
      void loadList();
      setOpenId(result.value.ruleSetId); // straight to the review, where "Draft rules" starts the drafting
    } else {
      onFailure(result.failure);
      setCreateNotice(failureNotice(result.failure, 'The rule set was not created'));
      if (isBillingAdminRefusal(result.failure) || isEbillingDisabled(result.failure)) setNewOpen(false);
    }
  };

  if (disabled) {
    return (
      <PageNotice testId="ebilling-disabled" title="eBilling is not enabled">
        The eBilling feature is switched off for this environment, so rule sets cannot be reviewed here. Ask an administrator to enable it.
      </PageNotice>
    );
  }

  if (ruleSets === null && access === 'unknown') {
    return (
      <div className={s.centered} data-testid="page-loading">
        <Spinner size="large" label="Loading rule sets" />
      </div>
    );
  }

  if (listRefused) {
    return (
      <PageNotice testId="read-only-notice" title="Read only">
        You do not have the billing administrator right, so you cannot see or change rule sets. Ask an administrator to grant it.
        <br />
        <Button appearance="primary" className={s.retry} onClick={() => void loadList()}>
          Check again
        </Button>
      </PageNotice>
    );
  }

  return (
    <div className={s.root}>
      {access === 'readOnly' && (
        <div className={s.banner} data-testid="read-only-banner">
          <NoticeBar
            notice={{
              intent: 'warning',
              title: 'Read only',
              text: 'You do not have the billing administrator right, so changes are not available. You can still look at the rules.',
            }}
          />
        </div>
      )}

      <div className={s.fill}>
        {openId ? (
          <RuleSetDetailView
            key={openId}
            api={api}
            firmLookup={firmLookup}
            ruleSetId={openId}
            canWrite={canWrite}
            onBack={() => {
              setOpenId(null);
              void loadList();
            }}
            onFailure={onFailure}
            onListChanged={() => void loadList()}
          />
        ) : (
          <>
            {listNotice && (
              <div className={s.banner}>
                <NoticeBar notice={listNotice} onDismiss={() => setListNotice(null)} />
              </div>
            )}
            <RuleSetListView
              ruleSets={ruleSets}
              firmNames={firmNames}
              canWrite={canWrite}
              onOpen={setOpenId}
              onNew={() => {
                setCreateNotice(null);
                setNewOpen(true);
              }}
              onRefresh={() => void loadList()}
            />
          </>
        )}
      </div>

      <NewRuleSetModal
        open={newOpen}
        busy={creating}
        firmLookup={firmLookup}
        notice={createNotice}
        onClose={() => !creating && setNewOpen(false)}
        onCreate={values => void create(values)}
      />
    </div>
  );
};
