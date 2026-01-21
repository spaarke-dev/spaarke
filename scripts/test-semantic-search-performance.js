/**
 * Semantic Search API - Performance Validation Script
 *
 * Tests NFR-01 and NFR-02 from spec.md:
 * - NFR-01: Search latency p50 < 500ms, p95 < 1000ms
 * - NFR-02: Support 50 concurrent searches
 *
 * Prerequisites:
 * - Node.js 18+ installed
 * - Access token for BFF API (set via environment variable)
 * - API deployed and accessible
 *
 * Usage:
 *   # Set environment variables
 *   export BFF_API_URL="https://spe-api-dev-67e2xz.azurewebsites.net"
 *   export ACCESS_TOKEN="your-bearer-token"
 *   export TENANT_ID="your-tenant-id"
 *
 *   # Run test
 *   node scripts/test-semantic-search-performance.js
 */

const https = require('https');
const http = require('http');

// Configuration
const CONFIG = {
  apiUrl: process.env.BFF_API_URL || 'https://spe-api-dev-67e2xz.azurewebsites.net',
  accessToken: process.env.ACCESS_TOKEN,
  tenantId: process.env.TENANT_ID || 'test-tenant-id',

  // Test parameters
  concurrentUsers: parseInt(process.env.CONCURRENT_USERS || '50', 10),
  iterationsPerUser: parseInt(process.env.ITERATIONS || '5', 10),
  warmupRequests: 5,

  // Performance targets (from spec.md)
  targets: {
    p50Ms: 500,
    p95Ms: 1000,
    maxConcurrent: 50
  },

  // Sample search requests for testing
  testQueries: [
    'contracts about payment terms',
    'invoices from 2024',
    'project documentation',
    'client agreements',
    'service level agreement'
  ]
};

// Test entity for scoped search (update with actual test data)
const TEST_ENTITY = {
  type: process.env.TEST_ENTITY_TYPE || 'matter',
  id: process.env.TEST_ENTITY_ID || '00000000-0000-0000-0000-000000000001'
};

/**
 * Makes an HTTP request and measures latency
 */
async function makeRequest(url, options, body) {
  const startTime = Date.now();

  return new Promise((resolve, reject) => {
    const protocol = url.startsWith('https') ? https : http;
    const urlObj = new URL(url);

    const reqOptions = {
      hostname: urlObj.hostname,
      port: urlObj.port || (url.startsWith('https') ? 443 : 80),
      path: urlObj.pathname + urlObj.search,
      method: options.method || 'POST',
      headers: options.headers || {}
    };

    const req = protocol.request(reqOptions, (res) => {
      let data = '';
      res.on('data', chunk => data += chunk);
      res.on('end', () => {
        const elapsed = Date.now() - startTime;
        resolve({
          status: res.statusCode,
          headers: res.headers,
          body: data,
          latencyMs: elapsed
        });
      });
    });

    req.on('error', (error) => {
      const elapsed = Date.now() - startTime;
      reject({ error, latencyMs: elapsed });
    });

    req.on('timeout', () => {
      req.destroy();
      reject({ error: new Error('Request timeout'), latencyMs: Date.now() - startTime });
    });

    req.setTimeout(30000); // 30 second timeout

    if (body) {
      req.write(typeof body === 'string' ? body : JSON.stringify(body));
    }
    req.end();
  });
}

/**
 * Execute a semantic search request
 */
async function executeSearch(query, entityType, entityId) {
  const url = `${CONFIG.apiUrl}/api/ai/search/semantic`;

  const requestBody = {
    query: query,
    scope: 'entity',
    entityType: entityType,
    entityId: entityId,
    options: {
      limit: 20,
      includeHighlights: true,
      hybridMode: 'rrf'
    }
  };

  const headers = {
    'Content-Type': 'application/json',
    'Accept': 'application/json'
  };

  if (CONFIG.accessToken) {
    headers['Authorization'] = `Bearer ${CONFIG.accessToken}`;
  }

  return makeRequest(url, { method: 'POST', headers }, requestBody);
}

/**
 * Calculate percentile from sorted array
 */
function percentile(sortedArr, p) {
  const index = Math.ceil((p / 100) * sortedArr.length) - 1;
  return sortedArr[Math.max(0, index)];
}

/**
 * Run warmup requests to ensure API is ready
 */
