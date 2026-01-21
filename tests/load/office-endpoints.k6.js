/**
 * K6 Load Test: Office Integration Endpoints
 *
 * Tests the Office Add-in API endpoints under load to verify NFR requirements:
 * - API p95 response time < 2 seconds (NFR-01)
 * - SSE updates within 1 second (NFR-04)
 * - System handles 50 concurrent users (spec.md)
 * - Rate limiting functions correctly under load
 *
 * Endpoints tested:
 * - POST /office/save - Email/document save
 * - GET /office/jobs/{id} - Job status polling
 * - GET /office/jobs/{id}/stream - SSE streaming (latency only, no full stream test)
 * - GET /office/search/entities - Entity typeahead search
 * - GET /office/search/documents - Document search for sharing
 * - POST /office/share/links - Generate share links
 * - POST /office/share/attach - Package attachments
 * - GET /office/recent - Recent items
 *
 * Prerequisites:
 * - k6 installed (https://k6.io/docs/getting-started/installation/)
 * - API deployed and accessible
 * - Valid bearer token for authentication
 * - Test entity IDs in the target environment
 *
 * Usage:
 *   k6 run office-endpoints.k6.js \
 *     --env BASE_URL=https://your-api.azurewebsites.net \
 *     --env TOKEN=your-bearer-token \
 *     --env TEST_ENTITY_ID=guid-of-test-entity \
 *     --env TEST_DOCUMENT_ID=guid-of-test-document
 *
 * Environment Variables:
 *   BASE_URL: API base URL (required)
 *   TOKEN: Bearer token for authentication (required)
 *   TEST_ENTITY_ID: GUID of a test Matter/Account for association (required for save tests)
 *   TEST_DOCUMENT_ID: GUID of a test document for share tests (required for share tests)
 */

import http from 'k6/http';
import { check, sleep, group } from 'k6';
import { Rate, Trend, Counter } from 'k6/metrics';
import { uuidv4 } from 'https://jslib.k6.io/k6-utils/1.4.0/index.js';

// ============================================================================
// Custom Metrics
// ============================================================================

// Response time metrics by endpoint
const saveResponseTime = new Trend('office_save_response_time_ms');
const searchEntitiesResponseTime = new Trend('office_search_entities_response_time_ms');
const searchDocumentsResponseTime = new Trend('office_search_documents_response_time_ms');
const jobStatusResponseTime = new Trend('office_job_status_response_time_ms');
const sseFirstEventTime = new Trend('office_sse_first_event_time_ms');
const shareLinksResponseTime = new Trend('office_share_links_response_time_ms');
const shareAttachResponseTime = new Trend('office_share_attach_response_time_ms');
const recentResponseTime = new Trend('office_recent_response_time_ms');

// Success rate metrics
const saveSuccess = new Rate('office_save_success_rate');
const searchSuccess = new Rate('office_search_success_rate');
const jobStatusSuccess = new Rate('office_job_status_success_rate');
const shareSuccess = new Rate('office_share_success_rate');
const recentSuccess = new Rate('office_recent_success_rate');

// Rate limit tracking
const rateLimitHits = new Counter('office_rate_limit_hits');

// Overall throughput
const requestsCompleted = new Counter('office_requests_completed');

// ============================================================================
// Configuration
// ============================================================================

const BASE_URL = __ENV.BASE_URL || 'https://localhost:5001';
const TOKEN = __ENV.TOKEN || '';
const TEST_ENTITY_ID = __ENV.TEST_ENTITY_ID || '00000000-0000-0000-0000-000000000000';
const TEST_ENTITY_TYPE = __ENV.TEST_ENTITY_TYPE || 'sprk_matter';
const TEST_DOCUMENT_ID = __ENV.TEST_DOCUMENT_ID || '00000000-0000-0000-0000-000000000000';

// ============================================================================
// Test Scenarios
// ============================================================================

