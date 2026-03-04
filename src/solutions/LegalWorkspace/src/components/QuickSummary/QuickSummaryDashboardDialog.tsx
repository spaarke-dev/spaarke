import * as React from "react";
import {
  Dialog,
  DialogSurface,
  DialogTitle,
  DialogBody,
  DialogActions,
  Button,
  Text,
  makeStyles,
  tokens,
} from "@fluentui/react-components";
import { DismissRegular } from "@fluentui/react-icons";

export interface IQuickSummaryDashboardDialogProps {
  open: boolean;
  onClose: () => void;
}

const useStyles = makeStyles({
  surface: {
    maxWidth: "720px",
  },
  body: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    justifyContent: "center",
    gap: tokens.spacingVerticalL,
    paddingTop: tokens.spacingVerticalXXL,
    paddingBottom: tokens.spacingVerticalXXL,
  },
});

export const QuickSummaryDashboardDialog: React.FC<IQuickSummaryDashboardDialogProps> = ({
  open,
  onClose,
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
          Quick Summary Dashboard
        </DialogTitle>

        <DialogBody>
          <div className={styles.body}>
            <Text size={500} weight="semibold">
              Coming Soon
            </Text>
            <Text size={300} style={{ color: tokens.colorNeutralForeground3, textAlign: "center" }}>
              The Quick Summary Dashboard will provide detailed analytics and insights across your matters, projects, assignments, and tasks.
            </Text>
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

QuickSummaryDashboardDialog.displayName = "QuickSummaryDashboardDialog";
