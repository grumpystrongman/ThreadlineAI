import { callThreadline, extensionVersion } from './threadlineClient.js';

type SocketTicket = { ticket: string; websocketUrl: string; expiresInSeconds: number };
type AgentCommand = { type: 'command'; commandId: string; action: string; arguments?: Record<string, unknown> };
type AgentResult = { type: 'result'; commandId: string; success: boolean; result?: unknown; error?: string };

let socket: WebSocket | undefined;
let reconnectTimer: ReturnType<typeof setTimeout> | undefined;
let heartbeatTimer: ReturnType<typeof setInterval> | undefined;
let stopped = false;

export function startBrowserAgent(): void {
  stopped = false;
  void connect();
}

export function stopBrowserAgent(): void {
  stopped = true;
  if (reconnectTimer) clearTimeout(reconnectTimer);
  if (heartbeatTimer) clearInterval(heartbeatTimer);
  reconnectTimer = undefined;
  heartbeatTimer = undefined;
  socket?.close(1000, 'Threadline browser agent stopping');
  socket = undefined;
}

async function connect(): Promise<void> {
  if (stopped || socket?.readyState === WebSocket.OPEN || socket?.readyState === WebSocket.CONNECTING) return;
  try {
    const ticket = await callThreadline<SocketTicket>('/v1/browser-agent/socket-ticket', { method: 'POST', body: '{}' });
    const ws = new WebSocket(ticket.websocketUrl);
    socket = ws;
    ws.onopen = () => {
      sendState('hello');
      if (heartbeatTimer) clearInterval(heartbeatTimer);
      heartbeatTimer = setInterval(() => sendState('state'), 20_000);
    };
    ws.onmessage = event => void handleMessage(event.data);
    ws.onclose = () => scheduleReconnect();
    ws.onerror = () => {
      try { ws.close(); } catch { /* ignored */ }
    };
  } catch {
    scheduleReconnect();
  }
}

function scheduleReconnect(): void {
  if (heartbeatTimer) clearInterval(heartbeatTimer);
  heartbeatTimer = undefined;
  socket = undefined;
  if (stopped || reconnectTimer) return;
  reconnectTimer = setTimeout(() => {
    reconnectTimer = undefined;
    void connect();
  }, 3_000);
}

async function sendState(type: 'hello' | 'state'): Promise<void> {
  const ws = socket;
  if (!ws || ws.readyState !== WebSocket.OPEN) return;
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  ws.send(JSON.stringify({
    type,
    extensionVersion,
    tabId: tab?.id,
    windowId: tab?.windowId,
    title: tab?.title,
    url: tab?.url,
    status: tab?.status
  }));
}

async function handleMessage(raw: unknown): Promise<void> {
  const ws = socket;
  if (!ws || ws.readyState !== WebSocket.OPEN || typeof raw !== 'string') return;
  let command: AgentCommand;
  try { command = JSON.parse(raw) as AgentCommand; }
  catch { return; }
  if (command.type !== 'command' || !command.commandId || !command.action) return;

  let response: AgentResult;
  try {
    response = { type: 'result', commandId: command.commandId, success: true, result: await execute(command.action, command.arguments ?? {}) };
  } catch (error) {
    response = { type: 'result', commandId: command.commandId, success: false, error: error instanceof Error ? error.message : String(error) };
  }
  if (ws.readyState === WebSocket.OPEN) ws.send(JSON.stringify(response));
  void sendState('state');
}

async function execute(action: string, args: Record<string, unknown>): Promise<unknown> {
  switch (action) {
    case 'get_state':
      return await getState();
    case 'navigate': {
      const tab = await resolveTab(args);
      const url = requiredString(args, 'url');
      const updated = await chrome.tabs.update(tab.id!, { url });
      return tabSummary(updated);
    }
    case 'new_tab': {
      const created = await chrome.tabs.create({ url: optionalString(args, 'url') ?? 'about:blank', active: optionalBoolean(args, 'active') ?? true });
      return tabSummary(created);
    }
    case 'activate_tab': {
      const tab = await resolveTab(args, true);
      const updated = await chrome.tabs.update(tab.id!, { active: true });
      if (updated.windowId !== undefined) await chrome.windows.update(updated.windowId, { focused: true });
      return tabSummary(updated);
    }
    case 'close_tab': {
      const tab = await resolveTab(args, true);
      await chrome.tabs.remove(tab.id!);
      return { closedTabId: tab.id };
    }
    case 'inspect_dom':
    case 'click':
    case 'fill':
    case 'read_text':
    case 'scroll': {
      const tab = await resolveTab(args);
      return await sendContentCommand(tab.id!, action, args);
    }
    default:
      throw new Error(`Unsupported browser action '${action}'.`);
  }
}

async function getState(): Promise<unknown> {
  const tabs = await chrome.tabs.query({ currentWindow: true });
  return {
    tabs: tabs.map(tabSummary),
    active: tabs.find(tab => tab.active) ? tabSummary(tabs.find(tab => tab.active)!) : undefined
  };
}

async function resolveTab(args: Record<string, unknown>, requireId = false): Promise<chrome.tabs.Tab> {
  const requestedId = typeof args.tab_id === 'number' ? args.tab_id : undefined;
  if (requestedId !== undefined) {
    const tab = await chrome.tabs.get(requestedId);
    if (!tab.id) throw new Error('Requested tab has no id.');
    return tab;
  }
  if (requireId && typeof args.url_contains === 'string') {
    const tabs = await chrome.tabs.query({});
    const match = tabs.find(tab => (tab.url ?? '').includes(String(args.url_contains)));
    if (match?.id) return match;
  }
  const [active] = await chrome.tabs.query({ active: true, currentWindow: true });
  if (!active?.id) throw new Error('No active browser tab is available.');
  return active;
}

async function sendContentCommand(tabId: number, action: string, args: Record<string, unknown>): Promise<unknown> {
  try {
    return await chrome.tabs.sendMessage(tabId, { type: 'THREADLINE_BROWSER_AGENT_COMMAND', action, arguments: args });
  } catch (error) {
    throw new Error(`Browser content agent is unavailable for this page. Internal/browser-store pages intentionally block DOM control. ${error instanceof Error ? error.message : String(error)}`);
  }
}

function tabSummary(tab: chrome.tabs.Tab): Record<string, unknown> {
  return { tabId: tab.id, windowId: tab.windowId, title: tab.title, url: tab.url, active: tab.active, status: tab.status, incognito: tab.incognito };
}

function requiredString(args: Record<string, unknown>, name: string): string {
  const value = optionalString(args, name);
  if (!value) throw new Error(`'${name}' is required.`);
  return value;
}
function optionalString(args: Record<string, unknown>, name: string): string | undefined {
  const value = args[name];
  return typeof value === 'string' && value.trim() ? value.trim() : undefined;
}
function optionalBoolean(args: Record<string, unknown>, name: string): boolean | undefined {
  return typeof args[name] === 'boolean' ? args[name] as boolean : undefined;
}
