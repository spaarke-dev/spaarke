const path = require('path');

module.exports = {
  mode: 'production',
  entry: './SemanticSearchControl/index.ts',
  output: {
    filename: 'bundle.js',
    path: path.resolve(__dirname, 'out/controls/SemanticSearchControl'),
    library: {
      type: 'var',
      name: 'SemanticSearchControl'
    }
  },
  resolve: {
    extensions: ['.ts', '.tsx', '.js', '.jsx']
  },
  module: {
    rules: [
      {
        test: /\.tsx?$/,
        use: 'ts-loader',
        exclude: /node_modules/
      }
    ]
  },
  externals: {
    'react': 'React',
    'react-dom': 'ReactDOM'
  },
  optimization: {
    usedExports: true,
    sideEffects: true
  }
};
