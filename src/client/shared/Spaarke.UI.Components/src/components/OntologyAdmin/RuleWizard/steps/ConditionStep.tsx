/**
 * ConditionStep - step 2 of the rule wizard: what the rule looks for (spaarke-ontology-platform-r1 task 102; D-22, D-149).
 *
 * The condition is built ONLY from the BFF catalog: the rule type, the table the rule is about, each table's authorable columns, the
 * comparison operators, the relative-date forms, the join to each related table and the triage categories are all read from
 * `catalog`. There is no free-text JSON entry; the body below is a read-only preview of what the controls compose.
 *
 * Two kinds of rule are composed from the same controls:
 *   Existence  the subject's own tests (`when`), then clauses: "has related records matching" / "has no related records matching".
 *   Threshold  one measure compared to a number, plus the subject's own tests.
 *
 * The step does not decide whether the condition is acceptable: `validate` does, and its refusals for `ruleType` / `ruleBody` show here.
 *
 * Fluent v9 tokens only (ADR-021).
 */

import * as React from 'react';
import { Button, Input, Select, Text } from '@fluentui/react-components';
import { AddRegular, DeleteRegular } from '@fluentui/react-icons';
import { buildDraftBody, emptyClause, emptyRow, RULE_TYPE_THRESHOLD, subjectOf } from '../ruleDraft';
import type { ClauseDraft, ClauseKind, ConditionDraft, FilterRow } from '../ruleDraft';
import { errorMessage, RefusableField, useStepStyles } from '../stepCommon';
import type { StepBaseProps } from '../stepCommon';
import { FilterRowsEditor } from './FilterRowsEditor';

/** Whether the admin has put anything into a row, so its problem is worth showing before they ask for the summary. */
function isTouched(row: FilterRow): boolean {
  return row.field !== '' || row.value !== '' || row.listText !== '' || row.ids.length > 0;
}

/** The problems to show beside the rows: those of rows the admin has started. The rest are in the summary. */
function visibleIssues(rows: readonly FilterRow[], issues: Record<string, string>): Record<string, string> {
  const shown: Record<string, string> = {};
  for (const row of rows) {
    if (row.id in issues && isTouched(row)) {
      shown[row.id] = issues[row.id];
    }
  }
  return shown;
}

const CLAUSE_KINDS: ReadonlyArray<{ readonly kind: ClauseKind; readonly label: string }> = [
  { kind: 'exists', label: 'Has related records that match' },
  { kind: 'notExists', label: 'Has no related records that match' },
];

