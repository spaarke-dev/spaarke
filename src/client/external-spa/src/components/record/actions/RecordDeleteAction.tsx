/**
 * RecordDeleteAction — the record template's one toolbar `delete`, dispatched to the host of the record's type
 * (spaarke-SPA-external-access-platform-r4 task 015).
 *
 * `delete` used to be the Service Request's "Delete request" alone. An Event record has its own "Delete event" (the creator
 * only), so the action id now resolves to this dispatcher, which mounts the host of the record's entity and nothing else; the
 * record modal stays free of entity branches (FR-01). A record type with no delete host mounts none: the action stays hidden.
 */
import * as React from 'react';
import type { RecordActionHostContext } from '../recordTemplate.types';
import { EventDeleteActionHost } from './EventDeleteAction';
import { ServiceRequestDeleteActionHost } from './ServiceRequestDeleteAction';

const DELETE_HOSTS: Readonly<Record<string, React.FC<RecordActionHostContext>>> = {
  sprk_servicerequest: ServiceRequestDeleteActionHost,
  sprk_event: EventDeleteActionHost,
};

export const RecordDeleteActionHost: React.FC<RecordActionHostContext> = props => {
  const Host = Object.prototype.hasOwnProperty.call(DELETE_HOSTS, props.config.entity)
    ? DELETE_HOSTS[props.config.entity]
    : undefined;
  return Host ? <Host {...props} /> : null;
};
