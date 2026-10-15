// sprk_event ribbon commands — the status is statuscode + its paired statecode, never the deprecated second status column
// (spaarke-ontology-platform-r1 task 066, D-28).
//
// The values are the LIVE sprk_event statuscode option set (spaarkedev1 describe 2026-10-09): Draft 1 [0], Open 659490001 [0],
// Completed 659490002 [0], Closed 659490003 [0], On Hold 659490006 [0], Reassigned 659490007 [0], No Further Action 2 [1],
// Cancelled 659490004 [1], Transferred 659490005 [1]. The BFF test EventStatusDeprecationTests pins this table to the
// data-model row and to Spaarke.Dataverse.EventStatusCode.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadRibbon } from './ribbonHarness.mjs';

// Built from parts so the repo-wide deprecation guard (EventStatusDeprecationTests) stays literal-free.
const DEPRECATED_COLUMN = ['sprk', 'eventstatus'].join('_');

const LIVE = {
  DRAFT: [1, 0],
  OPEN: [659490001, 0],
  COMPLETED: [659490002, 0],
  CLOSED: [659490003, 0],
  ON_HOLD: [659490006, 0],
  REASSIGNED: [659490007, 0],
  ARCHIVED: [2, 1], // No Further Action
  CANCELLED: [659490004, 1],
  TRANSFERRED: [659490005, 1],
};

function xrmDouble() {
  const updates = [];
  return {
    updates,
    WebApi: {
      updateRecord: (entity, id, data) => {
        updates.push({ entity, id, data: JSON.parse(JSON.stringify(data)) }); // plain object: the script runs in a vm realm
        return Promise.resolve({ id });
      },
    },
    Navigation: { openConfirmDialog: () => Promise.resolve({ confirmed: true }), openAlertDialog: () => Promise.resolve() },
    App: { addGlobalNotification: () => Promise.resolve() },
    Utility: {
      getGlobalContext: () => ({ userSettings: { userId: '{U}', userName: 'U' }, getQueryStringParameters: () => ({}) }),
    },
  };
}

/** A form with the attributes the commands touch; the deprecated status attribute is deliberately present so a stale write is visible. */
function formDouble(extra = {}) {
  const attrs = {
    [DEPRECATED_COLUMN]: { value: 1, getValue() { return this.value; }, setValue(v) { this.value = v; this.written = true; } },
    sprk_eventhistory: { value: null, getValue() { return this.value; }, setValue(v) { this.value = v; } },
    sprk_completeddate: { getValue: () => null, setValue() {} },
    ownerid: { getValue: () => null, setValue() {} },
    ...extra,
  };
  const calls = { saved: 0, refreshed: 0 };
  return {
    attrs,
    calls,
    getAttribute: name => attrs[name] ?? null,
    data: {
      entity: { getId: () => '{AAAAAAAA-0000-0000-0000-000000000001}' },
      save: () => { calls.saved += 1; return Promise.resolve(); },
      refresh: () => { calls.refreshed += 1; return Promise.resolve(); },
    },
  };
}

const flush = () => new Promise(r => setTimeout(r, 0));

test('the ribbon status constants are the live statuscode values, each with its live statecode', () => {
  const xrm = xrmDouble();
  const { Spaarke } = loadRibbon(xrm);

  for (const [name, [status, state]] of Object.entries(LIVE)) {
    assert.equal(Spaarke.Event.EventStatus[name], status, name);
    assert.equal(Spaarke.Event.StateOfStatus[status], state, `${name} statecode`);
  }
});

