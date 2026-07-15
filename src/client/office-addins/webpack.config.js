const path = require('path');
const HtmlWebpackPlugin = require('html-webpack-plugin');
const CopyWebpackPlugin = require('copy-webpack-plugin');
const MiniCssExtractPlugin = require('mini-css-extract-plugin');
const webpack = require('webpack');
const devCerts = require('office-addin-dev-certs');
require('dotenv').config({ path: path.resolve(__dirname, '.env') });

const isProduction = process.env.NODE_ENV === 'production';

// Build date for version display
const BUILD_DATE = new Date().toLocaleDateString('en-US', {
  year: 'numeric',
  month: 'short',
  day: 'numeric',
});

// Environment variables for add-in configuration
// All values REQUIRED — no dev-specific fallbacks.
// Set in .env (local dev) or CI/CD pipeline environment variables.
const REQUIRED_ENV_VARS = ['ADDIN_CLIENT_ID', 'TENANT_ID', 'BFF_API_CLIENT_ID', 'BFF_API_BASE_URL'];
const missingVars = REQUIRED_ENV_VARS.filter((v) => !process.env[v]);
if (missingVars.length > 0) {
  throw new Error(
    `[Office Add-in Webpack] Missing required environment variables: ${missingVars.join(', ')}.\n` +
      `Copy .env.example to .env and set all values, or provide them via CI/CD pipeline.`
  );
}

const ENV_CONFIG = {
  ADDIN_CLIENT_ID: process.env.ADDIN_CLIENT_ID,
  TENANT_ID: process.env.TENANT_ID,
  BFF_API_CLIENT_ID: process.env.BFF_API_CLIENT_ID,
  BFF_API_BASE_URL: process.env.BFF_API_BASE_URL,
  // Optional: SmartTodo Code Page URL (smart-todo-decoupling-r3 FR-27 / task 070).
  // When set, the Outlook "Create To Do" ribbon opens the wizard from this URL
  // with launch-context query params. When unset, the ribbon action is hidden
  // / inert. Documented in .env.example.
  SMARTTODO_CODEPAGE_URL: process.env.SMARTTODO_CODEPAGE_URL || '',
  // Optional: Dataverse org URL for the SaveView "Quick Create" deep link
  // (email-communication-solution-r4 task 072 / FR-25). Config-driven, no
  // hardcoded org — when unset, Quick Create degrades to a no-op.
  ORG_URL: process.env.ORG_URL || '',
  // Optional: fallback MSAL popup redirect URI used only when the Office host
  // does not support NAA (`OfficeNaaStrategy`'s legacy-client fallback path).
  // Defaults to `${origin}/auth-callback.html` inside AuthService when unset.
  FALLBACK_REDIRECT_URI: process.env.FALLBACK_REDIRECT_URI || '',
  // Base URL the unified manifest's runtime/ribbon `code.page` + icon URLs point
  // at. Dev defaults to the local webpack-dev-server origin; production defaults
  // to the deployed Azure Static Web App (office-addins-deploy skill resource
  // reference) — override via ADDIN_BASE_URL for a custom domain.
  ADDIN_BASE_URL:
    process.env.ADDIN_BASE_URL ||
    (isProduction ? 'https://icy-desert-0bfdbb61e.6.azurestaticapps.net' : 'https://localhost:3000'),
};

async function getHttpsOptions() {
  if (isProduction) {
    return undefined;
  }
  const httpsOptions = await devCerts.getHttpsServerOptions();
  return {
    ca: httpsOptions.ca,
    key: httpsOptions.key,
    cert: httpsOptions.cert,
  };
}

