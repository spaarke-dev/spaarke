import * as React from 'react';
import { Radio, RadioGroup, Text, makeStyles, mergeClasses, tokens, useId } from '@fluentui/react-components';
import { canBeInvoice } from './invoiceMarking';
import type { InvoiceAttachmentRole, MarkableAttachment } from './invoiceMarking';

export interface InvoiceAttachmentMarkingProps {
  attachments: MarkableAttachment[];
  value: Record<string, InvoiceAttachmentRole>;
  onChange: (next: Record<string, InvoiceAttachmentRole>) => void;
  mode: 'new' | 'correction' | 'supporting-only';
  disabled?: boolean;
}

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', rowGap: tokens.spacingVerticalS },
  row: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    justifyContent: 'space-between',
    columnGap: tokens.spacingHorizontalM,
    padding: tokens.spacingVerticalS,
    borderRadius: tokens.borderRadiusMedium,
    border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    backgroundColor: tokens.colorNeutralBackground1,
  },
  rowDisabled: { backgroundColor: tokens.colorNeutralBackground3 },
  info: { display: 'flex', flexDirection: 'column', minWidth: 0 },
  name: { overflowWrap: 'anywhere', color: tokens.colorNeutralForeground1 },
  meta: { color: tokens.colorNeutralForeground3 },
  reason: { color: tokens.colorNeutralForeground3, fontStyle: 'italic' },
});

function formatSize(bytes?: number): string | undefined {
  if (bytes === undefined || bytes < 0) return undefined;
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

export const InvoiceAttachmentMarking: React.FC<InvoiceAttachmentMarkingProps> = ({
  attachments,
  value,
  onChange,
  mode,
  disabled,
}) => {
  const styles = useStyles();
  const baseId = useId('invoice-marking');

  return (
    <div className={styles.root} role="list">
      {attachments.map((a, i) => {
        const locked = !!a.disabledReason;
        const role: InvoiceAttachmentRole = locked ? 'skip' : (value[a.id] ?? 'supporting');
        const size = formatSize(a.sizeBytes);
        const nameId = `${baseId}-${i}`;
        return (
          <div key={a.id} role="listitem" className={mergeClasses(styles.row, locked && styles.rowDisabled)}>
            <div className={styles.info}>
              <Text className={styles.name} id={nameId}>
                {a.fileName}
              </Text>
              {size && (
                <Text size={200} className={styles.meta}>
                  {size}
                </Text>
              )}
              {locked && (
                <Text size={200} className={styles.reason}>
                  {a.disabledReason}
                </Text>
              )}
            </div>
            <RadioGroup
              layout="horizontal"
              aria-labelledby={nameId}
              value={role}
              disabled={disabled || locked}
              onChange={(_e, data) => {
                if (disabled || locked) return;
                onChange({ ...value, [a.id]: data.value as InvoiceAttachmentRole });
              }}
            >
              {canBeInvoice(a, mode) && <Radio value="invoice" label="Invoice" />}
              <Radio value="supporting" label="Supporting" />
              <Radio value="skip" label="Don't save" />
            </RadioGroup>
          </div>
        );
      })}
    </div>
  );
};
