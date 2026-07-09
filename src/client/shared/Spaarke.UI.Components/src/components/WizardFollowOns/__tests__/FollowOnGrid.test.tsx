/**
 * FollowOnGrid — config-driven card grid → dynamic step wiring.
 *
 * Behavioral, DOM-level tests (ADR-038): render an arbitrary FollowOnCardConfig[],
 * exercise real card clicks, and assert the grid drives the wizard shell's
 * addDynamicStep/removeDynamicStep handles and reports selection. No mock of
 * internals, no DI-registration test, no ctor null-check test — the two shell
 * handles are real injected collaborators represented by jest.fn() spies.
 *
 * @see FollowOnGrid.tsx
 */
import * as React from 'react';
import { screen, fireEvent } from '@testing-library/react';

import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { FollowOnGrid } from '../FollowOnGrid';
import { followOnStepId } from '../followOnTypes';
import type { FollowOnCardConfig } from '../followOnTypes';

/** Three arbitrary cards, incl. a NON-canonical id ("custom-thing") to prove
 *  the grid is not limited to the canonical FollowOnId union. */
function buildCards(): FollowOnCardConfig[] {
  return [
    {
      id: 'send-email',
      label: 'Send Email',
      icon: <span data-testid="icon-email" />,
      description: 'Compose a notification email.',
      renderStep: () => <div>email step body</div>,
      canAdvance: () => false,
    },
    {
      id: 'add-todo',
      label: 'Add To Do',
      icon: <span data-testid="icon-todo" />,
      description: 'Create a to do linked to this record.',
      renderStep: () => <div>todo step body</div>,
    },
    {
      id: 'custom-thing',
      label: 'Custom Thing',
      icon: <span data-testid="icon-custom" />,
      description: 'A wizard-specific follow-on card.',
      stepLabel: 'Custom',
      isSkippable: true,
      renderStep: () => <div>custom step body</div>,
    },
  ];
}

/**
 * Controlled harness: holds `selected` state so a card click round-trips
 * through onSelectionChange exactly as a real wizard would drive it.
 */
const Harness: React.FC<{
  cards: FollowOnCardConfig[];
  addDynamicStep: jest.Mock;
  removeDynamicStep: jest.Mock;
  onChangeSpy?: jest.Mock;
}> = ({ cards, addDynamicStep, removeDynamicStep, onChangeSpy }) => {
  const [selected, setSelected] = React.useState<string[]>([]);
  return (
    <FollowOnGrid
      cards={cards}
      selected={selected}
      onSelectionChange={next => {
        onChangeSpy?.(next);
        setSelected(next);
      }}
      addDynamicStep={addDynamicStep}
      removeDynamicStep={removeDynamicStep}
    />
  );
};

