# TUR 08 — Source, Windows CI, scope and security audit (2026-11-02)

Repository Public `ftmstr/Embasic-E-Note-Build` MIT, stacked Draft PR #8.
Base PR #7 HEAD: `718106948764f3efdd51c3ad3908c3704f0d09fc`.
Public `main` unchanged. Google Drive Kaynak ENOTE-08/04/05 and Tur 07
Rapor inspected. Research-chat HTML v1.1.1 and revised page/logo
requirements were considered; only specifically listed features below
were implemented in WPF or shared Core.

## Code commits

- `e51465a24476f6830fccfa2207ab3b718bf59f52`:
  research paper presets and 14 geometry tests; two verified genuine
  E-Note brand asset blobs (new ICO + 512 PNG).
- `071bd262c14f77a54a1141ba3bbb6023bb3c287e`:
  Windows application/taskbar icon; WPF page size/orientation/zoom
  geometry-preview tab. Does not edit/save/print actual pages.
- `04bb85d6ccac70bfa1dd8c1883a17028c1ca26e5`:
  experimental AES-256-GCM protected `ELH1` workspace node GUID/
  ancestry relative-path mapping; 18 negative tests.
- `5bf2383b4bbf8809b56f1687db80e559c51f3bbf`:
  bind encrypted root metadata UUID to authenticated factor UUID,
  one further negative case.
- `8bca0557db4e66f787fa3ec1336abe1627c12e75`:
  encrypted-only `.elh` create-only disk store and 8 more regression
  checks, including overwrite denial, missing file, tampered AEAD,
  wrong workspace ID and encrypted temp cleanup.

## Verified CI (not a real user GUI run)

- `41419990296`: initial page geometry/icon assets + 14 checks, PASS.
- `41420234517`: compiled page preview WPF, PASS.
- `41420839226`: ELH1 ancestry draft, Release 0 errors/warnings,
  31+15+30+35+38+42+18 = **209/209 PASS**.
- `41421331776`: root identity fix, PASS.
- [`41421579049`](https://github.com/ftmstr/Embasic-E-Note-Build/actions/runs/41421579049):
  LAST code HEAD, Windows Release **0 warnings / 0 errors**;
  workspace/paper 31/31, EMD1 15/15, EMD2/storage 30/30,
  recovery/rotation 35/35, Tur06 38/38, Tur07 42/42,
  encrypted workspace index 27/27 = **218/218 PASS**. Job log read.

## Security design, non-claims

`WorkspaceLockIndex` encrypts workspace ID and parent-child-note GUID
relationships, including relative paths, under an independently unlocked
256-bit root `LayerSecret`. It validates root ID and ancestry; child/note
keys are STILL separately required by the existing ELV1/ELK1 outer AEAD.
`WorkspaceLockIndexDiskStore` writes authenticated ciphertext only, to
same-directory encrypted temp then create-only File.Move. It never
writes plaintext note content or passwords. However:
- This is **experimental identity metadata**, not a production
  transactional, path-handle-bound workspace hierarchy.
- Real WPF folder tree is NOT linked to ELH1 and therefore actual user
  folders are not cryptographically locked by this work.
- No tested root-key-loss/disaster recovery across Windows users, Windows
  ACL-denial, full path symlink TOCTOU, cross-process CAS, disk-full,
  power-failure durability, rollback prevention or antivirus interference.
- Prototype record size cap is a memory/format safeguard, not an
  intended product limit on note/folder count or nesting.
- ELV1 factor rotation may leave old credential-accessible vault copies.
- EMD1/EMD2/ELK1/ELV1/ELH1/ERB1 are drafts; no cross-implementation
  vectors/security audit/final signature.

## Remaining product blockers

WPF **New Secure Note (.emd) DISABLED**; Word/UYAP visual WYSIWYG,
real page persistence, print preview, PDF/DOCX/RTF/UDF export,
actual UYAP Editor open/save comparison, Windows GUI/DPI/keyboard
accessibility, full SBOM/NOTICE/audit, signed installer and clean
Windows VM acceptance are NOT RUN / NOT IMPLEMENTED. No installer
was uploaded to Drive `Uygulama`.

Engineering progress estimate **24%**, not measured release
readiness or feature acceptance. Conditional release planning
2027 Q2–Q3 is not a delivery commitment. Release gate BLOCKED.
