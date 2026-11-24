/**
 * tableLabels - the display name of a Dataverse table, for text a person reads where a table is named (the Filing family modal, the
 * ontology rule wizard's "The rule is about" lists). Moved out of RecordFamilyModal (DI-157) so the rule wizard reuses it rather than
 * keeping a second list; any table not listed falls back to its logical name without `sprk_`, title-cased.
 */

/** Display names for the tables a Filing family contains; any other table falls back to its logical name. */
export const TABLE_LABELS: Readonly<Record<string, string>> = {
  // The three roots, and every child table of the Filing family (SecureChildLineage.cs).
  sprk_matter: 'Matter',
  sprk_project: 'Project',
  sprk_workassignment: 'Work Assignment',
  sprk_agreement: 'Agreement',
  sprk_analysis: 'Analysis',
  sprk_analysisoutput: 'Analysis Output',
  sprk_attachmentartifact: 'Attachment Artifact',
  sprk_billingevent: 'Billing Event',
  sprk_budget: 'Budget',
  sprk_budgetbucket: 'Budget Bucket',
  sprk_budgetrevision: 'Budget Revision',
  sprk_communication: 'Communication',
  sprk_communicationattachment: 'Communication Attachment',
  sprk_communicationparticipant: 'Communication Participant',
  sprk_communicationthread: 'Communication Thread',
  sprk_decisionrecord: 'Decision Record',
  sprk_document: 'Document',
  sprk_emailartifact: 'Email Artifact',
  sprk_emailreviewlog: 'Email Review Log',
  sprk_event: 'Event',
  sprk_eventlog: 'Event Log',
  sprk_fileversion: 'File Version',
  sprk_invoice: 'Invoice',
  sprk_kpiassessment: 'KPI Assessment',
  sprk_memo: 'Memo',
  sprk_policy: 'Policy',
  sprk_policyversion: 'Policy Version',
  sprk_reportcard: 'Report Card',
  sprk_signal: 'Signal',
  sprk_spendsignal: 'Spend Signal',
  sprk_spendsnapshot: 'Spend Snapshot',
  sprk_todo: 'To Do',
};

/** A known table's display name; any other table is its logical name without `sprk_`, title-cased. */
export function tableLabel(table: string): string {
  const known = TABLE_LABELS[table];
  if (known) return known;
  return table
    .replace(/^sprk_/, '')
    .split('_')
    .filter(w => w !== '')
    .map(w => w.charAt(0).toUpperCase() + w.slice(1))
    .join(' ');
}
