/**
 * PolymorphicPicker.focus.test.tsx - task 200.
 *
 * After the lookup opened from the picker menu settles (pick or cancel), keyboard focus goes back to the toolbar
 * trigger: the platform pane leaves focus on <body> or on the link behind the dialog, and the trigger is disabled while
 * the pane is open. Focus placed inside the host dialog on purpose is left alone.
 */
import * as React from 'react';
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { PolymorphicPicker, type RecordTypeCatalogEntry } from '../PolymorphicPicker';

const CATALOG: RecordTypeCatalogEntry[] = [
  {
    recordTypeRefId: '11111111-1111-1111-1111-111111111111',
    displayName: 'Matter',
    logicalName: 'sprk_matter',
    regardingField: 'sprk_regardingmatter',
    regardingRecordNumberField: 'sprk_matternumber',
  },
];

const trigger = (): HTMLElement => screen.getByTestId('polymorphic-picker-trigger');

function installXrm(lookupObjects: jest.Mock) {
  (window as unknown as { Xrm?: unknown }).Xrm = { WebApi: {}, Utility: { lookupObjects } };
}

async function chooseMatter() {
  fireEvent.click(trigger());
  fireEvent.click(await screen.findByTestId('polymorphic-picker-item-sprk_matter'));
}

describe('PolymorphicPicker: focus returns to the trigger (task 200)', () => {
  let outside: HTMLButtonElement;
  beforeEach(() => {
    outside = document.createElement('button');
    document.body.appendChild(outside);
    render(
      <FluentProvider theme={webLightTheme}>
        <div role="dialog" aria-label="Regarding">
          <input aria-label="Other field in the dialog" />
          <PolymorphicPicker catalog={CATALOG} onSelect={jest.fn()} webApi={{}} />
        </div>
      </FluentProvider>
    );
  });
  afterEach(() => {
    outside.remove();
    delete (window as unknown as { Xrm?: unknown }).Xrm;
  });

  it('pick: focus ends on the toolbar trigger', async () => {
    const lookupObjects = jest.fn().mockImplementation(async () => {
      outside.focus();
      return [{ id: 'abc', name: 'MAT-1', entityType: 'sprk_matter' }];
    });
    installXrm(lookupObjects);

    await chooseMatter();

    await waitFor(() => expect(lookupObjects).toHaveBeenCalled());
    await waitFor(() => expect(document.activeElement).toBe(trigger()));
  });

  it('cancel: focus ends on the toolbar trigger', async () => {
    const lookupObjects = jest.fn().mockImplementation(async () => {
      outside.focus();
      return [];
    });
    installXrm(lookupObjects);

    await chooseMatter();

    await waitFor(() => expect(lookupObjects).toHaveBeenCalled());
    await waitFor(() => expect(document.activeElement).toBe(trigger()));
  });

  it('does not move focus that is inside the host dialog on another control', async () => {
    const lookupObjects = jest.fn().mockImplementation(async () => {
      screen.getByRole('textbox', { name: 'Other field in the dialog' }).focus();
      return [];
    });
    installXrm(lookupObjects);

    await chooseMatter();

    await waitFor(() => expect(lookupObjects).toHaveBeenCalled());
    await act(async () => {
      await new Promise(r => window.setTimeout(r, 30));
    });
    expect(document.activeElement).toBe(screen.getByRole('textbox', { name: 'Other field in the dialog' }));
  });
});
