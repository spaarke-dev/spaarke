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
  // Task 062: the monitored-records subscription count (sprk_monitor); its flagged-entity read failed.
  monitored: 'Monitored records',
};

export function describeFailedSections(failedChannels: string[]): string[] {
  return failedChannels.map(code => FAILED_CHANNEL_LABELS[code] ?? code);
}
