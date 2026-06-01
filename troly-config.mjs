function asBool(v, fallback = false) {
  if (typeof v === 'boolean') return v;
  if (typeof v === 'string') {
    const s = v.trim().toLowerCase();
    if (s === '1' || s === 'true' || s === 'yes' || s === 'on') return true;
    if (s === '0' || s === 'false' || s === 'no' || s === 'off') return false;
  }
  return fallback;
}

function normalizePath(v, fallback) {
  const s = String(v || '').trim();
  if (!s) return fallback;
  return s.startsWith('/') ? s : `/${s}`;
}

// Troly backend already exposes a platform-neutral desktop-client blueprint at
// /v1/macos-client/* (api/controllers/macos_client.py), keyed by X-Client-Id /
// X-App-Version headers. The Windows agent reuses those endpoints as-is rather
// than introducing a parallel /v1/windows-client/* surface (see ADR-0002).
export function defaultTrolyConfig() {
  return {
    apiBaseUrl: '',
    loginPath: '/v1/macos-client/login',
    exchangeWebTokenPath: '/v1/macos-client/exchange-web-token',
    appTokenPath: '/v1/macos-client/app-token',
    keySyncPath: '/v1/macos-client/keys',
    floatingStatePath: '/v1/macos-client/floating-state',
    quickPreviewPath: '/v1/macos-client/quick-preview',
    tasksFromVoicePath: '/v1/macos-client/tasks/from-voice',
    meetingRadarPath: '/v1/macos-client/meeting-radar',
    // Per-install client identity, injected by the runtime/shell. clientId is a
    // stable per-install GUID (see ensureClientId in state.mjs); appVersion is
    // the agent version. Both are sent as X-Client-Id / X-App-Version headers.
    clientId: '',
    appVersion: '',
    timeoutMs: 15_000,
    requireTls: true,
    environment: 'development'
  };
}

export function readTrolyConfig(env = process.env) {
  const d = defaultTrolyConfig();
  const timeoutRaw = Number(env.TROLY_API_TIMEOUT_MS);
  const timeoutMs = Number.isFinite(timeoutRaw) ? Math.min(120_000, Math.max(1_000, timeoutRaw)) : d.timeoutMs;
  return {
    apiBaseUrl: String(env.TROLY_API_BASE_URL || env.TROLY_BASE_URL || d.apiBaseUrl).trim(),
    loginPath: normalizePath(env.TROLY_AUTH_LOGIN_PATH, d.loginPath),
    exchangeWebTokenPath: normalizePath(env.TROLY_EXCHANGE_WEB_TOKEN_PATH, d.exchangeWebTokenPath),
    appTokenPath: normalizePath(env.TROLY_APP_TOKEN_PATH, d.appTokenPath),
    keySyncPath: normalizePath(env.TROLY_KEY_SYNC_PATH, d.keySyncPath),
    floatingStatePath: normalizePath(env.TROLY_FLOATING_STATE_PATH, d.floatingStatePath),
    quickPreviewPath: normalizePath(env.TROLY_QUICK_PREVIEW_PATH, d.quickPreviewPath),
    tasksFromVoicePath: normalizePath(env.TROLY_TASKS_FROM_VOICE_PATH, d.tasksFromVoicePath),
    meetingRadarPath: normalizePath(env.TROLY_MEETING_RADAR_PATH, d.meetingRadarPath),
    clientId: String(env.TROLY_CLIENT_ID || d.clientId).trim(),
    appVersion: String(env.TROLY_APP_VERSION || d.appVersion).trim(),
    timeoutMs,
    requireTls: asBool(env.TROLY_REQUIRE_TLS, d.requireTls),
    environment: String(env.NODE_ENV || d.environment).trim() || d.environment
  };
}

function isLocalHost(hostname) {
  const h = String(hostname || '').toLowerCase();
  return h === 'localhost' || h === '127.0.0.1' || h === '::1';
}

export function validateTrolyConfig(config, { strict = false } = {}) {
  const errors = [];
  const warnings = [];

  if (!config.apiBaseUrl) {
    const msg = 'Missing TROLY_API_BASE_URL. Troly auth/key integration is not configured yet.';
    if (strict) errors.push(msg);
    else warnings.push(msg);
    return { errors, warnings };
  }

  let parsed = null;
  try {
    parsed = new URL(config.apiBaseUrl);
  } catch {
    errors.push('TROLY_API_BASE_URL is not a valid URL.');
    return { errors, warnings };
  }

  if (config.requireTls && parsed.protocol !== 'https:' && !isLocalHost(parsed.hostname)) {
    const msg = 'TROLY_API_BASE_URL should use https in non-local environments.';
    if (strict) errors.push(msg);
    else warnings.push(msg);
  }

  return { errors, warnings };
}

export function trolyEndpoints(config) {
  const empty = {
    loginUrl: '',
    exchangeWebTokenUrl: '',
    appTokenUrl: '',
    keySyncUrl: '',
    floatingStateUrl: '',
    quickPreviewUrl: '',
    tasksFromVoiceUrl: '',
    meetingRadarUrl: ''
  };
  if (!config.apiBaseUrl) return empty;

  const base = new URL(config.apiBaseUrl);
  const join = (p) => new URL(p, base).toString();
  return {
    loginUrl: join(config.loginPath),
    exchangeWebTokenUrl: join(config.exchangeWebTokenPath),
    appTokenUrl: join(config.appTokenPath),
    keySyncUrl: join(config.keySyncPath),
    floatingStateUrl: join(config.floatingStatePath),
    quickPreviewUrl: join(config.quickPreviewPath),
    tasksFromVoiceUrl: join(config.tasksFromVoicePath),
    meetingRadarUrl: join(config.meetingRadarPath)
  };
}

// Headers every Troly desktop-client request must carry. The backend keys rate
// limiting, audit logging, and token issuance by X-Client-Id + X-App-Version
// (see api/controllers/macos_client.py).
export function trolyRequestHeaders(config, { contentType = 'application/json' } = {}) {
  const headers = {};
  if (contentType) headers['Content-Type'] = contentType;
  if (config.clientId) headers['X-Client-Id'] = config.clientId;
  if (config.appVersion) headers['X-App-Version'] = config.appVersion;
  return headers;
}
