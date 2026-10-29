# ⚓️ CodeSail

[![CI](https://github.com/LucasN177/CodeSail/actions/workflows/ci.yml/badge.svg)](https://github.com/LucasN177/CodeSail/actions/workflows/ci.yml)
[![Deploy](https://github.com/LucasN177/CodeSail/actions/workflows/deploy.yml/badge.svg)](https://github.com/LucasN177/CodeSail/actions/workflows/deploy.yml)

**CodeSail** is a lightweight, fully client-side code editor built for iPads and other tablets. It runs on **Blazor WebAssembly (.NET 10)** as an installable **Progressive Web App (PWA)**, so everything happens inside the browser. There is no backend.

It connects straight to your **GitHub** repositories through the GitHub REST API. You can browse and edit code on the go, even offline.

**Live app:** https://lucasn177.github.io/CodeSail/

---

## ✨ Features

| | Status |
|---|---|
| **GitHub sign-in** with a fine-grained Personal Access Token, optional "stay signed in" | ✅ |
| **Repository picker** with search | ✅ |
| **File explorer**: full repo tree in one API call, branch switcher, quick file search | ✅ |
| **Monaco editor** (the VS Code engine): syntax highlighting for many languages, multi-cursor, tabs with separate undo history | ✅ |
| **Offline-first**: files, trees and branch lists are mirrored to IndexedDB; edits are saved locally as you type and survive restarts | ✅ |
| **Offline indicator** and API rate-limit display | ✅ |
| Commit, branch and pull-request workflow | 🚧 Phase 3 |
| In-browser C# compilation (Roslyn) | 🧪 Phase 4 |

### How editing works today

1. Open a repository, pick a branch and tap a file.
2. Changes are written to the device's IndexedDB about half a second after you stop typing. **Cmd + S** saves immediately.
3. Edited files show a **●** in their tab and in the explorer. They reopen automatically the next time you open that branch, even after the app was closed or the device was offline.
4. Use **⋮ → Discard changes** to go back to the GitHub version.

> Pushing changes back to GitHub comes in Phase 3. Until then, local edits stay on the device. Signing out deletes the token and all offline copies, and CodeSail warns you first if there are unsaved edits.

---

## 🛠️ Tech Stack

All dependencies are free and open source.

- **Framework:** Blazor WebAssembly (.NET 10), PWA template
- **UI components:** [MudBlazor](https://mudblazor.com/) (MIT)
- **Editor:** Monaco Editor via [BlazorMonaco](https://github.com/serdarciplak/BlazorMonaco) (MIT)
- **GitHub client:** [Octokit.NET](https://github.com/octokit/octokit.net) (MIT)
- **Local storage:** IndexedDB through a small JS module (`wwwroot/js/codesail.js`), with no extra library
- **Tests:** xUnit + [bUnit](https://bunit.dev/)
- **CI/CD & hosting:** GitHub Actions + GitHub Pages

---

## 🚀 Getting Started (Local Development)

### Prerequisites
- [.NET SDK 10](https://dotnet.microsoft.com/download). The version is pinned in `global.json`.
- A GitHub **fine-grained Personal Access Token** ([create one](https://github.com/settings/personal-access-tokens/new)) with:
  - **Repository access:** the repositories you want to edit
  - **Permissions:** *Contents: Read and write* (*Metadata: Read* is added automatically)

### Run
```bash
git clone https://github.com/LucasN177/CodeSail.git
cd CodeSail
dotnet watch --project src/CodeSail
```
Open http://localhost:5050 and sign in with your token. In the browser dev tools, switch on the **Device Toolbar** and choose an iPad to check the tablet layout.

### Test
```bash
dotnet test
```

### Project structure
```
src/CodeSail/
  Components/        FileExplorer, SourceEditor (Monaco wrapper)
  Layout/            App shell: app bar, auth guard, offline/rate-limit indicators
  Pages/             Home (sign-in), Repos, Workspace
  Services/          AuthState, WorkspaceService (online/offline loading), BrowserEnvironment
  Services/GitHub/   Octokit wrapper, tree builder, error messages
  Services/Storage/  Token store, IndexedDB file cache
  wwwroot/js/        IndexedDB + browser interop module
tests/CodeSail.Tests/
docs/IMPLEMENTATION_PLAN.md
```

---

## 🔐 Security & Privacy

- Your token never leaves the device except in requests to `api.github.com`.
- By default it is kept in `sessionStorage` and removed when the tab closes. With "stay signed in" it goes to `localStorage`.
- There is no server and no telemetry.

The repository itself needs **no secrets**. See [SECRETS.md](SECRETS.md).

---

## 🌐 Deployment

Every push to `main` builds the app and publishes it to GitHub Pages ([deploy.yml](.github/workflows/deploy.yml)). One-time setup: **Settings → Pages → Source: GitHub Actions**.

## 📱 Using as a PWA

Open the live URL in **Safari on your iPad**, tap **Share → Add to Home Screen**. CodeSail then launches full-screen without browser bars. After the first launch the app itself also loads offline.

---

## 🗺️ Roadmap

- [x] **Phase 0:** Project setup, CI, GitHub Pages deployment
- [x] **Phase 1:** GitHub authentication & repository tree explorer
- [x] **Phase 2:** Monaco editor integration with offline caching (IndexedDB)
- [ ] **Phase 3:** Commit & branch management workflow *(next)*
- [ ] **Phase 4:** In-browser compilation experiments with Roslyn (`Microsoft.CodeAnalysis`)

Details: [docs/IMPLEMENTATION_PLAN.md](docs/IMPLEMENTATION_PLAN.md)

---

## 📄 License

Distributed under the MIT License. See [LICENSE](LICENSE).
