/**
 * Source guards for the rule wizard (task 102; D-149, ADR-021, ADR-050):
 *
 *  - tokens only: no colour literal;
 *  - not opened through the platform dialog: no `navigateTo`, no Xrm, no `WizardModal`;
 *  - NO list is mirrored in the client: no action, next-step or dismissal-reason code, no severity or work-type label or value, and no
 *    Dataverse column or table name appears in the wizard's sources. Every one of these comes from the BFF catalog.
 *
 * Each guard has a control that proves it can fire (a string containing what it forbids), so a guard that matches nothing cannot pass
 * silently.
 */

import * as fs from 'fs';
import * as path from 'path';

const DIR = path.resolve(__dirname, '..');

function sourcesOf(dir: string): Array<{ file: string; text: string }> {
  const found: Array<{ file: string; text: string }> = [];
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      if (entry.name !== '__tests__') {
        found.push(...sourcesOf(full));
      }
    } else if (/\.(ts|tsx)$/.test(entry.name)) {
      found.push({ file: path.relative(DIR, full), text: fs.readFileSync(full, 'utf8') });
    }
  }
  return found;
}

/** The sources without comments, so a sentence that names a forbidden thing in prose is not a use of it. */
function code(text: string): string {
  return text.replace(/\/\*[\s\S]*?\*\//g, '').replace(/(^|[^:])\/\/.*$/gm, '$1');
}

const sources = sourcesOf(DIR);

const COLOUR = /#[0-9a-fA-F]{3,8}\b|\brgba?\(|\bhsla?\(/;
const PLATFORM_DIALOG = /\bnavigateTo\s*\(|\bXrm\b|import[^;]*\bWizardModal\b|<WizardModal\b/;

// The real catalog's codes and labels (DecisionActionCatalog, OntologyAdminRulesService), as quoted literals.
const ACTION_CODES = [
  'send-budget-inquiry',
  'revise-budget',
  'approve-variance',
  'mark-complete',
  'reschedule',
  'reassign',
  'send-reminder',
  'extend-response-date',
  'record-the-response',
  'add-todo',
  'create-event',
  'send-email',
  'assign-work',
];
const DISMISSAL_CODES = [
  'already-approved-offline',
  'not-material',
  'wrong-matter-misresolved',
  'duplicate',
  'handled-outside-spaarke',
  'already-done-outside-spaarke',
];
const OPTION_LABELS = ['Info', 'Warning', 'Critical', 'Ask outside counsel', 'Approve or rebudget', 'Chase a reply', 'Finish or reschedule', 'Coming due', 'Chase a response'];
const LIST_LITERAL = new RegExp(
  [...ACTION_CODES, ...DISMISSAL_CODES, ...OPTION_LABELS].map(w => `['"\`]${w}['"\`]`).join('|') + '|\\b1000000\\d\\d\\b'
);
const DATAVERSE_NAME = /\bsprk_[a-z]+/;

describe('RuleWizard sources', () => {
  it('finds the sources it guards', () => {
    expect(sources.length).toBeGreaterThan(12);
    expect(sources.map(s => s.file)).toEqual(expect.arrayContaining(['RuleWizard.tsx', 'authoringService.ts', 'ruleDraft.ts']));
  });

  it('contains no colour literal (tokens only, ADR-021)', () => {
    expect(COLOUR.test('color: "#fff"')).toBe(true);
    expect(COLOUR.test('rgb(1, 2, 3)')).toBe(true);
    for (const { file, text } of sources) {
      expect({ file, hit: COLOUR.test(code(text)) }).toEqual({ file, hit: false });
    }
  });

  it('is not opened through navigateTo, Xrm or WizardModal (ADR-050)', () => {
    expect(PLATFORM_DIALOG.test('Xrm.Navigation.navigateTo({})')).toBe(true);
    expect(PLATFORM_DIALOG.test("import { WizardModal } from '../x'")).toBe(true);
    for (const { file, text } of sources) {
      expect({ file, hit: PLATFORM_DIALOG.test(code(text)) }).toEqual({ file, hit: false });
    }
  });

  it('holds no action, next-step, dismissal-reason, severity or work-type list (D-149)', () => {
    expect(LIST_LITERAL.test("const a = 'assign-work'")).toBe(true);
    expect(LIST_LITERAL.test('{ value: 100000001 }')).toBe(true);
    expect(LIST_LITERAL.test('label: "Critical"')).toBe(true);
    for (const { file, text } of sources) {
      expect({ file, hit: LIST_LITERAL.test(code(text)) }).toEqual({ file, hit: false });
    }
  });

  it('names no Dataverse table or column: the column lists are the catalog\'s', () => {
    expect(DATAVERSE_NAME.test("field: 'sprk_matter'")).toBe(true);
    for (const { file, text } of sources) {
      expect({ file, hit: DATAVERSE_NAME.test(code(text)) }).toEqual({ file, hit: false });
    }
  });
});
