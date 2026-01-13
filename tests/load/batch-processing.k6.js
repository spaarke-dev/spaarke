/**
 * K6 Load Test: Batch Email Processing
 *
 * Tests the POST /api/v1/emails/admin/batch-process endpoint under load.
 * Verifies NFR targets:
 * - Batch processing: 100 emails/minute
 * - 10,000 email batch completes successfully
 *
 * Prerequisites:
 * - k6 installed (https://k6.io/docs/getting-started/installation/)
 * - API deployed and accessible
 * - Valid bearer token for authentication
 * - Dataverse environment with email data
 *
 * Usage:
 *   k6 run batch-processing.k6.js --env BASE_URL=https://your-api.azurewebsites.net --env TOKEN=your-bearer-token
 *
 * Environment Variables:
 *   BASE_URL: API base URL
 *   TOKEN: Bearer token for authentication
 *   CONTAINER_ID: SPE container ID for document storage
 */

import http from 'k6/http';
import { check, sleep, group } from 'k6';
import { Rate, Trend } from 'k6/metrics';

// Custom metrics
const batchSubmissionSuccess = new Rate('batch_submission_success');
const batchCompletionTime = new Trend('batch_completion_time_ms');
const jobStatusCheckTime = new Trend('job_status_check_time_ms');
const emailsPerMinute = new Trend('emails_per_minute');

// Configuration
const BASE_URL = __ENV.BASE_URL || 'https://localhost:5001';
const TOKEN = __ENV.TOKEN || '';
const CONTAINER_ID = __ENV.CONTAINER_ID || '';

// Test scenarios
export const options = {
    scenarios: {
        // Scenario 1: Small batch test (baseline)
        small_batch: {
            executor: 'constant-vus',
            vus: 1,
            duration: '2m',
            tags: { test_type: 'small_batch' },
            env: { BATCH_SIZE: '100' }
        },
        // Scenario 2: Medium batch test (1,000 emails)
        // medium_batch: {
        //     executor: 'constant-vus',
        //     vus: 1,
        //     duration: '15m',
        //     startTime: '3m',
        //     tags: { test_type: 'medium_batch' },
        //     env: { BATCH_SIZE: '1000' }
        // },
        // Scenario 3: Large batch test (10,000 emails) - uncomment for full load test
        // large_batch: {
        //     executor: 'constant-vus',
        //     vus: 1,
        //     duration: '2h',
        //     startTime: '20m',
        //     tags: { test_type: 'large_batch' },
        //     env: { BATCH_SIZE: '10000' }
        // }
    },
    thresholds: {
        'batch_submission_success': ['rate>0.95'],  // 95% success rate
        'batch_completion_time_ms': ['p(95)<120000'],  // 95% complete within 2 minutes
        'http_req_duration': ['p(95)<2000'],  // API response time < 2s (P95)
    }
};

