const ALARM_NAME = "smartshell-token-sync";
let observedAccessToken = "";
let lastForwardedAccessToken = "";

function cleanToken(value) {
  return String(value || "")
    .trim()
    .replace(/^Bearer\s+/i, "")
    .replace(/^["'\\]+|["'\\]+$/g, "");
}

function isAccessKey(key) {
  return /(access[_-]?token|accessToken)/i.test(key) || (/access/i.test(key) && /token/i.test(key));
}

function isRefreshKey(key) {
  return /(refresh[_-]?token|refreshToken)/i.test(key) || (/refresh/i.test(key) && /token/i.test(key));
}

function looksLikeJwt(value) {
  return value.length > 80 && (value.match(/\./g) || []).length >= 2;
}

async function sendToApplication(candidates) {
  let cookies = [];
  try {
    cookies = await chrome.cookies.getAll({ url: "https://admin.smartshell.gg/" });
  } catch {
    // Storage tokens are sufficient when Chrome does not expose cookies.
  }
  for (const cookie of cookies) {
    if (/token|jwt|authorization/i.test(cookie.name) && cookie.value.length > 20) {
      candidates.push({ key: `cookie.${cookie.name}`, value: cleanToken(cookie.value) });
    }
  }

  const access = observedAccessToken
    || candidates.find(item => isAccessKey(item.key))?.value
    || candidates.find(item => !isRefreshKey(item.key) && looksLikeJwt(item.value))?.value
    || "";
  const refresh = candidates.find(item => isRefreshKey(item.key))?.value || "";
  if (!access) {
    await chrome.action.setBadgeText({ text: "!" });
    await chrome.action.setBadgeBackgroundColor({ color: "#b45309" });
    return;
  }

  try {
    const response = await fetch("http://127.0.0.1:17843/smartshell/token", {
      method: "POST",
      headers: { "Content-Type": "text/plain" },
      body: JSON.stringify({ accessToken: cleanToken(access), refreshToken: cleanToken(refresh) }),
      cache: "no-store"
    });
    if (response.ok) lastForwardedAccessToken = cleanToken(access);
    await chrome.action.setBadgeText({ text: response.ok ? "ON" : "!" });
    await chrome.action.setBadgeBackgroundColor({ color: response.ok ? "#15803d" : "#b45309" });
  } catch {
    await chrome.action.setBadgeText({ text: "OFF" });
    await chrome.action.setBadgeBackgroundColor({ color: "#475569" });
  }
}

chrome.webRequest.onBeforeSendHeaders.addListener(
  details => {
    const header = (details.requestHeaders || []).find(item =>
      /^(authorization|x-authorization|x-access-token)$/i.test(item.name) && item.value);
    const access = cleanToken(header?.value || "");
    if (access.length < 20 || access === lastForwardedAccessToken) return;
    observedAccessToken = access;
    sendToApplication([{ key: `request.${header.name}`, value: access }]);
  },
  { urls: ["https://*.smartshell.gg/*"] },
  ["requestHeaders", "extraHeaders"]
);

async function requestCollection() {
  const tabs = await chrome.tabs.query({ url: "https://admin.smartshell.gg/*" });
  for (const tab of tabs) {
    if (!tab.id) continue;
    try {
      await chrome.tabs.sendMessage(tab.id, { type: "COLLECT_SMARTSHELL_TOKENS" });
    } catch {
      try {
        await chrome.scripting.executeScript({ target: { tabId: tab.id }, files: ["content.js"] });
      } catch {
        // A navigating or closed tab will be retried by the next alarm.
      }
    }
  }
}

chrome.runtime.onInstalled.addListener(() => {
  chrome.alarms.create(ALARM_NAME, { periodInMinutes: 0.5 });
  requestCollection();
});

chrome.runtime.onStartup.addListener(() => {
  chrome.alarms.create(ALARM_NAME, { periodInMinutes: 0.5 });
  requestCollection();
});

chrome.alarms.onAlarm.addListener(alarm => {
  if (alarm.name === ALARM_NAME) requestCollection();
});

chrome.runtime.onMessage.addListener(message => {
  if (message?.type === "SMARTSHELL_TOKENS") sendToApplication(message.candidates || []);
});
