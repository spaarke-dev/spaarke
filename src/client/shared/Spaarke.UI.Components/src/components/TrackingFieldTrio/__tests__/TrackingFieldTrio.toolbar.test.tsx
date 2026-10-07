/**
 * TrackingFieldTrio governance toolbar — unit tests (task 040, teams-app-r1).
 *
 * Covers the task's `<ui-tests>` acceptance criteria:
 *  - toolbar-renders: person + email icons render when the corresponding
 *    callback props are supplied.
 *  - adr-021-dark-mode: toolbar + existing controls render without a
 *    broken/invisible state under `webDarkTheme`.
 *  - console-error-check: zero console.error/console.warn during render in
 *    both light and dark themes.
 *
 * Also covers the `canGrantAccess` no-dead-click case (acceptance criterion 3)
 * and a no-regression check that the toolbar is absent when neither callback
 * prop is supplied (backward compatibility for existing consumers, per the
 * "opt-in" design).
 *
 * 🔴 Task 118 (unified-access-control-r2, FR-07 / owner decision D-1 option C)
 * INVERTED the gate's default from enabled to disabled, so the prop is now
 * stated explicitly wherever a test needs the icon live. The gate's own tests
 * live in the "fail closed" describe below and are deliberately paired — a
 * disabled-by-default assertion on its own would pass against a component that
 * disabled the icon for everyone.
 */

import * as React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import { TrackingFieldTrio } from '../TrackingFieldTrio';
import type { ITrackingFieldTrioProps, IAccessPermissionOption } from '../types';

const renderWithTheme = (ui: React.ReactElement, theme = webLightTheme) =>
  render(<FluentProvider theme={theme}>{ui}</FluentProvider>);

const ACCESS_PERMISSION_OPTIONS: IAccessPermissionOption[] = [
  { value: 100000000, label: 'Standard', color: '#00B050' },
  { value: 100000001, label: 'Limited', color: '#FFC000' },
  { value: 100000002, label: 'Restricted', color: '#FF0000' },
];

function makeProps(overrides?: Partial<ITrackingFieldTrioProps>): ITrackingFieldTrioProps {
  return {
    monitor: false,
    highPriority: false,
    accessPermission: 100000000,
    accessPermissionOptions: ACCESS_PERMISSION_OPTIONS,
    monitorLabel: 'Monitor',
    highPriorityLabel: 'High Priority',
    accessPermissionLabel: 'Access Permission',
    onMonitorChange: jest.fn(),
    onHighPriorityChange: jest.fn(),
    onAccessPermissionChange: jest.fn(),
    ...overrides,
  };
}

