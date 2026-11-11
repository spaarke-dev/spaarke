/**
 * "New rule set" (ebilling-r1 task 077, FR-28): an admin names a rule set, says what the document is, whether it applies to the
 * client default or to one firm, and uploads the guideline document. The server stores the document in the Procedure Library and
 * creates a Draft rule set; the drafting job (`Draft rules`, 075) runs afterwards.
 *
 * A `FormModal` (ADR-050). The source kind sets the initial "visible to firms" flag on the server (guidelines and rate cards on,
 * internal procedures off - D-66); the hint says so.
 */

import * as React from 'react';
import { Button, Dropdown, Field, Input, Option, Radio, RadioGroup, Text, makeStyles, tokens } from '@fluentui/react-components';
import { FormModal, LookupField, type ILookupItem } from '@spaarke/ui-components';
import { NoticeBar, type Notice } from './Notices';
import type { RuleSetScope, RuleSetSourceKind } from '../services/ruleSetApi';
import type { FirmLookup } from '../services/firmLookup';

const useStyles = makeStyles({
  file: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS, flexWrap: 'wrap' },
  hidden: { display: 'none' },
  muted: { color: tokens.colorNeutralForeground3 },
});

const SOURCE_KINDS: { value: RuleSetSourceKind; label: string; hint: string }[] = [
  { value: 'Guideline', label: 'Outside counsel guideline', hint: 'Firms will be able to see the quoted clauses of its published rules.' },
  { value: 'RateCard', label: 'Rate card or rate letter', hint: 'Firms will be able to see the quoted clauses of its published rules.' },
  { value: 'InternalProcedure', label: 'Internal procedure', hint: 'Firms will not see anything from it.' },
];

export interface NewRuleSetValues {
  name: string;
  sourceKind: RuleSetSourceKind;
  scope: RuleSetScope;
  firmId: string | null;
  file: File;
}

export interface NewRuleSetModalProps {
  open: boolean;
  busy: boolean;
  firmLookup: FirmLookup;
  /** The refusal of the last attempt (400 per-field errors, 403, 409, 413 ...). */
  notice: Notice | null;
  onClose: () => void;
  onCreate: (values: NewRuleSetValues) => void;
}

export const NewRuleSetModal: React.FC<NewRuleSetModalProps> = ({ open, busy, firmLookup, notice, onClose, onCreate }) => {
  const s = useStyles();
  const fileInput = React.useRef<HTMLInputElement | null>(null);
  const [name, setName] = React.useState('');
  const [sourceKind, setSourceKind] = React.useState<RuleSetSourceKind>('Guideline');
  const [scope, setScope] = React.useState<RuleSetScope>('ClientDefault');
  const [firm, setFirm] = React.useState<ILookupItem | null>(null);
  const [file, setFile] = React.useState<File | null>(null);

  React.useEffect(() => {
    if (open) {
      setName('');
      setSourceKind('Guideline');
      setScope('ClientDefault');
      setFirm(null);
      setFile(null);
    }
  }, [open]);

  const kind = SOURCE_KINDS.find(k => k.value === sourceKind) ?? SOURCE_KINDS[0];
  const ready = name.trim().length > 0 && file !== null && (scope === 'ClientDefault' || firm !== null);

  return (
    <FormModal
      open={open}
      onClose={onClose}
      onSubmit={() => {
        if (ready && file) onCreate({ name: name.trim(), sourceKind, scope, firmId: scope === 'Firm' ? (firm?.id ?? null) : null, file });
      }}
      title="New rule set"
      size="md"
      submitLabel="Create"
      submitDisabled={!ready}
      busy={busy}
    >
      <Field label="Name" required>
        <Input value={name} maxLength={200} onChange={(_, d) => setName(d.value)} data-testid="new-name" />
      </Field>

      <Field label="The document is" required hint={kind.hint}>
        <Dropdown
          value={kind.label}
          selectedOptions={[sourceKind]}
          onOptionSelect={(_, d) => setSourceKind((d.optionValue as RuleSetSourceKind) ?? 'Guideline')}
          data-testid="new-kind"
        >
          {SOURCE_KINDS.map(k => (
            <Option key={k.value} value={k.value}>
              {k.label}
            </Option>
          ))}
        </Dropdown>
      </Field>

      <Field label="Applies to" required>
        <RadioGroup layout="horizontal" value={scope} onChange={(_, d) => setScope(d.value as RuleSetScope)} data-testid="new-scope">
          <Radio value="ClientDefault" label="Client default" />
          <Radio value="Firm" label="One firm" />
        </RadioGroup>
      </Field>

      {scope === 'Firm' && (
        <LookupField
          label="Firm"
          required
          placeholder="Search firms (two letters or more)"
          value={firm}
          onChange={setFirm}
          onSearch={q => firmLookup.search(q)}
          minSearchLength={2}
        />
      )}

      <Field label="Guideline document" required hint="The document is stored in the Procedure Library; firms are never given a link to it.">
        <div className={s.file}>
          <input
            ref={fileInput}
            type="file"
            accept=".pdf,.doc,.docx,.txt,.rtf"
            className={s.hidden}
            onChange={e => setFile(e.target.files?.[0] ?? null)}
            data-testid="new-file"
          />
          <Button onClick={() => fileInput.current?.click()} disabled={busy}>
            Choose file
          </Button>
          <Text className={file ? undefined : s.muted} data-testid="new-file-name">
            {file ? file.name : 'No file chosen'}
          </Text>
        </div>
      </Field>

      {notice && <NoticeBar notice={notice} />}
    </FormModal>
  );
};
