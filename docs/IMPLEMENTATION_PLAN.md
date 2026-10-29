# CodeSail – Implementierungsplan

Stand: 29.10.2026 · Ziel: rein clientseitige Blazor-WASM-PWA als Code-Editor für iPad, mit direkter GitHub-Anbindung.

## Leitplanken

- **Nur kostenlose Abhängigkeiten.** Jede Bibliothek muss unter einer freien Lizenz (MIT/Apache) stehen, jeder Dienst muss im Gratis-Tarif ausreichen. Neue Pakete werden vor dem Hinzufügen in der Tabelle unten ergänzt.
- **Kein eigenes Backend.** Alles läuft im Browser; Tokens verlassen das Gerät nur in Richtung `api.github.com`.
- **iPad zuerst.** Touch-Ziele ≥ 44 px, Magic-Keyboard-Shortcuts, Safe-Area-Insets, Safari-Eigenheiten (Storage-Limits, Service-Worker-Verhalten) werden in jeder Phase mitgetestet.

## Abhängigkeiten (alle kostenlos)

| Zweck | Paket / Dienst | Lizenz / Kosten |
|---|---|---|
| Framework | .NET 10, Blazor WebAssembly (PWA-Template) | MIT |
| UI-Komponenten | MudBlazor | MIT |
| Editor | BlazorMonaco (Monaco Editor) | MIT |
| GitHub-API | Octokit.NET | MIT |
| Lokaler Speicher | IndexedDB über eigenes, kleines JS-Interop-Modul (kein Paket nötig) | – |
| Tests | xUnit, bUnit, coverlet | Apache 2.0 / MIT |
| CI/CD | GitHub Actions | gratis für öffentliche Repos (privat: 2.000 Min./Monat) |
| Hosting | GitHub Pages | gratis für öffentliche Repos |
| *Optional:* OAuth-Proxy | Cloudflare Workers | Free-Tier (100.000 Requests/Tag) |

> Bewusst **nicht** verwendet: Tailwind CLI (benötigt Node-Build-Schritt, MudBlazor deckt das Styling ab), kommerzielle Komponentenbibliotheken (Telerik, Syncfusion, DevExpress).

## Projektstruktur

```
CodeSail.slnx
global.json                      .NET-SDK 10 festgelegt
src/CodeSail/                    Blazor-WASM-PWA
  Layout/MainLayout.razor        App-Shell (AppBar, Explorer-Drawer, Editor-Fläche)
  Pages/                         Routen
  Components/                    (Phase 1+) RepoExplorer, EditorTabs, CommitDialog …
  Services/GitHub/               IGitHubService → Octokit
  Services/Storage/              ITokenStore, (Phase 2) IFileCache → IndexedDB
  Models/                        schlanke Records (RepoSummary, …)
  wwwroot/js/                    (Phase 2) indexedDb.js
tests/CodeSail.Tests/            xUnit + bUnit
.github/workflows/               ci.yml, deploy.yml
docs/                            dieser Plan
SECRETS.md                       benötigte Repo-Secrets
```

## Phase 0 – Projekt-Setup ✅ (erledigt)

- Blazor-WASM-PWA (`net10.0`) + Testprojekt + Solution (`.slnx`), SDK über `global.json` gepinnt.
- MudBlazor, BlazorMonaco, Octokit eingebunden; Bootstrap und Template-Beispielseiten entfernt.
- App-Shell mit Dark-Theme, iPad-Viewport (`viewport-fit=cover`, Safe-Area-Padding, kein Overscroll).
- `GitHubService` (Octokit mit WASM-tauglichem `HttpClientHandler`) und `BrowserTokenStore` (sessionStorage) als erste Bausteine.
- Octokit vom Publish-Trimming ausgenommen (Reflection-Serializer).
- GitHub Actions: `ci.yml` (Build + Tests bei PR/Push), `deploy.yml` (Publish → GitHub Pages inkl. `base href`, `404.html`-SPA-Fallback, `.nojekyll`).

**Manuell noch zu tun:** Repo → *Settings → Pages → Source: GitHub Actions* aktivieren (siehe `SECRETS.md`).

## Phase 1 – Authentifizierung & Repository-Explorer

1. **Login-Seite** (`Pages/Home.razor`): Eingabefeld für Fine-grained PAT, Validierung über `GetCurrentUserLoginAsync`, verständliche Fehlermeldungen (401, fehlende Scopes, Rate-Limit).
2. **Token-Handling:** sessionStorage als Standard, optional „Angemeldet bleiben“ (localStorage) mit deutlichem Hinweis. Logout löscht Token und Cache.
3. **Auth-Zustand:** `AuthState`-Service (scoped) mit Event für UI-Updates; Routen außer `/` erfordern Login.
4. **Repo-Auswahl:** Liste mit Suche (`MudAutocomplete`/`MudList`), sortiert nach „zuletzt aktualisiert“.
5. **Explorer:** Baum über die Git-Trees-API (`GET /repos/{o}/{r}/git/trees/{sha}?recursive=1`) – ein Request statt einem pro Ordner. Darstellung mit `MudTreeView`, lazy gerendert; Branch-Auswahl im Header.
6. **Rate-Limit-Anzeige** (`X-RateLimit-Remaining`) im AppBar-Menü.
7. Tests: bUnit für Login-Flow und Baum-Aufbau mit gemocktem `IGitHubService`.