async function warmup() {
  console.log(`\n[Warmup] Sending ${CONFIG.warmupRequests} warmup requests...`);

  for (let i = 0; i < CONFIG.warmupRequests; i++) {
    try {
      const query = CONFIG.testQueries[i % CONFIG.testQueries.length];
      await executeSearch(query, TEST_ENTITY.type, TEST_ENTITY.id);
      process.stdout.write('.');
    } catch (error) {
      process.stdout.write('x');
    }
  }
  console.log(' Done');

  // Brief pause after warmup
  await new Promise(resolve => setTimeout(resolve, 1000));
}

/**
 * Run load test with specified concurrency
 */
async function runLoadTest(concurrentUsers, iterationsPerUser) {
  console.log(`\n[Load Test] ${concurrentUsers} concurrent users, ${iterationsPerUser} iterations each`);
  console.log(`[Load Test] Total requests: ${concurrentUsers * iterationsPerUser}`);

  const results = {
    latencies: [],
    errors: [],
    successes: 0,
    failures: 0
  };

  // Create user simulation functions
  const userSimulations = [];

  for (let user = 0; user < concurrentUsers; user++) {
    const userTask = async () => {
      const userResults = [];

      for (let iter = 0; iter < iterationsPerUser; iter++) {
        const query = CONFIG.testQueries[(user + iter) % CONFIG.testQueries.length];

        try {
          const result = await executeSearch(query, TEST_ENTITY.type, TEST_ENTITY.id);

          if (result.status >= 200 && result.status < 300) {
            userResults.push({ latency: result.latencyMs, success: true });
          } else {
            userResults.push({ latency: result.latencyMs, success: false, status: result.status });
          }
        } catch (error) {
          userResults.push({ latency: error.latencyMs || 30000, success: false, error: error.error?.message });
        }

        // Small delay between requests from same user
        await new Promise(resolve => setTimeout(resolve, 100));
      }

      return userResults;
    };

    userSimulations.push(userTask);
  }

  // Execute all users concurrently
  const startTime = Date.now();
  const allResults = await Promise.all(userSimulations.map(fn => fn()));
  const totalDuration = Date.now() - startTime;

  // Aggregate results
  for (const userResults of allResults) {
    for (const result of userResults) {
      if (result.success) {
        results.successes++;
        results.latencies.push(result.latency);
      } else {
        results.failures++;
        results.errors.push(result);
      }
    }
  }

  // Calculate statistics
  results.latencies.sort((a, b) => a - b);

  return {
    totalRequests: concurrentUsers * iterationsPerUser,
    successes: results.successes,
    failures: results.failures,
    totalDurationMs: totalDuration,
    requestsPerSecond: (concurrentUsers * iterationsPerUser) / (totalDuration / 1000),
    latencyStats: results.latencies.length > 0 ? {
      min: results.latencies[0],
      max: results.latencies[results.latencies.length - 1],
      avg: Math.round(results.latencies.reduce((a, b) => a + b, 0) / results.latencies.length),
      p50: percentile(results.latencies, 50),
      p95: percentile(results.latencies, 95),
      p99: percentile(results.latencies, 99)
    } : null,
    errors: results.errors.slice(0, 10) // First 10 errors
  };
}

/**
 * Verify API is accessible
 */
async function checkApiHealth() {
  console.log(`\n[Health Check] Testing API at ${CONFIG.apiUrl}`);

  try {
    const result = await makeRequest(
      `${CONFIG.apiUrl}/healthz`,
      { method: 'GET', headers: { 'Accept': 'application/json' } }
    );

    if (result.status === 200) {
      console.log(`[Health Check] API is healthy (${result.latencyMs}ms)`);
      return true;
    } else {
      console.log(`[Health Check] API returned status ${result.status}`);
      return false;
    }
  } catch (error) {
    console.error(`[Health Check] Failed to reach API: ${error.error?.message}`);
    return false;
  }
}

/**
 * Main test execution
 */
