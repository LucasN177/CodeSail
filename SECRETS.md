# Secrets & Repository-Einstellungen

CodeSail läuft komplett im Browser – es gibt **keinen Server und keine Server-Secrets**. Das GitHub-Token jedes Nutzers wird zur Laufzeit in der App eingegeben und nur im Browser gespeichert. Es gehört **niemals** ins Repo oder in die Repo-Secrets.

## Aktuell benötigt (Phase 0–3)

**Keine eigenen Secrets.** Beide Workflows (`ci.yml`, `deploy.yml`) verwenden nur das automatisch bereitgestellte `GITHUB_TOKEN`; die nötigen Rechte sind direkt im Workflow über `permissions:` gesetzt.

Einmalig manuell einzustellen:

| Wo | Einstellung |
|---|---|
| *Settings → Pages → Build and deployment → Source* | **GitHub Actions** auswählen |
| *Settings → Actions → General → Workflow permissions* | Standard „Read repository contents“ genügt |

> GitHub Pages ist für **öffentliche** Repositories kostenlos. Bei einem privaten Repo wäre GitHub Pro nötig – dann das Repo öffentlich lassen.

## Optional: OAuth-Login über Cloudflare Worker (erst nach Phase 1)

Nur falls statt Personal Access Tokens ein „Mit GitHub anmelden“-Button gewünscht ist (siehe `docs/IMPLEMENTATION_PLAN.md`). Alle genannten Dienste sind im Gratis-Tarif nutzbar.

Anlegen unter *Settings → Secrets and variables → Actions*:

| Name | Art | Inhalt | Woher |
|---|---|---|---|
| `GH_OAUTH_CLIENT_SECRET` | Secret | Client-Secret der GitHub-OAuth-App | GitHub → *Settings → Developer settings → OAuth Apps* |
| `CLOUDFLARE_API_TOKEN` | Secret | API-Token mit Recht „Workers Scripts: Edit“ | Cloudflare Dashboard → *My Profile → API Tokens* |
| `CLOUDFLARE_ACCOUNT_ID` | Secret | Account-ID | Cloudflare Dashboard → Workers-Übersicht (rechte Spalte) |
| `GH_OAUTH_CLIENT_ID` | **Variable** (kein Secret) | Client-ID der OAuth-App | wie oben; ist öffentlich und wird in die App eingebaut |
| `OAUTH_PROXY_URL` | **Variable** | URL des Workers, z. B. `https://codesail-auth.<name>.workers.dev` | nach erstem Worker-Deploy |

## Für die lokale Entwicklung

Kein Secret nötig. Zum Testen in der App einen **Fine-grained Personal Access Token** erstellen (*GitHub → Settings → Developer settings → Fine-grained tokens*):

- Repository access: nur die Test-Repos
- Permissions: **Contents: Read and write**, **Metadata: Read** (automatisch)

Den Token nur in der laufenden App eingeben, nicht in Dateien wie `appsettings.json` speichern.
