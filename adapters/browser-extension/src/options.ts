import { defaultSettings, saveSettings } from './threadlineClient.js';

const baseUrl = document.querySelector<HTMLInputElement>('#baseUrl')!;
const localAccessToken = document.querySelector<HTMLInputElement>('#localAccessToken')!;
const maxCharacters = document.querySelector<HTMLInputElement>('#maxCharacters')!;
const maxSelectedTextCharacters = document.querySelector<HTMLInputElement>('#maxSelectedTextCharacters')!;
const maxVisibleTextCharacters = document.querySelector<HTMLInputElement>('#maxVisibleTextCharacters')!;
const maxArticleTextCharacters = document.querySelector<HTMLInputElement>('#maxArticleTextCharacters')!;
const maxHeadings = document.querySelector<HTMLInputElement>('#maxHeadings')!;
const maxLinks = document.querySelector<HTMLInputElement>('#maxLinks')!;
const maxLinkTextCharacters = document.querySelector<HTMLInputElement>('#maxLinkTextCharacters')!;
const maxImages = document.querySelector<HTMLInputElement>('#maxImages')!;
const maxTables = document.querySelector<HTMLInputElement>('#maxTables')!;
const maxTableCharacters = document.querySelector<HTMLInputElement>('#maxTableCharacters')!;
const redactionPatterns = document.querySelector<HTMLTextAreaElement>('#redactionPatterns')!;
const save = document.querySelector<HTMLButtonElement>('#save')!;
const status = document.querySelector<HTMLParagraphElement>('#status')!;

function numberOrDefault(value: unknown, fallback: number): number {
  const parsed = typeof value === 'number' ? value : typeof value === 'string' ? Number(value) : Number.NaN;
  return Number.isFinite(parsed) ? parsed : fallback;
}

async function load(): Promise<void> {
  const stored = await chrome.storage.local.get([
    'baseUrl',
    'localAccessToken',
    'maxCharacters',
    'maxSelectedTextCharacters',
    'maxVisibleTextCharacters',
    'maxArticleTextCharacters',
    'maxHeadings',
    'maxLinks',
    'maxLinkTextCharacters',
    'maxImages',
    'maxTables',
    'maxTableCharacters',
    'redactionPatterns'
  ]);

  baseUrl.value = typeof stored.baseUrl === 'string' ? stored.baseUrl : defaultSettings.baseUrl;
  localAccessToken.value = typeof stored.localAccessToken === 'string' ? stored.localAccessToken : '';
  maxCharacters.value = String(numberOrDefault(stored.maxCharacters, defaultSettings.maxCharacters));
  maxSelectedTextCharacters.value = String(numberOrDefault(stored.maxSelectedTextCharacters, defaultSettings.maxSelectedTextCharacters));
  maxVisibleTextCharacters.value = String(numberOrDefault(stored.maxVisibleTextCharacters, defaultSettings.maxVisibleTextCharacters));
  maxArticleTextCharacters.value = String(numberOrDefault(stored.maxArticleTextCharacters, defaultSettings.maxArticleTextCharacters));
  maxHeadings.value = String(numberOrDefault(stored.maxHeadings, defaultSettings.maxHeadings));
  maxLinks.value = String(numberOrDefault(stored.maxLinks, defaultSettings.maxLinks));
  maxLinkTextCharacters.value = String(numberOrDefault(stored.maxLinkTextCharacters, defaultSettings.maxLinkTextCharacters));
  maxImages.value = String(numberOrDefault(stored.maxImages, defaultSettings.maxImages));
  maxTables.value = String(numberOrDefault(stored.maxTables, defaultSettings.maxTables));
  maxTableCharacters.value = String(numberOrDefault(stored.maxTableCharacters, defaultSettings.maxTableCharacters));
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
    maxHeadings: Number(maxHeadings.value) || defaultSettings.maxHeadings,
    maxLinks: Number(maxLinks.value) || defaultSettings.maxLinks,
    maxLinkTextCharacters: Number(maxLinkTextCharacters.value) || defaultSettings.maxLinkTextCharacters,
    maxImages: Number(maxImages.value) || defaultSettings.maxImages,
    maxTables: Number(maxTables.value) || defaultSettings.maxTables,
    maxTableCharacters: Number(maxTableCharacters.value) || defaultSettings.maxTableCharacters,
    redactionPatterns: redactionPatterns.value.split(/\r?\n/).map(item => item.trim()).filter(Boolean)
  });
  status.textContent = 'Saved. Reload the extension or click Setup/heartbeat in the popup if Doctor does not see the new settings.';
});

await load();
