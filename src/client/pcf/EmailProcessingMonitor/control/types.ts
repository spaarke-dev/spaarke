/**
 * State machine for EmailProcessingMonitor control lifecycle
 */
export enum MonitorState {
    Loading = 'Loading',
    Ready = 'Ready',
    Error = 'Error'
}

/**
 * Email processing statistics from BFF API
 */
export interface EmailProcessingStats {
    /** Last updated timestamp */
    lastUpdated: string;

    /** Service start time */
    serviceStartTime: string;

    /** Conversion statistics */
    conversion: ConversionStats;

    /** Webhook statistics */
    webhook: WebhookStats;

    /** Polling statistics */
    polling: PollingStats;

    /** Filter statistics */
    filter: FilterStats;

    /** Job processing statistics */
    job: JobStats;

    /** File statistics */
    file: FileStats;
}

export interface ConversionStats {
    totalRequests: number;
    successes: number;
    failures: number;
    successRate: number;
    averageDurationMs: number;
}

export interface WebhookStats {
    totalReceived: number;
    enqueued: number;
    rejected: number;
    acceptRate: number;
    averageDurationMs: number;
}

export interface PollingStats {
    totalRuns: number;
    emailsFound: number;
    emailsEnqueued: number;
}

export interface FilterStats {
    totalEvaluations: number;
    matched: number;
    defaultAction: number;
    matchRate: number;
}

export interface JobStats {
    totalProcessed: number;
    succeeded: number;
    failed: number;
    skippedDuplicate: number;
    successRate: number;
    averageDurationMs: number;
}

export interface FileStats {
    totalAttachmentsProcessed: number;
    averageEmlSizeBytes: number;
}

/**
 * Recent error entry
 */
export interface RecentError {
    timestamp: string;
    errorCode: string;
    message: string;
    emailId?: string;
}
