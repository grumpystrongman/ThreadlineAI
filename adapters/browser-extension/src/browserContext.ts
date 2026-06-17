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
  const injected = await chrome.scripting.executeScript({
    target: { tabId },
    func: (maxCharacters: number) => {
      const compact = (value: string | null | undefined) => value?.replace(/\s+/g, ' ').trim() ?? '';
      const bodyText = compact(document.body?.innerText).slice(0, maxCharacters);
      return {
        title: document.title,
        url: location.href,
        selection: compact(window.getSelection()?.toString()),
        visibleText: bodyText,
        capturedAt: new Date().toISOString(),
        metadata: {
          origin: location.origin,
          host: location.host
        }
      };
    },
    args: [settings.maxCharacters]
  });

  const context = injected[0]?.result as ThreadlinePageContext | undefined;
  if (!context) throw new Error('Browser page context could not be collected from this tab. Try a normal https:// page and reload the tab after loading the extension.');
  return context;
}

export function buildBrowserContent(context: ThreadlinePageContext, mode: BrowserContextMode): string {
  const selected = context.selection?.trim();
  const body = mode === 'selection' && selected ? selected : context.visibleText;
  return [`Title: ${context.title}`, `URL: ${context.url}`, '', body].join('\n').trim();
}

export async function sendBrowserContext(tab: chrome.tabs.Tab, mode: BrowserContextMode): Promise<void> {
  if (!tab.id) throw new Error('No active tab id was available.');
  if (!tab.url || !/^https?:\/\//i.test(tab.url)) {
    throw new Error('Threadline can only capture normal http/https pages. Browser settings, extension pages, new-tab pages, and store pages are blocked by the browser.');
  }

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
