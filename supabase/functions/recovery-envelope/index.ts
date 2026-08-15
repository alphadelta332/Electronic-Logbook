const corsHeaders = {
  "access-control-allow-origin": "*",
  "access-control-allow-headers": "authorization, apikey, content-type",
  "access-control-allow-methods": "POST, OPTIONS",
  "cache-control": "no-store",
};

const uuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const fingerprintPattern = /^[0-9a-f]{64}$/;
const encoder = new TextEncoder();

class RecoveryError extends Error {
  constructor(
    readonly status: number,
    readonly code: string,
    message: string,
  ) {
    super(message);
  }
}

type RequestBody = {
  action?: unknown;
  logbookId?: unknown;
  deviceId?: unknown;
  devicePublicKey?: unknown;
  devicePublicKeyFingerprint?: unknown;
  devicePublicKeyAlgorithm?: unknown;
  wrappedPackageKey?: unknown;
  ingressKeyVersionId?: unknown;
  platformLabel?: unknown;
  deviceType?: unknown;
  recoveryCiphertext?: unknown;
  recoveryNonce?: unknown;
  recoverySalt?: unknown;
  recoveryAlgorithm?: unknown;
  recoveryKeyVersionId?: unknown;
};

type ManagedEnvelope = {
  ciphertext: string;
  nonce: string;
  key_version_id: string;
  wrapping_algorithm: string;
};

type RecoveryCodeEnvelope = ManagedEnvelope & { recovery_salt: string };

function requiredEnvironment(name: string): string {
  const value = Deno.env.get(name)?.trim();
  if (!value) {
    throw new RecoveryError(503, "RECOVERY_SERVICE_UNAVAILABLE", "Account recovery is temporarily unavailable.");
  }

  return value;
}

function jsonResponse(status: number, body: Record<string, unknown>): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { ...corsHeaders, "content-type": "application/json; charset=utf-8" },
  });
}

function requiredString(value: unknown, label: string, maximumLength = 8192): string {
  if (typeof value !== "string" || !value.trim() || value.length > maximumLength) {
    throw new RecoveryError(400, "RECOVERY_REQUEST_INVALID", `${label} is invalid.`);
  }

  return value.trim();
}

function requiredUuid(value: unknown, label: string): string {
  const result = requiredString(value, label, 64);
  if (!uuidPattern.test(result)) {
    throw new RecoveryError(400, "RECOVERY_REQUEST_INVALID", `${label} is invalid.`);
  }

  return result.toLowerCase();
}

function requiredDeviceType(value: unknown): "android" | "workbook" {
  const deviceType = requiredString(value, "Device type", 32);
  if (deviceType !== "android" && deviceType !== "workbook") {
    throw new RecoveryError(400, "RECOVERY_DEVICE_TYPE_INVALID", "This device type cannot use account recovery.");
  }

  return deviceType;
}

function decodeBase64(value: string, minimumBytes: number, maximumBytes: number): Uint8Array {
  if (value.length > Math.ceil(maximumBytes / 3) * 4 + 4 || !/^[A-Za-z0-9+/]+={0,2}$/.test(value)) {
    throw new RecoveryError(400, "RECOVERY_REQUEST_INVALID", "Recovery key material is invalid.");
  }

  let decoded: string;
  try {
    decoded = atob(value);
  } catch {
    throw new RecoveryError(400, "RECOVERY_REQUEST_INVALID", "Recovery key material is invalid.");
  }

  if (decoded.length < minimumBytes || decoded.length > maximumBytes) {
    throw new RecoveryError(400, "RECOVERY_REQUEST_INVALID", "Recovery key material is invalid.");
  }

  return Uint8Array.from(decoded, (character) => character.charCodeAt(0));
}

function encodeBase64(value: ArrayBuffer | Uint8Array): string {
  const bytes = value instanceof Uint8Array ? value : new Uint8Array(value);
  let binary = "";
  for (let index = 0; index < bytes.length; index += 1) {
    binary += String.fromCharCode(bytes[index]);
  }

  return btoa(binary);
}

function ownedBuffer(value: Uint8Array): ArrayBuffer {
  const copy = new Uint8Array(value.byteLength);
  copy.set(value);
  return copy.buffer;
}

function toHex(value: ArrayBuffer): string {
  return Array.from(new Uint8Array(value), (item) => item.toString(16).padStart(2, "0")).join("");
}

