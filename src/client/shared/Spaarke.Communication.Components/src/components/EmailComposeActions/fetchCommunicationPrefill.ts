/**
 * fetchCommunicationPrefill.ts
 *
 * Pure read of the `sprk_communication` fields the task-022
 * `deriveComposerFields` needs to derive Reply/Reply All/Forward pre-fill.
 * Mirrors the EXACT `$select` the production `CommunicationActionsApp`
 * (`src/client/pcf/CommunicationActions/CommunicationActions/CommunicationActionsApp.tsx`)
 * uses for its own prefill read — same fields, same shape — so reading-pane
 * Reply/Reply All/Forward behaves identically to the OOB-form action bar.
 *
 * Context-agnostic (ADR-012): takes an injected `IDataService` (the host
 * supplies `createXrmDataService()`), never touches `Xrm.WebApi` directly.
 */
import type { IDataService } from '@spaarke/ui-components';
import type { RecordPrefill } from '../../logic/actions';

const PREFILL_SELECT = 'sprk_from,sprk_to,sprk_cc,sprk_subject,sprk_body,createdon';

/**
 * Retrieves and maps a `sprk_communication` record to `RecordPrefill`. Throws
 * on a read failure — callers (see `useEmailComposeActions`) treat that as
 * best-effort (the composer opens with empty pre-fill and the caller
 * surfaces a soft warning) rather than blocking the compose action.
 */
export async function fetchCommunicationPrefill(
  dataService: IDataService,
  communicationId: string
): Promise<RecordPrefill> {
  const rec = (await dataService.retrieveRecord(
    'sprk_communication',
    communicationId,
    `?$select=${PREFILL_SELECT}`
  )) as Record<string, unknown>;

  return {
    from: (rec.sprk_from as string) ?? '',
    to: (rec.sprk_to as string) ?? '',
    cc: (rec.sprk_cc as string) ?? '',
    subject: (rec.sprk_subject as string) ?? '',
    body: (rec.sprk_body as string) ?? '',
    sent: (rec['createdon@OData.Community.Display.V1.FormattedValue'] as string) ?? '',
  };
}
