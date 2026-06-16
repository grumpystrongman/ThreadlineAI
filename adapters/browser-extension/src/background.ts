type ThreadlineBrowserContext = { source: 'browser'; contextType: 'tab' | 'selection'; title?: string; url?: string; content: string; capturedAt: string };
const nativeHostName = 'com.threadlineai.context';
function sendToNativeHost(context: ThreadlineBrowserContext): Promise<void> {
  return new Promise((resolve, reject) => chrome.runtime.sendNativeMessage(nativeHostName, context, () => {
    const error = chrome.runtime.lastError;
    if (error) reject(new Error(error.message)); else resolve();
  }));
}
chrome.runtime.onInstalled.addListener(() => chrome.contextMenus.create({ id: 'threadline-send-selection', title: 'Send selection to ThreadlineAI', contexts: ['selection'] }));
chrome.contextMenus.onClicked.addListener(async (info, tab) => {
  if (info.menuItemId !== 'threadline-send-selection') return;
  await sendToNativeHost({ source: 'browser', contextType: 'selection', title: tab?.title, url: tab?.url, content: info.selectionText ?? '', capturedAt: new Date().toISOString() });
});
chrome.action.onClicked.addListener(async (tab) => await sendToNativeHost({ source: 'browser', contextType: 'tab', title: tab.title, url: tab.url, content: `${tab.title ?? ''}\n${tab.url ?? ''}`.trim(), capturedAt: new Date().toISOString() }));