async function authenticate(request: Request): Promise<string> {
  const authorization = request.headers.get("authorization")?.trim();
  if (!authorization?.toLowerCase().startsWith("bearer ")) {
    throw new RecoveryError(401, "RECOVERY_AUTH_REQUIRED", "Sign in again to continue.");
  }

  const supabaseUrl = requiredEnvironment("SUPABASE_URL");
  const anonymousKey = requiredEnvironment("SUPABASE_ANON_KEY");
  const response = await fetch(`${supabaseUrl}/auth/v1/user`, {
    headers: { apikey: anonymousKey, authorization },
  });
  if (!response.ok) {
    throw new RecoveryError(401, "RECOVERY_AUTH_REQUIRED", "Sign in again to continue.");
  }

  const user = await response.json() as { id?: unknown };
  return requiredUuid(user.id, "Authenticated account");
}

async function serviceRpc<T>(name: string, parameters: Record<string, unknown>): Promise<T> {
  const supabaseUrl = requiredEnvironment("SUPABASE_URL");
  const serviceRoleKey = requiredEnvironment("SUPABASE_SERVICE_ROLE_KEY");
  const response = await fetch(`${supabaseUrl}/rest/v1/rpc/${name}`, {
    method: "POST",
    headers: {
      apikey: serviceRoleKey,
      authorization: `Bearer ${serviceRoleKey}`,
      "content-type": "application/json",
      accept: "application/json",
    },
    body: JSON.stringify(parameters),
  });

  if (!response.ok) {
    throw new RecoveryError(403, "RECOVERY_ACCESS_DENIED", "Account recovery could not be authorized.");
  }

  return await response.json() as T;
}

async function serviceConfiguration(): Promise<{
  publicKey: string;
  fingerprint: string;
  algorithm: string;
  keyVersionId: string;
}> {
  const publicKey = requiredEnvironment("RECOVERY_INGRESS_PUBLIC_KEY_SPKI_BASE64");
  const publicKeyBytes = decodeBase64(publicKey, 256, 8192);
  const fingerprint = toHex(await crypto.subtle.digest("SHA-256", ownedBuffer(publicKeyBytes)));
  publicKeyBytes.fill(0);
  return {
    publicKey,
    fingerprint,
    algorithm: "RSA-OAEP-256",
    keyVersionId: requiredEnvironment("RECOVERY_KEY_VERSION_ID"),
  };
}

async function importIngressPrivateKey(): Promise<CryptoKey> {
  const encoded = decodeBase64(requiredEnvironment("RECOVERY_INGRESS_PRIVATE_KEY_PKCS8_BASE64"), 512, 8192);
  try {
    return await crypto.subtle.importKey(
      "pkcs8",
      ownedBuffer(encoded),
      { name: "RSA-OAEP", hash: "SHA-256" },
      false,
      ["decrypt"],
    );
  } finally {
    encoded.fill(0);
  }
}

async function importDevicePublicKey(publicKey: string): Promise<CryptoKey> {
  const encoded = decodeBase64(publicKey, 256, 8192);
  try {
    return await crypto.subtle.importKey(
      "spki",
      ownedBuffer(encoded),
      { name: "RSA-OAEP", hash: "SHA-256" },
      false,
      ["encrypt"],
    );
  } catch {
    throw new RecoveryError(400, "RECOVERY_DEVICE_KEY_INVALID", "This device could not prepare account recovery.");
  } finally {
    encoded.fill(0);
  }
}

async function verifyDevicePublicKey(publicKey: string, expectedFingerprint: string): Promise<void> {
  if (!fingerprintPattern.test(expectedFingerprint)) {
    throw new RecoveryError(400, "RECOVERY_DEVICE_KEY_INVALID", "This device could not prepare account recovery.");
  }

  const encoded = decodeBase64(publicKey, 256, 8192);
  try {
    const actualFingerprint = toHex(await crypto.subtle.digest("SHA-256", ownedBuffer(encoded)));
    if (actualFingerprint !== expectedFingerprint) {
      throw new RecoveryError(400, "RECOVERY_DEVICE_KEY_INVALID", "This device could not prepare account recovery.");
    }
  } finally {
    encoded.fill(0);
  }
}

function managedAdditionalData(logbookId: string, keyVersionId: string): Uint8Array {
  return encoder.encode(`electronic-logbook|managed-service-v1|${logbookId}|${keyVersionId}`);
}

async function importManagedKey(): Promise<CryptoKey> {
  const encoded = decodeBase64(requiredEnvironment("RECOVERY_KEK_BASE64"), 32, 32);
  try {
    return await crypto.subtle.importKey("raw", ownedBuffer(encoded), "AES-GCM", false, ["encrypt", "decrypt"]);
  } finally {
    encoded.fill(0);
  }
}

