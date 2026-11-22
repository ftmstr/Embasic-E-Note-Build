# W014 / SEC-EMD-09 — contextual signature evidence (TUR53)

Status: IN PROGRESS. Safe `.emd` DISABLED; V1.0 BLOCKED. This collector does not authorize distribution, add a trusted root, ignore chain errors, or alter product behavior. Physical Windows/printer/TUR46 four-edge/DPI acceptance remains NOT RUN unless separately performed.

## Evidence layers

The existing TUR52 OS Authenticode result, unpinned offline NuGet verification, registry mathematics/expired-key status, and standalone offline leaf-chain observations remain unchanged. `windows-trust/contextual/` is an additional observation, included in the existing hash manifest and six existing environment artifacts. No new product tests or workflow jobs are added.

| Layer | New evidence | Limit |
| --- | --- | --- |
| CMS | PE WIN_CERTIFICATE or actual restored NuGet `.signature.p7s`, public certificate DER, mathematical signer check | Does not itself recompute the PE image digest or NuGet archive signature; existing OS/NuGet checks remain separate |
| ExtraStore | Embedded CMS and RFC3161 TSA candidates supplied to X509Chain; code-signing/timestamp EKU; actual chain/status/root identity and observed Windows Root stores | ExtraStore roots are candidates, never newly trusted roots; system trust is used without ignored errors |
| Timestamp | RFC3161 full decode, TSA key/ESS identifier/EKU/validity and token-to-parent-signature digest binding; claimed token time recorded | Legacy countersignatures remain REVIEW. No historical chain context is selected from unbound signer signingTime |
| Historical context | Separate signer and TSA chains at a bound RFC3161 time with NoCheck | NoCheck deliberately excludes revocation; not proof of historical non-revocation or final Authenticode lifetime policy |
| Online now | Separate current-time chain request, revocation ExcludeRoot, five-second URL retrieval setting | Windows may use cached responses. Online Build alone is not a retained fresh response |
| Direct CRL | Bounded HTTPS fetch of certificate CDP on explicit Microsoft/DigiCert hosts; raw DER, hash/time, issuer signature/name/CRLSign, serial and thisUpdate/nextUpdate | Only full direct supported CRLs, certificate-specific and current update window; delta/indirect/scoped/freshest or unknown critical semantics remain REVIEW; unavailable URLs are not PASS |
| Identity | Observed NuGet repository signer compared with separate official Microsoft publication; observed DigiCert root compared with exact official source revision; actual NuGet verify with fingerprint and online mode | Publication comparisons depend on Web PKI; not independent offline root custody, package author signature or rights approval |

CRL content is treated as signed data; HTTP CDP URLs are attempted over HTTPS with original and fetch URLs retained. Redirects, non-allowlisted hosts and overlarge inputs are refused. Unsupported algorithms/scopes and retrieval errors remain explicit. No guarantee is made that every chain certificate has a retained valid-window CRL. Root revocation and historical OCSP/CRL coverage remain separate open gates. No certificate store is changed; NuGet's revocation environment value is restored after collection.

## Independent publication comparisons

`third-party/TrustPolicy/published-identities.json` records comparison identities, not a production trust allowlist. Microsoft published the NuGet.org repository certificate SHA-256 `1f4b311d9acc115c8dc8018b5a49e00fce6da8e2855f9f014ca6f34570bc482d` on 2024-03-13. DigiCert's exact official `trust_anchor_identifiers.md` revision `250e828a04bc1a344c749e597dd1f3938d76f6f1` identifies Trusted Root G4 as `552f7bdcf1a7af9e6ce672017f4f12abf77240c78e761ac203d1d9d20ac89988`. Mismatch is not silently accepted. Other roots/signers remain outside this narrow independent publication comparison.

API contract: [Rfc3161TimestampToken.VerifySignatureForSignerInfo](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.pkcs.rfc3161timestamptoken.verifysignatureforsignerinfo) checks the token signature, TSA certificate and parent-signature binding; chain trust is handled separately. [NuGet repository signatures](https://learn.microsoft.com/en-us/nuget/api/repository-signatures-resource) describes repository identity; the [Microsoft certificate announcement](https://devblogs.microsoft.com/dotnet/the-nuget-org-repository-signing-certificate-will-be-updated-as-soon-as-april-8th-2024/) supplies the independent fingerprint.

## Legal applicability stays separate

| Material | Preserved evidence | Current applicability decision |
| --- | --- | --- |
| Installed .NET SDK/runtime | TUR50 installed MICROSOFT .NET LIBRARY terms and actual per-worker inventory; TUR51 VMR/component source links | Installed binary conditions are not replaced by source MIT. Source declaration is not binary rebuild. Final distribution scope REVIEW |
| Argon2/Blake2 | Original license text, both attributions, restored archive identity | NuGet repository signature adds integrity/identity evidence, no additional copyright permission and no author-signer claim |
| buffers/chainsaw/binary | Exact lock/NCC main-body MATCH from TUR52 | Missing/short exact legal declarations remain REVIEW; no missing text reconstructed |
| twirp-ts 2.5.0 | Retained short declaration, lock absent, INCLUSION_NOT_PROVEN | Do not infer full inclusion or exclusion; legal/bundle coverage REVIEW |
| Runner Node/Actions/installer | TUR51 eight archives/438 files/four pins/runner MATCH; TUR52 exact Node source legal counterparts and installer bytes | Source legal correspondence does not prove binary/source reproduction or redistribution rights; external download and full inventory REVIEW |
| Java/fonts/logo/final package | TUR50 three exact Java legal file matches; existing logo bytes | Preserve exact evidence. Font embedding/logo/final distribution acceptance remains open |

Package permission, signature identity, chain trust, revocation coverage and source reproduction are distinct acceptance columns. A successful signature/chain observation never changes legal REVIEW to DONE. The registry key remains EXPIRED_AT_REVIEW; its mathematical MATCH is not current publisher approval.
