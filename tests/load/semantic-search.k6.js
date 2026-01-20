/**
 * K6 Load Test: Semantic Search API
 *
 * Tests the POST /api/ai/search/semantic endpoint under load.
 * Verifies NFR targets from spec.md:
 * - NFR-01: Search latency p50 < 500ms, p95 < 1000ms
 * - NFR-02: Support 50 concurrent searches
 *
 * Prerequisites:
 * - k6 installed (https://k6.io/docs/getting-started/installation/)
 * - API deployed and accessible
 * - Bearer token for authentication
 * - Test entity (Matter, Project, etc.) with indexed documents
 *
 * Usage:
 *   k6 run semantic-search.k6.js \
 *     --env BASE_URL=https://spe-api-dev-67e2xz.azurewebsites.net \
 *     --env TOKEN=your-bearer-token \
 *     --env ENTITY_TYPE=matter \
 *     --env ENTITY_ID=your-entity-guid
 *
 * Environment Variables:
 *   BASE_URL: API base URL (required)
 *   TOKEN: Bearer token for authentication (required)
 *   ENTITY_TYPE: Entity type for scoped search (default: matter)
 *   ENTITY_ID: Entity ID for scoped search (required)
 *   HYBRID_MODE: Search mode - rrf, vectorOnly, keywordOnly (default: rrf)
 */

import http from 'k6/http';
import { check, sleep, group } from 'k6';
import { Rate, Trend, Counter } from 'k6/metrics';
import { randomItem } from 'https://jslib.k6.io/k6-utils/1.2.0/index.js';

// Custom metrics
const searchSuccessRate = new Rate('search_success_rate');
const searchResponseTime = new Trend('search_response_time_ms');
const searchesExecuted = new Counter('searches_executed');
const searchesSucceeded = new Counter('searches_succeeded');
const searchesFailed = new Counter('searches_failed');
const embeddingFallbacks = new Counter('embedding_fallbacks');

// Configuration
const BASE_URL = __ENV.BASE_URL || 'https://spe-api-dev-67e2xz.azurewebsites.net';
const TOKEN = __ENV.TOKEN || '';
const ENTITY_TYPE = __ENV.ENTITY_TYPE || 'matter';
const ENTITY_ID = __ENV.ENTITY_ID || '';
const HYBRID_MODE = __ENV.HYBRID_MODE || 'rrf';

// Sample search queries for realistic testing
const TEST_QUERIES = [
    'contracts about payment terms',
    'invoices from 2024',
    'project documentation',
    'client agreements',
    'service level agreement',
    'meeting notes summary',
    'financial reports quarterly',
    'legal correspondence',
    'technical specifications',
    'proposal documents'
];

// Test scenarios - NFR targets: p50 < 500ms, p95 < 1000ms, 50 concurrent
export const options = {
    scenarios: {
        // Scenario 1: Light load (baseline)
        light_load: {
            executor: 'constant-vus',
            vus: 10,
            duration: '1m',
            tags: { test_type: 'light' }
        },
        // Scenario 2: Medium load
        medium_load: {
            executor: 'constant-vus',
            vus: 25,
            duration: '1m',
            startTime: '1m30s',
            tags: { test_type: 'medium' }
        },
        // Scenario 3: Target load (NFR-02: 50 concurrent)
        target_load: {
            executor: 'constant-vus',
            vus: 50,
            duration: '2m',
            startTime: '3m',
            tags: { test_type: 'target' }
        },
        // Scenario 4: Spike test (optional - uncomment to test burst)
        // spike_load: {
        //     executor: 'ramping-vus',
        //     startVUs: 0,
        //     stages: [
        //         { duration: '30s', target: 100 },
        //         { duration: '1m', target: 100 },
        //         { duration: '30s', target: 0 }
        //     ],
        //     startTime: '6m',
        //     tags: { test_type: 'spike' }
        // }
    },
    thresholds: {
        // NFR-01: p50 < 500ms
        'search_response_time_ms{test_type:target}': ['p(50)<500'],
        // NFR-01: p95 < 1000ms
        'search_response_time_ms{test_type:target}': ['p(95)<1000'],
        // Overall success rate
        'search_success_rate': ['rate>0.95'],
        // Overall response time bounds
        'http_req_duration': ['p(99)<3000']
    }
};

// Helper: Get authorization headers
function getHeaders() {
    const headers = {
        'Content-Type': 'application/json',
        'Accept': 'application/json',
        'X-Correlation-Id': `load-test-${Date.now()}-${Math.random().toString(36).substring(7)}`
    };

    if (TOKEN) {
        headers['Authorization'] = `Bearer ${TOKEN}`;
    }

    return headers;
}

