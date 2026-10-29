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
| Markdown-Vorschau | Markdig | BSD-2-Clause |
| Icons erzeugen (nur Werkzeug, nicht in der App) | Svg.Skia in `tools/render-icons.cs` | MIT |
| *Geplant (Phase 4):* C#-Compiler | Microsoft.CodeAnalysis.CSharp (Roslyn) | MIT |
| *Geplant (Phase 4):* Referenz-Assemblies | Microsoft.NETCore.App.Ref (NuGet) | MIT |
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
  Layout/MainLayout.razor        App-Shell (AppBar, Anmeldeschutz, Offline-/Update-Hinweise)
  Pages/                         Home (Login), Repos, Workspace
  Components/                    FileExplorer, SourceEditor, DiffView, PreviewPane, ChangesPanel, Dialoge
  Services/                      AuthState, WorkspaceService, CommitService, BrowserEnvironment
  Services/GitHub/               GitHub-API (Octokit + direkte Git-Data-Aufrufe)
  Services/Storage/              Token-Speicher, IndexedDB-Cache
  Services/Preview/              Markdown-/HTML-Vorschau
  Models/                        schlanke Records (RepoSummary, CachedFile, …)
  wwwroot/js/                    codesail.js (IndexedDB, Vorschau, Online-Status), pwa.js (Service Worker, Updates)
src/CodeSail.Compiler/           (Phase 4, geplant) Roslyn-Anbindung, lazy geladen
tests/CodeSail.Tests/            xUnit + bUnit
tools/render-icons.cs            erzeugt die PNG-Icons aus dem SVG-Logo
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

## Phase 1 – Authentifizierung & Repository-Explorer ✅ (umgesetzt, Test mit echtem Token auf dem iPad offen)

Umgesetzt in `Pages/Home.razor` (Login), `Pages/Repos.razor` (Repo-Auswahl), `Pages/Workspace.razor` + `Components/FileExplorer.razor` (Explorer mit Branch-Auswahl und Dateisuche), `Services/AuthState.cs` und `Layout/MainLayout.razor` (Zugriffsschutz, API-Kontingent, Abmelden).

1. **Login-Seite** (`Pages/Home.razor`): Eingabefeld für Fine-grained PAT, Validierung über `GetCurrentUserLoginAsync`, verständliche Fehlermeldungen (401, fehlende Scopes, Rate-Limit).
2. **Token-Handling:** sessionStorage als Standard, optional „Angemeldet bleiben“ (localStorage) mit deutlichem Hinweis. Logout löscht Token und Cache.
3. **Auth-Zustand:** `AuthState`-Service (scoped) mit Event für UI-Updates; Routen außer `/` erfordern Login.
4. **Repo-Auswahl:** Liste mit Suche (`MudAutocomplete`/`MudList`), sortiert nach „zuletzt aktualisiert“.
5. **Explorer:** Baum über die Git-Trees-API (`GET /repos/{o}/{r}/git/trees/{sha}?recursive=1`) – ein Request statt einem pro Ordner. Darstellung mit `MudTreeView`, lazy gerendert; Branch-Auswahl im Header.
6. **Rate-Limit-Anzeige** (`X-RateLimit-Remaining`) im AppBar-Menü.
7. Tests: bUnit für Login-Flow und Baum-Aufbau mit gemocktem `IGitHubService`.

*Fertig, wenn:* Nutzer meldet sich auf dem iPad an, wählt ein Repo und sieht den Dateibaum.

## Phase 2 – Monaco-Editor & Offline-Cache ✅ (umgesetzt, Test mit echtem Token auf dem iPad offen)

Umgesetzt in `Components/SourceEditor.razor` (ein Monaco-Modell pro Datei, Autosave nach 500 ms, Cmd+S), `Pages/Workspace.razor` (Tabs, Dirty-Markierung, Änderungen verwerfen, geänderte Dateien öffnen sich wieder), `Services/WorkspaceService.cs` (Laden mit Offline-Fallback; lokale Änderungen haben Vorrang), `Services/Storage/IndexedDbFileCache.cs` + `wwwroot/js/codesail.js` (Stores `files`, `trees`, `repos`), `Services/BrowserEnvironment.cs` (Online-Status). Dateien werden über die Blob-API geladen (auch > 1 MB), Binärdateien abgelehnt. Der Service Worker leitet seine Basis jetzt aus dem eigenen Pfad ab, damit der Offline-Cache auch unter `/CodeSail/` greift.

