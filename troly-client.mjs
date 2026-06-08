// Thin client for the Troly backend desktop-client endpoints (/v1/macos-client/*).
// Mirrors the mcp-lib.requestJson style: uses global fetch, with an injectable
// `fetchImpl` so the HTTP layer can be unit-tested without the network. The Node
// runtime is the single HTTPS egress to the Troly backend (see ADR-0002).
import { trolyRequestHeaders } from './troly-config.mjs';

async function trolyRequest({ url, method = 'POST', headers = {}, body, currentToken, timeoutMs = 15_000, fetchImpl = fetch }) {
  if (!url) {
    const err = new Error('troly_not_configured');
    err.data = { reason: 'missing_endpoint_url' };
    throw err;
  }

  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), Math.max(1_000, Number(timeoutMs) || 15_000));
  let res;
  try {
    res = await fetchImpl(url, {
      method,
      headers: { ...headers, ...(currentToken ? { Authorization: `Bearer ${currentToken}` } : {}) },
      body: body !== undefined ? JSON.stringify(body) : undefined,
      signal: controller.signal
    });
  } catch (e) {
    const err = new Error('troly_upstream_error');
    err.data = { status: 0, cause: String(e?.message || e) };
    throw err;
  } finally {
    clearTimeout(timer);
  }

  const data = await res.json().catch(() => ({}));
  if (!res.ok || data?.error) {
    const err = new Error(res.status === 401 ? 'troly_unauthorized' : 'troly_upstream_error');
    err.data = { status: res.status, body: data };
    throw err;
  }
  return data;
}

export async function trolyLogin({ config, urls, email, password, fetchImpl } = {}) {
  return trolyRequest({
    url: urls?.loginUrl,
    method: 'POST',
    headers: trolyRequestHeaders(config || {}),
    body: { email, password },
    timeoutMs: config?.timeoutMs,
    fetchImpl
  });
}

export async function trolyExchangeWebToken({ config, urls, webToken, fetchImpl } = {}) {
  return trolyRequest({
    url: urls?.exchangeWebTokenUrl,
    method: 'POST',
    headers: trolyRequestHeaders(config || {}),
    body: { web_token: webToken, client_id: config?.clientId || undefined },
    timeoutMs: config?.timeoutMs,
    fetchImpl
  });
}

export async function trolyRefreshAppToken({ config, urls, currentToken, fetchImpl } = {}) {
  if (!urls?.appTokenUrl) {
    const err = new Error('troly_not_configured');
    err.data = { reason: 'missing_app_token_url' };
    throw err;
  }
  const u = new URL(urls.appTokenUrl);
  u.searchParams.set('current_token', currentToken || '');
  return trolyRequest({
    url: u.toString(),
    method: 'GET',
    headers: trolyRequestHeaders(config || {}, { contentType: null }),
    timeoutMs: config?.timeoutMs,
    fetchImpl
  });
}

export async function trolyFetchKeys({ config, urls, appToken, fetchImpl } = {}) {
  return trolyRequest({
    url: urls?.keySyncUrl,
    method: 'POST',
    headers: trolyRequestHeaders(config || {}),
    body: {},
    currentToken: appToken,
    timeoutMs: config?.timeoutMs,
    fetchImpl
  });
}

// Mirror the macagent KeyService contract: the backend /keys response is generic
// ({ keys: [{ key_id, secret_key, ... }] }) and does NOT distinguish providers.
// Prefer a named payload ({ anthropic_api_key, deepgram_api_key, gemini_api_key })
// if present; otherwise treat the first generic secret_key as the Anthropic key.
export function decodeKeys(payload) {
  const out = { anthropicApiKey: null, deepgramApiKey: null, geminiApiKey: null, raw: payload || null };
  if (payload && typeof payload === 'object') {
    if (typeof payload.anthropic_api_key === 'string') out.anthropicApiKey = payload.anthropic_api_key;
    if (typeof payload.deepgram_api_key === 'string') out.deepgramApiKey = payload.deepgram_api_key;
    if (typeof payload.gemini_api_key === 'string') out.geminiApiKey = payload.gemini_api_key;
    if (!out.anthropicApiKey && Array.isArray(payload.keys)) {
      const first = payload.keys.find((k) => k && typeof k.secret_key === 'string' && k.secret_key);
      if (first) out.anthropicApiKey = first.secret_key;
    }
  }
  return out;
}

// Parse a JWT payload without verifying the signature. Returns the claims object
// or null. Used only to read non-authoritative hints (user_id, expiry).
export function decodeJwt(token) {
  try {
    const part = String(token || '').split('.')[1];
    if (!part) return null;
    const json = Buffer.from(part, 'base64url').toString('utf8');
    const claims = JSON.parse(json);
    return claims && typeof claims === 'object' ? claims : null;
  } catch {
    return null;
  }
}

// Epoch-ms expiry from a JWT (backend uses `expires_at` in seconds; falls back to
// standard `exp`). Returns null if unavailable.
export function decodeJwtExp(token) {
  const claims = decodeJwt(token);
  const expSec = Number(claims?.expires_at ?? claims?.exp);
  return Number.isFinite(expSec) ? expSec * 1000 : null;
}
