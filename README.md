# ⚓️ CodeSail

**CodeSail** is a modern, lightweight, and fully client-side IDE tailored specifically for iPads and mobile tablets. Built on top of **Blazor WebAssembly (.NET)** and compiled into a **Progressive Web App (PWA)**, CodeSail bypasses traditional OS sandboxing limitations by running entirely within the browser's WebAssembly container. 

It connects directly to your **GitHub** repositories using the GitHub REST API, allowing you to seamlessly pull, edit, and commit code on the go—even when offline.

---

## ✨ Features

- **Designed for iPadOS:** Fully optimized for touch interactions, Apple Magic Keyboard, and modern tablet viewports. No heavy desktop UI patterns.
- **Serverless Architecture:** Since it runs entirely on Blazor WASM, the app is 100% client-side. Zero backend server costs, maximum privacy.
- **Direct GitHub Integration:** Powered by `Octokit.NET` via OAuth / Personal Access Tokens. Fetch repo trees, open files, and push commits natively.
- **Monaco Editor Core:** Powered by the same editor engine as VS Code (`BlazorMonaco`), bringing rich syntax highlighting, multi-cursor editing, and structural auto-completion directly to your iPad.
- **Offline-First Workflow:** Uses `IndexedDB` to mirror your active files locally. Edit code in mid-air or off the grid, and sync/commit your changes once you're back online.

---

## 🛠️ Tech Stack

- **Frontend Framework:** Blazor WebAssembly (.NET 8.0 / .NET 9.0)
- **Editor UI:** Monaco Editor via `BlazorMonaco`
- **GitHub Client:** `Octokit.NET`
- **Local Storage:** IndexedDB (via browser interop)
- **Styling:** Tailwind CSS / Modern Minimal CSS custom design

---

## 🚀 Getting Started (Local Development)

### Prerequisites
- [.NET SDK](https://dotnet.microsoft.com/download) (Version 8.0 or newer)
- A GitHub account and a **Personal Access Token (Fine-grained)** with `contents:write` permissions.

### Setup
1. Clone this repository:
   ```bash
   git clone https://github.com/yourusername/CodeSail.git
   cd CodeSail
   ```

2. Restore NuGet dependencies:
   ```bash
   dotnet restore
   ```

3. Run the application:
   ```bash
   dotnet watch run
   ```
   Open `https://localhost:xxxx` in your browser. Toggle **Developer Tools > Device Toolbar** and select **iPad** to test the responsive layout.

---

## 📱 Deploying as a PWA

Because CodeSail is entirely static, you can host it completely free of charge on:
- **GitHub Pages**
- **Vercel**
- **Cloudflare Pages**

Once deployed, simply open the URL in **Safari on your iPad**, tap the **Share** button, and select **"Add to Home Screen"**. CodeSail will launch as a standalone, full-screen app without browser address bars.

---

## 🗺️ Roadmap

- [ ] **Phase 1:** GitHub API Authentication & Repository Tree Explorer *(Current)*
- [ ] **Phase 2:** Monaco Editor Integration with Offline-Caching (IndexedDB)
- [ ] **Phase 3:** Full Commit & Branch management workflow
- [ ] **Phase 4:** In-browser compilation experimentation using Roslyn (`Microsoft.CodeAnalysis`)

---

## 📄 License

Distributed under the MIT License. See `LICENSE` for more information.