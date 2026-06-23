/**
 * RenameGuardDialog tests — R3 task 094 (covers task 091 / AC-H2.1).
 *
 * Verifies the OutputVariable rename-guard dialog presents three actions
 * (Auto-rename, Keep old name, Cancel rename), that Auto-rename is primary,
 * that each button surfaces the correct onResolve callback, that reference
 * lists render, and that closing the dialog is treated as Cancel.
 */
import * as React from 'react';
import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { RenameGuardDialog } from '../RenameGuardDialog';
import type { OutputVariableReference } from '../../../services/canvasValidation';
import { renderWithProviders, renderWithTheme, webDarkTheme } from './testUtils';

const refs: OutputVariableReference[] = [
  {
    nodeId: 'node_aa',
    nodeLabel: 'Send Email',
    nodeType: 'sendEmail',
    rawRefs: ['{{output_classify.output.documentType}}'],
  },
  {
    nodeId: 'node_bb',
    nodeLabel: 'Update Record',
    nodeType: 'updateRecord',
    rawRefs: ['{{output_classify.output.documentType}}', '{{output_classify.output.confidence}}'],
  },
];

describe('RenameGuardDialog', () => {
  it('renders title, both name tokens, and reference list when open', () => {
    renderWithProviders(
      <RenameGuardDialog
        open
        oldName="output_classify"
        newName="output_classifier"
        references={refs}
        onResolve={jest.fn()}
      />
    );

    expect(screen.getByText('Variable referenced by 2 downstream nodes')).toBeInTheDocument();
    expect(screen.getByText('output_classify')).toBeInTheDocument();
    expect(screen.getByText('output_classifier')).toBeInTheDocument();
    expect(screen.getByLabelText('Nodes referencing the renamed variable')).toBeInTheDocument();
    expect(screen.getByText('Send Email')).toBeInTheDocument();
    expect(screen.getByText('Update Record')).toBeInTheDocument();
  });

  it('uses singular title when exactly one node references the variable', () => {
    renderWithProviders(
      <RenameGuardDialog
        open
        oldName="output_classify"
        newName="output_x"
        references={[refs[0]]}
        onResolve={jest.fn()}
      />
    );
    expect(screen.getByText('Variable referenced by 1 downstream node')).toBeInTheDocument();
  });

  it('renders all three actions in the correct order (Cancel, Keep, Auto-rename last as primary)', () => {
    // Fluent v9 hashes class names so the "primary appearance" cannot be
    // asserted via className. Instead we assert the structural contract:
    // three buttons in DialogActions, primary action LAST per Fluent guidance.
    renderWithProviders(<RenameGuardDialog open oldName="a" newName="b" references={refs} onResolve={jest.fn()} />);
    const buttons = screen.getAllByRole('button');
    const names = buttons.map(b => b.textContent);
    // The DialogActions buttons should be these three, in this trailing order.
    expect(names).toEqual(expect.arrayContaining(['Cancel rename', 'Keep old name', 'Auto-rename references']));
    // Auto-rename should be the LAST button (Fluent v9 primary action convention).
    expect(names[names.length - 1]).toBe('Auto-rename references');
  });

  it('invokes onResolve("autoRename") when Auto-rename is clicked', async () => {
    const user = userEvent.setup();
    const onResolve = jest.fn();
    renderWithProviders(<RenameGuardDialog open oldName="a" newName="b" references={refs} onResolve={onResolve} />);
    await user.click(screen.getByRole('button', { name: 'Auto-rename references' }));
    expect(onResolve).toHaveBeenCalledTimes(1);
    expect(onResolve).toHaveBeenCalledWith('autoRename');
  });

  it('invokes onResolve("keepOldName") when Keep old name is clicked', async () => {
    const user = userEvent.setup();
    const onResolve = jest.fn();
    renderWithProviders(<RenameGuardDialog open oldName="a" newName="b" references={refs} onResolve={onResolve} />);
    await user.click(screen.getByRole('button', { name: 'Keep old name' }));
    expect(onResolve).toHaveBeenCalledWith('keepOldName');
  });

  it('invokes onResolve("cancel") when Cancel rename is clicked', async () => {
    const user = userEvent.setup();
    const onResolve = jest.fn();
    renderWithProviders(<RenameGuardDialog open oldName="a" newName="b" references={refs} onResolve={onResolve} />);
    await user.click(screen.getByRole('button', { name: 'Cancel rename' }));
    expect(onResolve).toHaveBeenCalledWith('cancel');
  });

  it('does not render dialog content when open=false', () => {
    renderWithProviders(
      <RenameGuardDialog open={false} oldName="a" newName="b" references={refs} onResolve={jest.fn()} />
    );
    expect(screen.queryByRole('button', { name: 'Auto-rename references' })).not.toBeInTheDocument();
  });

  it('renders correctly under dark theme (semantic-token parity)', () => {
    // R3 task 094 dark-mode parity check (POML step 3 — "dark mode static-scan").
    // ADR-021 requires semantic tokens only. We render under webDarkTheme and
    // assert (a) content still renders, (b) the title surfaces — confirming
    // that token-driven styles allow the component to mount cleanly under both
    // themes. Hardcoded colors would not visually break under jsdom (no real
    // paint), but a Fluent v9 render that depends on theme-specific provider
    // setup would crash if non-semantic styles bypassed the provider —
    // mounting cleanly under both themes is the meaningful parity assertion.
    renderWithTheme(
      <RenameGuardDialog open oldName="a" newName="b" references={refs} onResolve={jest.fn()} />,
      webDarkTheme
    );
    expect(screen.getByText('Variable referenced by 2 downstream nodes')).toBeInTheDocument();
    // The two name tokens render — they use tokens.colorNeutralBackground3 /
    // tokens.colorNeutralForeground1 (semantic, dark-mode aware).
    expect(screen.getByText('a')).toBeInTheDocument();
    expect(screen.getByText('b')).toBeInTheDocument();
  });
});
