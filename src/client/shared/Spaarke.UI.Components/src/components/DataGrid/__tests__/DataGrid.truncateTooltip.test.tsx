/**
 * DataGrid.truncateTooltip.test — opt-in ellipsis + tooltip on truncated text cells, R4 task 023a.
 *
 * Renders the REAL DataGrid over a mock IDataverseClient. jsdom has no layout, so overflow is simulated by
 * overriding `scrollWidth` / `clientWidth`. Pairs:
 *  - off (default): no truncation wrapper and no tooltip, even when the text would overflow;
 *  - on (configuration `display.truncateWithTooltip`, or the prop): an overflowing cell shows its full text in a
 *    tooltip; a cell that fits shows none; the prop `false` wins over the configuration; a host column renderer is
 *    left alone.
 */
import * as React from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { FluentProvider, webLightTheme } from '@fluentui/react-components';
import { DataGrid } from '../DataGrid';
import type {
  IDataverseClient,
  EntityMetadata,
  FetchMultipleResult,
  SavedQueryResult,
} from '../../../services/IDataverseClient';
import type { DataGridConfiguration } from '../../../types/DataGridConfiguration';

const CONFIG_ID = '61711823-1092-f111-b8dc-7ced8ddc4a05';

const FETCH =
  "<fetch><entity name='sprk_thing'><attribute name='sprk_thingid'/><attribute name='sprk_name'/>" +
  "<attribute name='sprk_summary'/><order attribute='sprk_name' descending='false'/></entity></fetch>";
const LAYOUT =
  "<grid name='result' object='1' jump='sprk_name' select='1'><row name='result' id='sprk_thingid'>" +
  "<cell name='sprk_name' width='220'/><cell name='sprk_summary' width='200'/></row></grid>";

const LONG_SUMMARY = 'A long summary that does not fit the column and is cut off with an ellipsis';

function configWith(truncateWithTooltip?: boolean): DataGridConfiguration {
  return {
    _version: '1.0',
    source: { type: 'inline', fetchXml: FETCH, layoutXml: LAYOUT },
    display: { title: 'Things', ...(truncateWithTooltip === undefined ? {} : { truncateWithTooltip }) },
  };
}

const METADATA: EntityMetadata = {
  primaryIdAttribute: 'sprk_thingid',
  primaryNameAttribute: 'sprk_name',
  attributes: {
    sprk_thingid: { attributeType: 'String', isPrimaryId: true },
    sprk_name: { attributeType: 'String', isPrimaryName: true, displayName: 'Name' },
    sprk_summary: { attributeType: 'Memo', displayName: 'Summary' },
  },
};

function makeClient(config: DataGridConfiguration): IDataverseClient {
  return {
    retrieveRecord: jest.fn(async () => ({ sprk_configjson: JSON.stringify(config) })),
    retrieveSavedQuery: jest.fn(
      async (): Promise<SavedQueryResult> => ({
        entityName: 'sprk_thing',
        fetchXml: FETCH,
        layoutXml: LAYOUT,
        name: 'Things',
      })
    ),
    retrieveSavedQueriesForEntity: jest.fn(async () => []),
    retrieveEntityMetadata: jest.fn(async () => METADATA),
    retrieveMultipleRecords: jest.fn(
      async (): Promise<FetchMultipleResult> => ({
        entities: [{ sprk_thingid: 't-1', sprk_name: 'First thing', sprk_summary: LONG_SUMMARY }],
        moreRecords: false,
      })
    ),
  } as unknown as IDataverseClient;
}

function renderGrid(config: DataGridConfiguration, extra: Partial<React.ComponentProps<typeof DataGrid>> = {}) {
  return render(
    <FluentProvider theme={webLightTheme}>
      <DataGrid configId={CONFIG_ID} dataverseClient={makeClient(config)} showViewSelector={false} {...extra} />
    </FluentProvider>
  );
}

/** Simulates layout: every element reports `scrollWidth` / `clientWidth` as given. */
function simulateWidths(scrollWidth: number, clientWidth: number) {
  Object.defineProperty(HTMLElement.prototype, 'scrollWidth', { configurable: true, get: () => scrollWidth });
  Object.defineProperty(HTMLElement.prototype, 'clientWidth', { configurable: true, get: () => clientWidth });
}

const hover = (el: Element) => {
  fireEvent.pointerEnter(el);
  fireEvent.mouseEnter(el);
};

/** Waits past the tooltip's show delay (250 ms) and reports whether any tooltip is in the document. */
async function tooltipShownAfterDelay(): Promise<HTMLElement | null> {
  await new Promise(r => setTimeout(r, 400));
  return screen.queryByRole('tooltip');
}

