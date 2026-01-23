/**
 * LoadingState component
 *
 * Displays skeleton cards during initial search load, providing visual feedback
 * that results are being loaded.
 *
 * @see ADR-021 for Fluent UI v9 requirements
 */

import * as React from "react";
import {
    makeStyles,
    tokens,
    Skeleton,
    SkeletonItem,
} from "@fluentui/react-components";
import { ILoadingStateProps } from "../types";

const useStyles = makeStyles({
    container: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalM,
        padding: tokens.spacingHorizontalM,
    },
    card: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalS,
        padding: tokens.spacingHorizontalM,
        backgroundColor: tokens.colorNeutralBackground1,
        borderRadius: tokens.borderRadiusMedium,
        border: `1px solid ${tokens.colorNeutralStroke1}`,
    },
    header: {
        display: "flex",
        alignItems: "center",
        gap: tokens.spacingHorizontalM,
    },
    icon: {
        width: "32px",
        height: "32px",
    },
    titleGroup: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXS,
        flex: 1,
    },
    title: {
        width: "60%",
        height: "20px",
    },
    subtitle: {
        width: "40%",
        height: "14px",
    },
    badge: {
        width: "48px",
        height: "24px",
    },
    content: {
        display: "flex",
        flexDirection: "column",
        gap: tokens.spacingVerticalXS,
        marginTop: tokens.spacingVerticalS,
    },
    line: {
        height: "14px",
    },
    lineFull: {
        width: "100%",
    },
    lineMedium: {
        width: "80%",
    },
    lineShort: {
        width: "50%",
    },
    metadata: {
        display: "flex",
        gap: tokens.spacingHorizontalM,
        marginTop: tokens.spacingVerticalS,
    },
    metaItem: {
        width: "80px",
        height: "14px",
    },
});

/**
 * Renders a single skeleton card that matches the ResultCard layout.
 */
const SkeletonCard: React.FC = () => {
    const styles = useStyles();

    return (
        <div className={styles.card}>
            <Skeleton>
                {/* Header: Icon + Title + Badge */}
                <div className={styles.header}>
                    <SkeletonItem
                        shape="square"
                        className={styles.icon}
                    />
                    <div className={styles.titleGroup}>
                        <SkeletonItem
                            shape="rectangle"
                            className={styles.title}
                        />
                        <SkeletonItem
                            shape="rectangle"
                            className={styles.subtitle}
                        />
                    </div>
                    <SkeletonItem
                        shape="rectangle"
                        className={styles.badge}
                    />
                </div>

                {/* Content: Snippet lines */}
                <div className={styles.content}>
                    <SkeletonItem
                        shape="rectangle"
                        className={`${styles.line} ${styles.lineFull}`}
                    />
                    <SkeletonItem
                        shape="rectangle"
                        className={`${styles.line} ${styles.lineMedium}`}
                    />
                    <SkeletonItem
                        shape="rectangle"
                        className={`${styles.line} ${styles.lineShort}`}
                    />
                </div>

                {/* Metadata row */}
                <div className={styles.metadata}>
                    <SkeletonItem
                        shape="rectangle"
                        className={styles.metaItem}
                    />
                    <SkeletonItem
                        shape="rectangle"
                        className={styles.metaItem}
                    />
                    <SkeletonItem
                        shape="rectangle"
                        className={styles.metaItem}
                    />
                </div>
            </Skeleton>
        </div>
    );
};

/**
 * LoadingState component that displays skeleton cards during loading.
 *
 * @param props.count - Number of skeleton cards to display (default: 3)
 */
export const LoadingState: React.FC<ILoadingStateProps> = ({ count = 3 }) => {
    const styles = useStyles();

    // Create array of specified length for rendering skeletons
    const skeletons = Array.from({ length: count }, (_, index) => index);

    return (
        <div className={styles.container}>
            {skeletons.map((index) => (
                <SkeletonCard key={index} />
            ))}
        </div>
    );
};

export default LoadingState;
