/**
 * Guard: every wizard that renders the Send Email follow-on step cleans up the step's uploads
 * (spaarke-ontology-platform-r1 task 220, #2813; review round 2 F4-5).
 *
 * Why. The step uploads a local file as a `sprk_document` at once. Two lines in the wizard that holds the step's
 * `IComposedEmail` decide whether those documents are removed when the email is not sent, and whether a Finish can lose
 * them to an early close:
 *   1. `useDiscardComposedEmailUploadsOnClose(open, emailRef)` — delete the step's unsent uploads when the wizard closes;
 *   2. `holdComposedEmailUploadsDuring(emailRef.current, …)` — hold them from the first moment of Finish (record creation
 *      runs before the send, and the window × is live meanwhile).
 * A new wizard that renders the step without them leaks every upload, or deletes documents a sent email names. This scan
 * fails the build when either line is missing.
 *
 * What it checks: every non-test `.tsx` under Spaarke.UI.Components `src/components/` and Spaarke.AI.Widgets `src/` that
 * renders `<SendEmailFollowOnStep` (comments ignored) calls both. Must-fire and must-not-fire controls run on synthetic
 * sources; the live scan must find the known hosts, so a scan that finds nothing cannot pass silently.
 *
 * Enforced by: this test (the package's jest suite).
 */
import * as fs from 'fs';
import * as path from 'path';

export interface ISourceFile {
  file: string;
  source: string;
}

const STEP_DEFINITION = /WizardFollowOns\/steps\/SendEmailFollowOnStep\.tsx$/;
const TEST_FILE = /(^|\/)__tests__\/|\.(test|spec)\.(ts|tsx)$/;
const REQUIRED = ['useDiscardComposedEmailUploadsOnClose(', 'holdComposedEmailUploadsDuring('] as const;

/** The source with `//` and block comments removed (good enough here: neither pattern appears in a string). */
function withoutComments(source: string): string {
  return source.replace(/\/\*[\s\S]*?\*\//g, '').replace(/(^|[^:])\/\/.*$/gm, '$1');
}

/** Files that render the step but miss a cleanup line, with what is missing. */
export function findViolations(files: readonly ISourceFile[]): Array<{ file: string; missing: string[] }> {
  const out: Array<{ file: string; missing: string[] }> = [];
  for (const f of files) {
    if (STEP_DEFINITION.test(f.file) || TEST_FILE.test(f.file)) continue;
    const code = withoutComments(f.source);
    if (!code.includes('<SendEmailFollowOnStep')) continue;
    const missing = REQUIRED.filter(r => !code.includes(r));
    if (missing.length > 0) out.push({ file: f.file, missing: [...missing] });
  }
  return out;
}

/** Hosts that render the step, for the "the scan found them" check. */
export function stepHosts(files: readonly ISourceFile[]): string[] {
  return files
    .filter(f => !STEP_DEFINITION.test(f.file) && !TEST_FILE.test(f.file))
    .filter(f => withoutComments(f.source).includes('<SendEmailFollowOnStep'))
    .map(f => f.file)
    .sort();
}

function readTree(root: string, label: string): ISourceFile[] {
  const out: ISourceFile[] = [];
  const walk = (dir: string) => {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      if (entry.name === 'node_modules' || entry.name === 'dist') continue;
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) walk(full);
      else if (entry.name.endsWith('.tsx')) {
        out.push({
          file: `${label}/${path.relative(root, full).split(path.sep).join('/')}`,
          source: fs.readFileSync(full, 'utf8'),
        });
      }
    }
  };
  if (fs.existsSync(root)) walk(root);
  return out;
}

describe('guard: wizards that render the Send Email step clean up its uploads (task 220)', () => {
  // Controls
  it('fires on a host that renders the step without the close hook or the Finish hold', () => {
    const violations = findViolations([
      { file: 'X/NewWizard.tsx', source: 'const a = <SendEmailFollowOnStep value={v} onChange={c} />;' },
      {
        file: 'X/HalfWizard.tsx',
        source:
          'useDiscardComposedEmailUploadsOnClose(open, emailRef);\n// holdComposedEmailUploadsDuring(\n<SendEmailFollowOnStep />',
      },
    ]);
    expect(violations).toEqual([
      { file: 'X/NewWizard.tsx', missing: [...REQUIRED] },
      { file: 'X/HalfWizard.tsx', missing: ['holdComposedEmailUploadsDuring('] },
    ]);
  });

  it('does not fire on a wired host, the step itself, a test, or a file that only mentions the step in a comment', () => {
    expect(
      findViolations([
        {
          file: 'X/Wired.tsx',
          source:
            'useDiscardComposedEmailUploadsOnClose(open, emailRef);\nonFinish={() => holdComposedEmailUploadsDuring(emailRef.current, f)}\n<SendEmailFollowOnStep />',
        },
        { file: 'WizardFollowOns/steps/SendEmailFollowOnStep.tsx', source: '<SendEmailFollowOnStep />' },
        { file: 'X/__tests__/Thing.test.tsx', source: '<SendEmailFollowOnStep />' },
        { file: 'X/Doc.tsx', source: '/* renders <SendEmailFollowOnStep /> elsewhere */' },
      ])
    ).toEqual([]);
  });

  // Live scan
  it('every wizard that renders the step is wired', () => {
    const components = path.resolve(__dirname, '../components');
    const aiWidgets = path.resolve(__dirname, '../../../Spaarke.AI.Widgets/src');
    const files = [...readTree(components, 'ui-components'), ...readTree(aiWidgets, 'ai-widgets')];

    expect(stepHosts(files)).toEqual(
      expect.arrayContaining([
        'ui-components/CreateRecordWizard/CreateRecordWizard.tsx',
        'ui-components/CreateReportCardWizard/CreateReportCardWizard.tsx',
        'ui-components/CreateWorkAssignmentWizard/WorkAssignmentWizardDialog.tsx',
        'ui-components/DocumentEmailWizard/DocumentEmailWizard.tsx',
        'ui-components/SummarizeFilesWizard/SummarizeFilesDialog.tsx',
        'ai-widgets/widgets/workspace/CreateAnalysisWizardWidget.tsx',
      ])
    );
    expect(findViolations(files)).toEqual([]);
  });
});
