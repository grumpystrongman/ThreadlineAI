export type ThreadlineSettings = {
  baseUrl: string;
  localAccessToken?: string;
  maxCharacters: number;
};

export const defaultSettings: ThreadlineSettings = {
  baseUrl: 'http://localhost:5057',
  maxCharacters: 12000
};

export async function getSettings(): Promise<ThreadlineSettings> {
  const stored = await chrome.storage.local.get(['baseUrl', 'localAccessToken', 'maxCharacters']);
  return {
    baseUrl: typeof stored.baseUrl === 'string' && stored.baseUrl.trim() ? stored.baseUrl.trim().replace(/\/$/, '') : defaultSettings.baseUrl,
    localAccessToken: typeof stored.localAccessToken === 'string' && stored.localAccessToken.trim() ? stored.localAccessToken.trim() : undefined,
    maxCharacters: Number.isFinite(stored.maxCharacters) ? Number(stored.maxCharacters) : defaultSettings.maxCharacters
  };
}

export async function callThreadline<T>(path: string, init?: RequestInit): Promise<T> {
  const settings = await getSettings();
  const headers = new Headers(init?.headers);
  headers.set('content-type', 'application/json');
  if (settings.localAccessToken) headers.set('X-Threadline-Token', settings.localAccessToken);

  const response = await fetch(`${settings.baseUrl}${path}`, { ...init, headers });
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