*Fertig, wenn:* Nutzer meldet sich auf dem iPad an, wählt ein Repo und sieht den Dateibaum.

## Phase 2 – Monaco-Editor & Offline-Cache

1. **Editor-Komponente** (`StandaloneCodeEditor`): Sprache aus Dateiendung ableiten, Theme `vs-dark`, Schriftgröße per Pinch/Setting, Touch-taugliche Optionen (`minimap` aus, größere Scrollbars).
2. **Tabs** für mehrere geöffnete Dateien, Dirty-Markierung.
3. **IndexedDB-Modul** (`wwwroot/js/indexedDb.js`, ~100 Zeilen, ohne Fremdpaket) mit Stores:
   - `files` – Key `owner/repo@branch:path` → `{ content, baseSha, dirty, updatedAt }`
   - `trees` – zuletzt geladener Baum je Repo/Branch
4. **`IFileCache`-Service** in C#: öffnet Dateien bevorzugt aus dem Cache, lädt sonst über die Contents-/Blobs-API nach.
5. **Offline-Erkennung** (`navigator.onLine` + Events) mit Statusanzeige; Service Worker cached App-Shell und Monaco-Assets (im PWA-Template bereits angelegt, Monaco-Dateien prüfen).
6. **Safari-Eigenheiten:** `navigator.storage.persist()` anfragen, damit iPadOS den Cache nicht nach 7 Tagen räumt; Speicherbedarf anzeigen.
7. **Keyboard:** Cmd+S (lokal speichern), Cmd+P (Datei-Schnellsuche) – Konflikte mit Safari-Shortcuts prüfen.

*Fertig, wenn:* Datei öffnen → Flugmodus → bearbeiten → App neu starten → Änderungen sind noch da.

## Phase 3 – Commit- & Branch-Workflow

1. **Änderungsübersicht:** alle `dirty`-Dateien des Repos, Diff-Ansicht mit Monacos `DiffEditor`.
2. **Commit über die Git-Data-API** (ein atomarer Commit für mehrere Dateien):
   blobs anlegen → tree mit `base_tree` → commit → `refs/heads/{branch}` aktualisieren.
3. **Konflikterkennung:** vor dem Push prüfen, ob sich der Branch-Head seit `baseSha` geändert hat; bei Konflikt Diff „lokal vs. remote“ anzeigen und Nutzer entscheiden lassen (kein automatisches Mergen).
4. **Branches:** anlegen, wechseln; optional „Pull Request erstellen“ (einfacher Dialog).
5. **Neue/gelöschte/umbenannte Dateien** im Explorer.
6. **Offline-Queue:** Commits, die offline ausgelöst werden, bleiben als „ausstehend“ markiert und werden bei Verbindung manuell bestätigt (kein stilles Auto-Push).

*Fertig, wenn:* Mehrere Dateien bearbeitet, als ein Commit gepusht, auf GitHub sichtbar.

## Phase 4 – Experiment: Kompilieren im Browser (Roslyn)

1. Machbarkeitsstudie in eigenem Branch: `Microsoft.CodeAnalysis.CSharp` (MIT) lazy laden (`<BlazorWebAssemblyLazyLoad>`), da es mehrere MB groß ist.
2. Referenz-Assemblies aus dem Publish-Output laden, einfache Konsolenprogramme kompilieren und ausführen.
3. Diagnosen als Monaco-Marker anzeigen.
4. Bewertung: Ladezeit und Speicherbedarf auf dem iPad; nur weiterverfolgen, wenn es praktikabel ist.

## Querschnitt / laufend

- **Sicherheit:** Token nie loggen, nie in URLs; Content-Security-Policy in `index.html` (nur `self`, `api.github.com`, Google Fonts). Fine-grained PAT mit minimalen Rechten empfehlen.
- **Tests:** jeder Service hinter Interface, bUnit-Tests für Komponenten; CI muss grün sein vor Merge.
- **Performance:** Trimming aktiv, später AOT nur prüfen, wenn Editor-Interop zu langsam ist (vergrößert Download).
- **Barrierefreiheit:** aria-Labels an Icon-Buttons, ausreichende Kontraste im Dark-Theme.

## Optional – OAuth statt PAT

GitHubs OAuth-Token-Endpunkt erlaubt kein CORS und braucht ein Client-Secret, daher geht reines Client-OAuth nicht. Kostenlose Lösung: ein Cloudflare Worker (Free-Tier) tauscht den `code` gegen ein Token. Erst angehen, wenn PAT-Login stabil läuft. Zusätzliche Secrets dafür stehen in `SECRETS.md`.

## Risiken

| Risiko | Gegenmaßnahme |
|---|---|
| Octokit nutzt Funktionen, die in WASM fehlen | eigener `HttpClientHandler` (umgesetzt); falls weitere Probleme: einzelne Endpunkte direkt per `HttpClient` |
| Safari löscht IndexedDB bei Speicherdruck | `storage.persist()`, Hinweis „ungespeicherte Änderungen“ prominent anzeigen |
| Monaco auf Touch nur eingeschränkt (Textauswahl, Kontextmenü) | früh auf echtem iPad testen; eigene Toolbar für Undo/Redo/Suchen |
| GitHub-API-Rate-Limit (5.000/h) | Tree-API recursive, ETags nutzen, Cache bevorzugen |
| Große Bundle-Größe | Trimming, Lazy Loading für Roslyn |
