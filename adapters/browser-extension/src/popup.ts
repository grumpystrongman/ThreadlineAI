type PopupResponse = { ok: boolean; error?: string; health?: unknown };

const status = document.querySelector<HTMLParagraphElement>('#status')!;
const testConnection = document.querySelector<HTMLButtonElement>('#testConnection')!;
const sendPage = document.querySelector<HTMLButtonElement>('#sendPage')!;
const sendSelection = document.querySelector<HTMLButtonElement>('#sendSelection')!;

function setStatus(message: string): void {
  status.textContent = message;
}

function sendMessage(message: unknown): Promise<PopupResponse> {
  return chrome.runtime.sendMessage(message);
}

testConnection.addEventListener('click', async () => {
  setStatus('Testing connection...');
  const response = await sendMessage({ type: 'THREADLINE_TEST_CONNECTION' });
  setStatus(response.ok ? 'Threadline service is reachable.' : `Connection failed: ${response.error}`);
});

sendPage.addEventListener('click', async () => {
  setStatus('Sending page...');
  const response = await sendMessage({ type: 'THREADLINE_SEND_ACTIVE_TAB', mode: 'page' });
  setStatus(response.ok ? 'Page sent to Threadline.' : `Send failed: ${response.error}`);
});

sendSelection.addEventListener('click', async () => {
  setStatus('Sending selection...');
  const response = await sendMessage({ type: 'THREADLINE_SEND_ACTIVE_TAB', mode: 'selection' });
  setStatus(response.ok ? 'Selection sent to Threadline.' : `Send failed: ${response.error}`);
});
