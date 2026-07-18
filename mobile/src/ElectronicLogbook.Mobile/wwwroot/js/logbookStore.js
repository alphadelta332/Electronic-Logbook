(() => {
    const databaseName = "electronic-logbook";
    const storeName = "portable-documents";
    const version = 1;

    function openDatabase() {
        return new Promise((resolve, reject) => {
            const request = indexedDB.open(databaseName, version);
            request.onupgradeneeded = () => {
                const database = request.result;
                if (!database.objectStoreNames.contains(storeName)) {
                    database.createObjectStore(storeName);
                }
            };
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error);
        });
    }

    async function withStore(mode, callback) {
        const database = await openDatabase();
        try {
            return await new Promise((resolve, reject) => {
                const transaction = database.transaction(storeName, mode);
                const store = transaction.objectStore(storeName);
                const request = callback(store);
                request.onsuccess = () => resolve(request.result);
                request.onerror = () => reject(request.error);
                transaction.onerror = () => reject(transaction.error);
            });
        } finally {
            database.close();
        }
    }

    window.electronicLogbookStore = {
        load: (key) => withStore("readonly", (store) => store.get(key)),
        save: (key, value) => withStore("readwrite", (store) => store.put(value, key))
    };
})();
