const path = require('path');
const TerserPlugin = require('terser-webpack-plugin');

module.exports = (env) => {
  const isAnalyze = env?.analyze;

  const config = {
    mode: 'production',
    entry: './src/index.tsx',
    output: {
      filename: 'bundle.js',
      path: path.resolve(__dirname, 'out'),
      clean: true,
    },
    resolve: {
      extensions: ['.ts', '.tsx', '.js', '.jsx'],
      alias: {
        // Resolve workspace dependency to shared library source for bundling
        '@spaarke/ui-components': path.resolve(__dirname, '../../shared/Spaarke.UI.Components/src'),
      },
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
    },
    // NO externals — Code Pages bundle everything (unlike PCF platform library approach)
    devServer: {
      static: path.resolve(__dirname),
      port: 3003,
      hot: true,
    },
    plugins: [],
  };

  // Optional bundle analyzer for `npm run build:analyze`
  if (isAnalyze) {
    const { BundleAnalyzerPlugin } = require('webpack-bundle-analyzer');
    config.plugins.push(new BundleAnalyzerPlugin());
  }

  return config;
};
