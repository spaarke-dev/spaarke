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
 * Also covers the `canGrantAccess=false` no-dead-click case (acceptance
 * criterion 3) and a no-regression check that the toolbar is absent when
 * neither callback prop is supplied (backward compatibility for existing
 * consumers, per the "opt-in" design).
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
      renderWithTheme(
        <TrackingFieldTrio {...makeProps({ onOpenGrantModal, onOpenEmailMembers })} />
      );

      expect(screen.getByRole('button', { name: 'Grant access' })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Email members' })).toBeInTheDocument();
    });

    it('invokes onOpenGrantModal when the person icon is clicked and access is granted', () => {
      const onOpenGrantModal = jest.fn();
      renderWithTheme(
        <TrackingFieldTrio {...makeProps({ onOpenGrantModal, canGrantAccess: true })} />
      );

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
      expect(screen.getAllByRole('radio')).toHaveLength(3);
    });
  });

  describe('canGrantAccess=false — no dead click (acceptance criterion 3)', () => {
    it('disables the person icon and never invokes onOpenGrantModal when clicked', () => {
      const onOpenGrantModal = jest.fn();
      renderWithTheme(
        <TrackingFieldTrio {...makeProps({ onOpenGrantModal, canGrantAccess: false })} />
      );

      const grantButton = screen.getByRole('button', { name: 'Grant access' });
      expect(grantButton).toBeDisabled();

      fireEvent.click(grantButton);
      expect(onOpenGrantModal).not.toHaveBeenCalled();
    });

    it('does not disable the email icon when canGrantAccess=false (independent of grant gating)', () => {
      const onOpenEmailMembers = jest.fn();
      renderWithTheme(
        <TrackingFieldTrio
          {...makeProps({ onOpenEmailMembers, onOpenGrantModal: jest.fn(), canGrantAccess: false })}
        />
      );

      expect(screen.getByRole('button', { name: 'Email members' })).not.toBeDisabled();
    });

    it('defaults the person icon to enabled when canGrantAccess is omitted', () => {
      const onOpenGrantModal = jest.fn();
      renderWithTheme(<TrackingFieldTrio {...makeProps({ onOpenGrantModal })} />);

      expect(screen.getByRole('button', { name: 'Grant access' })).not.toBeDisabled();
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

      expect(screen.getAllByRole('radio')).toHaveLength(3);
    });
  });

  describe('adr-021-dark-mode', () => {
    it('renders the toolbar and existing controls under webDarkTheme without a broken state', () => {
      renderWithTheme(
        <TrackingFieldTrio
          {...makeProps({ onOpenGrantModal: jest.fn(), onOpenEmailMembers: jest.fn() })}
        />,
        webDarkTheme
      );

      expect(screen.getByRole('button', { name: 'Grant access' })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Email members' })).toBeInTheDocument();
      expect(screen.getAllByRole('switch')).toHaveLength(2);
      expect(screen.getAllByRole('radio')).toHaveLength(3);
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
        <TrackingFieldTrio
          {...makeProps({ onOpenGrantModal: jest.fn(), onOpenEmailMembers: jest.fn() })}
        />,
        webDarkTheme
      );

      expect(errorSpy).not.toHaveBeenCalled();
      expect(warnSpy).not.toHaveBeenCalled();

      errorSpy.mockRestore();
      warnSpy.mockRestore();
    });
  });
});
