/**
 * FilterRowsEditor - the rows of one filter in the Condition step (spaarke-ontology-platform-r1 task 102; D-22, D-149, #46).
 *
 * One row is one test on one column: the column, the kind of test, and its value. The columns, the comparison operators, the null
 * tests, the relative-date forms and the triage categories are all the catalog's; this component holds none of its own. It only
 * composes the controls that the closed grammar's filter values take:
 *
 *   compares to a value     -> a value, or `{ "<op>": value }`
 *   is one of a list        -> `[value, ...]`
 *   is within a window      -> `{ "<lower op>": date, "<upper op>": date }` (two relative dates)
 *   has / has no value      -> `{ "notNull": true }` / `{ "null": true }`
 *
 * A value is typed (text, number, yes or no, or a relative date) because the grammar's scalars are. A triage-category column offers
 * the categories by name and sends their ids ("filter values are ids, never labels").
 *
 * Whether a row is acceptable is NOT decided here: `validate` decides, and its answer is shown by the Condition step. This component
 * only shows the one thing it can see, that an input cannot be written into the rule (`issue`).
 *
 * Fluent v9 tokens only (ADR-021).
 */

import * as React from 'react';
import { Button, Checkbox, Input, Select, Text, makeStyles, tokens } from '@fluentui/react-components';
import { DeleteRegular } from '@fluentui/react-icons';
import type { AuthoringCatalog, CatalogCategory } from '../authoringService';
import { emptyRow } from '../ruleDraft';
import type { FilterRow, RelativeDateDraft, ValueKind } from '../ruleDraft';

const useStyles = makeStyles({
  rows: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalS,
    minWidth: 0,
  },
  row: {
    display: 'flex',
    flexDirection: 'column',
    rowGap: tokens.spacingVerticalXS,
    paddingTop: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalXS,
    minWidth: 0,
  },
  controls: {
    display: 'flex',
    flexWrap: 'wrap',
    alignItems: 'center',
    columnGap: tokens.spacingHorizontalS,
    rowGap: tokens.spacingVerticalXS,
    minWidth: 0,
  },
  valueInput: {
    minWidth: '10rem',
  },
  daysInput: {
    width: '5rem',
  },
  issue: {
    color: tokens.colorPaletteRedForeground1,
  },
  muted: {
    color: tokens.colorNeutralForeground3,
  },
  categories: {
    display: 'flex',
    flexDirection: 'column',
  },
});

/** The words for the catalog's comparison operators and null tests. The symbols and member names are the catalog's; these are only labels. */
const OPERATOR_WORDS: Readonly<Record<string, string | undefined>> = {
  '=': 'is',
  '<>': 'is not',
  '>=': 'is at least',
  '<=': 'is at most',
  '>': 'is more than',
  '<': 'is less than',
};
const NULL_TEST_WORDS: Readonly<Record<string, string | undefined>> = { null: 'has no value', notNull: 'has a value' };

const operatorLabel = (op: string): string => {
  const word = OPERATOR_WORDS[op];
  return word === undefined ? op : `${word} (${op})`;
};

const VALUE_KINDS: ReadonlyArray<{ readonly kind: ValueKind; readonly label: string }> = [
  { kind: 'text', label: 'Text' },
  { kind: 'number', label: 'Number' },
  { kind: 'boolean', label: 'Yes or no' },
  { kind: 'date', label: 'Date' },
];

/** How a relative-date form reads as the unit of a number: `now` alone, or "days before / after now". */
export function describeDateForm(form: string): string {
  if (!form.includes('{N}')) {
    return form;
  }
  if (form.startsWith('now-')) {
    return 'days before now';
  }
  if (form.startsWith('now+')) {
    return 'days after now';
  }
  return form;
}

// ---- the test select: one control that picks the kind of test and its operator ---------------------------------------------------

/** `compare:<op>`, `in`, `window` or `null:<member>`. */
function testValueOf(row: FilterRow): string {
  switch (row.test) {
    case 'compare':
      return `compare:${row.op}`;
    case 'null':
      return `null:${row.op}`;
    default:
      return row.test;
  }
}

