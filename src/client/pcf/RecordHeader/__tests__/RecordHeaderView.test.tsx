/**
 * RecordHeaderView — configuration-driven header suite (r2 task 033).
 *
 * Adapted from `MatterHeader/__tests__/MatterHeaderView.test.tsx`, but where
 * that suite asserted a FIXED five-field Matter layout, this one asserts the
 * three CONFIG paths the control must survive (POML step 9):
 *
 *   1. a valid `layoutJson`      → renders exactly the configured layout
 *   2. an absent `layoutJson`    → renders tier-2 metadata-derived defaults
 *   3. a malformed `layoutJson`  → console.warn + defaults, NEVER blank (FR-03/NFR-10)
 *
 * plus the pure presentation helpers and the FR-12 self-detection idiom.
 *
 * `XrmDataverseClient` is mocked at the module boundary so the suite does not
 * depend on `Xrm.Utility.getEntityMetadata`s payload shape (that projection is
 * task 020s contract and is tested in the shared library). `Xrm.WebApi` +
 * `Xrm.Page` are shimmed on `window` for the record read + form-buffer staging.
 */

import * as React from 'react';
import * as fs from 'fs';
import * as path from 'path';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

// ─────────────────────────────────────────────────────────────────────────────
// Mock the metadata client BEFORE importing the view.
// ─────────────────────────────────────────────────────────────────────────────

const mockRetrieveEntityMetadata = jest.fn();

jest.mock('@spaarke/ui-components/dist/services/XrmDataverseClient', () => ({
  XrmDataverseClient: class {
    retrieveEntityMetadata(entityName: string, attributes?: string[]): Promise<unknown> {
      // BOTH arguments are forwarded: the requested-attribute list is part of
      // the contract the v1.1.1 defect fix depends on (see the DEF-1 suite).
      return mockRetrieveEntityMetadata(entityName, attributes);
    }
  },
}));

import {
  RecordHeaderView,
  buildMetadataAttributeNames,
  buildSelectFields,
  summaryFieldExists,
  toCellSpan,
  toNumberKind,
  extractCurrencySymbol,
} from '../control/RecordHeaderView';
import { resolveEntityContext } from '../control/entityContext';
import {
  applyFormControlHints,
  buildHeaderFormMetadata,
  buildRequestedAttributeNames,
  normalizeFormFormat,
  readFormControlOrder,
} from '../control/useHeaderFormMetadata';
import { CONTROL_VERSION } from '../control/version';

// ─────────────────────────────────────────────────────────────────────────────
// Fixtures — a deliberately NON-Matter entity, so a compiled-in `sprk_matter`
// anywhere in the control would fail this suite loudly.
// ─────────────────────────────────────────────────────────────────────────────

const ENTITY = 'sprk_project';
const RECORD_ID = '11111111-2222-3333-4444-555555555555';

const ENTITY_METADATA = {
  primaryIdAttribute: 'sprk_projectid',
  // Live-verified: Project's primary NAME is sprk_projectnumber, not sprk_projectname.
  primaryNameAttribute: 'sprk_projectnumber',
  attributes: {
    sprk_projectid: { attributeType: 'Uniqueidentifier' },
    sprk_projectnumber: { attributeType: 'String', displayName: 'Project Number' },
    sprk_projectname: { attributeType: 'String', displayName: 'Project Name' },
    sprk_description: { attributeType: 'Memo', displayName: 'Description' },
    sprk_startdate: { attributeType: 'DateTime', format: 'DateOnly', displayName: 'Start Date' },
    sprk_budget: { attributeType: 'Money', displayName: 'Budget' },
    sprk_active: { attributeType: 'Boolean', displayName: 'Active' },
    sprk_projecttype: { attributeType: 'Lookup', displayName: 'Project Type', targets: ['sprk_projecttype_ref'] },
    sprk_status: {
      attributeType: 'Picklist',
      displayName: 'Status',
      optionSet: [
        { value: 1, label: 'Open' },
        { value: 2, label: 'Closed' },
      ],
    },
    createdon: { attributeType: 'DateTime', displayName: 'Created On' },
  },
};

/** The record payload Dataverse would return for the fields under test. */
const RECORD = {
  sprk_projectnumber: 'PRJ-0001',
  sprk_projectname: 'Apollo',
  sprk_description: 'A multiline description.',
  sprk_startdate: '2026-03-01',
  sprk_budget: 12500,
  'sprk_budget@OData.Community.Display.V1.FormattedValue': '$12,500.00',
  sprk_active: true,
  sprk_status: 1,
  'sprk_status@OData.Community.Display.V1.FormattedValue': 'Open',
  _sprk_projecttype_value: '99999999-8888-7777-6666-555555555555',
  '_sprk_projecttype_value@OData.Community.Display.V1.FormattedValue': 'Internal',
};

let retrieveRecord: jest.Mock;
let lastSelect: string | undefined;

/**
 * Per-control hints the LIVE FORM supplies but the Client-API metadata payload
 * does not — `attribute.getFormat()` and, for lookups, `control.getEntityTypes()`.
 * Both default to absent so every pre-existing test is unaffected.
 */
interface IFormHints {
  formats?: Record<string, string>;
  entityTypes?: Record<string, string[]>;
}

function installXrm(
  formControlNames: string[] = Object.keys(ENTITY_METADATA.attributes),
  hints: IFormHints = {}
): void {
  retrieveRecord = jest.fn((_entity: string, _id: string, options?: string) => {
    lastSelect = options;
    return Promise.resolve({ ...RECORD });
  });

  const attributes: Record<string, { setValue: jest.Mock; getIsDirty: () => boolean }> = {};
  for (const name of formControlNames) {
    attributes[name] = { setValue: jest.fn(), getIsDirty: () => true };
  }

  const controls = formControlNames.map(name => ({
    getName: () => name,
    getLabel: () => `Form ${name}`,
    getAttribute: () => ({
      getRequiredLevel: () => (name === 'sprk_projectnumber' ? 'required' : 'none'),
      getFormat: () => hints.formats?.[name],
    }),
    // Only lookup controls expose this; everything else omits the method
    // entirely, which is what the production guard probes for.
    getEntityTypes: hints.entityTypes?.[name] ? () => hints.entityTypes![name] : undefined,
  }));

  (window as unknown as { Xrm: unknown }).Xrm = {
    WebApi: { retrieveRecord, retrieveMultipleRecords: jest.fn(() => Promise.resolve({ entities: [] })) },
    Navigation: { navigateTo: jest.fn(() => Promise.resolve()) },
    Page: {
      getAttribute: (n: string) => attributes[n] ?? null,
      ui: { controls: { forEach: (cb: (c: unknown, i: number) => void) => controls.forEach(cb) } },
    },
  };
}

