/**
 * WizardModal.test.tsx — thin `SprkModal` preset for wizard chrome (spec FR-09).
 * Verifies the stepper sidebar renders done/active/pending state, the footer
 * shows the Cancel · Skip · Back · Next(/Finish) button set with the right
 * enable/disable + label rules, and the nav callbacks fire correctly.
 */
import * as React from 'react';
import { fireEvent, screen, within } from '@testing-library/react';
import { renderWithProviders } from '../../../../__mocks__/pcfMocks';
import { WizardModal } from '../WizardModal';

const noop = () => {};

describe('WizardModal (preset — FR-09)', () => {
  it('renders the stepper with all step labels', () => {
    renderWithProviders(
      <WizardModal
        open
        onClose={noop}
        title="Create New Matter"
        steps={['Details', 'Parties', 'Review']}
        active={1}
        onBack={noop}
        onNext={noop}
      >
        <div>step content</div>
      </WizardModal>,
    );
    const dialog = screen.getByRole('dialog');
    expect(within(dialog).getByText('Details')).toBeInTheDocument();
    expect(within(dialog).getByText('Parties')).toBeInTheDocument();
    expect(within(dialog).getByText('Review')).toBeInTheDocument();
  });

  it('marks the active step distinctly from done/pending steps', () => {
    renderWithProviders(
      <WizardModal
        open
        onClose={noop}
        title="Create New Matter"
        steps={['Details', 'Parties', 'Review']}
        active={1}
        onBack={noop}
        onNext={noop}
      >
        <div>step content</div>
      </WizardModal>,
    );
    const dialog = screen.getByRole('dialog');
    // Each step row wraps its label in a `<div>` carrying the step/stepActive classes;
    // the active row's wrapper class differs from the done/pending rows' (bold + foreground1).
    const activeRow = within(dialog).getByText('Parties').closest('div');
    const doneRow = within(dialog).getByText('Details').closest('div');
    const pendingRow = within(dialog).getByText('Review').closest('div');
    expect(activeRow?.className).not.toEqual(doneRow?.className);
    expect(activeRow?.className).not.toEqual(pendingRow?.className);
  });

  it('renders the wizard size', () => {
    renderWithProviders(
      <WizardModal
        open
        onClose={noop}
        title="Create New Matter"
        steps={['Details', 'Parties', 'Review']}
        active={0}
        onBack={noop}
        onNext={noop}
      >
        <div>step content</div>
      </WizardModal>,
    );
    // `wizard` size is a fixed 62vw/74vh spec (sizes.ts) — smoke-check the dialog rendered.
    expect(screen.getByRole('dialog')).toBeInTheDocument();
  });

  it('footer shows Cancel, Back, and Next (no Skip when onSkip is not supplied)', () => {
    renderWithProviders(
      <WizardModal
        open
        onClose={noop}
        title="Create New Matter"
        steps={['Details', 'Parties', 'Review']}
        active={1}
        onBack={noop}
        onNext={noop}
      >
        <div>step content</div>
      </WizardModal>,
    );
    const dialog = screen.getByRole('dialog');
    expect(within(dialog).getByRole('button', { name: /^cancel$/i })).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: /^back$/i })).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: /^next$/i })).toBeInTheDocument();
    expect(within(dialog).queryByRole('button', { name: /^skip$/i })).toBeNull();
  });

  it('footer shows Skip when onSkip is supplied and not on the last step', () => {
    renderWithProviders(
      <WizardModal
        open
        onClose={noop}
        title="Create New Matter"
        steps={['Details', 'Parties', 'Review']}
        active={1}
        onBack={noop}
        onNext={noop}
        onSkip={noop}
      >
        <div>step content</div>
      </WizardModal>,
    );
    const dialog = screen.getByRole('dialog');
    expect(within(dialog).getByRole('button', { name: /^skip$/i })).toBeInTheDocument();
  });

  it('hides Skip on the last step even when onSkip is supplied', () => {
    renderWithProviders(
      <WizardModal
        open
        onClose={noop}
        title="Create New Matter"
        steps={['Details', 'Parties', 'Review']}
        active={2}
        onBack={noop}
        onNext={noop}
        onSkip={noop}
      >
        <div>step content</div>
      </WizardModal>,
    );
    const dialog = screen.getByRole('dialog');
    expect(within(dialog).queryByRole('button', { name: /^skip$/i })).toBeNull();
  });

  it('disables Back at step 0', () => {
    renderWithProviders(
      <WizardModal
        open
        onClose={noop}
        title="Create New Matter"
        steps={['Details', 'Parties', 'Review']}
        active={0}
        onBack={noop}
        onNext={noop}
      >
        <div>step content</div>
      </WizardModal>,
    );
    const dialog = screen.getByRole('dialog');
    expect(within(dialog).getByRole('button', { name: /^back$/i })).toBeDisabled();
  });

  it('enables Back after step 0', () => {
    renderWithProviders(
      <WizardModal
        open
        onClose={noop}
        title="Create New Matter"
        steps={['Details', 'Parties', 'Review']}
        active={1}
        onBack={noop}
        onNext={noop}
      >
        <div>step content</div>
      </WizardModal>,
    );
    const dialog = screen.getByRole('dialog');
    expect(within(dialog).getByRole('button', { name: /^back$/i })).toBeEnabled();
  });

  it('the primary button reads "Finish" on the last step', () => {
    renderWithProviders(
      <WizardModal
        open
        onClose={noop}
        title="Create New Matter"
        steps={['Details', 'Parties', 'Review']}
        active={2}
        onBack={noop}
        onNext={noop}
      >
        <div>step content</div>
      </WizardModal>,
    );
    const dialog = screen.getByRole('dialog');
    expect(within(dialog).getByRole('button', { name: /^finish$/i })).toBeInTheDocument();
    expect(within(dialog).queryByRole('button', { name: /^next$/i })).toBeNull();
  });

  it('the primary button reads "Next" before the last step', () => {
    renderWithProviders(
      <WizardModal
        open
        onClose={noop}
        title="Create New Matter"
        steps={['Details', 'Parties', 'Review']}
        active={0}
        onBack={noop}
        onNext={noop}
      >
        <div>step content</div>
      </WizardModal>,
    );
    const dialog = screen.getByRole('dialog');
    expect(within(dialog).getByRole('button', { name: /^next$/i })).toBeInTheDocument();
  });

  it('clicking Back fires onBack', () => {
    const onBack = jest.fn();
    renderWithProviders(
      <WizardModal
        open
        onClose={noop}
        title="Create New Matter"
        steps={['Details', 'Parties', 'Review']}
        active={1}
        onBack={onBack}
        onNext={noop}
      >
        <div>step content</div>
      </WizardModal>,
    );
    fireEvent.click(screen.getByRole('button', { name: /^back$/i }));
    expect(onBack).toHaveBeenCalledTimes(1);
  });

  it('clicking Next fires onNext', () => {
    const onNext = jest.fn();
    renderWithProviders(
      <WizardModal
        open
        onClose={noop}
        title="Create New Matter"
        steps={['Details', 'Parties', 'Review']}
        active={1}
        onBack={noop}
        onNext={onNext}
      >
        <div>step content</div>
      </WizardModal>,
    );
    fireEvent.click(screen.getByRole('button', { name: /^next$/i }));
    expect(onNext).toHaveBeenCalledTimes(1);
  });

  it('clicking Skip fires onSkip', () => {
    const onSkip = jest.fn();
    renderWithProviders(
      <WizardModal
        open
        onClose={noop}
        title="Create New Matter"
        steps={['Details', 'Parties', 'Review']}
        active={1}
        onBack={noop}
        onNext={noop}
        onSkip={onSkip}
      >
        <div>step content</div>
      </WizardModal>,
    );
    fireEvent.click(screen.getByRole('button', { name: /^skip$/i }));
    expect(onSkip).toHaveBeenCalledTimes(1);
  });

  it('clicking Cancel fires onClose', () => {
    const onClose = jest.fn();
    renderWithProviders(
      <WizardModal
        open
        onClose={onClose}
        title="Create New Matter"
        steps={['Details', 'Parties', 'Review']}
        active={1}
        onBack={noop}
        onNext={noop}
      >
        <div>step content</div>
      </WizardModal>,
    );
    fireEvent.click(screen.getByRole('button', { name: /^cancel$/i }));
    expect(onClose).toHaveBeenCalledTimes(1);
  });
});
