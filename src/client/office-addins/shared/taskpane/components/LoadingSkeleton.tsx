import React from 'react';
import {
  makeStyles,
  tokens,
  Skeleton,
  SkeletonItem,
} from '@fluentui/react-components';

/**
 * LoadingSkeleton - Skeleton loading state for Office Add-in task pane.
 *
 * Provides visual feedback during initial load or data fetching.
 * Uses Fluent UI v9 Skeleton components per ADR-021.
 */

const useStyles = makeStyles({
  container: {
    display: 'flex',
    flexDirection: 'column',
    height: '100%',
    backgroundColor: tokens.colorNeutralBackground1,
  },
  header: {
    display: 'flex',
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    padding: `${tokens.spacingVerticalM} ${tokens.spacingHorizontalM}`,
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
    backgroundColor: tokens.colorNeutralBackground2,
  },
  headerTitle: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
  },
  headerActions: {
    display: 'flex',
    gap: tokens.spacingHorizontalS,
  },
  navigation: {
    display: 'flex',
    justifyContent: 'center',
    gap: tokens.spacingHorizontalM,
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`,
    borderBottom: `1px solid ${tokens.colorNeutralStroke2}`,
  },
  content: {
    flex: 1,
    padding: tokens.spacingVerticalM,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  card: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    padding: tokens.spacingHorizontalM,
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorNeutralBackground2,
  },
  footer: {
    display: 'flex',
    justifyContent: 'space-between',
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`,
    borderTop: `1px solid ${tokens.colorNeutralStroke2}`,
    backgroundColor: tokens.colorNeutralBackground2,
  },
});

export interface LoadingSkeletonProps {
  /** Whether to show the header skeleton */
  showHeader?: boolean;
  /** Whether to show the navigation skeleton */
  showNavigation?: boolean;
  /** Whether to show the footer skeleton */
  showFooter?: boolean;
  /** Number of content cards to show */
  contentCards?: number;
}

export const LoadingSkeleton: React.FC<LoadingSkeletonProps> = ({
  showHeader = true,
  showNavigation = true,
  showFooter = true,
  contentCards = 2,
}) => {
  const styles = useStyles();

  return (
    <div className={styles.container}>
      {showHeader && (
        <Skeleton aria-label="Loading header">
          <div className={styles.header}>
            <div className={styles.headerTitle}>
              <SkeletonItem shape="circle" size={24} />
              <SkeletonItem shape="rectangle" style={{ width: '120px', height: '24px' }} />
            </div>
            <div className={styles.headerActions}>
              <SkeletonItem shape="circle" size={32} />
              <SkeletonItem shape="circle" size={32} />
            </div>
          </div>
        </Skeleton>
      )}

      {showNavigation && (
        <Skeleton aria-label="Loading navigation">
          <div className={styles.navigation}>
            <SkeletonItem shape="rectangle" style={{ width: '60px', height: '32px' }} />
            <SkeletonItem shape="rectangle" style={{ width: '60px', height: '32px' }} />
            <SkeletonItem shape="rectangle" style={{ width: '60px', height: '32px' }} />
            <SkeletonItem shape="rectangle" style={{ width: '60px', height: '32px' }} />
          </div>
        </Skeleton>
      )}

      <Skeleton aria-label="Loading content" className={styles.content}>
        {Array.from({ length: contentCards }, (_, index) => (
          <div key={index} className={styles.card}>
            <SkeletonItem shape="rectangle" style={{ width: '100%', height: '20px' }} />
            <SkeletonItem shape="rectangle" style={{ width: '80%', height: '16px' }} />
            <SkeletonItem shape="rectangle" style={{ width: '60%', height: '16px' }} />
          </div>
        ))}
      </Skeleton>

      {showFooter && (
        <Skeleton aria-label="Loading footer">
          <div className={styles.footer}>
            <SkeletonItem shape="rectangle" style={{ width: '80px', height: '14px' }} />
            <SkeletonItem shape="rectangle" style={{ width: '50px', height: '14px' }} />
          </div>
        </Skeleton>
      )}
    </div>
  );
};
