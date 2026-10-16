/**
 * Ontology admin Classification area (task 103; D-22, FR-59, D-49):
 *
 *  - the list shows each category's guidance, On/Off, the rules that read it, its 30-day volume and its recall against the floor;
 *    a category never measured shows "Not measured" and no figure; no classifier confidence is shown anywhere;
 *  - a new category is created from the form and the form says it saves Off;
 *  - an edit sends the version the form loaded; a rename the server refuses shows the refusal NAMING the rule, and keeps the typed text;
 *  - the On/Off switch writes through the service and reloads;
 *  - the service shapes the BFF's answers, sends the right routes, and treats a thrown ApiError and a returned non-2xx alike;
 *  - tokens only, light and dark (ADR-021).
 */

import '@testing-library/jest-dom';
import * as fs from 'fs';
import * as path from 'path';
import * as React from 'react';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { FluentProvider, webDarkTheme, webLightTheme } from '@fluentui/react-components';
import {
  ClassificationArea,
  ClassificationRequestError,
  ONTOLOGY_ADMIN_CLASSIFICATION_ROUTE,
  createClassificationService,
  describeRecall,
  describeVolume,
  groupRuleReads,
  parseClassificationList,
} from '..';
import type { ClassificationCategory, ClassificationList, ClassificationService } from '..';

const FEE = '8b62dd84-1fbc-f111-aaaf-3833c5e9614d';
const INVOICE = '71310056-598b-f111-8077-7ced8ddc4cc6';

const fee = (over: Partial<ClassificationCategory> = {}): ClassificationCategory => ({
  id: FEE,
  name: 'Fee / rate change',
  guidance: 'A change to rates or fees.',
  enabled: true,
  version: 27482664,
  volume30Days: 1,
  recall: { status: 'MeetsFloor', recall: 88.89, labelledSetSize: 92, measuredOn: '2026-10-10', measuredBeforeLastEdit: false },
  readBy: [
    {
      policyId: 'p1',
      policyCode: 'POL-COMMIT-BUDGET',
      policyName: 'Budget variance',
      policyEnabled: true,
      versionId: 'v2',
      versionNumber: 2,
      versionState: 'InForce',
    },
    {
      policyId: 'p1',
      policyCode: 'POL-COMMIT-BUDGET',
      policyName: 'Budget variance',
      policyEnabled: true,
      versionId: 'v1',
      versionNumber: 1,
      versionState: 'Earlier',
    },
  ],
  ...over,
});

const invoice = (over: Partial<ClassificationCategory> = {}): ClassificationCategory => ({
  id: INVOICE,
  name: 'Invoice / Billing',
  guidance: null,
  enabled: false,
  version: 26666194,
  volume30Days: 0,
  recall: { status: 'NotMeasured', recall: null, labelledSetSize: null, measuredOn: null, measuredBeforeLastEdit: false },
  readBy: [],
  ...over,
});

const list = (over: Partial<ClassificationList> = {}): ClassificationList => ({
  categories: [fee(), invoice()],
  recallFloor: { percent: 80, minItems: 50 },
  volumeWindowDays: 30,
  volumeAvailable: true,
  referencesAvailable: true,
  ...over,
});

function fakeService(initial: ClassificationList = list()): jest.Mocked<ClassificationService> {
  return {
    list: jest.fn(async () => initial),
    create: jest.fn(async r => ({ id: 'new', name: r.name, guidance: r.guidance, enabled: false, version: 1 })),
    update: jest.fn(async (id, r) => ({ id, name: r.name, guidance: r.guidance, enabled: true, version: r.version + 1 })),
    setEnabled: jest.fn(async (id, enabled, version) => ({ id, name: 'x', guidance: null, enabled, version: version + 1 })),
  };
}

const renderArea = (service: ClassificationService, theme = webLightTheme) =>
  render(
    <FluentProvider theme={theme}>
      <ClassificationArea service={service} />
    </FluentProvider>
  );

