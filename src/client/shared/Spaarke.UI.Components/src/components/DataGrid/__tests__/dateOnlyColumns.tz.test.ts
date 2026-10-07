/** @jest-environment ./jest.newYorkEnvironment.js */
/**
 * Date Only columns in the shared grid — timezone regression guard
 * (spaarke-ontology-platform-r1 task 098, 2026-10-05).
 *
 * sprk_event's six date columns became Dataverse Behavior **DateOnly**: the Web
 * API now returns `"2026-10-02"`, which `new Date("2026-10-02")` reads as UTC
 * midnight — **Oct 1** in every US zone. The grid must show the calendar day
 * for a Date Only BEHAVIOUR column and leave genuine instants (UserLocal /
 * TimeZoneIndependent, always `…Z`) on their local-day rendering.
 *
 * Pins the process timezone to America/New_York BEFORE any `Date` use, so a UTC
 * CI runner cannot pass this trivially (same pattern as dateLocal.test.ts).
 */

// America/New_York is set by the @jest-environment above. Assigning process.env.TZ in a jest test file only
// changes Jest's per-file copy of process.env, so it never reached Node (task 098).
import { renderCellValue } from '../DataGrid';
import { resolveConfig } from '../configResolution';
import type { DataGridConfiguration } from '../../../types/DataGridConfiguration';
import type { EntityMetadata } from '../../../services/IDataverseClient';
import { XrmDataverseClient, _resetEntityMetadataCacheForTests } from '../../../services/XrmDataverseClient';
import { ColumnRendererService } from '../../../services/ColumnRendererService';
import { DataverseAttributeType } from '../../../types/ColumnRendererTypes';

const OCT_2 = new Date(2026, 9, 2).toLocaleDateString();
const OCT_1 = new Date(2026, 9, 1).toLocaleDateString();

describe('renderCellValue — Date Only cells show the calendar day (task 098)', () => {
  it('the harness is genuinely behind UTC (the guard is meaningful)', () => {
    expect(new Date('2026-10-02').toLocaleDateString()).toBe(OCT_1);
  });

  it("'dateonly' (metadata: Date Only behaviour) renders the stored day", () => {
    expect(renderCellValue('2026-10-02', 'dateonly')).toBe(OCT_2);
  });

  it("'date' renders a bare YYYY-MM-DD as that day (only a Date Only behaviour column returns one)", () => {
    expect(renderCellValue('2026-10-02', 'date')).toBe(OCT_2);
  });

  it("'date' leaves a UserLocal instant on its LOCAL day — genuine instants are not reinterpreted", () => {
    // 2026-10-02T03:00Z is 23:00 on Oct 1 in New York: a UserLocal Date-Only-format value entered then IS Oct 1.
    expect(renderCellValue('2026-10-02T03:00:00Z', 'date')).toBe(OCT_1);
    expect(renderCellValue('2026-10-02T04:00:00Z', 'date')).toBe(OCT_2);
  });

  it("'datetime' is unchanged", () => {
    expect(renderCellValue('2026-10-02T03:00:00Z', 'datetime')).toBe(new Date('2026-10-02T03:00:00Z').toLocaleString());
  });
});

