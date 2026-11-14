/**
 * Information — the record's read-only fields (R3 `RecordDetailFields`, moved unchanged into the template; task 005).
 * The fields are the entity's read description (`RECORD_DETAIL_ENTITIES`): the module allow-list, no new column.
 */
import * as React from 'react';
import { RecordDetailFields } from '../../detail/RecordDetailFields';
import type { RecordSectionContext } from '../recordTemplate.types';
import { RecordSectionCard } from './RecordSectionCard';

export const InformationSection: React.FC<RecordSectionContext> = ({ detail, record }) => (
  <RecordSectionCard id="information">
    <RecordDetailFields cfg={detail} record={record} />
  </RecordSectionCard>
);
