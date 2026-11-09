using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Embasic.ENote.Storage;

internal static class VaultAuthorityPipeProtocol
{
    internal const int ChallengeSize = 32;
    internal const int RequestPayloadSize = 1 + 1 + 16 + 8 + 32 + 32;
    internal const int RequestSize = RequestPayloadSize + 32;
    internal const int ResponsePayloadSize = 1 + 8 + 32;
    internal const int ResponseSize = ResponsePayloadSize + 64;
    internal const byte Observe = 1;
    internal const byte Publish = 2;
    internal const byte Rejected = 0;
    internal const byte Observed = 1;
    internal const byte Committed = 2;
    internal const byte PriorObserved = 3;
    internal const byte Superseded = 4;
    internal const byte Unknown = 5;

    internal static bool IsP256(ECDsa key) =>
        key.ExportParameters(includePrivateParameters: false).Curve.Oid.Value == "1.2.840.10045.3.1.7";

    internal static void RequireKey(ReadOnlySpan<byte> key)
    {
        if (key.Length != 32) throw new ArgumentException("Expected a 32-byte IPC role key.");
    }

    internal static byte[] Request(byte operation, Guid workspace, VaultGenerationStamp? candidate,
        ReadOnlySpan<byte> challenge, ReadOnlySpan<byte> key)
    {
        RequireKey(key);
        byte[] request = new byte[RequestSize];
        request[0] = 1;
        request[1] = operation;
        workspace.TryWriteBytes(request.AsSpan(2, 16));
        if (candidate is not null) WriteStamp(request.AsSpan(18, 40), candidate);
        RandomNumberGenerator.Fill(request.AsSpan(58, 32));
        RequestMac(request, challenge, key).CopyTo(request, RequestPayloadSize);
        return request;
    }

    internal static bool VerifyRequest(byte[] request, byte[] challenge, ReadOnlySpan<byte> key) =>
        CryptographicOperations.FixedTimeEquals(RequestMac(request, challenge, key), request.AsSpan(RequestPayloadSize));

    private static byte[] RequestMac(byte[] request, ReadOnlySpan<byte> challenge, ReadOnlySpan<byte> key)
    {
        Span<byte> bound = stackalloc byte[5 + ChallengeSize + RequestPayloadSize];
        "EVRQ1"u8.CopyTo(bound);
        challenge.CopyTo(bound[5..]);
        request.AsSpan(0, RequestPayloadSize).CopyTo(bound[(5 + ChallengeSize)..]);
        return HMACSHA256.HashData(key, bound);
    }

    internal static byte[] Response(byte status, VaultGenerationStamp? stamp, byte[] challenge,
        byte[] request, ECDsa signer)
    {
        byte[] response = new byte[ResponseSize];
        response[0] = status;
        if (stamp is not null) WriteStamp(response.AsSpan(1, 40), stamp);
        byte[] signature;
        lock (signer)
            signature = signer.SignData(ResponseBinding(response, challenge, request), HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        if (signature.Length != 64) throw new CryptographicException("Expected P-256 response signature.");
        signature.CopyTo(response, ResponsePayloadSize);
        return response;
    }

    internal static bool VerifyResponse(byte[] response, byte[] challenge, byte[] request, ECDsa verifier) =>
        verifier.VerifyData(ResponseBinding(response, challenge, request), response.AsSpan(ResponsePayloadSize),
            HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

    private static byte[] ResponseBinding(byte[] response, byte[] challenge, byte[] request)
    {
        byte[] bound = new byte[5 + ChallengeSize + 32 + ResponsePayloadSize];
        "EVRS1"u8.CopyTo(bound);
        challenge.CopyTo(bound, 5);
        SHA256.HashData(request).CopyTo(bound, 5 + ChallengeSize);
        response.AsSpan(0, ResponsePayloadSize).CopyTo(bound.AsSpan(5 + ChallengeSize + 32));
        return bound;
    }

    internal static VaultGenerationStamp ReadStamp(ReadOnlySpan<byte> bytes, Guid workspace) =>
        new(workspace, BinaryPrimitives.ReadInt64LittleEndian(bytes), Convert.ToHexString(bytes[8..40]));

    private static void WriteStamp(Span<byte> bytes, VaultGenerationStamp stamp)
    {
        BinaryPrimitives.WriteInt64LittleEndian(bytes, stamp.Epoch);
        Convert.FromHexString(stamp.ManifestSha256).CopyTo(bytes[8..40]);
    }
}
