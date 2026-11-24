/**
 * TodoWizardDialog — the in-app host's create reaches the BFF route (field-mapping-server-write-path-r1 task 024, FR-16).
 *
 * The generic shell is a stub that hands over its config; the test calls the real `onFinish` and checks that the to do is
 * created through `POST /api/v1/child-records/sprk_todo` with the host's `authenticatedFetch` (never
 * `dataService.createRecord`, i.e. Xrm.WebApi), and that the LegalWorkspace To Do widget is still told to refetch
 * (`sprk_todo:created`), which the host's data-service wrapper can no longer do for this create.
 */
import * as React from 'react';
import { renderWithProviders } from '../../../__mocks__/pcfMocks';
import type { ICreateRecordWizardConfig, IFinishContext } from '../../CreateRecordWizard';
import type { IDataService } from '../../../types/serviceInterfaces';
import { TodoWizardDialog } from '../TodoWizardDialog';
import { SPRK_TODO_CHANNEL_NAME, SPRK_TODO_CREATED } from '../todoWizardHostSupport';

const mockShell: { config?: ICreateRecordWizardConfig } = {};
jest.mock('../../CreateRecordWizard', () => ({
  CreateRecordWizard: (props: { config: ICreateRecordWizardConfig }) => {
    mockShell.config = props.config;
    return null;
  },
}));

const NEW_ID = '0b0b0b0b-0000-0000-0000-000000000124';

function makeDataService() {
  return {
    createRecord: jest.fn(),
    retrieveRecord: jest.fn(),
    retrieveMultipleRecords: jest.fn(async () => ({ entities: [] })),
    updateRecord: jest.fn(),
    deleteRecord: jest.fn(),
  };
}

function finishContext(): IFinishContext {
  return {
    association: null,
    uploadedFiles: [],
    selectedActions: [],
    followOn: { email: { to: [] } },
  } as unknown as IFinishContext;
}

describe('TodoWizardDialog — onFinish creates through the BFF route', () => {
  const posted: Array<{ name: string; message: unknown }> = [];
  const RealBroadcastChannel = (globalThis as { BroadcastChannel?: unknown }).BroadcastChannel;

  beforeEach(() => {
    mockShell.config = undefined;
    posted.length = 0;
    (globalThis as { BroadcastChannel?: unknown }).BroadcastChannel = class {
      constructor(private readonly name: string) {}
      postMessage(message: unknown) {
        posted.push({ name: this.name, message });
      }
      close() {}
    };
  });
  afterEach(() => {
    (globalThis as { BroadcastChannel?: unknown }).BroadcastChannel = RealBroadcastChannel;
  });

  it('posts the sprk_todo create to the child-records route, never dataService.createRecord, and broadcasts sprk_todo:created', async () => {
    const dataService = makeDataService();
    const fetchMock = jest.fn(
      async () =>
        ({ ok: true, status: 201, json: async () => ({ id: NEW_ID }), text: async () => '' }) as unknown as Response
    );
    renderWithProviders(
      <TodoWizardDialog
        open
        onClose={jest.fn()}
        dataService={dataService as unknown as IDataService}
        authenticatedFetch={fetchMock as never}
        bffBaseUrl="https://bff.example.test"
      />
    );

    const success = await mockShell.config!.onFinish(finishContext());

    expect(success.title).toBe('To Do created!');
    expect(dataService.createRecord).not.toHaveBeenCalled();
    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe('https://bff.example.test/api/v1/child-records/sprk_todo');
    expect(init.method).toBe('POST');
    expect(posted).toEqual([{ name: SPRK_TODO_CHANNEL_NAME, message: { type: SPRK_TODO_CREATED, todoId: NEW_ID } }]);
  });

  it('broadcasts nothing and throws the server message when the create is refused', async () => {
    jest.spyOn(console, 'error').mockImplementation(() => undefined);
    const dataService = makeDataService();
    const fetchMock = jest.fn(
      async () =>
        ({
          ok: false,
          status: 403,
          json: async () => ({}),
          text: async () => JSON.stringify({ title: 'Forbidden', status: 403, detail: 'You may not create this.' }),
        }) as unknown as Response
    );
    renderWithProviders(
      <TodoWizardDialog
        open
        onClose={jest.fn()}
        dataService={dataService as unknown as IDataService}
        authenticatedFetch={fetchMock as never}
        bffBaseUrl="https://bff.example.test"
      />
    );

    await expect(mockShell.config!.onFinish(finishContext())).rejects.toThrow(/You may not create this/);
    expect(dataService.createRecord).not.toHaveBeenCalled();
    expect(posted).toEqual([]);
  });
});
