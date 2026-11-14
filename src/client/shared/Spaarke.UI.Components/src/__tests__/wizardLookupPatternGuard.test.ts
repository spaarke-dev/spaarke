/**
 * Guard: the create wizards use the side-pane lookup and the shared composer, never the old inline patterns
 * (spaarke-ontology-platform-r1 task 184; owner directive 2026-11-12 after UAT round 4, findings F-R4-2 / F-R4-3).
 *
 * Why. A wizard field that picks a person or organization is `DataverseLookupField` (Select -> `INavigationService.openLookup`,
 * the Dataverse side pane). A wizard email is the shared `EmailComposer`. The pre-existing duplicates, the inline
 * `LookupField` rendered inside a step and the old `components/EmailStep/` (its own `LookupField` copy and a second
 * `SendEmailStep`), were converted by tasks 179-183 and retired by task 184. A new or copied step can bring either back
 * without any test noticing, so this scan fails the build when it does. Pattern: `.claude/patterns/ui/wizard-side-pane-lookup-and-email-compose.md`.
 *
 * What it checks, over the non-test sources under `components/Create*Wizard/` and `components/WizardFollowOns/`:
 *   1. `inline-lookup`  - the file uses the inline `LookupField` as a value outside an import declaration: JSX (`<LookupField`,
 *                         `<ns.LookupField`), an argument (`React.createElement(LookupField`), an assignment
 *                         (`const L = LookupField`) or a re-export (`export { LookupField as X } from`), and the same for every
 *                         name it is imported as. Comments and the text of strings are ignored (a string-aware walk, so a `//` or
 *                         `/*` inside `"image/*"` hides nothing); `DataverseLookupField` is the sanctioned component and never matches.
 *   2. `email-step`     - the file references a module whose path ends in `EmailStep`: after `from`, `import(`, `import '...'`,
 *                         `require(` or `jest.mock(`. `DocumentEmailStep`, the composer wrappers and a bare `'EmailStep'` string
 *                         (a step id) do not match.
 * and, over every non-test source in the package:
 *   3. `send-email-step` - exactly one module defines or exports a symbol named `SendEmailStep` (the composer wrapper): an inline
 *                         `export const|function|class`, `export { X as SendEmailStep }`, or a local declaration exported later by
 *                         an export list or `export default`. A plain re-export of the same binding is not a second one.
 *
 * Ratchet: BASELINE lists the violations that exist today, each with its reason. A violation that is not listed fails; a listed
 * entry that no longer violates fails until it is removed from the list. It is empty: tasks 179-183 converted every field the
 * owner wanted on the side pane (reference tables included, D-219), and task 184 retired the files that were left behind.
 *
 * Fix a violation by using `DataverseLookupField` with the host's `navigationService`, or `EmailComposer` (compose-only inside
 * a wizard). Do not add an entry to BASELINE for a new step.
 *
 * Enforced by: this test (run by the `xrm-capability-guard` job of ci-tier1-blocking.yml, and by the package's jest suite).
 */
import * as fs from 'fs';
import * as path from 'path';

export type GuardRule = 'inline-lookup' | 'email-step' | 'send-email-step';

export interface ISourceFile {
  /** Path relative to `src/components`, forward slashes (e.g. `CreateMatterWizard/CreateRecordStep.tsx`). */
  file: string;
  source: string;
}

export interface IViolation {
  rule: GuardRule;
  file: string;
}

export interface IBaselineEntry extends IViolation {
  reason: string;
}

/** Violations that exist today and are deliberately kept. Empty after task 184. */
const BASELINE: readonly IBaselineEntry[] = [];

/** Wizard source folders the lookup and email rules apply to. */
const GUARDED_FOLDER = /^(Create[A-Za-z]*Wizard|WizardFollowOns)\//;
const SOURCE_FILE = /\.(ts|tsx)$/;
const TEST_FILE = /(^|\/)__tests__\/|\.(test|spec)\.(ts|tsx)$/;

export function isGuardedWizardFile(file: string): boolean {
  return GUARDED_FOLDER.test(file) && SOURCE_FILE.test(file) && !TEST_FILE.test(file);
}

