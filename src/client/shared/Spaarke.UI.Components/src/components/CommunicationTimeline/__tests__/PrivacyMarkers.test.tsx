/**
 * PrivacyMarkers.test.tsx — FR-21 privilege/privacy marker rendering (task 043).
 *
 * These markers are DISPLAY ONLY; over-disclosure is prevented server-side (the BFF never returns a row/marker the
 * caller may not see). So this suite asserts the render contract: the right badges appear for the right marker
 * values, and an unmarked message renders nothing.
 */
import * as React from 'react';
import { render, screen } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { PrivacyMarkers } from '../subcomponents/PrivacyMarkers';
import { PRIVILEGE_NONE, PRIVILEGE_POTENTIALLY_PRIVILEGED, PRIVILEGE_PRIVILEGED } from '../CommunicationTimeline.types';

const renderWithProvider = (ui: React.ReactElement) =>
  render(<FluentProvider theme={webLightTheme}>{ui}</FluentProvider>);

describe('PrivacyMarkers', () => {
  it('renders the Privileged badge when privilege is Privileged', () => {
    renderWithProvider(<PrivacyMarkers privilege={PRIVILEGE_PRIVILEGED} />);
    expect(screen.getByText('Privileged')).toBeInTheDocument();
  });

  it('renders the "May be privileged" badge when privilege is Potentially Privileged', () => {
    renderWithProvider(<PrivacyMarkers privilege={PRIVILEGE_POTENTIALLY_PRIVILEGED} />);
    expect(screen.getByText('May be privileged')).toBeInTheDocument();
  });

  it('renders Internal only and Private badges when both flags are set', () => {
    renderWithProvider(<PrivacyMarkers privilege={PRIVILEGE_NONE} isInternalOnly isPrivate />);
    expect(screen.getByText('Internal only')).toBeInTheDocument();
    expect(screen.getByText('Private')).toBeInTheDocument();
  });

  it('exposes the markers as a labelled group region (a11y / NFR-05)', () => {
    renderWithProvider(<PrivacyMarkers privilege={PRIVILEGE_PRIVILEGED} isInternalOnly />);
    expect(screen.getByRole('group', { name: 'Privilege and privacy markers' })).toBeInTheDocument();
  });

  it('renders nothing for an unmarked, non-privileged message (no empty DOM)', () => {
    const { container } = renderWithProvider(
      <PrivacyMarkers privilege={PRIVILEGE_NONE} isInternalOnly={false} isPrivate={false} />
    );
    expect(screen.queryByRole('group', { name: 'Privilege and privacy markers' })).not.toBeInTheDocument();
    expect(container.querySelector('[role="group"]')).toBeNull();
  });

  it('renders no privilege badge for None even when privacy flags are absent', () => {
    renderWithProvider(<PrivacyMarkers privilege={PRIVILEGE_NONE} />);
    expect(screen.queryByText('Privileged')).not.toBeInTheDocument();
    expect(screen.queryByText('May be privileged')).not.toBeInTheDocument();
  });
});