describe('ClassificationArea list', () => {
  it('shows guidance, On/Off, the rules that read each category, the 30-day volume and the recall against the floor', async () => {
    renderArea(fakeService());

    const row = await screen.findByTestId(`classification-row-${FEE}`);
    expect(within(row).getByText('Fee / rate change')).toBeInTheDocument();
    expect(within(row).getByText('A change to rates or fees.')).toBeInTheDocument();
    expect(within(row).getByRole('switch', { name: 'Fee / rate change is On' })).toBeChecked();
    // one rule, listed once, though two of its versions name the category
    expect(within(screen.getByTestId(`classification-readers-${FEE}`)).getAllByText('POL-COMMIT-BUDGET')).toHaveLength(1);
    expect(screen.getByTestId(`classification-volume-${FEE}`)).toHaveTextContent('1');
    const recall = screen.getByTestId(`classification-recall-${FEE}`);
    expect(recall).toHaveTextContent('Meets floor');
    expect(recall).toHaveTextContent('88.9% on 92 items, measured 2026-10-10 (floor 80% on 50 items)');
    expect(screen.getByText(/Recall floor: 80% on at least 50 labelled items/)).toBeInTheDocument();
  });

  it('shows "Measured before the last edit" on the recall of a category edited after its measurement, and not on the others', async () => {
    renderArea(
      fakeService({
        ...list(),
        categories: [
          fee({ recall: { status: 'MeetsFloor', recall: 88.89, labelledSetSize: 92, measuredOn: '2026-10-10', measuredBeforeLastEdit: true } }),
          invoice(),
        ],
      })
    );

    expect(await screen.findByTestId(`classification-recall-stale-${FEE}`)).toHaveTextContent('Measured before the last edit');
    expect(screen.queryByTestId(`classification-recall-stale-${INVOICE}`)).not.toBeInTheDocument();
  });

  it('shows Not measured, with no figure, for a category never measured, and No rule / No guidance when there are none', async () => {
    renderArea(fakeService());

    const row = await screen.findByTestId(`classification-row-${INVOICE}`);
    const recall = within(row).getByTestId(`classification-recall-${INVOICE}`);
    expect(recall).toHaveTextContent('Not measured');
    expect(recall).not.toHaveTextContent('%');
    expect(within(row).getByText('No rule')).toBeInTheDocument();
    expect(within(row).getByText('No guidance')).toBeInTheDocument();
    expect(within(row).getByRole('switch', { name: 'Invoice / Billing is Off' })).not.toBeChecked();
  });

  it('shows no classifier confidence anywhere (none exists, #5)', async () => {
    const { container } = renderArea(fakeService());
    await screen.findByTestId('classification-table');

    expect(container.textContent?.toLowerCase()).not.toContain('confidence');
  });

  it('warns that an empty "Read by" is not an answer when the references could not be read, and shows a dash for an unknown volume', async () => {
    renderArea(
      fakeService(
        list({ referencesAvailable: false, volumeAvailable: false, categories: [fee({ readBy: [], volume30Days: null })] })
      )
    );

    expect(await screen.findByTestId('classification-references-unavailable')).toBeInTheDocument();
    expect(screen.getByTestId('classification-volume-unavailable')).toBeInTheDocument();
    expect(screen.getByTestId(`classification-volume-${FEE}`)).toHaveTextContent('—');
  });

  it('shows an error with a retry (never an empty list) when the categories cannot be read', async () => {
    const service = fakeService();
    service.list.mockRejectedValueOnce(new ClassificationRequestError('Dataverse is unavailable.', 503));
    renderArea(service);

    expect(await screen.findByTestId('classification-error')).toHaveTextContent('Dataverse is unavailable.');
    expect(screen.queryByTestId('classification-table')).not.toBeInTheDocument();

    await userEvent.click(screen.getByTestId('classification-retry'));
    expect(await screen.findByTestId('classification-table')).toBeInTheDocument();
    expect(service.list).toHaveBeenCalledTimes(2);
  });
});

