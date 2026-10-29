// Registriert den Service Worker und erkennt neue App-Versionen.
// Läuft vor Blazor, damit kein Update-Ereignis verloren geht; die App fragt den Zustand später ab.

window.codesailPwa = (() => {
    const listeners = [];
    let waitingWorker = null;

    function announce(worker) {
        waitingWorker = worker;
        listeners.forEach(notify => notify());
    }

    function track(worker) {
        worker.addEventListener("statechange", () => {
            // Beim allerersten Installieren gibt es noch keinen Controller – das ist kein Update
            if (worker.state === "installed" && navigator.serviceWorker.controller) {
                announce(worker);
            }
        });
    }

    if ("serviceWorker" in navigator) {
        navigator.serviceWorker.register("service-worker.js", { updateViaCache: "none" }).then(registration => {
            if (registration.waiting && navigator.serviceWorker.controller) {
                announce(registration.waiting);
            }
            registration.addEventListener("updatefound", () => track(registration.installing));

            // Als Home-Screen-App wird CodeSail selten neu geladen; beim Zurückkehren nach Updates suchen
            document.addEventListener("visibilitychange", () => {
                if (document.visibilityState === "visible") {
                    registration.update().catch(() => { });
                }
            });
        });

        let reloading = false;
        navigator.serviceWorker.addEventListener("controllerchange", () => {
            if (!reloading) {
                reloading = true;
                location.reload();
            }
        });
    }

    return {
        get updateReady() { return waitingWorker !== null; },
        onUpdate(callback) { listeners.push(callback); },
        // Der wartende Service Worker übernimmt; "controllerchange" lädt die Seite dann neu
        applyUpdate() {
            if (waitingWorker) {
                waitingWorker.postMessage("skipWaiting");
            } else {
                location.reload();
            }
        }
    };
})();
