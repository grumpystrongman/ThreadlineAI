export type ExtractionLimits = {
  maxCharacters: number;
  maxSelectedTextCharacters: number;
  maxVisibleTextCharacters: number;
  maxArticleTextCharacters: number;
  maxHeadings: number;
  maxLinks: number;
  maxImages: number;
  maxTables: number;
  maxTableCharacters: number;
  maxLinkTextCharacters: number;
};

export type ThreadlineSettings = ExtractionLimits & {
  baseUrl: string;
  localAccessToken?: string;
  redactionPatterns: string[];
};

export type BrowserTabIdentity = {
  tabId?: number;
  windowId?: number;
  title?: string;
  url?: string;
  status?: string;
  incognito?: boolean;
  capturedAt: string;
};

type AdapterRegistration = {
  id: string;
  kind: string;
  displayName: string;
  version?: string;
  lastSeenAt?: string;
  metadata?: Record<string, string>;
};

export type ThreadlineHealth = {
  status?: string;
  service?: string;
  authRequired?: boolean;
  maxContextCharacters?: number;
};

export type HeartbeatResult = {
  ok: boolean;
  adapterId?: string;
  registered?: boolean;
  registration?: AdapterRegistration;
  error?: string;
};

export type RedactionOutcome = {
  text: string;
  findingCount: number;
  hooks: string[];
};

export const extensionDisplayName = 'ThreadlineAI Browser Extension';
export const extensionVersion = chrome.runtime.getManifest().version;
export const adapterStorageKey = 'threadlineBrowserAdapterId';

const oneMinuteMs = 60_000;
const adapterPermissions = 35; // ReadSessions | WriteContext | RegisterAdapters

export const defaultSettings: ThreadlineSettings = {
  baseUrl: 'http://localhost:5057',
  maxCharacters: 12000,
  maxSelectedTextCharacters: 4000,
  maxVisibleTextCharacters: 12000,
  maxArticleTextCharacters: 12000,
  maxHeadings: 40,
  maxLinks: 80,
  maxImages: 80,
  maxTables: 20,
  maxTableCharacters: 2000,
  maxLinkTextCharacters: 240,
  redactionPatterns: []
};

export async function getSettings(): Promise<ThreadlineSettings> {
  const stored = await chrome.storage.local.get([
    'baseUrl',
    'localAccessToken',
    'maxCharacters',
    'maxSelectedTextCharacters',
    'maxVisibleTextCharacters',
    'maxArticleTextCharacters',
    'maxHeadings',
    'maxLinks',
    'maxImages',
    'maxTables',
    'maxTableCharacters',
    'maxLinkTextCharacters',
    'redactionPatterns'
  ]);

  const maxCharacters = readNumber(stored.maxCharacters, defaultSettings.maxCharacters, 1000, 50000);
  return {
    baseUrl: typeof stored.baseUrl === 'string' && stored.baseUrl.trim() ? stored.baseUrl.trim().replace(/\/$/, '') : defaultSettings.baseUrl,
    localAccessToken: typeof stored.localAccessToken === 'string' && stored.localAccessToken.trim() ? stored.localAccessToken.trim() : undefined,
    maxCharacters,
    maxSelectedTextCharacters: readNumber(stored.maxSelectedTextCharacters, defaultSettings.maxSelectedTextCharacters, 500, maxCharacters),
    maxVisibleTextCharacters: readNumber(stored.maxVisibleTextCharacters, defaultSettings.maxVisibleTextCharacters, 1000, maxCharacters),
    maxArticleTextCharacters: readNumber(stored.maxArticleTextCharacters, defaultSettings.maxArticleTextCharacters, 1000, maxCharacters),
    maxHeadings: readNumber(stored.maxHeadings, defaultSettings.maxHeadings, 0, 120),
    maxLinks: readNumber(stored.maxLinks, defaultSettings.maxLinks, 0, 200),
    maxImages: readNumber(stored.maxImages, defaultSettings.maxImages, 0, 200),
    maxTables: readNumber(stored.maxTables, defaultSettings.maxTables, 0, 60),
    maxTableCharacters: readNumber(stored.maxTableCharacters, defaultSettings.maxTableCharacters, 200, 10000),
    maxLinkTextCharacters: readNumber(stored.maxLinkTextCharacters, defaultSettings.maxLinkTextCharacters, 40, 1000),
    redactionPatterns: readRedactionPatterns(stored.redactionPatterns)
  };
}