export const options = {
    scenarios: {
        // Scenario 1: Baseline - Single user, all endpoints
        baseline_single_user: {
            executor: 'constant-vus',
            vus: 1,
            duration: '2m',
            tags: { test_type: 'baseline' },
            exec: 'baselineTest'
        },

        // Scenario 2: Light load - 10 concurrent users (save flow focus)
        light_load_10_users: {
            executor: 'constant-vus',
            vus: 10,
            duration: '3m',
            startTime: '2m30s',
            tags: { test_type: 'light_load' },
            exec: 'saveFlowTest'
        },

        // Scenario 3: Target load - 50 concurrent users (per spec requirement)
        target_load_50_users: {
            executor: 'constant-vus',
            vus: 50,
            duration: '5m',
            startTime: '6m',
            tags: { test_type: 'target_load' },
            exec: 'mixedLoadTest'
        },

        // Scenario 4: Search stress test - High volume typeahead
        search_stress: {
            executor: 'constant-arrival-rate',
            rate: 100,  // 100 requests per second
            timeUnit: '1s',
            duration: '1m',
            preAllocatedVUs: 20,
            maxVUs: 50,
            startTime: '11m30s',
            tags: { test_type: 'search_stress' },
            exec: 'searchStressTest'
        },

        // Scenario 5: Rate limit validation
        rate_limit_test: {
            executor: 'per-vu-iterations',
            vus: 1,
            iterations: 1,
            startTime: '13m',
            tags: { test_type: 'rate_limit' },
            exec: 'rateLimitTest'
        },

        // Scenario 6: SSE latency test
        sse_latency_test: {
            executor: 'constant-vus',
            vus: 5,
            duration: '2m',
            startTime: '14m',
            tags: { test_type: 'sse_latency' },
            exec: 'sseLatencyTest'
        }
    },

    // Thresholds based on spec.md NFR requirements
    thresholds: {
        // API response time p95 < 2 seconds (NFR-01)
        'office_save_response_time_ms': ['p(95)<2000'],
        'office_search_entities_response_time_ms': ['p(95)<500'],  // Typeahead should be fast
        'office_search_documents_response_time_ms': ['p(95)<1000'],
        'office_job_status_response_time_ms': ['p(95)<500'],
        'office_share_links_response_time_ms': ['p(95)<2000'],
        'office_share_attach_response_time_ms': ['p(95)<2000'],
        'office_recent_response_time_ms': ['p(95)<500'],

        // SSE first event within 1 second (NFR-04)
        'office_sse_first_event_time_ms': ['p(95)<1000'],

        // Success rates
        'office_save_success_rate': ['rate>0.95'],
        'office_search_success_rate': ['rate>0.99'],
        'office_job_status_success_rate': ['rate>0.99'],
        'office_share_success_rate': ['rate>0.95'],
        'office_recent_success_rate': ['rate>0.99'],

        // Overall HTTP performance
        'http_req_duration': ['p(95)<2000', 'p(99)<5000'],
        'http_req_failed': ['rate<0.05']  // Less than 5% failure rate
    }
};

// ============================================================================
// Helper Functions
// ============================================================================

