/**
 * AssignWorkFollowOnStep.tsx
 * Follow-on step for creating a Work Assignment linked to the parent entity.
 *
 * Replaces the old "Assign Resources" step. Collects all fields needed to
 * create a sprk_workassignment Dataverse record, linked to the parent matter
 * or project via N:1 relationship.
 *
 * Fields:
 *   - Name (required, free text)
 *   - Description (optional, multi-line)
 *   - Matter Type (optional, lookup — auto-filled from parent matter)
 *   - Practice Area (optional, lookup — auto-filled from parent record)
 *   - Priority (option set: Low / Normal / High / Critical; defaults to Normal)
 *   - Response Due Date (optional, date picker)
 *   - Assigned Attorney (optional, contact lookup)
 *   - Assigned Paralegal (optional, contact lookup)
 *   - Assigned Outside Counsel (optional, organization lookup)
 *
 * Constraints:
 *   - Fluent v9 only — ZERO hard-coded colors
 *   - makeStyles with semantic tokens throughout
 *   - ADR-021: dark mode support via colorNeutral/colorBrand tokens
 *   - ADR-012: shared library component, no solution-specific imports
 */
import * as React from 'react';
import {
  Text,
  Input,
  Textarea,
  Select,
  Field,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { LookupField } from '../../LookupField/LookupField';
import type { ILookupItem } from '../../../types/LookupTypes';

// ---------------------------------------------------------------------------
// Priority option set values (sprk_priority on sprk_workassignment)
// ---------------------------------------------------------------------------

export const WORK_ASSIGNMENT_PRIORITY = {
  Low: 100000000,
  Normal: 100000001,
  High: 100000002,
  Critical: 100000003,
} as const;

export type WorkAssignmentPriorityValue =
  (typeof WORK_ASSIGNMENT_PRIORITY)[keyof typeof WORK_ASSIGNMENT_PRIORITY];

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

  /** Assigned Paralegal (contact lookup). */
  paralegalValue: ILookupItem | null;
  onParalegalChange: (item: ILookupItem | null) => void;
  onSearchParalegals: (query: string) => Promise<ILookupItem[]>;

  /** Assigned Outside Counsel (organization lookup). */
  outsideCounselValue: ILookupItem | null;
  onOutsideCounselChange: (item: ILookupItem | null) => void;
  onSearchOutsideCounsel: (query: string) => Promise<ILookupItem[]>;
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
});

// ---------------------------------------------------------------------------
// AssignWorkStep (exported)
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
  paralegalValue,
  onParalegalChange,
  onSearchParalegals,
  outsideCounselValue,
  onOutsideCounselChange,
  onSearchOutsideCounsel,
}) => {
  const styles = useStyles();

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
          <LookupField
            label="Matter Type"
            placeholder="Search matter types..."
            value={matterTypeValue}
            onChange={onMatterTypeChange}
            onSearch={onSearchMatterTypes}
            minSearchLength={1}
          />
          <LookupField
            label="Practice Area"
            placeholder="Search practice areas..."
            value={practiceAreaValue}
            onChange={onPracticeAreaChange}
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
                onChange={(_e, data) =>
                  onPriorityChange(Number(data.value) as WorkAssignmentPriorityValue)
                }
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
    </div>
  );
};
