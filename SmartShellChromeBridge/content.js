(function () {
if (globalThis.__smartShellBridgeInstalled) return;
globalThis.__smartShellBridgeInstalled = true;

let collecting = false;

function cleanToken(value) {
  return String(value || "")
    .trim()
    .replace(/^Bearer\s+/i, "")
    .replace(/^["'\\]+|["'\\]+$/g, "");
}

function collectCandidates(value, key, result, depth = 0) {
  if (value == null || depth > 8 || result.length >= 100) return;
  if (typeof value === "object") {
    if (Array.isArray(value)) {
      value.forEach((item, index) => collectCandidates(item, `${key}[${index}]`, result, depth + 1));
    } else {
      Object.entries(value).forEach(([name, item]) => collectCandidates(item, `${key}.${name}`, result, depth + 1));
    }
    return;
  }

  const text = String(value).trim();
  if (!text) return;
  if (/(token|jwt|authorization)/i.test(key) && text.length > 20) {
    result.push({ key, value: cleanToken(text) });
  }

  if ((text.startsWith("{") || text.startsWith("[")) && depth < 6) {
    try {
      collectCandidates(JSON.parse(text), key, result, depth + 1);
    } catch {
      // A plain token-like string needs no further parsing.
    }
  } else if (text.length > 80 && (text.match(/\./g) || []).length >= 2) {
    result.push({ key, value: cleanToken(text) });
  }
}

async function collectIndexedDb(result) {
  if (!indexedDB.databases) return;
  let databases = [];
  try {
    databases = await indexedDB.databases();
  } catch {
    return;
  }
  for (const dbInfo of databases) {
    if (!dbInfo.name) continue;
    try {
      const db = await new Promise((resolve, reject) => {
        const request = indexedDB.open(dbInfo.name);
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error);
      });
      for (const storeName of Array.from(db.objectStoreNames)) {
        const values = await new Promise(resolve => {
          const request = db.transaction(storeName, "readonly").objectStore(storeName).getAll();
          request.onsuccess = () => resolve(request.result || []);
          request.onerror = () => resolve([]);
        });
        collectCandidates(values, `indexedDB.${dbInfo.name}.${storeName}`, result);
      }
      db.close();
    } catch {
      // One inaccessible database must not discard tokens found elsewhere.
    }
  }
}

async function sendCandidates(candidates) {
  try {
    await chrome.runtime.sendMessage({ type: "SMARTSHELL_TOKENS", candidates });
  } catch {
    // Chrome invalidates old content-script contexts when an unpacked extension is reloaded.
  }
}

async function reportTokens() {
  if (collecting) return;
  collecting = true;
  const candidates = [];
  try {
    for (let index = 0; index < localStorage.length; index++) {
      const key = localStorage.key(index);
      collectCandidates(localStorage.getItem(key), `localStorage.${key}`, candidates);
    }
    for (let index = 0; index < sessionStorage.length; index++) {
      const key = sessionStorage.key(index);
      collectCandidates(sessionStorage.getItem(key), `sessionStorage.${key}`, candidates);
    }
    await collectIndexedDb(candidates);
  } catch {
    // Keep candidates already collected from the other browser stores.
  } finally {
    await sendCandidates(candidates);
    collecting = false;
  }
}

chrome.runtime.onMessage.addListener(message => {
  if (message?.type === "COLLECT_SMARTSHELL_TOKENS") reportTokens();
});

window.addEventListener("storage", reportTokens);
setTimeout(reportTokens, 500);
})();
