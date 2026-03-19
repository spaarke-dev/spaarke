/**
 * AssignResourcesStep.tsx
 * Follow-on step for "Assign Resources" in the Create New Matter wizard.
 *
 * Uses LookupField for each assignment. Values are lifted to WizardDialog
 * form state (AI pre-fill populates these from CreateRecordStep).
 *
 * Constraints:
 *   - Fluent v9: Text, Checkbox -- ZERO hardcoded colors
 *   - makeStyles with semantic tokens
 */

import * as React from 'react';
import {
  Text,
  Checkbox,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { LookupField } from './LookupField';
import type { ILookupItem } from '../../types/LookupTypes';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IAssignResourcesStepProps {
  /** Assigned Attorney lookup value. */
  attorneyValue: ILookupItem | null;
  /** Called when attorney changes. */
  onAttorneyChange: (item: ILookupItem | null) => void;
  /** Search function for attorney (contacts). */
  onSearchAttorneys: (query: string) => Promise<ILookupItem[]>;
  /** Whether attorney was AI pre-filled. */
  isAttorneyAiPrefilled?: boolean;

  /** Assigned Paralegal lookup value. */
  paralegalValue: ILookupItem | null;
  /** Called when paralegal changes. */
  onParalegalChange: (item: ILookupItem | null) => void;
  /** Search function for paralegal (contacts). */
  onSearchParalegals: (query: string) => Promise<ILookupItem[]>;
  /** Whether paralegal was AI pre-filled. */
  isParalegalAiPrefilled?: boolean;

  /** Assigned Outside Counsel lookup value. */
  outsideCounselValue: ILookupItem | null;
  /** Called when outside counsel changes. */
  onOutsideCounselChange: (item: ILookupItem | null) => void;
  /** Search function for outside counsel (organizations). */
  onSearchOutsideCounsel: (query: string) => Promise<ILookupItem[]>;
  /** Whether outside counsel was AI pre-filled. */
  isOutsideCounselAiPrefilled?: boolean;

  /** Whether "Notify assigned resources" is checked. */
  notifyResources: boolean;
  /** Called when notify toggle changes. */
  onNotifyChange: (checked: boolean) => void;
}

// ---------------------------------------------------------------------------
// Styles
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalL,
  },
  headerText: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  stepTitle: {
    color: tokens.colorNeutralForeground1,
  },
  stepSubtitle: {
    color: tokens.colorNeutralForeground3,
  },
  section: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  sectionTitle: {
    color: tokens.colorNeutralForeground1,
    borderBottomWidth: '1px',
    borderBottomStyle: 'solid',
    borderBottomColor: tokens.colorNeutralStroke2,
    paddingBottom: tokens.spacingVerticalXS,
  },
  sectionFields: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalS,
  },
  notifySection: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  notifyHint: {
    color: tokens.colorNeutralForeground4,
    paddingLeft: '28px', // align with checkbox label
  },
});

// ---------------------------------------------------------------------------
// AssignResourcesStep (exported)
// ---------------------------------------------------------------------------

export const AssignResourcesStep: React.FC<IAssignResourcesStepProps> = ({
  attorneyValue,
  onAttorneyChange,
  onSearchAttorneys,
  isAttorneyAiPrefilled,
  paralegalValue,
  onParalegalChange,
  onSearchParalegals,
  isParalegalAiPrefilled,
  outsideCounselValue,
  onOutsideCounselChange,
  onSearchOutsideCounsel,
  isOutsideCounselAiPrefilled,
  notifyResources,
  onNotifyChange,
}) => {
  const styles = useStyles();

  return (
    <div className={styles.root}>
      {/* Header */}
      <div className={styles.headerText}>
        <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
          Assign Resources
        </Text>
        <Text size={200} className={styles.stepSubtitle}>
          Search and assign internal and external resources to this matter.
          All fields are optional.
        </Text>
      </div>

      {/* Internal Resources */}
      <div className={styles.section}>
        <Text size={400} weight="semibold" className={styles.sectionTitle}>
          Internal Resources
        </Text>
        <div className={styles.sectionFields}>
          <LookupField
            label="Assigned Attorney"
            placeholder="Search contacts..."
            value={attorneyValue}
            onChange={onAttorneyChange}
            onSearch={onSearchAttorneys}
            isAiPrefilled={isAttorneyAiPrefilled}
            minSearchLength={2}
          />
          <LookupField
            label="Assigned Paralegal"
            placeholder="Search contacts..."
            value={paralegalValue}
            onChange={onParalegalChange}
            onSearch={onSearchParalegals}
            isAiPrefilled={isParalegalAiPrefilled}
            minSearchLength={2}
          />
        </div>
      </div>

      {/* External Resources */}
      <div className={styles.section}>
        <Text size={400} weight="semibold" className={styles.sectionTitle}>
          External Resources
        </Text>
        <div className={styles.sectionFields}>
          <LookupField
            label="Assigned Outside Counsel"
            placeholder="Search organizations..."
            value={outsideCounselValue}
            onChange={onOutsideCounselChange}
            onSearch={onSearchOutsideCounsel}
            isAiPrefilled={isOutsideCounselAiPrefilled}
            minSearchLength={2}
          />
        </div>
      </div>

      {/* Notifications */}
      <div className={styles.section}>
        <Text size={400} weight="semibold" className={styles.sectionTitle}>
          Notifications
        </Text>
        <div className={styles.notifySection}>
          <Checkbox
            checked={notifyResources}
            onChange={(_e, data) => onNotifyChange(!!data.checked)}
            label="Notify assigned resources"
          />
          <Text size={100} className={styles.notifyHint}>
            Notifications will be sent when this feature is available.
          </Text>
        </div>
      </div>
    </div>
  );
};