// Helper: Get headers with auth
function getHeaders() {
    return {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${TOKEN}`,
        'X-Correlation-Id': `load-test-${Date.now()}`
    };
}

// Helper: Submit batch job
function submitBatchJob(batchSize, startDate, endDate) {
    const url = `${BASE_URL}/api/v1/emails/admin/batch-process`;

    const payload = JSON.stringify({
        startDate: startDate,
        endDate: endDate,
        maxEmails: batchSize,
        includeAttachments: true,
        createAttachmentDocuments: true,
        queueForAiProcessing: false,
        statusFilter: 'Completed',
        skipAlreadyConverted: true,
        containerId: CONTAINER_ID
    });

    const response = http.post(url, payload, { headers: getHeaders() });

    check(response, {
        'batch submitted (202)': (r) => r.status === 202,
        'has job id': (r) => {
            try {
                const body = JSON.parse(r.body);
                return body.jobId !== undefined;
            } catch {
                return false;
            }
        }
    });

    batchSubmissionSuccess.add(response.status === 202 ? 1 : 0);

    if (response.status === 202) {
        try {
            return JSON.parse(response.body);
        } catch {
            return null;
        }
    }
    return null;
}

// Helper: Poll job status until completion
function waitForJobCompletion(jobId, maxWaitMs = 300000) {
    const url = `${BASE_URL}/api/v1/emails/admin/batch-process/${jobId}/status`;
    const startTime = Date.now();
    let lastStatus = 'Pending';

    while (Date.now() - startTime < maxWaitMs) {
        const statusStart = Date.now();
        const response = http.get(url, { headers: getHeaders() });
        const statusDuration = Date.now() - statusStart;

        jobStatusCheckTime.add(statusDuration);

        check(response, {
            'status check (200)': (r) => r.status === 200
        });

        if (response.status === 200) {
            try {
                const status = JSON.parse(response.body);
                lastStatus = status.status;

                console.log(`Job ${jobId}: ${status.status} - ${status.progressPercent}% (${status.processedCount}/${status.totalCount})`);

                if (status.status === 'Completed' || status.status === 'PartiallyCompleted' || status.status === 'Failed') {
                    const completionTime = Date.now() - startTime;
                    batchCompletionTime.add(completionTime);

                    // Calculate emails per minute
                    const totalProcessed = status.processedCount + status.errorCount + status.skippedCount;
                    const minutesElapsed = completionTime / 60000;
                    if (minutesElapsed > 0) {
                        emailsPerMinute.add(totalProcessed / minutesElapsed);
                    }

                    return status;
                }
            } catch (e) {
                console.error(`Failed to parse status response: ${e.message}`);
            }
        }

        // Poll every 5 seconds
        sleep(5);
    }

    console.error(`Job ${jobId} did not complete within ${maxWaitMs}ms. Last status: ${lastStatus}`);
    return null;
}

// Main test function
export default function () {
    const batchSize = parseInt(__ENV.BATCH_SIZE || '100');

    // Calculate date range (last 30 days by default)
    const endDate = new Date();
    const startDate = new Date();
    startDate.setDate(startDate.getDate() - 30);

    group('Batch Email Processing', function () {
        console.log(`Submitting batch job for ${batchSize} emails...`);

        // Submit batch job
        const jobResponse = submitBatchJob(
            batchSize,
            startDate.toISOString(),
            endDate.toISOString()
        );

        if (jobResponse && jobResponse.jobId) {
            console.log(`Batch job submitted: ${jobResponse.jobId}`);

            // Wait for job completion
            const finalStatus = waitForJobCompletion(jobResponse.jobId);

            if (finalStatus) {
                check(finalStatus, {
                    'job completed': (s) => s.status === 'Completed' || s.status === 'PartiallyCompleted',
                    'no unexpected failures': (s) => s.errorCount < (s.totalCount * 0.05)  // < 5% error rate
                });

                console.log(`Job completed: ${finalStatus.processedCount} processed, ${finalStatus.errorCount} errors, ${finalStatus.skippedCount} skipped`);
            }
        } else {
            console.error('Failed to submit batch job');
        }
    });

    // Sleep between test iterations
    sleep(10);
}

// Generate summary report
export function handleSummary(data) {
    const summary = {
        testRun: new Date().toISOString(),
        metrics: {
            batchSubmissionSuccessRate: data.metrics.batch_submission_success ? data.metrics.batch_submission_success.values.rate : 0,
            avgCompletionTimeMs: data.metrics.batch_completion_time_ms ? data.metrics.batch_completion_time_ms.values.avg : 0,
            p95CompletionTimeMs: data.metrics.batch_completion_time_ms ? data.metrics.batch_completion_time_ms.values['p(95)'] : 0,
            avgEmailsPerMinute: data.metrics.emails_per_minute ? data.metrics.emails_per_minute.values.avg : 0,
            httpReqDurationP95: data.metrics.http_req_duration ? data.metrics.http_req_duration.values['p(95)'] : 0
        },
        thresholds: data.thresholds
    };

    return {
        'stdout': JSON.stringify(summary, null, 2),
        'batch-processing-results.json': JSON.stringify(summary, null, 2)
    };
}
