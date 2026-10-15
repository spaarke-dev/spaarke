/**
 * intakeSuccess - the success screen both intake wizards return from `onFinish` (r3 task 052).
 *
 * Buttons (design note sections 1.4 and 2.4): "Open request" (primary; the new request's detail view, where Documents
 * live) and "Done"; plus "Service Requests" to go to the list tab (the request appears there). "Open request" is offered
 * only when the server returned a GUID, and goes through the shared record opener (`useOpenRecord`), so it is denied the
 * same way any other record open is.
 */
import * as React from 'react';
import { Button } from '@fluentui/react-components';
import type { IWizardSuccessConfig } from '@spaarke/ui-components/components/Wizard/wizardShellTypes';
import { SuccessBody, SuccessIcon } from './intakeFormParts';
import type { OpenRecord } from '../components/detail/RecordOpener';
import { isGuid } from '../components/detail/record-targets';

export interface IntakeSuccessInput {
  /** The success screen's heading. */
  heading: string;
  /** Body paragraphs, in order. */
  paragraphs: React.ReactNode[];
  /** The created request's id (`sprk_servicerequestid`). */
  requestId: string;
  onClose: () => void;
  /** Opens the Service Requests tab; omitted = no such button. */
  onOpenServiceRequests?: () => void;
  openRecord: OpenRecord;
}

export function intakeSuccessConfig(input: IntakeSuccessInput): IWizardSuccessConfig {
  const { onClose, onOpenServiceRequests, openRecord, requestId } = input;
  return {
    icon: <SuccessIcon />,
    title: input.heading,
    body: (
      <SuccessBody>
        {input.paragraphs.map((p, i) => (
          <div key={i}>{p}</div>
        ))}
      </SuccessBody>
    ),
    actions: (
      <>
        <Button appearance="secondary" onClick={onClose}>
          Done
        </Button>
        {onOpenServiceRequests && (
          <Button
            appearance="secondary"
            onClick={() => {
              onClose();
              onOpenServiceRequests();
            }}
          >
            Service Requests
          </Button>
        )}
        {isGuid(requestId) && (
          <Button
            appearance="primary"
            onClick={() => {
              onClose();
              openRecord({ entityName: 'sprk_servicerequest', recordId: requestId.toLowerCase() });
            }}
          >
            Open request
          </Button>
        )}
      </>
    ),
  };
}
