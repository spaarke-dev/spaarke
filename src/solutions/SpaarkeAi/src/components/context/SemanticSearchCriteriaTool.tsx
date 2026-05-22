/**
 * SemanticSearchCriteriaTool.tsx — In-pane search criteria editor for the
 * SpaarkeAi Context pane (Task 095).
 *
 * Surfaced when the user selects "Semantic Search" from the Context pane's
 * Tools dropdown (ContextPaneMenu). Provides a simplified Search Criteria
 * editor that fits the narrow Context pane:
 *
 *   1. Domain selector (one Dropdown — documents / matters / projects / invoices).
 *      The full SemanticSearch Code Page uses tabs; this in-pane tool uses a
 *      Dropdown so it fits a ~360px-wide pane.
 *   2. AI Search query textarea.
 *   3. Optional date range (from / to date inputs).
 *   4. Search primary button → launches sprk_semanticsearch via
 *      Xrm.Navigation.navigateTo with the criteria as URL params.
 *
 * Persistence:
 *   Transient criteria state (query / domain / dateFrom / dateTo) persists in
 *   localStorage under `spaarke:context:semantic-search-criteria`. This means:
 *     - The criteria survives modal open/close (operator flow: type a query,
 *       click Search, results modal opens, user closes modal — criteria are
 *       still in the pane).
 *     - The criteria survives tool switches (Quick Start → Semantic Search →
 *       Quick Start → Semantic Search shows what was last typed).
 *     - The criteria survives browser restarts (localStorage, not sessionStorage).
 *
 * Modal launch shape:
 *   Mirrors the wizard launchers in @spaarke/ui-components/WorkspaceShell/
 *   wizardLaunchers.ts — same Xrm frame-walk, same pageType:"webresource",
 *   same target:2, same 80% × 80% modal (wider than wizards because the
 *   full SemanticSearch page has its own SearchFilterPane + map/grid views).
 *
 *   data string carries:
 *     query=<encoded>&domain=<documents|matters|projects|invoices>
 *     &dateFrom=<YYYY-MM-DD>&dateTo=<YYYY-MM-DD>
 *
 *   The SemanticSearch Code Page already parses `query` + `domain` from its
 *   data envelope (see src/client/code-pages/SemanticSearch/src/index.tsx).
 *   dateFrom / dateTo are added by this tool — the full page will receive
 *   them and can optionally seed its DateRangeFilter; missing handling on
 *   the page side is fine (they're ignored and the user can set them in
 *   the full filter pane).
 *
 * Bug-fix invariant (matches the playbook-modal pane-blank fix in Task 095):
 *   When the user clicks Search, this component does NOT change the
 *   ContextPaneController's selectedTool. The localStorage-backed selectedTool
 *   means the Context pane re-renders with this tool still selected after the
 *   modal closes — that is the uniform fix for the "pane goes blank after a
 *   modal closes" bug.
 *
 * Standards:
 *   - ADR-012: SpaarkeAi-local — the criteria are an in-pane subset of the
 *     full SemanticSearch experience (which lives in src/client/code-pages/).
 *   - ADR-021: Fluent v9 tokens only — no hex / rgba literals.
 *   - ADR-022: React 19, functional component.
 *   - ADR-025: Icons from `@fluentui/react-icons` v9.
 *   - ADR-028: No auth surface change — criteria stay client-side until the
 *     modal launch hands off to the SemanticSearch Code Page, which has its
 *     own MSAL bootstrap.
 *
 * @see ContextPaneController — parent
 * @see ContextPaneMenu — surface that toggles this tool's visibility
 * @see wizardLaunchers — proven Xrm frame-walk + navigateTo pattern
 */

import * as React from 'react';
import {
  makeStyles,
  shorthands,
  tokens,
  Button,
  Dropdown,
  Option,
  Input,
  Label,
  Textarea,
  Text,
} from '@fluentui/react-components';
import { SearchRegular } from '@fluentui/react-icons';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

