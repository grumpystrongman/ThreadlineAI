import { sendActiveTab, sendBrowserContext } from './browserContext.js';
import {
  BrowserTabIdentity,
  extensionVersion,
  formatHeartbeatAge,
  getLastHeartbeatAgeMs,
  registerBrowserExtension,
  sendExtensionHeartbeat,
  testThreadlineConnection
} from './threadlineClient.js';

const heartbeatAlarmName = 'threadline-extension-heartbeat';
const heartbeatPeriodMinutes = 1;

type RuntimeMessage = {
  type?: string;
  mode?: string;
};

async function getCurrentTabIdentity(): Promise<BrowserTabIdentity | undefined> {
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  if (!tab) return undefined;

  return {
    tabId: tab.id,
    windowId: tab.windowId,
    title: tab.title,
    url: tab.url,
    status: tab.status,
    incognito: tab.incognito,
    capturedAt: new Date().toISOString()
  };
}

function unavailableReason(identity?: BrowserTabIdentity): string | undefined {
  const url = identity?.url ?? '';
  if (!url) return 'Chrome did not expose an active tab URL yet.';
  if (/^chrome:\/\//i.test(url)) return 'Chrome internal pages block extension page capture.';
  if (/^edge:\/\//i.test(url)) return 'Edge internal pages block extension page capture.';
  if (/^about:/i.test(url)) return 'Browser about/new-tab pages do not expose page DOM to extensions.';
  if (/^devtools:/i.test(url)) return 'DevTools pages do not expose page DOM to extensions.';
  if (/^chrome-extension:\/\//i.test(url)) return 'Extension pages cannot be captured by another extension context.';
  if (/^chromewebstore\.google\.com/i.test(url)) return 'Chrome Web Store pages block normal extension capture.';
  if (!/^https?:\/\//i.test(url)) return 'Only normal http/https pages expose full page context to Threadline.';
  return undefined;
}

function buildTitleOnlyGuidance(identity?: BrowserTabIdentity): string {
  const reason = unavailableReason(identity);
  if (reason) {
    return `This page is unavailable for full browser context. ${reason} Threadline can still use Chrome title-only context from the Windows active-window path, but Send page/selection needs a normal http/https tab.`;
  }

  if (identity?.status && identity.status !== 'complete') {
    return 'This tab is still loading. Chrome title-only context may be available now; full page context is better after the page finishes loading.';
  }

  return 'Chrome title-only context comes from the Windows active-window path. Use Send page or Send selection when you want Threadline to receive title, URL, selected text, visible text, article/main text, DOM metadata, and redaction metadata.';
}

function installMenusAndAlarm(): void {
  chrome.contextMenus.removeAll(() => {
    chrome.contextMenus.create({ id: 'threadline-send-selection', title: 'Send selection to ThreadlineAI', contexts: ['selection'] });
    chrome.contextMenus.create({ id: 'threadline-send-page', title: 'Send page to ThreadlineAI', contexts: ['page'] });
  });

  chrome.alarms.create(heartbeatAlarmName, { periodInMinutes: heartbeatPeriodMinutes });
}

async function heartbeatOnce(): Promise<void> {
  const identity = await getCurrentTabIdentity();
  const result = await sendExtensionHeartbeat(identity);
  if (!result.ok) {
    console.warn('Threadline heartbeat failed:', result.error);
  }
}

chrome.runtime.onInstalled.addListener(() => {
  installMenusAndAlarm();
  void heartbeatOnce();
});

chrome.runtime.onStartup.addListener(() => {
  installMenusAndAlarm();
  void heartbeatOnce();
});

chrome.alarms.onAlarm.addListener((alarm) => {
  if (alarm.name === heartbeatAlarmName) {
    void heartbeatOnce();
  }
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

chrome.runtime.onMessage.addListener((message: RuntimeMessage, _sender, sendResponse) => {
  if (message?.type === 'THREADLINE_SEND_ACTIVE_TAB') {
    sendActiveTab(message.mode === 'selection' ? 'selection' : 'page')
      .then(() => sendResponse({ ok: true }))
      .catch((error) => sendResponse({ ok: false, error: error instanceof Error ? error.message : String(error) }));
    return true;
  }

  if (message?.type === 'THREADLINE_TEST_CONNECTION') {
    testThreadlineConnection()
      .then((health) => sendResponse({ ok: true, health }))
      .catch((error) => sendResponse({ ok: false, error: error instanceof Error ? error.message : String(error) }));
    return true;
  }

  if (message?.type === 'THREADLINE_REGISTER_EXTENSION') {
    getCurrentTabIdentity()
      .then((identity) => registerBrowserExtension(identity))
      .then((registration) => sendResponse({ ok: true, registration }))
      .catch((error) => sendResponse({ ok: false, error: error instanceof Error ? error.message : String(error) }));
    return true;
  }

  if (message?.type === 'THREADLINE_HEARTBEAT') {
    getCurrentTabIdentity()
      .then((identity) => sendExtensionHeartbeat(identity))
      .then((heartbeat) => sendResponse(heartbeat.ok ? { ok: true, heartbeat } : { ok: false, error: heartbeat.error, heartbeat }))
      .catch((error) => sendResponse({ ok: false, error: error instanceof Error ? error.message : String(error) }));
    return true;
  }

  if (message?.type === 'THREADLINE_GET_EXTENSION_STATUS') {
    Promise.all([getCurrentTabIdentity(), getLastHeartbeatAgeMs()])
      .then(([identity, heartbeatAge]) => sendResponse({
        ok: true,
        status: {
          extensionVersion,
          identity,
          heartbeatAgeLabel: formatHeartbeatAge(heartbeatAge),
          titleOnlyGuidance: buildTitleOnlyGuidance(identity),
          unavailableReason: unavailableReason(identity)
        }
      }))
      .catch((error) => sendResponse({ ok: false, error: error instanceof Error ? error.message : String(error) }));
    return true;
  }

  return false;
});
