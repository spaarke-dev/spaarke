import { TOKEN_EXPIRY_BUFFER_MS } from '../config';
/** In-memory token cache strategy (fastest, no I/O). */
export class CacheStrategy {
    constructor() {
        this.name = 'cache';
        this._entry = null;
    }
    async tryAcquireToken() {
        if (!this._entry)
            return null;
        if (Date.now() >= this._entry.expiresOn - TOKEN_EXPIRY_BUFFER_MS) {
            this._entry = null;
            return null;
        }
        return {
            accessToken: this._entry.accessToken,
            expiresOn: this._entry.expiresOn,
            source: 'cache',
        };
    }
    /** Store a token in the cache. Called after any successful acquisition. */
    store(token, expiresOn) {
        this._entry = { accessToken: token, expiresOn };
    }
    /** Clear the cache (e.g., on 401 retry). */
    clear() {
        this._entry = null;
    }
    /** Return the raw cached token string, or null if empty/expired. */
    getCachedToken() {
        if (!this._entry)
            return null;
        if (Date.now() >= this._entry.expiresOn - TOKEN_EXPIRY_BUFFER_MS)
            return null;
        return this._entry.accessToken;
    }
}
//# sourceMappingURL=CacheStrategy.js.map