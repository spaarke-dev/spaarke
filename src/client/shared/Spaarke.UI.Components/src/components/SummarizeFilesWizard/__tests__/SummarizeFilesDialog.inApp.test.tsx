/**
 * SummarizeFilesDialog.inApp.test.tsx — the props Summarize Files hands its shell when hosted in-app
 * (spaarke-ontology-platform-r1 task 113; D-26; ADR-050 as amended: named size, explicit dismiss, `uiScale`).
 *
 * `InAppWizardHost` mounts the dialog non-embedded; the dialog must forward the app-shell `uiScale` to
 * `WizardShell` (which realises it on `SprkModal`) and must NOT override the shell's named-size / explicit-dismiss
 * defaults. The code page (embedded) passes no `uiScale`, so that path is unchanged.
 *
 * Classification (ADR-038 §7): MAINTAIN — pins the ADR-050 hosting contract for a shipped wizard.
 */
import * as React from 'react';
import { render } from '@testing-library/react';

const mockShell: { props: any } = { props: null };
jest.mock('../../Wizard/WizardShell', () => ({
  WizardShell: React.forwardRef((props: any, _ref: unknown) => {
    mockShell.props = props;
    return <div data-testid="shell" />;
  }),
}));

import { SummarizeFilesDialog } from '../SummarizeFilesDialog';

beforeEach(() => {
  mockShell.props = null;
});

describe('SummarizeFilesDialog — in-app hosting props', () => {
  it('forwards uiScale to the shell and leaves size / dismiss / legacy sizing to the shell defaults', () => {
    render(<SummarizeFilesDialog open onClose={jest.fn()} embedded={false} uiScale={1.5} authenticatedFetch={jest.fn()} />);

    expect(mockShell.props.uiScale).toBe(1.5);
    expect(mockShell.props.embedded).toBe(false);
    expect(mockShell.props.hideTitle).toBe(false);
    expect(mockShell.props).not.toHaveProperty('size');
    expect(mockShell.props).not.toHaveProperty('dismiss');
    expect(mockShell.props).not.toHaveProperty('maxWidth');
    expect(mockShell.props).not.toHaveProperty('height');
  });

  it('the code-page embed passes no uiScale (unchanged)', () => {
    render(<SummarizeFilesDialog open onClose={jest.fn()} embedded authenticatedFetch={jest.fn()} />);

    expect(mockShell.props).not.toHaveProperty('uiScale');
    expect(mockShell.props.embedded).toBe(true);
    expect(mockShell.props.hideTitle).toBe(true);
  });
});