describe('ClassificationArea create', () => {
  it('creates a category from the form, says it saves Off, and reloads the list', async () => {
    const service = fakeService();
    renderArea(service);
    await screen.findByTestId('classification-table');

    await userEvent.click(screen.getByTestId('classification-new'));
    expect(screen.getByTestId('classification-form-new-note')).toHaveTextContent('saved Off');
    expect(screen.getByTestId('classification-form-save')).toBeDisabled(); // no name yet

    await userEvent.type(screen.getByTestId('classification-form-name'), '  Settlement ');
    await userEvent.type(screen.getByTestId('classification-form-guidance'), 'An offer to settle.');
    await userEvent.click(screen.getByTestId('classification-form-save'));

    await waitFor(() => expect(service.create).toHaveBeenCalledWith({ name: 'Settlement', guidance: 'An offer to settle.' }));
    expect(await screen.findByTestId('classification-notice')).toHaveTextContent('Settlement was created Off');
    expect(screen.queryByTestId('classification-form')).not.toBeInTheDocument();
    expect(service.list).toHaveBeenCalledTimes(2);
  });

  it('keeps the form and shows the server message when the name is taken', async () => {
    const service = fakeService();
    service.create.mockRejectedValueOnce(
      new ClassificationRequestError('Another triage category already has this name.', 409, 'ontology.admin.classification.name_taken')
    );
    renderArea(service);
    await screen.findByTestId('classification-table');

    await userEvent.click(screen.getByTestId('classification-new'));
    await userEvent.type(screen.getByTestId('classification-form-name'), 'Scheduling');
    await userEvent.click(screen.getByTestId('classification-form-save'));

    expect(await screen.findByTestId('classification-form-error')).toHaveTextContent('already has this name');
    expect(screen.getByTestId('classification-form-name')).toHaveValue('Scheduling');
  });
});

describe('ClassificationArea edit', () => {
  it('sends the guidance edit with the version the form loaded', async () => {
    const service = fakeService();
    renderArea(service);
    await userEvent.click(await screen.findByTestId(`classification-edit-${INVOICE}`));

    expect(screen.getByTestId('classification-form-save')).toBeDisabled(); // nothing changed yet
    await userEvent.type(screen.getByTestId('classification-form-guidance'), 'Bills and amounts due.');
    await userEvent.click(screen.getByTestId('classification-form-save'));

    await waitFor(() =>
      expect(service.update).toHaveBeenCalledWith(INVOICE, {
        name: 'Invoice / Billing',
        guidance: 'Bills and amounts due.',
        version: 26666194,
      })
    );
  });

  it('tells the administrator up front that a rule reads the category and the name cannot change', async () => {
    renderArea(fakeService());
    await userEvent.click(await screen.findByTestId(`classification-edit-${FEE}`));

    expect(screen.getByTestId('classification-form-readers')).toHaveTextContent('Read by POL-COMMIT-BUDGET');
  });

  it('shows a refused rename NAMING the rule, keeps what was typed, and does not close the form', async () => {
    const service = fakeService();
    service.update.mockRejectedValueOnce(
      new ClassificationRequestError(
        'This category cannot be renamed while a rule reads it: POL-COMMIT-BUDGET (version 2, in force).',
        409,
        'ontology.admin.classification.rename_blocked',
        {},
        fee().readBy
      )
    );
    renderArea(service);
    await userEvent.click(await screen.findByTestId(`classification-edit-${FEE}`));

    const name = screen.getByTestId('classification-form-name');
    await userEvent.clear(name);
    await userEvent.type(name, 'Fee change');
    await userEvent.click(screen.getByTestId('classification-form-save'));

    const error = await screen.findByTestId('classification-form-error');
    expect(error).toHaveTextContent('Rename refused');
    expect(error).toHaveTextContent('POL-COMMIT-BUDGET');
    expect(screen.getByTestId('classification-form-blockers')).toHaveTextContent('POL-COMMIT-BUDGET (Budget variance): version 2 (in force), version 1 (earlier version)');
    expect(screen.getByTestId('classification-form-name')).toHaveValue('Fee change');
    expect(service.list).toHaveBeenCalledTimes(1); // nothing was saved, so nothing reloaded
  });

  it.each([
    ['ontology.admin.classification.rename_unverifiable', 'Whether a rule reads this category cannot be checked from your business unit, so it cannot be renamed here.'],
    ['ontology.admin.classification.rename_routed', 'This category cannot be renamed while the routing configuration names it (Communication:CategoryRouting:CategoryToTeam).'],
  ])('shows the server refusal %s as a refused rename and keeps what was typed', async (reasonCode, message) => {
    const service = fakeService();
    service.update.mockRejectedValueOnce(new ClassificationRequestError(message, 409, reasonCode));
    renderArea(service);
    await userEvent.click(await screen.findByTestId(`classification-edit-${INVOICE}`));
    const name = screen.getByTestId('classification-form-name');
    await userEvent.clear(name);
    await userEvent.type(name, 'Billing');
    await userEvent.click(screen.getByTestId('classification-form-save'));

    const error = await screen.findByTestId('classification-form-error');
    expect(error).toHaveTextContent('Rename refused');
    expect(error).toHaveTextContent(message);
    expect(screen.getByTestId('classification-form-name')).toHaveValue('Billing');
    expect(screen.queryByTestId('classification-form-blockers')).not.toBeInTheDocument();
  });

  it('shows the field message for a bad value', async () => {
    const service = fakeService();
    service.update.mockRejectedValueOnce(
      new ClassificationRequestError('One or more validation errors occurred.', 400, 'ontology.admin.classification.invalid', {
        guidance: ['Guidance can be at most 2000 characters.'],
      })
    );
    renderArea(service);
    await userEvent.click(await screen.findByTestId(`classification-edit-${INVOICE}`));
    await userEvent.type(screen.getByTestId('classification-form-guidance'), 'x');
    await userEvent.click(screen.getByTestId('classification-form-save'));

    expect(await screen.findByText('Guidance can be at most 2000 characters.')).toBeInTheDocument();
  });

  it('cancels without saving', async () => {
    const service = fakeService();
    renderArea(service);
    await userEvent.click(await screen.findByTestId(`classification-edit-${INVOICE}`));
    await userEvent.click(screen.getByTestId('classification-form-cancel'));

    expect(screen.queryByTestId('classification-form')).not.toBeInTheDocument();
    expect(service.update).not.toHaveBeenCalled();
  });
});

