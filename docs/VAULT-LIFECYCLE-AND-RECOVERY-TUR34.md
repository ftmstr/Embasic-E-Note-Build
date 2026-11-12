# TUR 34 · Vault lifecycle, replacement and recovery

## W007 / SEC-EMD-05

The hosted Windows lifecycle job registers a disposable LocalService endpoint with the persisted CNG key, service SID, ACL-restricted pipe and vault publication store. It observes the initial generation, issues native STOP and verifies that cancellation reaches a waiting request. A repeat STOP during the held STOP_PENDING state must return 1061. It then starts the same approved epoch, checks the original selection and repeats clean shutdown. This does not exercise an installed release service.

A killed administrator fixture leaves a flushed partial envelope under the pending name. The reader must refuse the missing active envelope and the new service must refuse the old role. After verified activation the stale service epoch is refused. Restart of the selected epoch observes the persisted generation. The prior distinct-logon and preopened-handle controls remain in the main regression job.

## W012

On Windows the live replacement holds directory handles without delete sharing from the root to the destination parent. It checks the old and staged file identities and ciphertext while both handles are open and observes the selected generation again. The ACL-preserving ReplaceFile operation still consumes paths and requires those file handles to close first. A hostile file-entry substitution in that final interval remains possible. The installed identity and ciphertext are checked before success. A Windows replacement error or failed postcondition is reported as an uncertain outcome.

Separate-volume backup acceptance verifies different Windows volume serials for the selected catalog and its copy on a disposable NTFS VHDX. The test simulates an unavailable backup name, changes one ciphertext byte, and observes a selection change before publishing the verified restore. It requires no catalog publication in those cases and a successful verified retry. The test does not model loss of a physical device, independent storage ownership or power loss.

## EPF1

The remote proof verifier requires the exact externally selected workspace, revision and digest. It checks a fresh nonce with the operator key authorized for that revision. Disposing the active operator private key preserves offline verification of already signed evidence. The previous operator and an unapproved replacement cannot authorize the next rotation. Recovery requires an independently trusted re-enrollment decision or a separately protected key backup.

An independently administered second machine or physical key device remains a deployment exercise. Both the expected floor and rotation schedule must be retained outside the rollback domain. If all trusted inputs roll back together, an old but valid proof can again be accepted. Anti-rollback, hardware CAS, old-copy revocation and atomic delivery remain open.
