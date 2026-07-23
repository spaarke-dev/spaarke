/**
 * launchSurface.test.ts — the hand-off orchestrator lifecycle (task 012, design §1).
 * Covers: envelope build (preset authoritative), wizard launch + URL id + result
 * read on close + cleanup, OOB-form launch + savedEntityReference return, unmapped
 * consumertype, and no-Xrm-host degradation.
 */
import { buildHandoffEnvelope, launchSurface, normalizeOobFormDefaults } from '../surfaceHandoff/launchSurface';
import { resolveSurfaceLaunch, EVENT_SUBTYPE_TASK_GUID } from '../surfaceHandoff/surfaceLaunchRegistry';
import { writeHandoffResult } from '../surfaceHandoff/handoffStorage';

/* eslint-disable @typescript-eslint/no-explicit-any */

function installXrm(navigateTo: jest.Mock): void {
  (window as any).Xrm = { Navigation: { navigateTo } };
}
function clearXrm(): void {
  delete (window as any).Xrm;
}

describe('buildHandoffEnvelope', () => {
  it('merges the registry preset AUTHORITATIVELY over draftValues', () => {
    const entry = resolveSurfaceLaunch('create-task')!;
    const env = buildHandoffEnvelope('h-1', entry, {
      consumerType: 'create-task',
      bffBaseUrl: 'https://bff.example',
      draftValues: { title: 'Follow up', sprk_eventtype_ref: 'SHOULD-BE-OVERRIDDEN' },
      fileIds: ['f1'],
    });
    expect(env.draftValues.title).toBe('Follow up');
    // preset wins on conflict — the Task subtype GUID is authoritative.
    expect(env.draftValues.sprk_eventtype_ref).toBe(EVENT_SUBTYPE_TASK_GUID);
    expect(env.fileIds).toEqual(['f1']);
    expect(env.source.consumerType).toBe('create-task');
    expect(env.target).toEqual({ surface: 'sprk_createeventwizard', kind: 'wizard' });
    expect(env.resolvedLookups).toEqual({}); // task 013 slot, empty now
  });
});

describe('normalizeOobFormDefaults (D-013-02)', () => {
  it('maps the create-todo Action draft keys to sprk_ logical column names', () => {
    const out = normalizeOobFormDefaults('create-todo', {
      title: 'Call client',
      description: 'Re: NDA',
      priority_suggestion: 'High',
    });
    expect(out).toEqual({
      sprk_name: 'Call client',
      sprk_description: 'Re: NDA',
      // KEY mapped; VALUE passed through as-is (option-set coercion is a follow-up).
      sprk_priority: 'High',
    });
  });

  it('passes through unknown keys unchanged (tolerant partial map)', () => {
    const out = normalizeOobFormDefaults('create-todo', {
      title: 'Call client',
      sprk_regardingcommunication: 'abc', // already a logical name — untouched
      note: 'freeform', // unmapped — untouched
    });
    expect(out).toEqual({
      sprk_name: 'Call client',
      sprk_regardingcommunication: 'abc',
      note: 'freeform',
    });
  });

  it('returns the draft values unchanged for a consumer without a key map', () => {
    const draft = { title: 'x', foo: 1 };
    expect(normalizeOobFormDefaults('create-matter', draft)).toBe(draft);
  });
});

describe('launchSurface — wizard kind', () => {
  beforeEach(() => sessionStorage.clear());
  afterEach(() => clearXrm());

  it('launches the web resource with the handoff id on the URL, reads the result, cleans up', async () => {
    // The wizard writes its result during the (awaited) navigateTo, then the
    // modal closes (promise resolves).
    const navigateTo = jest.fn(async (pageInput: any) => {
      // Extract the handoff id the orchestrator put on the launch URL.
      const m = /handoffId=([^&]+)/.exec(pageInput.data);
      const id = decodeURIComponent(m![1]);
      writeHandoffResult(id, { committed: true, recordId: 'matter-99' });
    });
    installXrm(navigateTo);

    const outcome = await launchSurface({
      consumerType: 'create-matter',
      bffBaseUrl: 'https://bff.example',
      draftValues: { sprk_mattername: 'Acme' },
    });

    expect(navigateTo).toHaveBeenCalledTimes(1);
    const [pageInput, navOptions] = navigateTo.mock.calls[0];
    expect(pageInput.pageType).toBe('webresource');
    expect(pageInput.webresourceName).toBe('sprk_creatematterwizard');
    expect(pageInput.data).toContain('handoffId=');
    expect(pageInput.data).toContain('bffBaseUrl=');
    expect(navOptions.target).toBe(2); // in-page modal
    expect(navOptions.title).toBe('Create New Matter');

    expect(outcome.launched).toBe(true);
    expect(outcome.result).toEqual({ committed: true, recordId: 'matter-99' });
    // cleanup: both keys gone
    expect(sessionStorage.getItem(`sprk.handoff.${outcome.handoffId}`)).toBeNull();
    expect(sessionStorage.getItem(`sprk.handoff.${outcome.handoffId}.result`)).toBeNull();
  });

  it('infers cancellation when the wizard wrote no result', async () => {
    const navigateTo = jest.fn(async () => {
      /* user dismissed — no result written */
    });
    installXrm(navigateTo);
    const outcome = await launchSurface({ consumerType: 'create-matter', bffBaseUrl: 'https://bff' });
    expect(outcome.launched).toBe(true);
    expect(outcome.result).toEqual({ committed: false, cancelled: true });
  });

  it('stores the envelope BEFORE launch so the surface can read it on boot', async () => {
    let envelopeAtLaunch: string | null = null;
    const navigateTo = jest.fn(async (pageInput: any) => {
      const id = decodeURIComponent(/handoffId=([^&]+)/.exec(pageInput.data)![1]);
      envelopeAtLaunch = sessionStorage.getItem(`sprk.handoff.${id}`);
    });
    installXrm(navigateTo);
    await launchSurface({
      consumerType: 'create-task',
      bffBaseUrl: 'https://bff',
      draftValues: { title: 'T' },
    });
    expect(envelopeAtLaunch).toBeTruthy();
    const parsed = JSON.parse(envelopeAtLaunch!);
    expect(parsed.draftValues.sprk_eventtype_ref).toBe(EVENT_SUBTYPE_TASK_GUID);
  });
});

