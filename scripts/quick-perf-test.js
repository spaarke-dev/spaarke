// Quick performance baseline test for unauthenticated endpoints
const https = require('https');

const API_URL = 'https://spe-api-dev-67e2xz.azurewebsites.net';

async function measureLatency(url, name) {
    const results = [];
    
    for (let i = 0; i < 10; i++) {
        const start = Date.now();
        await new Promise((resolve, reject) => {
            const req = https.get(url, (res) => {
                res.on('data', () => {});
                res.on('end', () => resolve(res.statusCode));
            });
            req.on('error', reject);
            req.setTimeout(10000);
        });
        results.push(Date.now() - start);
    }
    
    results.sort((a, b) => a - b);
    const p50 = results[Math.floor(results.length * 0.5)];
    const p95 = results[Math.floor(results.length * 0.95)];
    const avg = Math.round(results.reduce((a, b) => a + b, 0) / results.length);
    
    console.log(`${name}:`);
    console.log(`  Avg: ${avg}ms | p50: ${p50}ms | p95: ${p95}ms | Min: ${results[0]}ms | Max: ${results[results.length-1]}ms`);
    return { name, avg, p50, p95 };
}

async function main() {
    console.log('='.repeat(60));
    console.log('Quick Performance Baseline Test');
    console.log('API: ' + API_URL);
    console.log('='.repeat(60));
    console.log('');
    
    const endpoints = [
        { url: `${API_URL}/ping`, name: '/ping' },
        { url: `${API_URL}/healthz`, name: '/healthz' }
    ];
    
    for (const ep of endpoints) {
        await measureLatency(ep.url, ep.name);
    }
    
    console.log('');
    console.log('Note: Semantic search endpoint requires authentication.');
    console.log('Run k6 test with TOKEN env var for full performance validation.');
}

main().catch(console.error);