/**
 * Mirrors `SearchDomain` from the full SemanticSearch Code Page
 * (src/client/code-pages/SemanticSearch/src/types/index.ts). Replicated as a
 * literal union here so this file does NOT cross the package boundary (the
 * SemanticSearch types live in a separate code-page that isn't a published
 * npm package consumed by SpaarkeAi).
 */
type SearchDomain = 'documents' | 'matters' | 'projects' | 'invoices';

interface DomainDescriptor {
  id: SearchDomain;
  label: string;
}

const DOMAINS: readonly DomainDescriptor[] = [
  { id: 'documents', label: 'Documents' },
  { id: 'matters', label: 'Matters' },
  { id: 'projects', label: 'Projects' },
  { id: 'invoices', label: 'Invoices' },
];

const VALID_DOMAINS: ReadonlySet<string> = new Set<SearchDomain>([
  'documents',
  'matters',
  'projects',
  'invoices',
]);

/** Shape persisted in localStorage. */
interface PersistedCriteria {
  query: string;
  domain: SearchDomain;
  dateFrom: string;
  dateTo: string;
}

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const STORAGE_KEY = 'spaarke:context:semantic-search-criteria';

const DEFAULT_CRITERIA: PersistedCriteria = {
  query: '',
  domain: 'documents',
  dateFrom: '',
  dateTo: '',
};

const SEMANTIC_SEARCH_WEBRESOURCE = 'sprk_semanticsearch';
const SEMANTIC_SEARCH_TITLE = 'Semantic Search Results';

// ---------------------------------------------------------------------------
// localStorage helpers — try/catch-wrapped for private browsing / quota
// ---------------------------------------------------------------------------

function readPersistedCriteria(): PersistedCriteria {
  if (typeof window === 'undefined') return { ...DEFAULT_CRITERIA };
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY);
    if (raw === null) return { ...DEFAULT_CRITERIA };
    const parsed = JSON.parse(raw) as unknown;
    if (typeof parsed !== 'object' || parsed === null) {
      return { ...DEFAULT_CRITERIA };
    }
    const obj = parsed as Record<string, unknown>;
    return {
      query: typeof obj.query === 'string' ? obj.query : DEFAULT_CRITERIA.query,
      domain:
        typeof obj.domain === 'string' && VALID_DOMAINS.has(obj.domain)
          ? (obj.domain as SearchDomain)
          : DEFAULT_CRITERIA.domain,
      dateFrom:
        typeof obj.dateFrom === 'string' ? obj.dateFrom : DEFAULT_CRITERIA.dateFrom,
      dateTo:
        typeof obj.dateTo === 'string' ? obj.dateTo : DEFAULT_CRITERIA.dateTo,
    };
  } catch {
    return { ...DEFAULT_CRITERIA };
  }
}

function writePersistedCriteria(criteria: PersistedCriteria): void {
  if (typeof window === 'undefined') return;
  try {
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify(criteria));
  } catch {
    // Best-effort persistence; silent on quota / private-mode failures.
  }
}

// ---------------------------------------------------------------------------
// Xrm frame-walk — same shape as wizardLaunchers.ts (proven across iframes)
// ---------------------------------------------------------------------------

/* eslint-disable @typescript-eslint/no-explicit-any */

function resolveXrmNavigation(): { navigateTo: (...args: unknown[]) => Promise<unknown> } | null {
  if (typeof window === 'undefined') return null;
  try {
    const w = window as any;
    const xrm = w?.Xrm ?? w?.parent?.Xrm ?? w?.top?.Xrm ?? null;
    if (!xrm?.Navigation?.navigateTo) return null;
    return xrm.Navigation;
  } catch {
    return null;
  }
}

/* eslint-enable @typescript-eslint/no-explicit-any */

// ---------------------------------------------------------------------------
// Modal launch — Xrm.Navigation.navigateTo to sprk_semanticsearch
// ---------------------------------------------------------------------------

function buildSearchDataParams(criteria: PersistedCriteria): string {
  const parts: string[] = [];
  if (criteria.query) parts.push(`query=${encodeURIComponent(criteria.query)}`);
  if (criteria.domain) parts.push(`domain=${encodeURIComponent(criteria.domain)}`);
  if (criteria.dateFrom) parts.push(`dateFrom=${encodeURIComponent(criteria.dateFrom)}`);
  if (criteria.dateTo) parts.push(`dateTo=${encodeURIComponent(criteria.dateTo)}`);
  return parts.join('&');
}

