/**
 * Email Processing Dashboard React Component
 *
 * Displays email-to-document processing statistics using Fluent UI v9.
 * Features:
 * - Conversion rate metrics (success/failure)
 * - Webhook/Polling/Filter statistics
 * - Job processing status
 * - Auto-refresh with configurable interval
 * - Dark mode support
 */

import * as React from 'react';
import { useState, useEffect, useCallback, useRef } from 'react';
import {
    FluentProvider,
    webLightTheme,
    webDarkTheme,
    Card,
    CardHeader,
    Text,
    Title3,
    Body1,
    Caption1,
    Spinner,
    Button,
    Badge,
    Divider,
    tokens,
    makeStyles,
    shorthands
} from '@fluentui/react-components';
// Using inline SVG icons to reduce bundle size (replaces @fluentui/react-icons)
import {
    ArrowSync24Regular,
    CheckmarkCircle24Regular,
    DismissCircle24Regular,
    Mail24Regular,
    DocumentArrowRight24Regular,
    Filter24Regular,
    Clock24Regular
} from './icons';
import { EmailProcessingStats } from './types';

// ============================================================================
// Styles using Fluent UI makeStyles (v9 pattern)
// ============================================================================

const useStyles = makeStyles({
    container: {
        display: 'flex',
        flexDirection: 'column',
        height: '100%',
        ...shorthands.padding('16px'),
        boxSizing: 'border-box',
        backgroundColor: tokens.colorNeutralBackground1,
    },
    header: {
        display: 'flex',
        justifyContent: 'space-between',
        alignItems: 'center',
        marginBottom: '16px',
    },
    headerTitle: {
        display: 'flex',
        alignItems: 'center',
        ...shorthands.gap('8px'),
    },
    grid: {
        display: 'grid',
        gridTemplateColumns: 'repeat(auto-fit, minmax(280px, 1fr))',
        ...shorthands.gap('16px'),
        flexGrow: 1,
        overflowY: 'auto',
    },
    card: {
        minHeight: '120px',
    },
    cardContent: {
        display: 'flex',
        flexDirection: 'column',
        ...shorthands.gap('8px'),
        ...shorthands.padding('0', '16px', '16px', '16px'),
    },
    metricRow: {
        display: 'flex',
        justifyContent: 'space-between',
        alignItems: 'center',
    },
    metricValue: {
        fontWeight: 'bold',
        fontSize: '24px',
    },
    metricLabel: {
        color: tokens.colorNeutralForeground3,
    },
    successRate: {
        color: tokens.colorPaletteGreenForeground1,
    },
    errorRate: {
        color: tokens.colorPaletteRedForeground1,
    },
    neutralRate: {
        color: tokens.colorNeutralForeground1,
    },
    footer: {
        display: 'flex',
        justifyContent: 'space-between',
        alignItems: 'center',
        marginTop: '16px',
        paddingTop: '8px',
        ...shorthands.borderTop('1px', 'solid', tokens.colorNeutralStroke1),
    },
    footerText: {
        color: tokens.colorNeutralForeground3,
    },
    loadingContainer: {
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        height: '100%',
        ...shorthands.gap('16px'),
    },
    errorContainer: {
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        height: '100%',
        ...shorthands.gap('16px'),
        ...shorthands.padding('24px'),
        textAlign: 'center',
    },
    smallMetrics: {
        display: 'flex',
        ...shorthands.gap('16px'),
        flexWrap: 'wrap',
    },
    smallMetric: {
        display: 'flex',
        alignItems: 'baseline',
        ...shorthands.gap('4px'),
    },
});

// ============================================================================
// Component Props
// ============================================================================

interface EmailProcessingDashboardProps {
    bffApiUrl: string;
    accessToken: string;
    isDarkTheme: boolean;
    refreshIntervalSeconds: number;
    version: string;
    onError?: (error: string) => void;
}

// ============================================================================
// Component
// ============================================================================

