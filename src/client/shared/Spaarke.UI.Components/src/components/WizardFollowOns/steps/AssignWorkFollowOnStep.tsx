/**
 * AssignWorkFollowOnStep.tsx
 * Follow-on step for creating a Work Assignment linked to the just-created
 * record, shared across all Spaarke create wizards.
 *
 * Generalized into the WizardFollowOns module from
 * CreateRecordWizard/steps/AssignWorkFollowOnStep (the canonical pure-form
 * implementation). It is a controlled form only — the `sprk_workassignment`
 * record is created by the wizard's `onFinish` after the parent record exists
 * and its GUID + regarding are available (via `applyResolverFields`).
 *
 * Fields:
 *   - Name (required, free text)
 *   - Description (optional, multi-line)
 *   - Matter Type (optional, lookup — auto-filled from parent by the wizard)
 *   - Practice Area (optional, lookup — auto-filled from parent by the wizard)
 *   - Priority (option set: Low / Normal / High / Critical; default Normal)
 *   - Response Due Date (optional, date picker)
 *   - Assigned Attorney / Paralegal (contact lookups, active contacts only)
 *   - Assigned Outside Counsel (organization lookup)
 *
 * Every lookup uses the Dataverse side pane (`DataverseLookupField`) when the
 * host supplies a `navigationService` (owner D-219, 2026-11-12); the
 * `onSearch*` callbacks are the inline fallback when it is absent.
 *
 * Constraints: Fluent v9 only, semantic tokens (ADR-021), no domain imports
 * (ADR-012).
 */
import * as React from 'react';
import { Text, Input, Textarea, Select, Field, Button, Spinner, makeStyles, tokens } from '@fluentui/react-components';
import { PersonRegular } from '@fluentui/react-icons';
import { DataverseLookupField } from '../../LookupField/DataverseLookupField';
import { ACTIVE_CONTACT_LOOKUP_FILTERS } from '../../LookupField/lookupFilters';
import type { ILookupItem } from '../../../types/LookupTypes';
import type { INavigationService } from '../../../types/serviceInterfaces';

// ---------------------------------------------------------------------------
// Priority option set values (sprk_priority on sprk_workassignment)
// ---------------------------------------------------------------------------

export const WORK_ASSIGNMENT_PRIORITY = {
  Low: 100000000,
  Normal: 100000001,
  High: 100000002,
  Critical: 100000003,
} as const;