/**
 * Fire-and-forget Semantic Search modal launch. Catches user-cancel + dialog
 * error per the wizardLaunchers.ts precedent. Console-warns when running
 * outside the Dataverse host (Vite dev, jsdom) — same posture as
 * WorkspacePaneMenu.launchWizard.
 *
 * NOTE: We deliberately do NOT await this. The Promise that
 * navigateTo returns resolves when the modal closes — and our pane behavior
 * is identical whether we await it or not (the Context pane already shows
 * the right criteria because selectedTool is persisted in localStorage at the
 * controller level).
 */
function launchSemanticSearch(criteria: PersistedCriteria): void {
  const nav = resolveXrmNavigation();
  if (nav === null) {
    console.warn(
      '[SemanticSearchCriteriaTool] Xrm.Navigation.navigateTo not available — running outside Dataverse host. Search launch is a no-op.',
    );
    return;
  }
  const data = buildSearchDataParams(criteria);
  try {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (nav.navigateTo as any)(
      {
        pageType: 'webresource',
        webresourceName: SEMANTIC_SEARCH_WEBRESOURCE,
        data,
      },
      {
        target: 2,
        width: { value: 80, unit: '%' },
        height: { value: 80, unit: '%' },
        title: SEMANTIC_SEARCH_TITLE,
      },
    ).catch?.((err: unknown) => {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      const code = (err as any)?.errorCode;
      if (code !== 2) {
        console.warn('[SemanticSearchCriteriaTool] navigateTo error:', err);
      }
    });
  } catch (err) {
    console.warn('[SemanticSearchCriteriaTool] navigateTo threw synchronously:', err);
  }
}

// ---------------------------------------------------------------------------
// Styles — Fluent v9 tokens only (ADR-021)
// ---------------------------------------------------------------------------

const useStyles = makeStyles({
  root: {
    flex: 1,
    display: 'flex',
    flexDirection: 'column',
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
    paddingLeft: tokens.spacingHorizontalM,
    paddingRight: tokens.spacingHorizontalM,
    gap: tokens.spacingVerticalM,
    overflowY: 'auto',
    backgroundColor: tokens.colorNeutralBackground1,
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    margin: tokens.spacingHorizontalS,
    ...shorthands.border('1px', 'solid', tokens.colorNeutralStroke2),
  },
  header: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  headerTitle: {
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground1,
  },
  headerSubtitle: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
  },
  field: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  fieldLabel: {
    fontWeight: tokens.fontWeightSemibold,
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground2,
  },
  dropdown: {
    width: '100%',
    minWidth: 'auto',
  },
  textarea: {
    width: '100%',
    minHeight: '72px',
  },
  dateRow: {
    display: 'flex',
    flexDirection: 'row',
    gap: tokens.spacingHorizontalS,
  },
  dateField: {
    flex: 1,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    minWidth: 0,
  },
  dateFieldLabel: {
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
  },
  dateInput: {
    width: '100%',
  },
  searchButtonRow: {
    display: 'flex',
    justifyContent: 'flex-end',
    marginTop: tokens.spacingVerticalS,
  },
});

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------

/**
 * SemanticSearchCriteriaTool — In-pane criteria editor + Search button.
 *
 * State is local React + localStorage persistence (no PaneEventBus side
 * effects — the criteria are purely a user-input surface; nothing else in
 * the SpaarkeAi shell needs to subscribe to changes here).
 */