export async function saveSettings(settings: Partial<ThreadlineSettings>): Promise<void> {
  await chrome.storage.local.set(settings);
}

export async function callThreadline<T>(path: string, init?: RequestInit): Promise<T> {
  const settings = await getSettings();
  const headers = new Headers(init?.headers);
  headers.set('content-type', 'application/json');
  if (settings.localAccessToken) headers.set('X-Threadline-Token', settings.localAccessToken);

  let response: Response;
  try {
    response = await fetch(`${settings.baseUrl}${path}`, { ...init, headers });
  } catch (error) {
    throw new Error(`Threadline service is unavailable at ${settings.baseUrl}. Start the local service, then try again. ${error instanceof Error ? error.message : String(error)}`);
  }

  if (!response.ok) {
    const text = await response.text();
    throw new Error(`Threadline service returned ${response.status}: ${text}`);
  }

  if (response.status === 204) return undefined as T;
  return await response.json() as T;
}

export async function getActiveSessionId(): Promise<string> {
  const session = await callThreadline<{ id: string }>('/sessions/active');
  return session.id;
}

export async function testThreadlineConnection(): Promise<ThreadlineHealth> {
  return await callThreadline<ThreadlineHealth>('/health');
}

export async function getStoredAdapterId(): Promise<string | undefined> {
  const stored = await chrome.storage.local.get([adapterStorageKey]);
  const value = stored[adapterStorageKey];
  return typeof value === 'string' && value.trim() ? value.trim() : undefined;
}

export async function registerBrowserExtension(tabIdentity?: BrowserTabIdentity): Promise<AdapterRegistration> {
  const registration = await callThreadline<AdapterRegistration>('/adapters', {
    method: 'POST',
    body: JSON.stringify({
      kind: 'BrowserExtension',
      displayName: extensionDisplayName,
      permissions: adapterPermissions,
      version: extensionVersion,
      metadata: buildAdapterMetadata(tabIdentity)
    })
  });

  await chrome.storage.local.set({ [adapterStorageKey]: registration.id, threadlineLastHeartbeatAt: new Date().toISOString() });
  return registration;
}

export async function sendExtensionHeartbeat(tabIdentity?: BrowserTabIdentity): Promise<HeartbeatResult> {
  const adapterId = await getStoredAdapterId();
  if (!adapterId) {
    try {
      const registration = await registerBrowserExtension(tabIdentity);
      return { ok: true, adapterId: registration.id, registered: true, registration };
    } catch (error) {
      return { ok: false, error: error instanceof Error ? error.message : String(error) };
    }
  }

  try {
    const registration = await callThreadline<AdapterRegistration>(`/adapters/${encodeURIComponent(adapterId)}/heartbeat`, {
      method: 'POST',
      body: JSON.stringify({
        version: extensionVersion,
        metadata: buildAdapterMetadata(tabIdentity)
      })
    });
    await chrome.storage.local.set({ threadlineLastHeartbeatAt: new Date().toISOString() });
    return { ok: true, adapterId: registration.id, registered: false, registration };
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    if (message.includes('returned 404')) {
      const registration = await registerBrowserExtension(tabIdentity);
      return { ok: true, adapterId: registration.id, registered: true, registration };
    }

    return { ok: false, adapterId, error: message };
  }
}

export async function getLastHeartbeatAgeMs(): Promise<number | undefined> {
  const stored = await chrome.storage.local.get(['threadlineLastHeartbeatAt']);
  const value = stored.threadlineLastHeartbeatAt;
  if (typeof value !== 'string') return undefined;

  const timestamp = Date.parse(value);
  return Number.isFinite(timestamp) ? Math.max(0, Date.now() - timestamp) : undefined;
}

export function formatHeartbeatAge(ageMs: number | undefined): string {
  if (ageMs === undefined) return 'never';
  if (ageMs < oneMinuteMs) return 'less than a minute ago';
  return `${Math.round(ageMs / oneMinuteMs)} minute(s) ago`;
}