/**
 * Walks `source` as code and returns it with every comment replaced by spaces (newlines kept). It is string-aware: a `//` or a
 * `/*` inside a string, a template literal's text or a JSX attribute value (`accept="image/*"`) starts no comment. With
 * `blankStrings` the text of each string is replaced by spaces too (quotes kept), so a string that merely mentions
 * `<LookupField` never counts; `${...}` inside a template literal is code and is walked as code.
 *
 * Known limits (a source scan, not a parser): a regular-expression literal that contains a quote or `//`, and an apostrophe in
 * JSX text, can open a bogus string; a quote string ends at the line end, so the damage stops at that line.
 */
function walkCode(
  source: string,
  start: number,
  blankStrings: boolean,
  untilCloseBrace: boolean
): { text: string; end: number } {
  let out = '';
  let i = start;
  let depth = 0;
  const n = source.length;
  const blank = (text: string): string => (blankStrings ? text.replace(/[^\n]/g, ' ') : text);
  while (i < n) {
    const c = source[i];
    const d = source[i + 1];
    if (c === '/' && d === '/') {
      while (i < n && source[i] !== '\n') i++;
    } else if (c === '/' && d === '*') {
      const close = source.indexOf('*/', i + 2);
      const stop = close === -1 ? n : close + 2;
      out += source.slice(i, stop).replace(/[^\n]/g, ' ');
      i = stop;
    } else if (c === '"' || c === "'") {
      let j = i + 1;
      while (j < n && source[j] !== c && source[j] !== '\n') j += source[j] === '\\' ? 2 : 1;
      const closed = j < n && source[j] === c;
      out += c + blank(source.slice(i + 1, Math.min(j, n))) + (closed ? c : '');
      i = closed ? j + 1 : Math.min(j, n);
    } else if (c === '`') {
      out += c;
      i++;
      while (i < n && source[i] !== '`') {
        if (source[i] === '\\') {
          out += blank(source.slice(i, i + 2));
          i += 2;
        } else if (source[i] === '$' && source[i + 1] === '{') {
          const inner = walkCode(source, i + 2, blankStrings, true);
          out += '${' + inner.text + '}';
          i = inner.end;
        } else {
          out += blank(source[i]);
          i++;
        }
      }
      if (i < n) {
        out += '`';
        i++;
      }
    } else {
      if (untilCloseBrace && c === '}' && depth === 0) return { text: out, end: i + 1 };
      if (c === '{') depth++;
      else if (c === '}') depth--;
      out += c;
      i++;
    }
  }
  return { text: out, end: n };
}

/** Comments removed; string text kept (module specifiers stay visible). */
export function stripComments(source: string): string {
  return walkCode(source, 0, false, false).text;
}

/** Comments removed and string text blanked: only code remains. */
export function codeOnly(source: string): string {
  return walkCode(source, 0, true, false).text;
}

const IDENT = '[A-Za-z_$][A-Za-z0-9_$]*';
const NOT_IDENT_BEFORE = '(?<![A-Za-z0-9_$])';
const NOT_IDENT_AFTER = '(?![A-Za-z0-9_$])';
/** `import ... from '...'` (type-only too), possibly over several lines. Side-effect imports bind nothing. */
const IMPORT_DECLARATION = /(?<![A-Za-z0-9_$.])import\s+(?:type\s+)?[^;'"`]*?\bfrom\s*(['"])[^'"\n]*\1\s*;?/g;

/**
 * True when the file uses the inline `LookupField` as a value anywhere outside an import declaration: as JSX (`<LookupField`,
 * `<ns.LookupField`), as an argument (`React.createElement(LookupField`), in an assignment (`const L = LookupField`), or in a
 * re-export (`export { LookupField as X } from`), and the same for every name it is imported as (`import { LookupField as X }`).
 * An import that is never used is not a rendering. `DataverseLookupField` and any other longer name never match.
 */
export function usesInlineLookupField(source: string): boolean {
  const code = codeOnly(source);
  const imports = code.match(IMPORT_DECLARATION) ?? [];
  const rest = code.replace(IMPORT_DECLARATION, ' ');
  const names = ['LookupField'];
  for (const decl of imports) {
    for (const m of decl.matchAll(new RegExp(String.raw`${NOT_IDENT_BEFORE}LookupField\s+as\s+(${IDENT})`, 'g')))
      names.push(m[1]);
  }
  return names.some(name =>
    new RegExp(String.raw`${NOT_IDENT_BEFORE}${name.replace(/\$/g, '\\$')}${NOT_IDENT_AFTER}`).test(rest)
  );
}

