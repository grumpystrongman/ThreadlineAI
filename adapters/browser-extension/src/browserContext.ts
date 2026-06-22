import { callThreadline, getActiveSessionId, getSettings, limitText, redactText } from './threadlineClient.js';
import type { ExtractionLimits, ThreadlineSettings } from './threadlineClient.js';

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
  visibleText: string;
  articleText: string;
  selection: string;
  headings: string[];
  links: Array<{ text: string; href: string }>;
  images: Array<{ alt: string; src: string }>;
  tables: string[];
  warnings: string[];
  extractionMode: string;
  limits: Record<string, number>;
};

export async function getPageContext(tabId: number, tabUrl?: string, selectedOverride?: string): Promise<ThreadlinePageContext> {
  const settings = await getSettings();
  const injected = await chrome.scripting.executeScript({
    target: { tabId },
    func: (limits: ExtractionLimits) => {
      const compact = (value: string | null | undefined) => value?.replace(/\s+/g, ' ').trim() ?? '';
      const limit = (value: string, maxCharacters: number, label: string, warnings: string[]) => {
        if (value.length <= maxCharacters) return value;
        warnings.push(`${label} was trimmed to ${maxCharacters} characters.`);
        return `${value.slice(0, Math.max(0, maxCharacters - 36))}\n...[trimmed by Threadline extension]`;
      };
      const read = (selector: string) => Array.from(document.querySelectorAll(selector))
        .map(element => compact((element as HTMLElement).innerText || element.textContent))
        .filter(Boolean)
        .join('\n');
      const readHeadings = () => Array.from(document.querySelectorAll('h1,h2,h3,[role="heading"]'))
        .map(element => compact((element as HTMLElement).innerText || element.textContent))
        .filter(Boolean)
        .slice(0, limits.maxHeadings);
      const readLinks = () => Array.from(document.querySelectorAll('a[href]'))
        .map(anchor => ({
          text: compact((anchor as HTMLElement).innerText || anchor.textContent).slice(0, limits.maxLinkTextCharacters),
          href: (anchor as HTMLAnchorElement).href
        }))
        .filter(item => item.href)
        .slice(0, limits.maxLinks);
      const readImages = () => Array.from(document.querySelectorAll('img'))
        .map(image => ({ alt: compact((image as HTMLImageElement).alt), src: (image as HTMLImageElement).currentSrc || (image as HTMLImageElement).src }))
        .filter(item => item.alt || item.src)
        .slice(0, limits.maxImages);
      const readTables = (warnings: string[]) => Array.from(document.querySelectorAll('table'))
        .map(table => limit(compact((table as HTMLElement).innerText || table.textContent), limits.maxTableCharacters, 'A table', warnings))
        .filter(Boolean)
        .slice(0, limits.maxTables);
      const cleanGoogleChrome = (value: string) => value
        .replace(/\bShare\b.*?\bEditing\b/gi, ' ')
        .replace(/\bFile\b\s*\bEdit\b\s*\bView\b\s*\bInsert\b\s*\bFormat\b\s*\bTools\b\s*\bExtensions\b\s*\bHelp\b/gi, ' ')
        .replace(/\bNormal text\b.*?\bShow tabs & outlines\b/gi, ' ')
        .replace(/\bTurn on screen reader support\b.*?\bBanner hidden\b/gi, ' ')
        .replace(/\b\d\b(?:\s+\b\d\b){4,}/g, ' ')
        .replace(/\s+/g, ' ')
        .trim();

      const host = location.host;
      const warnings: string[] = [];
      let extractionMode = 'generic-dom';
      let articleRaw = read('article, main, [role="main"], .markdown-body, .prose, [itemprop="articleBody"], [data-testid*="article"]');
      let visibleRaw = compact(document.body?.innerText);

      if (host === 'docs.google.com' && location.pathname.includes('/document/')) {
        const docsText = read('.kix-lineview-content, .kix-wordhtmlgenerator-word-node, .docs-editor-container [aria-label], [role="textbox"]');
        visibleRaw = cleanGoogleChrome(docsText || visibleRaw);
        articleRaw = visibleRaw;
        extractionMode = 'google-docs-dom';
        if (!visibleRaw || visibleRaw.length < 120) warnings.push('Google Docs DOM extraction returned little document-like text. Threadline will try the structured export fallback next.');
      } else if (host === 'mail.google.com') {
        const gmailText = read('[role="main"], .aeF, .AO');
        visibleRaw = compact(gmailText || visibleRaw);
        articleRaw = visibleRaw;
        extractionMode = 'gmail-dom';
      } else if (articleRaw) {
        extractionMode = 'article-or-main-dom';
      }

      if (!visibleRaw) warnings.push('No visible body text was found in the page DOM. The site may render inside a canvas, iframe, shadow DOM, or browser-protected surface.');
      if (!articleRaw) warnings.push('No article/main text was found; visible page text was used as the full-page fallback.');

      const selectedText = limit(compact(window.getSelection()?.toString()), limits.maxSelectedTextCharacters, 'Selected text', warnings);
      const visibleText = limit(visibleRaw, limits.maxVisibleTextCharacters, 'Visible text', warnings);
      const articleText = limit(articleRaw || visibleRaw, limits.maxArticleTextCharacters, 'Article/main text', warnings);
      const plainText = limit(articleText || visibleText, limits.maxCharacters, 'Combined page text', warnings);

      const snapshot: BrowserSnapshot = {
        plainText,
        visibleText,
        articleText,
        selection: selectedText,
        headings: readHeadings(),
        links: readLinks(),
        images: readImages(),
        tables: readTables(warnings),
        warnings,
        extractionMode,
        limits: {
          maxCharacters: limits.maxCharacters,
          maxSelectedTextCharacters: limits.maxSelectedTextCharacters,
          maxVisibleTextCharacters: limits.maxVisibleTextCharacters,
          maxArticleTextCharacters: limits.maxArticleTextCharacters,
          maxHeadings: limits.maxHeadings,
          maxLinks: limits.maxLinks,
          maxImages: limits.maxImages,
          maxTables: limits.maxTables,
          maxTableCharacters: limits.maxTableCharacters,
          maxLinkTextCharacters: limits.maxLinkTextCharacters
        }
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
          snapshotFormat: 'browser-snapshot-v2',
          documentReadyState: document.readyState
        }
      };
    },
    args: [settings]
  });

  const context = injected[0]?.result as ThreadlinePageContext | undefined;
  if (!context) throw new Error('Browser page context could not be collected from this tab. Try a normal https:// page and reload the tab after loading the extension.');

  const explicitSelection = selectedOverride?.replace(/\s+/g, ' ').trim();
  if (explicitSelection) {
    context.selection = limitText(explicitSelection, settings.maxSelectedTextCharacters);
    context.metadata = { ...(context.metadata ?? {}), selectionSource: 'context-menu' };
  }

  const exportedSnapshot = await tryGetGoogleDocsStructuredExport(tabUrl ?? context.url, settings);
  if (exportedSnapshot) {
    context.visibleText = JSON.stringify(exportedSnapshot);
    context.metadata = { ...(context.metadata ?? {}), extractionMode: exportedSnapshot.extractionMode, snapshotFormat: 'browser-snapshot-v2' };
  }

  applyContextRedactions(context, settings);
  return context;
}

