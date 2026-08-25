(() => {
    const databaseName = "electronic-logbook";
    const documentStoreName = "portable-documents";
    const keyStoreName = "portable-keys";
    const version = 2;
    const maxElogbookBytes = 64 * 1024 * 1024;
    const networkRestoredHandlers = new Map();
    let nextNetworkRestoredHandlerId = 1;

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

    function nativeCredentialsPlugin() {
        const plugin = globalThis.Capacitor?.Plugins?.ElectronicLogbookCredentials;
        return globalThis.Capacitor?.isNativePlatform?.() && plugin?.getGoogleIdToken ? plugin : null;
    }

    function nativeNetworkPlugin() {
        const plugin = globalThis.Capacitor?.Plugins?.Network;
        return globalThis.Capacitor?.isNativePlatform?.() && plugin?.getStatus ? plugin : null;
    }

    async function notifyNetworkRestored(dotNetReference) {
        try {
            await dotNetReference.invokeMethodAsync("HandleNetworkRestoredAsync");
        } catch {
            console.warn("Electronic Logbook network-restored callback failed.");
        }
    }

    window.electronicLogbookStore = {
        load: (key) => withStore(documentStoreName, "readonly", (store) => store.get(key)),
        save: (key, value) => withStore(documentStoreName, "readwrite", (store) => store.put(value, key)),
        delete: (key) => withStore(documentStoreName, "readwrite", (store) => store.delete(key))
    };

    window.electronicLogbookNetwork = {
        isOnline: async () => {
            const plugin = nativeNetworkPlugin();
            if (plugin) {
                const status = await plugin.getStatus();
                return status?.connected === true;
            }

            return navigator.onLine !== false;
        },
        subscribe: async (dotNetReference) => {
            const subscriptionId = nextNetworkRestoredHandlerId++;
            const plugin = nativeNetworkPlugin();
            if (plugin?.addListener) {
                const initialStatus = await plugin.getStatus();
                let wasConnected = initialStatus?.connected === true;
                const listener = await plugin.addListener("networkStatusChange", (status) => {
                    const connected = status?.connected === true;
                    if (connected && !wasConnected) {
                        void notifyNetworkRestored(dotNetReference);
                    }
                    wasConnected = connected;
                });
                networkRestoredHandlers.set(subscriptionId, { kind: "native", listener });
                return subscriptionId;
            }

            const handler = () => void notifyNetworkRestored(dotNetReference);
            networkRestoredHandlers.set(subscriptionId, { kind: "browser", handler });
            window.addEventListener("online", handler);
            return subscriptionId;
        },
        unsubscribe: async (subscriptionId) => {
            const subscription = networkRestoredHandlers.get(subscriptionId);
            if (!subscription) {
                return;
            }

            if (subscription.kind === "native") {
                await subscription.listener.remove();
            } else {
                window.removeEventListener("online", subscription.handler);
            }
            networkRestoredHandlers.delete(subscriptionId);
        }
    };

    window.electronicLogbookDiagnostics = {
        copy: async (redactedText) => {
            if (!navigator.clipboard?.writeText) {
                throw new Error("Clipboard access is not available.");
            }

            await navigator.clipboard.writeText(String(redactedText ?? ""));
        }
    };

    window.electronicLogbookCredentials = {
        getGoogleIdToken: async (options) => {
            const native = nativeCredentialsPlugin();
            if (!native) {
                throw new Error("Google sign-in is only available in the installed Android app.");
            }

            return await native.getGoogleIdToken(options);
        }
    };

    function normalizePreferences(value) {
        const [themeMode, accent] = String(value ?? "").split("|", 2);
        return {
            themeMode: themeMode === "Light" || themeMode === "Dark" || themeMode === "System" ? themeMode : "System",
            accent: ["Forest", "Ocean", "Sky", "Indigo", "Violet", "Plum", "Rose", "Teal"].includes(accent) ? accent : "Forest"
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

            const isFlightEntryPath = path === "/flights/new"
                || /^\/flights\/[^/]+\/edit$/.test(path);
            if (isFlightEntryPath) {
                history.replaceState(history.state, "", "/");
                globalThis.dispatchEvent(new PopStateEvent("popstate", { state: history.state }));
                return true;
            }

            if (history.length > 1) {
                history.back();
            } else {
                location.assign("/");
            }

            return true;
        }
    };

    const flightEntryControlSelector = ".flight-entry-page input, .flight-entry-page select, .flight-entry-page textarea";
    let focusedFlightEntryControl = null;
    let keyboardFocusTimer = null;

    function keepFocusedFlightEntryControlVisible() {
        const main = document.querySelector(".app-main");
        const control = focusedFlightEntryControl;
        if (!main || !control?.isConnected) {
            return;
        }

        const field = control.closest("label") ?? control;
        const actionHeader = control.closest(".flight-entry-page")?.querySelector(".entry-action-header");
        const headerClearance = Math.ceil(actionHeader?.getBoundingClientRect().height ?? 16);
        main.style.setProperty("--entry-scroll-padding-top", `${headerClearance}px`);
        field.scrollIntoView({ behavior: "instant", block: "nearest", inline: "nearest" });
    }

    function scheduleFocusedFlightEntryControlVisibilityCheck() {
        globalThis.cancelAnimationFrame?.(keyboardFocusTimer);
        keyboardFocusTimer = globalThis.requestAnimationFrame?.(keepFocusedFlightEntryControlVisible);
    }

    document.addEventListener("focusin", (event) => {
        if (!event.target?.matches?.(flightEntryControlSelector)) {
            return;
        }

        focusedFlightEntryControl = event.target;
        scheduleFocusedFlightEntryControlVisibilityCheck();
    });

    document.addEventListener("focusout", (event) => {
        if (event.target === focusedFlightEntryControl) {
            focusedFlightEntryControl = null;
        }
    });

    globalThis.visualViewport?.addEventListener("resize", scheduleFocusedFlightEntryControlVisibilityCheck);
    globalThis.visualViewport?.addEventListener("scroll", scheduleFocusedFlightEntryControlVisibilityCheck);

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
        getRecoveryPublicKey: async () => {
            const native = nativeKeyPlugin();
            if (!native?.getRecoveryPublicKey) {
                throw new Error("Account recovery keys are only available in the installed Android app.");
            }

            return await native.getRecoveryPublicKey();
        },
        wrapPackageKeyForRecoveryService: async (keyName, servicePublicKey) => {
            const native = nativeKeyPlugin();
            if (!native?.wrapPackageKeyForRecoveryService) {
                throw new Error("Account recovery wrapping is only available in the installed Android app.");
            }

            return await native.wrapPackageKeyForRecoveryService({ keyName, servicePublicKey });
        },
        importRecoveryEnvelope: async (keyName, wrappedKey) => {
            const native = nativeKeyPlugin();
            if (!native?.importRecoveryEnvelope) {
                throw new Error("Account recovery import is only available in the installed Android app.");
            }

            return Boolean((await native.importRecoveryEnvelope({ keyName, wrappedKey })).imported);
        },
        wrapPackageKeyForRecoveryCode: async (keyName, recoveryCode) => {
            const native = nativeKeyPlugin();
            if (!native?.wrapPackageKeyForRecoveryCode) {
                throw new Error("Recovery-code setup is only available in the installed Android app.");
            }
            return await native.wrapPackageKeyForRecoveryCode({ keyName, recoveryCode });
        },
        testRecoveryCodeEnvelope: async (keyName, recoveryCode, envelope) => {
            const native = nativeKeyPlugin();
            if (!native?.testRecoveryCodeEnvelope) {
                throw new Error("Recovery-code confirmation is only available in the installed Android app.");
            }
            return Boolean((await native.testRecoveryCodeEnvelope({ keyName, recoveryCode, envelope })).confirmed);
        },
        importRecoveryCodeEnvelope: async (keyName, recoveryCode, envelope) => {
            const native = nativeKeyPlugin();
            if (!native?.importRecoveryCodeEnvelope) {
                throw new Error("Recovery-code restore is only available in the installed Android app.");
            }
            return Boolean((await native.importRecoveryCodeEnvelope({ keyName, recoveryCode, envelope })).imported);
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
        nativeSaveToDevice: async (fileName, bytes, contentType) => {
            const plugin = globalThis.Capacitor?.Plugins?.ElectronicLogbookNativeFiles;
            if (!globalThis.Capacitor?.isNativePlatform?.() || !plugin?.saveToDevice) {
                return null;
            }

            return await plugin.saveToDevice({
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