describe('resolveConfig — the renderer follows the column BEHAVIOUR, not just its format', () => {
  const config: DataGridConfiguration = {
    _version: '1.0',
    source: { type: 'savedquery-set', entityLogicalName: 'sprk_event' },
  };
  const layoutXml =
    '<grid name="resultset" object="1" jump="sprk_eventname" select="1" icon="1" preview="1">' +
    '<row name="result" id="sprk_eventid">' +
    '<cell name="sprk_eventname" width="200"/><cell name="sprk_duedate" width="100"/>' +
    '<cell name="sprk_legacydate" width="100"/><cell name="createdon" width="100"/>' +
    '</row></grid>';

  function rendererOf(name: string, meta: EntityMetadata): string | undefined {
    return resolveConfig(undefined, config, meta, layoutXml).columns.find(c => c.name === name)?.renderer;
  }

  const meta: EntityMetadata = {
    primaryIdAttribute: 'sprk_eventid',
    primaryNameAttribute: 'sprk_eventname',
    attributes: {
      sprk_eventname: { attributeType: 'String', isPrimaryName: true },
      sprk_duedate: { attributeType: 'DateTime', format: 'DateOnly', dateTimeBehavior: 'DateOnly' },
      sprk_legacydate: { attributeType: 'DateTime', format: 'DateOnly', dateTimeBehavior: 'UserLocal' },
      createdon: { attributeType: 'DateTime', format: 'DateAndTime', dateTimeBehavior: 'UserLocal' },
    },
  };

  it('Date Only behaviour → dateonly; UserLocal with Date Only format → date; date-and-time → datetime', () => {
    expect(rendererOf('sprk_duedate', meta)).toBe('dateonly');
    expect(rendererOf('sprk_legacydate', meta)).toBe('date');
    expect(rendererOf('createdon', meta)).toBe('datetime');
  });
});

describe('XrmDataverseClient — projects a DateTime column behaviour from every metadata shape', () => {
  let originalXrm: unknown;
  beforeEach(() => {
    originalXrm = (window as any).Xrm;
    _resetEntityMetadataCacheForTests();
  });
  afterEach(() => {
    (window as any).Xrm = originalXrm;
    _resetEntityMetadataCacheForTests();
  });

  it('reads Web API DateTimeBehavior.Value, a plain string, the numeric client-API Behavior, or nothing', async () => {
    (window as any).Xrm = {
      WebApi: { retrieveRecord: jest.fn(), retrieveMultipleRecords: jest.fn() },
      Utility: {
        getEntityMetadata: jest.fn().mockResolvedValue({
          PrimaryIdAttribute: 'sprk_eventid',
          PrimaryNameAttribute: 'sprk_eventname',
          Attributes: [
            { LogicalName: 'sprk_duedate', AttributeType: 2, Format: 'DateOnly', DateTimeBehavior: { Value: 'DateOnly' } },
            { LogicalName: 'sprk_basedate', AttributeType: 2, DateTimeBehavior: 'DateOnly' },
            { LogicalName: 'sprk_meetingdate', AttributeType: 2, Behavior: 2 },
            { LogicalName: 'createdon', AttributeType: 2, Behavior: 1 },
            { LogicalName: 'sprk_other', AttributeType: 2 },
          ],
        }),
      },
    };

    const meta = await new XrmDataverseClient().retrieveEntityMetadata('sprk_event');

    expect(meta.attributes.sprk_duedate.dateTimeBehavior).toBe('DateOnly');
    expect(meta.attributes.sprk_basedate.dateTimeBehavior).toBe('DateOnly');
    expect(meta.attributes.sprk_meetingdate.dateTimeBehavior).toBe('DateOnly');
    expect(meta.attributes.createdon.dateTimeBehavior).toBe('UserLocal');
    expect(meta.attributes.sprk_other.dateTimeBehavior).toBeUndefined();
  });
});

describe('ColumnRendererService — Date Only column renders the stored day', () => {
  it('a bare YYYY-MM-DD is that calendar day; an instant keeps its local day', () => {
    const render = ColumnRendererService.getRenderer({
      name: 'sprk_duedate',
      displayName: 'Due Date',
      dataType: DataverseAttributeType.DateOnly,
    });
    const text = (value: string): string => {
      const out = render(value, {} as never, {} as never) as { props: { children: string } };
      return out.props.children;
    };

    const oct2 = new Date(2026, 9, 2).toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
    const oct1 = new Date(2026, 9, 1).toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
    expect(text('2026-10-02')).toBe(oct2);
    expect(text('2026-10-02T03:00:00Z')).toBe(oct1);
  });
});
