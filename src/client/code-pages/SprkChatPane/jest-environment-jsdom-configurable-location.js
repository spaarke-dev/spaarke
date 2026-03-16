/**
 * Custom Jest environment that extends jest-environment-jsdom to make
 * window.location mockable by tests.
 *
 * Jest 30 + jsdom marks window.location as non-configurable, which prevents
 * standard mocking techniques. This environment exposes a reconfigureUrl()
 * method on the global scope that uses jsdom's internal reconfigure API.
 */
const { TestEnvironment: JsdomEnvironment } = require('jest-environment-jsdom');

class JsdomEnvironmentWithConfigurableLocation extends JsdomEnvironment {
  async setup() {
    await super.setup();

    // Expose a helper function that uses jsdom's reconfigure API to change the URL.
    // This is the only reliable way to change window.location.href in jsdom.
    const dom = this.dom;
    this.global.__setWindowLocationHref = (url) => {
      dom.reconfigure({ url });
    };
  }
}

module.exports = JsdomEnvironmentWithConfigurableLocation;
