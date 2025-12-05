import { defineConfig, devices } from '@playwright/test';
import * as dotenv from 'dotenv';
import * as path from 'path';

// Load environment variables
dotenv.config({ path: path.resolve(__dirname, '.env') });

export default defineConfig({
  testDir: '../specs',

  // Reusable settings for all PCF tests
  timeout: 60000,
  expect: { timeout: 10000 },

  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: process.env.CI ? 1 : undefined,

  reporter: [
    ['html', { outputFolder: 'playwright-report' }],
    ['json', { outputFile: 'test-results.json' }],
    ['junit', { outputFile: 'junit.xml' }],
    ['list']
  ],

  use: {
    baseURL: process.env.POWER_APPS_URL || 'https://make.powerapps.com',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',

    // Power Apps requires longer timeouts
    actionTimeout: 15000,
    navigationTimeout: 30000
  },

  projects: [
    // Primary browser for Dataverse/Power Apps
    {
      name: 'edge',
      use: { channel: 'msedge' }
    },
    // Secondary browsers
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] }
    },
    {
      name: 'firefox',
      use: { ...devices['Desktop Firefox'] }
    },
    {
      name: 'webkit',
      use: { ...devices['Desktop Safari'] }
    }
  ]
});
