# Embasic E-Note — security status (Tur 01)

**NOT IMPLEMENTED:** The encrypted .emd writer/reader, secure key storage, application/folder/note locks, recovery and authenticated exports do not exist in this bootstrap.

- .emd denotes a planned authenticated, versioned encrypted container specified by Drive ENOTE-08.
- .emd must never be read or written as plaintext. The bootstrap plaintext reader rejects it.
- Standard .md is unencrypted and must not be described as protected.
- Only the application's UI shell exists; it does not create or save notes.
- The app-lock UI alone is not disk encryption. Real folder protection requires key policy.
- Security of same-account malware, screenshots and unlocked process memory is outside absolute guarantees.
- Printing/export and the official UYAP UDF acceptance remain NOT RUN.
- No runtime third-party NuGet packages, network API endpoints or ad SDKs were introduced by this commit.

Release gate: no installer is to be published until encryption, key recovery, Windows GUI,
UDF Editor acceptance, output, licensing and packaging tests pass with evidence.
