# Tur 24 — Independent retry intent, root recovery and fresh factors

Status **REVIEW**. W007 / SEC-EMD-05 and the W012 release gate remain **BLOCKED**.

## Independent retry and lost replies

The transition-aware path expects one atomic provider state per workspace.
It contains the current stamp, one pending expected/candidate pair and the
latest commit receipt. A stamp commits to the workspace ID, epoch and
manifest digest. The provider reserves an empty slot for the exact current
stamp. Its commit must match the entire reservation and update the stamp,
receipt and pending slot together. A provider using this contract must reject
legacy unbound CAS.

The coordinator stages and verifies the candidate before requesting a
reservation. It re-reads the independent state before committing. Local HMAC
journaling is optional. If the journal is absent or deleted, resume uses the
exact independently recorded intent. An authenticated conflicting same-slot
local record fails closed. A lost commit reply can be resolved from the
latest independent receipt and selected manifest even after the journal is
deleted. An older receipt falls outside this recovery window. A missing or
corrupt staged candidate blocks the pending slot. Cancellation, competing
staged-file cleanup and disaster recovery of that slot remain open.

The tests simulate atomic reserve and commit in process. A rollback of the
fixture authority together with local storage can erase the reservation.
Legacy generation providers keep their existing behavior. The interface
specifies an independent state requirement. A production provider and
physical CAS, reboot and power-loss results are still pending.

## Windows key delivery

The generation-backed Windows ELV1 file gate checks the independent
generation and pending state before opening the enrolled file, and checks
them again before handing off the key. A pending transition, changed stamp
or unavailable authority aborts delivery. The final read and delivery are
separate operations. An advance or reservation can occur between them.
The tests cover the last-observed state through simulated authorities.
A real release fence and mandatory coverage across legacy entry points are open.

## Workspace root recovery

The preparation path authenticates the exact root ELV1 pinned in the supplied
generation, then verifies the root-encrypted ELH1 index. It uses the existing
ERB1 backup format and produces an authorization proof with the exact
generation, root ID and encrypted backup/index SHA-256 values. The caller must
enroll that proof with a separately protected authority. A preparation result
alone grants no recovery authorization.

Restore requires the currently selected generation, a matching authorized
proof and bounded exact encrypted inputs. It authenticates both envelopes,
then checks the proof and generation again before delivery. The prototype
restores from the backup password without the original Windows provider.
The child factor still needs its own credentials. Provider enrollment,
authorization lifecycle, hardware failure and physical device replacement
remain open. An old ERB1 held with its password is an offline copy of root
material. The authority gate cannot erase such a copy.

## Fresh-factor rekey

A replacement factor keeps its ID and kind and generates new random key
material. The ELH1 root rebuild authenticates the old index and encrypts it
with the replacement. A selected ELK1 document authenticates the old chain
and inner EMD2, creates a fresh EMD2 content key and recovery secret and
seals the result with the ordered replacement factors. The caller can
create a fresh ELV1 using the existing vault creation API. The tests check
wrong credentials, unchanged chains, the old recovery secret and byte-for-byte
preservation of source data. The document path uses the existing in-memory
Markdown string API.

Workspace inventory, durable staging of every dependent object, commit and
restart orchestration remain open. Historical ELV1, ELK1 and EMD2 copies
remain usable with historical credentials. These primitives use existing
wire formats. For key lifecycle and backup guidance see
[NIST SP 800-57 Part 1 Rev. 5](https://csrc.nist.gov/pubs/sp/800/57/pt1/r5/final).

## Release boundary

WPF secure `.emd` creation stays disabled. The read-only TPM capability
probe provides no physical CAS or power-loss evidence. Physical NTFS ACL,
disk full, symlink/junction TOCTOU, hostile CAS, cross-user DPAPI, physical
GUI/DPI, printer/PDF and real UYAP UDF open/edit/resave require separate
acceptance. Main and installer publication remain outside this draft.
