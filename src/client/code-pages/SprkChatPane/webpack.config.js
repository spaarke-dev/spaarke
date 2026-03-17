const path = require('path');
const webpack = require('webpack');
const TerserPlugin = require('terser-webpack-plugin');
const fs = require('fs');

// Read .env.production for build-time env vars (VITE_BFF_BASE_URL, VITE_MSAL_CLIENT_ID)
function loadEnvFile() {
  const envFile = path.resolve(__dirname, '.env.production');
  const vars = {};
  if (fs.existsSync(envFile)) {
    fs.readFileSync(envFile, 'utf-8').split('\n').forEach(line => {
      const match = line.match(/^(\w+)=(.*)$/);
      if (match) vars[match[1]] = match[2].trim();
    });
  }
  return vars;
}

module.exports = (env) => {
  const isAnalyze = env?.analyze;
  const envVars = loadEnvFile();

  const config = {
    mode: 'production',
    entry: './src/index.tsx',
    output: {
      filename: 'bundle.js',
      path: path.resolve(__dirname, 'out'),
      publicPath: '',
      clean: true,
    },
    resolve: {
      extensions: ['.ts', '.tsx', '.js', '.jsx'],
      alias: {
        // Resolve workspace dependencies to shared library source for bundling
        '@spaarke/ui-components': path.resolve(__dirname, '../../shared/Spaarke.UI.Components/src'),
        '@spaarke/auth': path.resolve(__dirname, '../../shared/Spaarke.Auth/src'),
      },
      modules: [
        path.resolve(__dirname, 'node_modules'),
        'node_modules',
      ],
    },
    module: {
      rules: [
        {
          test: /\.tsx?$/,
          use: {
            loader: 'esbuild-loader',
            options: {
              target: 'es2020',
              jsx: 'automatic',
            },
          },
          // Exclude node_modules EXCEPT the shared library source (resolved via alias)
          exclude: [
            /node_modules/,
            /__tests__/,
            /\.test\.tsx?$/,
            /\.spec\.tsx?$/,
          ],
          include: [
            path.resolve(__dirname, 'src'),
            path.resolve(__dirname, '../../shared/Spaarke.UI.Components/src'),
            path.resolve(__dirname, '../../shared/Spaarke.Auth/src'),
          ],
        },
        {
          // Fix ESM .mjs files (e.g. @lexical/react) that use bare specifiers
          // like 'react/jsx-runtime' without file extension
          test: /\.m?js$/,
          resolve: {
            fullySpecified: false,
          },
        },
      ],
    },
    optimization: {
      minimize: true,
      minimizer: [
        new TerserPlugin({
          terserOptions: {
            compress: {
              drop_console: true,
              drop_debugger: true,
              pure_funcs: ['console.debug', 'console.info'],
              passes: 2,
            },
            mangle: {
              safari10: true,
            },
            output: {
              comments: false,
            },
          },
          extractComments: false,
        }),
      ],
      usedExports: true,
      sideEffects: true,
      splitChunks: false,
    },
    // NO externals — Code Pages bundle everything (unlike PCF platform library approach)
    devServer: {
      static: path.resolve(__dirname),
      port: 3003,
      hot: true,
    },
    plugins: [
      new webpack.optimize.LimitChunkCountPlugin({ maxChunks: 1 }),
      // Inject Vite-style import.meta.env variables for bffConfig.ts / msalConfig.ts
      new webpack.DefinePlugin({
        'import.meta.env.VITE_BFF_BASE_URL': JSON.stringify(envVars.VITE_BFF_BASE_URL || ''),
        'import.meta.env.VITE_MSAL_CLIENT_ID': JSON.stringify(envVars.VITE_MSAL_CLIENT_ID || ''),
      }),
    ],
  };

  // Optional bundle analyzer for `npm run build:analyze`
  if (isAnalyze) {
    const { BundleAnalyzerPlugin } = require('webpack-bundle-analyzer');
    config.plugins.push(new BundleAnalyzerPlugin());
  }

  return config;
};