describe('ClassificationArea On/Off', () => {
  it('turns a category On through the service with its version, then reloads', async () => {
    const service = fakeService();
    renderArea(service);

    await userEvent.click(await screen.findByRole('switch', { name: 'Invoice / Billing is Off' }));

    await waitFor(() => expect(service.setEnabled).toHaveBeenCalledWith(INVOICE, true, 26666194));
    expect(await screen.findByTestId('classification-notice')).toHaveTextContent('Invoice / Billing is On.');
    expect(service.list).toHaveBeenCalledTimes(2);
  });

  it('asks for confirmation BEFORE turning Off a category a rule reads, and writes nothing until the administrator confirms', async () => {
    const service = fakeService();
    renderArea(service);

    await userEvent.click(await screen.findByRole('switch', { name: 'Fee / rate change is On' }));

    const confirm = await screen.findByTestId('classification-confirm-off');
    expect(confirm).toHaveTextContent('Turn Fee / rate change Off?');
    // new emails stop being classified as X; the rule still matches emails ALREADY classified as X (it does not "stop matching")
    expect(confirm).toHaveTextContent('New emails will no longer be classified as Fee / rate change.');
    expect(confirm).toHaveTextContent('POL-COMMIT-BUDGET still matches emails already classified as Fee / rate change.');
    expect(confirm).not.toHaveTextContent('cannot match');
    expect(service.setEnabled).not.toHaveBeenCalled();
    expect(screen.getByRole('switch', { name: 'Fee / rate change is On' })).toBeChecked();
  });

  it('turns the category Off once confirmed, with its version, and says what Off does and does not do', async () => {
    const service = fakeService();
    renderArea(service);
    await userEvent.click(await screen.findByRole('switch', { name: 'Fee / rate change is On' }));

    await userEvent.click(await screen.findByTestId('classification-confirm-off-yes'));

    await waitFor(() => expect(service.setEnabled).toHaveBeenCalledWith(FEE, false, 27482664));
    const notice = await screen.findByTestId('classification-notice');
    expect(notice).toHaveTextContent('Fee / rate change is Off. New emails will no longer be classified as Fee / rate change.');
    expect(notice).toHaveTextContent('POL-COMMIT-BUDGET still matches emails already classified as Fee / rate change.');
    expect(notice).not.toHaveTextContent('cannot match');
    expect(screen.queryByTestId('classification-confirm-off')).not.toBeInTheDocument();
  });

  it('leaves the category On, and writes nothing, when the administrator keeps it', async () => {
    const service = fakeService();
    renderArea(service);
    await userEvent.click(await screen.findByRole('switch', { name: 'Fee / rate change is On' }));

    await userEvent.click(await screen.findByTestId('classification-confirm-off-no'));

    expect(screen.queryByTestId('classification-confirm-off')).not.toBeInTheDocument();
    expect(service.setEnabled).not.toHaveBeenCalled();
    expect(screen.getByRole('switch', { name: 'Fee / rate change is On' })).toBeChecked();
  });

  it('turns Off at once, with no confirmation, a category no rule reads', async () => {
    const service = fakeService({
      ...list(),
      categories: [fee({ readBy: [] }), invoice()],
    });
    renderArea(service);

    await userEvent.click(await screen.findByRole('switch', { name: 'Fee / rate change is On' }));

    await waitFor(() => expect(service.setEnabled).toHaveBeenCalledWith(FEE, false, 27482664));
    expect(screen.queryByTestId('classification-confirm-off')).not.toBeInTheDocument();
    expect(await screen.findByTestId('classification-notice')).toHaveTextContent('Fee / rate change is Off.');
  });

  it('turns On at once, with no confirmation, even for a category a rule reads', async () => {
    const service = fakeService({
      ...list(),
      categories: [fee({ enabled: false }), invoice()],
    });
    renderArea(service);

    await userEvent.click(await screen.findByRole('switch', { name: 'Fee / rate change is Off' }));

    await waitFor(() => expect(service.setEnabled).toHaveBeenCalledWith(FEE, true, 27482664));
    expect(screen.queryByTestId('classification-confirm-off')).not.toBeInTheDocument();
  });

  it('disables the switch of a row while its edit form is open, so a reload cannot remount the form and lose what was typed', async () => {
    const service = fakeService();
    renderArea(service);
    await userEvent.click(await screen.findByTestId(`classification-edit-${INVOICE}`));
    await userEvent.type(screen.getByTestId('classification-form-guidance'), 'Half-typed guidance');

    expect(screen.getByRole('switch', { name: 'Invoice / Billing is Off' })).toBeDisabled();
    // another row's switch is untouched
    expect(screen.getByRole('switch', { name: 'Fee / rate change is On' })).toBeEnabled();
    expect(service.list).toHaveBeenCalledTimes(1); // nothing reloaded behind the open form
    expect(screen.getByTestId('classification-form-guidance')).toHaveValue('Half-typed guidance');

    await userEvent.click(screen.getByTestId('classification-form-cancel'));
    expect(screen.getByRole('switch', { name: 'Invoice / Billing is Off' })).toBeEnabled();
  });

  it('shows the server message when Dataverse refuses the change (a Console User)', async () => {
    const service = fakeService();
    service.setEnabled.mockRejectedValueOnce(
      new ClassificationRequestError('You do not have permission to change triage categories.', 403, 'ontology.admin.classification.not_permitted')
    );
    renderArea(service);

    await userEvent.click(await screen.findByRole('switch', { name: 'Invoice / Billing is Off' }));

    expect(await screen.findByTestId('classification-notice')).toHaveTextContent('You do not have permission');
  });
});

