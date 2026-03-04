import * as React from "react";
import {
  Dialog,
  DialogSurface,
  DialogTitle,
  DialogBody,
  DialogActions,
  Button,
  makeStyles,
  tokens,
} from "@fluentui/react-components";
import { DismissRegular } from "@fluentui/react-icons";
import { ActionCard } from "./ActionCard";
import { ACTION_CARD_CONFIGS, IActionCardConfig } from "./getStartedConfig";

export interface IGetStartedExpandDialogProps {
  /** Whether the dialog is open. */
  open: boolean;
  /** Called when the dialog should close (dismiss button or overlay click). */
  onClose: () => void;
  /**
   * Map of card id to click handler.
   * Keys must match the `id` field in ACTION_CARD_CONFIGS.
   */
  onCardClick?: Partial<Record<string, () => void>>;
  /** Set of card ids that should be rendered in a disabled state. */
  disabledCards?: ReadonlySet<string>;
}

const useStyles = makeStyles({
  surface: {
    maxWidth: "680px",
  },
  titleRow: {
    display: "flex",
    flexDirection: "row",
    alignItems: "center",
    justifyContent: "space-between",
  },
  grid: {
    display: "flex",
    flexWrap: "wrap",
    gap: tokens.spacingHorizontalL,
    paddingTop: tokens.spacingVerticalM,
    paddingBottom: tokens.spacingVerticalM,
  },
});

/**
 * GetStartedExpandDialog -- displays all 7 action cards in a wrapping
 * flex grid inside a Fluent v9 Dialog.
 *
 * Opened when the user clicks the "more" expand icon in GetStartedRow
 * (when maxVisible limits the visible card count).
 */
export const GetStartedExpandDialog: React.FC<IGetStartedExpandDialogProps> = ({
  open,
  onClose,
  onCardClick = {},
  disabledCards = new Set<string>(),
}) => {
  const styles = useStyles();

  return (
    <Dialog open={open} onOpenChange={(_e, data) => { if (!data.open) onClose(); }}>
      <DialogSurface className={styles.surface}>
        <DialogTitle
          action={
            <Button
              appearance="subtle"
              aria-label="Close"
              icon={<DismissRegular />}
              onClick={onClose}
            />
          }
        >
          Get Started
        </DialogTitle>

        <DialogBody>
          <div className={styles.grid} role="group" aria-label="All quick actions">
            {ACTION_CARD_CONFIGS.map((config: IActionCardConfig) => (
              <ActionCard
                key={config.id}
                icon={config.icon}
                label={config.label}
                ariaLabel={config.ariaLabel}
                onClick={onCardClick[config.id]}
                disabled={disabledCards.has(config.id)}
              />
            ))}
          </div>
        </DialogBody>

        <DialogActions>
          <Button appearance="secondary" onClick={onClose}>
            Close
          </Button>
        </DialogActions>
      </DialogSurface>
    </Dialog>
  );
};

GetStartedExpandDialog.displayName = "GetStartedExpandDialog";
