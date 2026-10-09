/**
 * TrackingFieldTrio — the Access Permission pill's note (unified-access-control-r2 task 175, owner round 87): where the
 * effective value comes from ("inherited from Matter X" / "set on this record"), as the pill's tooltip and accessible
 * description. Omitted: no tooltip, the pill unchanged. The menu offers exactly the options the host passes (the host
 * filters them by the parent's floor).
 */

import * as React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { TrackingFieldTrio } from '../TrackingFieldTrio';
import type { ITrackingFieldTrioProps } from '../types';

function makeProps(overrides?: Partial<ITrackingFieldTrioProps>): ITrackingFieldTrioProps {
  return {
    monitor: false,
    highPriority: false,
    accessPermission: 100000002,
    accessPermissionOptions: [
      { value: 100000001, label: 'Limited' },
      { value: 100000002, label: 'Restricted' },
    ],
    monitorLabel: 'Monitor',
    highPriorityLabel: 'High Priority',
    accessPermissionLabel: 'Access Permission',
    onMonitorChange: jest.fn(),
    onHighPriorityChange: jest.fn(),
    onAccessPermissionChange: jest.fn(),
    ...overrides,
  };
}

const renderTrio = (props: ITrackingFieldTrioProps) =>
  render(
    <FluentProvider theme={webLightTheme}>
      <TrackingFieldTrio {...props} />
    </FluentProvider>
  );

describe('TrackingFieldTrio — Access Permission note (task 175)', () => {
  const NOTE = 'Access Permission: Restricted (inherited from Matter Acme v. Beta)';

  it('describes the pill with the note', () => {
    renderTrio(makeProps({ accessPermissionNote: NOTE }));

    const pill = screen.getByRole('button', { name: 'Access Permission' });
    expect(pill).toHaveTextContent('Restricted');
    expect(pill).toHaveAccessibleDescription(NOTE);
  });

  it('without a note the pill has no description', () => {
    renderTrio(makeProps());

    expect(screen.getByRole('button', { name: 'Access Permission' })).not.toHaveAccessibleDescription();
  });

  it('with a note the menu still opens and offers exactly the options passed', async () => {
    const onAccessPermissionChange = jest.fn();
    renderTrio(makeProps({ accessPermissionNote: NOTE, onAccessPermissionChange }));

    fireEvent.click(screen.getByRole('button', { name: 'Access Permission' }));
    const items = await screen.findAllByRole('menuitem');
    expect(items.map(i => i.textContent)).toEqual(['Limited', 'Restricted']);
    fireEvent.click(items[0]);
    expect(onAccessPermissionChange).toHaveBeenCalledWith(100000001);
  });
});