Abweichungen/offen: Schriftgröße per Pinch und Cmd+P (Schnellsuche) fehlen noch. Monaco erkennt die Sprache selbst über die Dateiendung in der Modell-URI, eine eigene Zuordnung ist nicht nötig.

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

## Phase 3 – Commit- & Branch-Workflow ✅ (umgesetzt, Test mit echtem Token auf dem iPad offen)

Umgesetzt in `Services/CommitService.cs` (ein Commit über Git-Data-API: Baum mit `base_tree` und Inhalten direkt im Tree-Request statt einzelner Blobs → Commit → Ref-Update ohne `force`), `Components/ChangesPanel.razor` (Änderungsliste mit M/A/D, Commit-Formular, neuer Branch + Pull Request), `Components/DiffView.razor` (Monaco-Diff), `Components/TextPromptDialog.razor` sowie Datei-Operationen und Branch-Anlage im `WorkspaceService`. Git-Data-Aufrufe laufen direkt per HTTP, weil Octokit `"sha": null` (Löschen) weglässt und Branch-Namen mit `/` kodiert.

Konfliktregel: Eine Datei ist im Konflikt, wenn ihre Blob-SHA auf GitHub nicht mehr der SHA entspricht, von der aus lokal bearbeitet wurde. Änderungen an *anderen* Dateien auf GitHub sind kein Konflikt; der Commit setzt dann auf dem neuen Stand auf. Bei Konflikten entscheidet der Nutzer: eigene Fassung committen (überschreibt) oder abbrechen und die Datei verwerfen. Verschiebt jemand den Branch genau zwischen Prüfung und Push, lehnt GitHub das Update ab und der Ablauf wiederholt sich einmal.

Abweichungen: Eine eigene Offline-Warteschlange ist nicht nötig, da lokale Änderungen ohnehin erhalten bleiben; offline ist der Commit-Button gesperrt. Ein Diff „lokal vs. aktueller GitHub-Stand“ bei Konflikten fehlt noch (angezeigt wird lokal vs. Ausgangsstand).

1. **Änderungsübersicht:** alle `dirty`-Dateien des Repos, Diff-Ansicht mit Monacos `DiffEditor`.
2. **Commit über die Git-Data-API** (ein atomarer Commit für mehrere Dateien):
   blobs anlegen → tree mit `base_tree` → commit → `refs/heads/{branch}` aktualisieren.
3. **Konflikterkennung:** vor dem Push prüfen, ob sich der Branch-Head seit `baseSha` geändert hat; bei Konflikt Diff „lokal vs. remote“ anzeigen und Nutzer entscheiden lassen (kein automatisches Mergen).
4. **Branches:** anlegen, wechseln; optional „Pull Request erstellen“ (einfacher Dialog).
5. **Neue/gelöschte/umbenannte Dateien** im Explorer.
6. **Offline-Queue:** Commits, die offline ausgelöst werden, bleiben als „ausstehend“ markiert und werden bei Verbindung manuell bestätigt (kein stilles Auto-Push).

*Fertig, wenn:* Mehrere Dateien bearbeitet, als ein Commit gepusht, auf GitHub sichtbar.

## Phase 3b – Vorschau für Markdown und HTML ✅ (umgesetzt)

Umgesetzt in `Services/Preview/PreviewBuilder.cs` (Markdown → HTML mit Markdig, Pfadauflösung, Einbetten von Repo-Dateien), `Services/Preview/PreviewService.cs` (lädt Bilder/CSS/JS aus dem lokalen Cache bzw. per Blob-API, Blobs pro Sitzung zwischengespeichert) und `Components/PreviewPane.razor`.

