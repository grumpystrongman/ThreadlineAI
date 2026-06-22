type ThreadlinePageContext = {
  title: string;
  url: string;
  selection: string;
  visibleText: string;
  capturedAt: string;
  metadata: Record<string, string>;
};

type ExtractionLimits = {
  maxCharacters?: number;
  maxSelectedTextCharacters?: number;
  maxVisibleTextCharacters?: number;
  maxArticleTextCharacters?: number;
  maxHeadings?: number;
  maxLinks?: number;
  maxImages?: number;
  maxTables?: number;
  maxTableCharacters?: number;
  maxLinkTextCharacters?: number;
};

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

const defaultLimits: Required<ExtractionLimits> = {
  maxCharacters: 12000,
  maxSelectedTextCharacters: 4000,
  maxVisibleTextCharacters: 12000,
  maxArticleTextCharacters: 12000,
  maxHeadings: 40,
  maxLinks: 80,
  maxImages: 80,
  maxTables: 20,
  maxTableCharacters: 2000,
  maxLinkTextCharacters: 240
};

function compact(value: string | null | undefined): string {
  return value?.replace(/\s+/g, ' ').trim() ?? '';
}

function coerceLimits(input: ExtractionLimits | undefined): Required<ExtractionLimits> {
  return { ...defaultLimits, ...(input ?? {}) };
}

function limitText(value: string, maxCharacters: number, label: string, warnings: string[]): string {
  if (value.length <= maxCharacters) return value;
  warnings.push(`${label} was trimmed to ${maxCharacters} characters.`);
  return `${value.slice(0, Math.max(0, maxCharacters - 36))}\n...[trimmed by Threadline extension]`;
}

function read(selector: string): string {
  return Array.from(document.querySelectorAll(selector))
    .map(element => compact((element as HTMLElement).innerText || element.textContent))
    .filter(Boolean)
    .join('\n');
}

function collectPageSnapshot(inputLimits?: ExtractionLimits): BrowserSnapshot {
  const limits = coerceLimits(inputLimits);
  const warnings: string[] = [];
  const selection = limitText(compact(window.getSelection()?.toString()), limits.maxSelectedTextCharacters, 'Selected text', warnings);
  const visibleRaw = compact(document.body?.innerText);
  const articleRaw = read('article, main, [role="main"], .markdown-body, .prose, [itemprop="articleBody"]');
  const visibleText = limitText(visibleRaw, limits.maxVisibleTextCharacters, 'Visible text', warnings);
  const articleText = limitText(articleRaw || visibleRaw, limits.maxArticleTextCharacters, 'Article/main text', warnings);
  const plainText = limitText(articleText || visibleText, limits.maxCharacters, 'Combined page text', warnings);

  if (!visibleRaw) warnings.push('No visible body text was found in the page DOM.');
  if (!articleRaw) warnings.push('No article/main text was found; visible page text was used as the fallback.');

  return {
    plainText,
    visibleText,
    articleText,
    selection,
    headings: Array.from(document.querySelectorAll('h1,h2,h3,[role="heading"]'))
      .map(element => compact((element as HTMLElement).innerText || element.textContent))
      .filter(Boolean)
      .slice(0, limits.maxHeadings),
    links: Array.from(document.querySelectorAll('a[href]'))
      .map(anchor => ({ text: compact((anchor as HTMLElement).innerText || anchor.textContent).slice(0, limits.maxLinkTextCharacters), href: (anchor as HTMLAnchorElement).href }))
      .filter(item => item.href)
      .slice(0, limits.maxLinks),
    images: Array.from(document.querySelectorAll('img'))
      .map(image => ({ alt: compact((image as HTMLImageElement).alt), src: (image as HTMLImageElement).currentSrc || (image as HTMLImageElement).src }))
      .filter(item => item.alt || item.src)
      .slice(0, limits.maxImages),
    tables: Array.from(document.querySelectorAll('table'))
      .map(table => limitText(compact((table as HTMLElement).innerText || table.textContent), limits.maxTableCharacters, 'A table', warnings))
      .filter(Boolean)
      .slice(0, limits.maxTables),
    warnings,
    extractionMode: articleRaw ? 'article-or-main-dom' : 'generic-dom',
    limits
  };
}

chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
  if (message?.type !== 'THREADLINE_GET_PAGE_CONTEXT') return false;

  const snapshot = collectPageSnapshot(message.limits ?? { maxCharacters: message.maxCharacters });
  const context: ThreadlinePageContext = {
    title: document.title,
    url: location.href,
    selection: snapshot.selection,
    visibleText: JSON.stringify(snapshot),
    capturedAt: new Date().toISOString(),
    metadata: {
      origin: location.origin,
      host: location.host,
      extractionMode: snapshot.extractionMode,
      snapshotFormat: 'browser-snapshot-v2',
      documentReadyState: document.readyState
    }
  };

  sendResponse(context);
  return true;
});
