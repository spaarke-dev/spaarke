/**
 * ViewsTab — Views tab content
 * (spaarke-side-pane-navigation-history-r1 task 060, spec FR-10 UI).
 *
 * Reuses `ViewService.getAllUserQueries()` to list the signed-in user's
 * personal `userquery` views across ALL entities, grouped by
 * `returnedtypecode` (entity) for display. System `savedquery` views are
 * NEVER fetched by this tab — the default listing is userquery only
 * (opt-in / pin-only per FR-10; a future pin-a-view affordance is out of
 * this task's scope).
 *
 * ViewService contract note (reuse-vs-extend decision, task 060): every
 * existing ViewService public method (`getViews`, `getDefaultView`,
 * `getViewById`) requires an `entityLogicalName` up front — they answer
 * "views FOR entity X", not "every view this user owns across every
 * entity", which is what FR-10's cross-entity grouped listing needs. Per
 * root CLAUDE.md §11 (extend over fork) this task added ONE additive public
 * method, `ViewService.getAllUserQueries()`, that queries `userquery`
 * without an entity filter and reuses the SAME record→IViewDefinition
 * mapping shape as the existing (now-shared) private mapper. No existing
 * method's signature changed; no parallel view-querying service was
 * introduced — the MODIFY-ONLY/reuse constraint (NFR-08) is honored via
 * extension, not a fork. See
 * `projects/spaarke-side-pane-navigation-history-r1/notes/060-viewservice-extension.md`.
 *
 * Click behavior: `Xrm.Navigation.navigateTo({pageType:'entitylist',
 * entityName, viewId})` opens the entity grid with that view selected. The
 * `viewId` field required widening `PageInput` in `xrmContext.ts` (additive
 * optional field — same pattern as task 010's `webresourceName` widen);
 * mirrors the `pageType:'entitylist', entityName, viewId` shape already used
 * by `LegalWorkspace/WorkspaceGrid.tsx` (there via an untyped `any` Xrm
 * reference — this tab uses the canonical typed `getXrm()` path instead,
 * consistent with RecentTab.tsx/PinnedTab.tsx).
 *
 * Host-context only (project constraint): all data access goes through
 * ViewService, which itself only calls `Xrm.WebApi` under the signed-in
 * user. No BFF, no plugin, no per-form web resource.
 *
 * ADR-021: Fluent v9 tokens only; no portal-rendered component here (no
 * Popover/Tooltip/Dialog/Menu) so the fluent-v9-portal-gotcha re-wrap does
 * not apply — light/dark flow through the ambient `FluentProvider`
 * `NavigatorBody` resolves, mirroring RecentTab.tsx/PinnedTab.tsx's own note.
 * ADR-022: this Code-Page file only uses React-16/17-safe APIs (no
 * `createRoot`). The shared-lib touches this task made (`ViewService.ts`,
 * `xrmContext.ts`) are pure TypeScript with no React import, so the
 * React-version-safety constraint is inherently satisfied.
 *
 * @see ../../../client/shared/Spaarke.UI.Components/src/services/ViewService.ts
 * @see NavigatorBody.tsx (task 040/060) — mounts this component in the `views` tab panel
 * @see RecentTab.tsx / PinnedTab.tsx (task 041/050) — row/empty-state pattern this file mirrors locally
 */

import * as React from 'react';
import { Caption1, Spinner, Text, makeStyles, shorthands, tokens } from '@fluentui/react-components';
import { ViewService, getXrm, type IViewDefinition, type XrmContext } from '@spaarke/ui-components';

// ─────────────────────────────────────────────────────────────────────────────
// Entity label helper (local duplicate of RecentTab.tsx's `formatEntityLabel` —
// CLAUDE.md §11: extension is earned by a real second consumer of the SAME
// module, not a sibling render surface; RecentTab.tsx/PinnedTab.tsx already
// established this precedent for this exact 2-line helper).
// ─────────────────────────────────────────────────────────────────────────────

const KNOWN_ENTITY_LABELS: Record<string, string> = {
  sprk_matter: 'Matter',
  sprk_document: 'Document',
};

function formatEntityLabel(entityLogicalName: string): string {
  const known = KNOWN_ENTITY_LABELS[entityLogicalName];
  if (known) return known;
  const stripped = entityLogicalName.replace(/^[a-z]+_/, '');
  if (!stripped) return entityLogicalName;
  return stripped.charAt(0).toUpperCase() + stripped.slice(1);
}

interface EntityViewGroup {
  entityLogicalName: string;
  views: IViewDefinition[];
}

/**
 * Groups views by `entityLogicalName`, preserving first-seen order — which
 * is the server-sorted order (`$orderby=returnedtypecode,name`) coming out
 * of `ViewService.getAllUserQueries()`.
 */
function groupByEntity(views: IViewDefinition[]): EntityViewGroup[] {
  const order: string[] = [];
  const map = new Map<string, IViewDefinition[]>();
  for (const view of views) {
    const key = view.entityLogicalName;
    if (!map.has(key)) {
      map.set(key, []);
      order.push(key);
    }
    map.get(key)!.push(view);
  }
  return order.map(entityLogicalName => ({ entityLogicalName, views: map.get(entityLogicalName)! }));
}

