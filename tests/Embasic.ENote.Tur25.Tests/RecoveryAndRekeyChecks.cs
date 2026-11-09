using System.Security.Cryptography;
using Embasic.ENote.Emd;
using Embasic.ENote.Storage;

internal static partial class Program
{
    private static void RecoveryChecks(Fixture f)
    {
        Check("root authorization enrolls and revokes durably using exact revisions", () =>
        {
            string folder = f.Folder();
            var data = f.Data(f.Folder());
            using (var provider = f.Open(folder, provision: true))
            {
                Equal(new VaultRootRecoveryAuthorization(0, null), provider.ReadAuthorization(f.Workspace));
                Equal(1L, provider.CompareAndSetAuthorization(f.Initial.Stamp, 0, f.Backup.Proof).Revision);
                var recovery = new VaultRootRecovery(new VaultGenerationRecovery(provider, data), provider);
                using var restored = recovery.Restore(f.Workspace, f.Backup.Envelope, f.Index, BackupPassword);
                Equal(f.Root.Id, restored.Id);
                Throws<CryptographicException>(() =>
                    provider.CompareAndSetAuthorization(f.Initial.Stamp, 0, null));
                Equal(2L, provider.CompareAndSetAuthorization(f.Initial.Stamp, 1, null).Revision);
            }
            using var restarted = f.Open(folder);
            Equal(new VaultRootRecoveryAuthorization(2, null), restarted.ReadAuthorization(f.Workspace));
            var blocked = new VaultRootRecovery(new VaultGenerationRecovery(restarted, data), restarted);
            Throws<CryptographicException>(() => blocked.Restore(f.Workspace, f.Backup.Envelope, f.Index, BackupPassword));
            Throws<CryptographicException>(() => restarted.CompareAndSetAuthorization(f.Initial.Stamp, 0, f.Backup.Proof));
        });
        Check("revoking and restoring the identical proof still invalidates a running restore", () =>
        {
            using var provider = f.Open(f.Folder(), provision: true);
            provider.CompareAndSetAuthorization(f.Initial.Stamp, 0, f.Backup.Proof);
            var lifecycle = new ChangeOnSecondRead(provider, f);
            var recovery = new VaultRootRecovery(new VaultGenerationRecovery(provider, f.Data(f.Folder())), lifecycle);
            Throws<CryptographicException>(() => recovery.Restore(f.Workspace, f.Backup.Envelope, f.Index, BackupPassword));
            Equal(new VaultRootRecoveryAuthorization(3, f.Backup.Proof), provider.ReadAuthorization(f.Workspace));
        });
        Check("pending generation blocks authorization writes and commit clears the old proof", () =>
        {
            using var provider = f.Open(f.Folder(), provision: true);
            provider.CompareAndSetAuthorization(f.Initial.Stamp, 0, f.Backup.Proof);
            provider.ReserveTransition(f.Intent);
            Throws<CryptographicException>(() => provider.CompareAndSetAuthorization(f.Initial.Stamp, 1, null));
            provider.CommitTransition(f.Intent);
            Equal(new VaultRootRecoveryAuthorization(2, null), provider.ReadAuthorization(f.Workspace));
            Throws<ArgumentException>(() => provider.CompareAndSetAuthorization(f.Next.Stamp, 2, f.Backup.Proof));
            Throws<CryptographicException>(() => provider.CompareAndSetAuthorization(f.Initial.Stamp, 2, f.Backup.Proof));
        });
        Check("only one root authorization update can use the same revision", () =>
        {
            string folder = f.Folder();
            using var first = f.Open(folder, provision: true);
            using var second = f.Open(folder);
            var outcomes = Task.WhenAll(TryEnroll(first), TryEnroll(second)).GetAwaiter().GetResult();
            Equal(1, outcomes.Count(x => x));
            Equal(1L, first.ReadAuthorization(f.Workspace).Revision);

            Task<bool> TryEnroll(VaultTransitionFilePrototype provider) => Task.Run(() =>
            {
                try { provider.CompareAndSetAuthorization(f.Initial.Stamp, 0, f.Backup.Proof); return true; }
                catch (CryptographicException) { return false; }
            });
        });
        Check("a malformed proof cannot consume an authorization revision", () =>
        {
            using var provider = f.Open(f.Folder(), provision: true);
            var invalid = f.Backup.Proof with { BackupSha256 = "wrong" };
            Throws<ArgumentException>(() => provider.CompareAndSetAuthorization(f.Initial.Stamp, 0, invalid));
            Equal(new VaultRootRecoveryAuthorization(0, null), provider.ReadAuthorization(f.Workspace));
        });
    }

    private sealed class ChangeOnSecondRead(VaultTransitionFilePrototype provider, Fixture f) : IVaultRootRecoveryLifecycle
    {
        private int _reads;
        public VaultRootRecoveryAuthorization ReadAuthorization(Guid workspaceId)
        {
            if (++_reads == 2)
            {
                provider.CompareAndSetAuthorization(f.Initial.Stamp, 1, null);
                provider.CompareAndSetAuthorization(f.Initial.Stamp, 2, f.Backup.Proof);
            }
            return provider.ReadAuthorization(workspaceId);
        }