/** A module reference: after `from`, `import(`, `import '...'`, `require(` or a `jest.mock`-family call. */
const MODULE_REFERENCE = new RegExp(
  String.raw`(?:${NOT_IDENT_BEFORE}from|${NOT_IDENT_BEFORE}import\s*\(?|${NOT_IDENT_BEFORE}require\s*\(|${NOT_IDENT_BEFORE}jest\s*\.\s*(?:mock|doMock|unmock|requireActual|requireMock)\s*\()\s*['"][^'"\n]*${NOT_IDENT_BEFORE}EmailStep(?:/[^'"\n]*)?['"]`
);

/** True when the file references a module whose path ends in `EmailStep` (a bare `'EmailStep'` string, as a step id, is not one). */
export function importsEmailStep(source: string): boolean {
  return MODULE_REFERENCE.test(stripComments(source));
}

/**
 * True when the file defines or exports a symbol named `SendEmailStep` of its own: an inline `export const|function|class`, an
 * `export { X as SendEmailStep }`, or a local `SendEmailStep` that an export list or `export default` exports later. A plain
 * re-export (`export { SendEmailStep } from './wrappers/SendEmailStep'`) is the same binding and does not count.
 */
export function definesSendEmailStep(source: string): boolean {
  const code = codeOnly(source);
  if (
    new RegExp(
      String.raw`export\s+(?:default\s+)?(?:const|let|var|function|class)\s+SendEmailStep${NOT_IDENT_AFTER}`
    ).test(code)
  ) {
    return true;
  }
  if (
    new RegExp(
      String.raw`export\s*(?:type\s*)?\{[^}]*${NOT_IDENT_BEFORE}as\s+SendEmailStep${NOT_IDENT_AFTER}[^}]*\}`
    ).test(code)
  ) {
    return true;
  }
  const declaredHere = new RegExp(
    String.raw`${NOT_IDENT_BEFORE}(?:const|let|var|function|class)\s+SendEmailStep${NOT_IDENT_AFTER}`
  ).test(code);
  if (!declaredHere) return false;
  const exportedByList = new RegExp(
    String.raw`export\s*\{[^}]*${NOT_IDENT_BEFORE}SendEmailStep${NOT_IDENT_AFTER}(?!\s+as\b)[^}]*\}(?!\s*from\b)`
  ).test(code);
  const exportedByDefault = new RegExp(String.raw`export\s+default\s+SendEmailStep${NOT_IDENT_AFTER}`).test(code);
  return exportedByList || exportedByDefault;
}

/** Rules 1 and 2 over the wizard folders, rule 3 over every file given. Sorted for a stable report. */
export function findViolations(files: readonly ISourceFile[]): IViolation[] {
  const found: IViolation[] = [];
  for (const f of files) {
    if (isGuardedWizardFile(f.file)) {
      if (usesInlineLookupField(f.source)) found.push({ rule: 'inline-lookup', file: f.file });
      if (importsEmailStep(f.source)) found.push({ rule: 'email-step', file: f.file });
    }
  }
  const definers = files.filter(f => !TEST_FILE.test(f.file) && definesSendEmailStep(f.source)).map(f => f.file);
  if (definers.length > 1) {
    for (const file of definers) found.push({ rule: 'send-email-step', file });
  }
  return found.sort((a, b) => (a.rule + a.file).localeCompare(b.rule + b.file));
}

const key = (v: IViolation): string => `${v.rule} ${v.file}`;

/** The ratchet: violations not in the baseline, and baseline entries that no longer violate. */
export function applyBaseline(
  violations: readonly IViolation[],
  baseline: readonly IViolation[]
): { unlisted: string[]; stale: string[] } {
  const current = new Set(violations.map(key));
  const listed = new Set(baseline.map(key));
  return {
    unlisted: violations.map(key).filter(k => !listed.has(k)),
    stale: baseline.map(key).filter(k => !current.has(k)),
  };
}