export const ConditionStep: React.FC<StepBaseProps> = ({ catalog, draft, onChange, errors, locked }) => {
  const styles = useStepStyles();
  const condition = draft.condition;
  const subject = subjectOf(catalog, condition.subject);
  const isThreshold = condition.ruleType === RULE_TYPE_THRESHOLD;
  const built = buildDraftBody(draft, catalog);
  const summary = built.ruleBody === null ? Object.values(built.issues)[0] : undefined;

  const setCondition = (change: (current: ConditionDraft) => ConditionDraft) =>
    onChange(d => ({ ...d, condition: change(d.condition) }));

  const subjects = isThreshold ? catalog.subjects.filter(s => s.thresholdFields.length > 0) : catalog.subjects;
  const ruleBodyError = errorMessage(errors, 'ruleBody');
  const maxClauses = catalog.limits.maxClauses;

  const setClause = (id: string, change: (clause: ClauseDraft) => ClauseDraft) =>
    setCondition(c => ({ ...c, clauses: c.clauses.map(clause => (clause.id === id ? change(clause) : clause)) }));

  return (
    <div className={styles.step} data-testid="rule-wizard-condition">
      <Text className={styles.intro}>
        Say what the rule looks for. Everything here is chosen from the lists the service offers; the rule is written for you.
      </Text>

      <RefusableField label="Kind of rule" field="ruleType" errors={errors} required>
        <Select
          value={condition.ruleType}
          disabled={locked}
          onChange={(_event, data) => setCondition(c => ({ ...c, ruleType: data.value, subject: '', when: [], clauses: [], thresholdField: '' }))}
          data-testid="rule-wizard-rule-type"
        >
          {catalog.ruleTypes.map(type => (
            <option key={type} value={type}>
              {type}
            </option>
          ))}
        </Select>
      </RefusableField>

      <RefusableField label="The rule is about" field="subject" errors={errors} required>
        <Select
          value={condition.subject}
          disabled={locked}
          onChange={(_event, data) => setCondition(c => ({ ...c, subject: data.value, when: [], clauses: [], thresholdField: '' }))}
          data-testid="rule-wizard-subject"
        >
          <option value="">Choose a table</option>
          {subjects.map(s => (
            <option key={s.entity} value={s.entity}>
              {s.entity}
            </option>
          ))}
        </Select>
      </RefusableField>

      {subject === undefined ? null : (
        <>
          <section className={styles.group} aria-label="Tests on the record itself" data-testid="rule-wizard-when">
            <Text weight="semibold">Tests on the record itself</Text>
            <FilterRowsEditor
              catalog={catalog}
              entity={subject.entity}
              columns={subject.authorableFields}
              rows={condition.when}
              onChange={rows => setCondition(c => ({ ...c, when: rows }))}
              issues={visibleIssues(condition.when, built.issues)}
              testId="rule-wizard-when-row"
              addLabel="Add a test"
              emptyText="No tests on the record itself."
              disabled={locked}
            />
          </section>

          {isThreshold ? (
            <section className={styles.group} aria-label="The measure to compare" data-testid="rule-wizard-threshold">
              <Text weight="semibold">The measure to compare</Text>
              <div className={styles.inline}>
                <Select
                  value={condition.thresholdField}
                  disabled={locked}
                  aria-label="Measure"
                  onChange={(_event, data) => setCondition(c => ({ ...c, thresholdField: data.value }))}
                  data-testid="rule-wizard-threshold-field"
                >
                  <option value="">Choose a measure</option>
                  {subject.thresholdFields.map(t => (
                    <option key={t.field} value={t.field}>
                      {t.field}
                    </option>
                  ))}
                </Select>
                <Select
                  value={condition.thresholdOperator}
                  disabled={locked}
                  aria-label="Comparison"
                  onChange={(_event, data) => setCondition(c => ({ ...c, thresholdOperator: data.value }))}
                  data-testid="rule-wizard-threshold-operator"
                >
                  {catalog.operators.threshold.map(op => (
                    <option key={op} value={op}>
                      {op}
                    </option>
                  ))}
                </Select>
                <Input
                  value={condition.thresholdValue}
                  disabled={locked}
                  inputMode="decimal"
                  aria-label="Value"
                  onChange={(_event, data) => setCondition(c => ({ ...c, thresholdValue: data.value }))}
                  data-testid="rule-wizard-threshold-value"
                />
              </div>
              {subject.thresholdFields.map(t =>
                t.field === condition.thresholdField ? (
                  <Text key={t.field} size={200} className={styles.muted} data-testid="rule-wizard-threshold-range">
                    Allowed values: {t.min} to {t.max}.
                  </Text>
                ) : null
              )}
              {'threshold' in built.issues && (condition.thresholdField !== '' || condition.thresholdValue !== '') ? (
                <Text size={200} className={styles.error} data-testid="rule-wizard-threshold-issue">
                  {built.issues.threshold}
                </Text>
              ) : null}
            </section>
          ) : (
            <section className={styles.group} aria-label="Related records" data-testid="rule-wizard-clauses">
              <Text weight="semibold">Related records</Text>
              {condition.clauses.length === 0 ? <Text className={styles.muted}>No related-record tests yet.</Text> : null}
              {condition.clauses.map((clause, index) => {
                const join = subject.joins.find(j => j.related === clause.related);
                const clauseId = `rule-wizard-clause-${index}`;
                return (
                  <div key={clause.id} className={styles.group} data-testid={clauseId}>
                    <div className={styles.inline}>
                      <Select
                        value={clause.kind}
                        disabled={locked}
                        aria-label="Kind of related test"
                        onChange={(_event, data) => setClause(clause.id, c => ({ ...c, kind: data.value as ClauseKind }))}
                        data-testid={`${clauseId}-kind`}
                      >
                        {CLAUSE_KINDS.map(k => (
                          <option key={k.kind} value={k.kind}>
                            {k.label}
                          </option>
                        ))}
                      </Select>
                      <Select
                        value={clause.related}
                        disabled={locked}
                        aria-label="Related records"
                        onChange={(_event, data) =>
                          setClause(clause.id, c => ({ ...c, related: data.value, filter: [emptyRow(catalog)] }))
                        }
                        data-testid={`${clauseId}-related`}
                      >
                        <option value="">Choose related records</option>
                        {subject.joins.map(j => (
                          <option key={j.related} value={j.related}>
                            {j.related}
                          </option>
                        ))}
                      </Select>
                      <Button
                        appearance="subtle"
                        icon={<DeleteRegular />}
                        aria-label="Remove this related-record test"
                        disabled={locked}
                        onClick={() => setCondition(c => ({ ...c, clauses: c.clauses.filter(x => x.id !== clause.id) }))}
                        data-testid={`${clauseId}-remove`}
                      />
                    </div>
                    {join === undefined ? (
                      clause.related === '' ? null : (
                        <Text size={200} className={styles.error}>
                          The service offers no such related records for this table.
                        </Text>
                      )
                    ) : (
                      <FilterRowsEditor
                        catalog={catalog}
                        entity={join.related}
                        columns={join.authorableFields}
                        rows={clause.filter}
                        onChange={rows => setClause(clause.id, c => ({ ...c, filter: rows }))}
                        issues={visibleIssues(clause.filter, built.issues)}
                        testId={`${clauseId}-row`}
                        addLabel="Add a test"
                        emptyText="No tests on these records."
                        disabled={locked}
                      />
                    )}
                  </div>
                );
              })}
              <div>
                <Button
                  appearance="secondary"
                  size="small"
                  icon={<AddRegular />}
                  disabled={locked || condition.clauses.length >= maxClauses || subject.joins.length === 0}
                  onClick={() =>
                    setCondition(c => ({ ...c, clauses: [...c.clauses, emptyClause(catalog, '')] }))
                  }
                  data-testid="rule-wizard-add-clause"
                >
                  Add a related-record test
                </Button>
                <Text size={200} className={styles.muted}>
                  {' '}
                  {condition.clauses.length} of {maxClauses} used.
                </Text>
              </div>
            </section>
          )}
        </>
      )}

      {ruleBodyError ? (
        <Text role="alert" className={styles.error} data-testid="rule-wizard-condition-refusal">
          {ruleBodyError}
        </Text>
      ) : null}

      {summary === undefined ? (
        <details data-testid="rule-wizard-body-details">
          <summary>How the rule will be written</summary>
          <pre className={styles.mono} data-testid="rule-wizard-body-preview">
            {previewOf(built.ruleBody)}
          </pre>
        </details>
      ) : (
        <Text size={200} className={styles.muted} data-testid="rule-wizard-condition-summary">
          To continue: {summary}
        </Text>
      )}
    </div>
  );
};

ConditionStep.displayName = 'ConditionStep';

/** The body indented for reading. Read-only: nothing here is parsed back. */
function previewOf(ruleBody: string | null): string {
  if (ruleBody === null) {
    return '';
  }
  try {
    return JSON.stringify(JSON.parse(ruleBody), null, 2);
  } catch {
    return ruleBody;
  }
}
