// sprk_event ribbon commands — "Add Memo" creates the memo through the BFF, never a quick create form and never a direct
// Xrm create (field-mapping-server-write-path-r1 task 051, FR-24). The shared create is Spaarke.SecureChild.Ribbon.createChild
// (sprk_secure_child_ribbon.js, pinned by secureChildRibbonScript.test.ts); here it is a recording stub, so this test pins what
// the Event command does around it.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadRibbon } from './ribbonHarness.mjs';

const EVENT_ID = 'a1510000-0000-4000-8000-000000000001';
const MEMO_ID = 'b1510000-0000-4000-8000-000000000002';

function setup({ createResult = { id: MEMO_ID }, withHelper = true } = {}) {
  const calls = { forms: [], alerts: [], notifications: [], creates: [], saves: 0, webApiCreates: 0 };
  const xrm = {
    WebApi: {
      createRecord: () => {
        calls.webApiCreates++;
        return Promise.resolve({ id: 'x' });
      },
    },
    Navigation: {
      openForm: f => {
        calls.forms.push(JSON.parse(JSON.stringify(f)));
        return Promise.resolve({});
      },
      openAlertDialog: a => {
        calls.alerts.push(a);
        return Promise.resolve();
      },
      openConfirmDialog: () => Promise.resolve({ confirmed: true }),
    },
    App: {
      addGlobalNotification: n => {
        calls.notifications.push(n);
        return Promise.resolve();
      },
    },
    Utility: { getGlobalContext: () => ({ userSettings: { userId: '{U}', userName: 'U' }, getQueryStringParameters: () => ({}) }) },
  };
  const { Spaarke } = loadRibbon(xrm);
  if (withHelper) {
    Spaarke.SecureChild = {
      Ribbon: {
        hostOf: fc => {
          const id = String(fc.data.entity.getId() || '').replace(/[{}]/g, '').toLowerCase();
          return id ? { entity: fc.data.entity.getEntityName(), id } : null;
        },
        createChild: (table, host, selected) => {
          calls.creates.push({ table, host: { ...host }, selected });
          return Promise.resolve(createResult);
        },
      },
    };
  }
  let history = '[]';
  const formContext = {
    data: {
      entity: { getId: () => `{${EVENT_ID.toUpperCase()}}`, getEntityName: () => 'sprk_event' },
      save: () => {
        calls.saves++;
        return Promise.resolve();
      },
    },
    getAttribute: name => (name === 'sprk_eventhistory' ? { getValue: () => history, setValue: v => (history = v) } : null),
  };
  return { Spaarke, formContext, calls, getHistory: () => history };
}

test('Add Memo creates the memo through the shared BFF create, filed under the event, then records history and opens it', async () => {
  const { Spaarke, formContext, calls, getHistory } = setup();
  await Spaarke.Event.AddMemo(formContext);

  assert.equal(calls.creates.length, 1);
  assert.equal(calls.creates[0].table, 'sprk_memo');
  assert.deepEqual(calls.creates[0].host, { entity: 'sprk_event', id: EVENT_ID });
  assert.equal(calls.creates[0].selected, null);
  assert.equal(calls.saves, 1);
  assert.match(getHistory(), /memo_added/);
  assert.deepEqual(calls.forms, [{ entityName: 'sprk_memo', entityId: MEMO_ID }]);
  assert.equal(calls.notifications.length, 1);
});

test('Add Memo never opens a quick create form and never creates through Xrm.WebApi (must not fire)', async () => {
  const { Spaarke, formContext, calls } = setup();
  await Spaarke.Event.AddMemo(formContext);

  assert.equal(calls.webApiCreates, 0);
  assert.ok(calls.forms.every(f => !('useQuickCreateForm' in f) && !('createFromEntity' in f)));
});

test('Add Memo with the create helper not loaded creates nothing and tells the user (no quick create fallback)', async () => {
  const { Spaarke, formContext, calls } = setup({ withHelper: false });
  await Spaarke.Event.AddMemo(formContext);

  assert.equal(calls.creates.length, 0);
  assert.equal(calls.webApiCreates, 0);
  assert.equal(calls.forms.length, 0);
  assert.equal(calls.saves, 0);
  assert.equal(calls.alerts.length, 1);
  assert.match(calls.alerts[0].text, /Nothing was created/);
});

test('Add Memo when the BFF create did not happen (refused) saves nothing and opens nothing', async () => {
  const { Spaarke, formContext, calls, getHistory } = setup({ createResult: null });
  await Spaarke.Event.AddMemo(formContext);

  assert.equal(calls.creates.length, 1);
  assert.equal(calls.saves, 0);
  assert.equal(calls.forms.length, 0);
  assert.equal(getHistory(), '[]');
});