// ─────────────────────────────────────────────────────────────────────────────
// Navigation
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Navigate to a view's entity list with that view selected. `viewId` is the
 * `userquery` id — Dataverse's `navigateTo` accepts a personal-view id the
 * same way it accepts a `savedquery` id. Takes `xrm` explicitly (rather than
 * re-resolving `getXrm()` internally) to match `navigateToRow`'s signature
 * in RecentTab.tsx/PinnedTab.tsx.
 */
function navigateToView(xrm: XrmContext, view: IViewDefinition): void {
  const navigation = xrm.Navigation;
  if (!navigation) return;
  void navigation.navigateTo({
    pageType: 'entitylist',
    entityName: view.entityLogicalName,
    viewId: view.id,
  });
}

// ─────────────────────────────────────────────────────────────────────────────
// Styles
// ─────────────────────────────────────────────────────────────────────────────

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    ...shorthands.gap(tokens.spacingVerticalM),
  },
  group: {
    display: 'flex',
    flexDirection: 'column',
    ...shorthands.gap(tokens.spacingVerticalXS),
  },
  groupHeading: {
    color: tokens.colorNeutralForeground3,
    textTransform: 'uppercase',
    letterSpacing: '0.02em',
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
  },
  centeredState: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    height: '100%',
    minHeight: '80px',
    ...shorthands.padding(tokens.spacingVerticalL, tokens.spacingHorizontalL),
    color: tokens.colorNeutralForeground3,
    textAlign: 'center',
  },
  row: {
    display: 'flex',
    alignItems: 'center',
    ...shorthands.gap(tokens.spacingHorizontalS),
    ...shorthands.padding(tokens.spacingVerticalSNudge, tokens.spacingHorizontalS),
    ...shorthands.borderRadius(tokens.borderRadiusMedium),
    cursor: 'pointer',
    ':hover': {
      backgroundColor: tokens.colorNeutralBackground1Hover,
    },
    ':focus-visible': {
      ...shorthands.outline('2px', 'solid', tokens.colorStrokeFocus2),
    },
  },
  rowName: {
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    fontSize: tokens.fontSizeBase300,
    color: tokens.colorNeutralForeground1,
  },
});

// ─────────────────────────────────────────────────────────────────────────────
// Component
// ─────────────────────────────────────────────────────────────────────────────

type LoadStatus = 'loading' | 'ready' | 'error';

export const ViewsTab: React.FC = () => {
  const styles = useStyles();

  const [status, setStatus] = React.useState<LoadStatus>('loading');
  const [errorMessage, setErrorMessage] = React.useState<string | null>(null);
  const [views, setViews] = React.useState<IViewDefinition[]>([]);

  React.useEffect(() => {
    let cancelled = false;

    async function load(): Promise<void> {
      const xrm = getXrm();
      if (!xrm) {
        if (!cancelled) {
          setViews([]);
          setStatus('ready');
        }
        return;
      }

      setStatus('loading');
      setErrorMessage(null);

      try {
        const viewService = new ViewService(xrm);
        const userQueries = await viewService.getAllUserQueries();
        if (cancelled) return;
        setViews(userQueries);
        setStatus('ready');
      } catch (err) {
        // getAllUserQueries() itself never rejects (internally try/caught) —
        // this branch is defensive only, mirroring RecentTab.tsx's loadEdited.
        if (cancelled) return;
        setErrorMessage(err instanceof Error ? err.message : 'Failed to load views.');
        setStatus('error');
      }
    }

    void load();
    return () => {
      cancelled = true;
    };
  }, []);

  const handleViewClick = React.useCallback((view: IViewDefinition) => {
    const xrm = getXrm();
    if (!xrm) return;
    navigateToView(xrm, view);
  }, []);

  let content: React.ReactElement;
  if (status === 'loading') {
    content = (
      <div className={styles.centeredState} data-testid="views-tab-loading">
        <Spinner size="tiny" label="Loading views…" />
      </div>
    );
  } else if (status === 'error') {
    content = (
      <div className={styles.centeredState} data-testid="views-tab-error">
        <Caption1>{errorMessage}</Caption1>
      </div>
    );
  } else if (views.length === 0) {
    content = (
      <div className={styles.centeredState} data-testid="views-tab-empty">
        <Caption1>Your saved views will appear here.</Caption1>
      </div>
    );
  } else {
    const groups = groupByEntity(views);
    content = (
      <div data-testid="views-tab">
        {groups.map(group => {
          const entityLabel = formatEntityLabel(group.entityLogicalName);
          return (
            <section
              key={group.entityLogicalName}
              className={styles.group}
              aria-label={entityLabel}
              data-testid={`views-tab-group-${group.entityLogicalName}`}
            >
              <Text className={styles.groupHeading}>{entityLabel}</Text>
              <div role="list" aria-label={`${entityLabel} views`}>
                {group.views.map(view => (
                  <div
                    key={view.id}
                    className={styles.row}
                    role="listitem"
                    tabIndex={0}
                    data-testid={`views-tab-row-${view.id}`}
                    onClick={() => handleViewClick(view)}
                    onKeyDown={event => {
                      if (event.key === 'Enter' || event.key === ' ') {
                        event.preventDefault();
                        handleViewClick(view);
                      }
                    }}
                  >
                    <Text className={styles.rowName} title={view.name}>
                      {view.name}
                    </Text>
                  </div>
                ))}
              </div>
            </section>
          );
        })}
      </div>
    );
  }

  return (
    <div className={styles.container} data-testid="views-tab-container">
      {content}
    </div>
  );
};

export default ViewsTab;
