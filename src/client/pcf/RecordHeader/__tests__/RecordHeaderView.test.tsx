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
import { render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';

// ─────────────────────────────────────────────────────────────────────────────
// Mock the metadata client BEFORE importing the view.
// ─────────────────────────────────────────────────────────────────────────────

const mockRetrieveEntityMetadata = jest.fn();

jest.mock('@spaarke/ui-components/dist/services/XrmDataverseClient', () => ({
  XrmDataverseClient: class {
    retrieveEntityMetadata(entityName: string): Promise<unknown> {
      return mockRetrieveEntityMetadata(entityName);
    }
  },
}));

import {
  RecordHeaderView,
  buildSelectFields,
  toCellSpan,
  toNumberKind,
  extractCurrencySymbol,
} from '../control/RecordHeaderView';
import { resolveEntityContext } from '../control/entityContext';
import { buildHeaderFormMetadata, readFormControlOrder } from '../control/useHeaderFormMetadata';
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

function installXrm(formControlNames: string[] = Object.keys(ENTITY_METADATA.attributes)): void {
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
    getAttribute: () => ({ getRequiredLevel: () => (name === 'sprk_projectnumber' ? 'required' : 'none') }),
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

  it('starts at 1.1.0 (the new control identity baseline)', () => {
    expect(CONTROL_VERSION).toBe('1.1.0');
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
    expect(mockRetrieveEntityMetadata).toHaveBeenCalledWith('sprk_widget');
  });
});
