// Evidence collector only. Does not authorize product, packages or trust-store changes.
using System;
using System.Collections.Generic;
using System.Formats.Asn1;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;

public static class ContextualTrust
{
    static string directory;
    static DateTimeOffset review;
    static readonly Dictionary<string, object> chains = new Dictionary<string, object>();
    static readonly Dictionary<string, X509Certificate2[]> pairs = new Dictionary<string, X509Certificate2[]>();
    const string NugetPin = "1f4b311d9acc115c8dc8018b5a49e00fce6da8e2855f9f014ca6f34570bc482d";
    const string RootPin = "552f7bdcf1a7af9e6ce672017f4f12abf77240c78e761ac203d1d9d20ac89988";
    static string Hash(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();
    static Dictionary<string, object> D(params object[] a)
    {
        var d = new Dictionary<string, object>();
        for (int i = 0; i < a.Length; i += 2) d[(string)a[i]] = a[i + 1];
        return d;
    }
    public static void Start(string output, DateTimeOffset at)
    {
        directory = output; review = at; chains.Clear(); pairs.Clear();
        Directory.CreateDirectory(Path.Combine(output, "cms"));
        Directory.CreateDirectory(Path.Combine(output, "crls"));
        Directory.CreateDirectory(Path.Combine(output, "certificates"));
    }
    static object Certificate(X509Certificate2 c)
    {
        if (c == null) return null;
        string h = Hash(c.RawData), path = "certificates/" + h + ".cer";
        File.WriteAllBytes(Path.Combine(directory, path), c.RawData);
        return D("sha256", h, "subject", c.Subject, "issuer", c.Issuer,
            "serial", c.SerialNumber, "path", path,
            "not_before", c.NotBefore.ToUniversalTime().ToString("o"),
            "not_after", c.NotAfter.ToUniversalTime().ToString("o"),
            "nuget_published_signer_fingerprint_match", h == NugetPin);
    }
    static object Chain(X509Certificate2 c, X509Certificate2Collection extra, DateTimeOffset at, string mode, string eku)
    {
        if (c == null) return null;
        string key = Hash(c.RawData) + at.ToString("o") + mode + eku +
            string.Join("", extra.Cast<X509Certificate2>().Select(x => Hash(x.RawData)).OrderBy(x => x));
        if (chains.TryGetValue(key, out object existing)) return existing;
        using (var chain = new X509Chain())
        {
            chain.ChainPolicy.ExtraStore.AddRange(extra);
            chain.ChainPolicy.VerificationTime = at.UtcDateTime;
            chain.ChainPolicy.RevocationMode = (X509RevocationMode)Enum.Parse(typeof(X509RevocationMode), mode);
            chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
            chain.ChainPolicy.DisableCertificateDownloads = mode != "Online";
            chain.ChainPolicy.UrlRetrievalTimeout = TimeSpan.FromSeconds(5);
            chain.ChainPolicy.ApplicationPolicy.Add(new Oid(eku));
            // System trust, no ignored errors, no custom roots or trust-store writes.
            bool built = chain.Build(c);
            var elements = chain.ChainElements.Cast<X509ChainElement>().Select(e =>
                D("certificate", Certificate(e.Certificate), "status", e.ChainElementStatus.Select(s => s.Status.ToString()).ToArray())).ToArray();
            for (int i = 0; i + 1 < chain.ChainElements.Count; i++)
            {
                var child = chain.ChainElements[i].Certificate; var issuer = chain.ChainElements[i + 1].Certificate;
                pairs[Hash(child.RawData) + Hash(issuer.RawData)] = new[] { child, issuer };
            }
            X509Certificate2 root = chain.ChainElements.Count == 0 ? null : chain.ChainElements[chain.ChainElements.Count - 1].Certificate;
            var stores = new List<string>();
            if (root != null && OperatingSystem.IsWindows())
                foreach (StoreLocation loc in new[] { StoreLocation.LocalMachine, StoreLocation.CurrentUser })
                {
                    using (var store = new X509Store(StoreName.Root, loc))
                    {
                        store.Open(OpenFlags.ReadOnly);
                        if (store.Certificates.Cast<X509Certificate2>().Any(x => Hash(x.RawData) == Hash(root.RawData))) stores.Add(loc + "/Root");
                    }
                }
            var result = D("build", built, "verification_time_utc", at.ToString("o"), "revocation_mode", mode,
                "downloads", mode == "Online", "extra_store_count", extra.Count, "application_eku", eku,
                "status", chain.ChainStatus.Select(s => D("status", s.Status.ToString(), "information", s.StatusInformation.Trim())).ToArray(),
                "elements", elements, "root_stores", stores, "digicert_published_root_match", root != null && Hash(root.RawData) == RootPin,
                "scope", mode == "Online" ? "OS online request may use cache; not retained fresh CRL/OCSP proof" : "Contextual chain observation; no fresh revocation approval");
            chains[key] = result; return result;
        }
    }
    static List<byte[]> PeCms(string path)
    {
        var result = new List<byte[]>();
        using (var stream = File.OpenRead(path)) using (var r = new BinaryReader(stream))
        {
            if (stream.Length < 64 || r.ReadUInt16() != 0x5a4d) return result;
            stream.Position = 0x3c; int pe = r.ReadInt32();
            if (pe < 0 || pe + 26L > stream.Length) throw new InvalidDataException("Invalid PE header");
            stream.Position = pe; if (r.ReadUInt32() != 0x4550) throw new InvalidDataException("Invalid PE signature");
            stream.Position = pe + 24; int magic = r.ReadUInt16();
            int start = magic == 0x20b ? 112 : magic == 0x10b ? 96 : throw new InvalidDataException("Unknown PE optional header");
            stream.Position = pe + 24 + start + 32; long offset = r.ReadUInt32(), size = r.ReadUInt32();
            if (offset == 0 || size == 0) return result;
            if (size > 32 * 1024 * 1024 || offset + size > stream.Length) throw new InvalidDataException("Invalid PE certificate table");
            for (long p = offset; p + 8 <= offset + size;)
            {
                stream.Position = p; uint length = r.ReadUInt32(); r.ReadUInt16(); ushort type = r.ReadUInt16();
                if (length < 8 || p + length > offset + size) throw new InvalidDataException("Invalid WIN_CERTIFICATE");
                if (type == 2) result.Add(r.ReadBytes(checked((int)length - 8)));
                p += (length + 7L) & ~7L;
            }
        }
        return result;
    }
    public static object Inspect(string path, bool pe)
    {
        var values = new List<object>();
        try
        {
            var blobs = pe ? PeCms(path) : new List<byte[]> { File.ReadAllBytes(path) };
            foreach (var raw in blobs) values.Add(Cms(raw, 0));
            return D("path", path, "status", blobs.Count > 0 ? "CMS_OBSERVED" : "NO_EMBEDDED_CMS", "signatures", values,
                "scope", "CMS mathematical signature is separate from PE/package content validation by OS/NuGet");
        }
        catch (Exception e) { return D("path", path, "status", "INSPECTION_ERROR", "error", e.Message); }
    }
    static object Cms(byte[] raw, int depth)
    {
        if (depth > 4 || raw.Length > 32 * 1024 * 1024) throw new InvalidDataException("CMS evidence bound exceeded");
        var cms = new SignedCms(); cms.Decode(raw);
        string path = "cms/" + Hash(raw) + ".p7s"; File.WriteAllBytes(Path.Combine(directory, path), raw);
        var extra = new X509Certificate2Collection(); extra.AddRange(cms.Certificates);
        var signers = new List<object>();
        foreach (SignerInfo signer in cms.SignerInfos)
        {
            bool math = false; string error = null;
            try { signer.CheckSignature(extra, true); math = true; } catch (CryptographicException e) { error = e.Message; }
            var timestamps = new List<object>(); var nested = new List<object>();
            foreach (CryptographicAttributeObject attr in signer.UnsignedAttributes)
                foreach (AsnEncodedData value in attr.Values)
                {
                    string oid = attr.Oid.Value;
                    if (oid == "1.3.6.1.4.1.311.2.4.1") { nested.Add(Cms(value.RawData, depth + 1)); continue; }
                    if (oid != "1.3.6.1.4.1.311.3.3.1" && oid != "1.2.840.113549.1.9.16.2.14") continue;
                    Rfc3161TimestampToken token; int consumed;
                    bool decoded = Rfc3161TimestampToken.TryDecode(value.RawData, out token, out consumed);
                    if (!decoded || consumed != value.RawData.Length) { timestamps.Add(D("oid", oid, "binding", false, "status", "TOKEN_NOT_FULLY_DECODED")); continue; }
                    X509Certificate2 tsa; bool bound = token.VerifySignatureForSignerInfo(signer, out tsa, extra);
                    var candidates = new X509Certificate2Collection(); candidates.AddRange(extra); candidates.AddRange(token.AsSignedCms().Certificates);
                    var at = token.TokenInfo.Timestamp;
                    string tokenPath = "cms/" + Hash(value.RawData) + ".p7s"; File.WriteAllBytes(Path.Combine(directory, tokenPath), value.RawData);
                    timestamps.Add(D("oid", oid, "kind", "RFC3161", "path", tokenPath, "binding", bound,
                        "timestamp_utc", at.ToString("o"), "tsa", Certificate(tsa),
                        "tsa_at_timestamp", bound ? Chain(tsa, candidates, at, "NoCheck", "1.3.6.1.5.5.7.3.8") : null,
                        "signer_at_timestamp", bound && math ? Chain(signer.Certificate, candidates, at, "NoCheck", "1.3.6.1.5.5.7.3.3") : null,
                        "tsa_online_now", bound ? Chain(tsa, candidates, review, "Online", "1.3.6.1.5.5.7.3.8") : null,
                        "certificates", candidates.Cast<X509Certificate2>().Select(Certificate).ToArray(),
                        "scope", "Bound token time; historical chains exclude revocation and do not assert historical non-revocation"));
                }
            foreach (SignerInfo counter in signer.CounterSignerInfos)
                timestamps.Add(D("kind", "LEGACY_COUNTERSIGNATURE", "status", "REVIEW_NOT_USED_FOR_HISTORICAL_CHAIN"));
            signers.Add(D("certificate", Certificate(signer.Certificate), "cms_signature_only_match", math, "error", error,
                "current_no_revocation", Chain(signer.Certificate, extra, review, "NoCheck", "1.3.6.1.5.5.7.3.3"),
                "current_online", Chain(signer.Certificate, extra, review, "Online", "1.3.6.1.5.5.7.3.3"),
                "timestamps", timestamps, "nested", nested));
        }
        return D("path", path, "sha256", Hash(raw), "certificates", cms.Certificates.Cast<X509Certificate2>().Select(Certificate).ToArray(), "signers", signers);
    }
    static DateTimeOffset Time(AsnReader r) => r.PeekTag().HasSameClassAndValue(Asn1Tag.UtcTime) ? r.ReadUtcTime() : r.ReadGeneralizedTime();
    static string Serial(ReadOnlySpan<byte> value) => Convert.ToHexString(value).TrimStart('0').ToLowerInvariant();
    static List<string> CrlUrls(X509Certificate2 cert)
    {
        var urls = new List<string>();
        foreach (X509Extension ext in cert.Extensions)
            if (ext.Oid.Value == "2.5.29.31") ReadUris(new AsnReader(ext.RawData, AsnEncodingRules.DER), urls, 0);
        return urls;
    }
    static void ReadUris(AsnReader r, List<string> urls, int depth)
    {
        if (depth > 8) throw new InvalidDataException("CRL URL nesting limit");
        while (r.HasData)
        {
            var t = r.PeekTag();
            if (t.TagClass == TagClass.ContextSpecific && t.TagValue == 6 && !t.IsConstructed)
                urls.Add(r.ReadCharacterString(UniversalTagNumber.IA5String, t));
            else if (t.IsConstructed) ReadUris(r.ReadSequence(t), urls, depth + 1);
            else r.ReadEncodedValue();
        }
    }
    static object CheckCrl(byte[] raw, X509Certificate2 child, X509Certificate2 issuer)
    {
        var r = new AsnReader(raw, AsnEncodingRules.DER); var list = r.ReadSequence(); r.ThrowIfNotEmpty();
        byte[] tbs = list.ReadEncodedValue().ToArray(); var algorithm = list.ReadSequence(); string oid = algorithm.ReadObjectIdentifier();
        int unused; byte[] sig = list.ReadBitString(out unused); list.ThrowIfNotEmpty();
        bool verified = false;
        var alg = oid == "1.2.840.113549.1.1.11" || oid == "1.2.840.10045.4.3.2" ? HashAlgorithmName.SHA256 :
            oid == "1.2.840.113549.1.1.12" || oid == "1.2.840.10045.4.3.3" ? HashAlgorithmName.SHA384 :
            oid == "1.2.840.113549.1.1.13" ? HashAlgorithmName.SHA512 : default(HashAlgorithmName);
        if (alg != default(HashAlgorithmName) && unused == 0)
        {
            using (RSA rsa = issuer.GetRSAPublicKey())
                if (rsa != null && oid.StartsWith("1.2.840.113549.1.1.")) verified = rsa.VerifyData(tbs, sig, alg, RSASignaturePadding.Pkcs1);
            using (ECDsa ec = issuer.GetECDsaPublicKey())
                if (ec != null && oid.StartsWith("1.2.840.10045.4.3.")) verified = ec.VerifyData(tbs, sig, alg, DSASignatureFormat.Rfc3279DerSequence);
        }
        var body = new AsnReader(tbs, AsnEncodingRules.DER).ReadSequence();
        if (body.PeekTag().HasSameClassAndValue(Asn1Tag.Integer)) body.ReadInteger();
        var innerAlg = body.ReadSequence(); bool algorithmMatch = innerAlg.ReadObjectIdentifier() == oid;
        byte[] name = body.ReadEncodedValue().ToArray();
        bool issuerName = name.SequenceEqual(issuer.SubjectName.RawData) && child.IssuerName.RawData.SequenceEqual(issuer.SubjectName.RawData);
        var from = Time(body); DateTimeOffset? until = null;
        if (body.HasData && (body.PeekTag().HasSameClassAndValue(Asn1Tag.UtcTime) || body.PeekTag().HasSameClassAndValue(Asn1Tag.GeneralizedTime))) until = Time(body);
        bool revoked = false, entryExtensions = false;
        string serial = Serial(child.GetSerialNumber().Reverse().ToArray());
        if (body.HasData && body.PeekTag().HasSameClassAndValue(Asn1Tag.Sequence))
        {
            var entries = body.ReadSequence();
            while (entries.HasData)
            {
                var e = entries.ReadSequence(); var s = Serial(e.ReadIntegerBytes().Span); Time(e);
                if (s == serial) revoked = true;
                if (e.HasData)
                {
                    var exts = e.ReadSequence();
                    while (exts.HasData)
                    {
                        var ext = exts.ReadSequence(); string id = ext.ReadObjectIdentifier();
                        bool critical = ext.PeekTag().HasSameClassAndValue(Asn1Tag.Boolean) && ext.ReadBoolean();
                        // Reason/invalidity date do not change the direct serial-list membership.
                        if (id == "2.5.29.29" || (critical && id != "2.5.29.21" && id != "2.5.29.24")) entryExtensions = true;
                    }
                }
            }
        }
        bool restricted = false;
        if (body.HasData)
        {
            var wrapper = body.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, true)); var extensions = wrapper.ReadSequence();
            while (extensions.HasData)
            {
                var ext = extensions.ReadSequence(); string id = ext.ReadObjectIdentifier();
                // Fail closed for delta/scoped/indirect/partitioned CRLs; no all-chain conclusion.
                bool critical = ext.PeekTag().HasSameClassAndValue(Asn1Tag.Boolean) && ext.ReadBoolean();
                if (id == "2.5.29.27" || id == "2.5.29.28" || id == "2.5.29.46" ||
                    (critical && id != "2.5.29.20" && id != "2.5.29.35")) restricted = true;
            }
        }
        body.ThrowIfNotEmpty();
        bool window = until.HasValue && from <= review && review <= until.Value;
        var usage = issuer.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault();
        bool issuerUsage = usage != null && (usage.KeyUsages & X509KeyUsageFlags.CrlSign) != 0;
        bool supported = !restricted && !entryExtensions && issuerUsage;
        return D("signature_match", verified, "signature_algorithm", oid, "algorithm_identifiers_match", algorithmMatch,
            "issuer_name_match", issuerName, "issuer_crl_sign_usage", issuerUsage, "this_update_utc", from.ToString("o"), "next_update_utc", until?.ToString("o"),
            "age_seconds", (review - from).TotalSeconds, "review_within_update_window", window,
            "full_direct_crl_supported", supported, "serial_listed", revoked,
            "status", verified && issuerName && algorithmMatch && window && supported ? (revoked ? "REVOKED" : "NOT_LISTED_IN_VALID_WINDOW_CRL") : "REVIEW",
            "scope", "Specific certificate/direct full CRL at review only; not historical or whole-chain non-revocation");
    }
    public static object CollectCrls()
    {
        var results = new List<object>(); var downloads = new Dictionary<string, byte[]>();
        var failures = new Dictionary<string, string>(); var fetchedAt = new Dictionary<string, string>();
        using (var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }))
        {
            client.Timeout = TimeSpan.FromSeconds(12);
            client.MaxResponseContentBufferSize = 8 * 1024 * 1024;
            foreach (var pair in pairs.Values)
                foreach (string declared in CrlUrls(pair[0]).Distinct().Take(2))
                {
                    var d = D("certificate_sha256", Hash(pair[0].RawData), "issuer_sha256", Hash(pair[1].RawData), "distribution_url", declared);
                    try
                    {
                        var url = new Uri(declared);
                        if ((url.Scheme != "http" && url.Scheme != "https") ||
                            !new[] { "crl3.digicert.com", "crl4.digicert.com", "www.microsoft.com", "crl.microsoft.com", "mscrl.microsoft.com" }.Contains(url.Host))
                            throw new InvalidDataException("URL outside evidence fetch allowlist");
                        var secure = new UriBuilder(url) { Scheme = "https", Port = -1 }.Uri;
                        d["fetch_url"] = secure.AbsoluteUri;
                        if (failures.TryGetValue(secure.AbsoluteUri, out string priorError))
                            throw new IOException("Same-run fetch failure retained: " + priorError);
                        byte[] raw;
                        if (!downloads.TryGetValue(secure.AbsoluteUri, out raw))
                        {
                            try
                            {
                                using (var response = client.GetAsync(secure).GetAwaiter().GetResult())
                                {
                                    response.EnsureSuccessStatusCode();
                                    raw = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                                }
                            }
                            catch (Exception e) { failures[secure.AbsoluteUri] = e.Message; throw; }
                            downloads[secure.AbsoluteUri] = raw;
                            fetchedAt[secure.AbsoluteUri] = DateTimeOffset.UtcNow.ToString("o");
                        }
                        string path = "crls/" + Hash(raw) + ".crl"; File.WriteAllBytes(Path.Combine(directory, path), raw);
                        d["downloaded_at_utc"] = fetchedAt[secure.AbsoluteUri]; d["sha256"] = Hash(raw); d["path"] = path;
                        d["validation"] = CheckCrl(raw, pair[0], pair[1]);
                    }
                    catch (Exception e) { d["error"] = e.Message; d["status"] = "UNAVAILABLE_OR_UNSUPPORTED"; }
                    results.Add(d);
                }
        }
        return results;
    }
}
