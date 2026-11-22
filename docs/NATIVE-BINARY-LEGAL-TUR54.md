# W014 / SEC-EMD-09 — native Windows binary scope (TUR54)

Status: IN PROGRESS / legal REVIEW. Safe `.emd` DISABLED; V1.0 BLOCKED. Physical Windows/printer/TUR46 four-edge/DPI acceptance NOT RUN unless separately performed. No installer/publish/merge authorization is created by this evidence.

## Concrete gap and new evidence

A `PresentationFramework.dll` anchor does not identify the native WPF libraries or their legal exceptions. Existing environment jobs now add `windows-trust/native-scope/` before the same trust-file hash manifest is generated. Product code, tests, workflow jobs and action pins do not change. Original TUR53 CMS/ExtraStore/RFC3161/CRL records and comparisons are not rewritten by this new collector.

The collector reads the installed WindowsDesktop runtime root's DLL PE headers. It records native images and mixed-mode candidates; managed images with a managed-native header (for example ReadyToRun) are separately enumerated rather than equated to an unmanaged DLL. Root discovery counts and required exception-file presence are explicit. This is not a recursive whole-installation inventory, proof that files were loaded, or a final distribution SBOM.

For selected files it records actual component version, full-file SHA-256/size, COFF machine, FileVersion/ProductVersion/OriginalFilename/company, import DLL names and OS Authenticode/signing-certificate observations. No DLL is loaded or executed. No binary is copied into the evidence or the public repository. Existing coreclr anchors and selected-SDK DiaSymReader native files are also associated with their observed versions.

| Informative exception | Recorded candidate category | Remaining acceptance |
| --- | --- | --- |
| coreclr.dll; PresentationNative_cor3.dll; vcruntime140_cor3.dll; wpfgfx_cor3.dll; Microsoft.DiaSymReader.Native.*.dll | .NET Library scope information | Exact binary/version/accompanying terms, notices and distribution obligations REVIEW |
| D3DCompiler_47_cor3.dll | Windows SDK scope information | Exact SDK REDIST origin and SDK redistribution conditions REVIEW |
| Other native/mixed root DLLs | Other-files MIT scope information | Informative classification, not per-version or whole-package rights approval |

The source category comes from exact official dotnet/core `license-information-windows.md` revision `6c25b9da384e8d8a16fd12c79da8a8aa6bdfc6a1`. That document is informative and not itself a license. Exact dotnet/wpf redistributables rationale revision `7e162b757e8acad3d2d6d78515f9dc62cb886034` documents original-to-renamed files and import-table naming; it discusses .NET Core 3.0 and is not silently treated as a current-version grant. Both original documents and their source repository MIT licenses are retained with Git blob/SHA-256 identities in `third-party/NativeScope`. Source document licenses do not relicense the installed binaries.

## SDK REDIST and conditions

Only already installed Windows SDK roots are inspected: specific KitsRoot10 registry values and the standard SDK root. SDK include-directory versions, local accompanying license/REDIST/notice files and bounded `Redist/**/d3dcompiler_47.dll` candidates are retained. No SDK, runtime or installer is downloaded/installed. Discovery limits or unavailable files are visible; absence is not PASS. Local legal texts are retained byte-for-byte, with limits explicitly marked.

Each observed `D3DCompiler_47_cor3.dll` is compared with actual SDK REDIST candidates using full-file SHA-256, size and machine. A match proves byte identity across the rename, not a rebuild or permission to redistribute. A mismatch or missing local candidate leaves the source association open. Import names in actual WPF native binaries separately show which renamed dependency the file references; this is static metadata, not execution evidence.

[Windows SDK REDIST](https://learn.microsoft.com/en-us/legal/windows-sdk/redist), updated 2024-10-21, lists `Redist/D3D/x64/d3dcompiler_47.dll` and the x86 counterpart for Classic Windows applications, excluding Universal Windows applications. That list conditions use on a valid SDK license and unmodified object code under accompanying terms. [SDK terms](https://learn.microsoft.com/en-us/legal/windows-sdk/license) separately address redistribution, functionality, notices, downstream terms and other restrictions. SDK path/version/REDIST presence is not proof that all obligations have been met. The E-Note WPF project is not a final installer or publish manifest.

Installed MICROSOFT .NET LIBRARY terms remain separate from source MIT. Global package metadata, OS Authenticode Valid, NuGet verify exit 0, or a source declaration never replace per-file legal review. No incomplete license text is reconstructed.

## Preserved gates

The TUR53 distinction remains: historical NoCheck chain true is not historical non-revocation; 41 valid-update-window CRL certificate observations are distinct from only two certificates covered by CRLs issued within 24 hours. Expired serial absence can reflect CRL pruning. DigiCert TLS/raw coverage and independent signer gaps remain open; online tool success is not a retained fresh response. Registry key EXPIRED_AT_REVIEW remains unchanged.

Preserve TUR52 exact buffers/chainsaw/binary lock/NCC MATCH and twirp-ts INCLUSION_NOT_PROVEN, Node source/binary limits; TUR51 VMR/eight archives/438 files/four pins/runner MATCH; Java three exact legal files and both Konscious attributions. Native legal mapping cannot close fonts/logo/final distribution, UDF/physical or safe-EMD gates.
