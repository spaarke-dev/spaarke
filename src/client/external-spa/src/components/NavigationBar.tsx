import * as React from "react";
import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbButton,
  BreadcrumbDivider,
  makeStyles,
  tokens,
} from "@fluentui/react-components";

const useStyles = makeStyles({
  nav: {
    paddingTop: tokens.spacingVerticalS,
    paddingBottom: tokens.spacingVerticalS,
  },
});

export interface BreadcrumbNavItem {
  /** Display label for this breadcrumb item */
  label: string;
  /** Optional href. When provided the item renders as a link. */
  href?: string;
}

interface NavigationBarProps {
  /** Ordered list of breadcrumb items, left to right */
  items: BreadcrumbNavItem[];
}

/**
 * NavigationBar — breadcrumb navigation component for SPA pages.
 *
 * Renders a Fluent UI v9 Breadcrumb trail from the `items` prop.
 * The last item is rendered as non-interactive (current page).
 * Earlier items with an `href` render as link buttons for navigation.
 *
 * Styled exclusively with Fluent v9 design tokens (ADR-021). No hard-coded colors.
 */
export const NavigationBar: React.FC<NavigationBarProps> = ({ items }) => {
  const styles = useStyles();

  if (items.length === 0) {
    return null;
  }

  return (
    <nav aria-label="Breadcrumb" className={styles.nav}>
      <Breadcrumb aria-label="Breadcrumb navigation">
        {items.map((item, index) => {
          const isLast = index === items.length - 1;
          return (
            <React.Fragment key={`${item.label}-${index}`}>
              <BreadcrumbItem>
                <BreadcrumbButton
                  current={isLast}
                  href={!isLast && item.href ? item.href : undefined}
                  style={
                    isLast
                      ? { color: tokens.colorNeutralForeground1 }
                      : { color: tokens.colorBrandForeground1 }
                  }
                >
                  {item.label}
                </BreadcrumbButton>
              </BreadcrumbItem>
              {!isLast && <BreadcrumbDivider />}
            </React.Fragment>
          );
        })}
      </Breadcrumb>
    </nav>
  );
};

export default NavigationBar;
