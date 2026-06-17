import { callThreadline, getActiveSessionId, getSettings } from './threadlineClient.js';

export type ThreadlinePageContext = {
  title: string;
  url: string;
  selection: string;
  visibleText: string;
  capturedAt: string;
  metadata?: Record<string, string>;
};

export type BrowserContextMode = 'selection' | 'page';

export async function getPageContext(tabId: number): Promise<ThreadlinePageContext> {
  const settings = await getSettings();
  return await chrome.tabs.sendMessage(tabId, {
    type: 'THREADLINE_GET_PAGE_CONTEXT',
    maxCharacters: settings.maxCharacters
  });
}

export function buildBrowserContent(context: ThreadlinePageContext, mode: BrowserContextMode): string {
  const selected = context.selection?.trim();
  const body = mode === 'selection' && selected ? selected : context.visibleText;
  return [`Title: ${context.title}`, `URL: ${context.url}`, '', body].join('\n').trim();
}

export async function sendBrowserContext(tab: chrome.tabs.Tab, mode: BrowserContextMode): Promise<void> {
  if (!tab.id) throw new Error('No active tab id was available.');
  const sessionId = await getActiveSessionId();
  const pageContext = await getPageContext(tab.id);
  const content = buildBrowserContent(pageContext, mode);
  if (!content) throw new Error('No browser context was captured.');

  const body = JSON.stringify({
    source: 'Browser',
    contextType: mode === 'selection' ? 'browser-selection' : 'browser-page',
    content,
    applicationName: 'Browser',
    processName: 'browser-extension',
    windowTitle: pageContext.title,
    uri: pageContext.url,
    userApproved: true,
    metadata: {
      adapter: 'ThreadlineAI Browser Extension',
      capturedAt: pageContext.capturedAt,
      mode
    }
  });

  await callThreadline(`/sessions/${sessionId}/events/preview`, { method: 'POST', body });
  await callThreadline(`/sessions/${sessionId}/events`, { method: 'POST', body });
}

export async function sendActiveTab(mode: BrowserContextMode): Promise<void> {
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  if (!tab) throw new Error('No active browser tab was found.');
  await sendBrowserContext(tab, mode);
}
