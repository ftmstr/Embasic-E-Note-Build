# TUR52: exact package/bundle links and observed Windows trust

W014 / SEC-EMD-09 IN PROGRESS. Secure .emd DISABLED; V1.0 BLOCKED.
Product code and product tests are unchanged. No new repeat test is added.
The original TUR51 eight archives/438 files, VMR source links, four Action pins
and runner comparisons remain unchanged, as do Java and Konscious evidence.

| Item | Acceptance | Status before final Windows CI |
| --- | --- | --- |
| W014-PKG-01 | Bind buffers/chainsaw/binary exact npm archives to the retained Action lock and full main-module bodies | REVIEW |
| W014-PKG-02 | Record twirp-ts 2.5.0 lock absence and package legal evidence without claiming bundle exclusion | REVIEW |
| W014-SIG-01 | Record registry signature cryptographic result separately from key expiry and independent trust | REVIEW |
| W014-SIG-02 | Record real Windows Authenticode, public certificate chains and NuGet verification output | REVIEW |
| W014-TOOL-01 | Record runner Node candidates, their own version/source declarations/legal files and exact external installer script identity | REVIEW |

## Exact packages and Action bundle

The retained upload-artifact commit is
ea165f8d65b6e75b540449e92b4886f43607fa02. Its package-lock includes
buffers 0.1.1, chainsaw 0.1.0 and binary 0.3.0, with exact resolved URLs and
SHA-512 integrity. Downloaded archives match both lock and registry metadata.

| Package | Lock | NCC main module |
| --- | --- | --- |
| buffers 0.1.1 | MATCH | 51590 |
| chainsaw 0.1.0 | MATCH | 46533 |
| binary 0.3.0 | MATCH | 66474 |
| twirp-ts 2.5.0 | ABSENT_FROM_RETAINED_LOCK | INCLUSION_NOT_PROVEN |

The full main-module bodies match after narrowly documented NCC import
rewrites, built-in EventEmitter/Stream parentheses and outer whitespace
trimming. Remaining body text must be exactly equal. Source hashes, bundle
byte ranges and module/import IDs are retained. The CI collector compares
these ranges with the actual runner bundle and rechecks package integrity,
identity, legal/readme hashes and normalized full-body equality. This is not
a package rebuild, execution proof, publisher identity or license approval.

Twirp-ts 2.5.0 occurs in the old .licenses declarations but not in the
retained lock. Its exact main-body comparison found no match. This does not
prove all of its code is absent: transformation, another version or partial
code inclusion would require further review.

The exact four package archives contain no complete standalone LICENSE text.
Buffers has no package license declaration; chainsaw declares MIT/X11;
binary's README says MIT; twirp-ts says MIT <3. Metadata/short labels are
retained as declarations, not reconstructed into invented license wording.
Legal acceptance stays REVIEW.

Package archives are fetched as integrity-pinned data; they are never npm
installed and their scripts are never executed. The repository evidence ZIP
contains registry metadata, hash/range review records, key response and the
two original MIT-header installer scripts. It does not redistribute the
unresolved package archives or package/bundle source bodies.

## Registry signatures and trust boundary

The registry response supplies ECDSA signatures for all four exact versions.
An independent local cryptographic check matched all four using the key
returned by the registry key endpoint. That key declares expiry
2025-01-29T00:00:00.000Z. Cryptographic equality is therefore not labelled
current trusted publisher approval. Trusted signing time, independent key
anchoring, publisher/source provenance and revocation remain open.

Windows CI repeats the real registry signature observation with built-in
.NET ECDsa APIs and records expiry against collection time. No new Python
crypto dependency is added. Key ID and calculated DER/SPKI SHA-256 are
separate identifiers; their equality is not assumed.

## Windows signatures and tool origins

Collect-WindowsTrust.ps1 records SHA-256 and Get-AuthenticodeSignature for
the previously inventoried binary anchors. Signer/timestamp certificates
and chain elements are retained as public DER files. X509Chain uses Offline
revocation and disables certificate downloads; observed Root-store presence
is recorded. OS signature status and explicit offline chain status remain
separate. An expired or unsigned finding is evidence, not a collector crash
or a release PASS.

On the build worker, actual restored NuGet archives are passed to
dotnet nuget verify --all, without a custom signer allowlist or trust-store
change. The tool's exit status, complete output, archive SHA-256, detached
signature bytes and public CMS certificates are retained. Revocation mode is
explicitly offline and restored afterward. Tool acceptance is not fresh
revocation, independent root trust, binary/source reproducibility or legal
redistribution approval.

The collector locates live Runner.Worker executable paths and looks for that
runner's bundled node20/node24 executables. It records binary hashes,
Authenticode, runtime version/release self-declarations and available legal
files. These are runtime candidates, not proof of every step's selected
runtime; live Node paths are separately observed without command-line data.
No source URL is automatically promoted into a verified binary/source link.

Both pinned setup-dotnet installer scripts match exact source bytes and are
retained with original MIT headers. Windows records the PowerShell script's
Authenticode status. Matching installer source does not verify a downloaded
SDK archive: archive origin/hash/signature and final distribution terms remain
separate acceptance items.

## Release boundaries

Windows trust findings, package/registry cryptographic checks and module
identity collection do not close W014. Binary licensing, independent signer
trust, fresh revocation, missing complete package terms, runner/source
provenance, fonts/logo and final installer remain REVIEW/BLOCKED.
Installed MICROSOFT .NET LIBRARY terms stay distinct from source MIT.

Physical Windows/printer/TUR46 four-edge/DPI/security NOT RUN in this Linux
workspace. Existing PDF/XPS/UDF/Markdown/RTF/DOCX/ODT acceptances are preserved
by the existing CI. No main merge or installer release.

Engineering estimate remains the previous 98%, not an acceptance percentage.
