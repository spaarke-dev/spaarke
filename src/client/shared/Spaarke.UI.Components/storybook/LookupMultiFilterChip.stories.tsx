/**
 * LookupMultiFilterChip.stories — Storybook stories for the lookup chip primitive.
 *
 * Five interaction states × 2 themes per task 005 POML acceptance:
 *   1. Empty — popover closed, no selections (default presentation)
 *   2. Typing — popover open, user typing triggers debounced search
 *   3. Single-select — one record picked, chip shows just that name
 *   4. Multi-select — three records picked, chip shows "{first} +2 more"
 *   5. Cleared — selections cleared via "Clear all" pill button
 *
 * All stories wrapped in `<FluentProvider applyStylesToPortals theme={...} />`
 * — the canonical NFR-03 root-provider pattern.
 *
 * **Mock client design**: the mock returns a fixed corpus of 5 "Acme*" + 5
 * "Beta*" + 40 generic records. The story uses a `callCounter` ref to assert
 * the debounce contract (typing "Acme" triggers exactly ONE call). Stories
 * that need the counter expose it via on-screen counter Text — the Default /
 * Typing variants both show the counter to give the developer-tester visual
 * confirmation.
 *
 * **Storybook config**: parallels DataGrid.stories.tsx — lives OUTSIDE `src/`
 * so the TypeScript library build (`tsc`) does not pick it up. When
 * Storybook is wired in a later task, the standard
 * `stories: ['../storybook/**\/*.stories.@(ts|tsx)']` pattern picks it up.
 *
 * @see projects/spaarke-datagrid-framework-r1/tasks/005-lookup-multi-filter-chip.poml
 */

import * as React from 'react';
import { FluentProvider, Text, webLightTheme, webDarkTheme, makeStyles, tokens } from '@fluentui/react-components';
import { LookupMultiFilterChip, type LookupRecord } from '../src/components/DataGrid/chips/LookupMultiFilterChip';
import type {
  IDataverseClient,
  SavedQueryResult,
  SavedQuerySummary,
  EntityMetadata,
  FetchMultipleResult,
} from '../src/services/IDataverseClient';

// ─────────────────────────────────────────────────────────────────────────────
// Mock corpus + IDataverseClient factory — STORYBOOK-ONLY
// ─────────────────────────────────────────────────────────────────────────────

interface MockEmployee {
  systemuserid: string;
  fullname: string;
  createdon: string;
}

/**
 * Build a deterministic 50-row corpus: 5 "Acme employee N", 5 "Beta employee N",
 * 40 generic "Employee N". `createdon` is monotonically decreasing so "top 50
 * recent" returns the array as-is (well, sliced if smaller).
 */
function buildMockCorpus(): MockEmployee[] {
  const rows: MockEmployee[] = [];
  const baseTs = Date.now();
  for (let i = 1; i <= 5; i++) {
    rows.push({
      systemuserid: `acme-${i}`,
      fullname: `Acme employee ${i}`,
      createdon: new Date(baseTs - i * 1000).toISOString(),
    });
  }
  for (let i = 1; i <= 5; i++) {
    rows.push({
      systemuserid: `beta-${i}`,
      fullname: `Beta employee ${i}`,
      createdon: new Date(baseTs - (10 + i) * 1000).toISOString(),
    });
  }
  for (let i = 1; i <= 40; i++) {
    rows.push({
      systemuserid: `gen-${i}`,
      fullname: `Employee ${i}`,
      createdon: new Date(baseTs - (100 + i) * 1000).toISOString(),
    });
  }
  return rows;
}

interface MockClientInstance extends IDataverseClient {
  /** Test-helper: how many times has retrieveMultipleRecords been called? */
  getCallCount(): number;
  /** Test-helper: reset the counter (between stories). */
  resetCallCount(): void;
}

/**
 * Build a mock client with a `callCount` counter so stories can visually
 * assert the debounce contract: typing "Acme" should produce exactly ONE
 * network call, not one per keystroke.
 */