module.exports = async (env, options) => {
  const mode = options.mode || 'development';
  const addin = env?.addin || 'outlook'; // Default to outlook

  return {
    mode,
    devtool: mode === 'production' ? 'source-map' : 'eval-source-map',
    entry: {
      // Outlook taskpane
      'outlook/taskpane': './outlook/taskpane/index.tsx',
      // Word taskpane
      'word/taskpane': './word/taskpane/index.tsx',
      // Commands (function files)
      'outlook/commands': './outlook/commands/index.ts',
      'word/commands': './word/commands/index.ts',
    },
    output: {
      path: path.resolve(__dirname, 'dist'),
      filename: '[name].bundle.js',
      clean: true,
    },
    resolve: {
      extensions: ['.ts', '.tsx', '.js', '.jsx'],
      alias: {
        '@shared': path.resolve(__dirname, 'shared'),
        '@outlook': path.resolve(__dirname, 'outlook'),
        '@word': path.resolve(__dirname, 'word'),
      },
    },
    module: {
      rules: [
        {
          test: /\.tsx?$/,
          use: {
            loader: 'ts-loader',
            options: {
              transpileOnly: true, // Skip type checking during build
            },
          },
          exclude: /node_modules/,
        },
        {
          test: /\.css$/,
          use: [
            mode === 'production' ? MiniCssExtractPlugin.loader : 'style-loader',
            'css-loader',
          ],
        },
        {
          test: /\.(png|jpg|jpeg|gif|svg|ico)$/,
          type: 'asset/resource',
          generator: {
            filename: 'assets/[name][ext]',
          },
        },
      ],
    },
    plugins: [
      // Outlook taskpane HTML
      new HtmlWebpackPlugin({
        template: './outlook/taskpane/taskpane.html',
        filename: 'outlook/taskpane.html',
        chunks: ['outlook/taskpane'],
      }),
      // Outlook taskpane TEST HTML (for browser testing with mock Office.js)
      new HtmlWebpackPlugin({
        template: './outlook/taskpane/taskpane-test.html',
        filename: 'outlook/taskpane-test.html',
        chunks: ['outlook/taskpane'],
      }),
      // Word taskpane HTML
      new HtmlWebpackPlugin({
        template: './word/taskpane/taskpane.html',
        filename: 'word/taskpane.html',
        chunks: ['word/taskpane'],
      }),
      // Outlook commands HTML
      new HtmlWebpackPlugin({
        template: './outlook/commands/commands.html',
        filename: 'outlook/commands.html',
        chunks: ['outlook/commands'],
      }),
      // Word commands HTML
      new HtmlWebpackPlugin({
        template: './word/commands/commands.html',
        filename: 'word/commands.html',
        chunks: ['word/commands'],
      }),
      // Copy manifests and assets
      new CopyWebpackPlugin({
        patterns: [
          { from: './public/index.html', to: 'index.html' },
          {
            // OfficeNaaStrategy's legacy-client fallback MSAL popup redirect target
            // (replaces the deprecated self-built `auth-dialog.html` — task 072).
            // Static, config-free — see the file's own header comment for why it
            // deliberately does NOT instantiate its own MSAL client.
            from: './public/auth-callback.html',
            to: 'auth-callback.html',
          },
          {
            // Unified JSON manifest (email-communication-solution-r4 task 072 / FR-25) —
            // single source of truth for BOTH dev and production builds. Retires the
            // divergent `outlook-manifest.xml` (XML v1.0.19) + orphaned `manifest.prod.json`
            // (never referenced by this build). Parameterize app IDs + base URL at build time.
            from: './outlook/manifest.json',
            to: 'outlook/manifest.json',
            transform: (content) => {
              let manifest = content.toString();
              // Replace hardcoded app ID in "id" and "webApplicationInfo.id"
              manifest = manifest.replace(
                /"id":\s*"c1258e2d-1688-49d2-ac99-a7485ebd9995"/g,
                `"id": "${ENV_CONFIG.ADDIN_CLIENT_ID}"`
              );
              // Replace hardcoded resource URI (api://{BFF_API_CLIENT_ID})
              manifest = manifest.replace(
                /"resource":\s*"api:\/\/[a-f0-9-]+"/,
                `"resource": "api://${ENV_CONFIG.BFF_API_CLIENT_ID}"`
              );
              // Replace the manifest's dev-authored base URL with the resolved
              // per-mode ADDIN_BASE_URL (localhost for dev, deployed SWA for prod).
              manifest = manifest.split('https://localhost:3000').join(ENV_CONFIG.ADDIN_BASE_URL);
              return manifest;
            },
          },
          {
            from: './word/word-manifest.xml',
            to: 'word/manifest.xml'
          },
          { from: './shared/assets', to: 'assets', noErrorOnMissing: true },
          // Mock Office.js for browser testing
          { from: './outlook/taskpane/mock-office.js', to: 'outlook/mock-office.js', noErrorOnMissing: true },
          { from: './staticwebapp.config.json', to: 'staticwebapp.config.json', noErrorOnMissing: true },
        ],
      }),
      // Define environment variables for client-side code
      new webpack.DefinePlugin({
        'process.env.ADDIN_CLIENT_ID': JSON.stringify(ENV_CONFIG.ADDIN_CLIENT_ID),
        'process.env.TENANT_ID': JSON.stringify(ENV_CONFIG.TENANT_ID),
        'process.env.BFF_API_CLIENT_ID': JSON.stringify(ENV_CONFIG.BFF_API_CLIENT_ID),
        'process.env.BFF_API_BASE_URL': JSON.stringify(ENV_CONFIG.BFF_API_BASE_URL),
        'process.env.SMARTTODO_CODEPAGE_URL': JSON.stringify(ENV_CONFIG.SMARTTODO_CODEPAGE_URL),
        'process.env.ORG_URL': JSON.stringify(ENV_CONFIG.ORG_URL),
        'process.env.FALLBACK_REDIRECT_URI': JSON.stringify(ENV_CONFIG.FALLBACK_REDIRECT_URI),
        'process.env.BUILD_DATE': JSON.stringify(BUILD_DATE),
      }),
      ...(mode === 'production'
        ? [
            new MiniCssExtractPlugin({
              filename: '[name].css',
            }),
          ]
        : []),
    ],
    devServer: {
      static: {
        directory: path.join(__dirname, 'dist'),
      },
      port: 3000,
      https: await getHttpsOptions(),
      headers: {
        'Access-Control-Allow-Origin': '*',
      },
      hot: true,
      allowedHosts: 'all',
    },
    optimization: {
      splitChunks: {
        chunks: 'all',
        cacheGroups: {
          vendor: {
            test: /[\\/]node_modules[\\/]/,
            name: 'vendors',
            chunks: 'all',
          },
        },
      },
    },
  };
};
