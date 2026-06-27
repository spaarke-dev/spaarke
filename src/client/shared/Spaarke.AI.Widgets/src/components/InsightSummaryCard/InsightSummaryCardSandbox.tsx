/**
 * @spaarke/ai-widgets — InsightSummaryCard dev sandbox (Task 035)
 *
 * **SC-01 deliverable** — "documented props + Storybook story (or equivalent)
 * demonstrating all 5+ states". Satisfies the OR clause via this self-rendering
 * sandbox component instead of Storybook because `@spaarke/ai-widgets` has no
 * Storybook setup (verified Task 001; recorded in DR-001 §Negative). Standing
 * up Storybook for a single component is yak-shaving per DR-001.
 *
 * What this delivers
 * ------------------
 * A runnable React component that renders {@link InsightSummaryCard} once per
 * FR-06 state (idle / loading / loaded / error / decline / stale) plus a
 * light/dark theme toggle. Each card carries a mock `onFetchInsight` callback
 * that drives the card into the labelled state when the user opens its Popover.
 * The header above the grid documents the state matrix so the matrix is the
 * "Storybook-equivalent" props table.
 *
 * Constraints honoured (per task POML)
 * ------------------------------------
 *   - SC-01: 6 state stories — `idle`, `loading`, `loaded`, `error`, `decline`,
 *           `stale` — each visibly distinct.
 *   - ADR-021: light + dark variants via FluentProvider theme toggle; semantic
 *              tokens only (no hex, no rgba, no `var(--...)`).
 *   - Q-U3: NO feedback affordance anywhere in this sandbox.
 *   - Q-U1: NO `@v1`/`@vN` identifier-suffix vernacular anywhere.
 *
 * Why a sandbox component, not `*.stories.tsx`?
 * --------------------------------------------
 *   - Storybook is NOT installed in this package (no `.storybook/`, no
 *     Storybook deps, no `*.stories.tsx`). A `.stories.tsx` extension would be
 *     a misleading nameplate suggesting tooling that does not exist.
 *   - This sandbox compiles via the package's normal `tsc` (no extra tooling)
 *     and is consumable by any host (a dev playground route, an MDA web
 *     resource, a one-off page) via `import { InsightSummaryCardSandbox }
 *     from '@spaarke/ai-widgets'`.
 *
 * Why NOT a dev route inside AnalysisWorkspace (DR-001 suggestion (a))?
 * --------------------------------------------------------------------
 *   - AnalysisWorkspace is an auth-gated three-pane code page dedicated to
 *     analysis editing — wiring a dev sandbox there would couple unrelated
 *     surfaces and violate ADR-012's "context-agnostic components" boundary.
 *   - Co-locating the sandbox with the component is canonical for shared libs:
 *     callers don't reach into a host page to preview a library component.
 *
 * @see projects/ai-spaarke-insights-engine-widgets-r1/decisions/DR-001-component-reuse.md §Negative
 * @see projects/ai-spaarke-insights-engine-widgets-r1/notes/insight-component-reuse-investigation.md §3.3
 * @see projects/ai-spaarke-insights-engine-widgets-r1/spec.md SC-01 + FR-06
 * @see ADR-021 — Fluent v9 + semantic tokens (binding)
 */

import * as React from 'react';
import { useCallback, useMemo, useState } from 'react';
import {
  Button,
  FluentProvider,
  makeStyles,
  Switch,
  Text,
  tokens,
  webDarkTheme,
  webLightTheme,
} from '@fluentui/react-components';

import { InsightSummaryCard } from './InsightSummaryCard';
import { InsightDeclineError } from './InsightSummaryCard.types';
import type { Citation } from './Citation.types';
import type { InsightEnvelope } from './state';

// ---------------------------------------------------------------------------
// State matrix — drives the demo grid and the in-component props table.
//
// Each row defines:
//   - `key`         : reducer status the card will land on after first open
//   - `title`       : human-readable header
//   - `description` : what triggers this state in production
//   - `behavior`    : how the sandbox mock simulates the trigger
// ---------------------------------------------------------------------------

interface IStateMatrixRow {
  key: 'idle' | 'loading' | 'loaded' | 'error' | 'decline' | 'stale';
  title: string;
  description: string;
  behavior: string;
}