describe('TrackingFieldTrio — governance toolbar (task 040)', () => {
  describe('toolbar-renders', () => {
    it('renders both the person icon and the email icon when both callbacks are supplied', () => {
      const onOpenGrantModal = jest.fn();
      const onOpenEmailMembers = jest.fn();
      renderWithTheme(<TrackingFieldTrio {...makeProps({ onOpenGrantModal, onOpenEmailMembers })} />);

      expect(screen.getByRole('button', { name: 'Grant access' })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Email members' })).toBeInTheDocument();
    });

    it('invokes onOpenGrantModal when the person icon is clicked and access is granted', () => {
      const onOpenGrantModal = jest.fn();
      renderWithTheme(<TrackingFieldTrio {...makeProps({ onOpenGrantModal, canGrantAccess: true })} />);

      fireEvent.click(screen.getByRole('button', { name: 'Grant access' }));
      expect(onOpenGrantModal).toHaveBeenCalledTimes(1);
    });

    it('invokes onOpenEmailMembers when the email icon is clicked', () => {
      const onOpenEmailMembers = jest.fn();
      renderWithTheme(<TrackingFieldTrio {...makeProps({ onOpenEmailMembers })} />);

      fireEvent.click(screen.getByRole('button', { name: 'Email members' }));
      expect(onOpenEmailMembers).toHaveBeenCalledTimes(1);
    });

    it('renders only the person icon when onOpenEmailMembers is omitted', () => {
      const onOpenGrantModal = jest.fn();
      renderWithTheme(<TrackingFieldTrio {...makeProps({ onOpenGrantModal })} />);

      expect(screen.getByRole('button', { name: 'Grant access' })).toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Email members' })).not.toBeInTheDocument();
    });

    it('renders only the email icon when onOpenGrantModal is omitted', () => {
      const onOpenEmailMembers = jest.fn();
      renderWithTheme(<TrackingFieldTrio {...makeProps({ onOpenEmailMembers })} />);

      expect(screen.queryByRole('button', { name: 'Grant access' })).not.toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Email members' })).toBeInTheDocument();
    });

    it('renders no toolbar at all when neither callback is supplied (backward compatibility)', () => {
      renderWithTheme(<TrackingFieldTrio {...makeProps()} />);

      expect(screen.queryByRole('button', { name: 'Grant access' })).not.toBeInTheDocument();
      expect(screen.queryByRole('button', { name: 'Email members' })).not.toBeInTheDocument();
      // Existing controls are unaffected.
      expect(screen.getAllByRole('switch')).toHaveLength(2);
      expect(screen.getByText('Standard')).toBeInTheDocument(); // Access Permission is a single pill (v1.0.27)
    });
  });

  describe('the grant gate — fail closed, and no dead click (task 040 criterion 3; inverted by task 118)', () => {
    it('disables the person icon and never invokes onOpenGrantModal when clicked', () => {
      const onOpenGrantModal = jest.fn();
      renderWithTheme(<TrackingFieldTrio {...makeProps({ onOpenGrantModal, canGrantAccess: false })} />);

      const grantButton = screen.getByRole('button', { name: 'Grant access' });
      expect(grantButton).toBeDisabled();

      fireEvent.click(grantButton);
      expect(onOpenGrantModal).not.toHaveBeenCalled();
    });

    it('does not disable the email icon when canGrantAccess=false (independent of grant gating)', () => {
      const onOpenEmailMembers = jest.fn();
      renderWithTheme(
        <TrackingFieldTrio {...makeProps({ onOpenEmailMembers, onOpenGrantModal: jest.fn(), canGrantAccess: false })} />
      );

      expect(screen.getByRole('button', { name: 'Email members' })).not.toBeDisabled();
    });

    // 🔴 THE INVERSION (task 118, unified-access-control-r2, FR-07 / owner decision D-1 option C).
    //
    // This test asserted the OPPOSITE until v1.0.31 — "defaults the person icon to enabled when
    // canGrantAccess is omitted" — and that default was the client half of the defect the task closed:
    // the server denies an access question it cannot evaluate, while the client permitted one. An
    // omitted prop means the host has no answer from the server, and no answer is not a yes.
    //
    // It is deliberately paired with the `canGrantAccess: true` case below rather than left alone. A
    // disabled-by-default assertion passes just as well against a component that disables the icon
    // unconditionally, which would be a total loss of the affordance rather than a fix.
    it('disables the person icon when canGrantAccess is omitted (fail closed)', () => {
      const onOpenGrantModal = jest.fn();
      renderWithTheme(<TrackingFieldTrio {...makeProps({ onOpenGrantModal })} />);

      const grantButton = screen.getByRole('button', { name: 'Grant access' });
      expect(grantButton).toBeDisabled();

      fireEvent.click(grantButton);
      expect(onOpenGrantModal).not.toHaveBeenCalled();
    });

    it('enables the person icon only when canGrantAccess is explicitly true', () => {
      const onOpenGrantModal = jest.fn();
      renderWithTheme(<TrackingFieldTrio {...makeProps({ onOpenGrantModal, canGrantAccess: true })} />);

      expect(screen.getByRole('button', { name: 'Grant access' })).not.toBeDisabled();
    });

    // The email icon is gated independently — it must not become collateral damage of a fail-closed
    // grant gate. The existing `canGrantAccess: false` case asserts this; the OMITTED case is the new
    // one, because that is the state every un-wired host and every harness now lands in.
    it('leaves the email icon enabled when canGrantAccess is omitted', () => {
      renderWithTheme(
        <TrackingFieldTrio {...makeProps({ onOpenGrantModal: jest.fn(), onOpenEmailMembers: jest.fn() })} />
      );

      expect(screen.getByRole('button', { name: 'Email members' })).not.toBeDisabled();
    });
  });

  describe('Existing controls unaffected (no regression)', () => {
    it('Monitor/High-Priority/Access-Permission still render and fire callbacks with the toolbar present', () => {
      const onMonitorChange = jest.fn();
      renderWithTheme(
        <TrackingFieldTrio
          {...makeProps({ onMonitorChange, onOpenGrantModal: jest.fn(), onOpenEmailMembers: jest.fn() })}
        />
      );

      const [monitorSwitch] = screen.getAllByRole('switch');
      fireEvent.click(monitorSwitch);
      expect(onMonitorChange).toHaveBeenCalledWith(true);

      expect(screen.getByText('Standard')).toBeInTheDocument(); // Access Permission is a single pill (v1.0.27)
    });
  });

  describe('adr-021-dark-mode', () => {
    it('renders the toolbar and existing controls under webDarkTheme without a broken state', () => {
      renderWithTheme(
        <TrackingFieldTrio {...makeProps({ onOpenGrantModal: jest.fn(), onOpenEmailMembers: jest.fn() })} />,
        webDarkTheme
      );

      expect(screen.getByRole('button', { name: 'Grant access' })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Email members' })).toBeInTheDocument();
      expect(screen.getAllByRole('switch')).toHaveLength(2);
      expect(screen.getByText('Standard')).toBeInTheDocument(); // Access Permission is a single pill (v1.0.27)
    });
  });

  describe('console-error-check', () => {
    it('renders the full component (toolbar + existing controls) with zero console errors/warnings — light theme', () => {
      const errorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});
      const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => {});

      renderWithTheme(
        <TrackingFieldTrio
          {...makeProps({ onOpenGrantModal: jest.fn(), onOpenEmailMembers: jest.fn(), canGrantAccess: false })}
        />,
        webLightTheme
      );

      expect(errorSpy).not.toHaveBeenCalled();
      expect(warnSpy).not.toHaveBeenCalled();

      errorSpy.mockRestore();
      warnSpy.mockRestore();
    });

    it('renders the full component (toolbar + existing controls) with zero console errors/warnings — dark theme', () => {
      const errorSpy = jest.spyOn(console, 'error').mockImplementation(() => {});
      const warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => {});

      renderWithTheme(
        <TrackingFieldTrio {...makeProps({ onOpenGrantModal: jest.fn(), onOpenEmailMembers: jest.fn() })} />,
        webDarkTheme
      );

      expect(errorSpy).not.toHaveBeenCalled();
      expect(warnSpy).not.toHaveBeenCalled();

      errorSpy.mockRestore();
      warnSpy.mockRestore();
    });
  });
});
