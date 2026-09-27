(() => {
    "use strict";
    const isTestMode = () => new URLSearchParams(location.search).get("test") === "1";
    const openDatabase = () => new Promise((resolve, reject) => {
        const request = indexedDB.open("GridSpace", 1);
        request.onupgradeneeded = () => request.result.createObjectStore("workbooks");
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error);
        request.onblocked = () => reject(new Error("Recovery storage is blocked by another tab."));
    });
    const toBase64 = bytes => {
        let binary = "";
        for (let i = 0; i < bytes.length; i += 32768) binary += String.fromCharCode(...bytes.subarray(i, i + 32768));
        return btoa(binary);
    };
    globalThis.gridSpaceStorage = {
        isTestMode,
        publishDiagnostics: json => { if (isTestMode()) globalThis.gridSpaceDiagnostics = Object.freeze(JSON.parse(json)); },
        async load() {
            if (isTestMode()) return "";
            const db = await openDatabase();
            try {
                return await new Promise((resolve, reject) => {
                    const transaction = db.transaction("workbooks", "readonly");
                    const request = transaction.objectStore("workbooks").get("recovery");
                    request.onsuccess = () => resolve(request.result || ""); request.onerror = () => reject(request.error);
                });
            } finally { db.close(); }
        },
        async save(json) {
            if (isTestMode()) return "";
            const db = await openDatabase();
            try {
                await new Promise((resolve, reject) => {
                    const transaction = db.transaction("workbooks", "readwrite");
                    transaction.objectStore("workbooks").put(json, "recovery");
                    transaction.oncomplete = () => resolve(); transaction.onerror = () => reject(transaction.error); transaction.onabort = () => reject(transaction.error || new Error("Recovery write was aborted."));
                });
                return "";
            } finally { db.close(); }
        },
        open() {
            return new Promise((resolve, reject) => {
                const input = document.createElement("input"); input.type = "file"; input.accept = ".gridspace,.json,.xlsx,.csv,.tsv,.txt"; input.style.display = "none";
                let finished = false;
                const finish = (value, error) => { if (finished) return; finished = true; input.remove(); error ? reject(error) : resolve(value); };
                input.oncancel = () => finish("");
                input.onchange = async () => {
                    try {
                        const file = input.files?.[0]; if (!file) return finish("");
                        if (file.size > 32 * 1024 * 1024) throw new Error("The workbook exceeds the 32 MB import limit.");
                        const bytes = new Uint8Array(await file.arrayBuffer()); finish(JSON.stringify({ name: file.name, base64: toBase64(bytes) }));
                    } catch (error) { finish("", error); }
                };
                document.body.appendChild(input); input.click();
            });
        },
        async download(name, base64, contentType) {
            const binary = atob(base64); const bytes = new Uint8Array(binary.length);
            for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
            const url = URL.createObjectURL(new Blob([bytes], { type: contentType }));
            const anchor = document.createElement("a"); anchor.href = url; anchor.download = name; anchor.style.display = "none";
            document.body.appendChild(anchor); anchor.click(); anchor.remove(); setTimeout(() => URL.revokeObjectURL(url), 60000); return "";
        }
    };
})();
