/**
 * The record template's action hosts, by toolbar action id (spaarke-SPA-external-access-platform-r4 task 005, FR-05).
 *
 * Most toolbar actions are a click handler the modal owns (Refresh, Copy link). An action whose flow has its own dialogs
 * or notices has a host here: `RecordTemplateModal` mounts the host for every action id the record's config lists, in
 * the record's content, and shows the action only while the host offers it (hide, don't disable). The modal itself has no
 * entity-specific branch.
 *
 * `delete` is the Service Request's "Delete request": the only toolbar Delete in the FR-01 table. A future record-level
 * Delete for another type needs its own host (the requester check here never matches another entity's row).
 */
import type * as React from 'react';
import type { RecordActionHostContext, RecordToolbarActionId } from '../recordTemplate.types';
import { DocumentDownloadAction } from './DocumentDownloadAction';
import { ServiceRequestDeleteActionHost } from './ServiceRequestDeleteAction';
import { SubmitInvoiceAction } from './SubmitInvoiceAction';

export const RECORD_ACTION_HOSTS: Readonly<Partial<Record<RecordToolbarActionId, React.FC<RecordActionHostContext>>>> =
  {
    download: DocumentDownloadAction,
    delete: ServiceRequestDeleteActionHost,
    // Task 007 (FR-10): Matter, Project and Work assignment list it; the host decides when it is offered.
    submitInvoice: SubmitInvoiceAction,
  };