- **Bedienung:** Augen-Symbol in der Editor-Leiste bei `.md`/`.markdown`/`.html`/`.htm`. Querformat: Vorschau rechts neben dem Code, per Symbol auch „nur Vorschau“. Hochformat: Umschalten zwischen Code und Vorschau. Aktualisiert sich beim Tippen (nach dem Autosave, ~0,5 s); die Scrollposition bleibt erhalten.
- **Sicherheit:** Die Vorschau läuft in einem iframe mit `sandbox` **ohne** `allow-same-origin`. Skripte aus dem Repo laufen, kommen aber nicht an Token, `localStorage` oder IndexedDB der App (im Browser geprüft). Deshalb lädt der iframe keine Repo-Dateien selbst: Stylesheets und Skripte werden eingebettet, Bilder als `data:`-URL eingesetzt.
- **Lokale Änderungen zuerst:** Ein noch nicht committetes Stylesheet erscheint sofort in der HTML-Vorschau.
- **Grenzen:** `url(...)` innerhalb von CSS, `import` in JS-Modulen und Links auf andere Repo-Seiten werden nicht aufgelöst. Projekte mit Build-Schritt (React/Vite usw.) lassen sich nicht in der Vorschau anzeigen. Offline fehlen Bilder, die noch nie geladen wurden.

## Phase 3c – Branch-Verwaltung und Pull Requests ✅ (umgesetzt, Test mit echtem Token offen)

Die Seitenleiste hat vier Reiter (als Symbole mit Tooltip, damit sie in 320 px passen): Dateien, Änderungen, **Branches**, **Pull Requests**.

- **Branches** (`Components/BranchesPanel.razor`): Suche; aktueller und Standard-Branch oben; Kennzeichnung „aktuell“, „Standard“, „geschützt“ und „N lokal“ (Dateien mit nicht committeten Änderungen). Aktionen je Branch: wechseln, mit dem Standard-Branch vergleichen (Commits vor/hinter, geänderte Dateien, direkt weiter zum PR), Pull Request erstellen, neuer Branch von hier, umbenennen, löschen.
  - Löschen fragt nach und nennt die Zahl der lokalen Änderungen, die dabei verloren gehen. Standard- und geschützte Branches lassen sich nicht löschen.
  - Beim Umbenennen ziehen lokale Änderungen unter den neuen Namen um. Offene PRs passt GitHub selbst an.
- **Pull Requests** (`Components/PullRequestsPanel.razor`, `PullRequestView.razor`, `Services/PullRequestService.cs`): Filter Offen/Geschlossen/Alle (die 50 zuletzt aktualisierten), Badge mit der Zahl offener PRs, „Neuer Pull Request“ (Quelle/Ziel, Titelvorschlag aus dem Branch-Namen, Beschreibung, Entwurf).
  - Die Detailansicht öffnet sich im Hauptbereich. Sie zeigt Status, Autor, Commits, +/−, die Beschreibung als Markdown (abgeschottet in einem eigenen iframe, weil PR-Texte von Dritten stammen; die Höhe passt sich an) und die geänderten Dateien mit aufklappbarem Diff.
  - Aktionen: Mergen (Merge-Commit, Squash oder Rebase, optional Quell-Branch löschen), Schließen/Wieder öffnen, Branch öffnen, auf GitHub öffnen.
  - Gemergt wird nur mit der Head-SHA aus der Ansicht. Hat sich der Branch inzwischen geändert, lehnt GitHub ab (409) und die Ansicht lädt neu. Branches aus Forks werden nie gelöscht. Schlägt nur das Löschen fehl (z. B. geschützt), bleibt der Merge trotzdem erfolgreich.
- **Grenzen:** Kommentare und Reviews, Status-Checks (CI) und das Bearbeiten von Titel/Beschreibung sind nicht enthalten; dafür gibt es „Auf GitHub“. Bei mehr als 100 geänderten Dateien zeigt die Ansicht nur die ersten 100.
- **Token-Rechte:** Lesen und Mergen von PRs braucht *Pull requests: Read and write*. Branches anlegen, umbenennen und löschen braucht *Contents: Read and write*. Das Umbenennen erfordert auf GitHub außerdem Admin-Rechte am Repository, wenn es der Standard-Branch ist.

## Phase 3d – Alltagslücken ✅ (umgesetzt, Test mit echtem Token offen)

Die Seitenleiste hat jetzt sechs Symbol-Reiter: Dateien, Suche, Änderungen, Verlauf, Branches, Pull Requests.

