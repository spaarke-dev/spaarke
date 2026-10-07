/**
 * unified-access-control-r2 task 152 — human labels for the briefing sections whose read as the caller failed.
 * Channel codes match the DailyBriefingCollector constants; unknown codes fall through verbatim.
 */
const FAILED_CHANNEL_LABELS: Record<string, string> = {
  'upcoming-tasks': 'Upcoming tasks',
  'overdue-tasks': 'Overdue tasks',
  documents: 'Documents',
  matters: 'Matters',
  projects: 'Projects',
  'to-dos': 'To dos',
};

const FAILED_ENTITY_LABELS: Record<string, string> = {
  sprk_matter: 'matters',
  sprk_project: 'projects',
  sprk_invoice: 'invoices',
  sprk_document: 'documents',
  sprk_workassignment: 'work assignments',
  sprk_event: 'tasks',
  sprk_todo: 'to dos',
};

export function describeFailedSections(failedChannels: string[], highPriorityFailedEntityTypes: string[]): string[] {
  const labels = failedChannels.map(code => FAILED_CHANNEL_LABELS[code] ?? code);
  if (highPriorityFailedEntityTypes.length > 0) {
    const kinds = highPriorityFailedEntityTypes.map(e => FAILED_ENTITY_LABELS[e] ?? e);
    labels.push(`High priority (${kinds.join(', ')})`);
  }
  return labels;
}