describe('classificationView', () => {
  it('judges recall against the floor and never shows a figure for an unmeasured category', () => {
    const floor = { percent: 80, minItems: 50 };
    expect(describeRecall({ status: 'BelowFloor', recall: 70, labelledSetSize: 92, measuredOn: '2026-10-10', measuredBeforeLastEdit: false }, floor)).toMatchObject({
      label: 'Below floor',
      tone: 'critical',
    });
    expect(describeRecall({ status: 'SetTooSmall', recall: 95, labelledSetSize: 30, measuredOn: null, measuredBeforeLastEdit: false }, floor)).toMatchObject({
      label: 'Set too small',
      tone: 'warning',
      detail: '95.0% on 30 items (floor 80% on 50 items)',
    });
    expect(describeRecall({ status: 'NotMeasured', recall: null, labelledSetSize: null, measuredOn: null, measuredBeforeLastEdit: false }, floor)).toEqual({
      label: 'Not measured',
      tone: 'neutral',
      detail: null,
      staleNote: null,
    });
    // a status that claims a measurement but carries no figure is not shown as one
    expect(describeRecall({ status: 'MeetsFloor', recall: null, labelledSetSize: 92, measuredOn: null, measuredBeforeLastEdit: false }, floor).label).toBe('Not measured');
  });

  it('marks a recall measured before the category was last edited, and only then', () => {
    const floor = { percent: 80, minItems: 50 };
    const measured = { status: 'MeetsFloor' as const, recall: 88.89, labelledSetSize: 92, measuredOn: '2026-10-10' };

    expect(describeRecall({ ...measured, measuredBeforeLastEdit: true }, floor).staleNote).toBe('Measured before the last edit');
    expect(describeRecall({ ...measured, measuredBeforeLastEdit: false }, floor).staleNote).toBeNull();
    // a category never measured has no figure to be stale, whatever the flag says
    expect(
      describeRecall({ status: 'NotMeasured', recall: null, labelledSetSize: null, measuredOn: null, measuredBeforeLastEdit: true }, floor).staleNote
    ).toBeNull();
  });

  it('groups version references by rule, newest version first', () => {
    const groups = groupRuleReads(fee().readBy);
    expect(groups).toHaveLength(1);
    expect(groups[0].versions).toBe('version 2 (in force), version 1 (earlier version)');
  });

  it('shows a dash for an unknown volume and zero for none', () => {
    expect(describeVolume(null)).toBe('—');
    expect(describeVolume(0)).toBe('0');
  });
});

