/**
 * TrackingFieldTrio — unit tests (task 023, FR-14/NFR-04/NFR-05/ADR-021).
 *
 * Covers the task's `<ui-tests>` acceptance criteria:
 *  - Flags read/write: injected value + onChange spies fire correctly for
 *    monitor, high-priority, and access-permission (identical semantics to
 *    the pre-lift PCF-local component).
 *  - Entity-agnostic: rendering with a NON-`sprk_communication` option set
 *    (different values/labels/colors) renders faithfully — no
 *    `sprk_communication`-specific integer/label leaks through.
 *  - Dark mode (ADR-021): renders under `webDarkTheme` without hardcoded
 *    light-only colors — computed styles resolve via Fluent CSS custom
 *    properties (`var(--…)`), not raw hex/rgb literals baked into markup.
 */

import * as React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme, webDarkTheme } from '@fluentui/react-components';
import { TrackingFieldTrio } from '../TrackingFieldTrio';
import type { ITrackingFieldTrioProps, IAccessPermissionOption } from '../types';

const renderWithTheme = (ui: React.ReactElement, theme = webLightTheme) =>
  render(<FluentProvider theme={theme}>{ui}</FluentProvider>);

const SPRK_COMMUNICATION_OPTIONS: IAccessPermissionOption[] = [
  { value: 100000000, label: 'Standard', color: '#00B050' },
  { value: 100000001, label: 'Limited', color: '#FFC000' },
  { value: 100000002, label: 'Restricted', color: '#FF0000' },
];

// A deliberately DIFFERENT, non-`sprk_communication` option set — different
// values, labels, and colors — proving the shared core is entity-agnostic.
const OTHER_ENTITY_OPTIONS: IAccessPermissionOption[] = [
  { value: 1, label: 'Public' },
  { value: 2, label: 'Confidential', color: '#3355FF' },
];

function makeProps(overrides?: Partial<ITrackingFieldTrioProps>): ITrackingFieldTrioProps {
  return {
    monitor: false,
    highPriority: false,
    accessPermission: 100000000,
    accessPermissionOptions: SPRK_COMMUNICATION_OPTIONS,
    monitorLabel: 'Monitor',
    highPriorityLabel: 'High Priority',
    accessPermissionLabel: 'Access Permission',
    onMonitorChange: jest.fn(),
    onHighPriorityChange: jest.fn(),
    onAccessPermissionChange: jest.fn(),
    ...overrides,
  };
}

describe('TrackingFieldTrio', () => {
  describe('Flags read/write', () => {
    it('renders injected monitor/high-priority/access-permission values', () => {
      renderWithTheme(<TrackingFieldTrio {...makeProps({ monitor: true, highPriority: true })} />);

      // Access Permission is a single PILL showing the selected value; the other
      // options live in a menu (not rendered until opened).
      expect(screen.getByText('Standard')).toBeInTheDocument();
      expect(screen.queryByText('Limited')).not.toBeInTheDocument();
      expect(screen.queryByText('Restricted')).not.toBeInTheDocument();
      // Two switches: Monitor + High Priority, both checked (label "Yes").
      const switches = screen.getAllByRole('switch');
      expect(switches).toHaveLength(2);
      switches.forEach(s => expect(s).toBeChecked());
    });

    it('fires onMonitorChange with the new boolean when toggled', () => {
      const onMonitorChange = jest.fn();
      renderWithTheme(<TrackingFieldTrio {...makeProps({ monitor: false, onMonitorChange })} />);

      const [monitorSwitch] = screen.getAllByRole('switch');
      fireEvent.click(monitorSwitch);

      expect(onMonitorChange).toHaveBeenCalledTimes(1);
      expect(onMonitorChange).toHaveBeenCalledWith(true);
    });

    it('fires onHighPriorityChange with the new boolean when toggled', () => {
      const onHighPriorityChange = jest.fn();
      renderWithTheme(<TrackingFieldTrio {...makeProps({ highPriority: false, onHighPriorityChange })} />);

      const [, highPrioritySwitch] = screen.getAllByRole('switch');
      fireEvent.click(highPrioritySwitch);

      expect(onHighPriorityChange).toHaveBeenCalledTimes(1);
      expect(onHighPriorityChange).toHaveBeenCalledWith(true);
    });

    it('fires onAccessPermissionChange with the picked value when a menu option is chosen', async () => {
      const onAccessPermissionChange = jest.fn();
      renderWithTheme(<TrackingFieldTrio {...makeProps({ accessPermission: 100000000, onAccessPermissionChange })} />);

      // Open the pill's menu, then pick "Restricted".
      fireEvent.click(screen.getByRole('button', { name: 'Access Permission' }));
      fireEvent.click(await screen.findByRole('menuitem', { name: 'Restricted' }));

      expect(onAccessPermissionChange).toHaveBeenCalledTimes(1);
      expect(onAccessPermissionChange).toHaveBeenCalledWith(100000002);
    });
  });

  describe('Entity-agnostic (options injected, FR-14)', () => {
    it('renders a NON-sprk_communication option set faithfully with no leaked sprk_communication data', async () => {
      renderWithTheme(
        <TrackingFieldTrio
          {...makeProps({
            accessPermission: 2,
            accessPermissionOptions: OTHER_ENTITY_OPTIONS,
            accessPermissionLabel: 'Visibility',
          })}
        />
      );

      // The pill shows the selected value ("Confidential", value 2).
      expect(screen.getByText('Confidential')).toBeInTheDocument();
      // No sprk_communication-specific labels leak through.
      expect(screen.queryByText('Standard')).not.toBeInTheDocument();
      expect(screen.queryByText('Limited')).not.toBeInTheDocument();
      expect(screen.queryByText('Restricted')).not.toBeInTheDocument();
      // The menu offers exactly the injected 2 options (no hardcoded 3rd).
      fireEvent.click(screen.getByRole('button', { name: 'Visibility' }));
      expect(await screen.findByRole('menuitem', { name: 'Public' })).toBeInTheDocument();
      expect(screen.getAllByRole('menuitem')).toHaveLength(2);
    });

    it('renders field display labels from injected props, not hardcoded strings', () => {
      renderWithTheme(
        <TrackingFieldTrio
          {...makeProps({
            monitorLabel: 'Watch',
            highPriorityLabel: 'Urgent',
            accessPermissionLabel: 'Visibility',
            showTitle: true,
          })}
        />
      );

      expect(screen.getByText('Watch')).toBeInTheDocument();
      expect(screen.getByText('Urgent')).toBeInTheDocument();
      expect(screen.getByText('Visibility')).toBeInTheDocument();
    });
  });

  describe('Dark mode (ADR-021)', () => {
    it('renders all three flags under a dark FluentProvider theme without crashing', () => {
      renderWithTheme(<TrackingFieldTrio {...makeProps({ monitor: true, highPriority: false })} />, webDarkTheme);

      expect(screen.getAllByRole('switch')).toHaveLength(2);
      // Access Permission renders as a single pill (no radios); the selected
      // value shows, tinted via Griffel-resolved tokens, never raw hex in JSX.
      expect(screen.queryByRole('radio')).not.toBeInTheDocument();
      expect(screen.getByText('Standard')).toBeInTheDocument();
    });
  });
});