describe('FollowOnGrid', () => {
  it('renders an arbitrary FollowOnCardConfig[] — every card label + the empty-selection hint', () => {
    renderWithProviders(<Harness cards={buildCards()} addDynamicStep={jest.fn()} removeDynamicStep={jest.fn()} />);

    expect(screen.getByText('Send Email')).toBeInTheDocument();
    expect(screen.getByText('Add To Do')).toBeInTheDocument();
    expect(screen.getByText('Custom Thing')).toBeInTheDocument();
    // Zero selected → early-finish hint is shown.
    expect(screen.getByText(/click Finish to create the record without follow-on steps/i)).toBeInTheDocument();
  });

  it('selecting a card adds its dynamic step (derived id + card-array canonical order) and reports selection', () => {
    const addDynamicStep = jest.fn();
    const removeDynamicStep = jest.fn();
    const onChangeSpy = jest.fn();
    const cards = buildCards();

    renderWithProviders(
      <Harness
        cards={cards}
        addDynamicStep={addDynamicStep}
        removeDynamicStep={removeDynamicStep}
        onChangeSpy={onChangeSpy}
      />
    );

    // Click the "Add To Do" card.
    fireEvent.click(screen.getByRole('checkbox', { name: /Add To Do/i }));

    expect(addDynamicStep).toHaveBeenCalledTimes(1);
    const [stepConfig, canonicalOrder] = addDynamicStep.mock.calls[0];
    expect(stepConfig.id).toBe(followOnStepId('add-todo')); // "followon-add-todo"
    expect(stepConfig.label).toBe('Add To Do');
    expect(typeof stepConfig.renderContent).toBe('function');
    // Default advancement predicate is always-true when the card omits canAdvance.
    expect(stepConfig.canAdvance()).toBe(true);
    // Canonical order defaults to the card-array order mapped to step ids.
    expect(canonicalOrder).toEqual([
      followOnStepId('send-email'),
      followOnStepId('add-todo'),
      followOnStepId('custom-thing'),
    ]);

    // Selection reported in card-array order.
    expect(onChangeSpy).toHaveBeenLastCalledWith(['add-todo']);
    // Card now reflects selected state; hint is gone.
    expect(screen.getByRole('checkbox', { name: /Add To Do/i })).toHaveAttribute('aria-checked', 'true');
    expect(screen.queryByText(/click Finish to create the record without follow-on steps/i)).not.toBeInTheDocument();
  });

  it('carries per-card step overrides (stepLabel + isSkippable + canAdvance) into the injected step config', () => {
    const addDynamicStep = jest.fn();
    const cards = buildCards();

    renderWithProviders(<Harness cards={cards} addDynamicStep={addDynamicStep} removeDynamicStep={jest.fn()} />);

    // "Custom Thing" has stepLabel + isSkippable; "Send Email" has canAdvance:()=>false.
    fireEvent.click(screen.getByRole('checkbox', { name: /Custom Thing/i }));
    const [customConfig] = addDynamicStep.mock.calls[0];
    expect(customConfig.id).toBe(followOnStepId('custom-thing'));
    expect(customConfig.label).toBe('Custom'); // stepLabel override
    expect(customConfig.isSkippable).toBe(true);

    fireEvent.click(screen.getByRole('checkbox', { name: /Send Email/i }));
    const [emailConfig] = addDynamicStep.mock.calls[1];
    expect(emailConfig.canAdvance()).toBe(false); // required-field gate
  });

  it('deselecting a card removes its dynamic step and trims the selection', () => {
    const addDynamicStep = jest.fn();
    const removeDynamicStep = jest.fn();
    const onChangeSpy = jest.fn();
    const cards = buildCards();

    renderWithProviders(
      <Harness
        cards={cards}
        addDynamicStep={addDynamicStep}
        removeDynamicStep={removeDynamicStep}
        onChangeSpy={onChangeSpy}
      />
    );

    const todoCard = () => screen.getByRole('checkbox', { name: /Add To Do/i });

    fireEvent.click(todoCard()); // select
    expect(onChangeSpy).toHaveBeenLastCalledWith(['add-todo']);

    fireEvent.click(todoCard()); // deselect
    expect(removeDynamicStep).toHaveBeenCalledWith(followOnStepId('add-todo'));
    expect(onChangeSpy).toHaveBeenLastCalledWith([]);
    // Back to zero selection → hint returns.
    expect(screen.getByText(/click Finish to create the record without follow-on steps/i)).toBeInTheDocument();
  });

  it("renders a card's renderSelectedExtra inline only while that card is selected", () => {
    const cards: FollowOnCardConfig[] = [
      {
        id: 'send-email',
        label: 'Send Email',
        icon: <span />,
        description: 'Email.',
        renderStep: () => <div>email</div>,
        renderSelectedExtra: () => <div data-testid="short-summary-toggle">include only short summary</div>,
      },
    ];

    renderWithProviders(<Harness cards={cards} addDynamicStep={jest.fn()} removeDynamicStep={jest.fn()} />);

    // Not selected → extra absent.
    expect(screen.queryByTestId('short-summary-toggle')).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('checkbox', { name: /Send Email/i }));

    // Selected → extra visible (SummarizeFiles' inline toggle pattern, as config).
    expect(screen.getByTestId('short-summary-toggle')).toBeInTheDocument();
  });
});
