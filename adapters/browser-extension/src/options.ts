import { defaultSettings } from './threadlineClient.js';

const baseUrl = document.querySelector<HTMLInputElement>('#baseUrl')!;
const localAccessToken = document.querySelector<HTMLInputElement>('#localAccessToken')!;
const maxCharacters = document.querySelector<HTMLInputElement>('#maxCharacters')!;
const save = document.querySelector<HTMLButtonElement>('#save')!;
const status = document.querySelector<HTMLParagraphElement>('#status')!;

async function load(): Promise<void> {
  const stored = await chrome.storage.local.get(['baseUrl', 'localAccessToken', 'maxCharacters']);
  baseUrl.value = typeof stored.baseUrl === 'string' ? stored.baseUrl : defaultSettings.baseUrl;
  localAccessToken.value = typeof stored.localAccessToken === 'string' ? stored.localAccessToken : '';
  maxCharacters.value = String(Number.isFinite(stored.maxCharacters) ? stored.maxCharacters : defaultSettings.maxCharacters);
}

save.addEventListener('click', async () => {
  await chrome.storage.local.set({
    baseUrl: baseUrl.value.trim() || defaultSettings.baseUrl,
    localAccessToken: localAccessToken.value.trim(),
    maxCharacters: Number(maxCharacters.value) || defaultSettings.maxCharacters
  });
  status.textContent = 'Saved.';
});

await load();
