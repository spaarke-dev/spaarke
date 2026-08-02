import * as React from 'react';
import {
  Menu,
  MenuTrigger,
  MenuPopover,
  MenuList,
  MenuItemRadio,
  MenuGroupHeader,
  Button,
  Tooltip,
  makeStyles,
} from '@fluentui/react-components';
import { DesktopRegular } from '@fluentui/react-icons';
import { useUiScale } from '../../hooks/useUiScale';
import type { DisplaySizePreference } from '../../utils/themeStorage';

const useStyles = makeStyles({
  trigger: {
    minWidth: 'auto',
  },
});

const DISPLAY_SIZE_LABELS: Record<DisplaySizePreference, string> = {
  default: 'Default',
  large: 'Large',
  'extra-large': 'Extra-large',
};

const DISPLAY_SIZE_ORDER: readonly DisplaySizePreference[] = ['default', 'large', 'extra-large'];

/**
 * DisplaySizeMenu — the app-shell "Display size" affordance (spec FR-06 /
 * design §6.9 / P0.5). A compact Fluent v9 `Menu` (mirrors the established
 * `AssistantToolMenu` / `WorkspacePaneMenu` / `ContextPaneMenu` /
 * `DataGrid/ViewSelector` idiom — `Menu` + `MenuItemRadio` + `checkedValues`/
 * `onCheckedValueChange` — no parallel menu abstraction is introduced)
 * offering Default / Large / Extra-large. Persists via `useUiScale()` (the
 * existing code-page theme-storage pattern, extended — see `themeStorage.ts`).
 *
 * Self-contained — no required props. Mount once per app shell: SpaarkeAi's
 * `App.tsx` (no existing appearance/settings surface previously existed
 * there — this is the app-shell-level control) and LegalWorkspace's
 * `PageHeader` (standalone only, alongside the existing `ThemeToggle`;
 * embedded LegalWorkspace inherits the host's control — see
 * `LegalWorkspaceApp.tsx`).
 *
 * @example
 * ```tsx
 * import { DisplaySizeMenu } from "@spaarke/ui-components";
 * <DisplaySizeMenu />
 * ```
 */
export const DisplaySizeMenu: React.FC = () => {
  const { displaySize, setDisplaySize } = useUiScale();
  const styles = useStyles();

  const checkedValues = React.useMemo(() => ({ displaySize: [displaySize] }), [displaySize]);

  const handleCheckedValueChange = React.useCallback(
    (_ev: unknown, data: { name: string; checkedItems: string[] }) => {
      if (data.name !== 'displaySize') return;
      const next = data.checkedItems[0] as DisplaySizePreference | undefined;
      if (next) setDisplaySize(next);
    },
    [setDisplaySize]
  );

  return (
    <Menu checkedValues={checkedValues} onCheckedValueChange={handleCheckedValueChange} positioning="below-end">
      <MenuTrigger disableButtonEnhancement>
        <Tooltip content={`Display size: ${DISPLAY_SIZE_LABELS[displaySize]}`} relationship="label">
          <Button
            className={styles.trigger}
            appearance="subtle"
            size="small"
            icon={<DesktopRegular />}
            aria-label={`Display size: ${DISPLAY_SIZE_LABELS[displaySize]}. Click to change.`}
            data-testid="display-size-menu-trigger"
          />
        </Tooltip>
      </MenuTrigger>
      <MenuPopover data-testid="display-size-menu-popover">
        <MenuList>
          <MenuGroupHeader>Display size</MenuGroupHeader>
          {DISPLAY_SIZE_ORDER.map((size) => (
            <MenuItemRadio key={size} name="displaySize" value={size} data-testid={`display-size-${size}`}>
              {DISPLAY_SIZE_LABELS[size]}
            </MenuItemRadio>
          ))}
        </MenuList>
      </MenuPopover>
    </Menu>
  );
};

DisplaySizeMenu.displayName = 'DisplaySizeMenu';

export default DisplaySizeMenu;
