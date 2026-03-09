const path = require('path');
const webpack = require('webpack');
const TerserPlugin = require('terser-webpack-plugin');

module.exports = (env) => {
  const isAnalyze = env?.analyze;

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
        '@spaarke/auth': path.resolve(__dirname, '../../shared/Spaarke.Auth/src'),
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
          exclude: [/node_modules/, /__tests__/, /\.test\.tsx?$/, /\.spec\.tsx?$/],
        },
        {
          // CSS imports (library styles)
          test: /\.css$/,
          use: ['style-loader', 'css-loader'],
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
      port: 3002,
      hot: true,
    },
    plugins: [
      new webpack.optimize.LimitChunkCountPlugin({ maxChunks: 1 }),
    ],
  };

  // Optional bundle analyzer for `npm run build:analyze`
  if (isAnalyze) {
    const { BundleAnalyzerPlugin } = require('webpack-bundle-analyzer');
    config.plugins.push(new BundleAnalyzerPlugin());
  }

  return config;
};
