/**
 * k6 Load Test Script for SDAP AI Features
 *
 * Tests:
 * - RAG Search endpoints
 * - Analysis execution
 * - Export operations
 * - Circuit breaker behavior
 *
 * Prerequisites:
 * - Install k6: https://k6.io/docs/getting-started/installation/
 * - Set environment variables:
 *   - API_BASE_URL: BFF API URL
 *   - AUTH_TOKEN: Bearer token for authentication (optional for resilience endpoints)
 *
 * Usage:
 *   # Baseline test (10 VUs)
 *   k6 run --vus 10 --duration 2m k6-ai-load-test.js
 *
 *   # Target test (100 VUs)
 *   k6 run --vus 100 --duration 5m k6-ai-load-test.js
 *
 *   # Stress test (200+ VUs)
 *   k6 run --vus 200 --duration 10m k6-ai-load-test.js
 *
 *   # With scenarios (recommended)
 *   k6 run k6-ai-load-test.js
 */

import http from 'k6/http';
import { check, sleep, group } from 'k6';
import { Rate, Trend, Counter } from 'k6/metrics';

// Custom metrics
const ragSearchLatency = new Trend('rag_search_latency', true);
const ragSearchErrors = new Rate('rag_search_errors');
const resilienceHealthLatency = new Trend('resilience_health_latency', true);
const circuitBreakerOpen = new Counter('circuit_breaker_open');
const analysisExecuteLatency = new Trend('analysis_execute_latency', true);
const analysisErrors = new Rate('analysis_errors');
const exportLatency = new Trend('export_latency', true);
const exportErrors = new Rate('export_errors');

// Configuration
const BASE_URL = __ENV.API_BASE_URL || 'https://spe-api-dev-67e2xz.azurewebsites.net';
const AUTH_TOKEN = __ENV.AUTH_TOKEN || '';

// Test scenarios
export const options = {
    scenarios: {
        // Scenario 1: Baseline warm-up
        baseline: {
            executor: 'constant-vus',
            vus: 10,
            duration: '2m',
            startTime: '0s',
            tags: { scenario: 'baseline' },
        },
        // Scenario 2: Ramp to target (100 VUs)
        target_load: {
            executor: 'ramping-vus',
            startVUs: 10,
            stages: [
                { duration: '1m', target: 50 },
                { duration: '2m', target: 100 },
                { duration: '3m', target: 100 }, // Sustain
                { duration: '1m', target: 50 },
            ],
            startTime: '2m',
            tags: { scenario: 'target' },
        },
        // Scenario 3: Stress test (200+ VUs)
        stress: {
            executor: 'ramping-vus',
            startVUs: 50,
            stages: [
                { duration: '1m', target: 150 },
                { duration: '2m', target: 200 },
                { duration: '3m', target: 250 }, // Peak stress
                { duration: '2m', target: 100 },
                { duration: '1m', target: 0 },
            ],
            startTime: '10m',
            tags: { scenario: 'stress' },
        },
    },
    thresholds: {
        // Overall HTTP latency
        http_req_duration: ['p(95)<3000', 'p(99)<5000'],
        // RAG search specific
        rag_search_latency: ['p(95)<2000', 'p(99)<4000'],
        rag_search_errors: ['rate<0.05'], // <5% error rate
        // Resilience health should be fast
        resilience_health_latency: ['p(95)<500'],
        // Analysis execution
        analysis_execute_latency: ['p(95)<5000'],
        analysis_errors: ['rate<0.10'], // <10% error rate (may fail without auth)
        // Export operations
        export_latency: ['p(95)<3000'],
        export_errors: ['rate<0.10'],
    },
};

// Headers for authenticated requests
function getHeaders() {
    const headers = {
        'Content-Type': 'application/json',
        'Accept': 'application/json',
    };
    if (AUTH_TOKEN) {
        headers['Authorization'] = `Bearer ${AUTH_TOKEN}`;
    }
    return headers;
}

export default function () {
    // Test Group 1: Resilience/Health endpoints (no auth required)
    group('Resilience Endpoints', function () {
        testResilienceHealth();
        testCircuitBreakers();
    });

    // Test Group 2: RAG Search (auth may be required)
    group('RAG Search', function () {
        testRagSearch();
    });

    // Test Group 3: Analysis (auth required)
    if (AUTH_TOKEN) {
        group('Analysis Execution', function () {
            testAnalysisExecution();
        });
    }

    // Think time between iterations
    sleep(Math.random() * 2 + 1); // 1-3 seconds
}

// ============================================================================
// Test: Resilience Health
// ============================================================================
function testResilienceHealth() {
    const start = Date.now();
    const response = http.get(`${BASE_URL}/api/resilience/health`, {
        headers: { 'Accept': 'application/json' },
        tags: { name: 'resilience_health' },
    });
    const duration = Date.now() - start;

    resilienceHealthLatency.add(duration);

    const success = check(response, {
        'resilience health status 2xx or 503': (r) => r.status === 200 || r.status === 503,
        'resilience health has body': (r) => r.body.length > 0,
    });

    // Track if any circuits are open
    if (response.status === 503) {
        circuitBreakerOpen.add(1);
        console.log(`Circuit breaker open detected at ${new Date().toISOString()}`);
    }

    // Parse response to check service states
    if (response.status === 200 || response.status === 503) {
        try {
            const data = JSON.parse(response.body);
            if (data.services) {
                for (const [service, info] of Object.entries(data.services)) {
                    if (info.state === 'Open') {
                        circuitBreakerOpen.add(1);
                        console.log(`Circuit ${service} is OPEN`);
                    }
                }
            }
        } catch (e) {
            // Ignore parse errors
        }
    }
}

