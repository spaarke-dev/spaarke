/**
 * Assignment and visibility of a rule set (ebilling-r1 task 077, FR-28, D-66): the rule set applies to the client default or to one
 * firm, and its "visible to firms" flag decides whether firms see the quoted clauses of its published rules.
 *
 * Both are single calls to the BFF (`PUT .../assignment`, `PUT .../visibility`); the server validates the firm and refuses a retired set.
 */

import * as React from 'react';
import { Button, Dropdown, Field, Option, Switch, Text, makeStyles, tokens } from '@fluentui/react-components';
import { LookupField, type ILookupItem } from '@spaarke/ui-components';
import type { RuleSetScope, RuleSetSummary } from '../services/ruleSetApi';
import type { FirmLookup } from '../services/firmLookup';

const useStyles = makeStyles({
  root: {
    display: 'flex', flexWrap: 'wrap', alignItems: 'flex-end', gap: tokens.spacingHorizontalL,
    padding: tokens.spacingHorizontalM, borderRadius: tokens.borderRadiusMedium,
    border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`, backgroundColor: tokens.colorNeutralBackground1,
  },
  field: { minWidth: '220px' },
  lookup: { minWidth: '280px' },
  visibility: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalXXS },
  muted: { color: tokens.colorNeutralForeground3 },
});

const SCOPE_LABEL: Record<RuleSetScope, string> = { ClientDefault: 'Client default', Firm: 'A firm' };

export interface AssignmentPanelProps {
  ruleSet: RuleSetSummary;
  /** The assigned firm's name, when known. */
  firmName: string | null;
  firmLookup: FirmLookup;
  canWrite: boolean;
  busy: boolean;
  onAssign: (scope: RuleSetScope, firmId: string | null) => void;
  onVisibility: (visible: boolean) => void;
}

export const AssignmentPanel: React.FC<AssignmentPanelProps> = ({
  ruleSet, firmName, firmLookup, canWrite, busy, onAssign, onVisibility,
}) => {
  const s = useStyles();
  const currentScope: RuleSetScope | null = ruleSet.scope === 'Firm' ? 'Firm' : ruleSet.scope === 'ClientDefault' ? 'ClientDefault' : null;
  const [scope, setScope] = React.useState<RuleSetScope>(currentScope ?? 'ClientDefault');
  const [firm, setFirm] = React.useState<ILookupItem | null>(
    ruleSet.firmId ? { id: ruleSet.firmId, name: firmName ?? ruleSet.firmId } : null
  );

  // A reload (after an assignment, or another admin's change) puts the stored assignment back in the controls.
  React.useEffect(() => {
    setScope(currentScope ?? 'ClientDefault');
    setFirm(ruleSet.firmId ? { id: ruleSet.firmId, name: firmName ?? ruleSet.firmId } : null);
  }, [currentScope, ruleSet.firmId, firmName]);

  const retired = ruleSet.status === 'Retired';
  const disabled = !canWrite || retired || busy;
  const changed = scope !== currentScope || (scope === 'Firm' && firm?.id !== ruleSet.firmId);
  const canAssign = !disabled && changed && (scope === 'ClientDefault' || firm !== null);

  return (
    <div className={s.root} data-testid="assignment-panel">
      <Field label="Applies to" className={s.field}>
        <Dropdown
          value={SCOPE_LABEL[scope]}
          selectedOptions={[scope]}
          disabled={disabled}
          onOptionSelect={(_, d) => setScope((d.optionValue as RuleSetScope) ?? 'ClientDefault')}
          data-testid="assign-scope"
        >
          <Option value="ClientDefault">{SCOPE_LABEL.ClientDefault}</Option>
          <Option value="Firm">{SCOPE_LABEL.Firm}</Option>
        </Dropdown>
      </Field>

      {scope === 'Firm' && (
        <div className={s.lookup} data-testid="assign-firm">
          <LookupField
            label="Firm"
            required
            placeholder="Search firms (two letters or more)"
            value={firm}
            onChange={setFirm}
            onSearch={q => firmLookup.search(q)}
            minSearchLength={2}
          />
        </div>
      )}

      {canWrite && !retired && (
        <Button appearance="primary" disabled={!canAssign} onClick={() => onAssign(scope, scope === 'Firm' ? (firm?.id ?? null) : null)} data-testid="assign-save">
          Save assignment
        </Button>
      )}

      <div className={s.visibility}>
        <Switch
          label="Visible to firms"
          checked={ruleSet.visibleToFirms}
          disabled={disabled}
          onChange={(_, d) => onVisibility(d.checked)}
          data-testid="visibility-switch"
        />
        <Text size={200} className={s.muted}>
          {ruleSet.visibleToFirms
            ? 'Firms see the quoted clause text of the published rules.'
            : 'Firms see the rule name and reason only, never the clause text.'}
        </Text>
      </div>
    </div>
  );
};