export const SemanticSearchCriteriaTool: React.FC = () => {
  const styles = useStyles();

  // Hydrate from localStorage on mount (read once via lazy initializer).
  const [criteria, setCriteria] = React.useState<PersistedCriteria>(() =>
    readPersistedCriteria(),
  );

  // Mirror every change back to localStorage so a modal close (or tool switch)
  // restores the last-typed criteria.
  const updateCriteria = React.useCallback(
    (patch: Partial<PersistedCriteria>) => {
      setCriteria((prev) => {
        const next = { ...prev, ...patch };
        writePersistedCriteria(next);
        return next;
      });
    },
    [],
  );

  const handleDomainChange = React.useCallback(
    (_e: unknown, data: { optionValue?: string }) => {
      const value = data.optionValue;
      if (typeof value === 'string' && VALID_DOMAINS.has(value)) {
        updateCriteria({ domain: value as SearchDomain });
      }
    },
    [updateCriteria],
  );

  const handleQueryChange = React.useCallback(
    (_e: unknown, data: { value: string }) => {
      updateCriteria({ query: data.value });
    },
    [updateCriteria],
  );

  const handleDateFromChange = React.useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      updateCriteria({ dateFrom: e.target.value });
    },
    [updateCriteria],
  );

  const handleDateToChange = React.useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      updateCriteria({ dateTo: e.target.value });
    },
    [updateCriteria],
  );

  const handleSearchClick = React.useCallback(() => {
    launchSemanticSearch(criteria);
  }, [criteria]);

  const activeDomainLabel =
    DOMAINS.find((d) => d.id === criteria.domain)?.label ?? 'Documents';

  return (
    <div className={styles.root} data-testid="semantic-search-criteria-tool">
      <div className={styles.header}>
        <Text className={styles.headerTitle} size={400}>
          Search Criteria
        </Text>
        <Text className={styles.headerSubtitle} size={200}>
          Refine your query, then open results in a full Semantic Search view.
        </Text>
      </div>

      {/* Domain ------------------------------------------------------------ */}
      <div className={styles.field}>
        <Label className={styles.fieldLabel} htmlFor="ss-criteria-domain">
          Domain
        </Label>
        <Dropdown
          id="ss-criteria-domain"
          className={styles.dropdown}
          value={activeDomainLabel}
          selectedOptions={[criteria.domain]}
          onOptionSelect={handleDomainChange}
          data-testid="semantic-search-criteria-domain"
        >
          {DOMAINS.map((d) => (
            <Option key={d.id} value={d.id} text={d.label}>
              {d.label}
            </Option>
          ))}
        </Dropdown>
      </div>

      {/* AI query --------------------------------------------------------- */}
      <div className={styles.field}>
        <Label className={styles.fieldLabel} htmlFor="ss-criteria-query">
          AI Search query
        </Label>
        <Textarea
          id="ss-criteria-query"
          className={styles.textarea}
          value={criteria.query}
          onChange={handleQueryChange}
          placeholder="e.g. contracts about indemnification"
          resize="vertical"
          data-testid="semantic-search-criteria-query"
        />
      </div>

      {/* Date range ------------------------------------------------------- */}
      <div className={styles.field}>
        <Label className={styles.fieldLabel}>Date range (optional)</Label>
        <div className={styles.dateRow}>
          <div className={styles.dateField}>
            <Label
              className={styles.dateFieldLabel}
              htmlFor="ss-criteria-date-from"
            >
              From
            </Label>
            <Input
              id="ss-criteria-date-from"
              className={styles.dateInput}
              type="date"
              value={criteria.dateFrom}
              onChange={handleDateFromChange}
              data-testid="semantic-search-criteria-date-from"
            />
          </div>
          <div className={styles.dateField}>
            <Label
              className={styles.dateFieldLabel}
              htmlFor="ss-criteria-date-to"
            >
              To
            </Label>
            <Input
              id="ss-criteria-date-to"
              className={styles.dateInput}
              type="date"
              value={criteria.dateTo}
              onChange={handleDateToChange}
              data-testid="semantic-search-criteria-date-to"
            />
          </div>
        </div>
      </div>

      {/* Search button --------------------------------------------------- */}
      <div className={styles.searchButtonRow}>
        <Button
          appearance="primary"
          icon={<SearchRegular />}
          onClick={handleSearchClick}
          data-testid="semantic-search-criteria-submit"
        >
          Search
        </Button>
      </div>
    </div>
  );
};

SemanticSearchCriteriaTool.displayName = 'SemanticSearchCriteriaTool';
