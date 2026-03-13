/**
 * EventWizardDialog.tsx
 * Thin wrapper around CreateRecordWizard for "Create New Event".
 *
 * Default export enables React.lazy() dynamic import.
 */
import * as React from 'react';
import { Button, Text, tokens } from '@fluentui/react-components';
import { CheckmarkCircleFilled } from '@fluentui/react-icons';

import {
  CreateRecordWizard,
  type ICreateRecordWizardConfig,
  type IFinishContext,
} from '../../../../../client/shared/Spaarke.UI.Components/src/components/CreateRecordWizard';

import type { IWizardSuccessConfig } from '../../../../../client/shared/Spaarke.UI.Components/src/components/Wizard/wizardShellTypes';

import { CreateEventStep } from './CreateEventStep';
import { EventService } from './eventService';
import { EMPTY_EVENT_FORM } from './formTypes';
import type { ICreateEventFormState } from './formTypes';

import {
  searchContactsAsLookup,
  searchOrganizationsAsLookup,
  searchUsersAsLookup,
} from '../CreateMatter/matterService';

import { getSpeContainerIdFromBusinessUnit } from '../../services/xrmProvider';
import { navigateToEntity } from '../../utils/navigation';
import type { IWebApi } from '../../types/xrm';

// ---------------------------------------------------------------------------
// Props
// ---------------------------------------------------------------------------

interface IEventWizardDialogProps {
  open: boolean;
  onClose: () => void;
  webApi: IWebApi;
}

// ---------------------------------------------------------------------------
// EventWizardDialog
// ---------------------------------------------------------------------------

const EventWizardDialog: React.FC<IEventWizardDialogProps> = ({ open, onClose, webApi }) => {
  const [formValid, setFormValid] = React.useState(false);
  const [formValues, setFormValues] = React.useState<ICreateEventFormState>(EMPTY_EVENT_FORM);
  const formValuesRef = React.useRef(formValues);
  formValuesRef.current = formValues;

  React.useEffect(() => {
    if (open) {
      setFormValid(false);
      setFormValues(EMPTY_EVENT_FORM);
    }
  }, [open]);

  const handleSearchContacts = React.useCallback(
    (query: string) => searchContactsAsLookup(webApi, query),
    [webApi]
  );
  const handleSearchOrganizations = React.useCallback(
    (query: string) => searchOrganizationsAsLookup(webApi, query),
    [webApi]
  );
  const handleSearchUsers = React.useCallback(
    (query: string) => searchUsersAsLookup(webApi, query),
    [webApi]
  );

  const config: ICreateRecordWizardConfig = React.useMemo(
    () => ({
      title: 'Create New Event',
      entityLabel: 'event',
      filesStepSubtitle:
        'Upload documents to associate with this event, or click Next to skip.',
      finishingLabel: 'Creating event\u2026',

      infoStep: {
        id: 'create-record',
        label: 'Event Details',
        canAdvance: () => formValid,
        renderContent: (_wizardFiles) => (
          <CreateEventStep
            webApi={webApi}
            onValidChange={setFormValid}
            onFormValues={setFormValues}
            initialFormValues={formValues}
          />
        ),
      },

      searchContacts: handleSearchContacts,
      searchOrganizations: handleSearchOrganizations,
      searchUsers: handleSearchUsers,

      resolveSpeContainerId: () => getSpeContainerIdFromBusinessUnit(webApi),

      buildEmailSubject: (entityName: string) => `New Event: ${entityName}`,
      buildEmailBody: (fields: Record<string, string>) =>
        `Dear Colleague,\n\nA new event, "${fields.eventName || ''}", has been created.\n\n` +
        `Please review and take any necessary action.\n\n` +
        `Kind regards,\n[Your Name]`,
      getEntityName: () => formValuesRef.current.eventName,
      getFormFields: () => ({
        eventName: formValuesRef.current.eventName,
        eventTypeName: formValuesRef.current.eventTypeName,
      }),

      onFinish: async (_context: IFinishContext): Promise<IWizardSuccessConfig> => {
        const currentFormValues = formValuesRef.current;

        const eventService = new EventService(webApi);
        const result = await eventService.createEvent(currentFormValues);
        if (!result.success) {
          throw new Error(result.errorMessage ?? 'Failed to create event');
        }

        const eventId = result.eventId!;
        const eventName = result.eventName!;

        const viewEvent = () => {
          navigateToEntity({
            action: 'openRecord',
            entityName: 'sprk_event',
            entityId: eventId,
          });
          onClose();
        };

        return {
          icon: (
            <CheckmarkCircleFilled
              fontSize={64}
              style={{ color: tokens.colorPaletteGreenForeground1 }}
            />
          ),
          title: 'Event created!',
          body: (
            <Text size={300} style={{ color: tokens.colorNeutralForeground2 }}>
              <span style={{ color: tokens.colorBrandForeground1, fontWeight: 600 }}>
                &ldquo;{eventName}&rdquo;
              </span>{' '}
              has been created and is ready to use.
            </Text>
          ),
          actions: (
            <>
              <Button
                appearance="primary"
                onClick={viewEvent}
                aria-label={`View event: ${eventName}`}
              >
                View Event
              </Button>
              <Button appearance="secondary" onClick={onClose}>
                Close
              </Button>
            </>
          ),
          warnings: [],
        };
      },
    }),
    [formValid, formValues, webApi, handleSearchContacts, handleSearchOrganizations, handleSearchUsers, onClose]
  );

  return (
    <CreateRecordWizard
      open={open}
      onClose={onClose}
      webApi={webApi}
      config={config}
    />
  );
};

export default EventWizardDialog;