export const EmailProcessingDashboard: React.FC<EmailProcessingDashboardProps> = ({
    bffApiUrl,
    accessToken,
    isDarkTheme,
    refreshIntervalSeconds,
    version,
    onError
}) => {
    const styles = useStyles();
    const [stats, setStats] = useState<EmailProcessingStats | null>(null);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const [lastRefresh, setLastRefresh] = useState<Date | null>(null);
    const refreshTimerRef = useRef<number | null>(null);

    // Fetch stats from BFF API
    const fetchStats = useCallback(async () => {
        try {
            const response = await fetch(`${bffApiUrl}/api/admin/email-processing/stats`, {
                method: 'GET',
                headers: {
                    'Authorization': `Bearer ${accessToken}`,
                    'Content-Type': 'application/json',
                }
            });

            if (!response.ok) {
                throw new Error(`HTTP ${response.status}: ${response.statusText}`);
            }

            const data = await response.json() as EmailProcessingStats;
            setStats(data);
            setLastRefresh(new Date());
            setError(null);
        } catch (err) {
            const errorMessage = err instanceof Error ? err.message : 'Failed to fetch statistics';
            setError(errorMessage);
            onError?.(errorMessage);
        } finally {
            setLoading(false);
        }
    }, [bffApiUrl, accessToken, onError]);

    // Initial fetch and auto-refresh setup
    useEffect(() => {
        fetchStats();

        // Set up auto-refresh if interval > 0
        if (refreshIntervalSeconds > 0) {
            refreshTimerRef.current = window.setInterval(fetchStats, refreshIntervalSeconds * 1000);
        }

        return () => {
            if (refreshTimerRef.current) {
                window.clearInterval(refreshTimerRef.current);
            }
        };
    }, [fetchStats, refreshIntervalSeconds]);

    // Manual refresh handler
    const handleRefresh = () => {
        setLoading(true);
        fetchStats();
    };

    // Format percentage
    const formatPercent = (value: number): string => {
        return `${(value * 100).toFixed(1)}%`;
    };

    // Format number with commas
    const formatNumber = (value: number): string => {
        return value.toLocaleString();
    };

    // Format duration
    const formatDuration = (ms: number): string => {
        if (ms < 1000) return `${Math.round(ms)}ms`;
        return `${(ms / 1000).toFixed(1)}s`;
    };

    // Format bytes
    const formatBytes = (bytes: number): string => {
        if (bytes < 1024) return `${bytes} B`;
        if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
        return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
    };

    // Format timestamp
    const formatTime = (date: Date | null): string => {
        if (!date) return 'Never';
        return date.toLocaleTimeString();
    };

    // Loading state
    if (loading && !stats) {
        return (
            <FluentProvider theme={isDarkTheme ? webDarkTheme : webLightTheme}>
                <div className={styles.loadingContainer}>
                    <Spinner size="large" label="Loading statistics..." />
                </div>
            </FluentProvider>
        );
    }

    // Error state
    if (error && !stats) {
        return (
            <FluentProvider theme={isDarkTheme ? webDarkTheme : webLightTheme}>
                <div className={styles.errorContainer}>
                    <DismissCircle24Regular color={tokens.colorPaletteRedForeground1} />
                    <Title3>Failed to load statistics</Title3>
                    <Body1>{error}</Body1>
                    <Button appearance="primary" onClick={handleRefresh}>
                        Retry
                    </Button>
                </div>
            </FluentProvider>
        );
    }

    return (
        <FluentProvider theme={isDarkTheme ? webDarkTheme : webLightTheme}>
            <div className={styles.container}>
                {/* Header */}
                <div className={styles.header}>
                    <div className={styles.headerTitle}>
                        <Mail24Regular />
                        <Title3>Email Processing Monitor</Title3>
                        {error && (
                            <Badge appearance="filled" color="danger" size="small">
                                Error
                            </Badge>
                        )}
                    </div>
                    <Button
                        appearance="subtle"
                        icon={<ArrowSync24Regular />}
                        onClick={handleRefresh}
                        disabled={loading}
                    >
                        {loading ? 'Refreshing...' : 'Refresh'}
                    </Button>
                </div>

                {/* Stats Grid */}
                {stats && (
                    <div className={styles.grid}>
                        {/* Conversion Stats Card */}
                        <Card className={styles.card}>
                            <CardHeader
                                header={<Text weight="semibold">Conversions</Text>}
                                description={<Caption1>Email to document processing</Caption1>}
                                image={<DocumentArrowRight24Regular />}
                            />
                            <div className={styles.cardContent}>
                                <div className={styles.metricRow}>
                                    <Text className={styles.metricLabel}>Total Requests</Text>
                                    <Text className={styles.metricValue}>
                                        {formatNumber(stats.conversion.totalRequests)}
                                    </Text>
                                </div>
                                <div className={styles.metricRow}>
                                    <Text className={styles.metricLabel}>Success Rate</Text>
                                    <Text className={`${styles.metricValue} ${
                                        stats.conversion.successRate >= 0.9 ? styles.successRate :
                                        stats.conversion.successRate >= 0.7 ? styles.neutralRate :
                                        styles.errorRate
                                    }`}>
                                        {formatPercent(stats.conversion.successRate)}
                                    </Text>
                                </div>
                                <Divider />
                                <div className={styles.smallMetrics}>
                                    <div className={styles.smallMetric}>
                                        <CheckmarkCircle24Regular color={tokens.colorPaletteGreenForeground1} />
                                        <Body1>{formatNumber(stats.conversion.successes)}</Body1>
                                    </div>
                                    <div className={styles.smallMetric}>
                                        <DismissCircle24Regular color={tokens.colorPaletteRedForeground1} />
                                        <Body1>{formatNumber(stats.conversion.failures)}</Body1>
                                    </div>
                                    <div className={styles.smallMetric}>
                                        <Clock24Regular />
                                        <Body1>{formatDuration(stats.conversion.averageDurationMs)}</Body1>
                                    </div>
                                </div>
                            </div>
                        </Card>

                        {/* Webhook Stats Card */}
                        <Card className={styles.card}>
                            <CardHeader
                                header={<Text weight="semibold">Webhooks</Text>}
                                description={<Caption1>Incoming notifications</Caption1>}
                            />
                            <div className={styles.cardContent}>
                                <div className={styles.metricRow}>
                                    <Text className={styles.metricLabel}>Received</Text>
                                    <Text className={styles.metricValue}>
                                        {formatNumber(stats.webhook.totalReceived)}
                                    </Text>
                                </div>
                                <div className={styles.metricRow}>
                                    <Text className={styles.metricLabel}>Accept Rate</Text>
                                    <Text className={`${styles.metricValue} ${
                                        stats.webhook.acceptRate >= 0.9 ? styles.successRate :
                                        stats.webhook.acceptRate >= 0.7 ? styles.neutralRate :
                                        styles.errorRate
                                    }`}>
                                        {formatPercent(stats.webhook.acceptRate)}
                                    </Text>
                                </div>
                                <Divider />
                                <div className={styles.smallMetrics}>
                                    <div className={styles.smallMetric}>
                                        <Caption1>Enqueued:</Caption1>
                                        <Body1>{formatNumber(stats.webhook.enqueued)}</Body1>
                                    </div>
                                    <div className={styles.smallMetric}>
                                        <Caption1>Rejected:</Caption1>
                                        <Body1>{formatNumber(stats.webhook.rejected)}</Body1>
                                    </div>
                                </div>
                            </div>
                        </Card>

                        {/* Job Processing Card */}
                        <Card className={styles.card}>
                            <CardHeader
                                header={<Text weight="semibold">Job Processing</Text>}
                                description={<Caption1>Background queue status</Caption1>}
                            />
                            <div className={styles.cardContent}>
                                <div className={styles.metricRow}>
                                    <Text className={styles.metricLabel}>Total Processed</Text>
                                    <Text className={styles.metricValue}>
                                        {formatNumber(stats.job.totalProcessed)}
                                    </Text>
                                </div>
                                <div className={styles.metricRow}>
                                    <Text className={styles.metricLabel}>Success Rate</Text>
                                    <Text className={`${styles.metricValue} ${
                                        stats.job.successRate >= 0.9 ? styles.successRate :
                                        stats.job.successRate >= 0.7 ? styles.neutralRate :
                                        styles.errorRate
                                    }`}>
                                        {formatPercent(stats.job.successRate)}
                                    </Text>
                                </div>
                                <Divider />
                                <div className={styles.smallMetrics}>
                                    <div className={styles.smallMetric}>
                                        <Caption1>Succeeded:</Caption1>
                                        <Body1>{formatNumber(stats.job.succeeded)}</Body1>
                                    </div>
                                    <div className={styles.smallMetric}>
                                        <Caption1>Failed:</Caption1>
                                        <Body1>{formatNumber(stats.job.failed)}</Body1>
                                    </div>
                                    <div className={styles.smallMetric}>
                                        <Caption1>Skipped:</Caption1>
                                        <Body1>{formatNumber(stats.job.skippedDuplicate)}</Body1>
                                    </div>
                                </div>
                            </div>
                        </Card>

                        {/* Filter Stats Card */}
                        <Card className={styles.card}>
                            <CardHeader
                                header={<Text weight="semibold">Filter Rules</Text>}
                                description={<Caption1>Email filtering statistics</Caption1>}
                                image={<Filter24Regular />}
                            />
                            <div className={styles.cardContent}>
                                <div className={styles.metricRow}>
                                    <Text className={styles.metricLabel}>Evaluations</Text>
                                    <Text className={styles.metricValue}>
                                        {formatNumber(stats.filter.totalEvaluations)}
                                    </Text>
                                </div>
                                <div className={styles.metricRow}>
                                    <Text className={styles.metricLabel}>Match Rate</Text>
                                    <Text className={styles.metricValue}>
                                        {formatPercent(stats.filter.matchRate)}
                                    </Text>
                                </div>
                                <Divider />
                                <div className={styles.smallMetrics}>
                                    <div className={styles.smallMetric}>
                                        <Caption1>Matched:</Caption1>
                                        <Body1>{formatNumber(stats.filter.matched)}</Body1>
                                    </div>
                                    <div className={styles.smallMetric}>
                                        <Caption1>Default:</Caption1>
                                        <Body1>{formatNumber(stats.filter.defaultAction)}</Body1>
                                    </div>
                                </div>
                            </div>
                        </Card>

                        {/* Polling Stats Card */}
                        <Card className={styles.card}>
                            <CardHeader
                                header={<Text weight="semibold">Polling</Text>}
                                description={<Caption1>Scheduled email checks</Caption1>}
                            />
                            <div className={styles.cardContent}>
                                <div className={styles.metricRow}>
                                    <Text className={styles.metricLabel}>Total Runs</Text>
                                    <Text className={styles.metricValue}>
                                        {formatNumber(stats.polling.totalRuns)}
                                    </Text>
                                </div>
                                <Divider />
                                <div className={styles.smallMetrics}>
                                    <div className={styles.smallMetric}>
                                        <Caption1>Emails Found:</Caption1>
                                        <Body1>{formatNumber(stats.polling.emailsFound)}</Body1>
                                    </div>
                                    <div className={styles.smallMetric}>
                                        <Caption1>Enqueued:</Caption1>
                                        <Body1>{formatNumber(stats.polling.emailsEnqueued)}</Body1>
                                    </div>
                                </div>
                            </div>
                        </Card>

                        {/* File Stats Card */}
                        <Card className={styles.card}>
                            <CardHeader
                                header={<Text weight="semibold">Files</Text>}
                                description={<Caption1>Attachment processing</Caption1>}
                            />
                            <div className={styles.cardContent}>
                                <div className={styles.metricRow}>
                                    <Text className={styles.metricLabel}>Attachments Processed</Text>
                                    <Text className={styles.metricValue}>
                                        {formatNumber(stats.file.totalAttachmentsProcessed)}
                                    </Text>
                                </div>
                                <div className={styles.metricRow}>
                                    <Text className={styles.metricLabel}>Avg EML Size</Text>
                                    <Text className={styles.metricValue}>
                                        {formatBytes(stats.file.averageEmlSizeBytes)}
                                    </Text>
                                </div>
                            </div>
                        </Card>
                    </div>
                )}

                {/* Footer */}
                <div className={styles.footer}>
                    <Caption1 className={styles.footerText}>
                        Last refresh: {formatTime(lastRefresh)}
                        {refreshIntervalSeconds > 0 && ` (Auto-refresh: ${refreshIntervalSeconds}s)`}
                    </Caption1>
                    <Caption1 className={styles.footerText}>
                        v{version}
                    </Caption1>
                </div>
            </div>
        </FluentProvider>
    );
};