async function decryptIngressEnvelope(wrappedPackageKey: string): Promise<Uint8Array> {
  const encrypted = decodeBase64(wrappedPackageKey, 256, 8192);
  try {
    const plaintext = new Uint8Array(await crypto.subtle.decrypt(
      { name: "RSA-OAEP" },
      await importIngressPrivateKey(),
      ownedBuffer(encrypted),
    ));
    if (plaintext.length !== 32) {
      plaintext.fill(0);
      throw new RecoveryError(400, "RECOVERY_ENVELOPE_INVALID", "The recovery envelope is invalid.");
    }

    return plaintext;
  } catch (error) {
    if (error instanceof RecoveryError) {
      throw error;
    }
    throw new RecoveryError(400, "RECOVERY_ENVELOPE_INVALID", "The recovery envelope is invalid.");
  } finally {
    encrypted.fill(0);
  }
}

async function wrapManagedEnvelope(
  packageKey: Uint8Array,
  logbookId: string,
  keyVersionId: string,
): Promise<{ ciphertext: string; nonce: string }> {
  const nonce = crypto.getRandomValues(new Uint8Array(12));
  const ciphertext = await crypto.subtle.encrypt(
    {
      name: "AES-GCM",
      iv: ownedBuffer(nonce),
      additionalData: ownedBuffer(managedAdditionalData(logbookId, keyVersionId)),
      tagLength: 128,
    },
    await importManagedKey(),
    ownedBuffer(packageKey),
  );
  return { ciphertext: encodeBase64(ciphertext), nonce: encodeBase64(nonce) };
}

async function unwrapManagedEnvelope(envelope: ManagedEnvelope, logbookId: string): Promise<Uint8Array> {
  if (envelope.wrapping_algorithm !== "AES-256-GCM") {
    throw new RecoveryError(503, "RECOVERY_ENVELOPE_UNAVAILABLE", "Account recovery is temporarily unavailable.");
  }

  const ciphertext = decodeBase64(envelope.ciphertext, 48, 512);
  const nonce = decodeBase64(envelope.nonce, 12, 12);
  try {
    const plaintext = new Uint8Array(await crypto.subtle.decrypt(
      {
        name: "AES-GCM",
        iv: ownedBuffer(nonce),
        additionalData: ownedBuffer(managedAdditionalData(logbookId, envelope.key_version_id)),
        tagLength: 128,
      },
      await importManagedKey(),
      ownedBuffer(ciphertext),
    ));
    if (plaintext.length !== 32) {
      plaintext.fill(0);
      throw new RecoveryError(503, "RECOVERY_ENVELOPE_UNAVAILABLE", "Account recovery is temporarily unavailable.");
    }

    return plaintext;
  } catch (error) {
    if (error instanceof RecoveryError) {
      throw error;
    }
    throw new RecoveryError(503, "RECOVERY_ENVELOPE_UNAVAILABLE", "Account recovery is temporarily unavailable.");
  } finally {
    ciphertext.fill(0);
    nonce.fill(0);
  }
}

async function bindDeviceRecoveryKey(
  accountId: string,
  logbookId: string,
  deviceId: string,
  publicKey: string,
  fingerprint: string,
  algorithm: string,
): Promise<void> {
  if (algorithm !== "RSA-OAEP-256") {
    throw new RecoveryError(400, "RECOVERY_DEVICE_KEY_INVALID", "This device could not prepare account recovery.");
  }
  await verifyDevicePublicKey(publicKey, fingerprint);
  await importDevicePublicKey(publicKey);
  await serviceRpc("elb_bind_device_recovery_key", {
    p_actor_account_id: accountId,
    p_logbook_id: logbookId,
    p_device_id: deviceId,
    p_public_key: publicKey,
    p_fingerprint: fingerprint,
    p_algorithm: algorithm,
  });
}

async function enroll(accountId: string, body: RequestBody): Promise<Response> {
  const logbookId = requiredUuid(body.logbookId, "Logbook");
  const deviceId = requiredUuid(body.deviceId, "Device");
  const publicKey = requiredString(body.devicePublicKey, "Device recovery key");
  const fingerprint = requiredString(body.devicePublicKeyFingerprint, "Device recovery fingerprint", 64);
  const algorithm = requiredString(body.devicePublicKeyAlgorithm, "Device recovery algorithm", 64);
  const configuration = await serviceConfiguration();
  if (requiredString(body.ingressKeyVersionId, "Ingress key version", 128) !== configuration.keyVersionId) {
    throw new RecoveryError(409, "RECOVERY_CONFIGURATION_CHANGED", "Account recovery configuration changed. Try again.");
  }

  await bindDeviceRecoveryKey(accountId, logbookId, deviceId, publicKey, fingerprint, algorithm);
  const packageKey = await decryptIngressEnvelope(requiredString(body.wrappedPackageKey, "Wrapped package key"));
  try {
    const envelope = await wrapManagedEnvelope(packageKey, logbookId, configuration.keyVersionId);
    await serviceRpc("elb_upsert_managed_recovery_envelope", {
      p_actor_account_id: accountId,
      p_logbook_id: logbookId,
      p_device_id: deviceId,
      p_wrapping_algorithm: "AES-256-GCM",
      p_key_version_id: configuration.keyVersionId,
      p_ciphertext: envelope.ciphertext,
      p_nonce: envelope.nonce,
    });
    return jsonResponse(200, { enrolled: true, keyVersionId: configuration.keyVersionId });
  } finally {
    packageKey.fill(0);
  }
}

