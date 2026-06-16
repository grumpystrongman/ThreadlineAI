function getVisibleText(): string {
  const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
  const parts: string[] = [];
  let node: Node | null;
  while ((node = walker.nextNode())) {
    const value = node.textContent?.replace(/\s+/g, ' ').trim();
    if (value && value.length > 2) parts.push(value);
    if (parts.join(' ').length > 8000) break;
  }
  return parts.join('\n');
}
chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
  if (message?.type !== 'THREADLINE_GET_VISIBLE_TEXT') return;
  sendResponse({ title: document.title, url: location.href, content: getVisibleText(), capturedAt: new Date().toISOString() });
});