export function redactText(value: string, customPatterns: string[] = []): RedactionOutcome {
  let text = value;
  let findingCount = 0;
  const hooks = new Set<string>();

  const apply = (label: string, pattern: RegExp, replacement = '[REDACTED]'): void => {
    const matches = text.match(pattern);
    if (!matches || matches.length === 0) return;
    findingCount += matches.length;
    hooks.add(label);
    text = text.replace(pattern, replacement);
  };

  apply('url-secret', /([?&](?:api[_-]?key|token|access_token|client_secret|password)=)[^&\s]+/gi, '$1[REDACTED]');
  apply('bearer-token', /\bbearer\s+[A-Za-z0-9._\-]{20,}\b/gi);
  apply('openai-style-api-key', /\bsk-[A-Za-z0-9_\-]{20,}\b/g);
  apply('jwt', /\beyJ[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+\b/g);
  apply('email-address', /\b[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}\b/gi);
  apply('phone-number', /\b(?:\+?1[\s.\-]?)?(?:\(?\d{3}\)?[\s.\-]?)\d{3}[\s.\-]?\d{4}\b/g);
  apply('ssn', /\b\d{3}-\d{2}-\d{4}\b/g);
  apply('medical-record-marker', /\b(?:MRN|medical record number|patient id)\s*[:#=]?\s*[A-Za-z0-9\-]{5,}\b/gi);

  for (const pattern of customPatterns) {
    const trimmed = pattern.trim();
    if (!trimmed) continue;

    try {
      apply('custom-redaction-hook', new RegExp(trimmed, 'gi'));
    } catch {
      const escaped = trimmed.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
      apply('custom-redaction-hook', new RegExp(escaped, 'gi'));
    }
  }

  return { text, findingCount, hooks: Array.from(hooks) };
}

export function limitText(value: string, maxCharacters: number): string {
  if (value.length <= maxCharacters) return value;
  return `${value.slice(0, Math.max(0, maxCharacters - 36))}\n...[trimmed by Threadline extension]`;
}

function readNumber(value: unknown, fallback: number, min: number, max: number): number {
  const parsed = typeof value === 'number' ? value : typeof value === 'string' ? Number(value) : Number.NaN;
  if (!Number.isFinite(parsed)) return fallback;
  return Math.min(max, Math.max(min, Math.floor(parsed)));
}

function readRedactionPatterns(value: unknown): string[] {
  if (Array.isArray(value)) return value.map(item => String(item).trim()).filter(Boolean).slice(0, 100);
  if (typeof value === 'string') return value.split(/\r?\n/).map(item => item.trim()).filter(Boolean).slice(0, 100);
  return defaultSettings.redactionPatterns;
}

function buildAdapterMetadata(tabIdentity?: BrowserTabIdentity): Record<string, string> {
  const metadata: Record<string, string> = {
    extensionId: chrome.runtime.id,
    manifestVersion: '3',
    extensionVersion,
    browserUserAgent: typeof navigator !== 'undefined' ? navigator.userAgent.slice(0, 240) : 'unknown',
    heartbeatSupported: 'true',
    capabilities: 'current-tab,page-title,url,selected-text,visible-text,article-text,dom-limits,redaction-hooks,unavailable-explanations'
  };

  if (tabIdentity) {
    metadata.currentTabCapturedAt = tabIdentity.capturedAt;
    if (tabIdentity.tabId !== undefined) metadata.currentTabId = String(tabIdentity.tabId);
    if (tabIdentity.windowId !== undefined) metadata.currentWindowId = String(tabIdentity.windowId);
    if (tabIdentity.title) metadata.currentTabTitle = tabIdentity.title.slice(0, 240);
    if (tabIdentity.url) metadata.currentTabUrl = tabIdentity.url.slice(0, 500);
    if (tabIdentity.status) metadata.currentTabStatus = tabIdentity.status;
    if (tabIdentity.incognito !== undefined) metadata.currentTabIncognito = String(tabIdentity.incognito);
  }

  return metadata;
}
