/**
 * ebilling-r1 task 130e-2, fix round 1 (F4, F5): the component is not rendered with events in this node-environment suite, so what it does on a
 * click is decided by the pure functions in `services/ruleForm.ts` and `services/firmDescription.ts` (tested in `ruleForm.test.ts`). This test
 * pins that the components CALL them where it matters, so that dropping the call (deleting the edit, the Save guard, the Discard dismissal or the
 * selection clear) fails here instead of passing silently. Source scan, in the style of the pin test against the C# source.
 */

import * as fs from 'fs';
import * as path from 'path';

const read = (rel: string) => fs.readFileSync(path.join(__dirname, '..', rel), 'utf8').replace(/\r\n/g, '\n');
const PANE = read('components/ProposalDetailPane.tsx');
const VIEW = read('components/RuleSetDetailView.tsx');

/** The text of `const name = ... {` up to its closing `  };` at two-space indent (a function the component defines). */
const fnBody = (source: string, name: string): string => new RegExp(`const ${name} = [^\\n]*\\{\\n([\\s\\S]*?)\\n  \\};`).exec(source)?.[1] ?? '';

describe('the scan itself (known limit: it is whitespace-sensitive)', () => {
  // Accepted: the patterns match the source as formatted today (prettier keeps it so). A reformat that moves a token fails a test below, which is the
  // safe direction (a failing scan is looked at; a scan that quietly matched nothing would not be). These controls show both sides.
  it('(control) it finds the wiring as written, and does not find it in a reformatted copy', () => {
    expect(/disabled=\{form\.saveDisabled\}[^\n]*data-testid="save-edit"/.test('<Button disabled={form.saveDisabled} onClick={go} data-testid="save-edit">')).toBe(true);
    expect(/disabled=\{form\.saveDisabled\}[^\n]*data-testid="save-edit"/.test('<Button disabled={ form.saveDisabled } onClick={go} data-testid="save-edit">')).toBe(false);
    expect(fnBody('  const save = async (): Promise<void> => {\n    await onSave(proposal, edit);\n  };', 'save')).toContain('onSave(');
    expect(fnBody('const save = async () => { await onSave(); };', 'save')).toBe('');
  });
});

describe('ProposalDetailPane uses the pure form state', () => {
  it('(control) the scan finds the functions it reads', () => {
    expect(fnBody(PANE, 'save')).toContain('onSave(');
    expect(fnBody(PANE, 'reset')).toContain('setParams(');
  });

  it('the edit it saves is the one computeRuleForm builds (which carries the firm description)', () => {
    expect(PANE).toMatch(/const form = computeRuleForm\(/);
    expect(PANE).toMatch(/const \{[^}]*\bedit\b[^}]*\} = form;/);
    expect(fnBody(PANE, 'save')).toMatch(/await onSave\(proposal, edit\);/);
  });

  it('the Save button is off by the form\'s saveDisabled (which holds the firm description block), and save() refuses a blocked text too', () => {
    expect(PANE).toMatch(/disabled=\{form\.saveDisabled\}[^\n]*data-testid="save-edit"/);
    expect(fnBody(PANE, 'save')).toMatch(/if \(firm\.blocked\) return;/);
  });

  it('Discard dismisses the shown refusals and restores the stored text (F4)', () => {
    expect(fnBody(PANE, 'reset')).toMatch(/dispatchFirm\(\{ type: 'reset', stored: proposal\.firmDescription, refusals: firmRefusals \}\)/);
  });

  it('typing dismisses them, and Remove on a Duplicate empties the text', () => {
    expect(PANE).toMatch(/dispatchFirm\(\{ type: 'edit', text: d\.value, refusals: firmRefusals \}\)/);
    expect(PANE).toMatch(/dispatchFirm\(\{ type: 'edit', text: '', refusals: firmRefusals \}\)/);
  });

  it('the shown refusals pass through shownRefusals (a dismissed set is not shown)', () => {
    expect(PANE).toMatch(/shownRefusals: shownRefusals\(firmRefusals, firmField\)/);
  });
});

describe('RuleSetDetailView keeps and clears the refusals', () => {
  it('a save\'s refusals are marked as from the edit, the others are not', () => {
    expect(VIEW).toMatch(/fail\(outcome\.failure, 'The rule was not saved', shown\.policyId, 'edit'\)/);
    expect(VIEW).toMatch(/fail\(outcome\.failure, `The rule was not \$\{verb\}`, shown\.policyId\);/);
    expect(VIEW).toMatch(/from: 'other', byRule: firmDescriptionRefusals\(result\.failure\)/);
  });

  it('selecting another rule drops a save\'s refusals', () => {
    expect(VIEW).toMatch(/onSelect=\{id => \{\s*setRefusals\(refusalsAfterSelect\);\s*setSelected\(id\);\s*\}\}/);
  });

  it('the pane is given the selected rule\'s refusals and the rule set name', () => {
    expect(VIEW).toMatch(/firmRefusals=\{proposal \? refusals\.byRule\[proposal\.policyId\] : undefined\}/);
    expect(VIEW).toMatch(/ruleSetName=\{summary\.name\}/);
  });
});
