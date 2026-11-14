/**
 * R4 task 011 (owner 2026-11-13): "Your access" and every read-only line use the Manage Access labels ("View Only" /
 * "Collaborate" / "Full Access") from `/me` (`projects` for a project, `records` for a matter or work assignment, task 008).
 * Nothing is inferred: no "Can contribute", no "Read only", and a record `/me` has no level for shows no value.
 *
 * Pure checks of `recordAccess.ts`, plus a source scan that keeps the wording and the label map in one place (D-11 #2718):
 * the screens that render the record are covered in `RecordTemplateAccess.test.tsx`.
 */
import fs from 'fs';
import path from 'path';
import { describe, expect, it } from 'vitest';
import {
  ACCESS_LEVEL_LABELS,
  createTaskRightOf,
  decidingLevelOf,
  readOnlyNoteOf,
  recordAccessFactsOf,
  recordLevelOf,
  yourAccessOf,
} from '../src/components/record/recordAccess';
import type { ExternalUserContextResponse } from '../src/auth/bff-client';
import { AccessLevel } from '../src/types';

const MATTER = '00000000-0000-4000-8000-000000000201';
const WA = '00000000-0000-4000-8000-000000000301';
const PROJECT = '00000000-0000-4000-8000-000000000101';

const meOf = (extra: Partial<ExternalUserContextResponse> = {}): ExternalUserContextResponse => ({
  contactId: 'c-1',
  email: 'me@example.test',
  projects: [],
  ...extra,
});

describe('ACCESS_LEVEL_LABELS (the one label map)', () => {
  it('is the Manage Access wording', () => {
    expect(ACCESS_LEVEL_LABELS).toEqual({
      [AccessLevel.ViewOnly]: 'View Only',
      [AccessLevel.Collaborate]: 'Collaborate',
      [AccessLevel.FullAccess]: 'Full Access',
    });
  });
});

describe('recordLevelOf: the level /me gives for this record', () => {
  it.each([
    ['ViewOnly', AccessLevel.ViewOnly],
    ['Collaborate', AccessLevel.Collaborate],
    ['FullAccess', AccessLevel.FullAccess],
  ])('a matter listed in /me records at %s (must fire)', (label, level) => {
    expect(
      recordLevelOf(
        'matters',
        MATTER,
        meOf({ records: [{ recordType: 'matter', recordId: MATTER, accessLevel: label }] })
      )
    ).toBe(level);
  });

  it('a work assignment is read from its own record type, and the id compares without case', () => {
    const me = meOf({
      records: [{ recordType: 'workassignment', recordId: WA.toUpperCase(), accessLevel: 'Collaborate' }],
    });
    expect(recordLevelOf('workassignments', WA, me)).toBe(AccessLevel.Collaborate);
  });

  it('a project still reads /me projects', () => {
    expect(
      recordLevelOf('projects', PROJECT, meOf({ projects: [{ projectId: PROJECT, accessLevel: 'FullAccess' }] }))
    ).toBe(AccessLevel.FullAccess);
  });

  it('a project is not looked up in records, and a matter is not looked up in projects (must-not-fire)', () => {
    const asRecord = meOf({ records: [{ recordType: 'matter', recordId: PROJECT, accessLevel: 'Collaborate' }] });
    expect(recordLevelOf('projects', PROJECT, asRecord)).toBeUndefined();
    const asProject = meOf({ projects: [{ projectId: MATTER, accessLevel: 'Collaborate' }] });
    expect(recordLevelOf('matters', MATTER, asProject)).toBeUndefined();
  });

  it.each([
    ['/me not read', null],
    ['an older BFF (no records member)', meOf()],
    ['records empty', meOf({ records: [] })],
    ['another matter only', meOf({ records: [{ recordType: 'matter', recordId: WA, accessLevel: 'Collaborate' }] })],
    [
      'the same id under the other record type',
      meOf({ records: [{ recordType: 'workassignment', recordId: MATTER, accessLevel: 'Collaborate' }] }),
    ],
    ['an unrecognised level', meOf({ records: [{ recordType: 'matter', recordId: MATTER, accessLevel: 'Owner' }] })],
  ])('a matter is absent from /me (%s): no level (must-not-fire)', (_name, me) => {
    expect(recordLevelOf('matters', MATTER, me)).toBeUndefined();
  });

  it.each(['invoices', 'servicerequests', 'documents', undefined])(
    'a record kind /me has no level for (%s) never has one, whatever /me lists',
    root => {
      const me = meOf({
        projects: [{ projectId: MATTER, accessLevel: 'Collaborate' }],
        records: [{ recordType: 'matter', recordId: MATTER, accessLevel: 'Collaborate' }],
      });
      expect(recordLevelOf(root, MATTER, me)).toBeUndefined();
    }
  );

  it('a prototype-named root is not a record kind', () => {
    expect(recordLevelOf('constructor', MATTER, meOf({ records: [] }))).toBeUndefined();
    expect(recordLevelOf('__proto__', MATTER, meOf({ records: [] }))).toBeUndefined();
  });
});

