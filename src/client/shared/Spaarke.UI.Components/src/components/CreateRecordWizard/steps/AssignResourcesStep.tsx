/**
 * AssignResourcesStep.tsx
 * Follow-on step for assigning internal and external resources.
 *
 * Moved from LegalWorkspace's CreateMatter to the shared library since this
 * step is entity-agnostic — it uses lookup callbacks provided by the parent.
 *
 * @see CreateRecordWizard — wires search callbacks and form state
 */
import * as React from 'react';
import {
  Text,
  Checkbox,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { LookupField } from '../../LookupField/LookupField';
import type { ILookupItem } from '../../../types/LookupTypes';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IAssignResourcesStepProps {
  attorneyValue: ILookupItem | null;
  onAttorneyChange: (item: ILookupItem | null) => void;
  onSearchAttorneys: (query: string) => Promise<ILookupItem[]>;

  paralegalValue: ILookupItem | null;
  onParalegalChange: (item: ILookupItem | null) => void;
  onSearchParalegals: (query: string) => Promise<ILookupItem[]>;

  outsideCounselValue: ILookupItem | null;
  onOutsideCounselChange: (item: ILookupItem | null) => void;
  onSearchOutsideCounsel: (query: string) => Promise<ILookupItem[]>;

  notifyResources: boolean;
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
    paddingLeft: '28px',
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const AssignResourcesStep: React.FC<IAssignResourcesStepProps> = ({
  attorneyValue,
  onAttorneyChange,
  onSearchAttorneys,
  paralegalValue,
  onParalegalChange,
  onSearchParalegals,
  outsideCounselValue,
  onOutsideCounselChange,
  onSearchOutsideCounsel,
  notifyResources,
  onNotifyChange,
}) => {
  const styles = useStyles();

  return (
    <div className={styles.root}>
      <div className={styles.headerText}>
        <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
          Assign Resources
        </Text>
        <Text size={200} className={styles.stepSubtitle}>
          Search and assign internal and external resources. All fields are optional.
        </Text>
      </div>

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

            minSearchLength={2}
          />
          <LookupField
            label="Assigned Paralegal"
            placeholder="Search contacts..."
            value={paralegalValue}
            onChange={onParalegalChange}
            onSearch={onSearchParalegals}

            minSearchLength={2}
          />
        </div>
      </div>

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

            minSearchLength={2}
          />
        </div>
      </div>

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