describe('launchSurface — oob-form kind', () => {
  beforeEach(() => sessionStorage.clear());
  afterEach(() => clearXrm());

  it('launches an entityrecord form pre-seeded and returns the savedEntityReference', async () => {
    const navigateTo = jest.fn(async () => ({
      savedEntityReference: [{ id: '{TODO-42}', entityType: 'sprk_todo', name: 'Call client' }],
    }));
    installXrm(navigateTo);

    const outcome = await launchSurface({
      consumerType: 'create-todo',
      bffBaseUrl: 'https://bff',
      draftValues: { sprk_name: 'Call client', sprk_description: 'Re: NDA' },
    });

    const [pageInput] = navigateTo.mock.calls[0];
    expect(pageInput.pageType).toBe('entityrecord');
    expect(pageInput.entityName).toBe('sprk_todo');
    expect(pageInput.data).toEqual({ sprk_name: 'Call client', sprk_description: 'Re: NDA' });

    expect(outcome.launched).toBe(true);
    // GUID normalized (braces stripped)
    expect(outcome.result).toEqual({ committed: true, recordId: 'TODO-42' });
  });

  it('normalizes the create-todo Action draft keys to logical names on the form data (D-013-02)', async () => {
    const navigateTo = jest.fn(async () => ({
      savedEntityReference: [{ id: 'todo-7', entityType: 'sprk_todo', name: 'Call client' }],
    }));
    installXrm(navigateTo);

    // The CREATE-TODO@v1 Action emits its own field vocabulary — NOT logical names.
    const outcome = await launchSurface({
      consumerType: 'create-todo',
      bffBaseUrl: 'https://bff',
      draftValues: { title: 'Call client', description: 'Re: NDA', priority_suggestion: 'High' },
    });

    const [pageInput] = navigateTo.mock.calls[0];
    expect(pageInput.pageType).toBe('entityrecord');
    // Keys are renamed to sprk_ logical columns so the OOB form actually pre-fills.
    expect(pageInput.data).toEqual({
      sprk_name: 'Call client',
      sprk_description: 'Re: NDA',
      sprk_priority: 'High',
    });
    expect(outcome.result).toEqual({ committed: true, recordId: 'todo-7' });
  });

  it('treats a dismissed OOB form (no savedEntityReference) as cancelled', async () => {
    const navigateTo = jest.fn(async () => undefined);
    installXrm(navigateTo);
    const outcome = await launchSurface({ consumerType: 'create-todo', bffBaseUrl: 'https://bff' });
    expect(outcome.result).toEqual({ committed: false, cancelled: true });
  });
});

describe('launchSurface — degradation', () => {
  beforeEach(() => sessionStorage.clear());
  afterEach(() => clearXrm());

  it('returns launched:false for an unmapped consumertype (no envelope written)', async () => {
    installXrm(jest.fn());
    const outcome = await launchSurface({ consumerType: 'create-widget', bffBaseUrl: 'https://bff' });
    expect(outcome.launched).toBe(false);
    expect(outcome.handoffId).toBe('');
  });

  it('returns launched:false when no Xrm host is reachable, and cleans up', async () => {
    clearXrm();
    const outcome = await launchSurface({ consumerType: 'create-matter', bffBaseUrl: 'https://bff' });
    expect(outcome.launched).toBe(false);
    // envelope was written then cleaned up
    expect(sessionStorage.getItem(`sprk.handoff.${outcome.handoffId}`)).toBeNull();
  });
});