export type WorkAssignmentPriorityValue = (typeof WORK_ASSIGNMENT_PRIORITY)[keyof typeof WORK_ASSIGNMENT_PRIORITY];

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface IAssignWorkFollowOnStepProps {
  // -- Core fields --
  /** Name of the work assignment (required). */
  nameValue: string;
  onNameChange: (value: string) => void;
  /** Description (optional, multi-line). */
  descriptionValue: string;
  onDescriptionChange: (value: string) => void;

  // -- Classification lookups (auto-filled from parent) --
  /** Matter Type lookup. Auto-filled from the parent matter/project form. */
  matterTypeValue: ILookupItem | null;
  onMatterTypeChange: (item: ILookupItem | null) => void;
  onSearchMatterTypes: (query: string) => Promise<ILookupItem[]>;

  /** Practice Area lookup. Auto-filled from the parent matter/project form. */
  practiceAreaValue: ILookupItem | null;
  onPracticeAreaChange: (item: ILookupItem | null) => void;
  onSearchPracticeAreas: (query: string) => Promise<ILookupItem[]>;

  // -- Scheduling --
  /** Priority option set value. Defaults to Normal (100000001). */
  priorityValue: WorkAssignmentPriorityValue;
  onPriorityChange: (value: WorkAssignmentPriorityValue) => void;

  /** Response Due Date (ISO date string, e.g. "2026-04-15"). */
  responseDueDateValue: string;
  onResponseDueDateChange: (value: string) => void;

  // -- Resource lookups --
  /** Assigned Attorney (contact lookup). */
  attorneyValue: ILookupItem | null;
  onAttorneyChange: (item: ILookupItem | null) => void;
  onSearchAttorneys: (query: string) => Promise<ILookupItem[]>;
  /**
   * Optional "Assign to me" resolver for Assigned Attorney
   * (spaarkeai-assistant-enhancements-r1 task 014 / FR-A4). When supplied,
   * renders an "Assign to me" button beside the field; the resolved item
   * (or `null` on failure) is applied via `onAttorneyChange`. Omitting this
   * prop hides the button entirely (fully backward compatible).
   */
  onAssignAttorneyToMe?: () => Promise<ILookupItem | null>;

  /** Assigned Paralegal (contact lookup). */
  paralegalValue: ILookupItem | null;
  onParalegalChange: (item: ILookupItem | null) => void;
  onSearchParalegals: (query: string) => Promise<ILookupItem[]>;

  /** Assigned Outside Counsel (organization lookup). */
  outsideCounselValue: ILookupItem | null;
  onOutsideCounselChange: (item: ILookupItem | null) => void;
  onSearchOutsideCounsel: (query: string) => Promise<ILookupItem[]>;

  /**
   * Host navigation service. When supplied, every lookup on this step opens the
   * Dataverse side pane; when omitted the step renders the inline search fields
   * (the `onSearch*` callbacks) exactly as before.
   */
  navigationService?: INavigationService;
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
  twoColumn: {
    display: 'grid',
    gridTemplateColumns: '1fr 1fr',
    gap: tokens.spacingHorizontalM,
  },
  fullWidth: {
    width: '100%',
  },
  assigneeRow: {
    display: 'flex',
    alignItems: 'flex-end',
    gap: tokens.spacingHorizontalS,
  },
  assigneeLookup: {
    flexGrow: 1,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const AssignWorkFollowOnStep: React.FC<IAssignWorkFollowOnStepProps> = ({
  nameValue,
  onNameChange,
  descriptionValue,
  onDescriptionChange,
  matterTypeValue,
  onMatterTypeChange,
  onSearchMatterTypes,
  practiceAreaValue,
  onPracticeAreaChange,
  onSearchPracticeAreas,
  priorityValue,
  onPriorityChange,
  responseDueDateValue,
  onResponseDueDateChange,
  attorneyValue,
  onAttorneyChange,
  onSearchAttorneys,
  onAssignAttorneyToMe,
  paralegalValue,
  onParalegalChange,
  onSearchParalegals,
  outsideCounselValue,
  onOutsideCounselChange,
  onSearchOutsideCounsel,
  navigationService,
}) => {
  const styles = useStyles();
  const [isAssigningToMe, setIsAssigningToMe] = React.useState(false);

  const handleAssignToMe = React.useCallback(async () => {
    if (!onAssignAttorneyToMe) return;
    setIsAssigningToMe(true);
    try {
      const assignee = await onAssignAttorneyToMe();
      if (assignee) {
        onAttorneyChange(assignee);
      }
    } finally {
      setIsAssigningToMe(false);
    }
  }, [onAssignAttorneyToMe, onAttorneyChange]);

  return (
    <div className={styles.root}>
      {/* Header */}
      <div className={styles.headerText}>
        <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
          Assign Work
        </Text>
        <Text size={200} className={styles.stepSubtitle}>
          Create a work assignment linked to this record. All fields except Name are optional.
        </Text>
      </div>

      {/* Details section */}
      <div className={styles.section}>
        <Text size={400} weight="semibold" className={styles.sectionTitle}>
          Details
        </Text>
        <div className={styles.sectionFields}>
          <Field label="Name" required>
            <Input
              value={nameValue}
              onChange={(_e, data) => onNameChange(data.value)}
              placeholder="Enter work assignment name..."
              className={styles.fullWidth}
            />
          </Field>
          <Field label="Description">
            <Textarea
              value={descriptionValue}
              onChange={(_e, data) => onDescriptionChange(data.value)}
              placeholder="Describe the work to be done..."
              rows={3}
              className={styles.fullWidth}
            />
          </Field>
        </div>
      </div>

      {/* Classification section */}
      <div className={styles.section}>
        <Text size={400} weight="semibold" className={styles.sectionTitle}>
          Classification
        </Text>
        <div className={styles.sectionFields}>
          <DataverseLookupField
            label="Matter Type"
            entityType="sprk_mattertype_ref"
            placeholder="Search matter types..."
            value={matterTypeValue}
            onChange={onMatterTypeChange}
            navigationService={navigationService}
            onSearch={onSearchMatterTypes}
            minSearchLength={1}
          />
          <DataverseLookupField
            label="Practice Area"
            entityType="sprk_practicearea_ref"
            placeholder="Search practice areas..."
            value={practiceAreaValue}
            onChange={onPracticeAreaChange}
            navigationService={navigationService}
            onSearch={onSearchPracticeAreas}
            minSearchLength={1}
          />
        </div>
      </div>

      {/* Scheduling section */}
      <div className={styles.section}>
        <Text size={400} weight="semibold" className={styles.sectionTitle}>
          Scheduling
        </Text>
        <div className={styles.sectionFields}>
          <div className={styles.twoColumn}>
            <Field label="Priority">
              <Select
                value={String(priorityValue)}
                onChange={(_e, data) => onPriorityChange(Number(data.value) as WorkAssignmentPriorityValue)}
                className={styles.fullWidth}
              >
                <option value={String(WORK_ASSIGNMENT_PRIORITY.Low)}>Low</option>
                <option value={String(WORK_ASSIGNMENT_PRIORITY.Normal)}>Normal</option>
                <option value={String(WORK_ASSIGNMENT_PRIORITY.High)}>High</option>
                <option value={String(WORK_ASSIGNMENT_PRIORITY.Critical)}>Critical</option>
              </Select>
            </Field>
            <Field label="Response Due Date">
              <Input
                type="date"
                value={responseDueDateValue}
                onChange={(_e, data) => onResponseDueDateChange(data.value)}
                className={styles.fullWidth}
              />
            </Field>
          </div>
        </div>
      </div>

      {/* Resources section */}
      <div className={styles.section}>
        <Text size={400} weight="semibold" className={styles.sectionTitle}>
          Resources
        </Text>
        <div className={styles.sectionFields}>
          <div className={styles.assigneeRow}>
            <div className={styles.assigneeLookup}>
              <DataverseLookupField
                label="Assigned Attorney"
                entityType="contact"
                placeholder="Search contacts..."
                value={attorneyValue}
                onChange={onAttorneyChange}
                navigationService={navigationService}
                onSearch={onSearchAttorneys}
                filters={ACTIVE_CONTACT_LOOKUP_FILTERS}
                minSearchLength={2}
              />
            </div>
            {onAssignAttorneyToMe && (
              <Button
                appearance="secondary"
                icon={isAssigningToMe ? <Spinner size="tiny" /> : <PersonRegular />}
                onClick={handleAssignToMe}
                disabled={isAssigningToMe}
                aria-label="Assign attorney to me"
              >
                Assign to me
              </Button>
            )}
          </div>
          <DataverseLookupField
            label="Assigned Paralegal"
            entityType="contact"
            placeholder="Search contacts..."
            value={paralegalValue}
            onChange={onParalegalChange}
            navigationService={navigationService}
            onSearch={onSearchParalegals}
            filters={ACTIVE_CONTACT_LOOKUP_FILTERS}
            minSearchLength={2}
          />
          <DataverseLookupField
            label="Assigned Outside Counsel"
            entityType="sprk_organization"
            placeholder="Search organizations..."
            value={outsideCounselValue}
            onChange={onOutsideCounselChange}
            navigationService={navigationService}
            onSearch={onSearchOutsideCounsel}
            minSearchLength={2}
          />
        </div>
      </div>
    </div>
  );
};

AssignWorkFollowOnStep.displayName = 'AssignWorkFollowOnStep';
