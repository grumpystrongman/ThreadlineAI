import { defaultSettings, saveSettings } from './threadlineClient.js';

const baseUrl = document.querySelector<HTMLInputElement>('#baseUrl')!;
const localAccessToken = document.querySelector<HTMLInputElement>('#localAccessToken')!;
const maxCharacters = document.querySelector<HTMLInputElement>('#maxCharacters')!;
const maxSelectedTextCharacters = document.querySelector<HTMLInputElement>('#maxSelectedTextCharacters')!;
const maxVisibleTextCharacters = document.querySelector<HTMLInputElement>('#maxVisibleTextCharacters')!;
const maxArticleTextCharacters = document.querySelector<HTMLInputElement>('#maxArticleTextCharacters')!;
const maxLinks = document.querySelector<HTMLInputElement>('#maxLinks')!;
const maxImages = document.querySelector<HTMLInputElement>('#maxImages')!;
const maxTables = document.querySelector<HTMLInputElement>('#maxTables')!;
const redactionPatterns = document.querySelector<HTMLTextAreaElement>('#redactionPatterns')!;
const save = document.querySelector<HTMLButtonElement>('#save')!;
const status = document.querySelector<HTMLParagraphElement>('#status')!;

async function load(): Promise<void> {
  const stored = await chrome.storage.local.get([
    'baseUrl',
    'localAccessToken',
    'maxCharacters',
    'maxSelectedTextCharacters',
    'maxVisibleTextCharacters',
    'maxArticleTextCharacters',
    'maxLinks',
    'maxImages',
    'maxTables',
    'redactionPatterns'
  ]);

  baseUrl.value = typeof stored.baseUrl === 'string' ? stored.baseUrl : defaultSettings.baseUrl;
  localAccessToken.value = typeof stored.localAccessToken === 'string' ? stored.localAccessToken : '';
  maxCharacters.value = String(Number.isFinite(stored.maxCharacters) ? stored.maxCharacters : defaultSettings.maxCharacters);
  maxSelectedTextCharacters.value = String(Number.isFinite(stored.maxSelectedTextCharacters) ? stored.maxSelectedTextCharacters : defaultSettings.maxSelectedTextCharacters);
  maxVisibleTextCharacters.value = String(Number.isFinite(stored.maxVisibleTextCharacters) ? stored.maxVisibleTextCharacters : defaultSettings.maxVisibleTextCharacters);
  maxArticleTextCharacters.value = String(Number.isFinite(stored.maxArticleTextCharacters) ? stored.maxArticleTextCharacters : defaultSettings.maxArticleTextCharacters);
  maxLinks.value = String(Number.isFinite(stored.maxLinks) ? stored.maxLinks : defaultSettings.maxLinks);
  maxImages.value = String(Number.isFinite(stored.maxImages) ? stored.maxImages : defaultSettings.maxImages);
  maxTables.value = String(Number.isFinite(stored.maxTables) ? stored.maxTables : defaultSettings.maxTables);
  redactionPatterns.value = Array.isArray(stored.redactionPatterns) ? stored.redactionPatterns.join('\n') : '';
}

save.addEventListener('click', async () => {
  await saveSettings({
    baseUrl: baseUrl.value.trim() || defaultSettings.baseUrl,
    localAccessToken: localAccessToken.value.trim(),
    maxCharacters: Number(maxCharacters.value) || defaultSettings.maxCharacters,
    maxSelectedTextCharacters: Number(maxSelectedTextCharacters.value) || defaultSettings.maxSelectedTextCharacters,
    maxVisibleTextCharacters: Number(maxVisibleTextCharacters.value) || defaultSettings.maxVisibleTextCharacters,
    maxArticleTextCharacters: Number(maxArticleTextCharacters.value) || defaultSettings.maxArticleTextCharacters,
    maxLinks: Number(maxLinks.value) || defaultSettings.maxLinks,
    maxImages: Number(maxImages.value) || defaultSettings.maxImages,
    maxTables: Number(maxTables.value) || defaultSettings.maxTables,
    redactionPatterns: redactionPatterns.value.split(/\r?\n/).map(item => item.trim()).filter(Boolean)
  });
  status.textContent = 'Saved. Reload the extension or click Setup/heartbeat in the popup if Doctor does not see the new settings.';
});

await load();
