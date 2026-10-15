/**
 * ConsoleKit statusOverride — unit tests (ebilling-r1 task 109). Covers the stated contracts:
 *  - StatusBar / RecordRow `statusOverride`: given label + tone shown; absent = the Console mapping, unchanged
 *  - ADR-021: statusOverride renders under the dark theme
 * (OutcomeCards / CountFilters / Disclosure were removed after the ontology-r1 review: task 110 reuses
 * MetricCard, Fluent Accordion and SprkChat/OutcomeCard.)
 */

import '@testing-library/jest-dom';
import * as React from 'react';
import { render, screen } from '@testing-library/react';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';

import { CONSOLE_STATE_BADGES, RecordRow, StatusBar } from '..';

function renderThemed(ui: React.ReactElement, dark = false) {
  return render(<FluentProvider theme={dark ? webDarkTheme : webLightTheme}>{ui}</FluentProvider>);
}

describe('statusOverride on StatusBar', () => {
  it('shows the given label and tone, and the bar intent follows the tone', () => {
    renderThemed(
      <StatusBar
        state="Decided"
        statusOverride={{ label: 'Returned to firm', tone: 'warning', description: 'Sent back for correction.' }}
        recordId="DR-0001"
      />
    );
    const badge = screen.getByTestId('status-badge');
    expect(badge).toHaveTextContent('Returned to firm');
    expect(badge).toHaveAttribute('data-tone', 'warning');
    expect(badge).toHaveAttribute('aria-label', 'Returned to firm. Sent back for correction.');
    expect(screen.getByTestId('status-bar')).toHaveAttribute('data-intent', 'warning');
    expect(screen.queryByText('Decided')).not.toBeInTheDocument();
  });

  it('names the badge by its label alone when the override has no description', () => {
    renderThemed(<StatusBar state="Decided" statusOverride={{ label: 'Rejected', tone: 'critical' }} />);
    expect(screen.getByTestId('status-badge')).toHaveAttribute('aria-label', 'Rejected');
    expect(screen.getByTestId('status-bar')).toHaveAttribute('data-intent', 'error');
  });

  it('uses the override tone for the intent even where the state has its own intent exception (Dismissed)', () => {
    renderThemed(<StatusBar state="Dismissed" statusOverride={{ label: 'Approved', tone: 'success' }} />);
    expect(screen.getByTestId('status-bar')).toHaveAttribute('data-intent', 'success');
  });

  it('without an override renders exactly the Console mapping', () => {
    renderThemed(<StatusBar state="Dismissed" />);
    const badge = screen.getByTestId('status-badge');
    expect(badge).toHaveTextContent(CONSOLE_STATE_BADGES.Dismissed.label);
    expect(badge).toHaveAttribute('data-tone', CONSOLE_STATE_BADGES.Dismissed.tone);
    expect(badge).toHaveAttribute(
      'aria-label',
      `${CONSOLE_STATE_BADGES.Dismissed.label}. ${CONSOLE_STATE_BADGES.Dismissed.description}`
    );
    expect(screen.getByTestId('status-bar')).toHaveAttribute('data-intent', 'info');
  });

  it('renders identically with statusOverride undefined and absent', () => {
    // Generated Fluent ids differ per render; everything else must be byte-for-byte equal.
    const markup = () => screen.getByTestId('status-bar').outerHTML.replace(/fui-_r_[0-9a-z]+_/g, 'fui-id');
    const a = renderThemed(<StatusBar state="Denied" recordId="DR-1" />);
    const without = markup();
    a.unmount();
    renderThemed(<StatusBar state="Denied" recordId="DR-1" statusOverride={undefined} />);
    expect(markup()).toBe(without);
  });

  it('leaves the Console table with its nine entries', () => {
    expect(Object.keys(CONSOLE_STATE_BADGES)).toHaveLength(9);
  });
});

describe('statusOverride on RecordRow', () => {
  it('shows the given label and tone', () => {
    renderThemed(
      <RecordRow
        recordId="DR-0002"
        title="Line 4 disallowed"
        state="Decided"
        statusOverride={{ label: 'Disallowed', tone: 'critical' }}
      />
    );
    const badge = screen.getByTestId('status-badge');
    expect(badge).toHaveTextContent('Disallowed');
    expect(badge).toHaveAttribute('data-tone', 'critical');
  });

  it('without an override shows the Console label and tone', () => {
    renderThemed(<RecordRow recordId="DR-0002" title="x" state="Authorized" />);
    const badge = screen.getByTestId('status-badge');
    expect(badge).toHaveTextContent(CONSOLE_STATE_BADGES.Authorized.label);
    expect(badge).toHaveAttribute('data-tone', CONSOLE_STATE_BADGES.Authorized.tone);
  });
});

describe('ADR-021 safety of statusOverride', () => {
  it('renders the override under the dark theme', () => {
    renderThemed(
      <>
        <StatusBar state="Decided" statusOverride={{ label: 'Approved', tone: 'success' }} />
        <RecordRow
          recordId="DR-0003"
          title="x"
          state="Decided"
          statusOverride={{ label: 'Approved', tone: 'success' }}
        />
      </>,
      true
    );
    expect(screen.getByTestId('status-bar')).toBeInTheDocument();
    expect(screen.getAllByTestId('status-badge').length).toBeGreaterThan(0);
  });
});