function applyTest(row: FilterRow, value: string): FilterRow {
  if (value === 'in' || value === 'window') {
    return { ...row, test: value };
  }
  const separator = value.indexOf(':');
  const kind = value.slice(0, separator);
  const op = value.slice(separator + 1);
  return kind === 'null' ? { ...row, test: 'null', op } : { ...row, test: 'compare', op };
}

// ---- value controls ---------------------------------------------------------------------------------------------------------

interface DateEditorProps {
  catalog: AuthoringCatalog;
  date: RelativeDateDraft;
  label: string;
  testId: string;
  disabled: boolean;
  onChange: (date: RelativeDateDraft) => void;
}

const DateEditor: React.FC<DateEditorProps> = ({ catalog, date, label, testId, disabled, onChange }) => {
  const styles = useStyles();
  const needsDays = date.form.includes('{N}');
  return (
    <span className={styles.controls}>
      {needsDays ? (
        <Input
          className={styles.daysInput}
          value={date.days}
          disabled={disabled}
          inputMode="numeric"
          aria-label={`${label} days`}
          onChange={(_event, data) => onChange({ ...date, days: data.value })}
          data-testid={`${testId}-days`}
        />
      ) : null}
      <Select
        value={date.form}
        disabled={disabled}
        aria-label={label}
        onChange={(_event, data) => onChange({ ...date, form: data.value })}
        data-testid={`${testId}-form`}
      >
        {catalog.relativeDates.forms.map(form => (
          <option key={form} value={form}>
            {describeDateForm(form)}
          </option>
        ))}
      </Select>
    </span>
  );
};

interface ScalarEditorProps {
  catalog: AuthoringCatalog;
  row: FilterRow;
  kinds: readonly ValueKind[];
  testId: string;
  disabled: boolean;
  onChange: (row: FilterRow) => void;
}

/** A typed value: the kind, then the input the kind needs. */
const ScalarEditor: React.FC<ScalarEditorProps> = ({ catalog, row, kinds, testId, disabled, onChange }) => {
  const styles = useStyles();
  return (
    <>
      <Select
        value={row.valueKind}
        disabled={disabled}
        aria-label="Value type"
        onChange={(_event, data) => onChange({ ...row, valueKind: data.value as ValueKind })}
        data-testid={`${testId}-kind`}
      >
        {VALUE_KINDS.filter(k => kinds.includes(k.kind)).map(k => (
          <option key={k.kind} value={k.kind}>
            {k.label}
          </option>
        ))}
      </Select>
      {row.valueKind === 'date' ? (
        <DateEditor
          catalog={catalog}
          date={row.date}
          label="Date"
          testId={`${testId}-date`}
          disabled={disabled}
          onChange={date => onChange({ ...row, date })}
        />
      ) : row.valueKind === 'boolean' ? (
        <Select
          value={row.value}
          disabled={disabled}
          aria-label="Value"
          onChange={(_event, data) => onChange({ ...row, value: data.value })}
          data-testid={`${testId}-value`}
        >
          <option value="">Choose</option>
          <option value="true">Yes</option>
          <option value="false">No</option>
        </Select>
      ) : (
        <Input
          className={styles.valueInput}
          value={row.value}
          disabled={disabled}
          aria-label="Value"
          inputMode={row.valueKind === 'number' ? 'decimal' : 'text'}
          onChange={(_event, data) => onChange({ ...row, value: data.value })}
          data-testid={`${testId}-value`}
        />
      )}
    </>
  );
};

// ---- one row ----------------------------------------------------------------------------------------------------------------

interface RowEditorProps {
  catalog: AuthoringCatalog;
  row: FilterRow;
  /** The columns this row may choose: the table's, less those a sibling row already tests. */
  columns: readonly string[];
  /** The catalog's triage categories when this row's column is the triage column; otherwise null. */
  categories: readonly CatalogCategory[] | null;
  issue: string | undefined;
  testId: string;
  disabled: boolean;
  onChange: (row: FilterRow) => void;
  onRemove: () => void;
}