// Helper: Build search request payload
function buildSearchPayload(query, mode = HYBRID_MODE) {
    return {
        query: query,
        scope: 'entity',
        entityType: ENTITY_TYPE,
        entityId: ENTITY_ID,
        options: {
            limit: 20,
            offset: 0,
            includeHighlights: true,
            hybridMode: mode
        }
    };
}

// Helper: Execute search request
function executeSearch(query, mode = HYBRID_MODE) {
    const url = `${BASE_URL}/api/ai/search/semantic`;
    const payload = JSON.stringify(buildSearchPayload(query, mode));

    const startTime = Date.now();
    const response = http.post(url, payload, {
        headers: getHeaders(),
        timeout: '30s'
    });
    const duration = Date.now() - startTime;

    searchResponseTime.add(duration);
    searchesExecuted.add(1);

    const success = response.status >= 200 && response.status < 300;
    searchSuccessRate.add(success ? 1 : 0);

    if (success) {
        searchesSucceeded.add(1);

        // Check for embedding fallback warning
        try {
            const body = JSON.parse(response.body);
            if (body.metadata?.warnings?.some(w => w.code === 'EMBEDDING_FALLBACK')) {
                embeddingFallbacks.add(1);
            }
        } catch (e) {
            // Ignore parse errors
        }
    } else {
        searchesFailed.add(1);
    }

    check(response, {
        'search successful (2xx)': (r) => r.status >= 200 && r.status < 300,
        'response under 500ms (p50 target)': (r) => duration < 500,
        'response under 1000ms (p95 target)': (r) => duration < 1000,
        'has results array': (r) => {
            if (r.status >= 200 && r.status < 300) {
                try {
                    const body = JSON.parse(r.body);
                    return Array.isArray(body.results);
                } catch {
                    return false;
                }
            }
            return true;
        },
        'has metadata': (r) => {
            if (r.status >= 200 && r.status < 300) {
                try {
                    const body = JSON.parse(r.body);
                    return body.metadata !== undefined;
                } catch {
                    return false;
                }
            }
            return true;
        }
    });

    return {
        status: response.status,
        duration: duration,
        resultsCount: success ? JSON.parse(response.body).results?.length : 0,
        totalResults: success ? JSON.parse(response.body).metadata?.totalResults : 0
    };
}

// Warmup function
export function setup() {
    console.log('='.repeat(60));
    console.log('Semantic Search Load Test');
    console.log('='.repeat(60));
    console.log(`Base URL: ${BASE_URL}`);
    console.log(`Entity: ${ENTITY_TYPE}/${ENTITY_ID}`);
    console.log(`Mode: ${HYBRID_MODE}`);
    console.log(`Token: ${TOKEN ? 'Configured' : 'NOT CONFIGURED'}`);
    console.log('='.repeat(60));

    // Validate configuration
    if (!TOKEN) {
        console.warn('WARNING: No TOKEN provided. Tests will likely fail with 401.');
    }
    if (!ENTITY_ID) {
        console.warn('WARNING: No ENTITY_ID provided. Using empty value.');
    }

    // Warmup: Run 5 searches to warm up the API
    console.log('Warming up API...');
    for (let i = 0; i < 5; i++) {
        const query = TEST_QUERIES[i % TEST_QUERIES.length];
        const result = executeSearch(query);
        console.log(`Warmup ${i + 1}/5: ${result.status} in ${result.duration}ms`);
        sleep(0.5);
    }
    console.log('Warmup complete.');

    return { startTime: Date.now() };
}

// Main test function
export default function (data) {
    group('Semantic Search', function () {
        // Pick a random query
        const query = randomItem(TEST_QUERIES);

        const result = executeSearch(query);

        // Log occasional samples (1 in 10)
        if (Math.random() < 0.1) {
            console.log(`Search: "${query.substring(0, 20)}..." -> ${result.status} (${result.duration}ms, ${result.resultsCount} results)`);
        }
    });

    // Small random delay between searches (simulates user think time)
    sleep(Math.random() * 0.5 + 0.2);  // 200-700ms
}

// Cleanup and summary
export function teardown(data) {
    const duration = (Date.now() - data.startTime) / 1000;
    console.log('='.repeat(60));
    console.log(`Test completed in ${duration.toFixed(1)}s`);
    console.log('='.repeat(60));
}