async function restore(accountId: string, body: RequestBody): Promise<Response> {
  const logbookId = requiredUuid(body.logbookId, "Logbook");
  const deviceId = requiredUuid(body.deviceId, "Device");
  const platformLabel = requiredString(body.platformLabel, "Platform label", 128);
  const deviceType = requiredDeviceType(body.deviceType);
  const publicKey = requiredString(body.devicePublicKey, "Device recovery key");
  const fingerprint = requiredString(body.devicePublicKeyFingerprint, "Device recovery fingerprint", 64);
  const algorithm = requiredString(body.devicePublicKeyAlgorithm, "Device recovery algorithm", 64);
  if (algorithm !== "RSA-OAEP-256") {
    throw new RecoveryError(400, "RECOVERY_DEVICE_KEY_INVALID", "This device could not prepare account recovery.");
  }
  await verifyDevicePublicKey(publicKey, fingerprint);
  await importDevicePublicKey(publicKey);
  await serviceRpc("elb_register_pending_recovery_device", {
    p_actor_account_id: accountId,
    p_logbook_id: logbookId,
    p_device_id: deviceId,
    p_device_type: deviceType,
    p_platform_label: platformLabel,
  });
  await bindDeviceRecoveryKey(accountId, logbookId, deviceId, publicKey, fingerprint, algorithm);

  const envelope = await serviceRpc<ManagedEnvelope>("elb_read_managed_recovery_envelope", {
    p_actor_account_id: accountId,
    p_logbook_id: logbookId,
    p_device_id: deviceId,
  });
  const packageKey = await unwrapManagedEnvelope(envelope, logbookId);
  try {
    const wrappedKey = encodeBase64(await crypto.subtle.encrypt(
      { name: "RSA-OAEP" },
      await importDevicePublicKey(publicKey),
      ownedBuffer(packageKey),
    ));
    await serviceRpc("elb_upsert_device_recovery_envelope", {
      p_actor_account_id: accountId,
      p_logbook_id: logbookId,
      p_device_id: deviceId,
      p_key_version_id: envelope.key_version_id,
      p_ciphertext: wrappedKey,
    });
    return jsonResponse(200, {
      wrappedKey,
      algorithm: "RSA-OAEP-256",
      keyVersionId: envelope.key_version_id,
    });
  } finally {
    packageKey.fill(0);
  }
}

async function enrollRecoveryCode(accountId: string, body: RequestBody): Promise<Response> {
  const logbookId = requiredUuid(body.logbookId, "Logbook");
  const deviceId = requiredUuid(body.deviceId, "Device");
  const algorithm = requiredString(body.recoveryAlgorithm, "Recovery-code algorithm", 64);
  const keyVersionId = requiredString(body.recoveryKeyVersionId, "Recovery-code key version", 64);
  if (algorithm !== "PBKDF2-SHA256-600000+A256GCM" || keyVersionId !== "recovery-code-v1") {
    throw new RecoveryError(400, "RECOVERY_CODE_ENVELOPE_INVALID", "The recovery-code envelope is invalid.");
  }
  await serviceRpc("elb_upsert_recovery_code_envelope", {
    p_actor_account_id: accountId,
    p_logbook_id: logbookId,
    p_device_id: deviceId,
    p_wrapping_algorithm: algorithm,
    p_key_version_id: keyVersionId,
    p_ciphertext: requiredString(body.recoveryCiphertext, "Recovery-code ciphertext", 512),
    p_nonce: requiredString(body.recoveryNonce, "Recovery-code nonce", 64),
    p_salt: requiredString(body.recoverySalt, "Recovery-code salt", 64),
  });
  return jsonResponse(200, { enrolled: true });
}

