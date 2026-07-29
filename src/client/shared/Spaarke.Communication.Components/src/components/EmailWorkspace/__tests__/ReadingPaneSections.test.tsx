/**
 * ReadingPaneSections.test.tsx
 *
 * Unit/RTL tests for the reading-pane MAIN-AREA redesign primitives
 * (email-communication-solution-r5): the shared `CollapsibleSection`
 * (COLLAPSED-by-default + count / status-dot headers), the confirmed-state
 * `EmailRelatedToPills`, and the `summarizeConnections` traffic-light helper
 * that drives the Association section's 🔴/🟡/🟢 status.
 */
import * as React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';
import { CollapsibleSection } from '../CollapsibleSection';
import { EmailRelatedToPills } from '../EmailRelatedToPills';
import { summarizeConnections, type Connection } from '../../../logic/connections';

function renderWithProvider(ui: React.ReactElement, theme = webLightTheme) {
  return render(<FluentProvider theme={theme}>{ui}</FluentProvider>);
}

function makeConnection(overrides: Partial<Connection> = {}): Connection {
  return {
    field: 'sprk_regardingmatter',
    entity: 'sprk_matter',
    slotLabel: 'Matter',
    targetName: 'Acme v Beta',
    targetId: 'mtr-1',
    confidence: 0.9,
    status: 'confirmed',
    order: 1,
    ...overrides,
  };
}

describe('CollapsibleSection', () => {
  it('is COLLAPSED by default (body not rendered) and shows a count header, then expands on click', () => {
    renderWithProvider(
      <CollapsibleSection id="attachments" title="Attachments" count={3}>
        <div data-testid="att-body">bodycontent</div>
      </CollapsibleSection>
    );

    expect(screen.getByText('Attachments')).toBeInTheDocument();
    expect(screen.getByText('(3)')).toBeInTheDocument();
    expect(screen.getByTestId('section-toggle-attachments')).toHaveAttribute('aria-expanded', 'false');
    expect(screen.queryByTestId('att-body')).not.toBeInTheDocument();

    fireEvent.click(screen.getByTestId('section-toggle-attachments'));
    expect(screen.getByTestId('section-toggle-attachments')).toHaveAttribute('aria-expanded', 'true');
    expect(screen.getByTestId('att-body')).toBeInTheDocument();
  });

  it('keepMounted renders the body while collapsed (hidden) so a child can report a header count', () => {
    renderWithProvider(
      <CollapsibleSection id="attachments" title="Attachments" count={0} keepMounted>
        <div data-testid="att-body">present-but-hidden</div>
      </CollapsibleSection>
    );

    // Collapsed, but the body is mounted (just hidden) for the count-reporting child.
    const body = screen.getByTestId('section-body-attachments');
    expect(body).toBeInTheDocument();
    expect(body).toHaveAttribute('hidden');
    expect(screen.getByTestId('att-body')).toBeInTheDocument();
  });

  it('renders a status dot + one-line label instead of a count (Association section)', () => {
    renderWithProvider(
      <CollapsibleSection id="association" title="Association" status={{ tone: 'green', label: 'All set' }}>
        <div>resolver</div>
      </CollapsibleSection>
    );

    expect(screen.getByText('Association')).toBeInTheDocument();
    expect(screen.getByText('All set')).toBeInTheDocument();
    expect(screen.queryByText(/\(\d+\)/)).not.toBeInTheDocument();
  });

  it('renders in dark mode with no console errors (ADR-021)', () => {
    const errorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});
    renderWithProvider(
      <CollapsibleSection id="related-to" title="Related to" count={1}>
        <div>x</div>
      </CollapsibleSection>,
      webDarkTheme
    );
    expect(errorSpy).not.toHaveBeenCalled();
    errorSpy.mockRestore();
  });
});

describe('EmailRelatedToPills', () => {
  it('renders one pill per confirmed association (type + name)', () => {
    renderWithProvider(
      <EmailRelatedToPills
        associations={[
          { entityType: 'sprk_matter', recordId: 'm1', recordName: 'Acme v Beta' },
          { entityType: 'contact', recordId: 'c1', recordName: 'Jane Doe' },
        ]}
      />
    );

    const pills = screen.getByTestId('email-related-to-pills');
    expect(pills).toBeInTheDocument();
    expect(screen.getByText('Acme v Beta')).toBeInTheDocument();
    expect(screen.getByText('Jane Doe')).toBeInTheDocument();
    // Friendly entity labels (Matter / Contact), not raw logical names.
    expect(screen.getByText('Matter')).toBeInTheDocument();
    expect(screen.getByText('Contact')).toBeInTheDocument();
  });

  it('shows an empty message when there are no confirmed associations', () => {
    renderWithProvider(<EmailRelatedToPills associations={[]} />);
    expect(screen.getByText('Not filed to any record yet.')).toBeInTheDocument();
    expect(screen.queryByTestId('email-related-to-pills')).not.toBeInTheDocument();
  });
});

describe('summarizeConnections (Association status dot)', () => {
  it('🔴 red when nothing is filed and a decision is pending', () => {
    const summary = summarizeConnections({
      needsDecision: [makeConnection({ status: 'ambiguous' })],
      filed: [],
      suggested: [],
    });
    expect(summary.tone).toBe('red');
    expect(summary.label).toBe('Needs your decision');
  });

  it('🔴 red "Not filed yet" when nothing is filed and nothing is pending', () => {
    const summary = summarizeConnections({ needsDecision: [], filed: [], suggested: [] });
    expect(summary.tone).toBe('red');
    expect(summary.label).toBe('Not filed yet');
  });

  it('🟡 yellow when filed but suggestions remain to review', () => {
    const summary = summarizeConnections({
      needsDecision: [],
      filed: [makeConnection()],
      suggested: [makeConnection({ field: 'sprk_regardingperson', entity: 'contact', status: 'suggested' })],
    });
    expect(summary.tone).toBe('yellow');
    expect(summary.label).toBe('Filed — suggestions to review');
  });

  it('🟢 green when filed and nothing left to review', () => {
    const summary = summarizeConnections({ needsDecision: [], filed: [makeConnection()], suggested: [] });
    expect(summary.tone).toBe('green');
    expect(summary.label).toBe('All set');
  });
});