const RowEditor: React.FC<RowEditorProps> = ({ catalog, row, columns, categories, issue, testId, disabled, onChange, onRemove }) => {
  const styles = useStyles();
  const canWindow = catalog.operators.rangeLower.length > 0 && catalog.operators.rangeUpper.length > 0;

  return (
    <div className={styles.row} data-testid={testId}>
      <div className={styles.controls}>
        <Select
          value={row.field}
          disabled={disabled}
          aria-label="Column"
          onChange={(_event, data) =>
            // A value belongs to the column it was entered for; a new column starts without the old column's value, list or ids.
            onChange(data.value === row.field ? row : { ...row, field: data.value, value: '', listText: '', ids: [] })
          }
          data-testid={`${testId}-field`}
        >
          <option value="">Choose a column</option>
          {columns.map(column => (
            <option key={column} value={column}>
              {column}
            </option>
          ))}
        </Select>

        <Select
          value={testValueOf(row)}
          disabled={disabled}
          aria-label="Test"
          onChange={(_event, data) => onChange(applyTest(row, data.value))}
          data-testid={`${testId}-test`}
        >
          {catalog.operators.comparison.map(op => (
            <option key={`compare:${op}`} value={`compare:${op}`}>
              {operatorLabel(op)}
            </option>
          ))}
          <option value="in">is one of</option>
          {canWindow ? <option value="window">is within a window</option> : null}
          {catalog.operators.nullTests.map(member => (
            <option key={`null:${member}`} value={`null:${member}`}>
              {NULL_TEST_WORDS[member] ?? member}
            </option>
          ))}
        </Select>

        {row.test === 'compare' && categories !== null ? (
          <Select
            value={row.value}
            disabled={disabled}
            aria-label="Category"
            onChange={(_event, data) => onChange({ ...row, valueKind: 'text', value: data.value })}
            data-testid={`${testId}-category`}
          >
            <option value="">Choose a category</option>
            {categories.map(category => (
              <option key={category.id} value={category.id}>
                {category.enabled ? category.name : `${category.name} (off)`}
              </option>
            ))}
          </Select>
        ) : null}

        {row.test === 'compare' && categories === null ? (
          <ScalarEditor
            catalog={catalog}
            row={row}
            kinds={['text', 'number', 'boolean', 'date']}
            testId={testId}
            disabled={disabled}
            onChange={onChange}
          />
        ) : null}

        {row.test === 'in' && categories === null ? (
          <>
            <Select
              value={row.valueKind === 'number' ? 'number' : 'text'}
              disabled={disabled}
              aria-label="Value type"
              onChange={(_event, data) => onChange({ ...row, valueKind: data.value as ValueKind })}
              data-testid={`${testId}-kind`}
            >
              <option value="text">Text</option>
              <option value="number">Number</option>
            </Select>
            <Input
              className={styles.valueInput}
              value={row.listText}
              disabled={disabled}
              aria-label="Values, separated by commas"
              placeholder="Values, separated by commas"
              onChange={(_event, data) => onChange({ ...row, listText: data.value })}
              data-testid={`${testId}-list`}
            />
          </>
        ) : null}

        {row.test === 'window' ? (
          <>
            <Select
              value={row.lowerOp}
              disabled={disabled}
              aria-label="Window start"
              onChange={(_event, data) => onChange({ ...row, lowerOp: data.value })}
              data-testid={`${testId}-lower-op`}
            >
              {catalog.operators.rangeLower.map(op => (
                <option key={op} value={op}>
                  {operatorLabel(op)}
                </option>
              ))}
            </Select>
            <DateEditor
              catalog={catalog}
              date={row.lower}
              label="Window start"
              testId={`${testId}-lower`}
              disabled={disabled}
              onChange={lower => onChange({ ...row, lower })}
            />
            <Select
              value={row.upperOp}
              disabled={disabled}
              aria-label="Window end"
              onChange={(_event, data) => onChange({ ...row, upperOp: data.value })}
              data-testid={`${testId}-upper-op`}
            >
              {catalog.operators.rangeUpper.map(op => (
                <option key={op} value={op}>
                  {operatorLabel(op)}
                </option>
              ))}
            </Select>
            <DateEditor
              catalog={catalog}
              date={row.upper}
              label="Window end"
              testId={`${testId}-upper`}
              disabled={disabled}
              onChange={upper => onChange({ ...row, upper })}
            />
          </>
        ) : null}

        <Button
          appearance="subtle"
          icon={<DeleteRegular />}
          aria-label="Remove this test"
          disabled={disabled}
          onClick={onRemove}
          data-testid={`${testId}-remove`}
        />
      </div>

      {row.test === 'in' && categories !== null ? (
        <div className={styles.categories} role="group" aria-label="Categories" data-testid={`${testId}-categories`}>
          {categories.map(category => (
            <Checkbox
              key={category.id}
              label={category.enabled ? category.name : `${category.name} (off)`}
              checked={row.ids.includes(category.id)}
              disabled={disabled}
              onChange={(_event, data) =>
                onChange({
                  ...row,
                  ids: data.checked === true ? [...row.ids, category.id] : row.ids.filter(id => id !== category.id),
                })
              }
              data-testid={`${testId}-category-${category.id}`}
            />
          ))}
        </div>
      ) : null}

      {issue ? (
        <Text size={200} className={styles.issue} data-testid={`${testId}-issue`}>
          {issue}
        </Text>
      ) : null}
    </div>
  );
};