- **Editor-Komfort:** Einstellungsdialog im Kontomenü (`SettingsService`, gespeichert im localStorage): Schriftgröße, Zeilenumbruch, Minimap, Design Dunkel/Hell/System (gilt für MudBlazor und Monaco). **Cmd+P** öffnet die Schnellsuche (`QuickOpenDialog`, unscharfe Suche über `FuzzyMatcher`, bevorzugt Wortanfänge im Dateinamen). **Touch-Leiste** (`TouchToolbar`, automatisch bei Touchscreen) mit Rückgängig/Wiederholen, Tab/Ausrücken, Esc, Pfeiltasten, Suchen/Ersetzen, Kommentar, Vorschläge, Formatieren und Klammern. Cmd+P und Cmd+Shift+F fängt ein globaler Listener ab, sie greifen also auch außerhalb des Editors.
- **Bilder und Binärdateien** (`BinaryView`): Bilder mit Einpassen/Originalgröße auf Schachbrett, PDFs eingebettet, alles andere zum Herunterladen.
- **Suche in allen Dateien** (`SearchService`, `SearchPanel`): durchsucht die lokalen Kopien des Branches, inklusive nicht committeter und neuer Dateien, auch offline; Optionen Groß/Klein, ganze Wörter, regulärer Ausdruck. Dateien, die noch nie geöffnet wurden, zählt die Suche und lädt sie auf Wunsch gesammelt nach (6 parallel, abbrechbar). Danach liegen sie im Offline-Cache. Binärformate und Dateien über 1 MB werden übersprungen, nach 2.000 Treffern ist Schluss.
- **Verlauf:** Reiter mit den Commits des Branches (`CommitList`, seitenweise), Commit-Ansicht mit Nachricht, Eltern und Diff (`CommitView`, gemeinsame `FileDiffList` mit den PRs), „Verlauf dieser Datei“ und „Zeilenherkunft (Blame)“ im Dateimenü. Blame läuft über die GraphQL-API, da REST keines anbietet.
- **Konflikte zusammenführen** (`ThreeWayMerge`, `MergeService`): Meldet der Commit einen Konflikt, bietet die App „Zusammenführen“ an. Zeilenbasierter Drei-Wege-Merge (Myers-Diff) zwischen dem Ausgangsstand, der lokalen Fassung und dem neuen Stand auf GitHub. Getrennte Änderungen werden kombiniert und direkt committet. Überschneidungen erhalten Git-Konfliktmarkierungen, der Editor springt zur ersten, und ein Commit mit verbliebenen Markierungen wird abgelehnt. Auf einer Seite neu angelegte oder gelöschte Dateien bleiben manuell zu entscheiden.
- **Branch aktualisieren:** „Mit main aktualisieren“ im Branches-Reiter bzw. „Branch aktualisieren“ in einem PR, der hinter seinem Ziel liegt (Merge-API von GitHub). Bei Überschneidungen verweist die App auf „Resolve conflicts“ auf GitHub; ein lokaler Merge zweier Branches ist nicht umgesetzt.
- **Pull Requests vertieft:** Titel/Beschreibung bearbeiten, CI-Status (Status-Checks und Check-Runs, aufklappbar, fehlende Token-Rechte werden genannt statt als Fehler gezeigt), Unterhaltung aus Kommentaren, Reviews und Zeilenkommentaren (als reiner Text, nicht als HTML), Kommentieren, Genehmigen, Änderungen anfordern. Zeilenkommentare direkt im Diff zu schreiben ist nicht enthalten.

## Phase 4 – Online-Compiler (4A umgesetzt, 4B geplant)

### Ziel und Umfang

CodeSail soll Code direkt auf dem iPad **übersetzen, Fehler anzeigen und ausführen**, ohne Server und auch offline. Zwei Stufen, weil sie sehr unterschiedlich aufwendig sind:

| Stufe | Sprache | Aufwand | Kern |
|---|---|---|---|
| **4A** | JavaScript, TypeScript | klein | Ausführen in einer abgeschotteten Sandbox wie bei der Vorschau; TypeScript übersetzt der Sprachdienst, der in Monaco schon steckt |
| **4B** | C# (.NET-Konsolenprogramme) | groß | Roslyn im Browser, Referenz-Assemblies, Ausführung in einer eigenen .NET-Laufzeit im Web Worker |

**Nicht** im Umfang: Web-Frameworks mit Build-Schritt (React/Vite, ASP.NET), Debugger mit Haltepunkten, beliebige NuGet-Pakete in der ersten Version, andere Sprachen. Python per Pyodide (MPL-2.0, ~10 MB) wäre ein späteres, eigenes Thema.

### Grundsatzentscheidungen

