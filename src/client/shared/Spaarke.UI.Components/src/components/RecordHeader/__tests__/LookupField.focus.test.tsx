/**
 * LookupField.focus.test.tsx - task 200.
 *
 * After the editable record-header lookup's picker settles (pick or cancel), keyboard focus goes back to the field's
 * value (the role=button element, which is a different element for an empty and a filled field): the platform pane
 * leaves focus on <body> or on the link behind the form. Focus placed elsewhere in the host dialog on purpose is left
 * alone.
 */
import * as React from 'react';
import { act, fireEvent, screen, waitFor } from '@testing-library/react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import { LookupField, type ILookupFieldValue } from '../fields/LookupField';

const TARGET = 'sprk_mattertype_ref';
const PICKED = { id: '22222222-2222-2222-2222-222222222222', name: 'Litigation', entityType: TARGET };

const valueButton = (): HTMLElement => screen.getByTestId('record-header-lookup-field-value');

/** A field whose saved value shows up as the new value (the host writes it back), as the record header does. */
const Host: React.FC = () => {
  const [value, setValue] = React.useState<ILookupFieldValue | null>(null);
  return (
    <div role="dialog" aria-label="Record">
      <input aria-label="Other field in the dialog" />
      <LookupField
        label="Matter Type"
        value={value}
        span={1}
        targets={[TARGET]}
        onSave={async picked => setValue(picked as ILookupFieldValue)}
      />
    </div>
  );
};

/** `this`-sensitive like the real Xrm.Utility.lookupObjects (see LookupField.edit.test.tsx). */
function stubLookupObjects(impl: () => Promise<unknown[]>): jest.Mock {
  const utility: Record<string, unknown> = { _clientApiExecutor: {} };
  const lookupObjects = jest.fn(function () {
    return impl();
  });
  utility.lookupObjects = lookupObjects;
  (window as unknown as { Xrm?: unknown }).Xrm = { WebApi: {}, Utility: utility };
  return lookupObjects;
}

describe('record header LookupField: focus returns to the field value (task 200)', () => {
  let outside: HTMLButtonElement;
  beforeEach(() => {
    outside = document.createElement('button');
    document.body.appendChild(outside);
  });
  afterEach(() => {
    outside.remove();
    delete (window as unknown as { Xrm?: unknown }).Xrm;
  });

  it('pick on an empty field: focus ends on the (now filled) value', async () => {
    const lookupObjects = stubLookupObjects(async () => {
      outside.focus();
      return [PICKED];
    });
    renderWithProviders(<Host />);

    fireEvent.click(valueButton());

    await waitFor(() => expect(lookupObjects).toHaveBeenCalled());
    await waitFor(() => expect(valueButton()).toHaveTextContent('Litigation'));
    await waitFor(() => expect(document.activeElement).toBe(valueButton()));
  });

  it('cancel: focus ends on the value', async () => {
    const lookupObjects = stubLookupObjects(async () => {
      outside.focus();
      return [];
    });
    renderWithProviders(<Host />);

    fireEvent.click(valueButton());

    await waitFor(() => expect(lookupObjects).toHaveBeenCalled());
    await waitFor(() => expect(document.activeElement).toBe(valueButton()));
  });

  it('does not move focus that is inside the host dialog on another control', async () => {
    const lookupObjects = stubLookupObjects(async () => {
      screen.getByRole('textbox', { name: 'Other field in the dialog' }).focus();
      return [];
    });
    renderWithProviders(<Host />);

    fireEvent.click(valueButton());

    await waitFor(() => expect(lookupObjects).toHaveBeenCalled());
    await act(async () => {
      await new Promise(r => window.setTimeout(r, 30));
    });
    expect(document.activeElement).toBe(screen.getByRole('textbox', { name: 'Other field in the dialog' }));
  });
});
