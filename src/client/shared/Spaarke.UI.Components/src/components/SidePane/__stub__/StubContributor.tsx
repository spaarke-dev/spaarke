/**
 * StubContributor — a throwaway `sidePaneRegistry` contributor that exists
 * SOLELY to prove FR-13 / Success Criterion 11 (spaarke-side-pane-navigation-
 * history-r1 task 085): a second contributor extends `SprkSidePaneHost` by
 * REGISTRATION ONLY, with zero host-code changes.
 *
 * This module has NO side effects at import time — it does NOT call
 * `registerSidePaneContributor()` itself. `NavigatorPane/src/main.tsx` (the
 * one production bundle that imports from `@spaarke/ui-components`) never
 * imports this file, so the stub cannot leak a stray rail icon into the
 * shipping Navigator pane. Only `stubContributor.test.tsx` imports
 * {@link STUB_SIDE_PANE_REGISTRY_ENTRY} and registers/unregisters it around
 * each test (see that file's `beforeEach`/`afterEach`).
 *
 * The registry entry below supplies exactly the fields the task requires —
 * { id, icon, title, component } — plus the one field every entry MUST carry
 * per `SidePaneRegistryEntry` (`order`, the rail sort key; NavigatorPane's own
 * registration in `main.tsx` carries the same field). No other registry
 * surface, no privileged hooks.
 *
 * @see ../sidePaneRegistry.ts (the registration contract being proven)
 * @see ../SprkSidePaneHost.tsx (host — NOT modified by this proof)
 * @see ../__tests__/stubContributor.test.tsx (the render proof)
 */

import * as React from 'react';
import { BeakerRegular } from '@fluentui/react-icons';
import { Text, makeStyles, shorthands, tokens } from '@fluentui/react-components';

import type { SidePaneContributorProps, SidePaneRegistryEntry } from '../sidePaneRegistry';

/** Unique id for this throwaway proof contributor. */
export const STUB_CONTRIBUTOR_ID = 'fr13-stub-proof';

/** Identifiable text asserted by the render test. */
export const STUB_CONTRIBUTOR_BODY_TEXT = 'FR-13 stub contributor — registration-only proof';

const useStyles = makeStyles({
  root: {
    ...shorthands.padding(tokens.spacingVerticalM, tokens.spacingHorizontalM),
    color: tokens.colorNeutralForeground1,
  },
});

/**
 * Minimal, trivially-identifiable body. Fluent v9 tokens only (ADR-021) —
 * no hardcoded colors. React-16/17-safe (ADR-022): plain function component,
 * `JSX.Element`-free return typing via inference (no `JSX.Element` usage
 * anywhere in this file), no `createRoot`.
 */
export const StubContributor: React.FC<SidePaneContributorProps> = ({ paneId }) => {
  const styles = useStyles();
  return (
    <div className={styles.root} data-testid="fr13-stub-contributor-body">
      <Text>
        {STUB_CONTRIBUTOR_BODY_TEXT} (pane: {paneId})
      </Text>
    </div>
  );
};

StubContributor.displayName = 'StubContributor';

/**
 * The registry entry descriptor — supplied to `registerSidePaneContributor`
 * / `replaceSidePaneContributor` by the TEST only (never at module load).
 * `order` is intentionally a large number so the stub never displaces a real
 * contributor's default-active position if a test happens to register both.
 */
export const STUB_SIDE_PANE_REGISTRY_ENTRY: SidePaneRegistryEntry = {
  id: STUB_CONTRIBUTOR_ID,
  icon: <BeakerRegular />,
  title: 'FR-13 Stub',
  order: 999,
  component: async () => ({ default: StubContributor }),
};
