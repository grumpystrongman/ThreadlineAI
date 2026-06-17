type ThreadlinePageContext = {
  title: string;
  url: string;
  selection: string;
  visibleText: string;
  capturedAt: string;
  metadata: Record<string, string>;
};

function compact(value: string | null | undefined): string {
  return value?.replace(/\s+/g, ' ').trim() ?? '';
}

function collectPageText(maxCharacters = 12000): string {
  const bodyText = compact(document.body?.innerText);
  return bodyText.slice(0, maxCharacters);
}

chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
  if (message?.type !== 'THREADLINE_GET_PAGE_CONTEXT') return false;

  const context: ThreadlinePageContext = {
    title: document.title,
    url: location.href,
    selection: compact(window.getSelection()?.toString()),
    visibleText: collectPageText(message.maxCharacters ?? 12000),
    capturedAt: new Date().toISOString(),
    metadata: {
      origin: location.origin,
      host: location.host
    }
  };

  sendResponse(context);
  return true;
});
