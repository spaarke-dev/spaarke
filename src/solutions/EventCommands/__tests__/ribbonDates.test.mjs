// sprk_event ribbon commands — Date Only writes and reads (spaarke-ontology-platform-r1 task 098, 2026-10-05).
//
// sprk_event's six date columns are Dataverse Date Only: the Web API accepts ONLY "YYYY-MM-DD" (a timestamp is HTTP 400)
// and the day written must be the user's LOCAL calendar day. The former code sent new Date().toISOString() — refused,
// and the UTC date anyway (23:30 on Oct 5 in New York is "2026-10-06T03:30:00.000Z").
//
// Run:  TZ=America/New_York node --test src/solutions/EventCommands/__tests__/
// (TZ is also forced below; a UTC process would pass trivially because local == UTC there.)
process.env.TZ = 'America/New_York';

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadRibbon } from './ribbonHarness.mjs';

const LATE_EVENING = new Date(2026, 9, 5, 23, 30); // 23:30 EDT on Oct 5 = 03:30Z on Oct 6

function xrmDouble() {
  const updates = [];
  return {
    updates,
    WebApi: {
      updateRecord: (entity, id, data) => {
        updates.push({ entity, id, data });
        return Promise.resolve({ id });
      },
    },
    Navigation: {
      openConfirmDialog: () => Promise.resolve({ confirmed: true }),
      openAlertDialog: () => Promise.resolve(),
      openWebResource: (url) => updates.push({ dialogUrl: url }),
    },
    App: { addGlobalNotification: () => Promise.resolve() },
    Utility: { getGlobalContext: () => ({ getQueryStringParameters: () => ({}) }) },
  };
}

const flush = () => new Promise(r => setTimeout(r, 0));

test('the harness is behind UTC (the guard is meaningful)', () => {
  assert.equal(LATE_EVENING.toISOString().slice(0, 10), '2026-10-06');
});

test('bulk complete (form subgrid) writes the LOCAL day as YYYY-MM-DD', async () => {
  const xrm = xrmDouble();
  const { Spaarke } = loadRibbon(xrm, LATE_EVENING);
  Spaarke.Event._executeBulkComplete(['e1'], { refresh() {} });
  await flush();
  assert.equal(xrm.updates[0].data.sprk_completeddate, '2026-10-05');
});

test('homepage Complete Selected writes the LOCAL day as YYYY-MM-DD', async () => {
  const xrm = xrmDouble();
  const { Spaarke } = loadRibbon(xrm, LATE_EVENING);
  Spaarke.Event.Homepage.CompleteSelected(['e1', 'e2'], 'sprk_event');
  await flush();
  await flush();
  const writes = xrm.updates.filter(u => u.data);
  assert.equal(writes.length, 2);
  for (const w of writes) assert.equal(w.data.sprk_completeddate, '2026-10-05');
});

function formDouble(values) {
  const set = {};
  const attr = name => ({
    getValue: () => (name in values ? values[name] : null),
    setValue: v => {
      set[name] = v;
    },
  });
  return {
    set,
    getAttribute: name => (name in values || name === 'sprk_completeddate' || name === 'sprk_duedate' ? attr(name) : null),
    data: { entity: { getId: () => '{E1}' }, save: () => Promise.resolve() },
  };
}

test('complete dialog result "YYYY-MM-DD" is set as that LOCAL day', async () => {
  const xrm = xrmDouble();
  const { Spaarke } = loadRibbon(xrm, LATE_EVENING);
  const form = formDouble({});
  Spaarke.Event._executeComplete(form, '2026-10-02', null);
  const d = form.set.sprk_completeddate;
  assert.deepEqual([d.getFullYear(), d.getMonth(), d.getDate()], [2026, 9, 2]);
});

test('complete without a dialog date sets TODAY in local time', () => {
  const xrm = xrmDouble();
  const { Spaarke } = loadRibbon(xrm, LATE_EVENING);
  const form = formDouble({});
  Spaarke.Event._executeComplete(form, '', null);
  const d = form.set.sprk_completeddate;
  assert.deepEqual([d.getFullYear(), d.getMonth(), d.getDate()], [2026, 9, 5]);
});

test('the complete dialog receives the due date as the form shows it (local day)', () => {
  const xrm = xrmDouble();
  const { Spaarke } = loadRibbon(xrm, LATE_EVENING);
  // A Date Only attribute's getValue() is that day at LOCAL midnight.
  Spaarke.Event.CompleteEvent(formDouble({ sprk_duedate: new Date(2026, 9, 2) }));
  const url = xrm.updates.find(u => u.dialogUrl).dialogUrl;
  assert.match(url, /dueDate=2026-10-02(&|$)/);
});

test('reschedule: the typed YYYY-MM-DD is set as that LOCAL day; the prompt is prefilled with the local day', () => {
  const xrm = xrmDouble();
  let prefill;
  xrm.__prompt = (_msg, def) => {
    prefill = def;
    return '2026-10-20';
  };
  const { Spaarke } = loadRibbon(xrm, LATE_EVENING);
  const form = formDouble({ sprk_duedate: new Date(2026, 9, 2) });
  Spaarke.Event.RescheduleEvent(form);
  assert.equal(prefill, '2026-10-02');
  const d = form.set.sprk_duedate;
  assert.deepEqual([d.getFullYear(), d.getMonth(), d.getDate()], [2026, 9, 20]);
});
