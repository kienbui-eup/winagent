import test from 'node:test';
import assert from 'node:assert/strict';

import { readTrolyConfig, trolyEndpoints } from '../troly-config.mjs';
import {
  trolyLogin,
  trolyRefreshAppToken,
  trolyFetchKeys,
  decodeKeys,
  decodeJwt,
  decodeJwtExp
} from '../troly-client.mjs';

function b64url(obj) {
  return Buffer.from(JSON.stringify(obj)).toString('base64url');
}
function makeJwt(claims) {
  return `${b64url({ alg: 'HS256', typ: 'JWT' })}.${b64url(claims)}.sig`;
}
const cfg = (env = {}) => readTrolyConfig({ TROLY_API_BASE_URL: 'https://troly.me', ...env });
const urls = () => trolyEndpoints(cfg());

// JSON Response-like stub.
const jres = (status, body) => ({ ok: status >= 200 && status < 300, status, json: async () => body });

test('decodeKeys: prefers a named payload', () => {
  const k = decodeKeys({ anthropic_api_key: 'a', deepgram_api_key: 'd', gemini_api_key: 'g' });
  assert.equal(k.anthropicApiKey, 'a');
  assert.equal(k.deepgramApiKey, 'd');
  assert.equal(k.geminiApiKey, 'g');
});

test('decodeKeys: falls back to first generic secret_key as Anthropic', () => {
  const k = decodeKeys({ keys: [{ key_id: 'k1', secret_key: 'sk1' }, { key_id: 'k2', secret_key: 'sk2' }] });
  assert.equal(k.anthropicApiKey, 'sk1');
  assert.equal(k.deepgramApiKey, null);
  assert.equal(k.geminiApiKey, null);
});

test('decodeKeys: empty payload yields all null', () => {
  const k = decodeKeys({});
  assert.equal(k.anthropicApiKey, null);
  assert.equal(k.deepgramApiKey, null);
  assert.equal(k.geminiApiKey, null);
});

test('decodeJwt / decodeJwtExp parse claims without verifying', () => {
  const token = makeJwt({ user_id: 'u1', expires_at: 1893456000 });
  assert.equal(decodeJwt(token).user_id, 'u1');
  assert.equal(decodeJwtExp(token), 1893456000 * 1000);
  assert.equal(decodeJwt('not-a-jwt'), null);
  assert.equal(decodeJwtExp('not-a-jwt'), null);
});

test('trolyLogin: returns backend data on success', async () => {
  const fetchImpl = async () => jres(200, { token: makeJwt({ user_id: 'u1' }), expires_in: 86400, token_type: 'Bearer' });
  const data = await trolyLogin({ config: cfg(), urls: urls(), email: 'a@b.c', password: 'pw', fetchImpl });
  assert.equal(data.expires_in, 86400);
  assert.ok(data.token);
});

test('trolyLogin: maps 401 to troly_unauthorized with status', async () => {
  const fetchImpl = async () => jres(401, { error: 'unauthorized' });
  await assert.rejects(
    () => trolyLogin({ config: cfg(), urls: urls(), email: 'a@b.c', password: 'bad', fetchImpl }),
    (err) => err.message === 'troly_unauthorized' && err.data?.status === 401
  );
});

test('trolyLogin: maps 500 to troly_upstream_error', async () => {
  const fetchImpl = async () => jres(500, { error: 'boom' });
  await assert.rejects(
    () => trolyLogin({ config: cfg(), urls: urls(), email: 'a@b.c', password: 'pw', fetchImpl }),
    (err) => err.message === 'troly_upstream_error' && err.data?.status === 500
  );
});

test('trolyRefreshAppToken: sends current_token as a query param via GET', async () => {
  let seen = null;
  const fetchImpl = async (u, opts) => {
    seen = { url: u, method: opts.method };
    return jres(200, { token: makeJwt({ user_id: 'u1' }), expires_in: 86400 });
  };
  await trolyRefreshAppToken({ config: cfg(), urls: urls(), currentToken: 'TOK123', fetchImpl });
  assert.equal(seen.method, 'GET');
  assert.ok(seen.url.includes('/v1/macos-client/app-token'));
  assert.ok(seen.url.includes('current_token=TOK123'));
});

test('trolyFetchKeys: sends Authorization Bearer with the app token + client headers', async () => {
  let headers = null;
  const fetchImpl = async (_u, opts) => {
    headers = opts.headers;
    return jres(200, { keys: [{ key_id: 'k1', secret_key: 'sk1' }] });
  };
  await trolyFetchKeys({ config: cfg({ TROLY_CLIENT_ID: 'cid-1', TROLY_APP_VERSION: '0.1.2' }), urls: urls(), appToken: 'APP', fetchImpl });
  assert.equal(headers.Authorization, 'Bearer APP');
  assert.equal(headers['X-Client-Id'], 'cid-1');
  assert.equal(headers['X-App-Version'], '0.1.2');
});