function collectComponentSources(componentsRoot: string): ISourceFile[] {
  const found: ISourceFile[] = [];
  const walk = (dir: string): void => {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      if (entry.name === 'node_modules') continue;
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) walk(full);
      else if (SOURCE_FILE.test(entry.name)) {
        found.push({
          file: path.relative(componentsRoot, full).split(path.sep).join('/'),
          source: fs.readFileSync(full, 'utf-8'),
        });
      }
    }
  };
  walk(componentsRoot);
  return found;
}

// src/client/shared/Spaarke.UI.Components/src/__tests__ -> src/components
const COMPONENTS_ROOT = path.resolve(__dirname, '../components');

describe('wizard lookup / email pattern guard - repository', () => {
  const files = collectComponentSources(COMPONENTS_ROOT);
  const violations = findViolations(files);
  const { unlisted, stale } = applyBaseline(violations, BASELINE);

  it('scans the wizard folders (sanity: the scan is not empty and reaches the known steps)', () => {
    const guarded = files.filter(f => isGuardedWizardFile(f.file)).map(f => f.file);
    expect(guarded.length).toBeGreaterThan(30);
    expect(guarded).toContain('CreateMatterWizard/CreateRecordStep.tsx');
    expect(guarded).toContain('CreateProjectWizard/CreateProjectStep.tsx');
    expect(guarded).toContain('WizardFollowOns/steps/AssignWorkFollowOnStep.tsx');
    expect(guarded).toContain('WizardFollowOns/steps/SendEmailFollowOnStep.tsx');
  });

  it('no wizard step renders the inline LookupField or imports EmailStep, and exactly one SendEmailStep is defined', () => {
    expect(unlisted).toEqual([]);
  });

  it('every baseline entry still violates (remove an entry once it is fixed)', () => {
    expect(stale).toEqual([]);
  });

  it('every baseline entry carries a reason', () => {
    expect(BASELINE.filter(b => b.reason.trim().length < 10)).toEqual([]);
  });

  it('the composer wrapper is the one SendEmailStep', () => {
    const definers = files.filter(f => definesSendEmailStep(f.source)).map(f => f.file);
    expect(definers).toEqual(['EmailComposer/wrappers/SendEmailStep.tsx']);
  });

  it('the old EmailStep folder is gone', () => {
    expect(files.filter(f => f.file.startsWith('EmailStep/')).map(f => f.file)).toEqual([]);
  });
});