function createMockClient(corpus: MockEmployee[] = buildMockCorpus()): MockClientInstance {
  let callCount = 0;
  const client: MockClientInstance = {
    getCallCount: () => callCount,
    resetCallCount: () => {
      callCount = 0;
    },
    retrieveSavedQuery: async (_id: string): Promise<SavedQueryResult> => ({
      entityName: 'systemuser',
      fetchXml: '<fetch />',
      layoutXml: '<grid />',
      name: 'mock',
    }),
    retrieveSavedQueriesForEntity: async (_e: string): Promise<SavedQuerySummary[]> => [],
    retrieveEntityMetadata: async (_e: string): Promise<EntityMetadata> => ({
      primaryIdAttribute: 'systemuserid',
      primaryNameAttribute: 'fullname',
      attributes: {},
    }),
    retrieveMultipleRecords: async <T = Record<string, unknown>,>(
      _entity: string,
      fetchXml: string
    ): Promise<FetchMultipleResult<T>> => {
      callCount++;
      // Parse the `like %term%` filter if present.
      const likeMatch = fetchXml.match(/value="%([^%]*)%"/);
      const search = likeMatch ? likeMatch[1].toLowerCase() : '';
      const topMatch = fetchXml.match(/top="(\d+)"/);
      const top = topMatch ? Number.parseInt(topMatch[1], 10) : 50;
      // Simulate ~100ms server latency so the spinner shows up.
      await new Promise<void>(resolve => setTimeout(resolve, 100));
      const filtered = search ? corpus.filter(r => r.fullname.toLowerCase().includes(search)) : corpus;
      const slice = filtered.slice(0, top);
      return {
        entities: slice as unknown as T[],
        moreRecords: filtered.length > slice.length,
      };
    },
    retrieveRecord: async <T = Record<string, unknown>,>(
      _entity: string,
      id: string,
      _select?: string[]
    ): Promise<T> => {
      const hit = corpus.find(r => r.systemuserid === id);
      if (!hit) throw new Error(`Record ${id} not found`);
      return hit as unknown as T;
    },
  };
  return client;
}

// ─────────────────────────────────────────────────────────────────────────────
// Story chrome
// ─────────────────────────────────────────────────────────────────────────────

const useStoryStyles = makeStyles({
  // Story canvas — gives the chip room to breathe so the popover doesn't clip.
  canvas: {
    minHeight: '400px',
    minWidth: '600px',
    padding: tokens.spacingVerticalL,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  counter: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
  },
});

interface StoryArgs {
  theme: 'light' | 'dark';
}

const StoryShell: React.FC<{ theme: 'light' | 'dark'; children: React.ReactNode }> = ({ theme, children }) => {
  // NFR-03: applyStylesToPortals on the root provider so the Popover surface
  // — which renders into a React portal — inherits the active theme.
  return (
    <FluentProvider
      applyStylesToPortals
      theme={theme === 'dark' ? webDarkTheme : webLightTheme}
      style={{ height: '100%', display: 'flex' }}
    >
      {children}
    </FluentProvider>
  );
};

// CSF default export — Storybook will pick this up once `.storybook` is configured.
export default {
  title: 'DataGrid/Chips/LookupMultiFilterChip',
  component: LookupMultiFilterChip,
  argTypes: {
    theme: { control: { type: 'radio' }, options: ['light', 'dark'] },
  },
};

// ─────────────────────────────────────────────────────────────────────────────
// Story 1: Empty — popover closed, no selections
// ─────────────────────────────────────────────────────────────────────────────

export const Empty = (args: StoryArgs) => {
  const client = React.useMemo(() => createMockClient(), []);
  const [value, setValue] = React.useState<Set<string>>(new Set());
  const styles = useStoryStyles();
  return (
    <StoryShell theme={args.theme}>
      <div className={styles.canvas}>
        <Text size={300}>State: empty (no selections, popover closed).</Text>
        <LookupMultiFilterChip
          lookupTargetEntity="systemuser"
          primaryNameAttribute="fullname"
          label="Owner"
          value={value}
          onChange={setValue}
          dataverseClient={client}
        />
      </div>
    </StoryShell>
  );
};
Empty.args = { theme: 'light' as const };
Empty.storyName = 'Empty (no selections)';

// ─────────────────────────────────────────────────────────────────────────────
// Story 2: Typing — popover open, user types "Acme"
//   The on-screen counter assertion: type "Acme" → 300ms idle → counter
//   increments by EXACTLY 1 (debounce verified visually).
// ─────────────────────────────────────────────────────────────────────────────

export const Typing = (args: StoryArgs) => {
  const client = React.useMemo(() => createMockClient(), []);
  const [value, setValue] = React.useState<Set<string>>(new Set());
  const [callCount, setCallCount] = React.useState<number>(0);
  const styles = useStoryStyles();

  // Poll the mock's call counter so the on-screen badge updates as the
  // chip issues lookups. (Simple polling avoids tying the mock to React
  // state, which would complicate the contract.)
  React.useEffect(() => {
    const handle = setInterval(() => setCallCount(client.getCallCount()), 100);
    return () => clearInterval(handle);
  }, [client]);

  return (
    <StoryShell theme={args.theme}>
      <div className={styles.canvas}>
        <Text size={300}>
          State: typing. Type <strong>&quot;Acme&quot;</strong> in the chip — after 300ms you should see exactly ONE
          lookup call (debounce verified).
        </Text>
        <Text className={styles.counter}>
          Lookup calls so far: <strong data-testid="story-call-counter">{callCount}</strong>
        </Text>
        <LookupMultiFilterChip
          lookupTargetEntity="systemuser"
          primaryNameAttribute="fullname"
          label="Owner"
          value={value}
          onChange={setValue}
          dataverseClient={client}
        />
      </div>
    </StoryShell>
  );
};
Typing.args = { theme: 'light' as const };
Typing.storyName = 'Typing (debounce verification)';