describe('DataGrid — truncateWithTooltip', () => {
  const scrollDesc = Object.getOwnPropertyDescriptor(HTMLElement.prototype, 'scrollWidth');
  const clientDesc = Object.getOwnPropertyDescriptor(HTMLElement.prototype, 'clientWidth');

  beforeEach(() => {
    jest.spyOn(console, 'info').mockImplementation(() => undefined);
  });
  afterEach(() => {
    jest.restoreAllMocks();
    // Restore jsdom's own (prototype-chain) definitions.
    if (scrollDesc) Object.defineProperty(HTMLElement.prototype, 'scrollWidth', scrollDesc);
    else delete (HTMLElement.prototype as unknown as Record<string, unknown>).scrollWidth;
    if (clientDesc) Object.defineProperty(HTMLElement.prototype, 'clientWidth', clientDesc);
    else delete (HTMLElement.prototype as unknown as Record<string, unknown>).clientWidth;
  });

  it('control: off by default — no truncation wrapper and no tooltip, even on overflow', async () => {
    simulateWidths(500, 100);
    const { container } = renderGrid(configWith());
    const cell = await screen.findByText(LONG_SUMMARY);
    expect(container.querySelectorAll('[data-truncate-cell]')).toHaveLength(0);
    // The value is the cell's own text, exactly as before.
    expect(cell.getAttribute('role')).toBe('gridcell');
    hover(cell);
    expect(await tooltipShownAfterDelay()).toBeNull();
  });

  it('on (configuration): an overflowing cell shows its full text in a tooltip', async () => {
    simulateWidths(500, 100);
    renderGrid(configWith(true));
    const span = await screen.findByText(LONG_SUMMARY);
    expect(span).toHaveAttribute('data-truncate-cell');
    hover(span);
    const tooltip = await screen.findByRole('tooltip');
    expect(tooltip).toHaveTextContent(LONG_SUMMARY);
  });

  it('on: the primary-name link is wrapped too and shows its tooltip on overflow', async () => {
    simulateWidths(500, 100);
    renderGrid(configWith(true));
    const link = await screen.findByRole('button', { name: 'First thing' });
    const wrapper = link.closest('[data-truncate-cell]') as HTMLElement;
    expect(wrapper).not.toBeNull();
    hover(wrapper);
    expect(await screen.findByRole('tooltip')).toHaveTextContent('First thing');
  });

  it('on: a cell whose text fits shows no tooltip', async () => {
    simulateWidths(100, 100);
    renderGrid(configWith(true));
    const span = await screen.findByText(LONG_SUMMARY);
    expect(span).toHaveAttribute('data-truncate-cell');
    hover(span);
    expect(await tooltipShownAfterDelay()).toBeNull();
  });

  describe('keyboard focus (focusMode="composite" puts focus on the gridcell, not on the inner span)', () => {
    const cellOf = (el: Element) => el.closest('[role="gridcell"]') as HTMLElement;

    it('focusing the gridcell of an overflowing cell shows the tooltip; leaving it hides it', async () => {
      simulateWidths(500, 100);
      renderGrid(configWith(true));
      const span = await screen.findByText(LONG_SUMMARY);
      const cell = cellOf(span);
      expect(cell).not.toBeNull();
      expect(screen.queryByRole('tooltip')).toBeNull();

      fireEvent.focusIn(cell);
      expect(await screen.findByRole('tooltip')).toHaveTextContent(LONG_SUMMARY);

      fireEvent.focusOut(cell);
      await waitFor(() => expect(screen.queryByRole('tooltip')).toBeNull());
    });

    it('focusing the gridcell of a cell whose text fits shows no tooltip', async () => {
      simulateWidths(100, 100);
      renderGrid(configWith(true));
      const span = await screen.findByText(LONG_SUMMARY);
      fireEvent.focusIn(cellOf(span));
      expect(await tooltipShownAfterDelay()).toBeNull();
    });

    it('with truncation off, focusing the gridcell shows no tooltip even on overflow', async () => {
      simulateWidths(500, 100);
      renderGrid(configWith());
      const cell = cellOf(await screen.findByText(LONG_SUMMARY));
      fireEvent.focusIn(cell);
      expect(await tooltipShownAfterDelay()).toBeNull();
    });

    it('the primary-name cell shows the tooltip on cell focus and its link still opens the record', async () => {
      simulateWidths(500, 100);
      const onRecordOpen = jest.fn();
      renderGrid(configWith(true), { onRecordOpen });
      const link = await screen.findByRole('button', { name: 'First thing' });
      fireEvent.focusIn(cellOf(link));
      expect(await screen.findByRole('tooltip')).toHaveTextContent('First thing');
      fireEvent.click(link);
      expect(onRecordOpen).toHaveBeenCalledTimes(1);
      expect(onRecordOpen).toHaveBeenCalledWith('t-1', expect.anything(), expect.anything());
    });
  });

  it('on (prop): the prop turns it on without the configuration', async () => {
    simulateWidths(500, 100);
    renderGrid(configWith(), { truncateWithTooltip: true });
    const span = await screen.findByText(LONG_SUMMARY);
    expect(span).toHaveAttribute('data-truncate-cell');
    hover(span);
    expect(await screen.findByRole('tooltip')).toHaveTextContent(LONG_SUMMARY);
  });

  it('the prop false wins over the configuration', async () => {
    simulateWidths(500, 100);
    const { container } = renderGrid(configWith(true), { truncateWithTooltip: false });
    await screen.findByText(LONG_SUMMARY);
    expect(container.querySelectorAll('[data-truncate-cell]')).toHaveLength(0);
  });

  it('a host column renderer is left alone', async () => {
    simulateWidths(500, 100);
    renderGrid(configWith(true), {
      overrides: { columnRenderers: { sprk_summary: v => <em data-testid="custom">{String(v)}</em> } },
    });
    const custom = await screen.findByTestId('custom');
    expect(custom.closest('[data-truncate-cell]')).toBeNull();
  });
});
