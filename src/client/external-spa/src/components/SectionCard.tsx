import * as React from "react";
import {
  Card,
  CardHeader,
  CardFooter,
  makeStyles,
  tokens,
  Text,
} from "@fluentui/react-components";

const useStyles = makeStyles({
  card: {
    backgroundColor: tokens.colorNeutralBackground2,
    borderColor: tokens.colorNeutralStroke2,
    borderWidth: "1px",
    borderStyle: "solid",
    borderRadius: tokens.borderRadiusMedium,
    width: "100%",
    boxSizing: "border-box",
  },
  body: {
    padding: `${tokens.spacingVerticalM} ${tokens.spacingHorizontalL}`,
    paddingTop: "0",
  },
});

interface SectionCardProps {
  /** Card section title */
  title: string;
  /** Card body content */
  children: React.ReactNode;
  /** Optional actions rendered in the card footer (e.g. buttons) */
  actions?: React.ReactNode;
}

/**
 * SectionCard — reusable card component for grouping related content.
 *
 * Uses Fluent UI v9 Card with CardHeader and optional CardFooter for actions.
 * Background color uses `tokens.colorNeutralBackground2` to create subtle
 * visual separation from the page background — no hard-coded colors (ADR-021).
 *
 * Supports dark mode automatically via Fluent design tokens.
 */
export const SectionCard: React.FC<SectionCardProps> = ({ title, children, actions }) => {
  const styles = useStyles();

  return (
    <Card className={styles.card} appearance="filled">
      <CardHeader
        header={
          <Text weight="semibold" size={400}>
            {title}
          </Text>
        }
      />

      <div className={styles.body}>{children}</div>

      {actions && <CardFooter>{actions}</CardFooter>}
    </Card>
  );
};

export default SectionCard;