async function main() {
  console.log('='.repeat(70));
  console.log('Semantic Search API - Performance Validation');
  console.log('='.repeat(70));

  console.log('\nConfiguration:');
  console.log(`  API URL: ${CONFIG.apiUrl}`);
  console.log(`  Concurrent Users: ${CONFIG.concurrentUsers}`);
  console.log(`  Iterations per User: ${CONFIG.iterationsPerUser}`);
  console.log(`  Test Entity: ${TEST_ENTITY.type}/${TEST_ENTITY.id}`);
  console.log(`  Auth Token: ${CONFIG.accessToken ? 'Configured' : 'NOT CONFIGURED'}`);

  console.log('\nPerformance Targets (from spec.md):');
  console.log(`  p50 Latency: < ${CONFIG.targets.p50Ms}ms`);
  console.log(`  p95 Latency: < ${CONFIG.targets.p95Ms}ms`);
  console.log(`  Concurrent Users: ${CONFIG.targets.maxConcurrent}`);

  // Pre-flight checks
  if (!CONFIG.accessToken) {
    console.warn('\n[Warning] ACCESS_TOKEN not set. Requests will likely fail with 401.');
    console.log('Set via: export ACCESS_TOKEN="your-bearer-token"');
  }

  // Health check
  const healthy = await checkApiHealth();
  if (!healthy) {
    console.error('\n[Error] API health check failed. Aborting tests.');
    console.log('Ensure API is deployed and accessible.');
    process.exit(1);
  }

  // Warmup
  await warmup();

  // Run load tests with increasing concurrency
  const testScenarios = [
    { users: 10, iterations: 5, label: 'Light Load (10 users)' },
    { users: 25, iterations: 5, label: 'Medium Load (25 users)' },
    { users: 50, iterations: 5, label: 'Target Load (50 users)' }
  ];

  const allResults = [];

  for (const scenario of testScenarios) {
    console.log(`\n${'='.repeat(70)}`);
    console.log(`Scenario: ${scenario.label}`);
    console.log('='.repeat(70));

    const results = await runLoadTest(scenario.users, scenario.iterations);
    allResults.push({ scenario: scenario.label, ...results });

    // Print results
    console.log('\nResults:');
    console.log(`  Total Requests: ${results.totalRequests}`);
    console.log(`  Successes: ${results.successes}`);
    console.log(`  Failures: ${results.failures}`);
    console.log(`  Total Duration: ${results.totalDurationMs}ms`);
    console.log(`  Throughput: ${results.requestsPerSecond.toFixed(2)} req/s`);

    if (results.latencyStats) {
      console.log('\nLatency Statistics:');
      console.log(`  Min: ${results.latencyStats.min}ms`);
      console.log(`  Max: ${results.latencyStats.max}ms`);
      console.log(`  Avg: ${results.latencyStats.avg}ms`);
      console.log(`  p50: ${results.latencyStats.p50}ms ${results.latencyStats.p50 < CONFIG.targets.p50Ms ? '✅ PASS' : '❌ FAIL'}`);
      console.log(`  p95: ${results.latencyStats.p95}ms ${results.latencyStats.p95 < CONFIG.targets.p95Ms ? '✅ PASS' : '❌ FAIL'}`);
      console.log(`  p99: ${results.latencyStats.p99}ms`);
    }

    if (results.errors.length > 0) {
      console.log(`\nSample Errors (first ${results.errors.length}):`);
      results.errors.forEach((err, i) => {
        console.log(`  ${i + 1}. ${err.error || `HTTP ${err.status}`}`);
      });
    }

    // Brief pause between scenarios
    await new Promise(resolve => setTimeout(resolve, 2000));
  }

  // Final summary
  console.log(`\n${'='.repeat(70)}`);
  console.log('PERFORMANCE VALIDATION SUMMARY');
  console.log('='.repeat(70));

  const targetResult = allResults.find(r => r.scenario.includes('50 users'));

  if (targetResult && targetResult.latencyStats) {
    const p50Pass = targetResult.latencyStats.p50 < CONFIG.targets.p50Ms;
    const p95Pass = targetResult.latencyStats.p95 < CONFIG.targets.p95Ms;
    const concurrencyPass = targetResult.failures === 0;

    console.log(`\nTarget Load (50 concurrent users):`);
    console.log(`  NFR-01 p50 < 500ms: ${p50Pass ? '✅ PASS' : '❌ FAIL'} (actual: ${targetResult.latencyStats.p50}ms)`);
    console.log(`  NFR-01 p95 < 1000ms: ${p95Pass ? '✅ PASS' : '❌ FAIL'} (actual: ${targetResult.latencyStats.p95}ms)`);
    console.log(`  NFR-02 50 concurrent: ${concurrencyPass ? '✅ PASS' : '❌ FAIL'} (failures: ${targetResult.failures})`);

    console.log(`\nOverall: ${p50Pass && p95Pass && concurrencyPass ? '✅ ALL TARGETS MET' : '❌ SOME TARGETS MISSED'}`);

    // Return exit code based on results
    process.exit(p50Pass && p95Pass && concurrencyPass ? 0 : 1);
  } else {
    console.log('\n[Warning] Could not complete target load test. Check errors above.');
    process.exit(1);
  }
}

// Run if executed directly
if (require.main === module) {
  main().catch(error => {
    console.error('Unexpected error:', error);
    process.exit(1);
  });
}

module.exports = { runLoadTest, executeSearch, CONFIG };
