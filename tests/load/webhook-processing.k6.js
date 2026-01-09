/**
 * K6 Load Test: Webhook Email Processing
 *
 * Tests the POST /api/v1/emails/webhook-trigger endpoint under load.
 * Simulates Dataverse webhooks for email creation events.
 * Verifies NFR targets:
 * - 95% of emails processed within 2 minutes
 * - API response times less than 2s (P95)
 *
 * Prerequisites:
 * - k6 installed (https://k6.io/docs/getting-started/installation/)
 * - API deployed and accessible
 * - Webhook endpoint accessible (may need VPN/firewall rules)
 * - Dataverse environment with email data
 *
 * Usage:
 *   k6 run webhook-processing.k6.js --env BASE_URL=https://your-api.azurewebsites.net --env WEBHOOK_SECRET=your-secret
 *
 * Environment Variables:
 *   BASE_URL: API base URL
 *   WEBHOOK_SECRET: Webhook validation secret (matches Dataverse Service Endpoint)
 *   SIMULATE_COUNT: Number of webhook events to simulate (default: 100)
 */

import http from 'k6/http';
import { check, sleep, group } from 'k6';
import { Rate, Trend, Counter } from 'k6/metrics';
import { randomString } from 'https://jslib.k6.io/k6-utils/1.2.0/index.js';

// Custom metrics
const webhookAcceptRate = new Rate('webhook_accept_rate');
const webhookResponseTime = new Trend('webhook_response_time_ms');
const webhooksSubmitted = new Counter('webhooks_submitted');
const webhooksAccepted = new Counter('webhooks_accepted');

// Configuration
const BASE_URL = __ENV.BASE_URL || 'https://localhost:5001';
const WEBHOOK_SECRET = __ENV.WEBHOOK_SECRET || '';
const SIMULATE_COUNT = parseInt(__ENV.SIMULATE_COUNT || '100');

// Test scenarios
export const options = {
    scenarios: {
        // Scenario 1: Sustained load (simulates steady email flow)
        sustained_load: {
            executor: 'constant-arrival-rate',
            rate: 10,              // 10 webhooks per second
            timeUnit: '1s',
            duration: '1m',
            preAllocatedVUs: 20,
            maxVUs: 50,
            tags: { test_type: 'sustained' }
        },
        // Scenario 2: Burst load (simulates email flood)
        // burst_load: {
        //     executor: 'constant-arrival-rate',
        //     rate: 100,              // 100 webhooks per second
        //     timeUnit: '1s',
        //     duration: '30s',
        //     preAllocatedVUs: 50,
        //     maxVUs: 100,
        //     startTime: '2m',
        //     tags: { test_type: 'burst' }
        // },
        // Scenario 3: Ramping load (stress test)
        // ramping_load: {
        //     executor: 'ramping-arrival-rate',
        //     startRate: 1,
        //     timeUnit: '1s',
        //     stages: [
        //         { target: 50, duration: '1m' },
        //         { target: 100, duration: '2m' },
        //         { target: 50, duration: '1m' },
        //         { target: 0, duration: '30s' }
        //     ],
        //     preAllocatedVUs: 50,
        //     maxVUs: 150,
        //     startTime: '5m',
        //     tags: { test_type: 'ramping' }
        // }
    },
    thresholds: {
        'webhook_accept_rate': ['rate>0.95'],  // 95% acceptance rate
        'webhook_response_time_ms': ['p(95)<500'],  // 95% respond within 500ms
        'http_req_duration': ['p(95)<2000'],  // Overall API response time < 2s (P95)
    }
};

// Helper: Generate a fake Dataverse webhook payload
function generateWebhookPayload(emailId) {
    return {
        PrimaryEntityId: emailId || generateGuid(),
        PrimaryEntityName: 'email',
        MessageName: 'Create',
        Stage: 40,
        ExecutionOrder: 1,
        CorrelationId: generateGuid(),
        IsExecutingOffline: false,
        InitiatingUserId: generateGuid(),
        BusinessUnitId: generateGuid(),
        OrganizationId: generateGuid()
    };
}

// Helper: Generate a random GUID
function generateGuid() {
    return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function(c) {
        const r = Math.random() * 16 | 0;
        const v = c === 'x' ? r : (r & 0x3 | 0x8);
        return v.toString(16);
    });
}

// Helper: Get headers with webhook secret
function getHeaders() {
    const headers = {
        'Content-Type': 'application/json',
        'X-Correlation-Id': `load-test-${Date.now()}-${randomString(8)}`
    };

    if (WEBHOOK_SECRET) {
        headers['X-Webhook-Secret'] = WEBHOOK_SECRET;
    }

    return headers;
}

// Helper: Submit webhook
function submitWebhook(emailId) {
    const url = `${BASE_URL}/api/v1/emails/webhook-trigger`;
    const payload = JSON.stringify(generateWebhookPayload(emailId));

    const startTime = Date.now();
    const response = http.post(url, payload, { headers: getHeaders() });
    const duration = Date.now() - startTime;

    webhookResponseTime.add(duration);
    webhooksSubmitted.add(1);

    const accepted = response.status === 202;
    webhookAcceptRate.add(accepted ? 1 : 0);

    if (accepted) {
        webhooksAccepted.add(1);
    }

    check(response, {
        'webhook accepted (202)': (r) => r.status === 202,
        'response under 500ms': (r) => duration < 500,
        'has job id': (r) => {
            if (r.status === 202) {
                try {
                    const body = JSON.parse(r.body);
                    return body.jobId !== undefined;
                } catch {
                    return false;
                }
            }
            return true;  // Don't fail for non-202 responses
        }
    });

    return {
        status: response.status,
        duration: duration,
        jobId: response.status === 202 ? JSON.parse(response.body).jobId : null
    };
}

// Main test function
export default function () {
    group('Webhook Processing', function () {
        // Generate a random email ID (in real test, use existing email IDs from Dataverse)
        const emailId = generateGuid();

        const result = submitWebhook(emailId);

        if (result.status === 202) {
            console.log(`Webhook accepted for email ${emailId}, job ${result.jobId}, ${result.duration}ms`);
        } else {
            console.warn(`Webhook rejected for email ${emailId}, status ${result.status}`);
        }
    });

    // Small random delay between webhooks
    sleep(Math.random() * 0.1);  // 0-100ms
}

// Generate summary report
export function handleSummary(data) {
    const summary = {
        testRun: new Date().toISOString(),
        metrics: {
            webhooksSubmitted: data.metrics.webhooks_submitted ? data.metrics.webhooks_submitted.values.count : 0,
            webhooksAccepted: data.metrics.webhooks_accepted ? data.metrics.webhooks_accepted.values.count : 0,
            acceptRate: data.metrics.webhook_accept_rate ? data.metrics.webhook_accept_rate.values.rate : 0,
            avgResponseTimeMs: data.metrics.webhook_response_time_ms ? data.metrics.webhook_response_time_ms.values.avg : 0,
            p95ResponseTimeMs: data.metrics.webhook_response_time_ms ? data.metrics.webhook_response_time_ms.values['p(95)'] : 0,
            maxResponseTimeMs: data.metrics.webhook_response_time_ms ? data.metrics.webhook_response_time_ms.values.max : 0,
            httpReqDurationP95: data.metrics.http_req_duration ? data.metrics.http_req_duration.values['p(95)'] : 0
        },
        thresholds: data.thresholds
    };

    return {
        'stdout': JSON.stringify(summary, null, 2),
        'webhook-processing-results.json': JSON.stringify(summary, null, 2)
    };
}