describe('wizard lookup / email pattern guard - controls (it can fire, and it can stay quiet)', () => {
  const step = 'CreateSeededWizard/SeededStep.tsx';

  describe('must fire', () => {
    it('on a seeded inline <LookupField under a Create*Wizard path', () => {
      const source = `import { LookupField } from '../LookupField';\nexport const S = () => (\n  <LookupField label="Assigned" onSearch={f} />\n);`;
      expect(findViolations([{ file: step, source }])).toEqual([{ rule: 'inline-lookup', file: step }]);
    });

    it('on a seeded <LookupField under WizardFollowOns, self-closing or with children', () => {
      expect(findViolations([{ file: 'WizardFollowOns/steps/X.tsx', source: 'const a = <LookupField />;' }])).toEqual([
        { rule: 'inline-lookup', file: 'WizardFollowOns/steps/X.tsx' },
      ]);
      expect(
        findViolations([{ file: 'WizardFollowOns/steps/X.tsx', source: 'const a = <LookupField>x</LookupField>;' }])
      ).toEqual([{ rule: 'inline-lookup', file: 'WizardFollowOns/steps/X.tsx' }]);
    });

    it('on the inline LookupField rendered through an import alias', () => {
      const source = `import { LookupField as SharedLookupField } from '../LookupField';\nconst a = <SharedLookupField label="x" />;`;
      expect(findViolations([{ file: step, source }])).toEqual([{ rule: 'inline-lookup', file: step }]);
    });

    it('on the inline LookupField reached through a namespace import', () => {
      const source = `import * as Lookups from '../LookupField';
const a = <Lookups.LookupField label="x" />;`;
      expect(findViolations([{ file: step, source }])).toEqual([{ rule: 'inline-lookup', file: step }]);
    });

    it.each([
      [
        'React.createElement(LookupField, ...)',
        `import { LookupField } from '../LookupField';\nexport const S = () => React.createElement(LookupField, { label: 'x' });`,
      ],
      [
        'an alias const',
        `import { LookupField } from '../LookupField';\nconst Picker = LookupField;\nexport const S = () => <Picker />;`,
      ],
      ['a re-exported alias (export-from)', `export { LookupField as PersonPicker } from '../LookupField';`],
      [
        'an imported alias that is exported on',
        `import { LookupField as Picker } from '../LookupField';\nexport { Picker };`,
      ],
      [
        'an imported alias passed as a value',
        `import { LookupField as Picker } from '../LookupField';\nexport const S = () => React.createElement(Picker, {});`,
      ],
      [
        'an alias whose name holds a $',
        `import { LookupField as $Picker } from '../LookupField';\nexport const S = () => <$Picker />;`,
      ],
      [
        'a // inside a string before the tag on the same line',
        `const a = (<><Foo placeholder="see http://x // not a comment" /><LookupField label="y" /></>);`,
      ],
      [
        'a /* inside a string that would swallow the code up to the next */',
        `const a = <input accept="image/*" />;\nconst b = <LookupField label="y" />;\n/** a later doc comment */\nexport const c = 1;`,
      ],
      [
        'a /* inside a single-quoted string',
        `const accept = 'image/*';\nconst b = <LookupField label="y" />;\n/* later */`,
      ],
      [
        'an apostrophe in JSX text (a bogus string ends at the line end)',
        `const a = <p>Don't pick here</p>;\nconst b = <LookupField label="y" />;`,
      ],
      ['code inside a template literal expression', 'const a = `${cond ? <LookupField label="y" /> : null}`;'],
    ])('on the inline LookupField used as a value: %s', (_label, source) => {
      expect(findViolations([{ file: step, source }])).toEqual([{ rule: 'inline-lookup', file: step }]);
    });

    it.each([
      ["import { SendEmailStep } from '../EmailStep';"],
      ["import { SendEmailStep } from '../../EmailStep/index';"],
      ["export { SendEmailStep } from '../EmailStep/SendEmailStep';"],
      ["const m = await import('../EmailStep');"],
      ["jest.mock('../../EmailStep/SendEmailStep', () => ({}));"],
      ["const m = require('../EmailStep');"],
      ["import '../EmailStep';"],
      ["import type { ISendEmailStepProps } from '../../EmailStep/SendEmailStep';"],
    ])('on a seeded EmailStep import: %s', source => {
      expect(findViolations([{ file: step, source }])).toEqual([{ rule: 'email-step', file: step }]);
    });

    it.each([
      ['an alias export', 'const Other = () => null;\nexport { Other as SendEmailStep };'],
      ['an alias re-export', "export { Other as SendEmailStep } from './other';"],
      [
        'a function exported later through a list',
        'function SendEmailStep() { return null; }\nexport { SendEmailStep };',
      ],
      [
        'a const exported later through a list with other names',
        'const SendEmailStep = () => null;\nexport { Foo, SendEmailStep, Bar };',
      ],
      [
        'a function exported later by default',
        'function SendEmailStep() { return null; }\nexport default SendEmailStep;',
      ],
    ])('on a second SendEmailStep export: %s', (_label, source) => {
      const result = findViolations([
        {
          file: 'EmailComposer/wrappers/SendEmailStep.tsx',
          source: 'export const SendEmailStep = forwardRef(() => null);',
        },
        { file: 'Other/Second.tsx', source },
      ]);
      expect(result).toEqual([
        { rule: 'send-email-step', file: 'EmailComposer/wrappers/SendEmailStep.tsx' },
        { rule: 'send-email-step', file: 'Other/Second.tsx' },
      ]);
    });

    it('on two modules that both define SendEmailStep (both are named)', () => {
      const result = findViolations([
        { file: 'EmailStep/SendEmailStep.tsx', source: 'export const SendEmailStep: React.FC = () => null;' },
        {
          file: 'EmailComposer/wrappers/SendEmailStep.tsx',
          source: 'export const SendEmailStep = forwardRef(() => null);',
        },
      ]);
      expect(result).toEqual([
        { rule: 'send-email-step', file: 'EmailComposer/wrappers/SendEmailStep.tsx' },
        { rule: 'send-email-step', file: 'EmailStep/SendEmailStep.tsx' },
      ]);
    });
  });

  describe('must not fire', () => {
    it('on DataverseLookupField usage (the sanctioned side-pane field)', () => {
      const source = `import { DataverseLookupField } from '../LookupField';\nconst a = <DataverseLookupField entityType="contact" navigationService={nav} />;`;
      expect(findViolations([{ file: step, source }])).toEqual([]);
    });

    it('on the inline LookupField name only in a comment, and on a type or hook that contains the name', () => {
      const source = [
        '// Falls back to inline <LookupField when absent',
        '/* <LookupField was replaced by DataverseLookupField */',
        'type P = { renderer: ILookupFieldProps };',
        'const a = <LookupFieldHint />;',
      ].join('\n');
      expect(findViolations([{ file: step, source }])).toEqual([]);
    });

    it('on DataverseLookupField reached through a namespace import', () => {
      const source = `import * as Lookups from '../LookupField';
const a = <Lookups.DataverseLookupField entityType="contact" />;`;
      expect(findViolations([{ file: step, source }])).toEqual([]);
    });

    it('on the word LookupField only inside a string, a template text or JSX attribute text', () => {
      const source = [
        "const hint = 'Use <LookupField> for people';",
        'const t = `render <LookupField /> here`;',
        'const a = <Hint text="<LookupField />" />;',
        'const c = React.createElement(DataverseLookupField, {});',
      ].join('\n');
      expect(findViolations([{ file: step, source }])).toEqual([]);
    });

    it('on an import of LookupField that is never used as a value', () => {
      const source = `import { LookupField, type ILookupFieldProps } from '../LookupField';\nexport type P = ILookupFieldProps;`;
      expect(findViolations([{ file: step, source }])).toEqual([]);
    });

    it('on code that follows a // or /* held inside a string (the string does not start a comment)', () => {
      const source = `const a = <input accept="image/*" placeholder="a // b" />;\nconst b = <DataverseLookupField entityType="contact" />;\n/* later */`;
      expect(findViolations([{ file: step, source }])).toEqual([]);
    });

    it('on an aliased DataverseLookupField', () => {
      const source = `import { DataverseLookupField as Pane } from '../LookupField';\nconst a = <Pane entityType="contact" />;`;
      expect(findViolations([{ file: step, source }])).toEqual([]);
    });

    it("on a bare 'EmailStep' string (a step id) that is not a module reference", () => {
      const source = [
        "const steps = [{ id: 'EmailStep', label: 'Email' }];",
        'const current = "EmailStep";',
        "export const isEmail = (id: string) => id === 'EmailStep';",
      ].join('\n');
      expect(findViolations([{ file: step, source }])).toEqual([]);
    });

    it('on a SendEmailStep that is declared but not exported, and on a re-export of the one binding', () => {
      const result = findViolations([
        {
          file: 'EmailComposer/wrappers/SendEmailStep.tsx',
          source: 'export const SendEmailStep = forwardRef(() => null);',
        },
        {
          file: 'Other/Local.tsx',
          source: 'function SendEmailStep() { return null; }\nexport const X = SendEmailStep;',
        },
        {
          file: 'Other/Reexport.ts',
          source:
            "export { SendEmailStep as default } from '../EmailComposer';\nexport { SendEmailStep } from '../EmailComposer';",
        },
      ]);
      expect(result).toEqual([]);
    });

    it('on the composer, DocumentEmailStep and a path that merely contains the letters EmailStep', () => {
      const source = [
        "import { EmailComposer } from '../EmailComposer';",
        "import { DocumentEmailStep } from './DocumentEmailStep';",
        "import { SendEmailFollowOnStep } from './steps/SendEmailFollowOnStep';",
      ].join('\n');
      expect(findViolations([{ file: step, source }])).toEqual([]);
    });

    it('on an inline LookupField outside the wizard folders (record header, the field itself, decision wizard)', () => {
      const source = 'const a = <LookupField />;';
      for (const file of [
        'RecordHeader/FieldGrid.tsx',
        'LookupField/DataverseLookupField.tsx',
        'DecisionWizard/steps/ParameterFields.tsx',
      ]) {
        expect(findViolations([{ file, source }])).toEqual([]);
      }
    });

    it('on a test file under a wizard folder', () => {
      const source = 'const a = <LookupField />; // asserts the fallback';
      expect(findViolations([{ file: 'CreateSeededWizard/__tests__/Step.test.tsx', source }])).toEqual([]);
      expect(findViolations([{ file: 'CreateSeededWizard/Step.test.tsx', source }])).toEqual([]);
    });

    it('on a single SendEmailStep definition and on a re-export of it', () => {
      const result = findViolations([
        {
          file: 'EmailComposer/wrappers/SendEmailStep.tsx',
          source: 'export const SendEmailStep = forwardRef(() => null);',
        },
        { file: 'EmailComposer/index.ts', source: "export { SendEmailStep } from './wrappers/SendEmailStep';" },
      ]);
      expect(result).toEqual([]);
    });
  });

  describe('the comment walk', () => {
    it('keeps a // and a /* that sit inside strings, and drops real comments', () => {
      const out = stripComments(`const a = "x // y"; // gone\nconst b = 'image/*'; /* gone */ const c = 1;`);
      expect(out).toContain('"x // y"');
      expect(out).toContain("'image/*'");
      expect(out).not.toContain('gone');
      expect(out).toContain('const c = 1;');
    });

    it('keeps the line count (a comment becomes spaces, newlines stay)', () => {
      const source = 'a\n/* one\ntwo */\nb // x\nc';
      expect(stripComments(source).split('\n')).toHaveLength(source.split('\n').length);
    });

    it('walks a template literal: text is a string, ${...} is code', () => {
      const out = codeOnly('const t = `http://x ${a /* c */ + b} // y`; const z = 1;');
      expect(out).not.toContain('http');
      expect(out).toContain('a ');
      expect(out).toContain('const z = 1;');
    });

    it('blanks string text but keeps the quotes', () => {
      expect(codeOnly(`const a = "abc"; const b = 'd';`)).toBe(`const a = "   "; const b = ' ';`);
    });

    it('ends an unterminated quote string at the line end', () => {
      expect(codeOnly("const a = <p>Don't</p>;\nconst b = 2;")).toContain('const b = 2;');
    });
  });

  describe('the ratchet', () => {
    const v: IViolation = { rule: 'inline-lookup', file: 'CreateSeededWizard/Old.tsx' };

    it('passes a violation that is in the baseline', () => {
      expect(applyBaseline([v], [v])).toEqual({ unlisted: [], stale: [] });
    });

    it('fails a new violation that is not in the baseline', () => {
      expect(applyBaseline([v], [])).toEqual({ unlisted: ['inline-lookup CreateSeededWizard/Old.tsx'], stale: [] });
    });

    it('fails a baseline entry that no longer violates', () => {
      expect(applyBaseline([], [v])).toEqual({ unlisted: [], stale: ['inline-lookup CreateSeededWizard/Old.tsx'] });
    });

    it('lists a violation by rule, so a file in the baseline for one rule is not excused for another', () => {
      const other: IViolation = { rule: 'email-step', file: v.file };
      expect(applyBaseline([v, other], [v])).toEqual({
        unlisted: ['email-step CreateSeededWizard/Old.tsx'],
        stale: [],
      });
    });
  });

  describe('the scope', () => {
    it.each([
      ['CreateMatterWizard/CreateRecordStep.tsx', true],
      ['CreateWorkAssignmentWizard/AssignWorkStep.tsx', true],
      ['CreateRecordWizard/steps/Anything.tsx', true],
      ['WizardFollowOns/steps/AssignWorkFollowOnStep.tsx', true],
      ['WizardFollowOns/steps/__tests__/AssignWorkFollowOnStep.test.tsx', false],
      ['CreateMatterWizard/__tests__/CreateMatterWizard.projectFiling.test.tsx', false],
      ['CreateMatterWizard/README.md', false],
      ['DocumentEmailWizard/DocumentEmailWizard.tsx', false],
      ['LookupField/LookupField.tsx', false],
    ])('%s guarded: %s', (file, guarded) => {
      expect(isGuardedWizardFile(file)).toBe(guarded);
    });
  });
});
