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

type BrowserSnapshot = {
  plainText: string;
  headings: string[];
  links: Array<{ text: string; href: string }>;
  images: Array<{ alt: string; src: string }>;
  tables: string[];
  warnings: string[];
  extractionMode: string;
};

export async function getPageContext(tabId: number, tabUrl?: string, selectedOverride?: string): Promise<ThreadlinePageContext> {
  const settings = await getSettings();
  const injected = await chrome.scripting.executeScript({
    target: { tabId },
    func: (maxCharacters: number) => {
      const compact = (value: string | null | undefined) => value?.replace(/\s+/g, ' ').trim() ?? '';
      const read = (selector: string) => Array.from(document.querySelectorAll(selector)).map(element => compact((element as HTMLElement).innerText || element.textContent)).filter(Boolean).join('\n');
      const readHeadings = () => Array.from(document.querySelectorAll('h1,h2,h3,[role="heading"]')).map(element => compact((element as HTMLElement).innerText || element.textContent)).filter(Boolean).slice(0, 40);
      const readLinks = () => Array.from(document.querySelectorAll('a[href]')).map(anchor => ({ text: compact((anchor as HTMLElement).innerText || anchor.textContent), href: (anchor as HTMLAnchorElement).href })).filter(item => item.href).slice(0, 80);
      const readImages = () => Array.from(document.querySelectorAll('img')).map(image => ({ alt: compact((image as HTMLImageElement).alt), src: (image as HTMLImageElement).currentSrc || (image as HTMLImageElement).src })).filter(item => item.alt || item.src).slice(0, 80);
      const readTables = () => Array.from(document.querySelectorAll('table')).map(table => compact((table as HTMLElement).innerText || table.textContent)).filter(Boolean).slice(0, 20);
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
      let extractionMode = 'generic-dom';
      const warnings: string[] = [];

      if (host === 'docs.google.com' && location.pathname.includes('/document/')) {
        const docsText = read('.kix-lineview-content, .kix-wordhtmlgenerator-word-node, .docs-editor-container [aria-label], [role="textbox"]');
        extracted = cleanGoogleChrome(docsText || compact(document.body?.innerText));
        extractionMode = 'google-docs-dom';
        if (!extracted || extracted.length < 120) warnings.push('Google Docs DOM extraction returned little document-like text.');
      } else if (host === 'mail.google.com') {
        const gmailText = read('[role="main"], .aeF, .AO');
        extracted = compact(gmailText || document.body?.innerText);
        extractionMode = 'gmail-dom';
      } else {
        const mainText = read('main, article, [role="main"]');
        extracted = compact(mainText || document.body?.innerText);
      }

      const snapshot: BrowserSnapshot = {
        plainText: extracted.slice(0, maxCharacters),
        headings: readHeadings(),
        links: readLinks(),
        images: readImages(),
        tables: readTables(),
        warnings,
        extractionMode
      };

      return {
        title: document.title,
        url: location.href,
        selection: selectedText,
        visibleText: JSON.stringify(snapshot),
        capturedAt: new Date().toISOString(),
        metadata: {
          origin: location.origin,
          host,
          extractionMode,
          snapshotFormat: 'browser-snapshot-v1'
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

  const exportedSnapshot = await tryGetGoogleDocsStructuredExport(tabUrl ?? context.url, settings.maxCharacters);
  if (exportedSnapshot) {
    context.visibleText = JSON.stringify(exportedSnapshot);
    context.metadata = { ...(context.metadata ?? {}), extractionMode: exportedSnapshot.extractionMode, snapshotFormat: 'browser-snapshot-v1' };
  }

  return context;
}

async function tryGetGoogleDocsStructuredExport(url: string, maxCharacters: number): Promise<BrowserSnapshot | null> {
  const match = url.match(/https:\/\/docs\.google\.com\/document\/d\/([^/]+)/i);
  const documentId = match?.[1];
  if (!documentId) return null;

  const html = await fetchGoogleDocsExport(documentId, 'html');
  if (html) {
    const parsed = parseGoogleDocsHtmlExport(html, maxCharacters);
    if (parsed.plainText.length > 0) return parsed;
  }

  const text = await fetchGoogleDocsExport(documentId, 'txt');
  if (text && !/^\s*</.test(text) && !text.includes('<html')) {
    return {
      plainText: text.replace(/\s+/g, ' ').trim().slice(0, maxCharacters),
      headings: [],
      links: [],
      images: [],
      tables: [],
      warnings: ['Used Google Docs plain text export fallback.'],
      extractionMode: 'google-docs-text-export'
    };
  }

  return null;
}

async function fetchGoogleDocsExport(documentId: string, format: 'html' | 'txt'): Promise<string | null> {
  try {
    const response = await fetch(`https://docs.google.com/document/d/${documentId}/export?format=${format}`, { credentials: 'include' });
    if (!response.ok) return null;
    const text = await response.text();
    return text.trim() ? text : null;
  } catch {
    return null;
  }
}

function parseGoogleDocsHtmlExport(html: string, maxCharacters: number): BrowserSnapshot {
  const parser = new DOMParser();
  const doc = parser.parseFromString(html, 'text/html');
  const compact = (value: string | null | undefined) => value?.replace(/\s+/g, ' ').trim() ?? '';
  const bodyText = compact(doc.body?.innerText || doc.body?.textContent).slice(0, maxCharacters);
  const headings = Array.from(doc.querySelectorAll('h1,h2,h3,h4')).map(element => compact(element.textContent)).filter(Boolean).slice(0, 60);
  const links = Array.from(doc.querySelectorAll('a[href]')).map(anchor => ({ text: compact(anchor.textContent), href: (anchor as HTMLAnchorElement).href || anchor.getAttribute('href') || '' })).filter(item => item.href).slice(0, 120);
  const images = Array.from(doc.querySelectorAll('img')).map(image => ({ alt: compact((image as HTMLImageElement).alt), src: (image as HTMLImageElement).src || image.getAttribute('src') || '' })).filter(item => item.alt || item.src).slice(0, 120);
  const tables = Array.from(doc.querySelectorAll('table')).map(table => compact((table as HTMLElement).innerText || table.textContent)).filter(Boolean).slice(0, 40);
  return {
    plainText: bodyText,
    headings,
    links,
    images,
    tables,
    warnings: [],
    extractionMode: 'google-docs-html-export'
  };
}

export function buildBrowserContent(context: ThreadlinePageContext, mode: BrowserContextMode): string {
  const selected = context.selection?.trim();
  const body = mode === 'selection' && selected ? selected : formatSnapshot(context.visibleText);
  return [`Title: ${context.title}`, `URL: ${context.url}`, `Extraction: ${context.metadata?.extractionMode ?? 'unknown'}`, '', body].join('\n').trim();
}

function formatSnapshot(value: string): string {
  try {
    const snapshot = JSON.parse(value) as BrowserSnapshot;
    const parts = [
      snapshot.plainText,
      snapshot.headings.length ? `\nHeadings:\n${snapshot.headings.map(item => `- ${item}`).join('\n')}` : '',
      snapshot.links.length ? `\nLinks:\n${snapshot.links.map(item => `- ${item.text || item.href}: ${item.href}`).join('\n')}` : '',
      snapshot.images.length ? `\nImages:\n${snapshot.images.map(item => `- ${item.alt || '(no alt)'}: ${item.src}`).join('\n')}` : '',
      snapshot.tables.length ? `\nTables:\n${snapshot.tables.map(item => `- ${item}`).join('\n')}` : '',
      snapshot.warnings.length ? `\nWarnings:\n${snapshot.warnings.map(item => `- ${item}`).join('\n')}` : ''
    ];
    return parts.filter(Boolean).join('\n').trim();
  } catch {
    return value;
  }
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
      extractionMode: pageContext.metadata?.extractionMode ?? 'unknown',
      snapshotFormat: pageContext.metadata?.snapshotFormat ?? 'none'
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
