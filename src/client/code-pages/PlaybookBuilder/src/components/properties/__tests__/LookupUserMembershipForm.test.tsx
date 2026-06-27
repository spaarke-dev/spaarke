/**
 * LookupUserMembershipForm tests — R3 task 094 (covers task 043 / FR-1B.4).
 *
 * Verifies all four fields render (entityType, roles, outputVariable,
 * includeRelated), that user edits invoke onConfigChange with the serialized
 * config, and that the form renders cleanly under dark theme (ADR-021).
 */
import * as React from 'react';
import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { LookupUserMembershipForm } from '../LookupUserMembershipForm';
import { renderWithProviders, renderWithTheme, webDarkTheme } from './testUtils';

const EMPTY_CONFIG = JSON.stringify({
  entityType: '',
  roles: [],
  outputVariable: '',
  includeRelated: false,
});

describe('LookupUserMembershipForm', () => {
  it('renders all four labelled fields', () => {
    renderWithProviders(<LookupUserMembershipForm nodeId="n1" configJson={EMPTY_CONFIG} onConfigChange={jest.fn()} />);

    expect(screen.getByLabelText(/Entity Type/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/^Roles$/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/Output Variable/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/Include related/i)).toBeInTheDocument();
  });

  it('seeds fields from configJson', () => {
    const seeded = JSON.stringify({
      entityType: 'sprk_matter',
      roles: ['Owner', 'ProjectManager'],
      outputVariable: 'matterMembers',
      includeRelated: true,
    });
    renderWithProviders(<LookupUserMembershipForm nodeId="n1" configJson={seeded} onConfigChange={jest.fn()} />);

    expect((screen.getByLabelText(/Entity Type/i) as HTMLInputElement).value).toBe('sprk_matter');
    expect((screen.getByLabelText(/^Roles$/i) as HTMLInputElement).value).toBe('Owner, ProjectManager');
    expect((screen.getByLabelText(/Output Variable/i) as HTMLInputElement).value).toBe('matterMembers');
    expect((screen.getByLabelText(/Include related/i) as HTMLInputElement).checked).toBe(true);
  });

  it('emits onConfigChange with updated entityType on edit', async () => {
    const user = userEvent.setup();
    const onChange = jest.fn();
    renderWithProviders(<LookupUserMembershipForm nodeId="n1" configJson={EMPTY_CONFIG} onConfigChange={onChange} />);

    await user.type(screen.getByLabelText(/Entity Type/i), 'a');

    expect(onChange).toHaveBeenCalled();
    const last = JSON.parse(onChange.mock.calls[onChange.mock.calls.length - 1][0]);
    expect(last.entityType).toBe('a');
  });

  it('emits onConfigChange with parsed comma-separated roles', async () => {
    const user = userEvent.setup();
    const onChange = jest.fn();
    renderWithProviders(<LookupUserMembershipForm nodeId="n1" configJson={EMPTY_CONFIG} onConfigChange={onChange} />);

    // Use paste to deliver the whole string in a single change event (avoids
    // re-renders losing intermediate state — onConfigChange is verified by
    // the final emitted JSON.)
    const rolesInput = screen.getByLabelText(/^Roles$/i);
    await user.click(rolesInput);
    await user.paste('Owner, ProjectManager');

    const last = JSON.parse(onChange.mock.calls[onChange.mock.calls.length - 1][0]);
    expect(last.roles).toEqual(['Owner', 'ProjectManager']);
  });

  it('emits onConfigChange with updated outputVariable on edit', async () => {
    const user = userEvent.setup();
    const onChange = jest.fn();
    renderWithProviders(<LookupUserMembershipForm nodeId="n1" configJson={EMPTY_CONFIG} onConfigChange={onChange} />);

    await user.click(screen.getByLabelText(/Output Variable/i));
    await user.paste('matterMembers');

    const last = JSON.parse(onChange.mock.calls[onChange.mock.calls.length - 1][0]);
    expect(last.outputVariable).toBe('matterMembers');
  });

  it('toggles includeRelated when the switch is clicked', async () => {
    const user = userEvent.setup();
    const onChange = jest.fn();
    renderWithProviders(<LookupUserMembershipForm nodeId="n1" configJson={EMPTY_CONFIG} onConfigChange={onChange} />);

    await user.click(screen.getByLabelText(/Include related/i));
    const last = JSON.parse(onChange.mock.calls[onChange.mock.calls.length - 1][0]);
    expect(last.includeRelated).toBe(true);
  });

  it('falls back to defaults on malformed configJson (no crash)', () => {
    renderWithProviders(
      <LookupUserMembershipForm nodeId="n1" configJson="not-valid-json" onConfigChange={jest.fn()} />
    );
    expect((screen.getByLabelText(/Entity Type/i) as HTMLInputElement).value).toBe('');
    expect((screen.getByLabelText(/Output Variable/i) as HTMLInputElement).value).toBe('');
  });

  it('renders cleanly under dark theme (ADR-021 semantic-token parity)', () => {
    renderWithTheme(
      <LookupUserMembershipForm nodeId="n1" configJson={EMPTY_CONFIG} onConfigChange={jest.fn()} />,
      webDarkTheme
    );
    // Same labels render — Fluent v9 token-driven styles flip automatically.
    expect(screen.getByLabelText(/Entity Type/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/Output Variable/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/Include related/i)).toBeInTheDocument();
  });
});
