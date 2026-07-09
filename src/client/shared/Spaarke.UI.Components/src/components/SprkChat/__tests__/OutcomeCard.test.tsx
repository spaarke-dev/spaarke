/**
 * Tests for the OutcomeCard component (spaarke-ai-architecture-redesign-r2 task 035 / FR-A1-06).
 *
 * KEEP rationale: anchors the client render contract of the server OutcomeCard v1 — the audience
 * split (user-facing summary shown, internal detail never rendered), the server-composed link as
 * a clickable button, next-step chips, the job-aware step strip, and the tolerant status reader.
 */

import * as React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { OutcomeCard, normalizeOutcomeStatus, type IOutcomeCard } from '../OutcomeCard';

const renderWithProvider = (ui: React.ReactElement) =>
  render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);

const baseCard: IOutcomeCard = {
  status: 'succeeded',
  summary: { userFacing: 'Task “Review NDA” was created.', internal: 'tell the user it is ready' },
};

describe('OutcomeCard', () => {
  it('renders the user-facing summary and never the internal/model-facing detail', () => {
    renderWithProvider(<OutcomeCard card={baseCard} />);

    expect(screen.getByText('Task “Review NDA” was created.')).toBeInTheDocument();
    expect(screen.queryByText('tell the user it is ready')).not.toBeInTheDocument();
  });

  it('renders the server-composed link as a button and invokes onOpenLink', () => {
    const onOpenLink = jest.fn();
    const card: IOutcomeCard = {
      ...baseCard,
      link: { url: 'https://org.crm.dynamics.com/main.aspx?pagetype=entityrecord', label: 'Open record', kind: 'record' },
    };

    renderWithProvider(<OutcomeCard card={card} onOpenLink={onOpenLink} />);

    const button = screen.getByRole('button', { name: /open record/i });
    fireEvent.click(button);
    expect(onOpenLink).toHaveBeenCalledWith(card.link);
  });

  it('renders next-step chips and invokes onNextStep when clicked', () => {
    const onNextStep = jest.fn();
    const card: IOutcomeCard = {
      ...baseCard,
      nextSteps: [
        { label: 'Analyze the document', actionKind: 'invoke_capability' },
        { label: 'Assign it', actionKind: 'invoke_capability' },
      ],
    };

    renderWithProvider(<OutcomeCard card={card} onNextStep={onNextStep} />);

    fireEvent.click(screen.getByRole('button', { name: /analyze the document/i }));
    expect(onNextStep).toHaveBeenCalledWith(card.nextSteps![0]);
  });

  it('renders next-step chips inert (disabled) when no onNextStep handler is supplied', () => {
    const card: IOutcomeCard = { ...baseCard, nextSteps: [{ label: 'Assign it', actionKind: 'invoke_capability' }] };

    renderWithProvider(<OutcomeCard card={card} />);

    expect(screen.getByRole('button', { name: /assign it/i })).toBeDisabled();
  });

  it('renders the job-aware step strip for an async completion', () => {
    const card: IOutcomeCard = {
      status: 'partial',
      summary: { userFacing: 'Document created; indexing in progress.' },
      completion: {
        mode: 'jobAware',
        steps: [
          { key: 'record', label: 'Record', status: 'succeeded' },
          { key: 'indexing', label: 'Indexing', status: 'running' },
        ],
      },
    };

    renderWithProvider(<OutcomeCard card={card} />);

    expect(screen.getByTestId('outcome-card-steps')).toBeInTheDocument();
    expect(screen.getByText('Record')).toBeInTheDocument();
    expect(screen.getByText('Indexing')).toBeInTheDocument();
    expect(screen.getByText('In progress')).toBeInTheDocument();
  });

  it('tolerates a numeric status ordinal from the wire', () => {
    expect(normalizeOutcomeStatus(0)).toBe('succeeded');
    expect(normalizeOutcomeStatus(1)).toBe('partial');
    expect(normalizeOutcomeStatus(2)).toBe('failed');
    expect(normalizeOutcomeStatus('Failed')).toBe('failed');
  });
});
