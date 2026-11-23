/**
 * Ontology admin Classification area: keyboard focus around the inline category form (task 202, scope item 4).
 *
 * Dev finding 2026-11-22 (Console): a triage category's Edit opened the form above the table but focus stayed on the
 * row's Edit button; Cancel unmounted the form and focus fell to <body>.
 *  - open: focus goes to the form's Name field (first field);
 *  - Cancel / Save: focus returns to that row's Edit button, or to the table when the row is gone;
 *  - the same for New category.
 */
import '@testing-library/jest-dom';
import * as React from 'react';
import { render, screen, waitFor, act } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { ClassificationArea } from '..';
import type { ClassificationCategory, ClassificationList, ClassificationService } from '..';

const FEE = '8b62dd84-1fbc-f111-aaaf-3833c5e9614d';
const INVOICE = '71310056-598b-f111-8077-7ced8ddc4cc6';

const category = (id: string, name: string, over: Partial<ClassificationCategory> = {}): ClassificationCategory => ({
  id,
  name,
  guidance: 'Guidance.',
  enabled: false,
  version: 7,
  volume30Days: 0,
  recall: {
    status: 'NotMeasured',
    recall: null,
    labelledSetSize: null,
    measuredOn: null,
    measuredBeforeLastEdit: false,
  },
  readBy: [],
  ...over,
});

const list = (categories: ClassificationCategory[]): ClassificationList => ({
  categories,
  recallFloor: { percent: 80, minItems: 50 },
  volumeWindowDays: 30,
  volumeAvailable: true,
  referencesAvailable: true,
});

function fakeService(initial: ClassificationList): jest.Mocked<ClassificationService> {
  let current = initial;
  return {
    list: jest.fn(async () => current),
    create: jest.fn(async r => {
      current = list([...current.categories, category('new', r.name)]);
      return { id: 'new', name: r.name, guidance: r.guidance, enabled: false, version: 1 };
    }),
    update: jest.fn(async (id, r) => ({
      id,
      name: r.name,
      guidance: r.guidance,
      enabled: false,
      version: r.version + 1,
    })),
    setEnabled: jest.fn(async (id, enabled, version) => ({
      id,
      name: 'x',
      guidance: null,
      enabled,
      version: version + 1,
    })),
  };
}

const renderArea = (service: ClassificationService) =>
  render(
    <FluentProvider theme={webLightTheme}>
      <ClassificationArea service={service} />
    </FluentProvider>
  );

const active = () => document.activeElement;

describe('ClassificationArea focus around the category form (task 202)', () => {
  it("Edit moves focus to the Name field; Cancel returns it to that row's Edit button", async () => {
    const user = userEvent.setup();
    renderArea(fakeService(list([category(FEE, 'Fee / rate change'), category(INVOICE, 'Invoice / Billing')])));
    const edit = await screen.findByTestId(`classification-edit-${INVOICE}`);

    await user.click(edit);

    const name = await screen.findByTestId('classification-form-name');
    await waitFor(() => expect(active()).toBe(name));

    await user.click(screen.getByTestId('classification-form-cancel'));

    await waitFor(() => expect(screen.queryByTestId('classification-form')).toBeNull());
    await waitFor(() => expect(active()).toBe(screen.getByTestId(`classification-edit-${INVOICE}`)));
  });

  it("Save returns focus to that row's Edit button", async () => {
    const user = userEvent.setup();
    const service = fakeService(list([category(FEE, 'Fee / rate change')]));
    renderArea(service);
    await user.click(await screen.findByTestId(`classification-edit-${FEE}`));
    const name = await screen.findByTestId('classification-form-name');
    await user.type(name, ' v2');

    await user.click(screen.getByTestId('classification-form-save'));

    await waitFor(() => expect(service.update).toHaveBeenCalled());
    await waitFor(() => expect(screen.queryByTestId('classification-form')).toBeNull());
    await waitFor(() => expect(active()).toBe(screen.getByTestId(`classification-edit-${FEE}`)));
  });

  it('the row is gone when the form closes: focus goes to the table', async () => {
    const user = userEvent.setup();
    const service = fakeService(list([category(FEE, 'Fee / rate change'), category(INVOICE, 'Invoice / Billing')]));
    renderArea(service);
    await user.click(await screen.findByTestId(`classification-edit-${FEE}`));
    await screen.findByTestId('classification-form');
    // The category is removed by another administrator; the next reload no longer has it.
    service.list.mockImplementation(async () => list([category(INVOICE, 'Invoice / Billing')]));
    service.update.mockImplementation(async (id, r) => ({
      id,
      name: r.name,
      guidance: r.guidance,
      enabled: false,
      version: r.version + 1,
    }));
    await user.type(screen.getByTestId('classification-form-name'), ' v2');

    await user.click(screen.getByTestId('classification-form-save'));

    await waitFor(() => expect(screen.queryByTestId(`classification-edit-${FEE}`)).toBeNull());
    await waitFor(() => expect(active()).toBe(screen.getByTestId('classification-table')));
  });

  it('New category: the form opens on Name, and Cancel returns focus to the New category button', async () => {
    const user = userEvent.setup();
    renderArea(fakeService(list([category(FEE, 'Fee / rate change')])));
    await user.click(await screen.findByTestId('classification-new'));

    const name = await screen.findByTestId('classification-form-name');
    await waitFor(() => expect(active()).toBe(name));

    await user.click(screen.getByTestId('classification-form-cancel'));

    await waitFor(() => expect(active()).toBe(screen.getByTestId('classification-new')));
  });

  it('focus the person put somewhere else after Cancel is not pulled back', async () => {
    const user = userEvent.setup();
    renderArea(fakeService(list([category(FEE, 'Fee / rate change'), category(INVOICE, 'Invoice / Billing')])));
    await user.click(await screen.findByTestId(`classification-edit-${INVOICE}`));
    await screen.findByTestId('classification-form');
    const other = screen.getByTestId(`classification-edit-${FEE}`);

    // Cancel, and in the same tick the person tabs to another row's Edit button.
    act(() => {
      screen.getByTestId('classification-form-cancel').click();
      other.focus();
    });
    await act(() => new Promise<void>(resolve => setTimeout(resolve, 300)));

    expect(active()).toBe(other);
  });
});