| Thema | Entscheidung | Begründung |
|---|---|---|
| Wo wird kompiliert? | Im Browser, kein Server | Leitplanke „kein Backend“; funktioniert offline |
| C#-Compiler | Roslyn (`Microsoft.CodeAnalysis.CSharp`, MIT) | Der echte C#-Compiler, läuft unter WebAssembly, liefert Diagnosen mit Zeile/Spalte |
| Laden von Roslyn | Eigenes Projekt `src/CodeSail.Compiler`, per `BlazorWebAssemblyLazyLoad` erst bei Bedarf | Mehrere MB, die sonst jeden App-Start verlangsamen |
| Referenz-Assemblies | Aus dem NuGet-Paket `Microsoft.NETCore.App.Ref` beim Build nach `wwwroot/refs/` kopieren und bei Bedarf laden | Die Laufzeit-Assemblies im `_framework`-Ordner liegen im Webcil-Format vor, Roslyn braucht echte PE-Metadaten |
| Ausführung C# | Kompiliertes Assembly in einer **zweiten .NET-Laufzeit im Web Worker** laden und starten | Hält die Oberfläche flüssig; Endlosschleifen lassen sich durch Beenden des Workers abbrechen, im UI-Thread ginge das nicht |
| Ausführung JS/TS | Web Worker in einem abgeschotteten iframe ohne `allow-same-origin`, `console.*` per `postMessage` an die App | Gleiches Sicherheitsmodell wie die Vorschau; zusätzlich eigener Thread, damit Endlosschleifen die App nicht blockieren |
| Offline | Roslyn und Referenzen nach dem ersten Laden im Cache des Service Workers | Die App funktioniert offline weiter; erster Download nur, wenn der Compiler genutzt wird |
| Multithreading in WASM | Nicht verwenden | Braucht COOP/COEP-Header (Cross-Origin-Isolation); GitHub Pages kann keine Header setzen |

### Architektur (C#)

```
Editor (Monaco)            Seitenleiste „Ausführen“             Web Worker (eigene .NET-Laufzeit)
  │ Cmd+Enter                  │                                   │
  ▼                            ▼                                   │
ProjectDiscovery ──► CompileService (Roslyn, lazy) ──► Assembly-Bytes ──► Runner: Assembly.Load, Main()
  .csproj finden,        Diagnosen ──► Monaco-Marker                     Console.Out/Err ──► Ausgabe-Panel
  .cs-Dateien sammeln    (Global.SetModelMarkers)                        Console.In ◄── Eingabefeld
                                                                         Abbrechen = Worker beenden
```

Kompiliert wird zunächst im Haupt-Thread, weil Roslyn nur bei Bedarf läuft und den Thread nur kurz belegt. Falls das auf dem iPad zu träge ist, kommt auch die Übersetzung in den Worker (siehe 4.6).

### Meilensteine

**4.0 Spike: Machbarkeit und Messwerte** (eigener Branch, wird wieder verworfen)
- Roslyn per Lazy Load in die App holen, „Hello World“ aus einem String kompilieren, mit `Assembly.Load(byte[])` laden und ausführen.
- Mindestens `System.Runtime`, `System.Console`, `System.Linq`, `System.Collections` als Referenz-Assemblies laden.
- **Messen** auf echtem iPad (Safari) und Desktop-Chrome: Downloadgröße (komprimiert), erste und wiederholte Übersetzungszeit, Speicherbedarf.
- **Prüfen:** Läuft eine zweite .NET-Laufzeit im Web Worker? (eigenes `dotnet.js` im Worker, oder kostenlose Bibliotheken wie SpawnDev.BlazorJS.WebWorkers bzw. BlazorWorker, beide MIT). Gibt es dafür in .NET 10 inzwischen offizielle Vorlagen?
- *Weiter, wenn:* Hello World läuft auf dem iPad, die erste Übersetzung braucht < 10 s, eine wiederholte < 2 s. Sonst die Stufe 4B neu bewerten.

**4.1 JavaScript/TypeScript ausführen (Stufe 4A)** ✅ umgesetzt, Test auf dem iPad offen

Umgesetzt in `wwwroot/js/runner.js`, `Services/Run/RunSession.cs` und `Components/RunPanel.razor`; ▶ in der Editor-Leiste bzw. **Cmd+Enter** bei `.js`, `.mjs`, `.cjs`, `.ts`, `.mts`, `.cts`.

