import { callThreadline } from './threadlineClient.js';
import { sendActiveTab, sendBrowserContext } from './browserContext.js';

chrome.runtime.onInstalled.addListener(() => {
  chrome.contextMenus.create({ id: 'threadline-send-selection', title: 'Send selection to ThreadlineAI', contexts: ['selection'] });
  chrome.contextMenus.create({ id: 'threadline-send-page', title: 'Send page to ThreadlineAI', contexts: ['page'] });
});

chrome.contextMenus.onClicked.addListener(async (info, tab) => {
  try {
    if (!tab) throw new Error('No tab was available from context menu.');
    if (info.menuItemId === 'threadline-send-selection') await sendBrowserContext(tab, 'selection', info.selectionText);
    if (info.menuItemId === 'threadline-send-page') await sendBrowserContext(tab, 'page');
  } catch (error) {
    console.error(error);
  }
});

chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
  if (message?.type === 'THREADLINE_SEND_ACTIVE_TAB') {
    sendActiveTab(message.mode === 'selection' ? 'selection' : 'page')
      .then(() => sendResponse({ ok: true }))
      .catch((error) => sendResponse({ ok: false, error: error instanceof Error ? error.message : String(error) }));
    return true;
  }

  if (message?.type === 'THREADLINE_TEST_CONNECTION') {
    callThreadline('/health')
      .then((health) => sendResponse({ ok: true, health }))
      .catch((error) => sendResponse({ ok: false, error: error instanceof Error ? error.message : String(error) }));
    return true;
  }

  return false;
});
