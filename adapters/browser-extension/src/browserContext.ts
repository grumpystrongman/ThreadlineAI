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

export async function getPageContext(tabId: number, tabUrl?: string, selectedOverride?: string): Promise<ThreadlinePageContext> {
  const settings = await getSettings();
  const injected = await chrome.scripting.executeScript({
    target: { tabId },
    func: (maxCharacters: number) => {
      const compact = (value: string | null | undefined) => value?.replace(/\s+/g, ' ').trim() ?? '';
      const read = (selector: string) => Array.from(document.querySelectorAll(selector)).map(element => compact((element as HTMLElement).innerText || element.textContent)).filter(Boolean).join('\n');
      const cleanGoogleChrome = (value: string) => value
        .replace(/\bShare\b.*?\bEditing\b/gi, ' ')
        .replace(/\bFile\b\s*\bEdit\b\s*\bView\b\s*\bInsert\b\s*\bFormat\b\s*\bTools\b\s*\bExtensions\b\s*\bHelp\b/gi, ' ')
        .replace(/\bNormal text\b.*?\bShow tabs & outlines\b/gi, ' ')
        .replace(/\bTurn on screen reader support\b.*?\bBanner hidden\b/gi, ' ')
        .replace(/\b\d\b(?:\s+\b\d\b){4,}/g, ' ')
        .replace(/\s+/g, ' ')
        .trim();

      const host = location.host;
      const selectedText = compact(window.getSelection()?.toString());
      let extracted = '';
      let extractionMode = 'body';

      if (host === 'docs.google.com' && location.pathname.includes('/document/')) {
        const docsText = read('.kix-lineview-content, .kix-wordhtmlgenerator-word-node, .docs-editor-container [aria-label], [role="textbox"]');
        extracted = cleanGoogleChrome(docsText || compact(document.body?.innerText));
        extractionMode = 'google-docs-dom';
      } else if (host === 'mail.google.com') {
        const gmailText = read('[role="main"], .aeF, .AO');
        extracted = compact(gmailText || document.body?.innerText);
        extractionMode = 'gmail';
      } else {
        const mainText = read('main, article, [role="main"]');
        extracted = compact(mainText || document.body?.innerText);
      }

      return {
        title: document.title,
        url: location.href,
        selection: selectedText,
        visibleText: extracted.slice(0, maxCharacters),
        capturedAt: new Date().toISOString(),
        metadata: {
          origin: location.origin,
          host,
          extractionMode
        }
      };
    },
    args: [settings.maxCharacters]
  });

  const context = injected[0]?.result as ThreadlinePageContext | undefined;
  if (!context) throw new Error('Browser page context could not be collected from this tab. Try a normal https:// page and reload the tab after loading the extension.');

  const explicitSelection = selectedOverride?.replace(/\s+/g, ' ').trim();
  if (explicitSelection) {
    context.selection = explicitSelection;
    context.metadata = { ...(context.metadata ?? {}), selectionSource: 'context-menu' };
  }

  const docsExport = await tryGetGoogleDocsExport(tabUrl ?? context.url, settings.maxCharacters);
  if (docsExport) {
    context.visibleText = docsExport;
    context.metadata = { ...(context.metadata ?? {}), extractionMode: 'google-docs-export' };
  }

  return context;
}

async function tryGetGoogleDocsExport(url: string, maxCharacters: number): Promise<string | null> {
  const match = url.match(/https:\/\/docs\.google\.com\/document\/d\/([^/]+)/i);
  const documentId = match?.[1];
  if (!documentId) return null;

  try {
    const response = await fetch(`https://docs.google.com/document/d/${documentId}/export?format=txt`, { credentials: 'include' });
    if (!response.ok) return null;
    const text = await response.text();
    if (!text.trim()) return null;
    if (/^\s*</.test(text) || text.includes('<html')) return null;
    return text.replace(/\s+/g, ' ').trim().slice(0, maxCharacters);
  } catch {
    return null;
  }
}

export function buildBrowserContent(context: ThreadlinePageContext, mode: BrowserContextMode): string {
  const selected = context.selection?.trim();
  const body = mode === 'selection' && selected ? selected : context.visibleText;
  return [`Title: ${context.title}`, `URL: ${context.url}`, `Extraction: ${context.metadata?.extractionMode ?? 'unknown'}`, '', body].join('\n').trim();
}

export async function sendBrowserContext(tab: chrome.tabs.Tab, mode: BrowserContextMode, selectedOverride?: string): Promise<void> {
  if (!tab.id) throw new Error('No active tab id was available.');
  if (!tab.url || !/^https?:\/\//i.test(tab.url)) {
    throw new Error('Threadline can only capture normal http/https pages. Browser settings, extension pages, new-tab pages, and store pages are blocked by the browser.');
  }

  const sessionId = await getActiveSessionId();
  const pageContext = await getPageContext(tab.id, tab.url, selectedOverride);
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
      mode,
      extractionMode: pageContext.metadata?.extractionMode ?? 'unknown'
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
