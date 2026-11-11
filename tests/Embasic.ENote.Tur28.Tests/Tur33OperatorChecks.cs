using System.Globalization;
using System.Security.Cryptography;
using Embasic.ENote.Storage;

internal static partial class Program
{
    private static void Tur33OperatorChecks()
    {
        Check("operator rotation is dual signed and an external revision floor rejects joint disk rollback", () =>
        {
            using var backup = new TempBackup();
            using var old = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var fresh = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            using var selector = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            Guid workspace = Guid.NewGuid();
            var first = new VaultRecoveryPolicyPin(workspace, 1, new string('A', 64));
            var second = first with { Revision = 2, Sha256 = new string('B', 64) };
            string floor = Path.Combine(backup.Path, "minimum.epf");
            var log = new VaultRecoveryPinJournal(backup.Path, workspace, selector.ExportSubjectPublicKeyInfo());
            var rotation = VaultRecoveryPinFloor.AuthorizeRotation(workspace, 2, old, fresh);
            byte[] initial = old.ExportSubjectPublicKeyInfo();
            log.Append(null, first, VaultRecoveryPinJournal.Sign(null, first, selector));
            log.Append(first, second, VaultRecoveryPinJournal.Sign(first, second, selector));
            File.WriteAllBytes(floor, VaultRecoveryPinFloor.Issue(second, old));
            Throws<CryptographicException>(() => VaultRecoveryPinFloor.ReadRequired(floor, workspace, initial, 2, rotation));
            File.WriteAllBytes(floor, VaultRecoveryPinFloor.Issue(second, fresh));
            var wrong = VaultRecoveryPinJournal.AuthorizeRotation(workspace, 2, old, fresh);
            var wrongDomain = new VaultRecoveryOperatorKeyTransition(wrong.FirstRevision, wrong.NewPublicKey,
                wrong.OutgoingSignature, wrong.IncomingSignature);
            Throws<CryptographicException>(() => VaultRecoveryPinFloor.ReadRequired(floor, workspace, initial, 2, wrongDomain));
            Equal(second, log.ReadRequired(VaultRecoveryPinFloor.ReadRequired(floor, workspace, initial, 2, rotation)));
            File.Delete(Directory.EnumerateFiles(backup.Path, "*.eps").Order(StringComparer.Ordinal).Last());
            File.WriteAllBytes(floor, VaultRecoveryPinFloor.Issue(first, old));
            Throws<CryptographicException>(() => VaultRecoveryPinFloor.ReadRequired(floor, workspace, initial, 2, rotation));
            Equal(first, log.ReadRequired(VaultRecoveryPinFloor.ReadRequired(floor, workspace, initial, 1, rotation)));
        });

        Check("fresh challenge verifies a separate operator process and rejects a replayed response", () =>
        {
            using var backup = new TempBackup();
            using var operatorKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var floor = new VaultRecoveryPolicyPin(Guid.NewGuid(), 3, new string('C', 64));
            byte[] nonce = RandomNumberGenerator.GetBytes(32);
            string keyPath = Path.Combine(backup.Path, "operator-test.key");
            string reply = Path.Combine(backup.Path, "signature");
            File.WriteAllBytes(keyPath, operatorKey.ExportPkcs8PrivateKey());
            using var process = StartHarness("tur33-floor-worker", keyPath, reply, floor.Workspace.ToString("D"),
                floor.Revision.ToString(CultureInfo.InvariantCulture), floor.Sha256, Convert.ToHexString(nonce));
            if (!process.WaitForExit(10000) || process.ExitCode != 0)
                throw new IOException("Separate operator test process did not return an attestation.");
            byte[] signature = File.ReadAllBytes(reply), pub = operatorKey.ExportSubjectPublicKeyInfo();
            VaultRecoveryPinFloor.VerifyAttestation(floor, nonce, signature, pub);
            nonce[0] ^= 1;
            Throws<CryptographicException>(() => VaultRecoveryPinFloor.VerifyAttestation(floor, nonce, signature, pub));
        });
    }

    private static int Tur33FloorWorker(string[] args)
    {
        using var key = ECDsa.Create();
        byte[] input = File.ReadAllBytes(args[1]);
        try
        {
            key.ImportPkcs8PrivateKey(input, out int consumed);
            Equal(input.Length, consumed);
            var floor = new VaultRecoveryPolicyPin(Guid.Parse(args[3]), long.Parse(args[4], CultureInfo.InvariantCulture), args[5]);
            File.WriteAllBytes(args[2], VaultRecoveryPinFloor.Attest(floor, Convert.FromHexString(args[6]), key));
            return 0;
        }
        finally { CryptographicOperations.ZeroMemory(input); }
    }
}