async function recoverySetupStatus(accountId: string, body: RequestBody): Promise<Response> {
  const status = await serviceRpc<{ managed_envelope_configured?: unknown; recovery_code_configured?: unknown }>(
    "elb_get_recovery_setup_status",
    {
      p_actor_account_id: accountId,
      p_logbook_id: requiredUuid(body.logbookId, "Logbook"),
      p_device_id: requiredUuid(body.deviceId, "Device"),
    },
  );
  return jsonResponse(200, {
    managedEnvelopeConfigured: status.managed_envelope_configured === true,
    recoveryCodeConfigured: status.recovery_code_configured === true,
  });
}

async function restoreWithRecoveryCode(accountId: string, body: RequestBody): Promise<Response> {
  const logbookId = requiredUuid(body.logbookId, "Logbook");
  const deviceId = requiredUuid(body.deviceId, "Device");
  const platformLabel = requiredString(body.platformLabel, "Platform label", 128);
  const deviceType = requiredDeviceType(body.deviceType);
  const publicKey = requiredString(body.devicePublicKey, "Device recovery key");
  const fingerprint = requiredString(body.devicePublicKeyFingerprint, "Device recovery fingerprint", 64);
  const algorithm = requiredString(body.devicePublicKeyAlgorithm, "Device recovery algorithm", 64);
  if (algorithm !== "RSA-OAEP-256") {
    throw new RecoveryError(400, "RECOVERY_DEVICE_KEY_INVALID", "This device could not prepare account recovery.");
  }
  await verifyDevicePublicKey(publicKey, fingerprint);
  await importDevicePublicKey(publicKey);
  await serviceRpc("elb_register_pending_recovery_device", {
    p_actor_account_id: accountId,
    p_logbook_id: logbookId,
    p_device_id: deviceId,
    p_device_type: deviceType,
    p_platform_label: platformLabel,
  });
  await bindDeviceRecoveryKey(accountId, logbookId, deviceId, publicKey, fingerprint, algorithm);
  const envelope = await serviceRpc<RecoveryCodeEnvelope>("elb_read_recovery_code_envelope", {
    p_actor_account_id: accountId,
    p_logbook_id: logbookId,
    p_device_id: deviceId,
  });
  return jsonResponse(200, {
    ciphertext: envelope.ciphertext,
    nonce: envelope.nonce,
    salt: envelope.recovery_salt,
    algorithm: envelope.wrapping_algorithm,
    keyVersionId: envelope.key_version_id,
  });
}

async function activate(accountId: string, body: RequestBody): Promise<Response> {
  const logbookId = requiredUuid(body.logbookId, "Logbook");
  const deviceId = requiredUuid(body.deviceId, "Device");
  const device = await serviceRpc<{ status?: unknown }>("elb_activate_recovered_device", {
    p_actor_account_id: accountId,
    p_logbook_id: logbookId,
    p_device_id: deviceId,
  });
  if (device.status !== "active") {
    throw new RecoveryError(409, "RECOVERY_ACTIVATION_INCOMPLETE", "Account recovery is not complete.");
  }
  return jsonResponse(200, { activated: true });
}

Deno.serve(async (request) => {
  if (request.method === "OPTIONS") {
    return new Response(null, { status: 204, headers: corsHeaders });
  }
  if (request.method !== "POST") {
    return jsonResponse(405, { code: "RECOVERY_METHOD_NOT_ALLOWED", message: "Use POST for account recovery." });
  }

  try {
    const contentLength = Number(request.headers.get("content-length") ?? "0");
    if (Number.isFinite(contentLength) && contentLength > 24_000) {
      throw new RecoveryError(413, "RECOVERY_REQUEST_TOO_LARGE", "The recovery request is too large.");
    }

    const accountId = await authenticate(request);
    const body = await request.json() as RequestBody;
    const action = requiredString(body.action, "Recovery action", 32);
    if (action === "configuration") {
      return jsonResponse(200, await serviceConfiguration());
    }
    if (action === "enroll") {
      return await enroll(accountId, body);
    }
    if (action === "status") {
      return await recoverySetupStatus(accountId, body);
    }
    if (action === "restore") {
      return await restore(accountId, body);
    }
    if (action === "enroll-code") {
      return await enrollRecoveryCode(accountId, body);
    }
    if (action === "restore-code") {
      return await restoreWithRecoveryCode(accountId, body);
    }
    if (action === "activate") {
      return await activate(accountId, body);
    }
    throw new RecoveryError(400, "RECOVERY_ACTION_INVALID", "The recovery action is invalid.");
  } catch (error) {
    if (error instanceof RecoveryError) {
      return jsonResponse(error.status, { code: error.code, message: error.message });
    }
    return jsonResponse(500, {
      code: "RECOVERY_SERVICE_FAILED",
      message: "Account recovery could not be completed.",
    });
  }
});
