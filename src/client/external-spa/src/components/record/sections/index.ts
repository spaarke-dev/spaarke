/**
 * The record template's sections, by section id (spaarke-SPA-external-access-platform-r4 task 005, FR-01).
 *
 * `RecordTemplateModal` renders each id a config lists through `renderRecordSection`; it has no entity-specific branch.
 * Adding a type changes only `recordTemplates.ts` (and, for a new section, one entry here).
 */
import * as React from 'react';
import type { RecordSectionContext, RecordSectionId } from '../recordTemplate.types';
import { DocumentsSection } from './DocumentsSection';
import { FilesSection } from './FilesSection';
import { InformationSection } from './InformationSection';
import { InvoicesSection } from './InvoicesSection';
import { MessagesSection } from './MessagesSection';
import { PreviewSection } from './PreviewSection';
import { TasksEventsSection } from './TasksEventsSection';

export const RECORD_SECTIONS: Readonly<Record<RecordSectionId, React.FC<RecordSectionContext>>> = {
  information: InformationSection,
  documents: DocumentsSection,
  invoices: InvoicesSection,
  files: FilesSection,
  preview: PreviewSection,
  messages: MessagesSection,
  tasksEvents: TasksEventsSection,
};

/** Renders one section of the shown record (the modal's default `renderSection`). */
export function renderRecordSection(id: RecordSectionId, context: RecordSectionContext): React.ReactNode {
  return React.createElement(RECORD_SECTIONS[id], context);
}

export { documentFileOf, type DocumentFile } from './PreviewSection';