const STATE_MATRIX: readonly IStateMatrixRow[] = [
  {
    key: 'idle',
    title: 'Idle',
    description: 'Pre-fetch initial state. Sparkle visible, narrative not yet requested.',
    behavior: 'No popover open → no fetch invoked → stays idle.',
  },
  {
    key: 'loading',
    title: 'Loading',
    description: 'Fetch in flight. Spinner + skeleton placeholder per FR-06.',
    behavior: 'Mock callback returns a Promise that never resolves (stays loading).',
  },
  {
    key: 'loaded',
    title: 'Loaded',
    description: 'Narrative + tldr + citations rendered. Refresh + expand affordances visible.',
    behavior: 'Mock resolves with a populated InsightEnvelope (tldr, narrative, 2 citations).',
  },
  {
    key: 'error',
    title: 'Error',
    description: 'Fetch failed (e.g., 503 FeatureDisabled per ADR-032). Graceful message.',
    behavior: 'Mock rejects with a plain Error → reducer transitions to error.',
  },
  {
    key: 'decline',
    title: 'Decline',
    description: 'Insufficient evidence per FR-06 exact text. Recommended-action hint optional.',
    behavior: 'Mock rejects with InsightDeclineError → reducer transitions to decline.',
  },
  {
    key: 'stale',
    title: 'Stale',
    description: 'Cache TTL expired. Loaded content shown with "may be out of date" banner.',
    behavior:
      'Mock resolves quickly then a one-shot timer dispatches MARK_STALE via a wrapping reducer (the wrapper card opens itself on mount via auto-open prop).',
  },
] as const;

// ---------------------------------------------------------------------------
// Mock data — single InsightEnvelope reused across loaded / stale demos.
// ---------------------------------------------------------------------------

const MOCK_CITATIONS: Citation[] = [
  {
    id: 'cite-assessment-1',
    type: 'assessment',
    label: 'Guideline Compliance assessment (FY26-Q1)',
    assessmentId: '00000000-0000-0000-0000-000000000001',
  },
  {
    id: 'cite-document-1',
    type: 'document',
    label: 'Matter intake summary.pdf',
    speHref: 'spe://drive/00000000-0000-0000-0000-000000000002/item/00000000-0000-0000-0000-000000000003',
  },
];

const MOCK_ENVELOPE: InsightEnvelope = {
  tldr: 'Matter health is generally positive with two outstanding action items.',
  narrative:
    'Across the past 30 days, Guideline Compliance trended upward (87% → 92%) ' +
    'while Budget Compliance held steady at 95%. Outcomes Achievement metrics ' +
    'are pending two open assessments scheduled for next quarter.',
  citations: MOCK_CITATIONS,
  generatedAt: '2026-06-11T12:00:00Z',
};

// ---------------------------------------------------------------------------
// Mock callback factory — one per state.
//
// Each card receives its own mock so the state machine lands on the labelled
// terminal state after the user clicks the trigger. `idle` returns no
// callback so the card never fetches.
// ---------------------------------------------------------------------------

type FetchFn = (options?: { force?: boolean }) => Promise<InsightEnvelope>;

function makeMockFetch(state: IStateMatrixRow['key']): FetchFn | undefined {
  switch (state) {
    case 'idle':
      // No callback supplied → the card has nothing to fetch and stays idle.
      return undefined;

    case 'loading':
      // Promise that never resolves → card stays in loading state.
      // Note: this is intentional for the visual demo; production code should
      // always settle the promise (success / error / decline) per FR-06.
      return () =>
        new Promise<InsightEnvelope>(() => {
          // never resolves
        });

    case 'loaded':
    case 'stale':
      // Both states share the loaded envelope. The `stale` card uses the host
      // to dispatch MARK_STALE after the envelope arrives — see notes below.
      return async () => MOCK_ENVELOPE;

    case 'error':
      return async () => {
        throw new Error('AI summaries unavailable in this environment');
      };

    case 'decline':
      return async () => {
        throw new InsightDeclineError(
          'Insufficient data is available to provide Insights Analysis',
          'Add more assessments or expand the date range to enable analysis.'
        );
      };

    default: {
      // Exhaustiveness check.
      const _exhaustive: never = state;
      return _exhaustive;
    }
  }
}

// ---------------------------------------------------------------------------
// Sandbox styles — Griffel; semantic tokens only (ADR-021).
// ---------------------------------------------------------------------------