const renderView = (layoutJson: string | null) =>
  render(
    <FluentProvider theme={webLightTheme}>
      <RecordHeaderView entityName={ENTITY} recordId={RECORD_ID} layoutJson={layoutJson} />
    </FluentProvider>
  );

beforeEach(() => {
  lastSelect = undefined;
  mockRetrieveEntityMetadata.mockReset();
  mockRetrieveEntityMetadata.mockResolvedValue(ENTITY_METADATA);
  installXrm();
});

afterEach(() => {
  delete (window as unknown as { Xrm?: unknown }).Xrm;
  jest.restoreAllMocks();
});

// ─────────────────────────────────────────────────────────────────────────────
// 1. Valid layoutJson
// ─────────────────────────────────────────────────────────────────────────────

describe('RecordHeaderView — valid layoutJson (tier 1)', () => {
  const VALID_LAYOUT = JSON.stringify({
    _version: '1.0',
    title: 'Project Overview',
    columns: 2,
    fields: [
      { name: 'sprk_projectnumber', span: 1 },
      { name: 'sprk_projectname', span: 1 },
      { name: 'sprk_status' },
      { name: 'sprk_budget' },
      { name: 'sprk_startdate' },
      { name: 'sprk_active' },
      { name: 'sprk_projecttype' },
      { name: 'sprk_description', renderer: 'textarea' },
    ],
  });

  it('renders the configured title and every configured field label', async () => {
    renderView(VALID_LAYOUT);

    expect(await screen.findByText('Project Overview')).toBeInTheDocument();
    // Labels resolve from the FORM control label (design.md 5.4 precedence).
    for (const name of [
      'sprk_projectnumber',
      'sprk_projectname',
      'sprk_status',
      'sprk_budget',
      'sprk_startdate',
      'sprk_active',
      'sprk_projecttype',
      'sprk_description',
    ]) {
      expect(screen.getByText(`Form ${name}`)).toBeInTheDocument();
    }
  });

  it('renders each renderer type with its formatted value, not its raw value', async () => {
    renderView(VALID_LAYOUT);

    expect(await screen.findByText('PRJ-0001')).toBeInTheDocument();
    // Money → currency-formatted with the symbol from the OData annotation (FR-07).
    expect(screen.getByText('$12,500.00')).toBeInTheDocument();
    // Picklist → resolved option label, never the integer (FR-09).
    expect(screen.getByText('Open')).toBeInTheDocument();
    expect(screen.queryByText('1')).not.toBeInTheDocument();
    // Boolean → Yes/No, never true/false (FR-08).
    expect(screen.getByText('Yes')).toBeInTheDocument();
    expect(screen.queryByText('true')).not.toBeInTheDocument();
    // Lookup → the formatted display name (FR-15).
    expect(screen.getByText('Internal')).toBeInTheDocument();
  });

  it('reads lookups through the decorated _value key and everything else plainly', async () => {
    renderView(VALID_LAYOUT);
    await waitFor(() => expect(retrieveRecord).toHaveBeenCalled());

    expect(lastSelect).toContain('_sprk_projecttype_value');
    expect(lastSelect).toContain('sprk_projectnumber');
    // The UNdecorated lookup name must NOT be selected — Dataverse 400s on it.
    expect(lastSelect).not.toMatch(/(^|,)sprk_projecttype(,|$)/);
  });

  it('does not warn when the configuration is valid', async () => {
    const warn = jest.spyOn(console, 'warn').mockImplementation(() => undefined);
    renderView(VALID_LAYOUT);
    await screen.findByText('Project Overview');
    expect(warn).not.toHaveBeenCalledWith(expect.stringContaining('resolveHeaderConfig'));
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// 2. Absent layoutJson → tier-2 derived defaults (FR-04)
// ─────────────────────────────────────────────────────────────────────────────

describe('RecordHeaderView — absent layoutJson (tier 2)', () => {
  it('renders metadata-derived defaults led by the primary NAME attribute', async () => {
    renderView(null);

    // sprk_projectnumber is Project's primary name — it must lead, and the
    // GUID primary id must never appear.
    expect(await screen.findByText('Form sprk_projectnumber')).toBeInTheDocument();
    expect(screen.queryByText('Form sprk_projectid')).not.toBeInTheDocument();
  });

  it('never renders blank — at least one field cell is present', async () => {
    const { container } = renderView(null);
    await screen.findByText('Form sprk_projectnumber');
    expect(container.querySelectorAll('label').length).toBeGreaterThan(0);
  });

  it('excludes the audit columns from derived defaults', async () => {
    renderView(null);
    await screen.findByText('Form sprk_projectnumber');
    expect(screen.queryByText('Form createdon')).not.toBeInTheDocument();
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// 3. Malformed layoutJson → warn + defaults, never blank (FR-03 / NFR-10)
// ─────────────────────────────────────────────────────────────────────────────

describe('RecordHeaderView — malformed layoutJson', () => {
  it.each([
    ['unparseable', '{{{'],
    ['wrong version', JSON.stringify({ _version: '2.0', fields: [] })],
    ['fields not an array', JSON.stringify({ _version: '1.0', fields: 'nope' })],
    ['no usable name', JSON.stringify({ _version: '1.0', fields: [{}] })],
  ])('degrades to derived defaults with a warning: %s', async (_label, layoutJson) => {
    const warn = jest.spyOn(console, 'warn').mockImplementation(() => undefined);

    renderView(layoutJson);

    // Never blank: the derived default layout still renders.
    expect(await screen.findByText('Form sprk_projectnumber')).toBeInTheDocument();
    expect(warn).toHaveBeenCalledWith(expect.stringContaining('resolveHeaderConfig'));
  });

  it('does not throw for any of the malformed inputs', () => {
    for (const bad of ['{{{', '[]', 'null', '"a string"', '']) {
      expect(() => renderView(bad)).not.toThrow();
    }
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// 4. Version footer (ADR-020)
// ─────────────────────────────────────────────────────────────────────────────

describe('RecordHeaderView — version footer', () => {
  it('renders the shared CONTROL_VERSION by default', async () => {
    renderView(null);
    expect(await screen.findByTestId('record-header-version')).toHaveTextContent(`v${CONTROL_VERSION}`);
  });

  it('stays on the 1.1.x line (the new control identity baseline)', () => {
    // 1.1.0 was the initial identity; 1.1.1 carries the first-UAT defect fixes.
    // Pinning the MINOR line still catches an accidental reset to 1.0.x, which
    // is what this assertion was actually guarding.
    expect(CONTROL_VERSION).toMatch(/^1\.1\.\d+$/);
  });

  it('is suppressed when showVersion is false', async () => {
    render(
      <FluentProvider theme={webLightTheme}>
        <RecordHeaderView entityName={ENTITY} recordId={RECORD_ID} layoutJson={null} showVersion={false} />
      </FluentProvider>
    );
    await screen.findByText('Form sprk_projectnumber');
    expect(screen.queryByTestId('record-header-version')).not.toBeInTheDocument();
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// 5. FR-12 self-detection (the type-cast idiom)
// ─────────────────────────────────────────────────────────────────────────────

describe('resolveEntityContext (FR-12)', () => {
  it('prefers context.mode.contextInfo', () => {
    expect(
      resolveEntityContext({
        mode: { contextInfo: { entityTypeName: 'sprk_invoice', entityId: 'id-1' } },
        page: { entityTypeName: 'sprk_wrong', entityId: 'id-wrong' },
      })
    ).toEqual({ entityName: 'sprk_invoice', recordId: 'id-1' });
  });

  it('falls back to context.page when contextInfo is absent', () => {
    expect(resolveEntityContext({ page: { entityTypeName: 'sprk_event', entityId: 'id-2' } })).toEqual({
      entityName: 'sprk_event',
      recordId: 'id-2',
    });
  });

  it('degrades to empty strings rather than throwing', () => {
    expect(resolveEntityContext(undefined)).toEqual({ entityName: '', recordId: '' });
    expect(resolveEntityContext({})).toEqual({ entityName: '', recordId: '' });
    expect(resolveEntityContext({ mode: {} })).toEqual({ entityName: '', recordId: '' });
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// 6. Metadata adaptation — the 031 insertion-order contract
// ─────────────────────────────────────────────────────────────────────────────

describe('buildHeaderFormMetadata', () => {
  it('puts FORM controls first, in form order (031 caller contract)', () => {
    const result = buildHeaderFormMetadata(ENTITY, ENTITY_METADATA, [
      { name: 'sprk_budget' },
      { name: 'sprk_projectnumber' },
    ]);
    const keys = Object.keys(result.attributes);
    expect(keys[0]).toBe('sprk_budget');
    expect(keys[1]).toBe('sprk_projectnumber');
  });

  it('still exposes attributes that are NOT on the form (so FR-14 can throw on edit)', () => {
    const result = buildHeaderFormMetadata(ENTITY, ENTITY_METADATA, [{ name: 'sprk_projectnumber' }]);
    expect(result.attributes.sprk_budget).toBeDefined();
  });

  it('prefers the form control label and carries the form requirement level', () => {
    const result = buildHeaderFormMetadata(ENTITY, ENTITY_METADATA, [
      { name: 'sprk_projectnumber', label: 'Number On This Form', requiredLevel: 'required' },
    ]);
    expect(result.attributes.sprk_projectnumber.label).toBe('Number On This Form');
    expect(result.attributes.sprk_projectnumber.displayName).toBe('Project Number');
    expect(result.attributes.sprk_projectnumber.requiredLevel).toBe('required');
  });

  it('carries the primary name/id attributes through unchanged', () => {
    const result = buildHeaderFormMetadata(ENTITY, ENTITY_METADATA, []);
    expect(result.primaryNameAttribute).toBe('sprk_projectnumber');
    expect(result.primaryIdAttribute).toBe('sprk_projectid');
    expect(result.entityLogicalName).toBe(ENTITY);
  });
});

describe('readFormControlOrder', () => {
  it('reads control names in form order', () => {
    installXrm(['sprk_projectname', 'sprk_budget']);
    expect(readFormControlOrder().map(c => c.name)).toEqual(['sprk_projectname', 'sprk_budget']);
  });

  it('returns an empty list rather than throwing when Xrm.Page is absent', () => {
    delete (window as unknown as { Xrm?: unknown }).Xrm;
    expect(() => readFormControlOrder()).not.toThrow();
    expect(readFormControlOrder()).toEqual([]);
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// 7. Pure presentation helpers
// ─────────────────────────────────────────────────────────────────────────────

describe('buildSelectFields', () => {
  const field = (name: string, renderer: string) =>
    ({ name, renderer, label: name, span: 1, readOnly: false, required: false }) as never;

  it('decorates lookups and leaves everything else plain', () => {
    expect(buildSelectFields([field('sprk_projecttype', 'lookup'), field('sprk_projectname', 'text')])).toEqual([
      '_sprk_projecttype_value',
      'sprk_projectname',
    ]);
  });

  it('collapses duplicates so $select stays valid', () => {
    expect(buildSelectFields([field('a', 'text'), field('a', 'text')])).toEqual(['a']);
  });

  it('returns an empty list for an empty layout', () => {
    expect(buildSelectFields([])).toEqual([]);
  });
});

describe('toCellSpan', () => {
  it('narrows to the 1 | 2 | 3 union the renderers accept', () => {
    expect(toCellSpan(1)).toBe(1);
    expect(toCellSpan(2)).toBe(2);
    expect(toCellSpan(3)).toBe(3);
  });

  it('never emits a value outside the union', () => {
    expect(toCellSpan(0)).toBe(1);
    expect(toCellSpan(99)).toBe(3);
  });
});

describe('toNumberKind', () => {
  it('maps the currency renderer and Money type to money', () => {
    expect(toNumberKind('currency', 'Decimal')).toBe('money');
    expect(toNumberKind('number', 'Money')).toBe('money');
  });

  it('maps integer types to integer so no decimals render', () => {
    expect(toNumberKind('number', 'Integer')).toBe('integer');
    expect(toNumberKind('number', 'BigInt')).toBe('integer');
  });

  it('maps Double and defaults everything else to decimal', () => {
    expect(toNumberKind('number', 'Double')).toBe('double');
    expect(toNumberKind('number', 'Decimal')).toBe('decimal');
    expect(toNumberKind('number', undefined)).toBe('decimal');
  });
});

describe('extractCurrencySymbol', () => {
  it('pulls the leading symbol out of a formatted money value', () => {
    expect(extractCurrencySymbol('$12,500.00')).toBe('$');
    expect(extractCurrencySymbol('€1.000,00')).toBe('€');
  });

  it('returns undefined when there is no symbol to extract', () => {
    expect(extractCurrencySymbol('12500')).toBeUndefined();
    expect(extractCurrencySymbol(undefined)).toBeUndefined();
    expect(extractCurrencySymbol(12500)).toBeUndefined();
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// 8. Negative — nothing entity-specific is compiled in (FR-12)
// ─────────────────────────────────────────────────────────────────────────────

describe('entity agnosticism (FR-12)', () => {
  it('renders an entity it has never heard of, using only what metadata supplies', async () => {
    mockRetrieveEntityMetadata.mockResolvedValue({
      primaryIdAttribute: 'sprk_widgetid',
      primaryNameAttribute: 'sprk_widgetname',
      attributes: { sprk_widgetname: { attributeType: 'String', displayName: 'Widget Name' } },
    });
    installXrm(['sprk_widgetname']);

    render(
      <FluentProvider theme={webLightTheme}>
        <RecordHeaderView entityName="sprk_widget" recordId={RECORD_ID} layoutJson={null} />
      </FluentProvider>
    );

    expect(await screen.findByText('Form sprk_widgetname')).toBeInTheDocument();
    // With no layoutJson the requested set is the form's own controls PLUS the
    // default summary candidate (task 034 / FR-22). Asserted as an exact array
    // rather than loosened to `toContain`: the whole point of this test is that
    // nothing ENTITY-SPECIFIC is compiled in, and an exact match is what proves
    // it. `sprk_recordsummary` is a cross-entity constant the owner created on
    // every rollout entity — it does not name `sprk_widget` or any other single
    // entity, so FR-12 still holds.
    expect(mockRetrieveEntityMetadata).toHaveBeenCalledWith('sprk_widget', ['sprk_widgetname', SUMMARY_FIELD]);
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// DEF-1 (v1.1.0 UAT) — metadata must reach the resolver, and a lookup must
// never be $select-ed by its bare name.
//
// The failure chain that shipped in v1.1.0:
//   empty entity metadata
//     -> resolveHeaderConfig derives renderer 'text' for EVERY field
//     -> buildSelectFields emits the lookup's BARE logical name
//     -> Dataverse 400s the whole $select ("Could not find a property named
//        'sprk_projecttype_ref'") — verified live against spaarkedev1
//     -> every field null -> every cell an em-dash
//
// These tests pin each link.
// ─────────────────────────────────────────────────────────────────────────────

describe('RecordHeaderView — DEF-1 regression: lookups and the $select', () => {
  it('emits a lookup as _<name>_value, never as the bare logical name', () => {
    const field = (name: string, renderer: string) =>
      ({ name, renderer, label: name, span: 1, readOnly: false, required: false }) as never;

    const select = buildSelectFields([
      field('sprk_projecttype_ref', 'lookup'),
      field('sprk_openeddate', 'date'),
      field('sprk_highpriority', 'boolean'),
    ]);

    expect(select).toContain('_sprk_projecttype_ref_value');
    // The exact string that returned HTTP 400 live. Its presence anywhere in
    // the $select is the defect.
    expect(select).not.toContain('sprk_projecttype_ref');
  });

  it('$selects the lookup correctly END-TO-END when metadata types the field', async () => {
    mockRetrieveEntityMetadata.mockResolvedValue({
      primaryIdAttribute: 'sprk_projectid',
      primaryNameAttribute: 'sprk_projectnumber',
      attributes: {
        // Live-verified sprk_project types.
        sprk_projecttype_ref: { attributeType: 'Lookup', displayName: 'Project Type', targets: ['sprk_projecttype'] },
        sprk_openeddate: { attributeType: 'DateTime', displayName: 'Opened Date' },
        sprk_highpriority: { attributeType: 'Boolean', displayName: 'High Priority' },
      },
    });
    installXrm(['sprk_projecttype_ref', 'sprk_openeddate', 'sprk_highpriority']);

    render(
      <FluentProvider theme={webLightTheme}>
        <RecordHeaderView
          entityName={ENTITY}
          recordId={RECORD_ID}
          layoutJson={JSON.stringify({
            _version: '1.0',
            fields: [
              { name: 'sprk_projecttype_ref' },
              { name: 'sprk_openeddate' },
              { name: 'sprk_highpriority' },
            ],
          })}
        />
      </FluentProvider>
    );

    await waitFor(() => expect(lastSelect).toBeDefined());
    expect(lastSelect).toContain('_sprk_projecttype_ref_value');
    expect(lastSelect).not.toMatch(/[=,]sprk_projecttype_ref(,|$)/);
  });

  it('renders METADATA display names, not humanized logical names', async () => {
    mockRetrieveEntityMetadata.mockResolvedValue({
      primaryIdAttribute: 'sprk_projectid',
      primaryNameAttribute: 'sprk_projectnumber',
      attributes: {
        sprk_openeddate: { attributeType: 'DateTime', displayName: 'Opened Date' },
        sprk_highpriority: { attributeType: 'Boolean', displayName: 'High Priority' },
      },
    });
    // No form controls at all, so the FORM label chain cannot mask a missing
    // metadata displayName — exactly the UAT condition that surfaced
    // "Openeddate" / "Highpriority".
    installXrm([]);

    render(
      <FluentProvider theme={webLightTheme}>
        <RecordHeaderView
          entityName={ENTITY}
          recordId={RECORD_ID}
          layoutJson={JSON.stringify({
            _version: '1.0',
            fields: [{ name: 'sprk_openeddate' }, { name: 'sprk_highpriority' }],
          })}
        />
      </FluentProvider>
    );

    expect(await screen.findByText('Opened Date')).toBeInTheDocument();
    expect(screen.getByText('High Priority')).toBeInTheDocument();
    expect(screen.queryByText('Openeddate')).not.toBeInTheDocument();
    expect(screen.queryByText('Highpriority')).not.toBeInTheDocument();
  });

  it('requests the layoutJson attributes even when they are NOT on the form', async () => {
    mockRetrieveEntityMetadata.mockResolvedValue(ENTITY_METADATA);
    installXrm(['sprk_projectnumber']);

    render(
      <FluentProvider theme={webLightTheme}>
        <RecordHeaderView
          entityName={ENTITY}
          recordId={RECORD_ID}
          layoutJson={JSON.stringify({
            _version: '1.0',
            summaryField: 'sprk_recordsummary',
            fields: [{ name: 'sprk_projecttype' }],
          })}
        />
      </FluentProvider>
    );

    await waitFor(() => expect(mockRetrieveEntityMetadata).toHaveBeenCalled());

    // Form control first, then the layout's names — including a field absent
    // from the form and the summaryField (which gates the FR-17 sparkle).
    expect(mockRetrieveEntityMetadata).toHaveBeenCalledWith(ENTITY, [
      'sprk_projectnumber',
      'sprk_projecttype',
      'sprk_recordsummary',
    ]);
  });
});

describe('buildRequestedAttributeNames', () => {
  const control = (name: string) => ({ name });

  it('unions form controls with configured names, form order first', () => {
    expect(
      buildRequestedAttributeNames([control('a'), control('b')], ['c', 'sprk_recordsummary'])
    ).toEqual(['a', 'b', 'c', 'sprk_recordsummary']);
  });

  it('de-duplicates across the two sources', () => {
    expect(buildRequestedAttributeNames([control('a'), control('b')], ['b', 'a', 'c'])).toEqual(['a', 'b', 'c']);
  });

  it('drops blanks and non-strings', () => {
    expect(
      buildRequestedAttributeNames(
        [control('a'), control('  '), control('' as string), { name: 42 as unknown as string }],
        ['  b  ', '']
      )
    ).toEqual(['a', 'b']);
  });

  it('returns an empty list when there is nothing to request', () => {
    expect(buildRequestedAttributeNames([], [])).toEqual([]);
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// Sparkle / summaryField wiring (task 034 — FR-17, FR-22, FR-23)
//
// The acceptance matrix in one place:
//
//   attribute in metadata │ value      │ sparkle │ popover body   │ in $select
//   ──────────────────────┼────────────┼─────────┼────────────────┼───────────
//   yes (default field)   │ populated  │ shown   │ the text       │ yes
//   yes (default field)   │ '' or null │ shown   │ "No summary    │ yes
//                         │            │         │  yet."         │
//   yes (configured)      │ populated  │ shown   │ the text       │ yes
//   NO                    │ n/a        │ HIDDEN  │ n/a            │ NO
//
// The last row matters most: a $select naming a column the entity does not
// have fails the WHOLE retrieve with HTTP 400 and blanks every cell — RS-1,
// third occurrence of that failure class (FAILURE-MODES G-12).
// ─────────────────────────────────────────────────────────────────────────────

const SUMMARY_FIELD = 'sprk_recordsummary';

/** `ENTITY_METADATA` plus the summary column the six rollout entities carry. */
const METADATA_WITH_SUMMARY = {
  ...ENTITY_METADATA,
  attributes: {
    ...ENTITY_METADATA.attributes,
    [SUMMARY_FIELD]: { attributeType: 'Memo', displayName: 'Record Summary' },
  },
};

/** A layout that renders two plain fields and configures nothing else. */
const MINIMAL_LAYOUT = JSON.stringify({
  _version: '1.0',
  fields: [{ name: 'sprk_projectnumber' }, { name: 'sprk_projectname' }],
});

/** Replace the record payload after `installXrm()` has wired the rest of Xrm. */
function stubRecord(extra: Record<string, unknown>): void {
  const webApi = (window as unknown as { Xrm: { WebApi: { retrieveRecord: unknown } } }).Xrm.WebApi;
  webApi.retrieveRecord = jest.fn((_entity: string, _id: string, options?: string) => {
    lastSelect = options;
    return Promise.resolve({ ...RECORD, ...extra });
  });
}

// Matches whether the accessible name comes from the Button's own aria-label
// ("View AI summary") or from the wrapping Tooltip's `relationship="label"`
// content ("AI Summary") — the suite should not depend on which one Fluent
// wins with.
const sparkleButton = () => screen.queryByRole('button', { name: /ai summary/i });

describe('buildSelectFields — summaryField branch (FR-23)', () => {
  const field = (name: string, renderer = 'text') =>
    ({ name, renderer, label: name, span: 1, required: false, readOnly: false }) as never;

  it('appends the summary field when one is supplied', () => {
    expect(buildSelectFields([field('a')], SUMMARY_FIELD)).toEqual(['a', SUMMARY_FIELD]);
  });

  it('omits it for every "does not exist" spelling — null, undefined, empty', () => {
    expect(buildSelectFields([field('a')], null)).toEqual(['a']);
    expect(buildSelectFields([field('a')], undefined)).toEqual(['a']);
    expect(buildSelectFields([field('a')], '')).toEqual(['a']);
    expect(buildSelectFields([field('a')])).toEqual(['a']);
  });

  it('does not repeat the column when the layout also renders it', () => {
    expect(buildSelectFields([field(SUMMARY_FIELD), field('a')], SUMMARY_FIELD)).toEqual([SUMMARY_FIELD, 'a']);
  });

  it('appends AFTER the layout fields, leaving render order untouched', () => {
    expect(buildSelectFields([field('b'), field('a')], SUMMARY_FIELD)).toEqual(['b', 'a', SUMMARY_FIELD]);
  });

  it('still decorates lookups when a summary field is present', () => {
    expect(buildSelectFields([field('sprk_projecttype', 'lookup')], SUMMARY_FIELD)).toEqual([
      '_sprk_projecttype_value',
      SUMMARY_FIELD,
    ]);
  });
});

describe('buildMetadataAttributeNames (FR-22)', () => {
  it('adds the default summary candidate so the existence gate can ever pass', () => {
    expect(buildMetadataAttributeNames(['sprk_projectname'])).toEqual(['sprk_projectname', SUMMARY_FIELD]);
  });

  it('does not duplicate it when layoutJson already named it', () => {
    expect(buildMetadataAttributeNames(['a', SUMMARY_FIELD, 'b'])).toEqual(['a', SUMMARY_FIELD, 'b']);
  });

  it('requests BOTH candidates when a different summaryField is configured', () => {
    // Neither can be dropped: which one wins is only known after the resolver
    // runs, and the resolver needs the metadata this list fetches.
    const names = buildMetadataAttributeNames(['sprk_description']);
    expect(names).toContain('sprk_description');
    expect(names).toContain(SUMMARY_FIELD);
  });

  it('produces the bare default when nothing is configured', () => {
    expect(buildMetadataAttributeNames([])).toEqual([SUMMARY_FIELD]);
  });
});

describe('summaryFieldExists (FR-17 — existence, never population)', () => {
  it('is true for an attribute present in metadata', () => {
    expect(summaryFieldExists(METADATA_WITH_SUMMARY as never, SUMMARY_FIELD)).toBe(true);
  });

  it('is false for an attribute absent from metadata', () => {
    expect(summaryFieldExists(ENTITY_METADATA as never, SUMMARY_FIELD)).toBe(false);
  });

  it('is false before metadata resolves, rather than throwing', () => {
    expect(summaryFieldExists(null, SUMMARY_FIELD)).toBe(false);
  });

  it('is false for an empty field name', () => {
    expect(summaryFieldExists(METADATA_WITH_SUMMARY as never, '')).toBe(false);
  });

  it('does not treat inherited Object properties as attributes', () => {
    // `hasOwnProperty`, not `in` — otherwise 'toString' would "exist" on every
    // entity and the sparkle would show for a nonsense summaryField.
    expect(summaryFieldExists(METADATA_WITH_SUMMARY as never, 'toString')).toBe(false);
  });
});

describe('RecordHeaderView — sparkle visible (FR-17 positive)', () => {
  beforeEach(() => {
    mockRetrieveEntityMetadata.mockResolvedValue(METADATA_WITH_SUMMARY);
  });

  it('renders the sparkle when the DEFAULT summary attribute exists', async () => {
    renderView(MINIMAL_LAYOUT);
    await waitFor(() => expect(sparkleButton()).toBeInTheDocument());
  });

  it('requests the summary attribute from metadata even though it is not on the form', async () => {
    renderView(MINIMAL_LAYOUT);
    await waitFor(() => expect(mockRetrieveEntityMetadata).toHaveBeenCalled());
    const [, requested] = mockRetrieveEntityMetadata.mock.calls[0];
    expect(requested).toContain(SUMMARY_FIELD);
  });

  it('adds the column to the $select once it is known to exist', async () => {
    renderView(MINIMAL_LAYOUT);
    await waitFor(() => expect(lastSelect).toContain(SUMMARY_FIELD));
  });

  it('shows the "No summary yet." empty state when the value is null', async () => {
    stubRecord({ [SUMMARY_FIELD]: null });
    renderView(MINIMAL_LAYOUT);

    await waitFor(() => expect(sparkleButton()).toBeInTheDocument());
    // Closed popover renders no body.
    expect(screen.queryByTestId('sparkle-popover-empty')).toBeNull();

    fireEvent.click(sparkleButton() as HTMLElement);

    await waitFor(() => expect(screen.getByTestId('sparkle-popover-empty')).toBeInTheDocument());
    expect(screen.getByTestId('sparkle-popover-empty')).toHaveTextContent(/no summary yet/i);
    expect(screen.queryByTestId('sparkle-popover-summary')).toBeNull();
  });

  it('shows the empty state for an EMPTY STRING too, not just null', async () => {
    stubRecord({ [SUMMARY_FIELD]: '' });
    renderView(MINIMAL_LAYOUT);

    await waitFor(() => expect(sparkleButton()).toBeInTheDocument());
    fireEvent.click(sparkleButton() as HTMLElement);

    await waitFor(() => expect(screen.getByTestId('sparkle-popover-empty')).toBeInTheDocument());
  });

  it('shows the summary text when the column is populated', async () => {
    stubRecord({ [SUMMARY_FIELD]: 'Apollo is a fixed-fee engagement.' });
    renderView(MINIMAL_LAYOUT);

    await waitFor(() => expect(sparkleButton()).toBeInTheDocument());
    fireEvent.click(sparkleButton() as HTMLElement);

    await waitFor(() => expect(screen.getByTestId('sparkle-popover-summary')).toBeInTheDocument());
    expect(screen.getByTestId('sparkle-popover-summary')).toHaveTextContent('Apollo is a fixed-fee engagement.');
    expect(screen.queryByTestId('sparkle-popover-empty')).toBeNull();
  });

  it('lets a configured summaryField outrank the default', async () => {
    stubRecord({ [SUMMARY_FIELD]: 'the DEFAULT column', sprk_description: 'the CONFIGURED column' });
    renderView(
      JSON.stringify({
        _version: '1.0',
        summaryField: 'sprk_description',
        fields: [{ name: 'sprk_projectnumber' }],
      })
    );

    await waitFor(() => expect(sparkleButton()).toBeInTheDocument());
    fireEvent.click(sparkleButton() as HTMLElement);

    await waitFor(() => expect(screen.getByTestId('sparkle-popover-summary')).toBeInTheDocument());
    expect(screen.getByTestId('sparkle-popover-summary')).toHaveTextContent('the CONFIGURED column');
  });
});

describe('RecordHeaderView — sparkle hidden (FR-17 negative)', () => {
  // `ENTITY_METADATA` (the default mock) deliberately has NO summary column —
  // which is also why every other suite in this file renders without a sparkle.

  const BOGUS_LAYOUT = JSON.stringify({
    _version: '1.0',
    summaryField: 'sprk_not_a_real_column',
    fields: [{ name: 'sprk_projectnumber' }, { name: 'sprk_projectname' }],
  });

  it('renders no sparkle when the default attribute is absent from metadata', async () => {
    renderView(MINIMAL_LAYOUT);
    await waitFor(() => expect(screen.getByTestId('header-toolbar')).toBeInTheDocument());
    expect(sparkleButton()).toBeNull();
  });

  it('renders no sparkle when a CONFIGURED summaryField names a non-existent attribute', async () => {
    renderView(BOGUS_LAYOUT);
    await waitFor(() => expect(screen.getByTestId('header-toolbar')).toBeInTheDocument());
    expect(sparkleButton()).toBeNull();
  });

  it('keeps the bogus column OUT of the $select — the RS-1 / HTTP 400 guard', async () => {
    renderView(BOGUS_LAYOUT);
    await waitFor(() => expect(lastSelect).toBeDefined());
    expect(lastSelect).not.toContain('sprk_not_a_real_column');
    expect(lastSelect).not.toContain(SUMMARY_FIELD);
  });

  it('still renders the header fields — a bad summaryField must never blank the form', async () => {
    renderView(BOGUS_LAYOUT);
    expect(await screen.findByText('PRJ-0001')).toBeInTheDocument();
    expect(screen.getByText('Apollo')).toBeInTheDocument();
  });

  it('leaves the To Do and Notepad slots untouched when the sparkle is hidden', async () => {
    renderView(MINIMAL_LAYOUT);
    const icons = await screen.findByTestId('header-toolbar-icons');
    // The launcher slots are the hook's concern, independent of the sparkle
    // gate — hiding one must not hide the others.
    expect(icons.querySelectorAll('button').length).toBeGreaterThan(0);
  });
});

describe('FR-22a — the summary field name has ONE source of truth', () => {
  const CONTROL_DIR = path.join(__dirname, '..', 'control');

  it('is never re-declared as a literal anywhere in the control source', () => {
    // The v1.0.20 sparkle regression WAS a second copy of this literal drifting
    // out of sync with the first. The constant is imported from the shared
    // library; a literal here would re-open that failure mode.
    const offenders = fs
      .readdirSync(CONTROL_DIR)
      .filter(f => f.endsWith('.ts') || f.endsWith('.tsx'))
      .filter(f => fs.readFileSync(path.join(CONTROL_DIR, f), 'utf8').includes(`'${SUMMARY_FIELD}'`));

    expect(offenders).toEqual([]);
  });

  it('imports the constant from the shared library instead', () => {
    const view = fs.readFileSync(path.join(CONTROL_DIR, 'RecordHeaderView.tsx'), 'utf8');
    expect(view).toContain('RECORDSUMMARY_FIELD');
    expect(view).toContain('@spaarke/ui-components/dist/hooks/toolbarLaunchDefaults');
  });
});

describe('DEF-01 — the sparkle refresh icon stays unwired', () => {
  it('adds no network call beyond the record read the header already makes', async () => {
    mockRetrieveEntityMetadata.mockResolvedValue(METADATA_WITH_SUMMARY);
    stubRecord({ [SUMMARY_FIELD]: 'a summary' });
    renderView(MINIMAL_LAYOUT);

    await waitFor(() => expect(sparkleButton()).toBeInTheDocument());
    fireEvent.click(sparkleButton() as HTMLElement);
    await waitFor(() => expect(screen.getByTestId('sparkle-popover-summary')).toBeInTheDocument());

    // The popover body comes from the ALREADY-FETCHED record payload, so
    // opening it triggers no second read — and there is no BFF call to make
    // (NFR-06: this control never leaves the Xrm host context).
    const xrm = (window as unknown as { Xrm: { WebApi: { retrieveRecord: jest.Mock } } }).Xrm;
    expect(xrm.WebApi.retrieveRecord).toHaveBeenCalledTimes(1);
  });
});

// ─────────────────────────────────────────────────────────────────────────────
// UAT round 2 — the Client-API metadata payload is NARROWER than the Web API's
//
// Two fields it does not supply, both of which the header depends on:
//   `Format`  — returned as a NUMBER, so the string-only parse yielded
//               undefined and EVERY DateOnly column rendered a time picker.
//   `Targets` — not in Microsoft's documented Client-API attribute metadata at
//               all, so lookups computed `editable === false` and clicking a
//               lookup cell did nothing.
//
// Both are now filled from the LIVE FORM, which returns documented strings and
// costs no round trip. Third and fourth instances of FAILURE-MODES G-13.
// ─────────────────────────────────────────────────────────────────────────────

describe('normalizeFormFormat', () => {
  it('maps the DateTime form formats onto the metadata vocabulary the resolver reads', () => {
    expect(normalizeFormFormat('date')).toBe('DateOnly');
    expect(normalizeFormFormat('datetime')).toBe('DateAndTime');
  });

  it('is case-insensitive — the Client API is not contractually lower-case', () => {
    expect(normalizeFormFormat('Date')).toBe('DateOnly');
    expect(normalizeFormFormat('DateTime')).toBe('DateAndTime');
  });

  it('passes non-date formats through untouched (inert — the resolver reads only DateOnly)', () => {
    expect(normalizeFormFormat('text')).toBe('text');
    expect(normalizeFormFormat('email')).toBe('email');
    expect(normalizeFormFormat('textarea')).toBe('textarea');
  });

  it('returns undefined for every non-string, rather than coercing', () => {
    // Notably a NUMBER: the metadata `Format` enum is deliberately NOT decoded
    // here, because its meaning depends on the attribute type (0 is DateOnly
    // for a DateTime but Email for a String).
    expect(normalizeFormFormat(0)).toBeUndefined();
    expect(normalizeFormFormat(1)).toBeUndefined();
    expect(normalizeFormFormat(undefined)).toBeUndefined();
    expect(normalizeFormFormat(null)).toBeUndefined();
    expect(normalizeFormFormat('')).toBeUndefined();
  });
});

describe('applyFormControlHints', () => {
  const META = {
    primaryIdAttribute: 'sprk_projectid',
    primaryNameAttribute: 'sprk_projectnumber',
    attributes: {
      sprk_openeddate: { attributeType: 'DateTime' },
      sprk_projecttype: { attributeType: 'Lookup' },
      sprk_projectname: { attributeType: 'String' },
    },
  } as never;

  it('fills a missing format from the form control', () => {
    const out = applyFormControlHints(META, [{ name: 'sprk_openeddate', format: 'DateOnly' }]);
    expect(out.attributes.sprk_openeddate.format).toBe('DateOnly');
  });

  it('fills missing lookup targets from getEntityTypes()', () => {
    const out = applyFormControlHints(META, [
      { name: 'sprk_projecttype', entityTypes: ['sprk_projecttype_ref'] },
    ]);
    expect(out.attributes.sprk_projecttype.targets).toEqual(['sprk_projecttype_ref']);
  });

  it('lets METADATA win — the form only fills blanks', () => {
    const withMeta = {
      ...META,
      attributes: {
        ...META.attributes,
        sprk_openeddate: { attributeType: 'DateTime', format: 'DateAndTime' },
        sprk_projecttype: { attributeType: 'Lookup', targets: ['from_metadata'] },
      },
    } as never;
    const out = applyFormControlHints(withMeta, [
      { name: 'sprk_openeddate', format: 'DateOnly' },
      { name: 'sprk_projecttype', entityTypes: ['from_form'] },
    ]);
    expect(out.attributes.sprk_openeddate.format).toBe('DateAndTime');
    expect(out.attributes.sprk_projecttype.targets).toEqual(['from_metadata']);
  });

  it('treats an EMPTY targets array as a blank worth filling', () => {
    const withEmpty = {
      ...META,
      attributes: { ...META.attributes, sprk_projecttype: { attributeType: 'Lookup', targets: [] } },
    } as never;
    const out = applyFormControlHints(withEmpty, [
      { name: 'sprk_projecttype', entityTypes: ['sprk_projecttype_ref'] },
    ]);
    expect(out.attributes.sprk_projecttype.targets).toEqual(['sprk_projecttype_ref']);
  });

  it('never MUTATES the input — metadata is page-session cached and shared', () => {
    // Mutating in place would leak one form's controls into every other
    // header's view of the same entity.
    const before = JSON.stringify(META);
    const out = applyFormControlHints(META, [{ name: 'sprk_openeddate', format: 'DateOnly' }]);
    expect(JSON.stringify(META)).toBe(before);
    expect(out).not.toBe(META);
  });

  it('returns the SAME object when there is nothing to fill (no needless re-render)', () => {
    expect(applyFormControlHints(META, [])).toBe(META);
    expect(applyFormControlHints(META, [{ name: 'sprk_projectname' }])).toBe(META);
  });

  it('ignores form controls with no matching attribute', () => {
    const out = applyFormControlHints(META, [{ name: 'not_an_attribute', format: 'DateOnly' }]);
    expect(out).toBe(META);
    expect(out.attributes.not_an_attribute).toBeUndefined();
  });
});

describe('RecordHeaderView — DateOnly renders a DATE input, not datetime-local', () => {
  const LAYOUT = JSON.stringify({ _version: '1.0', fields: [{ name: 'sprk_openeddate' }] });

  const META_WITH_DATE = {
    primaryIdAttribute: 'sprk_projectid',
    primaryNameAttribute: 'sprk_projectnumber',
    // `format` ABSENT, exactly as the Client API delivers it.
    attributes: { sprk_openeddate: { attributeType: 'DateTime', displayName: 'Opened Date' } },
  };

  /** DateField is click-to-edit; the native input only mounts in edit mode. */
  const openEditor = async (): Promise<void> => {
    const cell = await screen.findByTestId('record-header-date-field-value');
    fireEvent.click(cell);
    await screen.findByTestId('record-header-date-field-input');
  };

  it('uses the form format when metadata omits it (the UAT defect)', async () => {
    mockRetrieveEntityMetadata.mockResolvedValue(META_WITH_DATE);
    installXrm(['sprk_openeddate'], { formats: { sprk_openeddate: 'date' } });

    const { container } = renderView(LAYOUT);
    await openEditor();

    // Before the fix this was `datetime-local` on EVERY DateOnly column, and
    // committing its `yyyy-MM-ddTHH:mm` value into a DateOnly field errored.
    expect(container.querySelector('input[type="date"]')).toBeInTheDocument();
    expect(container.querySelector('input[type="datetime-local"]')).toBeNull();
  });

  it('still renders datetime-local for a genuine DateAndTime column', async () => {
    mockRetrieveEntityMetadata.mockResolvedValue(META_WITH_DATE);
    installXrm(['sprk_openeddate'], { formats: { sprk_openeddate: 'datetime' } });

    const { container } = renderView(LAYOUT);
    await openEditor();

    expect(container.querySelector('input[type="datetime-local"]')).toBeInTheDocument();
    expect(container.querySelector('input[type="date"]')).toBeNull();
  });
});

describe('RecordHeaderView — a lookup is editable once targets resolve', () => {
  const LAYOUT = JSON.stringify({ _version: '1.0', fields: [{ name: 'sprk_projecttype' }] });

  // `targets` ABSENT — Microsoft does not document `Targets` on the Client-API
  // attribute payload, which is why the picker never opened.
  const META_NO_TARGETS = {
    primaryIdAttribute: 'sprk_projectid',
    primaryNameAttribute: 'sprk_projectnumber',
    attributes: { sprk_projecttype: { attributeType: 'Lookup', displayName: 'Project Type' } },
  };

  it('is NOT editable when neither metadata nor the form supplies targets', async () => {
    mockRetrieveEntityMetadata.mockResolvedValue(META_NO_TARGETS);
    installXrm(['sprk_projecttype']);

    renderView(LAYOUT);

    const cell = await screen.findByTestId('record-header-lookup-field');
    expect(cell.getAttribute('data-editable')).toBe('false');
  });

  it('becomes editable when the form control supplies them (the UAT fix)', async () => {
    mockRetrieveEntityMetadata.mockResolvedValue(META_NO_TARGETS);
    installXrm(['sprk_projecttype'], { entityTypes: { sprk_projecttype: ['sprk_projecttype_ref'] } });

    renderView(LAYOUT);

    const cell = await screen.findByTestId('record-header-lookup-field');
    await waitFor(() => expect(cell.getAttribute('data-editable')).toBe('true'));
  });
});