function getHeaders() {
    return {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${TOKEN}`,
        'X-Correlation-Id': `perf-test-${uuidv4()}`
    };
}

function generateIdempotencyKey(prefix) {
    return `${prefix}-${uuidv4()}-${Date.now()}`;
}

function checkRateLimit(response) {
    if (response.status === 429) {
        rateLimitHits.add(1);
        const retryAfter = response.headers['Retry-After'];
        console.log(`Rate limited. Retry-After: ${retryAfter}s`);
        return true;
    }
    return false;
}

// ============================================================================
// Endpoint Test Functions
// ============================================================================

/**
 * Test POST /office/save endpoint
 * Returns jobId on success (202 Accepted) or existing document on duplicate (200 OK)
 */
function testSave() {
    const idempotencyKey = generateIdempotencyKey('save');
    const url = `${BASE_URL}/office/save`;

    const payload = JSON.stringify({
        contentType: 'Email',
        targetEntity: {
            entityType: TEST_ENTITY_TYPE,
            entityId: TEST_ENTITY_ID
        },
        email: {
            messageId: `perf-test-${uuidv4()}`,
            subject: 'Performance Test Email',
            sender: 'test@example.com',
            body: 'This is a performance test email body.'
        },
        processing: {
            profileSummary: false,
            ragIndex: false,
            deepAnalysis: false
        },
        idempotencyKey: idempotencyKey
    });

    const headers = getHeaders();
    headers['X-Idempotency-Key'] = idempotencyKey;

    const startTime = Date.now();
    const response = http.post(url, payload, { headers });
    const duration = Date.now() - startTime;

    saveResponseTime.add(duration);
    requestsCompleted.add(1);

    const isSuccess = response.status === 202 || response.status === 200;
    saveSuccess.add(isSuccess ? 1 : 0);

    if (!checkRateLimit(response)) {
        check(response, {
            'save: accepted or duplicate (2xx)': (r) => r.status === 202 || r.status === 200,
            'save: has jobId': (r) => {
                try {
                    const body = JSON.parse(r.body);
                    return body.jobId !== undefined;
                } catch {
                    return false;
                }
            },
            'save: response time < 3s': (r) => duration < 3000
        });
    }

    // Return jobId for subsequent tests
    if (isSuccess) {
        try {
            const body = JSON.parse(response.body);
            return body.jobId;
        } catch {
            return null;
        }
    }
    return null;
}

/**
 * Test GET /office/jobs/{id} endpoint
 */
function testJobStatus(jobId) {
    if (!jobId) return null;

    const url = `${BASE_URL}/office/jobs/${jobId}`;
    const startTime = Date.now();
    const response = http.get(url, { headers: getHeaders() });
    const duration = Date.now() - startTime;

    jobStatusResponseTime.add(duration);
    requestsCompleted.add(1);

    const isSuccess = response.status === 200;
    jobStatusSuccess.add(isSuccess ? 1 : 0);

    if (!checkRateLimit(response)) {
        check(response, {
            'job status: success (200)': (r) => r.status === 200,
            'job status: has status field': (r) => {
                try {
                    const body = JSON.parse(r.body);
                    return body.status !== undefined;
                } catch {
                    return false;
                }
            },
            'job status: response time < 500ms': (r) => duration < 500
        });
    }

    if (isSuccess) {
        try {
            return JSON.parse(response.body);
        } catch {
            return null;
        }
    }
    return null;
}

/**
 * Test GET /office/jobs/{id}/stream SSE endpoint (first event latency)
 * Note: k6 doesn't fully support SSE streaming, so we test connection + first response
 */
function testSseLatency(jobId) {
    if (!jobId) return;

    const url = `${BASE_URL}/office/jobs/${jobId}/stream`;
    const headers = getHeaders();
    headers['Accept'] = 'text/event-stream';

    const startTime = Date.now();
    const response = http.get(url, {
        headers,
        timeout: '5s',
        // Note: k6 will close the connection after receiving initial response
        // This tests the time to first event
    });
    const duration = Date.now() - startTime;

    sseFirstEventTime.add(duration);
    requestsCompleted.add(1);

    // SSE should start with 200 and text/event-stream content type
    if (!checkRateLimit(response)) {
        check(response, {
            'sse: connection established (200)': (r) => r.status === 200,
            'sse: correct content type': (r) =>
                r.headers['Content-Type'] &&
                r.headers['Content-Type'].includes('text/event-stream'),
            'sse: first event < 1s': (r) => duration < 1000
        });
    }
}

/**
 * Test GET /office/search/entities endpoint
 */
function testSearchEntities(query = 'test') {
    const url = `${BASE_URL}/office/search/entities?q=${encodeURIComponent(query)}&type=Matter,Account&top=20`;

    const startTime = Date.now();
    const response = http.get(url, { headers: getHeaders() });
    const duration = Date.now() - startTime;

    searchEntitiesResponseTime.add(duration);
    requestsCompleted.add(1);

    const isSuccess = response.status === 200;
    searchSuccess.add(isSuccess ? 1 : 0);

    if (!checkRateLimit(response)) {
        check(response, {
            'search entities: success (200)': (r) => r.status === 200,
            'search entities: has results array': (r) => {
                try {
                    const body = JSON.parse(r.body);
                    return Array.isArray(body.results);
                } catch {
                    return false;
                }
            },
            'search entities: response time < 500ms': (r) => duration < 500
        });
    }

    return response;
}

/**
 * Test GET /office/search/documents endpoint
 */
function testSearchDocuments(query = 'document') {
    const url = `${BASE_URL}/office/search/documents?q=${encodeURIComponent(query)}&top=20`;

    const startTime = Date.now();
    const response = http.get(url, { headers: getHeaders() });
    const duration = Date.now() - startTime;

    searchDocumentsResponseTime.add(duration);
    requestsCompleted.add(1);

    const isSuccess = response.status === 200;
    searchSuccess.add(isSuccess ? 1 : 0);

    if (!checkRateLimit(response)) {
        check(response, {
            'search documents: success (200)': (r) => r.status === 200,
            'search documents: has results array': (r) => {
                try {
                    const body = JSON.parse(r.body);
                    return Array.isArray(body.results);
                } catch {
                    return false;
                }
            },
            'search documents: response time < 1s': (r) => duration < 1000
        });
    }

    return response;
}

/**
 * Test POST /office/share/links endpoint
 */
function testShareLinks(documentId) {
    if (!documentId || documentId === '00000000-0000-0000-0000-000000000000') {
        // Skip if no valid document ID
        return null;
    }

    const url = `${BASE_URL}/office/share/links`;
    const payload = JSON.stringify({
        documentIds: [documentId],
        recipients: ['test@example.com'],
        grantAccess: false,
        role: 'ViewOnly'
    });

    const headers = getHeaders();
    headers['X-Idempotency-Key'] = generateIdempotencyKey('share-links');

    const startTime = Date.now();
    const response = http.post(url, payload, { headers });
    const duration = Date.now() - startTime;

    shareLinksResponseTime.add(duration);
    requestsCompleted.add(1);

    const isSuccess = response.status === 200;
    shareSuccess.add(isSuccess ? 1 : 0);

    if (!checkRateLimit(response)) {
        check(response, {
            'share links: success (200)': (r) => r.status === 200,
            'share links: has links array': (r) => {
                try {
                    const body = JSON.parse(r.body);
                    return Array.isArray(body.links);
                } catch {
                    return false;
                }
            },
            'share links: response time < 2s': (r) => duration < 2000
        });
    }

    return response;
}

/**
 * Test POST /office/share/attach endpoint
 */
function testShareAttach(documentId) {
    if (!documentId || documentId === '00000000-0000-0000-0000-000000000000') {
        // Skip if no valid document ID
        return null;
    }

    const url = `${BASE_URL}/office/share/attach`;
    const payload = JSON.stringify({
        documentIds: [documentId],
        deliveryMode: 'Url'
    });

    const startTime = Date.now();
    const response = http.post(url, payload, { headers: getHeaders() });
    const duration = Date.now() - startTime;

    shareAttachResponseTime.add(duration);
    requestsCompleted.add(1);

    const isSuccess = response.status === 200;
    shareSuccess.add(isSuccess ? 1 : 0);

    if (!checkRateLimit(response)) {
        check(response, {
            'share attach: success (200)': (r) => r.status === 200,
            'share attach: has attachments array': (r) => {
                try {
                    const body = JSON.parse(r.body);
                    return Array.isArray(body.attachments);
                } catch {
                    return false;
                }
            },
            'share attach: response time < 2s': (r) => duration < 2000
        });
    }

    return response;
}

/**
 * Test GET /office/recent endpoint
 */
function testRecent() {
    const url = `${BASE_URL}/office/recent?top=10`;

    const startTime = Date.now();
    const response = http.get(url, { headers: getHeaders() });
    const duration = Date.now() - startTime;

    recentResponseTime.add(duration);
    requestsCompleted.add(1);

    const isSuccess = response.status === 200;
    recentSuccess.add(isSuccess ? 1 : 0);

    if (!checkRateLimit(response)) {
        check(response, {
            'recent: success (200)': (r) => r.status === 200,
            'recent: has recentAssociations': (r) => {
                try {
                    const body = JSON.parse(r.body);
                    return Array.isArray(body.recentAssociations);
                } catch {
                    return false;
                }
            },
            'recent: response time < 500ms': (r) => duration < 500
        });
    }

    return response;
}

// ============================================================================
// Test Scenarios
// ============================================================================

/**
 * Baseline test - All endpoints, single user
 */
export function baselineTest() {
    group('Baseline - All Endpoints', function () {
        // Test save flow
        const jobId = testSave();
        sleep(0.5);

        // Test job status
        if (jobId) {
            testJobStatus(jobId);
            sleep(0.5);
        }

        // Test search endpoints
        testSearchEntities('test');
        sleep(0.3);

        testSearchDocuments('contract');
        sleep(0.3);

        // Test recent
        testRecent();
        sleep(0.3);

        // Test share endpoints (if document ID provided)
        if (TEST_DOCUMENT_ID !== '00000000-0000-0000-0000-000000000000') {
            testShareLinks(TEST_DOCUMENT_ID);
            sleep(0.3);

            testShareAttach(TEST_DOCUMENT_ID);
            sleep(0.3);
        }
    });

    sleep(1);
}

/**
 * Save flow test - Focuses on save + job status polling
 */
export function saveFlowTest() {
    group('Save Flow', function () {
        // Submit save
        const jobId = testSave();

        if (jobId) {
            // Poll job status a few times
            for (let i = 0; i < 3; i++) {
                sleep(1);
                const status = testJobStatus(jobId);
                if (status && (status.status === 'Completed' || status.status === 'Failed')) {
                    break;
                }
            }
        }
    });

    sleep(2);
}

/**
 * Mixed load test - Simulates realistic user behavior
 */
export function mixedLoadTest() {
    // 40% search operations (typeahead during entity selection)
    // 30% save operations
    // 20% job status checks
    // 10% recent/share operations

    const rand = Math.random();

    if (rand < 0.4) {
        group('Mixed - Search', function () {
            const queries = ['acme', 'smith', 'project', 'contract', 'invoice'];
            const query = queries[Math.floor(Math.random() * queries.length)];
            testSearchEntities(query);
        });
    } else if (rand < 0.7) {
        group('Mixed - Save Flow', function () {
            const jobId = testSave();
            if (jobId) {
                sleep(1);
                testJobStatus(jobId);
            }
        });
    } else if (rand < 0.9) {
        group('Mixed - Recent', function () {
            testRecent();
        });
    } else {
        group('Mixed - Share', function () {
            if (TEST_DOCUMENT_ID !== '00000000-0000-0000-0000-000000000000') {
                testShareLinks(TEST_DOCUMENT_ID);
            }
        });
    }

    sleep(Math.random() * 2 + 0.5);  // 0.5-2.5s between actions
}

/**
 * Search stress test - High volume typeahead simulation
 */
export function searchStressTest() {
    // Simulate typeahead with varying query lengths
    const prefixes = ['a', 'ac', 'acm', 'acme', 'b', 'ba', 'ban', 'bank'];
    const query = prefixes[Math.floor(Math.random() * prefixes.length)];

    testSearchEntities(query);
}

/**
 * Rate limit validation test
 */
export function rateLimitTest() {
    group('Rate Limit Test - Save', function () {
        console.log('Testing save endpoint rate limit (10/min)...');

        // Send 15 requests rapidly to trigger rate limit
        let rateLimited = false;
        for (let i = 0; i < 15; i++) {
            const jobId = testSave();
            if (jobId === null) {
                // Check if we got rate limited
                rateLimited = true;
                console.log(`Rate limited after ${i + 1} requests`);
                break;
            }
            sleep(0.1);  // Small delay
        }

        check({ rateLimited }, {
            'rate limit: triggered for save endpoint': (data) => data.rateLimited
        });
    });

    sleep(60);  // Wait for rate limit window to reset

    group('Rate Limit Test - Search', function () {
        console.log('Testing search endpoint rate limit (30/min)...');

        // Send 35 requests rapidly to trigger rate limit
        let rateLimited = false;
        for (let i = 0; i < 35; i++) {
            const response = testSearchEntities('test');
            if (response && response.status === 429) {
                rateLimited = true;
                console.log(`Rate limited after ${i + 1} requests`);
                break;
            }
            sleep(0.05);  // Small delay
        }

        check({ rateLimited }, {
            'rate limit: triggered for search endpoint': (data) => data.rateLimited
        });
    });
}

/**
 * SSE latency test - Tests job status streaming connection
 */
export function sseLatencyTest() {
    group('SSE Latency Test', function () {
        // First, create a save job to get a jobId
        const jobId = testSave();

        if (jobId) {
            sleep(0.5);

            // Test SSE connection latency
            testSseLatency(jobId);

            // Also test polling for comparison
            testJobStatus(jobId);
        }
    });

    sleep(2);
}

// ============================================================================
// Default Test Function
// ============================================================================

export default function () {
    baselineTest();
}

// ============================================================================
// Summary Report
// ============================================================================

export function handleSummary(data) {
    const summary = {
        testRun: new Date().toISOString(),
        environment: BASE_URL,
        testDuration: data.state ? data.state.testRunDurationMs : 0,
        metrics: {
            // Response times (p95)
            saveResponseTimeP95: data.metrics.office_save_response_time_ms ?
                data.metrics.office_save_response_time_ms.values['p(95)'] : null,
            searchEntitiesResponseTimeP95: data.metrics.office_search_entities_response_time_ms ?
                data.metrics.office_search_entities_response_time_ms.values['p(95)'] : null,
            searchDocumentsResponseTimeP95: data.metrics.office_search_documents_response_time_ms ?
                data.metrics.office_search_documents_response_time_ms.values['p(95)'] : null,
            jobStatusResponseTimeP95: data.metrics.office_job_status_response_time_ms ?
                data.metrics.office_job_status_response_time_ms.values['p(95)'] : null,
            sseFirstEventP95: data.metrics.office_sse_first_event_time_ms ?
                data.metrics.office_sse_first_event_time_ms.values['p(95)'] : null,
            shareLinksResponseTimeP95: data.metrics.office_share_links_response_time_ms ?
                data.metrics.office_share_links_response_time_ms.values['p(95)'] : null,
            recentResponseTimeP95: data.metrics.office_recent_response_time_ms ?
                data.metrics.office_recent_response_time_ms.values['p(95)'] : null,

            // Success rates
            saveSuccessRate: data.metrics.office_save_success_rate ?
                data.metrics.office_save_success_rate.values.rate : null,
            searchSuccessRate: data.metrics.office_search_success_rate ?
                data.metrics.office_search_success_rate.values.rate : null,
            jobStatusSuccessRate: data.metrics.office_job_status_success_rate ?
                data.metrics.office_job_status_success_rate.values.rate : null,
            shareSuccessRate: data.metrics.office_share_success_rate ?
                data.metrics.office_share_success_rate.values.rate : null,

            // Throughput
            totalRequestsCompleted: data.metrics.office_requests_completed ?
                data.metrics.office_requests_completed.values.count : 0,
            rateLimitHits: data.metrics.office_rate_limit_hits ?
                data.metrics.office_rate_limit_hits.values.count : 0,

            // Overall HTTP metrics
            httpReqDurationP95: data.metrics.http_req_duration ?
                data.metrics.http_req_duration.values['p(95)'] : null,
            httpReqFailedRate: data.metrics.http_req_failed ?
                data.metrics.http_req_failed.values.rate : null
        },
        thresholds: data.thresholds,
        nfrCompliance: {
            'NFR-01 (API p95 < 2s)': data.metrics.http_req_duration &&
                data.metrics.http_req_duration.values['p(95)'] < 2000,
            'NFR-04 (SSE < 1s)': data.metrics.office_sse_first_event_time_ms &&
                data.metrics.office_sse_first_event_time_ms.values['p(95)'] < 1000,
            'Spec (50 concurrent users)': !data.thresholds || Object.values(data.thresholds).every(t => t.ok),
            'Spec (Search < 500ms)': data.metrics.office_search_entities_response_time_ms &&
                data.metrics.office_search_entities_response_time_ms.values['p(95)'] < 500
        }
    };

    return {
        'stdout': JSON.stringify(summary, null, 2),
        'office-endpoints-results.json': JSON.stringify(summary, null, 2)
    };
}
