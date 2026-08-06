(() => {
    const databaseName = "electronic-logbook";
    const documentStoreName = "portable-documents";
    const keyStoreName = "portable-keys";
    const version = 2;
    const maxElogbookBytes = 64 * 1024 * 1024;

    function openDatabase() {
        return new Promise((resolve, reject) => {
            const request = indexedDB.open(databaseName, version);
            request.onupgradeneeded = () => {
                const database = request.result;
                if (!database.objectStoreNames.contains(documentStoreName)) {
                    database.createObjectStore(documentStoreName);
                }

                if (!database.objectStoreNames.contains(keyStoreName)) {
                    database.createObjectStore(keyStoreName);
                }
            };
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error);
        });
    }

    async function withStore(storeName, mode, callback) {
        const database = await openDatabase();
        try {
            return await new Promise((resolve, reject) => {
                const transaction = database.transaction(storeName, mode);
                const store = transaction.objectStore(storeName);
                const request = callback(store);
                let result;
                request.onsuccess = () => {
                    result = request.result;
                };
                request.onerror = () => reject(request.error);
                transaction.onerror = () => reject(transaction.error);
                transaction.onabort = () => reject(transaction.error);
                transaction.oncomplete = () => resolve(result);
            });
        } finally {
            database.close();
        }
    }

    async function createNonExtractablePackageKey() {
        if (!globalThis.crypto?.subtle) {
            throw new Error("Web Crypto is not available in this browser.");
        }

        return await crypto.subtle.generateKey(
            { name: "AES-GCM", length: 256 },
            false,
            ["encrypt", "decrypt"]);
    }

    async function getRequiredPackageKey(keyName) {
        const key = await withStore(keyStoreName, "readonly", (store) => store.get(keyName));
        if (!key) {
            throw new Error("Package key is not available.");
        }

        return key;
    }

    function nativeKeyPlugin() {
        const plugin = globalThis.Capacitor?.Plugins?.ElectronicLogbookNativeFiles;
        return globalThis.Capacitor?.isNativePlatform?.() && plugin?.hasPackageKey ? plugin : null;
    }

    window.electronicLogbookStore = {
        load: (key) => withStore(documentStoreName, "readonly", (store) => store.get(key)),
        save: (key, value) => withStore(documentStoreName, "readwrite", (store) => store.put(value, key))
    };

    function normalizePreferences(value) {
        const [themeMode, accent] = String(value ?? "").split("|", 2);
        return {
            themeMode: themeMode === "Light" || themeMode === "Dark" || themeMode === "System" ? themeMode : "System",
            accent: ["Forest", "Ocean", "Sky", "Indigo", "Violet", "Plum", "Rose", "Red", "Orange", "Amber", "Gold", "Teal"].includes(accent) ? accent : "Forest"
        };
    }

    function applyTheme(value) {
        const preferences = normalizePreferences(value);
        const themeName = preferences.themeMode.toLowerCase();
        document.documentElement.setAttribute("data-elb-theme", themeName);
        document.documentElement.setAttribute("data-elb-accent", preferences.accent.toLowerCase());
        document.documentElement.style.colorScheme = themeName === "system" ? "light dark" : themeName;
    }

    window.electronicLogbookUiPreferences = {
        load: (key) => localStorage.getItem(key),
        save: (key, value) => {
            const preferences = normalizePreferences(value);
            const serialized = `${preferences.themeMode}|${preferences.accent}`;
            localStorage.setItem(key, serialized);
            applyTheme(serialized);
        },
        applyTheme: (value) => applyTheme(value),
        isSystemDark: () => globalThis.matchMedia?.("(prefers-color-scheme: dark)")?.matches ?? false
    };

    applyTheme(localStorage.getItem("electronic-logbook.ui-preferences"));

    window.electronicLogbookNavigation = {
        scrollMainToTop: () => {
            const main = document.querySelector(".app-main");
            if (!main) {
                return;
            }

            main.scrollTop = 0;
            main.scrollLeft = 0;
        },
        handleAndroidBack: () => {
            const path = location.pathname.replace(/\/+$/, "") || "/";
            if (path === "/") {
                return false;
            }

            if (history.length > 1) {
                history.back();
            } else {
                location.assign("/");
            }

            return true;
        }
    };

    window.electronicLogbookKeys = {
        isSupported: () => Boolean(nativeKeyPlugin() || (globalThis.crypto?.subtle && globalThis.indexedDB)),
        hasPackageKey: async (keyName) => {
            const native = nativeKeyPlugin();
            if (native) {
                return Boolean((await native.hasPackageKey({ keyName })).exists);
            }

            return Boolean(await withStore(keyStoreName, "readonly", (store) => store.get(keyName)));
        },
        ensurePackageKey: async (keyName) => {
            const native = nativeKeyPlugin();
            if (native) {
                return Boolean((await native.ensurePackageKey({ keyName })).created);
            }

            const existing = await withStore(keyStoreName, "readonly", (store) => store.get(keyName));
            if (existing) {
                return false;
            }

            const key = await createNonExtractablePackageKey();
            await withStore(keyStoreName, "readwrite", (store) => store.put(key, keyName));
            return true;
        },
        importPackageKey: async (keyName, keyBytes) => {
            const native = nativeKeyPlugin();
            if (native) {
                return Boolean((await native.importPackageKey({ keyName, keyBytes: Array.from(keyBytes) })).imported);
            }

            if (!globalThis.crypto?.subtle) {
                throw new Error("Web Crypto is not available in this browser.");
            }

            const key = await crypto.subtle.importKey(
                "raw",
                new Uint8Array(keyBytes),
                { name: "AES-GCM" },
                false,
                ["encrypt", "decrypt"]);
            await withStore(keyStoreName, "readwrite", (store) => store.put(key, keyName));
            return true;
        },
        deletePackageKey: async (keyName) => {
            const native = nativeKeyPlugin();
            if (native) {
                await native.deletePackageKey({ keyName });
                return;
            }

            await withStore(keyStoreName, "readwrite", (store) => store.delete(keyName));
        },
        encrypt: async (keyName, nonce, plaintext, additionalData) => {
            const native = nativeKeyPlugin();
            if (native) {
                const result = await native.encryptPackagePayload({
                    keyName,
                    nonce: Array.from(nonce),
                    plaintext: Array.from(plaintext),
                    additionalData: Array.from(additionalData)
                });
                return {
                    ciphertext: new Uint8Array(result.ciphertext),
                    tag: new Uint8Array(result.tag)
                };
            }

            const key = await getRequiredPackageKey(keyName);
            const encrypted = new Uint8Array(await crypto.subtle.encrypt(
                {
                    name: "AES-GCM",
                    iv: new Uint8Array(nonce),
                    additionalData: new Uint8Array(additionalData),
                    tagLength: 128
                },
                key,
                new Uint8Array(plaintext)));
            return {
                ciphertext: encrypted.slice(0, encrypted.length - 16),
                tag: encrypted.slice(encrypted.length - 16)
            };
        },
        decrypt: async (keyName, nonce, ciphertext, tag, additionalData) => {
            const native = nativeKeyPlugin();
            if (native) {
                const result = await native.decryptPackagePayload({
                    keyName,
                    nonce: Array.from(nonce),
                    ciphertext: Array.from(ciphertext),
                    tag: Array.from(tag),
                    additionalData: Array.from(additionalData)
                });
                return new Uint8Array(result.plaintext);
            }

            const key = await getRequiredPackageKey(keyName);
            const encrypted = new Uint8Array(ciphertext.length + tag.length);
            encrypted.set(new Uint8Array(ciphertext));
            encrypted.set(new Uint8Array(tag), ciphertext.length);
            return new Uint8Array(await crypto.subtle.decrypt(
                {
                    name: "AES-GCM",
                    iv: new Uint8Array(nonce),
                    additionalData: new Uint8Array(additionalData),
                    tagLength: 128
                },
                key,
                encrypted));
        }
    };

    window.electronicLogbookFiles = {
        pick: (accept) => new Promise((resolve, reject) => {
            const input = document.createElement("input");
            input.type = "file";
            input.accept = accept;
            input.style.display = "none";
            let pickerSettled = false;
            const settle = (callback) => {
                if (pickerSettled) {
                    return;
                }

                pickerSettled = true;
                input.remove();
                callback();
            };
            input.onchange = async () => {
                const file = input.files?.[0];
                if (!file) {
                    settle(() => resolve(null));
                    return;
                }

                try {
                    if (file.size === 0) {
                        settle(() => reject(new Error("Selected file is empty.")));
                        return;
                    }

                    if (file.size > maxElogbookBytes) {
                        settle(() => reject(new Error(`Selected file is larger than the ${maxElogbookBytes} byte package limit.`)));
                        return;
                    }

                    const bytes = new Uint8Array(await file.arrayBuffer());
                    settle(() => resolve({
                        fileName: file.name,
                        contentType: file.type || "application/octet-stream",
                        bytes
                    }));
                } catch (error) {
                    settle(() => reject(error));
                }
            };
            input.oncancel = () => {
                settle(() => resolve(null));
            };
            document.body.appendChild(input);
            input.click();
        }),
        canShare: (fileName, bytes, contentType) => {
            if (!navigator.canShare || typeof File === "undefined") {
                return false;
            }

            try {
                const file = new File([new Uint8Array(bytes)], fileName, { type: contentType });
                return navigator.canShare({ files: [file] });
            } catch {
                return false;
            }
        },
        share: async (fileName, bytes, contentType) => {
            if (!navigator.share || typeof File === "undefined") {
                throw new Error("Web Share API file sharing is not available.");
            }

            const file = new File([new Uint8Array(bytes)], fileName, { type: contentType });
            await navigator.share({
                files: [file],
                title: fileName
            });
        },
        nativeShareOrDownload: async (fileName, bytes, contentType) => {
            const plugin = globalThis.Capacitor?.Plugins?.ElectronicLogbookNativeFiles;
            if (!globalThis.Capacitor?.isNativePlatform?.() || !plugin?.saveAndShare) {
                return null;
            }

            return await plugin.saveAndShare({
                fileName,
                contentType,
                bytes: Array.from(new Uint8Array(bytes))
            });
        },
        download: (fileName, bytes, contentType) => {
            const blob = new Blob([new Uint8Array(bytes)], { type: contentType });
            const url = URL.createObjectURL(blob);
            const link = document.createElement("a");
            link.href = url;
            link.download = fileName;
            link.rel = "noopener";
            document.body.appendChild(link);
            link.click();
            link.remove();
            URL.revokeObjectURL(url);
        }
    };
})();