- **Ausführung im Web Worker innerhalb eines abgeschotteten iframes** (statt direkt im iframe wie ursprünglich geplant): Der Worker hat einen eigenen Thread. Eine Endlosschleife friert die App deshalb nicht ein, im Browser gemessen lief die Oberfläche weiter. Der iframe ohne `allow-same-origin` sorgt dafür, dass `localStorage` fehlt und IndexedDB mit `SecurityError` blockiert ist (geprüft). „Stopp“ entfernt den iframe und beendet damit den Worker.
- **Klassischer statt Modul-Worker:** Modul-Worker aus Blob-URLs starten in einem Origin ohne Herkunft nicht (im Browser geprüft). Das Programm wird deshalb in eine `async`-Funktion gelegt; `await` auf oberster Ebene funktioniert trotzdem. `export` wird entfernt, ohne Zeilen zu verschieben, `import` wird mit Zeilenangabe als nicht unterstützt gemeldet.
- **TypeScript** übersetzt der TypeScript-Worker von Monaco (`getEmitOutput`, Ziel ESNext, mit Source Map). Syntaxfehler verhindern das Ausführen; Typfehler erscheinen als Warnung, das Programm läuft trotzdem (wie `tsc` mit `noEmitOnError: false`). Laufzeitfehler werden über die Source Map auf die richtige `.ts`-Zeile zurückgerechnet, ebenso Zeilen im Stacktrace.
- **Ausgabe-Panel** unten (ein-/ausblendbar, läuft beim Ausblenden weiter): `console.log/info/warn/error/debug/clear`, Objekte als JSON, Fehler mit antippbarer Zeilenangabe (springt im Editor an die Stelle), Status mit Laufzeit. Ausgaben werden im Worker gepuffert und gebündelt; nach 5.000 Zeilen wird abgeschnitten, damit eine Ausgabeflut die App nicht lahmlegt.
- **Grenzen:** kein `import` mehrerer Dateien, kein DOM (`document`, `alert`, `prompt`) im Worker – dafür ist die HTML-Vorschau da. Netzwerk per `fetch` nur zu Servern mit CORS.

**4.2 Compiler-Projekt und Referenz-Assemblies**
- `src/CodeSail.Compiler` (Klassenbibliothek): `ICompileService` mit `CompileAsync(CompileRequest) → CompileResult(bytes, diagnostics)`. Ohne Abhängigkeit zu Blazor, damit es sich normal testen lässt.
- MSBuild-Target: Referenz-Assemblies aus `Microsoft.NETCore.App.Ref` (passend zum TargetFramework) nach `wwwroot/refs/net10.0/` kopieren, plus `refs/index.json` mit Liste und Größen.
- Lazy Load: `CodeSail.Compiler`, `Microsoft.CodeAnalysis`, `Microsoft.CodeAnalysis.CSharp` über `LazyAssemblyLoader`. Referenzen werden per `HttpClient` geladen und pro Sitzung im Speicher gehalten.
- Service Worker: diese Dateien **nicht** beim Installieren vorab laden (Ausschlussmuster), sondern beim ersten Gebrauch in einen eigenen Cache legen. So belastet der Compiler niemanden, der ihn nicht nutzt.

**4.3 Projekt erkennen**
- Zur offenen `.cs`-Datei die nächste `.csproj` darüber suchen (Dateibaum plus lokale Änderungen).
- Aus der `.csproj` lesen: `TargetFramework`, `OutputType`, `LangVersion`, `Nullable`, `ImplicitUsings`, `AllowUnsafeBlocks`, `RootNamespace`. Alle `.cs`-Dateien darunter einsammeln, ohne `bin/`, `obj/` und Unterprojekte.
- Implizite `global using`s wie das .NET SDK erzeugen.
- Ohne `.csproj`: die einzelne Datei als Konsolenprogramm mit Top-Level-Statements behandeln.
- `PackageReference`s erkennen und klar melden, dass sie (noch) nicht unterstützt werden.
- *Testbar* mit normalen Unit-Tests (reine Logik auf `RepoTree` und `CachedFile`).

**4.4 Übersetzen und Fehler anzeigen**
- Dateiinhalte aus dem Editor-Cache (inklusive nicht gespeicherter Änderungen) an den `CompileService`.
- Diagnosen als Monaco-Marker (`Global.SetModelMarkers`, Fehler rot, Warnungen gelb) in allen offenen Dateien. Liste „Probleme“ im Ausgabe-Panel; Tippen springt zur Stelle.
- Optional: während des Tippens im Hintergrund nur die Diagnosen prüfen (entprellt, ~1 s), ohne auszuführen.
- *Fertig, wenn:* Ein Tippfehler erzeugt an der richtigen Stelle eine rote Wellenlinie, nach der Korrektur verschwindet sie.

