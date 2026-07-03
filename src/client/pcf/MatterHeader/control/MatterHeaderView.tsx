/**
 * MatterHeaderView - Composition of shared RecordHeader primitives for sprk_matter.
 *
 * PLACEHOLDER (task 021). Task 022 replaces this stub with the full composition
 * per spec.md FR-12 (RecordHeaderShell + HeaderToolbar + FieldGrid + field renderers).
 *
 * Entity is compile-time-fixed as "sprk_matter" — there is no runtime entity/fieldSchema
 * property on this PCF's manifest. Each per-entity PCF is a ~80 LOC thin composition
 * of the shared primitives in @spaarke/ui-components.
 */
import * as React from 'react';
import { CONTROL_VERSION } from './version';

export interface IMatterHeaderViewProps {
  recordId: string;
}

export const MatterHeaderView: React.FC<IMatterHeaderViewProps> = ({ recordId }) => {
  // TODO(task 022): replace stub with RecordHeaderShell + FieldGrid composition per FR-12.
  return (
    <div>
      <div>Matter Header (placeholder — task 022)</div>
      <div>recordId: {recordId || '(none)'}</div>
      <div style={{ fontSize: 10, opacity: 0.6, marginTop: 8 }}>v{CONTROL_VERSION}</div>
    </div>
  );
};
