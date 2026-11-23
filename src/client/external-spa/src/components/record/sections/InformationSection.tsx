/**
 * Information — the record's read-only fields (R3 `RecordDetailFields`, moved unchanged into the template; task 005).
 * The fields are the entity's read description (`RECORD_DETAIL_ENTITIES`): the module allow-list, no new column.
 *
 * A field that points at another record (an event's Regarding, task 015) is a link-style button that opens that record as a
 * child over this one, through the surrounding record's opener (`useChildRecordOpener`). Inside a child record there is no
 * opener, so the same field is plain text: one child level only (the one-level rule, task 013).
 */
import * as React from 'react';
import { RecordDetailFields, type DetailFieldLink } from '../../detail/RecordDetailFields';
import { useChildRecordOpener } from '../childRecord';
import type { RecordSectionContext } from '../recordTemplate.types';
import { RecordSectionCard } from './RecordSectionCard';

export const InformationSection: React.FC<RecordSectionContext> = ({ detail, record }) => {
  const opener = useChildRecordOpener();
  const handleOpenLink = React.useMemo(
    () => (opener ? (link: DetailFieldLink, trigger: HTMLElement) => opener(link.entity, link.id, trigger) : undefined),
    [opener]
  );
  return (
    <RecordSectionCard id="information">
      <RecordDetailFields cfg={detail} record={record} onOpenLink={handleOpenLink} />
    </RecordSectionCard>
  );
};