**4.5 Ausführen (Stufe 4B)**
- Runner im Web Worker: Assembly-Bytes und Referenzen übergeben, `Main`/Top-Level-Einstieg aufrufen, `Console.Out`/`Console.Error` zeilenweise zurückmelden.
- Eingabe: Textfeld im Ausgabe-Panel für `Console.ReadLine` (zeilenweise; vorab eingegebene Eingaben werden gepuffert).
- Abbrechen: Worker beenden und neu starten. Exit-Code und Laufzeit anzeigen.
- Ausnahmen mit Stacktrace; Zeilen, die auf eigene Dateien zeigen, sind antippbar.
- *Fertig, wenn:* Ein Konsolenprogramm mit Eingabe, Ausgabe und einer Ausnahme verhält sich wie lokal, und eine Endlosschleife lässt sich abbrechen, ohne dass die App hängt.

**4.6 Bedienung und Feinschliff**
- Ausgabe-Panel unten, ein- und ausklappbar (gleiches Muster wie die Vorschau), im Hochformat als eigene Ansicht.
- Tastenkürzel **Cmd+Enter** (nicht Cmd+R: das lädt in Safari die Seite neu), Button „▶ Ausführen“ in der Editor-Leiste bei passenden Dateien.
- Fortschrittsanzeige beim ersten Laden des Compilers („Compiler wird geladen, 1×, ca. X MB“ mit den Messwerten aus 4.0).
- Falls nötig: auch die Übersetzung in den Worker verlagern.

**4.7 Optional: NuGet-Pakete**
- Pakete direkt von `api.nuget.org` (Flat-Container) laden, `lib/netX` auswählen, im Cache ablegen. **Prüfen:** CORS-Header von nuget.org. Ohne CORS ist das ohne Server nicht machbar und entfällt.
- Nur reine .NET-Bibliotheken; Pakete mit nativen Teilen oder Source-Generatoren nicht.

### Bewusste Grenzen

- Kein Dateisystem außer dem Arbeitsspeicher, keine Prozesse, kein `System.Threading.Thread` (WASM ohne Threads). Netzwerk nur per `HttpClient` und nur zu Servern, die CORS erlauben.
- Keine GUI-Programme (WinForms/WPF/MAUI), keine ASP.NET-Server.
- Ausführungsgeschwindigkeit: Der .NET-Interpreter in WebAssembly ist deutlich langsamer als nativ. Für Lern- und Testprogramme reicht das, für Rechenlast nicht.

### Risiken und offene Fragen (im Spike klären)

| Risiko | Auswirkung | Gegenmaßnahme |
|---|---|---|
| Downloadgröße von Roslyn und Referenzen (geschätzt mehrere MB komprimiert, **messen**) | Langer erster Start des Compilers, Speicher auf dem iPad | Lazy Load, eigener Cache erst bei Nutzung, nur benötigte Referenzen laden |
| Safari begrenzt den Speicher von Web Workers und WASM | Abstürze bei großen Projekten | Messen; Projektgröße begrenzen; Warnhinweis |
| Zweite .NET-Laufzeit im Worker ist aufwendig oder instabil | 4.5 verzögert sich | Übergangslösung: Ausführen im Haupt-Thread mit klarer Warnung, dass Endlosschleifen die App blockieren |
| Roslyn langsam beim ersten Lauf (JIT/Interpreter) | Träge Bedienung | Compilation-Objekt wiederverwenden, nur geänderte Syntaxbäume neu parsen |
| CORS bei nuget.org | 4.7 entfällt | Ist optional |

### Tests

- `CodeSail.Compiler` und die Projekterkennung laufen auch unter normalem .NET. Deshalb lassen sich Übersetzung, Diagnosen und Projekterkennung als gewöhnliche xUnit-Tests prüfen, ohne Browser.
- Ausführung im Worker und die Ausgabe im Panel prüft ein bUnit-Test mit einem simulierten Runner. Dazu kommt ein manueller Test auf dem iPad je Meilenstein.
- CI misst die Größe der Compiler-Dateien und warnt bei deutlichem Wachstum.

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
