import test from 'node:test';
import assert from 'node:assert/strict';

import { startHttpApi } from '../http-api.mjs';
import { readTrolyConfig, trolyEndpoints } from '../troly-config.mjs';
import { createTrolySession } from '../troly-session.mjs';

async function req({ port, token, method, pth, body, headers = {} }) {
  const res = await fetch(`http://127.0.0.1:${port}${pth}`, {
    method,
    headers: {
      ...(body ? { 'content-type': 'application/json' } : {}),
      ...(token ? { authorization: `Bearer ${token}` } : {}),
      ...headers
    },
    body: body ? JSON.stringify(body) : undefined
  });
  const data = await res.json().catch(() => ({}));
  return { res, data };
}

function b64url(obj) {
  return Buffer.from(JSON.stringify(obj)).toString('base64url');
}
function makeJwt(claims) {
  return `${b64url({ alg: 'HS256', typ: 'JWT' })}.${b64url(claims)}.sig`;
}
const jres = (status, body) => ({ ok: status >= 200 && status < 300, status, json: async () => body });

// Fake Troly backend that the runtime proxies to.
function fakeBackend() {
  return async (urlStr, opts = {}) => {
    const p = new URL(urlStr).pathname;
    if (p === '/v1/macos-client/login') {
      const body = JSON.parse(opts.body || '{}');
      if (body.email === 'good@troly.me' && body.password === 'pw') {
        return jres(200, { token: makeJwt({ user_id: 'u1', expires_at: Math.floor(Date.now() / 1000) + 86400 }), expires_in: 86400, token_type: 'Bearer' });
      }
      return jres(401, { error: 'unauthorized' });
    }
    if (p === '/v1/macos-client/exchange-web-token') {
      return jres(200, { token: makeJwt({ user_id: 'u2' }), expires_in: 86400, token_type: 'Bearer' });
    }
    if (p === '/v1/macos-client/app-token') {
      return jres(200, { token: makeJwt({ user_id: 'u1', expires_at: Math.floor(Date.now() / 1000) + 86400 }), expires_in: 86400, token_type: 'Bearer' });
    }
    if (p === '/v1/macos-client/keys') {
      return jres(200, { keys: [{ key_id: 'k1', secret_key: 'sk-anthropic-xyz', created_at: 'now' }] });
    }
    return jres(404, { error: 'not_found' });
  };
}

async function makeServer(fetchImpl = fakeBackend()) {
  const config = readTrolyConfig({ TROLY_API_BASE_URL: 'https://troly.me', TROLY_CLIENT_ID: 'cid-1', TROLY_APP_VERSION: '0.1.2' });
  const urls = trolyEndpoints(config);
  const session = createTrolySession();
  const tabs = {
    listTabs: () => [],
    ensureTab: async () => 't1',
    createTab: async () => 't1',
    closeTab: async () => true,
    getControllerById: () => ({})
  };
  const server = await startHttpApi({
    port: 0,
    token: 'secret',
    tabs,
    defaultTabId: 't0',
    serverId: 'sid-test',
    stateDir: '/tmp',
    getStatus: async () => ({ ok: true }),
    troly: { config, urls, session, fetchImpl }
  });
  return { server, port: server.address().port };
}

test('runtime/status: configured but unauthenticated before login', async (t) => {
  const { server, port } = await makeServer();
  t.after(() => server.close());
  const { res, data } = await req({ port, token: 'secret', method: 'GET', pth: '/runtime/status' });
  assert.equal(res.status, 200);
  assert.equal(data.ok, true);
  assert.equal(data.serverId, 'sid-test');
  assert.equal(data.version, '0.1.2');
  assert.equal(data.troly.configured, true);
  assert.equal(data.troly.authenticated, false);
});

test('troly/login: authenticates, caches session, returns token', async (t) => {
  const { server, port } = await makeServer();
  t.after(() => server.close());
  const login = await req({ port, token: 'secret', method: 'POST', pth: '/troly/login', body: { email: 'good@troly.me', password: 'pw' } });
  assert.equal(login.res.status, 200);
  assert.equal(login.data.ok, true);
  assert.ok(login.data.token);
  assert.equal(login.data.userId, 'u1');
  assert.equal(typeof login.data.expiresAt, 'number');

  const status = await req({ port, token: 'secret', method: 'GET', pth: '/runtime/status' });
  assert.equal(status.data.troly.authenticated, true);
  assert.equal(status.data.troly.userId, 'u1');
});

test('troly/login: requires loopback bearer token', async (t) => {
  const { server, port } = await makeServer();
  t.after(() => server.close());
  const { res, data } = await req({ port, method: 'POST', pth: '/troly/login', body: { email: 'good@troly.me', password: 'pw' } });
  assert.equal(res.status, 401);
  assert.equal(data.error, 'unauthorized');
});

test('troly/login: maps backend 401 to troly_unauthorized', async (t) => {
  const { server, port } = await makeServer();
  t.after(() => server.close());
  const { res, data } = await req({ port, token: 'secret', method: 'POST', pth: '/troly/login', body: { email: 'good@troly.me', password: 'WRONG' } });
  assert.equal(res.status, 401);
  assert.equal(data.error, 'troly_unauthorized');
});

test('troly/login: validates email/password presence', async (t) => {
  const { server, port } = await makeServer();
  t.after(() => server.close());
  const { res, data } = await req({ port, token: 'secret', method: 'POST', pth: '/troly/login', body: { email: '' } });
  assert.equal(res.status, 400);
  assert.equal(data.error, 'troly_invalid_request');
});

test('troly/keys: 409 when not authenticated', async (t) => {
  const { server, port } = await makeServer();
  t.after(() => server.close());
  const { res, data } = await req({ port, token: 'secret', method: 'POST', pth: '/troly/keys' });
  assert.equal(res.status, 409);
  assert.equal(data.error, 'troly_no_session');
});

test('troly/keys: returns key presence after login (generic secret -> anthropic)', async (t) => {
  const { server, port } = await makeServer();
  t.after(() => server.close());
  await req({ port, token: 'secret', method: 'POST', pth: '/troly/login', body: { email: 'good@troly.me', password: 'pw' } });
  const { res, data } = await req({ port, token: 'secret', method: 'POST', pth: '/troly/keys' });
  assert.equal(res.status, 200);
  assert.equal(data.hasAnthropic, true);
  assert.equal(data.hasDeepgram, false);

  const status = await req({ port, token: 'secret', method: 'GET', pth: '/runtime/status' });
  assert.equal(status.data.troly.hasKeys.anthropic, true);
});

test('troly/app-token: refreshes after login', async (t) => {
  const { server, port } = await makeServer();
  t.after(() => server.close());
  await req({ port, token: 'secret', method: 'POST', pth: '/troly/login', body: { email: 'good@troly.me', password: 'pw' } });
  const { res, data } = await req({ port, token: 'secret', method: 'GET', pth: '/troly/app-token' });
  assert.equal(res.status, 200);
  assert.equal(data.ok, true);
  assert.ok(data.token);
});

test('troly/session: accepts a shell-pushed token (cold start)', async (t) => {
  const { server, port } = await makeServer();
  t.after(() => server.close());
  const token = makeJwt({ user_id: 'u9', expires_at: Math.floor(Date.now() / 1000) + 3600 });
  const { res, data } = await req({ port, token: 'secret', method: 'POST', pth: '/troly/session', body: { token } });
  assert.equal(res.status, 200);
  assert.equal(data.authenticated, true);
  assert.equal(data.userId, 'u9');
});