RowEditor.displayName = 'RowEditor';

// ---- the rows ---------------------------------------------------------------------------------------------------------------

export interface FilterRowsEditorProps {
  catalog: AuthoringCatalog;
  /** The table the rows are about, to recognise the triage column. */
  entity: string;
  /** Every column the table offers (the catalog's `authorableFields` for it). */
  columns: readonly string[];
  rows: FilterRow[];
  onChange: (rows: FilterRow[]) => void;
  /** What the wizard found cannot be written into the rule, by row id. */
  issues: Record<string, string>;
  /** Prefix of the rows' test ids. */
  testId: string;
  addLabel: string;
  emptyText: string;
  disabled: boolean;
}

export const FilterRowsEditor: React.FC<FilterRowsEditorProps> = ({
  catalog,
  entity,
  columns,
  rows,
  onChange,
  issues,
  testId,
  addLabel,
  emptyText,
  disabled,
}) => {
  const styles = useStyles();
  const used = new Set(rows.map(r => r.field).filter(f => f !== ''));
  const allUsed = columns.every(c => used.has(c));

  return (
    <div className={styles.rows}>
      {rows.length === 0 ? <Text className={styles.muted}>{emptyText}</Text> : null}
      {rows.map((row, index) => (
        <RowEditor
          key={row.id}
          catalog={catalog}
          row={row}
          // A column can be tested once in a filter (the grammar keys a filter by column), so a sibling's column is not offered.
          columns={columns.filter(c => c === row.field || !used.has(c))}
          categories={entity === catalog.triage.entity && row.field === catalog.triage.field ? catalog.triage.categories : null}
          issue={issues[row.id]}
          testId={`${testId}-${index}`}
          disabled={disabled}
          onChange={next => onChange(rows.map(r => (r.id === row.id ? next : r)))}
          onRemove={() => onChange(rows.filter(r => r.id !== row.id))}
        />
      ))}
      <div>
        <Button
          appearance="secondary"
          size="small"
          disabled={disabled || allUsed}
          onClick={() => onChange([...rows, emptyRow(catalog)])}
          data-testid={`${testId}-add`}
        >
          {addLabel}
        </Button>
      </div>
    </div>
  );
};

FilterRowsEditor.displayName = 'FilterRowsEditor';
