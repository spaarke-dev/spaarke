/**
 * DataverseLookupField.focus.test.tsx - task 200.
 *
 * After the side-pane lookup settles (pick, cancel or failure) keyboard focus goes back to the field's own control: the
 * Select button, or the Clear button once a value is picked. The platform pane leaves focus on <body> (or hands it to
 * whatever opened the dialog), so a keyboard user used to lose their place after every lookup in every wizard.
 * Focus the user or host put inside the host dialog on purpose is left alone.
 */
import * as React from 'react';
import { act, fireEvent, screen, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { DataverseLookupField } from '../DataverseLookupField';
import type { ILookupItem } from '../../../types/LookupTypes';
import type { INavigationService, LookupResult } from '../../../types/serviceInterfaces';

const LABEL = 'Limit to one matter';
const ENTITY = 'sprk_matter';
const GUID = '3f2504e0-4f89-11d3-9a0c-0305e82c3301';

const selectButton = (): HTMLElement => screen.getByRole('button', { name: `Select ${LABEL}` });
const clearButton = (): HTMLElement => screen.getByRole('button', { name: `Clear ${LABEL}` });

const picked: LookupResult = { id: GUID, name: 'MAT-000113', entityType: ENTITY };

/** The field with a controlled value, as every wizard hosts it. `inDialog` wraps it in a `role=dialog` with a sibling input. */
const Host: React.FC<{ openLookup: jest.Mock; inDialog?: boolean }> = ({ openLookup, inDialog = true }) => {
  const [value, setValue] = React.useState<ILookupItem | null>(null);
  const field = (
    <DataverseLookupField
      label={LABEL}
      entityType={ENTITY}
      value={value}
      onChange={setValue}
      navigationService={{ openLookup } as unknown as INavigationService}
    />
  );
  return inDialog ? (
    <div role="dialog" aria-label="New rule">
      <input aria-label="Other field in the dialog" />
      {field}
    </div>
  ) : (
    field
  );
};

/** An element outside the dialog, standing in for the grid link the platform pane hands focus back to. */
function makeOutsideLink(): HTMLButtonElement {
  const el = document.createElement('button');
  el.textContent = 'grid link behind the dialog';
  document.body.appendChild(el);
  return el;
}

describe('DataverseLookupField: focus returns to the field control (task 200)', () => {
  let outside: HTMLButtonElement;
  let warnSpy: jest.SpyInstance;
  beforeEach(() => {
    outside = makeOutsideLink();
    warnSpy = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
  });
  afterEach(() => {
    outside.remove();
    warnSpy.mockRestore();
  });

  it('pick, focus left on the body: focus ends on the control now showing the picked value (Clear)', async () => {
    const openLookup = jest.fn().mockImplementation(async () => {
      (document.activeElement as HTMLElement | null)?.blur(); // the pane closed: nothing holds focus
      return [picked];
    });
    renderWithProviders(<Host openLookup={openLookup} />);
    selectButton().focus();

    fireEvent.click(selectButton());

    await waitFor(() => expect(screen.getByText('MAT-000113')).toBeInTheDocument());
    await waitFor(() => expect(document.activeElement).toBe(clearButton()));
  });

  it('pick, focus handed to a link outside the dialog: focus ends on the Clear button', async () => {
    const openLookup = jest.fn().mockImplementation(async () => {
      outside.focus();
      return [picked];
    });
    renderWithProviders(<Host openLookup={openLookup} />);

    fireEvent.click(selectButton());

    await waitFor(() => expect(screen.getByText('MAT-000113')).toBeInTheDocument());
    await waitFor(() => expect(document.activeElement).toBe(clearButton()));
  });

  it('cancel (empty result): focus ends on the Select button', async () => {
    const openLookup = jest.fn().mockImplementation(async () => {
      outside.focus();
      return [];
    });
    renderWithProviders(<Host openLookup={openLookup} />);

    fireEvent.click(selectButton());

    await waitFor(() => expect(openLookup).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(document.activeElement).toBe(selectButton()));
  });

  it('openLookup throws: treated as a cancel, focus ends on the Select button', async () => {
    const openLookup = jest.fn().mockImplementation(async () => {
      outside.focus();
      throw new Error('no Xrm host');
    });
    renderWithProviders(<Host openLookup={openLookup} />);

    fireEvent.click(selectButton());

    await waitFor(() => expect(warnSpy).toHaveBeenCalled());
    await waitFor(() => expect(document.activeElement).toBe(selectButton()));
  });

  it('a field outside any dialog also gets focus back', async () => {
    const openLookup = jest.fn().mockImplementation(async () => {
      outside.focus();
      return [picked];
    });
    renderWithProviders(<Host openLookup={openLookup} inDialog={false} />);

    fireEvent.click(selectButton());

    await waitFor(() => expect(document.activeElement).toBe(clearButton()));
  });

  it('does not move focus that is inside the host dialog on another control when the lookup settles', async () => {
    const openLookup = jest.fn().mockImplementation(async () => {
      screen.getByRole('textbox', { name: 'Other field in the dialog' }).focus(); // put there on purpose
      return [picked];
    });
    renderWithProviders(<Host openLookup={openLookup} />);

    fireEvent.click(selectButton());

    await waitFor(() => expect(screen.getByText('MAT-000113')).toBeInTheDocument());
    await act(async () => {
      await new Promise(r => window.setTimeout(r, 30));
    });
    expect(document.activeElement).toBe(screen.getByRole('textbox', { name: 'Other field in the dialog' }));
  });

  it('unmounted before the lookup settles: no focus call and no error', async () => {
    let settle!: (v: unknown) => void;
    const openLookup = jest.fn().mockImplementation(() => new Promise(r => (settle = r)));
    const { unmount } = renderWithProviders(<Host openLookup={openLookup} />);
    fireEvent.click(selectButton());
    await waitFor(() => expect(openLookup).toHaveBeenCalled());

    unmount();
    const focusSpy = jest.spyOn(HTMLElement.prototype, 'focus');
    const error = jest.spyOn(console, 'error').mockImplementation(() => undefined);
    try {
      await act(async () => {
        settle([picked]);
        await new Promise(r => window.setTimeout(r, 30));
      });
      expect(focusSpy).not.toHaveBeenCalled();
      expect(error).not.toHaveBeenCalled();
    } finally {
      focusSpy.mockRestore();
      error.mockRestore();
    }
  });
});
