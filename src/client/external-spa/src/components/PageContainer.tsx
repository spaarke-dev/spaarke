import * as React from "react";
import { makeStyles, tokens, Text } from "@fluentui/react-components";

const useStyles = makeStyles({
  outer: {
    width: "100%",
    boxSizing: "border-box",
    paddingLeft: tokens.spacingHorizontalXL,
    paddingRight: tokens.spacingHorizontalXL,
    paddingTop: tokens.spacingVerticalXL,
    paddingBottom: tokens.spacingVerticalXL,
  },
  inner: {
    maxWidth: "1200px",
    margin: "0 auto",
    display: "flex",
    flexDirection: "column",
    gap: tokens.spacingVerticalL,
  },
  title: {
    color: tokens.colorNeutralForeground1,
  },
});

interface PageContainerProps {
  /** Page content */
  children: React.ReactNode;
  /** Optional page title rendered as an h1 above content */
  title?: string;
}

/**
 * PageContainer — responsive layout wrapper for SPA pages.
 *
 * Provides:
 * - Max-width of 1200px centered in the viewport
 * - Consistent horizontal and vertical padding using Fluent spacing tokens
 * - Optional `title` prop rendered as a large h1 heading
 *
 * Styled exclusively with Fluent v9 design tokens (ADR-021). No hard-coded colors.
 */
export const PageContainer: React.FC<PageContainerProps> = ({ children, title }) => {
  const styles = useStyles();

  return (
    <div className={styles.outer}>
      <div className={styles.inner}>
        {title && (
          <Text size={700} weight="semibold" as="h1" className={styles.title}>
            {title}
          </Text>
        )}
        {children}
      </div>
    </div>
  );
};

export default PageContainer;
