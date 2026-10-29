// Browser-Interop für CodeSail: IndexedDB-Cache (ohne Fremdbibliothek), Speicher- und Online-Status.
// Stores: files (Dateiinhalte), trees (Dateibäume je Branch), repos (Branches je Repo)

const DB_NAME = "codesail";
const DB_VERSION = 1;
const STORES = ["files", "trees", "repos"];

let dbPromise;

function openDb() {
    dbPromise ??= new Promise((resolve, reject) => {
        const request = indexedDB.open(DB_NAME, DB_VERSION);
        request.onupgradeneeded = () => {
            for (const name of STORES) {
                if (!request.result.objectStoreNames.contains(name)) {
                    request.result.createObjectStore(name, { keyPath: "key" });
                }
            }
        };
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => {
            dbPromise = undefined;
            reject(request.error);
        };
    });
    return dbPromise;
}

async function run(storeName, mode, action) {
    const db = await openDb();
    return new Promise((resolve, reject) => {
        const tx = db.transaction(storeName, mode);
        const request = action(tx.objectStore(storeName));
        tx.oncomplete = () => resolve(request?.result ?? null);
        tx.onerror = () => reject(tx.error);
        tx.onabort = () => reject(tx.error);
    });
}

export function get(store, key) {
    return run(store, "readonly", s => s.get(key));
}

export function put(store, value) {
    return run(store, "readwrite", s => s.put(value)).then(() => undefined);
}

export function remove(store, key) {
    return run(store, "readwrite", s => s.delete(key)).then(() => undefined);
}

// Alle Einträge, deren Schlüssel mit prefix beginnt (Schlüssel sind Strings)
export function getByPrefix(store, prefix) {
    const range = IDBKeyRange.bound(prefix, prefix + "￿");
    return run(store, "readonly", s => s.getAll(range));
}

export async function clearAll() {
    for (const name of STORES) {
        await run(name, "readwrite", s => s.clear());
    }
}

// Bittet den Browser, den Speicher nicht automatisch zu räumen (wichtig für Safari/iPadOS)
export async function requestPersistence() {
    if (!navigator.storage?.persist) return false;
    return (await navigator.storage.persisted()) || (await navigator.storage.persist());
}

export async function estimateUsage() {
    if (!navigator.storage?.estimate) return null;
    const { usage } = await navigator.storage.estimate();
    return usage ?? null;
}

// Online/Offline-Status an .NET melden
export function watchConnectivity(dotNetRef) {
    const notify = () => dotNetRef.invokeMethodAsync("OnConnectivityChanged", navigator.onLine);
    window.addEventListener("online", notify);
    window.addEventListener("offline", notify);
    return navigator.onLine;
}

// Meldet .NET, sobald eine neue App-Version bereitliegt (siehe pwa.js)
export function watchUpdates(dotNetRef) {
    const pwa = window.codesailPwa;
    if (!pwa) return false;
    pwa.onUpdate(() => dotNetRef.invokeMethodAsync("OnUpdateAvailable"));
    return pwa.updateReady;
}

export function applyUpdate() {
    window.codesailPwa?.applyUpdate();
}

// Vorschau: Der iframe meldet seine Scrollposition; nach jedem Neuaufbau (neues srcdoc)
// wird sie zurückgeschickt, damit die Ansicht beim Tippen nicht nach oben springt.
export function attachPreview(iframe, autoHeight = false) {
    if (iframe.codesailScroll !== undefined) return;
    iframe.codesailScroll = 0;
    addEventListener("message", e => {
        if (e.source !== iframe.contentWindow || !e.data) return;
        if (typeof e.data.codesailScroll === "number") iframe.codesailScroll = e.data.codesailScroll;
        if (e.data.codesailReady) iframe.contentWindow.postMessage({ codesailScroll: iframe.codesailScroll }, "*");
        // Begrenzt, damit ein sehr langer Text die Seite nicht sprengt (danach scrollt der Rahmen selbst)
        if (autoHeight && typeof e.data.codesailHeight === "number")
            iframe.style.height = Math.min(Math.max(e.data.codesailHeight, 40), 900) + "px";
    });
}

export function resetPreviewScroll(iframe) {
    iframe.codesailScroll = 0;
}

// Geräteeigenschaften: Systemdesign und Touchscreen (für Design "System" und die Touch-Leiste)
export function environmentInfo() {
    return {
        prefersDark: matchMedia("(prefers-color-scheme: dark)").matches,
        touch: matchMedia("(pointer: coarse)").matches || navigator.maxTouchPoints > 0
    };
}

// Globale Tastenkürzel, die auch greifen, wenn der Editor nicht fokussiert ist.
// Cmd/Strg+P würde sonst den Druckdialog öffnen.
export function registerShortcuts(dotNetRef) {
    addEventListener("keydown", e => {
        if (!(e.metaKey || e.ctrlKey) || e.altKey) return;
        const key = e.key.toLowerCase();
        const name = key === "p" && !e.shiftKey ? "quickOpen"
            : key === "f" && e.shiftKey ? "search"
            : null;
        if (!name) return;
        e.preventDefault();
        dotNetRef.invokeMethodAsync("OnShortcut", name);
    }, true);
}

// Lädt eine Datei als Download herunter (z. B. Binärdateien aus dem Repository)
export function downloadFile(fileName, base64, mimeType) {
    const bytes = Uint8Array.from(atob(base64), c => c.charCodeAt(0));
    const url = URL.createObjectURL(new Blob([bytes], { type: mimeType }));
    const link = Object.assign(document.createElement("a"), { href: url, download: fileName });
    link.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
}

// Schmale Ansicht (iPad hochkant): Explorer liegt dann über dem Editor
export function isNarrow() {
    return window.matchMedia("(max-width: 820px)").matches;
}
