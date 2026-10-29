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

// Schmale Ansicht (iPad hochkant): Explorer liegt dann über dem Editor
export function isNarrow() {
    return window.matchMedia("(max-width: 820px)").matches;
}
