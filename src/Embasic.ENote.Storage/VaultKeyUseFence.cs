namespace Embasic.ENote.Storage;

/// <summary>
/// Experimental scoped-use contract. Entry must check the exact generation and
/// exclude reservations and advances until disposal, across all provider writers.
/// A provider must fail closed if it cannot maintain exclusion for the whole scope.
/// Callers must avoid re-entry and keep callbacks synchronous and bounded.
/// </summary>
public interface IVaultKeyUseFence
{
    IDisposable EnterKeyUse(VaultGenerationStamp expected);
}
