type PopupResponse = { ok: boolean; error?: string; health?: unknown; status?: ExtensionStatus; heartbeat?: unknown; registration?: unknown };

type ExtensionStatus = {
  identity?: {
    title?: string;
    url?: string;
    status?: string;
    incognito?: boolean;
  };
  heartbeatAgeLabel?: string;
  titleOnlyGuidance?: string;
};

const statusElement = document.querySelector<HTMLParagraphElement>('#status')!;
const tabElement = document.querySelector<HTMLParagraphElement>('#currentTab')!;
const heartbeatElement = document.querySelector<HTMLParagraphElement>('#heartbeat')!;
const guidanceElement = document.querySelector<HTMLParagraphElement>('#guidance')!;
const testConnection = document.querySelector<HTMLButtonElement>('#testConnection')!;
const setup = document.querySelector<HTMLButtonElement>('#setup')!;
const heartbeat = document.querySelector<HTMLButtonElement>('#heartbeatNow')!;
const sendPage = document.querySelector<HTMLButtonElement>('#sendPage')!;
const sendSelection = document.querySelector<HTMLButtonElement>('#sendSelection')!;

function setStatus(message: string): void {
  statusElement.textContent = message;
}

function sendMessage(message: unknown): Promise<PopupResponse> {
  return chrome.runtime.sendMessage(message);
}

async function refresh(): Promise<void> {
  const response = await sendMessage({ type: 'THREADLINE_GET_EXTENSION_STATUS' });
  if (!response.ok || !response.status) {
    setStatus(`Status failed: ${response.error ?? 'Unknown error.'}`);
    return;
  }

  const identity = response.status.identity;
  tabElement.textContent = identity?.title ? `Current tab: ${identity.title}` : 'Current tab: unavailable.';
  heartbeatElement.textContent = `Extension heartbeat: ${response.status.heartbeatAgeLabel ?? 'never'}.`;
  guidanceElement.textContent = response.status.titleOnlyGuidance ?? '';
}

testConnection.addEventListener('click', async () => {
  setStatus('Testing connection...');
  const response = await sendMessage({ type: 'THREADLINE_TEST_CONNECTION' });
  setStatus(response.ok ? 'Threadline service is reachable.' : `Connection failed: ${response.error}`);
});

setup.addEventListener('click', async () => {
  setStatus('Registering extension...');
  const response = await sendMessage({ type: 'THREADLINE_REGISTER_EXTENSION' });
  setStatus(response.ok ? 'Extension registered with Threadline. Doctor can now see it.' : `Setup failed: ${response.error}`);
  await refresh();
});

heartbeat.addEventListener('click', async () => {
  setStatus('Sending heartbeat...');
  const response = await sendMessage({ type: 'THREADLINE_HEARTBEAT' });
  setStatus(response.ok ? 'Heartbeat sent.' : `Heartbeat failed: ${response.error}`);
  await refresh();
});

sendPage.addEventListener('click', async () => {
  setStatus('Sending page...');
  const response = await sendMessage({ type: 'THREADLINE_SEND_ACTIVE_TAB', mode: 'page' });
  setStatus(response.ok ? 'Page sent to Threadline.' : `Send failed: ${response.error}`);
  await refresh();
});

sendSelection.addEventListener('click', async () => {
  setStatus('Sending selection...');
  const response = await sendMessage({ type: 'THREADLINE_SEND_ACTIVE_TAB', mode: 'selection' });
  setStatus(response.ok ? 'Selection sent to Threadline.' : `Send failed: ${response.error}`);
  await refresh();
});

await refresh();

export {};