async function tryGetGoogleDocsStructuredExport(url: string, limits: ExtractionLimits): Promise<BrowserSnapshot | null> {
  const match = url.match(/https:\/\/docs\.google\.com\/document\/d\/([^/]+)/i);
  const documentId = match?.[1];
  if (!documentId) return null;

  const html = await fetchGoogleDocsExport(documentId, 'html');
  if (html) {
    const parsed = parseGoogleDocsHtmlExport(html, limits);
    if (parsed.plainText.length > 0) return parsed;
  }

  const text = await fetchGoogleDocsExport(documentId, 'txt');
  if (text && !/^\s*</.test(text) && !text.includes('<html')) {
    const plainText = limitText(text.replace(/\s+/g, ' ').trim(), limits.maxCharacters);
    return {
      plainText,
      visibleText: plainText,
      articleText: plainText,
      selection: '',
      headings: [],
      links: [],
      images: [],
      tables: [],
      warnings: ['Used Google Docs plain text export fallback.'],
      extractionMode: 'google-docs-text-export',
      limits: snapshotLimits(limits)
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

function parseGoogleDocsHtmlExport(html: string, limits: ExtractionLimits): BrowserSnapshot {
  const stripped = html
    .replace(/<script[\s\S]*?<\/script>/gi, ' ')
    .replace(/<style[\s\S]*?<\/style>/gi, ' ');
  const plainText = limitText(decodeHtml(stripped.replace(/<[^>]+>/g, ' ')).replace(/\s+/g, ' ').trim(), limits.maxCharacters);
  const headings = extractTaggedText(stripped, ['h1', 'h2', 'h3', 'h4'], limits.maxHeadings);
  const links = extractLinks(stripped, limits.maxLinks, limits.maxLinkTextCharacters);
  const images = extractImages(stripped, limits.maxImages);
  const tables = extractTaggedText(stripped, ['table'], limits.maxTables).map(table => limitText(table, limits.maxTableCharacters));
  return {
    plainText,
    visibleText: plainText,
    articleText: plainText,
    selection: '',
    headings,
    links,
    images,
    tables,
    warnings: ['Parsed Google Docs HTML export using lightweight parser.'],
    extractionMode: 'google-docs-html-export',
    limits: snapshotLimits(limits)
  };
}

function extractTaggedText(html: string, tags: string[], limit: number): string[] {
  const results: string[] = [];
  for (const tag of tags) {
    const expression = new RegExp(`<${tag}[^>]*>([\\s\\S]*?)<\\/${tag}>`, 'gi');
    let match = expression.exec(html);
    while (match && results.length < limit) {
      const text = decodeHtml(match[1].replace(/<[^>]+>/g, ' ')).replace(/\s+/g, ' ').trim();
      if (text) results.push(text);
      match = expression.exec(html);
    }
  }
  return results;
}

function extractLinks(html: string, limit: number, maxTextCharacters: number): Array<{ text: string; href: string }> {
  const results: Array<{ text: string; href: string }> = [];
  const expression = /<a[^>]+href=["']([^"']+)["'][^>]*>([\s\S]*?)<\/a>/gi;
  let match = expression.exec(html);
  while (match && results.length < limit) {
    const href = decodeHtml(match[1]).trim();
    const text = decodeHtml(match[2].replace(/<[^>]+>/g, ' ')).replace(/\s+/g, ' ').trim().slice(0, maxTextCharacters);
    if (href) results.push({ text, href });
    match = expression.exec(html);
  }
  return results;
}

function extractImages(html: string, limit: number): Array<{ alt: string; src: string }> {
  const results: Array<{ alt: string; src: string }> = [];
  const expression = /<img\b[^>]*>/gi;
  let match = expression.exec(html);
  while (match && results.length < limit) {
    const tag = match[0];
    const src = decodeHtml((tag.match(/\bsrc=["']([^"']+)["']/i)?.[1] ?? '').trim());
    const alt = decodeHtml((tag.match(/\balt=["']([^"']*)["']/i)?.[1] ?? '').trim());
    if (src || alt) results.push({ alt, src });
    match = expression.exec(html);
  }
  return results;
}

function decodeHtml(value: string): string {
  return value
    .replace(/&nbsp;/g, ' ')
    .replace(/&amp;/g, '&')
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&quot;/g, '"')
    .replace(/&#39;/g, "'");
}

function snapshotLimits(limits: ExtractionLimits): Record<string, number> {
  return {
    maxCharacters: limits.maxCharacters,
    maxSelectedTextCharacters: limits.maxSelectedTextCharacters,
    maxVisibleTextCharacters: limits.maxVisibleTextCharacters,
    maxArticleTextCharacters: limits.maxArticleTextCharacters,
    maxHeadings: limits.maxHeadings,
    maxLinks: limits.maxLinks,
    maxImages: limits.maxImages,
    maxTables: limits.maxTables,
    maxTableCharacters: limits.maxTableCharacters,
    maxLinkTextCharacters: limits.maxLinkTextCharacters
  };
}

function applyContextRedactions(context: ThreadlinePageContext, settings: ThreadlineSettings): void {
  const hooks = new Set<string>();
  let findingCount = 0;
  const redact = (value: string): string => {
    const result = redactText(value, settings.redactionPatterns);
    findingCount += result.findingCount;
    result.hooks.forEach(hook => hooks.add(hook));
    return result.text;
  };

  context.title = redact(context.title);
  context.url = redact(context.url);
  context.selection = redact(context.selection);

  try {
    const snapshot = JSON.parse(context.visibleText) as BrowserSnapshot;
    snapshot.plainText = redact(snapshot.plainText);
    snapshot.visibleText = redact(snapshot.visibleText);
    snapshot.articleText = redact(snapshot.articleText);
    snapshot.selection = redact(snapshot.selection);
    snapshot.headings = snapshot.headings.map(redact);
    snapshot.links = snapshot.links.map(link => ({ text: redact(link.text), href: redact(link.href) }));
    snapshot.images = snapshot.images.map(image => ({ alt: redact(image.alt), src: redact(image.src) }));
    snapshot.tables = snapshot.tables.map(redact);
    if (findingCount > 0) snapshot.warnings = [...snapshot.warnings, `${findingCount} sensitive value(s) were redacted in the extension before capture.`];
    context.visibleText = JSON.stringify(snapshot);
  } catch {
    context.visibleText = redact(context.visibleText);
  }

  context.metadata = {
    ...(context.metadata ?? {}),
    redactionFindingCount: findingCount.toString(),
    redactionHooks: hooks.size > 0 ? Array.from(hooks).join(',') : 'none'
  };
}

export function buildBrowserContent(context: ThreadlinePageContext, mode: BrowserContextMode): string {
  const selected = context.selection?.trim();
  const body = mode === 'selection' && selected ? selected : formatSnapshot(context.visibleText);
  const parts = [
    `Title: ${context.title}`,
    `URL: ${context.url}`,
    `Extraction: ${context.metadata?.extractionMode ?? 'unknown'}`,
    `Snapshot: ${context.metadata?.snapshotFormat ?? 'unknown'}`,
    selected ? `Selected text available: yes (${selected.length} chars)` : 'Selected text available: no',
    '',
    body
  ];
  return parts.join('\n').trim();
}

function formatSnapshot(value: string): string {
  try {
    const snapshot = JSON.parse(value) as BrowserSnapshot;
    const parts = [
      snapshot.articleText ? `Article/main text:\n${snapshot.articleText}` : '',
      snapshot.visibleText && snapshot.visibleText !== snapshot.articleText ? `\nVisible text:\n${snapshot.visibleText}` : '',
      snapshot.selection ? `\nSelected text:\n${snapshot.selection}` : '',
      snapshot.headings.length ? `\nHeadings:\n${snapshot.headings.map(item => `- ${item}`).join('\n')}` : '',
      snapshot.links.length ? `\nLinks:\n${snapshot.links.map(item => `- ${item.text || item.href}: ${item.href}`).join('\n')}` : '',
      snapshot.images.length ? `\nImages:\n${snapshot.images.map(item => `- ${item.alt || '(no alt)'}: ${item.src}`).join('\n')}` : '',
      snapshot.tables.length ? `\nTables:\n${snapshot.tables.map(item => `- ${item}`).join('\n')}` : '',
      snapshot.warnings.length ? `\nWarnings:\n${snapshot.warnings.map(item => `- ${item}`).join('\n')}` : ''
    ];
    return parts.filter(Boolean).join('\n').trim() || snapshot.plainText;
  } catch {
    return value;
  }
}

export function explainUnavailableTab(tab: chrome.tabs.Tab): string | undefined {
  const url = tab.url ?? '';
  if (!url) return 'This page is unavailable because Chrome has not exposed a URL for the active tab yet.';
  if (/^chrome:\/\//i.test(url)) return 'This page is unavailable because Chrome internal pages block extension DOM access.';
  if (/^edge:\/\//i.test(url)) return 'This page is unavailable because Edge internal pages block extension DOM access.';
  if (/^about:/i.test(url)) return 'This page is unavailable because browser about/new-tab pages do not expose page text to extensions.';
  if (/^devtools:/i.test(url)) return 'This page is unavailable because DevTools does not expose page DOM to normal extension capture.';
  if (/^chrome-extension:\/\//i.test(url)) return 'This page is unavailable because extension pages are protected from page capture.';
  if (/^https:\/\/chromewebstore\.google\.com\//i.test(url)) return 'This page is unavailable because Chrome Web Store pages block extension capture.';
  if (!/^https?:\/\//i.test(url)) return 'This page is unavailable because Threadline full-page capture only supports normal http/https tabs.';
  return undefined;
}

export async function sendBrowserContext(tab: chrome.tabs.Tab, mode: BrowserContextMode, selectedOverride?: string): Promise<void> {
  if (!tab.id) throw new Error('No active tab id was available.');
  const unavailable = explainUnavailableTab(tab);
  if (unavailable) {
    throw new Error(`${unavailable} Threadline can still use Chrome title-only context through the Windows active-window path; open a normal website or web app tab for full page context.`);
  }

  const settings = await getSettings();
  const sessionId = await getActiveSessionId();
  const pageContext = await getPageContext(tab.id, tab.url, selectedOverride);
  const content = limitText(buildBrowserContent(pageContext, mode), settings.maxCharacters);
  if (!content) throw new Error('No browser context was captured. The page may render in a protected iframe, canvas, shadow DOM, or another surface unavailable to extension DOM extraction.');

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
      snapshotFormat: pageContext.metadata?.snapshotFormat ?? 'none',
      redactionFindingCount: pageContext.metadata?.redactionFindingCount ?? '0',
      redactionHooks: pageContext.metadata?.redactionHooks ?? 'none',
      documentReadyState: pageContext.metadata?.documentReadyState ?? 'unknown'
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