// Generate detailed summary report
export function handleSummary(data) {
    const targetMetrics = data.metrics.search_response_time_ms;

    const summary = {
        testRun: new Date().toISOString(),
        configuration: {
            baseUrl: BASE_URL,
            entityType: ENTITY_TYPE,
            entityId: ENTITY_ID ? `${ENTITY_ID.substring(0, 8)}...` : 'not set',
            hybridMode: HYBRID_MODE
        },
        metrics: {
            totalSearches: data.metrics.searches_executed ? data.metrics.searches_executed.values.count : 0,
            successfulSearches: data.metrics.searches_succeeded ? data.metrics.searches_succeeded.values.count : 0,
            failedSearches: data.metrics.searches_failed ? data.metrics.searches_failed.values.count : 0,
            embeddingFallbacks: data.metrics.embedding_fallbacks ? data.metrics.embedding_fallbacks.values.count : 0,
            successRate: data.metrics.search_success_rate ? data.metrics.search_success_rate.values.rate : 0,
            latency: {
                min: targetMetrics ? targetMetrics.values.min : 0,
                avg: targetMetrics ? targetMetrics.values.avg : 0,
                med: targetMetrics ? targetMetrics.values.med : 0,
                p50: targetMetrics ? targetMetrics.values['p(50)'] : 0,
                p90: targetMetrics ? targetMetrics.values['p(90)'] : 0,
                p95: targetMetrics ? targetMetrics.values['p(95)'] : 0,
                p99: targetMetrics ? targetMetrics.values['p(99)'] : 0,
                max: targetMetrics ? targetMetrics.values.max : 0
            }
        },
        nfrValidation: {
            'NFR-01_p50_under_500ms': targetMetrics ? targetMetrics.values['p(50)'] < 500 : false,
            'NFR-01_p95_under_1000ms': targetMetrics ? targetMetrics.values['p(95)'] < 1000 : false,
            'NFR-02_50_concurrent': data.metrics.searches_failed ? data.metrics.searches_failed.values.count === 0 : false
        },
        thresholds: data.thresholds
    };

    // Determine overall pass/fail
    const allNfrPassed = Object.values(summary.nfrValidation).every(v => v === true);
    summary.overallResult = allNfrPassed ? 'PASS' : 'FAIL';

    // Console output
    let consoleOutput = '\n' + '='.repeat(70) + '\n';
    consoleOutput += 'SEMANTIC SEARCH PERFORMANCE VALIDATION RESULTS\n';
    consoleOutput += '='.repeat(70) + '\n\n';

    consoleOutput += 'Configuration:\n';
    consoleOutput += `  Base URL: ${summary.configuration.baseUrl}\n`;
    consoleOutput += `  Entity: ${summary.configuration.entityType}/${summary.configuration.entityId}\n`;
    consoleOutput += `  Hybrid Mode: ${summary.configuration.hybridMode}\n\n`;

    consoleOutput += 'Metrics:\n';
    consoleOutput += `  Total Searches: ${summary.metrics.totalSearches}\n`;
    consoleOutput += `  Successful: ${summary.metrics.successfulSearches}\n`;
    consoleOutput += `  Failed: ${summary.metrics.failedSearches}\n`;
    consoleOutput += `  Embedding Fallbacks: ${summary.metrics.embeddingFallbacks}\n`;
    consoleOutput += `  Success Rate: ${(summary.metrics.successRate * 100).toFixed(2)}%\n\n`;

    consoleOutput += 'Latency (ms):\n';
    consoleOutput += `  Min: ${summary.metrics.latency.min.toFixed(0)}ms\n`;
    consoleOutput += `  Avg: ${summary.metrics.latency.avg.toFixed(0)}ms\n`;
    consoleOutput += `  p50: ${summary.metrics.latency.p50.toFixed(0)}ms ${summary.nfrValidation.NFR_01_p50_under_500ms !== false ? '✓' : '✗'}\n`;
    consoleOutput += `  p95: ${summary.metrics.latency.p95.toFixed(0)}ms ${summary.nfrValidation.NFR_01_p95_under_1000ms !== false ? '✓' : '✗'}\n`;
    consoleOutput += `  p99: ${summary.metrics.latency.p99.toFixed(0)}ms\n`;
    consoleOutput += `  Max: ${summary.metrics.latency.max.toFixed(0)}ms\n\n`;

    consoleOutput += 'NFR Validation:\n';
    consoleOutput += `  NFR-01 p50 < 500ms:   ${summary.nfrValidation['NFR-01_p50_under_500ms'] ? '✅ PASS' : '❌ FAIL'} (actual: ${summary.metrics.latency.p50.toFixed(0)}ms)\n`;
    consoleOutput += `  NFR-01 p95 < 1000ms:  ${summary.nfrValidation['NFR-01_p95_under_1000ms'] ? '✅ PASS' : '❌ FAIL'} (actual: ${summary.metrics.latency.p95.toFixed(0)}ms)\n`;
    consoleOutput += `  NFR-02 50 concurrent: ${summary.nfrValidation['NFR-02_50_concurrent'] ? '✅ PASS' : '❌ FAIL'} (failures: ${summary.metrics.failedSearches})\n\n`;

    consoleOutput += '='.repeat(70) + '\n';
    consoleOutput += `OVERALL: ${summary.overallResult}\n`;
    consoleOutput += '='.repeat(70) + '\n';

    return {
        'stdout': consoleOutput,
        'semantic-search-results.json': JSON.stringify(summary, null, 2)
    };
}