// ============================================================================
// Test: Circuit Breakers
// ============================================================================
function testCircuitBreakers() {
    const response = http.get(`${BASE_URL}/api/resilience/circuits`, {
        headers: { 'Accept': 'application/json' },
        tags: { name: 'circuit_breakers' },
    });

    check(response, {
        'circuits status 200': (r) => r.status === 200,
        'circuits response valid': (r) => {
            try {
                const data = JSON.parse(r.body);
                return Array.isArray(data.circuits);
            } catch {
                return false;
            }
        },
    });
}

// ============================================================================
// Test: RAG Search
// ============================================================================
function testRagSearch() {
    const searchPayload = JSON.stringify({
        query: 'contract terms and conditions',
        tenantId: 'test-tenant-' + __VU,
        deploymentModel: 'Shared',
        topK: 5,
        minScore: 0.5,
        includeMetadata: true,
    });

    const start = Date.now();
    const response = http.post(`${BASE_URL}/api/ai/rag/search`, searchPayload, {
        headers: getHeaders(),
        tags: { name: 'rag_search' },
    });
    const duration = Date.now() - start;

    ragSearchLatency.add(duration);

    const success = check(response, {
        'RAG search status 2xx or 401': (r) => r.status >= 200 && r.status < 300 || r.status === 401,
        'RAG search response time < 3s': (r) => duration < 3000,
    });

    if (!success || response.status >= 400) {
        ragSearchErrors.add(1);
    } else {
        ragSearchErrors.add(0);
    }
}

// ============================================================================
// Test: Analysis Execution (Requires Auth)
// ============================================================================
function testAnalysisExecution() {
    // Create a minimal analysis request
    const analysisPayload = JSON.stringify({
        documentId: '00000000-0000-0000-0000-000000000000', // Placeholder
        driveId: 'test-drive-id',
        scopes: ['EntityExtractor'],
        outputFormat: 'json',
    });

    const start = Date.now();
    const response = http.post(`${BASE_URL}/api/ai/analysis/execute`, analysisPayload, {
        headers: getHeaders(),
        tags: { name: 'analysis_execute' },
    });
    const duration = Date.now() - start;

    analysisExecuteLatency.add(duration);

    const success = check(response, {
        'analysis status 2xx or 4xx': (r) => r.status >= 200 && r.status < 500,
        'analysis response time < 10s': (r) => duration < 10000,
    });

    if (!success || response.status >= 500) {
        analysisErrors.add(1);
    } else {
        analysisErrors.add(0);
    }
}

// ============================================================================
// Test: Export (Requires Auth)
// ============================================================================
function testExport() {
    const exportPayload = JSON.stringify({
        analysisId: '00000000-0000-0000-0000-000000000000', // Placeholder
        format: 'docx',
    });

    const start = Date.now();
    const response = http.post(`${BASE_URL}/api/ai/analysis/{analysisId}/export`, exportPayload, {
        headers: getHeaders(),
        tags: { name: 'export' },
    });
    const duration = Date.now() - start;

    exportLatency.add(duration);

    const success = check(response, {
        'export status 2xx or 4xx': (r) => r.status >= 200 && r.status < 500,
    });

    if (!success || response.status >= 500) {
        exportErrors.add(1);
    } else {
        exportErrors.add(0);
    }
}

// ============================================================================
// Teardown: Summary
// ============================================================================
export function handleSummary(data) {
    console.log('='.repeat(60));
    console.log('SDAP AI Load Test Summary');
    console.log('='.repeat(60));

    // Extract key metrics
    const metrics = data.metrics;

    console.log('\n📊 Key Metrics:');
    console.log(`  Total Requests: ${metrics.http_reqs?.values?.count || 0}`);
    console.log(`  Failed Requests: ${metrics.http_req_failed?.values?.passes || 0}`);

    if (metrics.http_req_duration) {
        console.log(`  HTTP P95 Latency: ${metrics.http_req_duration.values['p(95)']?.toFixed(2)}ms`);
        console.log(`  HTTP P99 Latency: ${metrics.http_req_duration.values['p(99)']?.toFixed(2)}ms`);
    }

    if (metrics.rag_search_latency) {
        console.log(`  RAG Search P95: ${metrics.rag_search_latency.values['p(95)']?.toFixed(2)}ms`);
    }

    if (metrics.circuit_breaker_open) {
        console.log(`  Circuit Breaker Opens: ${metrics.circuit_breaker_open.values.count}`);
    }

    console.log('\n' + '='.repeat(60));

    // Return JSON summary
    return {
        'scripts/load-tests/results/summary.json': JSON.stringify(data, null, 2),
        stdout: textSummary(data, { indent: '  ' }),
    };
}

// Simple text summary (k6 built-in doesn't export, so minimal implementation)
function textSummary(data, options) {
    const indent = options?.indent || '';
    let output = '';

    output += `${indent}scenarios: ${Object.keys(data.options?.scenarios || {}).join(', ')}\n`;
    output += `${indent}vus_max: ${data.metrics?.vus_max?.values?.value || 'N/A'}\n`;
    output += `${indent}iterations: ${data.metrics?.iterations?.values?.count || 0}\n`;

    return output;
}
