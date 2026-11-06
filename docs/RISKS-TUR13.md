# Tur 13 — Security and product blockers
Status as of 2026-11-06. All unverified gates remain open.

| Area | Finding | Acceptance state |
|---|---|---|
| HTML/script in Markdown | Inert `Run`-only preview tokenizer; no HTML execution, remote content or clickable links in code path | Parser tests PASS; real Windows GUI NOT RUN |
| Formatting semantics | Limited bold/italic/code/strike; not fully CommonMark, nested spans not guaranteed; complex pipe tables unsupported | IN PROGRESS |
| `.md` storage | SHA-256 optimistic expectedDigest and staged File.Replace, but not cross-process attack-resistant atomic CAS | IN PROGRESS; race/TOCTOU NOT RUN |
| `.md` layout sidecar | Separate plaintext JSON, possible source/metadata partial success; never write sidecar for `.emd` | IN PROGRESS |
| RTF | RichTextBox drafts; original immutable and new-copy only; layout fidelity not tested | IN PROGRESS |
| DOCX/ODT | Only safe simple paragraphs editable and saved as new copy. Complex documents refused/read-only; OOXML/ODF full fidelity absent | IN PROGRESS |
| ELK1 layered locks | Folder/child/note AES-GCM outer layers protect EMD2 alternate DPAPI slots, but old unlayered backups may bypass policies | BLOCKED |
| ELV1/ERB1 recovery | Old vault/backup copies may remain password-accessible after rotation; rollback/dedicated root recovery unresolved | BLOCKED |
| ELH1 path mapping | Path existence/ancestry checks are not handle-bound against malicious symlink swaps; no GUI integration | BLOCKED |
| DPAPI/account | Real different-user Windows credential/ACL denial tests not performed | NOT RUN |
| Disk/NTFS | Disk full, interrupted writes, reparse traversal, power loss and cross-process races not verified physically | NOT RUN |
| Native UI | No actual GUI automation, high DPI, keyboard/screen reader or large-tree performance acceptance | NOT RUN |
| Print/export | A4 model exists but WYSIWYG pagination and P0 PDF/DOCX/RTF/UDF output not accepted | BLOCKED |
| UDF | Real UYAP Doküman Editörü must open, edit, re-save and compare generated unsigned UDF | NOT RUN |
| Supply chain | NuGet/icon/font NOTICE/SBOM and distribution licensing incomplete | BLOCKED |
| V1 release | No signed installer, no physical Windows/UDF/security acceptance | BLOCKED |

Do not enable WPF **New Secure Note (.emd)** until independent locks,
key hierarchy, credential recovery and all mandatory security gates pass.
Do not upload a package to Drive `Uygulama` or merge into Public main.
