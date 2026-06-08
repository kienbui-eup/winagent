// In-memory Troly session for the runtime. NOT persisted to disk — the native
// shell owns the app token at rest (DPAPI on Windows); the runtime only caches
// it in RAM to call the Troly backend on the shell's behalf (see ADR-0002).
export function createTrolySession() {
  let session = null; // { appToken, expiresAt, userId, keys, updatedAt }

  const api = {
    set({ appToken, expiresAt = null, userId = null, keys } = {}) {
      if (!appToken) {
        const err = new Error('troly_invalid_request');
        err.data = { reason: 'missing_token' };
        throw err;
      }
      session = {
        appToken,
        expiresAt: expiresAt ?? session?.expiresAt ?? null,
        userId: userId ?? session?.userId ?? null,
        keys: keys !== undefined ? keys : session?.keys ?? null,
        updatedAt: Date.now()
      };
      return session;
    },
    setKeys(keys) {
      const s = api.require();
      s.keys = keys;
      s.updatedAt = Date.now();
      return s;
    },
    get() {
      return session;
    },
    require() {
      if (!session?.appToken) {
        const err = new Error('troly_no_session');
        err.data = { reason: 'not_authenticated' };
        throw err;
      }
      return session;
    },
    clear() {
      session = null;
    },
    isExpiringSoon(thresholdMs = 300_000) {
      if (!session?.expiresAt) return false;
      return session.expiresAt - Date.now() <= thresholdMs;
    },
    snapshot() {
      const k = session?.keys || null;
      return {
        authenticated: !!session?.appToken,
        userId: session?.userId || null,
        tokenExpiresAt: session?.expiresAt || null,
        expiringSoon: api.isExpiringSoon(),
        hasKeys: {
          anthropic: !!k?.anthropicApiKey,
          deepgram: !!k?.deepgramApiKey,
          gemini: !!k?.geminiApiKey
        }
      };
    }
  };

  return api;
}
