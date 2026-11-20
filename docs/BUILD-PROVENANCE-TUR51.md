# TUR51: source and legal provenance for observed CI builds

W014 / SEC-EMD-09 IN PROGRESS. V1.0 BLOCKED. Product code and product tests
are unchanged. TUR50 Java source matches, original Konscious license/2017 and
2024 attributions, and the four logo/icon hashes are preserved.

| Item | Acceptance | Status before final Windows CI |
| --- | --- | --- |
| W014-SRC-01 | Link SDK 10.0.114/115 and observed .NET 10 runtime declarations to exact VMR manifests and component source commits | REVIEW |
| W014-SRC-02 | Preserve version-specific original source legal files, separately from installed binary terms | REVIEW |
| W014-ACT-01 | Pin the four Actions to exact commits observed in TUR50 and retain root licenses plus upstream dependency declarations | REVIEW |
| W014-ACT-02 | Compare runner action legal/metadata and executed JavaScript entrypoint bytes with the retained exact source | REVIEW |

## The SDK commit belongs to the VMR

The previous `dotnet/sdk` query for
`8e0f8ed2312954727f8919d7d1c2f78b483b4ee7` returned 422. That historical
query is not evidence of a component source match. The same commit exists in
`dotnet/dotnet`, the virtual monolithic repository (VMR). Its original
`src/source-manifest.json` maps SDK source to
`dotnet/sdk@f71ce870eee437330738d7eda317459fdc908b71`.

| Observed declaration | VMR commit |
| --- | --- |
| SDK 10.0.114 / runtime 10.0.14 | 0427b9abbead3fa64c6655eb4290c537397131fa |
| SDK 10.0.115 / runtime 10.0.15 | 8e0f8ed2312954727f8919d7d1c2f78b483b4ee7 |
| Runtime 10.0.8 | 94ea82652cdd4e0f8046b5bd5becbd11461482ca |
| Runtime 10.0.11 | e2f47b0110ed922f21a1522da67279133ce28f32 |

Each snapshot preserves the original source manifest, root LICENSE/NOTICE
and legal files for sdk, runtime, aspnetcore, windowsdesktop and wpf. The five
component LICENSE blobs at each snapshot were independently read from the
component repository/commit named by the source manifest: all 20 match the
VMR copies. Metadata retains those origin checks. SDK maps to sdk; NETCore to
runtime; ASP.NET to aspnetcore; WindowsDesktop to windowsdesktop, wpf and
runtime. Other installed runtime generations stay UNMAPPED.

This verifies a source-declaration link, not a rebuild of installed binaries.
Unknown future revisions stay UNMAPPED; known revision/version conflicts fail
collection. Runtime lists still describe installed environments, not every
test's runtime selection. SDK 10.0.114/115 and runtime-set differences between
workers remain visible; no SDK upgrade or uniform-environment claim is made.

## Source MIT and binary terms remain distinct

The installed root LICENSE.txt begins MICROSOFT SOFTWARE LICENSE TERMS /
MICROSOFT .NET LIBRARY. It is not replaced by a source MIT license. Source
legal files are linked per snapshot/component and compared by exact SHA-256
with observed installed texts. Matches establish that text's equality only.
Missing per-runtime files and source-versus-binary differences remain REVIEW.
No copied source license is declared to cover a binary distribution without
its separate licensing evidence. Final distribution mode and rights stay open.

## Exact Actions without upgrading their code

| Action | TUR50 observed v4 commit now pinned |
| --- | --- |
| actions/checkout | 11d5960a326750d5838078e36cf38b85af677262 |
| actions/setup-dotnet | 67a3573c9a986a3f9c594539f4ab511d57bb3ce9 |
| actions/setup-java | cf277c60eb25467037889841efdb72551f06f6c3 |
| actions/upload-artifact | ea165f8d65b6e75b540449e92b4886f43607fa02 |

Exact upstream checkouts preserve four original root LICENSE files and all
370 `.licenses` dependency declarations, plus action.yml, package metadata,
lockfiles and `.licensed.yml` configuration. These are upstream declarations,
not a completed transitive license audit. Git tree, Git blob and SHA-256
values are retained. Main/post JavaScript entrypoint hashes are recorded;
the large JavaScript bundles are not copied into the source-evidence archives.

The collector verifies eight original evidence archives (438 files), verifies
all workflow `uses` refs against the recorded exact pins, and on GitHub Actions
compares legal/metadata files and main/post entrypoints in that job's runner
`_actions` directories. Missing files or byte changes fail collection. Local
collection labels runner comparisons NOT_RUN. An action absent from the job
is NOT_REQUESTED. No archived upstream code is executed by this collector.

Original files are stored inside deterministic ZIPs in
`third-party/BuildProvenance`; `.gitattributes -text` protects their bytes.
The three existing environment artifacts include those ZIPs and provenance
metadata. Archived dependency `.licenses` files retain their original paths;
their dot prefix cannot exclude them from the outer uploaded artifact.

## Remaining acceptance

Authenticode/package signatures and trust roots, binary/source reproducibility,
complete Action dependency audit, external installer downloads, runner Node.js
and OS legal inventory, per-runtime binary distribution terms, font embedding,
logo redistribution and final installer inventory remain open.

Physical Windows, printer, TUR46 four-edge measurements, DPI and physical
security NOT RUN. Secure .emd DISABLED. W010 IN PROGRESS; EXP-007 REVIEW;
V1.0 BLOCKED. No main merge or installer release.

Engineering estimate remains the previous 98%, not an acceptance percentage.