describe('createClassificationService', () => {
  const ok = (json: unknown, status = 200) => ({ ok: true, status, json: async () => json }) as unknown as Response;
  const refused = (status: number, json: unknown) => ({ ok: false, status, json: async () => json }) as unknown as Response;
  const base = 'https://bff.example/';
  const root = `https://bff.example${ONTOLOGY_ADMIN_CLASSIFICATION_ROUTE}`;

  it('reads the list from the categories route and shapes it', async () => {
    const fetch = jest.fn(async () =>
      ok({
        categories: [{ ...fee(), recall: fee().recall }],
        recallFloor: { percent: 80, minItems: 50 },
        volumeWindowDays: 30,
        volumeAvailable: true,
        referencesAvailable: true,
      })
    );

    const result = await createClassificationService({ fetch, baseUrl: base }).list();

    expect(fetch).toHaveBeenCalledWith(root, expect.objectContaining({ method: 'GET' }));
    expect(ONTOLOGY_ADMIN_CLASSIFICATION_ROUTE).toBe('/api/v1/ontology/admin/classification/categories');
    expect(result.categories[0].readBy).toHaveLength(2);
    expect(result.categories[0].recall.recall).toBe(88.89);
  });

  it('sends create, edit and On/Off to the right routes with the right bodies', async () => {
    const fetch = jest.fn(async () => ok({ id: 'x', name: 'N', guidance: null, enabled: false, version: 5 }, 201));
    const service = createClassificationService({ fetch, baseUrl: base });

    await service.create({ name: 'N', guidance: 'g' });
    await service.update(FEE, { name: 'N', guidance: 'g', version: 4 });
    await service.setEnabled(FEE, true, 4);

    const calls = fetch.mock.calls as unknown as Array<[string, RequestInit]>;
    expect(calls[0][0]).toBe(root);
    expect(calls[0][1].method).toBe('POST');
    expect(JSON.parse(calls[0][1].body as string)).toEqual({ name: 'N', guidance: 'g' }); // no enabled member: a new category saves Off
    expect(calls[1][0]).toBe(`${root}/${FEE}`);
    expect(calls[1][1].method).toBe('PATCH');
    expect(JSON.parse(calls[1][1].body as string)).toEqual({ name: 'N', guidance: 'g', version: 4 });
    expect(calls[2][0]).toBe(`${root}/${FEE}/enabled`);
    expect(calls[2][1].method).toBe('PUT');
    expect(JSON.parse(calls[2][1].body as string)).toEqual({ enabled: true, version: 4 });
  });

  it('reads a refused rename from a returned 409 and from a thrown ApiError alike, with the rules that block it', async () => {
    const problem = {
      detail: 'This category cannot be renamed while a rule reads it: POL-COMMIT-BUDGET (version 2, in force).',
      reasonCode: 'ontology.admin.classification.rename_blocked',
      blockedBy: fee().readBy,
    };
    const returned = createClassificationService({ fetch: jest.fn(async () => refused(409, problem)), baseUrl: base });
    const thrown = createClassificationService({
      fetch: jest.fn(async () => {
        throw Object.assign(new Error('Conflict'), { name: 'ApiError', status: 409, problemDetails: problem });
      }),
      baseUrl: base,
    });

    for (const service of [returned, thrown]) {
      const err = await service.update(FEE, { name: 'x', guidance: '', version: 1 }).catch((e: unknown) => e);
      expect(err).toBeInstanceOf(ClassificationRequestError);
      const failure = err as ClassificationRequestError;
      expect(failure.httpStatus).toBe(409);
      expect(failure.reasonCode).toBe('ontology.admin.classification.rename_blocked');
      expect(failure.message).toContain('POL-COMMIT-BUDGET');
      expect(failure.blockedBy.map(b => b.policyCode)).toEqual(['POL-COMMIT-BUDGET', 'POL-COMMIT-BUDGET']);
    }
  });

  it('reads the measuredBeforeLastEdit flag of a recall, and treats anything but true as false', () => {
    const body = (flag: unknown) => ({
      categories: [
        {
          id: FEE,
          name: 'Fee / rate change',
          version: 1,
          recall: { status: 'MeetsFloor', recall: 90, labelledSetSize: 60, measuredOn: '2026-10-10', measuredBeforeLastEdit: flag },
        },
      ],
    });

    expect(parseClassificationList(body(true)).categories[0].recall.measuredBeforeLastEdit).toBe(true);
    expect(parseClassificationList(body(false)).categories[0].recall.measuredBeforeLastEdit).toBe(false);
    expect(parseClassificationList(body('yes')).categories[0].recall.measuredBeforeLastEdit).toBe(false);
    expect(parseClassificationList(body(undefined)).categories[0].recall.measuredBeforeLastEdit).toBe(false);
  });

  it('carries the field errors of a 400', async () => {
    const service = createClassificationService({
      fetch: jest.fn(async () => refused(400, { errors: { name: ['Name is required.'] }, reasonCode: 'ontology.admin.classification.invalid' })),
      baseUrl: base,
    });

    const err = (await service.create({ name: '', guidance: '' }).catch((e: unknown) => e)) as ClassificationRequestError;

    expect(err.fieldErrors.name).toEqual(['Name is required.']);
  });

  it('is an error, never an empty list, for an answer that is not a category list', async () => {
    expect(() => parseClassificationList({ value: [] })).toThrow(ClassificationRequestError);
    expect(() => parseClassificationList(null)).toThrow(ClassificationRequestError);
    await expect(
      createClassificationService({ fetch: jest.fn(async () => ok({ nope: true })), baseUrl: base }).list()
    ).rejects.toBeInstanceOf(ClassificationRequestError);
  });

  it('reads a recall it cannot understand as Not measured, never as a figure', () => {
    const parsed = parseClassificationList({
      categories: [{ id: FEE, name: 'Fee', version: 1, recall: { status: 'Surprising', recall: 99 }, readBy: [{ junk: true }] }],
    });

    expect(parsed.categories[0].recall).toEqual({ status: 'NotMeasured', recall: null, labelledSetSize: null, measuredOn: null, measuredBeforeLastEdit: false });
    expect(parsed.categories[0].readBy).toEqual([]);
  });

  it('treats a network failure as an error without a status', async () => {
    const service = createClassificationService({
      fetch: jest.fn(async () => {
        throw new Error('network');
      }),
      baseUrl: base,
    });

    const err = (await service.list().catch((e: unknown) => e)) as ClassificationRequestError;

    expect(err).toBeInstanceOf(ClassificationRequestError);
    expect(err.httpStatus).toBeNull();
  });
});

describe('theming (ADR-021)', () => {
  it('renders under the light and the dark theme', async () => {
    for (const theme of [webLightTheme, webDarkTheme]) {
      const { unmount } = renderArea(fakeService(), theme);
      expect(await screen.findByTestId('classification-table')).toBeInTheDocument();
      unmount();
    }
  });

  it('uses Fluent tokens only: no hex, rgb or named colour literal in the Classification sources', () => {
    const dir = path.resolve(__dirname, '..');
    const sources = fs
      .readdirSync(dir)
      .filter(f => /\.(ts|tsx)$/.test(f))
      .map(f => ({ f, text: fs.readFileSync(path.join(dir, f), 'utf8') }));
    expect(sources.length).toBeGreaterThanOrEqual(5);
    for (const { f, text } of sources) {
      const code = text.replace(/\/\*[\s\S]*?\*\//g, '').replace(/^\s*\/\/.*$/gm, '');
      expect({ f, hex: /#[0-9a-fA-F]{3,8}\b/.test(code) }).toEqual({ f, hex: false });
      expect({ f, rgb: /\brgba?\(/.test(code) }).toEqual({ f, rgb: false });
    }
  });
});
