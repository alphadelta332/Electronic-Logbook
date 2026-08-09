using System.Security.Cryptography;
using System.Text;

if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
{
    Console.Error.WriteLine("Usage: RecoveryEnvelopeSecretGenerator <output-env-file>");
    return 2;
}

var outputPath = Path.GetFullPath(args[0]);
var outputDirectory = Path.GetDirectoryName(outputPath)
    ?? throw new InvalidOperationException("The output path has no parent directory.");
Directory.CreateDirectory(outputDirectory);

using var rsa = RSA.Create(3072);
var ingressPublicKey = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());
var ingressPrivateKey = Convert.ToBase64String(rsa.ExportPkcs8PrivateKey());
var recoveryKek = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

var contents = string.Join('\n',
    $"RECOVERY_INGRESS_PUBLIC_KEY_SPKI_BASE64={ingressPublicKey}",
    $"RECOVERY_INGRESS_PRIVATE_KEY_PKCS8_BASE64={ingressPrivateKey}",
    $"RECOVERY_KEK_BASE64={recoveryKek}",
    "RECOVERY_KEY_VERSION_ID=recovery-key-v1",
    string.Empty);

await using var stream = new FileStream(
    outputPath,
    FileMode.CreateNew,
    FileAccess.Write,
    FileShare.None,
    bufferSize: 4096,
    FileOptions.WriteThrough);
await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
await writer.WriteAsync(contents);
await writer.FlushAsync();

Console.WriteLine("Recovery envelope secret file created.");
return 0;
