/**
 * MatterHeaderView - Composition of shared RecordHeader primitives for sprk_matter.
 *
 * Implements spec FR-12 (REVISED post task 001) — thin composition of
 * `@spaarke/ui-components` primitives with the 5-field header + sparkle popover
 * wired via `useRecordHeaderToolbarActions`. Entity is compile-time-fixed as
 * `"sprk_matter"`.
 *
 * Field payload (verified via Dataverse MCP — see
 * `projects/record-header-and-notepad-r1/notes/design-alignment-corrections.md`):
 *   sprk_matternumber (span 1, required), sprk_mattername (span 2),
 *   sprk_mattertype (span 1), sprk_practicearea (span 1),
 *   sprk_matterdescription (span 3), sprk_recordsummary (sparkle popover body).
 *
 * Theming: platform-library Fluent v9 auto-applies host theme because the
 * manifest declares `control-type="virtual"` + `<platform-library>` (approach 1
 * in `.claude/patterns/pcf/fluent-v9-modern-theming.md`). No `FluentProvider`
 * wrap here — the class returns the element directly. Tests wrap explicitly
 * because jsdom has no host theme.
 *
 * Popover: consumer owns the `<Popover>` shell so the sparkle button (rendered
 * by `HeaderToolbar` inside `RecordHeaderShell`) remains the anchor.
 *
 * Standards: ADR-006 / ADR-012 / ADR-021 semantic tokens / ADR-022 React 16-17
 * safe; NFR-05 no `@spaarke/auth`; NFR-07 no BFF; NFR-02 total PCF LOC ≤ 100
 * excluding shared primitives.
 *
 * @see FR-12 in projects/record-header-and-notepad-r1/spec.md
 * @see projects/record-header-and-notepad-r1/notes/design-alignment-corrections.md
 * @see src/client/shared/Spaarke.UI.Components/src/__tests__/recordHeader.integration.test.tsx
 */

import * as React from 'react';
import { Popover, PopoverSurface, makeStyles, tokens } from '@fluentui/react-components';
import {
  FieldGrid,
  RecordHeaderLookupField,
  RecordHeaderShell,
  TextField,
  TextareaField,
  useRecordFieldValues,
  useRecordHeaderToolbarActions,
} from '@spaarke/ui-components';
import { CONTROL_VERSION } from './version';

const ENTITY = 'sprk_matter';
const FIELDS = [
  'sprk_matternumber', 'sprk_mattername', 'sprk_mattertype',
  'sprk_practicearea', 'sprk_matterdescription', 'sprk_recordsummary',
];

const useStyles = makeStyles({
  root: { position: 'relative', width: '100%' },
  versionFooter: {
    position: 'absolute',
    right: tokens.spacingHorizontalXS,
    bottom: tokens.spacingVerticalXXS,
    fontSize: tokens.fontSizeBase100,
    color: tokens.colorNeutralForeground4,
    pointerEvents: 'none',
    userSelect: 'none',
  },
});

export interface IMatterHeaderViewProps {
  /** Record GUID (no braces). Empty string = "no record selected". */
  recordId: string;
}

export const MatterHeaderView: React.FC<IMatterHeaderViewProps> = ({ recordId }) => {
  const styles = useStyles();
  const { values, loading } = useRecordFieldValues(ENTITY, recordId, FIELDS);
  const { toolbarProps, sparklePopoverOpen, setSparklePopoverOpen, sparklePopoverContent } =
    useRecordHeaderToolbarActions({
      entity: ENTITY,
      recordId,
      recordSummary: (values?.sprk_recordsummary ?? null) as string | null,
    });

  return (
    <div className={styles.root}>
      <RecordHeaderShell toolbar={toolbarProps} loading={loading}>
        <FieldGrid columns={3}>
          <TextField span={1} label="Matter Number" value={values?.sprk_matternumber as string | undefined} required />
          <TextField span={2} label="Matter Name" value={values?.sprk_mattername as string | undefined} />
          <RecordHeaderLookupField span={1} label="Matter Type" value={values?.sprk_mattertype as unknown as never} />
          <RecordHeaderLookupField span={1} label="Practice Area" value={values?.sprk_practicearea as unknown as never} />
          <TextareaField span={3} label="Matter Description" value={values?.sprk_matterdescription as string | undefined} />
        </FieldGrid>
      </RecordHeaderShell>
      <Popover open={sparklePopoverOpen} onOpenChange={(_, d) => setSparklePopoverOpen(d.open)}>
        <PopoverSurface>{sparklePopoverContent}</PopoverSurface>
      </Popover>
      <span className={styles.versionFooter} aria-hidden="true" data-testid="matter-header-version">
        v{CONTROL_VERSION}
      </span>
    </div>
  );
};
