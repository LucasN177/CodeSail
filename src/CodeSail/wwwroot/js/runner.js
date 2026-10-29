// Führt JavaScript und TypeScript aus (Phase 4A).
//
// Der Code läuft in einem Web Worker, den ein iframe mit sandbox="allow-scripts" (ohne allow-same-origin) startet:
// - eigener Thread: Endlosschleifen frieren die App nicht ein,
// - undurchsichtiger Origin: kein Zugriff auf localStorage, IndexedDB oder das Token der App,
// - Abbrechen = iframe entfernen, damit endet auch der Worker.

const MAX_LINES = 5000;
const FLUSH_MS = 100;

let current = null;

// ---------- TypeScript ----------

const tsApi = () => globalThis.monaco?.typescript ?? globalThis.monaco?.languages?.typescript;

export function configureTypeScript() {
    const api = tsApi();
    if (!api) return;
    const options = api.typescriptDefaults.getCompilerOptions();
    if (options.sourceMap && options.target === api.ScriptTarget.ESNext) return;
    api.typescriptDefaults.setCompilerOptions({
        ...options,
        target: api.ScriptTarget.ESNext,
        module: api.ModuleKind.ESNext,
        sourceMap: true
    });
}

function flatten(messageText) {
    let text = typeof messageText === "string" ? messageText : messageText.messageText;
    let next = typeof messageText === "string" ? null : messageText.next;
    while (next?.length) {
        text += " " + next[0].messageText;
        next = next[0].next;
    }
    return text;
}

