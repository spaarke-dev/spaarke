/**
 * CreateEventFollowOnStep.tsx
 * Follow-on step for collecting event details to create a `sprk_event` record
 * linked to the just-created record, shared across all Spaarke create wizards.
 *
 * Generalized into the WizardFollowOns module from
 * CreateRecordWizard/steps/CreateEventFollowOnStep. Reuses the shared
 * `CreateEventStep` form (from CreateEventWizard) — this component only supplies
 * follow-on framing. Like the other follow-on form steps, it collects field
 * values only; the `sprk_event` record is created by the wizard's `onFinish`
 * after the parent record exists (its regarding is seeded via
 * `applyResolverFields`).
 *
 * Constraints: Fluent v9 only, semantic tokens (ADR-021), no domain imports
 * (ADR-012).
 */
import * as React from 'react';
import { Text, makeStyles, tokens } from '@fluentui/react-components';
import { CreateEventStep } from '../../CreateEventWizard/CreateEventStep';
import type { ICreateEventFormState } from '../../CreateEventWizard/formTypes';
import type { IDataService } from '../../../types/serviceInterfaces';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

export interface ICreateEventFollowOnStepProps {
  /**
   * Data service for Dataverse operations. Passed to CreateEventStep /
   * EventService for `sprk_eventtype_ref` lookups.
   */
  dataService: IDataService;
  /** Current event form field values (controlled by the parent wizard). */
  formValues: ICreateEventFormState;
  /** Called whenever any field in the event form changes. */
  onFormValues: (values: ICreateEventFormState) => void;
  /**
   * Called with the current validity state on every form change.
   * True when the minimum required fields (event name) are filled.
   */
  onValidChange: (isValid: boolean) => void;
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
  stepTitle: {
    color: tokens.colorNeutralForeground1,
    marginBottom: tokens.spacingVerticalXS,
  },
  stepSubtitle: {
    color: tokens.colorNeutralForeground3,
    marginBottom: tokens.spacingVerticalM,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

export const CreateEventFollowOnStep: React.FC<ICreateEventFollowOnStepProps> = ({
  dataService,
  formValues,
  onFormValues,
  onValidChange,
}) => {
  const styles = useStyles();

  return (
    <div className={styles.root}>
      <div>
        <Text as="h2" size={500} weight="semibold" className={styles.stepTitle}>
          Create Event
        </Text>
        <Text size={200} className={styles.stepSubtitle}>
          Enter details for the event. It will be created and linked to the record when you click Finish.
        </Text>
      </div>

      <CreateEventStep
        dataService={dataService}
        onValidChange={onValidChange}
        onFormValues={onFormValues}
        initialFormValues={formValues}
      />
    </div>
  );
};

CreateEventFollowOnStep.displayName = 'CreateEventFollowOnStep';