describe('uac-r2 A2: a writeAccessPerRecord caller sees no level line, and the level never decides a write', () => {
  const perRecord = meOf({
    writeAccessPerRecord: true,
    projects: [{ projectId: PROJECT, accessLevel: 'ViewOnly' }],
    records: [{ recordType: 'matter', recordId: MATTER, accessLevel: 'ViewOnly' }],
  });

  it('the level is known (recordLevelOf) but the line is not shown: the composed read view would contradict working creates', () => {
    expect(recordLevelOf('matters', MATTER, perRecord)).toBe(AccessLevel.ViewOnly);
    expect(recordLevelOf('projects', PROJECT, perRecord)).toBe(AccessLevel.ViewOnly);
    expect(yourAccessOf(recordAccessFactsOf('matters', MATTER, perRecord))).toBeNull();
    expect(yourAccessOf(recordAccessFactsOf('projects', PROJECT, perRecord))).toBeNull();
  });

  it('a contact caller (no per-record flag) with the same entries keeps the label (must-not-fire of the rule above)', () => {
    const contact = meOf({
      projects: perRecord.projects,
      records: perRecord.records,
    });
    expect(yourAccessOf(recordAccessFactsOf('matters', MATTER, contact))).toBe('View Only');
    expect(yourAccessOf(recordAccessFactsOf('projects', PROJECT, contact))).toBe('View Only');
  });

  it('the level never decides a write action: no deciding level exists for them on any root', () => {
    for (const [root, id] of [
      ['projects', PROJECT],
      ['matters', MATTER],
      ['workassignments', WA],
    ] as const) {
      expect(decidingLevelOf(root, id, perRecord)).toBeUndefined();
    }
  });

  it('a per-record View Only matter gets the neutral line, never "needs Collaborate access"', () => {
    const facts = recordAccessFactsOf('matters', MATTER, perRecord);
    expect(readOnlyNoteOf('documents', { upload: false }, facts)).toBe(
      'You can view these documents, but not upload to this record.'
    );
  });

  it('the same caller WITHOUT the per-record flag and View Only is told which level is needed (the label decides there)', () => {
    const partner = meOf({ records: [{ recordType: 'matter', recordId: MATTER, accessLevel: 'ViewOnly' }] });
    const facts = recordAccessFactsOf('matters', MATTER, partner);
    expect(readOnlyNoteOf('documents', { upload: false }, facts)).toBe(
      'You can view these documents. Uploading needs Collaborate access.'
    );
  });
});

describe('yourAccessOf: nothing is inferred', () => {
  it('shows nothing without /me, without a level, and for a guessed project level', () => {
    expect(yourAccessOf(recordAccessFactsOf('matters', MATTER, null))).toBeNull();
    expect(yourAccessOf(recordAccessFactsOf('matters', MATTER, meOf()))).toBeNull();
    // A level-decided project caller with no entry: the actions fall back to View Only, but that is a guess, not an answer.
    expect(yourAccessOf(recordAccessFactsOf('projects', PROJECT, meOf()))).toBeNull();
  });
});

describe('createTaskRightOf: Add task needs the event right AND an explicit canCreateTask', () => {
  it.each([
    // [event right, list canCreateTask, expected]
    [true, true, true],
    [true, false, false],
    [true, null, null],
    [true, undefined, null],
    [false, true, false],
    [false, false, false],
    [false, undefined, false],
    [null, true, null],
    [null, false, null],
  ] as const)('event right %s and canCreateTask %s -> %s', (eventRight, canCreateTask, expected) => {
    expect(createTaskRightOf(eventRight, canCreateTask)).toBe(expected);
  });
});

// ---------------------------------------------------------------------------
// Source scan: one wording, one label map
// ---------------------------------------------------------------------------

const SRC = path.resolve(__dirname, '../src');

function sourceFiles(dir: string): string[] {
  return fs.readdirSync(dir, { withFileTypes: true }).flatMap(entry => {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) return sourceFiles(full);
    return /\.(ts|tsx|css|json|html)$/.test(entry.name) ? [full] : [];
  });
}

const rel = (file: string): string => path.relative(SRC, file).replace(/\\/g, '/');

/** The matcher's own controls: it must fire on the old wording and stay quiet on the new. */
const CAN_CONTRIBUTE = /can contribute|canContribute/i;

describe('src/ says "Can contribute" nowhere (task 011)', () => {
  it.each([
    ['the old header label', "canContribute: 'Can contribute',"],
    ['the old key', 'RECORD_ACCESS_TEXT.canContribute'],
    ['upper-case wording in a comment', '// the header says "CAN CONTRIBUTE" once a list answered'],
  ])('the matcher fires on %s', (_name, line) => {
    expect(CAN_CONTRIBUTE.test(line)).toBe(true);
  });

  it.each([['Collaborate'], ['a Collaborate caller may contribute documents'], ['Full Access']])(
    'the matcher stays quiet on %s',
    line => {
      expect(CAN_CONTRIBUTE.test(line)).toBe(false);
    }
  );

  it('scans a real tree', () => {
    expect(sourceFiles(SRC).map(rel)).toContain('components/record/recordAccess.ts');
  });

  it('no file in src/ has the wording', () => {
    const offenders = sourceFiles(SRC)
      .filter(file => CAN_CONTRIBUTE.test(fs.readFileSync(file, 'utf8')))
      .map(rel);
    expect(offenders).toEqual([]);
  });

  it('no file in src/ has the "Read only" header inference either', () => {
    const offenders = sourceFiles(SRC)
      .filter(file => /readOnly: 'Read only'|RECORD_ACCESS_TEXT\.readOnly/.test(fs.readFileSync(file, 'utf8')))
      .map(rel);
    expect(offenders).toEqual([]);
  });
});

describe('the access level labels have one home (D-11 #2718)', () => {
  it('only recordAccess.ts spells "View Only" and "Full Access" as a label in code (Collaborate is also the wire value, so it is not scanned)', () => {
    const offenders = sourceFiles(SRC)
      .filter(file => rel(file) !== 'components/record/recordAccess.ts')
      .filter(file => {
        // A string literal that IS a label (not a sentence or a comment that mentions the level).
        const code = fs
          .readFileSync(file, 'utf8')
          .split(/\r?\n/)
          .filter(line => !/^\s*(\/\/|\*|\/\*)/.test(line));
        return code.some(line => /(['"`])(View Only|Full Access)\1/.test(line));
      })
      .map(rel);
    expect(offenders).toEqual([]);
  });
});