// ─────────────────────────────────────────────────────────────────────────────
// Story 3: Single-select — one record pre-picked
// ─────────────────────────────────────────────────────────────────────────────

export const SingleSelect = (args: StoryArgs) => {
  const client = React.useMemo(() => createMockClient(), []);
  const [value, setValue] = React.useState<Set<string>>(new Set(['acme-1']));
  const selectedRecords: ReadonlyArray<LookupRecord> = [{ id: 'acme-1', name: 'Acme employee 1' }];
  const styles = useStoryStyles();
  return (
    <StoryShell theme={args.theme}>
      <div className={styles.canvas}>
        <Text size={300}>State: single-select. The chip label reads &quot;Acme employee 1&quot;.</Text>
        <LookupMultiFilterChip
          lookupTargetEntity="systemuser"
          primaryNameAttribute="fullname"
          label="Owner"
          value={value}
          onChange={setValue}
          selectedRecords={selectedRecords}
          dataverseClient={client}
        />
      </div>
    </StoryShell>
  );
};
SingleSelect.args = { theme: 'light' as const };
SingleSelect.storyName = 'Single-select';

// ─────────────────────────────────────────────────────────────────────────────
// Story 4: Multi-select — three records pre-picked
//   Chip label reads "Acme employee 1 +2 more".
// ─────────────────────────────────────────────────────────────────────────────

export const MultiSelect = (args: StoryArgs) => {
  const client = React.useMemo(() => createMockClient(), []);
  const [value, setValue] = React.useState<Set<string>>(new Set(['acme-1', 'acme-2', 'beta-3']));
  const selectedRecords: ReadonlyArray<LookupRecord> = [
    { id: 'acme-1', name: 'Acme employee 1' },
    { id: 'acme-2', name: 'Acme employee 2' },
    { id: 'beta-3', name: 'Beta employee 3' },
  ];
  const styles = useStoryStyles();
  return (
    <StoryShell theme={args.theme}>
      <div className={styles.canvas}>
        <Text size={300}>
          State: multi-select. The chip label reads &quot;Acme employee 1 +2 more&quot;. Opening the popover shows three
          dismissible pills.
        </Text>
        <LookupMultiFilterChip
          lookupTargetEntity="systemuser"
          primaryNameAttribute="fullname"
          label="Owner"
          value={value}
          onChange={setValue}
          selectedRecords={selectedRecords}
          dataverseClient={client}
        />
      </div>
    </StoryShell>
  );
};
MultiSelect.args = { theme: 'light' as const };
MultiSelect.storyName = 'Multi-select (3 records)';

// ─────────────────────────────────────────────────────────────────────────────
// Story 5: Cleared — was multi-select, then "Clear all"; ends in empty state
//   Demonstrates the round-trip: pre-populated → cleared via UI returns to
//   empty-state label and zero pills.
// ─────────────────────────────────────────────────────────────────────────────

export const Cleared = (args: StoryArgs) => {
  const client = React.useMemo(() => createMockClient(), []);
  const initialSelection = new Set(['acme-1', 'acme-2']);
  const [value, setValue] = React.useState<Set<string>>(initialSelection);
  const selectedRecords: ReadonlyArray<LookupRecord> = [
    { id: 'acme-1', name: 'Acme employee 1' },
    { id: 'acme-2', name: 'Acme employee 2' },
  ];
  const styles = useStoryStyles();

  const handleResetToCleared = React.useCallback(() => {
    setValue(new Set());
  }, []);
  const handleResetToInitial = React.useCallback(() => {
    setValue(new Set(initialSelection));
    // intentional: we want the parent to recreate the set each time
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    <StoryShell theme={args.theme}>
      <div className={styles.canvas}>
        <Text size={300}>
          State: cleared. Initial state has 2 selections; use the chip&apos;s &quot;Clear all&quot; button (or the reset
          buttons below) to toggle. When cleared the label reverts to <strong>&quot;Owner&quot;</strong>.
        </Text>
        <LookupMultiFilterChip
          lookupTargetEntity="systemuser"
          primaryNameAttribute="fullname"
          label="Owner"
          value={value}
          onChange={setValue}
          selectedRecords={selectedRecords}
          dataverseClient={client}
        />
        <div style={{ display: 'flex', gap: tokens.spacingHorizontalS }}>
          <button onClick={handleResetToCleared}>Reset to cleared</button>
          <button onClick={handleResetToInitial}>Reset to 2 selections</button>
        </div>
      </div>
    </StoryShell>
  );
};
Cleared.args = { theme: 'light' as const };
Cleared.storyName = 'Cleared (was multi-select)';