const FORM_COMMANDS = [
  ['Complete', 'COMPLETED', (S, f) => S.Event._executeComplete(f, '2026-10-09', null)],
  ['Cancel', 'CANCELLED', (S, f) => S.Event._executeCancel(f, '')],
  ['Close', 'CLOSED', (S, f) => S.Event._executeClose(f)],
  ['On Hold', 'ON_HOLD', (S, f) => S.Event._executePutOnHold(f)],
  ['Resume', 'OPEN', (S, f) => S.Event.ResumeEvent(f)],
  ['Archive', 'ARCHIVED', (S, f) => S.Event._executeArchive(f)],
  ['Reassign', 'REASSIGNED', (S, f) => S.Event._executeReassign(f, { id: '{B}', name: 'B' })],
];

for (const [label, key, run] of FORM_COMMANDS) {
  test(`${label} (form) saves the form, then PATCHes statuscode + statecode, and never touches the deprecated column`, async () => {
    const xrm = xrmDouble();
    const { Spaarke } = loadRibbon(xrm);
    const form = formDouble();

    run(Spaarke, form);
    await flush();

    const [status, state] = LIVE[key];
    assert.equal(form.calls.saved, 1);
    assert.deepEqual(xrm.updates, [
      { entity: 'sprk_event', id: 'AAAAAAAA-0000-0000-0000-000000000001', data: { statuscode: status, statecode: state } },
    ]);
    assert.equal(form.attrs[DEPRECATED_COLUMN].written, undefined, 'the deprecated attribute is not written');
    assert.equal(form.calls.refreshed, 1);
  });
}

test('Complete (grid, bulk) writes statuscode Completed + statecode Active + the Date Only completed date', async () => {
  const xrm = xrmDouble();
  const { Spaarke } = loadRibbon(xrm, new Date(2026, 9, 9, 12, 0));

  Spaarke.Event._executeBulkComplete(['{E1}'], { refresh() {} });
  await flush();

  assert.equal(xrm.updates.length, 1);
  assert.deepEqual(Object.keys(xrm.updates[0].data).sort(), ['sprk_completeddate', 'statecode', 'statuscode']);
  assert.equal(xrm.updates[0].data.statuscode, 659490002);
  assert.equal(xrm.updates[0].data.statecode, 0);
});

for (const [label, key] of [['Close', 'CLOSED'], ['Cancel', 'CANCELLED'], ['On Hold', 'ON_HOLD']]) {
  test(`${label} (homepage grid) PATCHes statuscode + statecode only`, async () => {
    const xrm = xrmDouble();
    const { Spaarke } = loadRibbon(xrm);

    Spaarke.Event.Homepage._executeBulkStatusUpdate(['{E1}', '{E2}'], Spaarke.Event.EventStatus[key], label);
    await flush();

    const [status, state] = LIVE[key];
    assert.equal(xrm.updates.length, 2);
    for (const u of xrm.updates) assert.deepEqual(u.data, { statuscode: status, statecode: state });
  });
}

test('Archive (homepage grid) is ONE update per record: No Further Action + Inactive', async () => {
  const xrm = xrmDouble();
  const { Spaarke } = loadRibbon(xrm);

  Spaarke.Event.Homepage._executeBulkArchive(['{E1}']);
  await flush();

  assert.deepEqual(xrm.updates, [{ entity: 'sprk_event', id: 'E1', data: { statuscode: 2, statecode: 1 } }]);
});

test('the enable rule reads statuscode (open work = Draft, Open, On Hold, Reassigned); Completed / Cancelled are not active', () => {
  const { Spaarke } = loadRibbon(xrmDouble());
  const withStatus = status => ({
    data: { entity: {} },
    getAttribute: name => (name === 'statuscode' ? { getValue: () => status } : null),
  });

  for (const key of ['DRAFT', 'OPEN', 'ON_HOLD', 'REASSIGNED']) {
    assert.equal(Spaarke.Event.IsEventActive(withStatus(LIVE[key][0])), true, key);
  }
  for (const key of ['COMPLETED', 'CLOSED', 'CANCELLED', 'TRANSFERRED', 'ARCHIVED']) {
    assert.equal(Spaarke.Event.IsEventActive(withStatus(LIVE[key][0])), false, key);
  }
});