        public VaultRootRecoveryAuthorization CompareAndSetAuthorization(VaultGenerationStamp expected,
            long expectedRevision, VaultRootRecoveryProof? replacement) =>
            provider.CompareAndSetAuthorization(expected, expectedRevision, replacement);
    }

    private static void RekeyChecks(Fixture f)
    {
        using var portable = PortableEmd.Encrypt(Markdown, Password);
        byte[] locked = LayeredEmd3.Seal(portable, [f.Root, f.Child, f.Note]);
        byte[] inherited = LayeredEmd3.Seal(portable, [f.Root, f.Child]);
        byte[] direct = LayeredEmd3.Seal(portable, [f.Root]);
        VaultRekeyDocument[] catalog =
        [
            new("Vault/Sub/Locked.emd", Digest(locked)),
            new("Vault/Sub/Inherited.emd", Digest(inherited)),
            new("Vault/Direct.emd", Digest(direct))
        ];
        var plan = VaultRekeyInventory.Create(f.Initial, f.Index, f.Root, [f.Root.Id], catalog);
        Check("root rekey includes exact ELH1, ELV1 and explicit and inherited documents", () =>
        {
            Equal(f.Initial.Stamp, plan.Generation);
            Equal(Digest(f.Index), plan.IndexSha256);
            Equal(5, plan.Artifacts.Count);
            Equal(f.Initial.Find(f.Root.Id, LayerKind.Folder)!.Pin.CiphertextSha256,
                plan.Artifacts.Single(x => x.Kind == VaultRekeyArtifactKind.FactorVault).SourceSha256);
            var nested = plan.Artifacts.Single(x => x.RelativePath == catalog[0].RelativePath);
            Equal(true, nested.RequiredFactors.SequenceEqual(new[] { f.Root.Id, f.Child.Id, f.Note.Id }));
            Equal(true, plan.Artifacts.Single(x => x.RelativePath == catalog[1].RelativePath).RequiredFactors
                .SequenceEqual(new[] { f.Root.Id, f.Child.Id }));
        });
        Check("subfolder rekey excludes unaffected siblings and preserves case-insensitive lock resolution", () =>
        {
            var lowerCase = catalog.Select(x => x with { RelativePath = x.RelativePath.ToLowerInvariant() });
            var subset = VaultRekeyInventory.Create(f.Initial, f.Index, f.Root, [f.Child.Id], lowerCase);
            Equal(3, subset.Artifacts.Count);
            Equal(false, subset.Artifacts.Any(x => x.RelativePath == "vault/direct.emd"));
            Equal(false, subset.Artifacts.Any(x => x.Kind == VaultRekeyArtifactKind.RootIndex));
        });
        Check("missing explicitly locked note and missing enrolled dependencies fail closed", () =>
        {
            Throws<InvalidDataException>(() => VaultRekeyInventory.Create(f.Initial, f.Index, f.Root,
                [f.Root.Id], catalog.Skip(1)));
            var incomplete = VaultGenerationManifest.Create(f.Workspace, 1,
                [f.Initial.Find(f.Root.Id, LayerKind.Folder)!]);
            Throws<CryptographicException>(() => VaultRekeyInventory.Create(incomplete, f.Index, f.Root, [f.Root.Id], catalog));
        });
        Check("duplicate aliases, traversing paths and skipped locked ancestors are refused", () =>
        {
            Throws<InvalidDataException>(() => VaultRekeyInventory.Create(f.Initial, f.Index, f.Root,
                [f.Root.Id], [.. catalog, catalog[0] with { RelativePath = "VAULT/Sub/LOCKED.emd" }]));
            Throws<InvalidDataException>(() => VaultRekeyInventory.Create(f.Initial, f.Index, f.Root,
                [f.Root.Id], [.. catalog, new("Vault/Sub/../Escape.emd", Digest(direct))]));
            var malformed = f.Entries;
            malformed[2] = malformed[2] with { ParentId = f.Root.Id };
            byte[] inconsistent = WorkspaceLockIndex.Protect(f.Workspace, f.Root, malformed);
            Throws<InvalidDataException>(() => VaultRekeyInventory.Create(f.Initial, inconsistent, f.Root,
                [f.Child.Id], catalog));
        });

        using var fresh = LayerSecret.Regenerate(f.Root);
        var sources = new Dictionary<string, byte[]>
        {
            ["index"] = f.Index, ["vault:" + f.Root.Id.ToString("N")] = f.RootVault,
            ["note:" + catalog[0].RelativePath] = locked, ["note:" + catalog[1].RelativePath] = inherited,
            ["note:" + catalog[2].RelativePath] = direct
        };
        var candidates = new Dictionary<string, byte[]>
        {
            ["index"] = WorkspaceLockIndex.RekeyRoot(f.Index, f.Workspace, f.Root, fresh),
            ["vault:" + f.Root.Id.ToString("N")] = LayerKeyVault.Create(fresh, ReplacementPassword, BackupPassword),
            ["note:" + catalog[0].RelativePath] = Rekey(locked, [f.Root, f.Child, f.Note], [fresh, f.Child, f.Note]),
            ["note:" + catalog[1].RelativePath] = Rekey(inherited, [f.Root, f.Child], [fresh, f.Child]),
            ["note:" + catalog[2].RelativePath] = Rekey(direct, [f.Root], [fresh])
        };
        var digests = candidates.ToDictionary(x => x.Key, x => Digest(x.Value));
        Check("missing source or wrong source digest leaves staging untouched", () =>
        {
            string folder = f.Folder();
            var stage = new VaultRekeyStaging(folder);
            Throws<InvalidDataException>(() => stage.Prepare(plan, digests, _ => null, Candidate));
            byte[] changed = (byte[])f.Index.Clone();
            changed[^1] ^= 1;
            Throws<CryptographicException>(() => stage.Prepare(plan, digests,
                item => item.Kind == VaultRekeyArtifactKind.RootIndex ? new MemoryStream(changed) : Source(item), Candidate));
            Equal(0, Directory.GetFiles(folder).Length);
        });
        Check("missing and extra target commitments reject before staging", () =>
        {
            var stage = new VaultRekeyStaging(f.Folder());
            var bad = new Dictionary<string, string>(digests);
            bad.Remove("index");
            Throws<InvalidDataException>(() => stage.Prepare(plan, bad, Source, Candidate));
            bad.Add("surprise", digests["index"]);
            Throws<InvalidDataException>(() => stage.Prepare(plan, bad, Source, Candidate));
        });
        Check("partial preparation resumes from exact immutable candidates without reopening completed work", () =>
        {
            string folder = f.Folder();
            var stage = new VaultRekeyStaging(folder);
            var missing = plan.Artifacts.Last().Id;
            Throws<InvalidDataException>(() => stage.Prepare(plan, digests, Source,
                item => item.Id == missing ? null : Candidate(item)));
            Equal(4, Directory.GetFiles(folder, "*.rekey").Length);
            Throws<InvalidDataException>(() => stage.RequirePrepared(plan, digests, Source));
            var reopened = new VaultRekeyStaging(folder);
            int newReads = 0;
            reopened.Prepare(plan, digests, Source, item =>
            {
                Equal(missing, item.Id); newReads++; return Candidate(item);
            });
            Equal(1, newReads);
            reopened.Prepare(plan, digests, Source, _ => throw new InvalidOperationException("Must reuse staged bytes."));
            reopened.RequirePrepared(plan, digests, Source);
            Equal(Markdown, LayeredEmd3.Open(candidates["note:" + catalog[0].RelativePath],
                [fresh, f.Child, f.Note], ReplacementPassword));
            Equal(Markdown, LayeredEmd3.Open(locked, [f.Root, f.Child, f.Note], Password));
        });
        Check("corrupt occupied target fails closed and is never overwritten on retry", () =>
        {
            string folder = f.Folder();
            var stage = new VaultRekeyStaging(folder);
            stage.Prepare(plan, digests, Source, Candidate);
            string path = Path.Combine(folder, digests["index"] + ".rekey");
            byte[] corrupt = "ELH1broken"u8.ToArray();
            File.WriteAllBytes(path, corrupt);
            Throws<CryptographicException>(() => stage.Prepare(plan, digests, Source, Candidate));
            Equal(true, File.ReadAllBytes(path).SequenceEqual(corrupt));
        });
        Check("wrong new artifact never occupies its committed digest filename", () =>
        {
            string folder = f.Folder();
            var stage = new VaultRekeyStaging(folder);
            Throws<CryptographicException>(() => stage.Prepare(plan, digests, Source,
                _ => new MemoryStream("ELH1wrong"u8.ToArray())));
            Equal(0, Directory.GetFiles(folder).Length);
        });

        Check("a supplied matching digest cannot stage an artifact of the wrong format", () =>
        {
            string folder = f.Folder();
            var stage = new VaultRekeyStaging(folder);
            var altered = new Dictionary<string, string>(digests) { ["index"] = Digest(inherited) };
            Throws<InvalidDataException>(() => stage.Prepare(plan, altered, Source, _ => new MemoryStream(inherited)));
            Equal(0, Directory.GetFiles(folder).Length);
        });

        Stream Source(VaultRekeyArtifact item) => new MemoryStream(sources[item.Id], writable: false);
        Stream Candidate(VaultRekeyArtifact item) => new MemoryStream(candidates[item.Id], writable: false);
    }

    private static byte[] Rekey(byte[] ciphertext, LayerSecret[] old, LayerSecret[] next)
    {
        var result = LayeredEmd3.Rekey(ciphertext, old, next, ReplacementPassword,
            out byte[] secret, currentPassword: Password);
        CryptographicOperations.ZeroMemory(secret);
        return result;
    }
}
