/**
 * The record template's action hosts, by toolbar action id (spaarke-SPA-external-access-platform-r4 task 005, FR-05).
 *
 * Most toolbar actions are a click handler the modal owns (Refresh, Copy link). An action whose flow has its own dialogs
 * or notices has a host here: `RecordTemplateModal` mounts the host for every action id the record's config lists, in
 * the record's content, and shows the action only while the host offers it (hide, don't disable). The modal itself has no
 * entity-specific branch.
 *
 * `delete` resolves to a small dispatcher (`RecordDeleteAction`): the Service Request's "Delete request" and the Event's
 * "Delete event" (task 015). `edit` and `changeStatus` are the Event record's own (task 015).
 */
import type * as React from 'react';
import type { RecordActionHostContext, RecordToolbarActionId } from '../recordTemplate.types';
import { DocumentDownloadAction } from './DocumentDownloadAction';
import { EventEditAction } from './EventEditAction';
import { EventStatusAction } from './EventStatusAction';
import { RecordDeleteActionHost } from './RecordDeleteAction';
import { SubmitInvoiceAction } from './SubmitInvoiceAction';

export const RECORD_ACTION_HOSTS: Readonly<Partial<Record<RecordToolbarActionId, React.FC<RecordActionHostContext>>>> =
  {
    download: DocumentDownloadAction,
    delete: RecordDeleteActionHost,
    // Task 007 (FR-10): Matter, Project and Work assignment list it; the host decides when it is offered.
    submitInvoice: SubmitInvoiceAction,
    // Task 015 (FR-36): the Event record's Edit and Change status; each is offered only on the BFF's own per-event answer.
    edit: EventEditAction,
    changeStatus: EventStatusAction,
  };