async function compileTypeScript(model) {
    configureTypeScript();
    const worker = await (await tsApi().getTypeScriptWorker())(model.uri);
    const key = model.uri.toString();
    const [syntactic, semantic, emit] = await Promise.all([
        worker.getSyntacticDiagnostics(key),
        worker.getSemanticDiagnostics(key),
        worker.getEmitOutput(key)
    ]);

    const toProblem = d => {
        const position = model.getPositionAt(d.start ?? 0);
        return { message: flatten(d.messageText), line: position.lineNumber, column: position.column };
    };
    const file = ext => emit.outputFiles.find(f => f.name.endsWith(ext))?.text;

    return {
        js: (file(".js") ?? "").replace(/\n\/\/# sourceMappingURL=.*\s*$/, ""),
        lineMap: file(".map") ? buildLineMap(file(".map")) : null,
        syntaxErrors: syntactic.map(toProblem),
        typeErrors: semantic.map(toProblem)
    };
}

// ---------- Source Map: generierte Zeile -> Zeile in der .ts-Datei ----------

const BASE64 = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

function decodeVlq(segment) {
    const values = [];
    let value = 0, shift = 0;
    for (const char of segment) {
        let digit = BASE64.indexOf(char);
        const more = digit & 32;
        digit &= 31;
        value += digit << shift;
        if (more) {
            shift += 5;
        } else {
            values.push(value & 1 ? -(value >> 1) : value >> 1);
            value = 0;
            shift = 0;
        }
    }
    return values;
}

/** Liefert ein Array: Index = generierte Zeile - 1, Wert = Originalzeile (1-basiert) oder null. */
export function buildLineMap(mapJson) {
    const { mappings } = JSON.parse(mapJson);
    const result = [];
    let sourceLine = 0;
    for (const line of mappings.split(";")) {
        let first = null;
        for (const segment of line.split(",")) {
            if (!segment) continue;
            const fields = decodeVlq(segment);
            if (fields.length >= 3) {
                sourceLine += fields[2];
                first ??= sourceLine + 1;
            }
        }
        result.push(first);
    }
    return result;
}

// ---------- Ausführen ----------

// Steht als erste Zeile vor dem Programm im Worker. Puffert die Konsole, damit eine Flut von
// Ausgaben (z. B. in einer Schleife) die App nicht überlastet.
const HARNESS = `const __q=[];let __n=0,__t=null;const __flush=()=>{__t=null;if(__q.length)postMessage({kind:"batch",items:__q.splice(0)})};const __push=m=>{if(++__n>${MAX_LINES}){if(__n===${MAX_LINES}+1)__q.push({kind:"console",level:"warn",text:"… weitere Ausgaben werden ausgelassen (Limit ${MAX_LINES} Zeilen)"});return}__q.push(m);if(__q.length>=200)__flush();else __t??=setTimeout(__flush,${FLUSH_MS})};const __fmt=v=>{if(typeof v==="string")return v;if(v instanceof Error)return v.stack||String(v);try{return JSON.stringify(v,(k,x)=>typeof x==="bigint"?x+"n":x===undefined?"undefined":x,2)??String(v)}catch{return String(v)}};for(const l of["log","info","warn","error","debug"])console[l]=(...a)=>__push({kind:"console",level:l,text:a.map(__fmt).join(" ")});console.table=d=>console.log(d);console.clear=()=>__push({kind:"clear"});const __report=(e,prefix="")=>{__push({kind:"error",message:prefix+(e instanceof Error?String(e):__fmt(e)),stack:e?.stack});__flush()};addEventListener("unhandledrejection",e=>{e.preventDefault();__report(e.reason,"Unbehandelte Promise-Ablehnung: ")});globalThis.__codesailDone=()=>{__flush();postMessage({kind:"done"})};(async()=>{`;

// Schließt die async-Funktion um das Programm (macht await auf oberster Ebene möglich)
const FOOTER = "\n})().then(globalThis.__codesailDone,e=>__report(e));\n";

// Läuft im iframe: nimmt den Code entgegen, startet den Worker und reicht seine Nachrichten durch.
// Ein klassischer Worker, weil Modul-Worker aus Blob-URLs in einem Origin ohne Herkunft nicht starten.
const BOOTSTRAP = `<!DOCTYPE html><meta charset="utf-8"><script>
addEventListener("message", e => {
  if (!e.data || typeof e.data.source !== "string") return;
  const url = URL.createObjectURL(new Blob([e.data.source], { type: "text/javascript" }));
  const worker = new Worker(url);
  worker.onmessage = m => parent.postMessage(m.data, "*");
  worker.onerror = err => { err.preventDefault(); parent.postMessage({ kind: "error", message: err.message, line: err.lineno, column: err.colno }, "*"); };
});
parent.postMessage({ kind: "ready" }, "*");
<\/script>`;

/**
 * Startet eine Datei. request: { runId, fileName, modelUri, language: "javascript" | "typescript" }.
 * Nachrichten gehen gebündelt an dotNetRef.OnRunMessages(runId, items).
 */
export async function run(container, request, dotNetRef) {
    stop();

    const model = globalThis.monaco.editor.getModel(globalThis.monaco.Uri.parse(request.modelUri));
    if (!model) throw new Error("Die Datei ist nicht im Editor geöffnet.");

    const run = { id: request.runId, pending: [], timer: null, lineMap: null, seenErrors: new Set() };
    const send = items => {
        run.pending.push(...items);
        run.timer ??= setTimeout(() => {
            run.timer = null;
            const batch = run.pending.splice(0);
            if (batch.length) dotNetRef.invokeMethodAsync("OnRunMessages", run.id, batch);
        }, 50);
    };

    let code = model.getValue();
    if (request.language === "typescript") {
        send([{ kind: "status", text: "TypeScript wird übersetzt …" }]);
        const result = await compileTypeScript(model);
        for (const p of result.typeErrors)
            send([{ kind: "problem", level: "warn", text: p.message, line: p.line, column: p.column }]);
        if (result.syntaxErrors.length) {
            for (const p of result.syntaxErrors)
                send([{ kind: "problem", level: "error", text: p.message, line: p.line, column: p.column }]);
            send([{ kind: "end", status: "failed" }]);
            return;
        }
        code = result.js;
        run.lineMap = result.lineMap;
    }

    const prepared = prepareScript(code);
    if (prepared.importLine) {
        send([{ kind: "problem", level: "error", line: prepared.importLine, column: 1,
            text: "import wird beim Ausführen noch nicht unterstützt. Lege den benötigten Code in dieselbe Datei." }]);
        send([{ kind: "end", status: "failed" }]);
        return;
    }
    code = prepared.code;

    // Zeile 1 im Worker ist das HARNESS; danach folgt das Programm
    const toSourceLine = generated => {
        const userLine = generated - 1;
        if (!Number.isFinite(userLine) || userLine < 1) return null;
        return run.lineMap ? (run.lineMap[userLine - 1] ?? null) : userLine;
    };
    const cleanStack = stack => stack?.replace(/blob:[^\s)]*?:(\d+):(\d+)/g,
        (_, line, column) => `${request.fileName}:${toSourceLine(Number(line)) ?? "?"}:${column}`);

    const iframe = document.createElement("iframe");
    iframe.setAttribute("sandbox", "allow-scripts");
    iframe.setAttribute("aria-hidden", "true");
    iframe.style.display = "none";
    iframe.srcdoc = BOOTSTRAP;

    const listener = e => {
        if (e.source !== iframe.contentWindow || !e.data) return;
        const message = e.data;
        if (message.kind === "ready") {
            iframe.contentWindow.postMessage({ source: HARNESS + "\n" + code + FOOTER }, "*");
            send([{ kind: "status", text: "Läuft …" }]);
            return;
        }
        const items = message.kind === "batch" ? message.items : [message];
        send(items.map(item => {
            if (item.kind !== "error") return item;
            // Ohne Zeilenangabe (Fehler aus der async-Funktion) die erste eigene Zeile aus dem Stacktrace nehmen
            const stackLine = item.stack?.match(/blob:[^\s)]*?:(\d+):\d+/)?.[1];
            const line = toSourceLine(item.line ?? Number(stackLine));
            // Fehler beim Laden des Moduls melden Worker und iframe teils doppelt
            const key = `${item.message}|${line}`;
            if (run.seenErrors.has(key)) return null;
            run.seenErrors.add(key);
            return { kind: "error", level: "error", text: item.message, line, column: line ? item.column : null, stack: cleanStack(item.stack) };
        }).filter(Boolean));
        if (items.some(item => item.kind === "error")) send([{ kind: "end", status: "failed" }]);
    };

    addEventListener("message", listener);
    current = { iframe, listener };
    container.appendChild(iframe);
}

/**
 * Macht aus Modul-Code ein Skript für den klassischen Worker, ohne Zeilen zu verschieben:
 * "export" wird entfernt (bzw. "export {…}" geleert), ein "import" wird gemeldet.
 */
export function prepareScript(code) {
    const lines = code.split("\n");
    for (let i = 0; i < lines.length; i++) {
        if (/^\s*import\s+[\w{*'"]/.test(lines[i])) return { code, importLine: i + 1 };
        lines[i] = lines[i]
            .replace(/^(\s*)export\s*\{[^}]*\};?\s*$/, "$1")
            .replace(/^(\s*)export\s+default\s+(?=(async\s+)?(function|class)\b)/, "$1")
            .replace(/^(\s*)export\s+(?=(async\s+)?(function|class|const|let|var)\b)/, "$1");
    }
    return { code: lines.join("\n"), importLine: null };
}

export function scrollToEnd(element) {
    element.scrollTop = element.scrollHeight;
}

/** Bricht den laufenden Code ab: Mit dem iframe endet auch der Worker. */
export function stop() {
    if (!current) return false;
    removeEventListener("message", current.listener);
    current.iframe.remove();
    current = null;
    return true;
}