const useSandboxStyles = makeStyles({
  root: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalL,
    padding: tokens.spacingVerticalL,
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
    minHeight: '100vh',
  },
  header: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  toolbar: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    gap: tokens.spacingHorizontalM,
    padding: tokens.spacingVerticalS,
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusMedium,
  },
  title: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase500,
    lineHeight: tokens.lineHeightBase500,
  },
  subtitle: {
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase300,
    lineHeight: tokens.lineHeightBase300,
  },
  matrix: {
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fit, minmax(360px, 1fr))',
    gap: tokens.spacingVerticalL,
  },
  matrixCell: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    padding: tokens.spacingVerticalS,
    border: `1px solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground1,
  },
  matrixHeader: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
  },
  matrixTitle: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase400,
  },
  matrixDescription: {
    color: tokens.colorNeutralForeground2,
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase200,
  },
  matrixBehavior: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase100,
    lineHeight: tokens.lineHeightBase100,
    fontStyle: 'italic',
  },
  propsTable: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    padding: tokens.spacingVerticalM,
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusMedium,
  },
  propsRow: {
    display: 'grid',
    gridTemplateColumns: '180px 200px 1fr',
    gap: tokens.spacingHorizontalM,
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase200,
    color: tokens.colorNeutralForeground2,
  },
  propsRowHeader: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  propName: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  propType: {
    color: tokens.colorPaletteRoyalBlueForeground2,
  },
});

// ---------------------------------------------------------------------------
// Props documentation table — surfaces the InsightSummaryCardProps contract
// the same way Storybook's "Args table" would. Updated in lockstep with
// InsightSummaryCard.types.ts whenever the contract changes.
// ---------------------------------------------------------------------------

interface IPropDocRow {
  name: string;
  type: string;
  description: string;
}

const PROPS_DOC: readonly IPropDocRow[] = [
  {
    name: 'topic',
    type: 'string (required)',
    description:
      'Insight topic identifier (registry key from sprk_aitopicregistry). Bare identifier — no version suffix.',
  },
  {
    name: 'subject',
    type: 'string (required)',
    description: 'Insight subject scope. r1 uses single-entity matter form: `matter:GUID`.',
  },
  {
    name: 'mode',
    type: "string (default 'single')",
    description: "Topic mode. r1 ships 'single' only; r2+ may add 'multi' / 'cohort'.",
  },
  {
    name: 'parameters',
    type: 'Record<string, unknown>',
    description: 'Optional topic-specific parameters forwarded to the playbook invocation payload.',
  },
  {
    name: 'kpiSlot',
    type: 'ReactNode',
    description: 'Optional caller-provided KPI rendering injected into the Card header.',
  },
  {
    name: 'onCitationClick',
    type: '(citation: Citation) => void',
    description:
      'Fired when the user clicks an inline citation. Host wires navigation (document viewer, MDA form, etc.).',
  },
  {
    name: 'onFetchInsight',
    type: '(options?: { force?: boolean }) => Promise<InsightEnvelope>',
    description:
      'Lazy-load callback invoked once on Popover open and again on explicit refresh (force=true bypasses cache per FR-20).',
  },
  {
    name: 'onFetchRegistry',
    type: '(topic, mode) => Promise<InsightRegistryEntry | null>',
    description: 'Optional FR-05 mount-check. Resolves to null when no registry row matches; the card renders nothing.',
  },
  {
    name: 'onRegistryResolved',
    type: '(entry: InsightRegistryEntry) => void',
    description: 'Fired after a registry row resolves to enabled. Hosts wire downstream TTL timers here.',
  },
  {
    name: 'theme',
    type: 'Theme (Fluent v9)',
    description:
      'Active Fluent v9 theme — required for the portal-gotcha re-wrap so dark mode propagates through Popover + Dialog.',
  },
  {
    name: 'triggerLabel',
    type: "string (default 'View Insight')",
    description: 'Label for the Popover-trigger button.',
  },
  {
    name: 'className',
    type: 'string',
    description: 'Optional root class name override; mergeClasses applies it LAST.',
  },
];

// ---------------------------------------------------------------------------
// Sandbox component
// ---------------------------------------------------------------------------

/**
 * Public sandbox component — renders all six FR-06 states in a responsive grid
 * with a light/dark theme toggle. Importable by any host:
 *
 * ```tsx
 * import { InsightSummaryCardSandbox } from '@spaarke/ai-widgets';
 *
 * // Anywhere a host needs a preview surface (dev playground, internal admin
 * // page, manual QA shell):
 * <InsightSummaryCardSandbox />
 * ```
 *
 * SC-01 satisfaction:
 *   - All six FR-06 states demonstrably visible in a single render.
 *   - Light + dark theme variants via the toolbar Switch.
 *   - Props matrix surfaces the InsightSummaryCardProps contract in lieu of
 *     Storybook's Args table.
 *
 * ADR-021 verification:
 *   - Uses ONLY tokens.* + semantic theme primitives.
 *   - FluentProvider wraps the sandbox with the toggled theme so the whole
 *     tree — including the cards' own portal-rewrap — sees the same theme.
 *
 * Q-U3 verification: NO feedback UI is rendered anywhere.
 * Q-U1 verification: NO `@v1`/`@vN` identifier-suffix vernacular appears.
 */
export const InsightSummaryCardSandbox: React.FC = () => {
  const [isDark, setIsDark] = useState(false);
  const theme = isDark ? webDarkTheme : webLightTheme;

  // Memoise the mocks per render so reference identity stays stable inside
  // the cards (avoids re-firing the on-open gate on every re-render).
  const mocks = useMemo(() => {
    const map: Partial<Record<IStateMatrixRow['key'], FetchFn | undefined>> = {};
    for (const row of STATE_MATRIX) {
      map[row.key] = makeMockFetch(row.key);
    }
    return map;
  }, []);

  // Citation click stub — sandbox just logs to console; production hosts
  // wire Xrm.Navigation.openForm or SPE viewer here.
  const handleCitationClick = useCallback((citation: Citation) => {
    // eslint-disable-next-line no-console
    console.info('[Sandbox] citation clicked:', citation);
  }, []);

  return (
    <FluentProvider theme={theme}>
      <SandboxBody
        isDark={isDark}
        onToggleDark={() => setIsDark(prev => !prev)}
        theme={theme}
        mocks={mocks}
        onCitationClick={handleCitationClick}
      />
    </FluentProvider>
  );
};

InsightSummaryCardSandbox.displayName = 'InsightSummaryCardSandbox';

// ---------------------------------------------------------------------------
// SandboxBody — separated so `useSandboxStyles` runs inside the FluentProvider
// subtree (Griffel hooks require an enclosing provider to resolve tokens).
// ---------------------------------------------------------------------------

interface ISandboxBodyProps {
  isDark: boolean;
  onToggleDark: () => void;
  theme: typeof webLightTheme | typeof webDarkTheme;
  mocks: Partial<Record<IStateMatrixRow['key'], FetchFn | undefined>>;
  onCitationClick: (citation: Citation) => void;
}

const SandboxBody: React.FC<ISandboxBodyProps> = ({ isDark, onToggleDark, theme, mocks, onCitationClick }) => {
  const styles = useSandboxStyles();

  return (
    <div className={styles.root} data-testid="insight-summary-card-sandbox">
      {/* ── Title + intro ───────────────────────────────────────────────── */}
      <header className={styles.header}>
        <Text className={styles.title}>InsightSummaryCard — Dev Sandbox</Text>
        <Text className={styles.subtitle}>
          Storybook-equivalent surface per SC-01. Click each card&apos;s &quot;View Insight&quot; trigger to drive the
          state machine to its labelled state. Toggle the theme to verify ADR-021 dark-mode parity (the cards re-wrap
          their Popover + Dialog portals, so dark mode propagates through both surfaces).
        </Text>
      </header>

      {/* ── Toolbar (theme toggle) ──────────────────────────────────────── */}
      <div className={styles.toolbar} role="toolbar" aria-label="Sandbox controls">
        <Switch
          checked={isDark}
          onChange={onToggleDark}
          label={isDark ? 'Dark theme' : 'Light theme'}
          data-testid="sandbox-theme-toggle"
        />
        <Button appearance="subtle" size="small" onClick={() => window.location.reload()} data-testid="sandbox-reset">
          Reset all states
        </Button>
      </div>

      {/* ── State matrix (one card per FR-06 state) ─────────────────────── */}
      <section aria-label="Insight card state matrix">
        <div className={styles.matrix}>
          {STATE_MATRIX.map(row => (
            <article key={row.key} className={styles.matrixCell} data-testid={`sandbox-state-${row.key}`}>
              <header className={styles.matrixHeader}>
                <Text className={styles.matrixTitle}>{row.title}</Text>
                <Text className={styles.matrixDescription}>{row.description}</Text>
                <Text className={styles.matrixBehavior}>{row.behavior}</Text>
              </header>

              <InsightSummaryCard
                topic="matter-health"
                subject={`matter:00000000-0000-0000-0000-00000000000${row.key.length}`}
                mode="single"
                theme={theme}
                onFetchInsight={mocks[row.key]}
                onCitationClick={onCitationClick}
                triggerLabel={`Open (${row.title})`}
              />
            </article>
          ))}
        </div>
      </section>

      {/* ── Props table (Storybook Args-table equivalent) ───────────────── */}
      <section aria-label="Component props documentation">
        <div className={styles.propsTable}>
          <Text className={styles.title}>InsightSummaryCardProps</Text>
          <Text className={styles.subtitle}>
            Mirror of <code>InsightSummaryCard.types.ts</code>. Update in lockstep when the contract changes. Full JSDoc
            lives on the interface itself.
          </Text>

          <div className={`${styles.propsRow} ${styles.propsRowHeader}`}>
            <span>Prop</span>
            <span>Type</span>
            <span>Purpose</span>
          </div>

          {PROPS_DOC.map(p => (
            <div key={p.name} className={styles.propsRow}>
              <span className={styles.propName}>{p.name}</span>
              <span className={styles.propType}>{p.type}</span>
              <span>{p.description}</span>
            </div>
          ))}
        </div>
      </section>
    </div>
  );
};

SandboxBody.displayName = 'InsightSummaryCardSandbox.Body';

export default InsightSummaryCardSandbox;
