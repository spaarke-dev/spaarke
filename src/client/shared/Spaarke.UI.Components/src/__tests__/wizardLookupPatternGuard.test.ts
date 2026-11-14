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
 *   1. `inline-lookup`  - the file renders the inline `LookupField` (`<LookupField`, or a JSX alias of that named import).
 *                         Comments are ignored; `<DataverseLookupField` is the sanctioned component and never matches.
 *   2. `email-step`     - the file imports a module path ending in `EmailStep` (`../EmailStep`, `../../EmailStep/index`, ...).
 *                         `DocumentEmailStep` and the composer wrappers do not match.
 * and, over every non-test source in the package:
 *   3. `send-email-step` - exactly one module defines an export named `SendEmailStep` (the composer wrapper). The old step
 *                         shared the name, so a consumer importing `SendEmailStep` from the package got whichever won.
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

/** Drops block and line comments so prose that mentions `<LookupField` does not count. Strings are kept. */
export function stripComments(source: string): string {
  return source.replace(/\/\*[\s\S]*?\*\//g, ' ').replace(/(^|[\s;{}()])\/\/[^\n]*/gm, '$1');
}

/** True when the file renders the inline `LookupField`, directly or through a named-import alias. */
export function rendersInlineLookupField(source: string): boolean {
  const code = stripComments(source);
  if (/<LookupField(?![A-Za-z0-9_$])/.test(code)) return true;
  // `import * as Lookups` then `<Lookups.LookupField`.
  if (/<[A-Za-z_$][A-Za-z0-9_$]*\.LookupField(?![A-Za-z0-9_$])/.test(code)) return true;
  // `import { LookupField as Foo }`: `\bLookupField` does not match inside `DataverseLookupField`.
  const aliases = [...code.matchAll(/(?<![A-Za-z0-9_$])LookupField\s+as\s+([A-Za-z_$][A-Za-z0-9_$]*)/g)].map(m => m[1]);
  return aliases.some(alias => new RegExp(`<${alias.replace(/\$/g, '\\$')}(?![A-Za-z0-9_$])`).test(code));
}

/** True when the file names a module path that ends in `EmailStep` (import, export-from, dynamic import, jest.mock, require). */
export function importsEmailStep(source: string): boolean {
  const code = stripComments(source);
  return /['"][^'"\n]*(?<![A-Za-z0-9_$])EmailStep(?:\/[^'"\n]*)?['"]/.test(code);
}

/** True when the file defines (not merely re-exports) a symbol named `SendEmailStep`. */
export function definesSendEmailStep(source: string): boolean {
  return /export\s+(?:default\s+)?(?:const|let|var|function|class)\s+SendEmailStep(?![A-Za-z0-9_$])/.test(
    stripComments(source)
  );
}

/** Rules 1 and 2 over the wizard folders, rule 3 over every file given. Sorted for a stable report. */
export function findViolations(files: readonly ISourceFile[]): IViolation[] {
  const found: IViolation[] = [];
  for (const f of files) {
    if (isGuardedWizardFile(f.file)) {
      if (rendersInlineLookupField(f.source)) found.push({ rule: 'inline-lookup', file: f.file });
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
      ["import { SendEmailStep } from '../EmailStep';"],
      ["import { SendEmailStep } from '../../EmailStep/index';"],
      ["export { SendEmailStep } from '../EmailStep/SendEmailStep';"],
      ["const m = await import('../EmailStep');"],
      ["jest.mock('../../EmailStep/SendEmailStep', () => ({}));"],
    ])('on a seeded EmailStep import: %s', source => {
      expect(findViolations([{ file: step, source }])).toEqual([{ rule: 'email-step', file: step }]);
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

    it('on an aliased DataverseLookupField', () => {
      const source = `import { DataverseLookupField as Pane } from '../LookupField';\nconst a = <Pane entityType="contact" />;`;
      expect(findViolations([{ file: step, source }])).toEqual([]);
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
