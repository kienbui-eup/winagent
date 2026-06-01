import test from 'node:test';
import assert from 'node:assert/strict';

import { readTrolyConfig, trolyEndpoints, trolyRequestHeaders, validateTrolyConfig } from '../troly-config.mjs';

test('troly-config: readTrolyConfig applies defaults and normalizes paths', () => {
  const config = readTrolyConfig({
    TROLY_API_BASE_URL: 'https://troly.me',
    TROLY_AUTH_LOGIN_PATH: 'v1/custom-login',
    TROLY_KEY_SYNC_PATH: '/v2/key-sync',
    TROLY_API_TIMEOUT_MS: '800',
    TROLY_REQUIRE_TLS: 'true'
  });

  assert.equal(config.apiBaseUrl, 'https://troly.me');
  assert.equal(config.loginPath, '/v1/custom-login');
  assert.equal(config.keySyncPath, '/v2/key-sync');
  assert.equal(config.timeoutMs, 1000);
  assert.equal(config.requireTls, true);
});

test('troly-config: defaults target the reused /v1/macos-client/* blueprint', () => {
  const config = readTrolyConfig({});

  assert.equal(config.loginPath, '/v1/macos-client/login');
  assert.equal(config.exchangeWebTokenPath, '/v1/macos-client/exchange-web-token');
  assert.equal(config.appTokenPath, '/v1/macos-client/app-token');
  assert.equal(config.keySyncPath, '/v1/macos-client/keys');
  assert.equal(config.floatingStatePath, '/v1/macos-client/floating-state');
  assert.equal(config.quickPreviewPath, '/v1/macos-client/quick-preview');
  assert.equal(config.tasksFromVoicePath, '/v1/macos-client/tasks/from-voice');
  assert.equal(config.meetingRadarPath, '/v1/macos-client/meeting-radar');
});

test('troly-config: client identity is read from env', () => {
  const config = readTrolyConfig({ TROLY_CLIENT_ID: 'install-guid-123', TROLY_APP_VERSION: '0.1.2' });
  assert.equal(config.clientId, 'install-guid-123');
  assert.equal(config.appVersion, '0.1.2');
});

test('troly-config: validate warns when api base url is missing in non-strict mode', () => {
  const config = readTrolyConfig({});
  const result = validateTrolyConfig(config, { strict: false });

  assert.equal(result.errors.length, 0);
  assert.equal(result.warnings.length > 0, true);
});

test('troly-config: validate errors on invalid URL', () => {
  const config = readTrolyConfig({ TROLY_API_BASE_URL: 'not-a-url' });
  const result = validateTrolyConfig(config, { strict: true });

  assert.equal(result.errors.length > 0, true);
});

test('troly-config: validate enforces https for non-local host in strict mode', () => {
  const config = readTrolyConfig({
    TROLY_API_BASE_URL: 'http://troly.me',
    TROLY_REQUIRE_TLS: 'true'
  });
  const result = validateTrolyConfig(config, { strict: true });

  assert.equal(result.errors.length > 0, true);
});

test('troly-config: trolyEndpoints builds the full macos-client URL set', () => {
  const config = readTrolyConfig({ TROLY_API_BASE_URL: 'https://troly.me/' });
  const urls = trolyEndpoints(config);

  assert.equal(urls.loginUrl, 'https://troly.me/v1/macos-client/login');
  assert.equal(urls.exchangeWebTokenUrl, 'https://troly.me/v1/macos-client/exchange-web-token');
  assert.equal(urls.appTokenUrl, 'https://troly.me/v1/macos-client/app-token');
  assert.equal(urls.keySyncUrl, 'https://troly.me/v1/macos-client/keys');
  assert.equal(urls.floatingStateUrl, 'https://troly.me/v1/macos-client/floating-state');
  assert.equal(urls.quickPreviewUrl, 'https://troly.me/v1/macos-client/quick-preview');
  assert.equal(urls.tasksFromVoiceUrl, 'https://troly.me/v1/macos-client/tasks/from-voice');
  assert.equal(urls.meetingRadarUrl, 'https://troly.me/v1/macos-client/meeting-radar');
});

test('troly-config: trolyEndpoints returns empty strings when no base url', () => {
  const urls = trolyEndpoints(readTrolyConfig({}));
  assert.equal(urls.loginUrl, '');
  assert.equal(urls.keySyncUrl, '');
});

test('troly-config: trolyRequestHeaders includes client identity when set', () => {
  const headers = trolyRequestHeaders(
    readTrolyConfig({ TROLY_CLIENT_ID: 'cid', TROLY_APP_VERSION: '1.2.3' })
  );
  assert.equal(headers['X-Client-Id'], 'cid');
  assert.equal(headers['X-App-Version'], '1.2.3');
  assert.equal(headers['Content-Type'], 'application/json');
});

test('troly-config: trolyRequestHeaders omits identity headers when unset', () => {
  const headers = trolyRequestHeaders(readTrolyConfig({}));
  assert.equal('X-Client-Id' in headers, false);
  assert.equal('X-App-Version' in headers, false);
});
