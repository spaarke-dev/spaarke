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
// Defaults are hardcoded as fallback if .env is not loaded
const ENV_CONFIG = {
  ADDIN_CLIENT_ID: process.env.ADDIN_CLIENT_ID || 'c1258e2d-1688-49d2-ac99-a7485ebd9995',
  TENANT_ID: process.env.TENANT_ID || 'a221a95e-6abc-4434-aecc-e48338a1b2f2',
  BFF_API_CLIENT_ID: process.env.BFF_API_CLIENT_ID || '1e40baad-e065-4aea-a8d4-4b7ab273458c',
  BFF_API_BASE_URL: process.env.BFF_API_BASE_URL || 'https://spe-api-dev-67e2xz.azurewebsites.net',
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
            // Use manifest-working.xml for both dev and prod (validated with M365 Admin Center)
            from: mode === 'production' ? './outlook/manifest-working.xml' : './outlook/manifest.json',
            to: mode === 'production' ? 'outlook/manifest.xml' : 'outlook/manifest.json'
          },
          {
            from: mode === 'production' ? './word/manifest-working.xml' : './word/manifest.xml',
            to: 'word/manifest.xml'
          },
          { from: './shared/assets', to: 'assets', noErrorOnMissing: true },
          { from: './staticwebapp.config.json', to: 'staticwebapp.config.json', noErrorOnMissing: true },
        ],
      }),
      // Define environment variables for client-side code
      new webpack.DefinePlugin({
        'process.env.ADDIN_CLIENT_ID': JSON.stringify(ENV_CONFIG.ADDIN_CLIENT_ID),
        'process.env.TENANT_ID': JSON.stringify(ENV_CONFIG.TENANT_ID),
        'process.env.BFF_API_CLIENT_ID': JSON.stringify(ENV_CONFIG.BFF_API_CLIENT_ID),
        'process.env.BFF_API_BASE_URL': JSON.stringify(ENV_CONFIG.BFF_API_BASE_URL),
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
