# TUR 33 · Vault isolation and rotation evidence

## W007 / SEC-EMD-05

The hosted Windows fixture uses the Service Control Manager to start a LocalService target and a NetworkService probe. A child inherits the target logon and service SID. The peer checks first acquisition of process memory/duplication and thread query handles, then attempts token access with deliberately granted process query access. The TUR 32 experiment remains a separate negative control. Preopened process, thread and token handles still work after the DACL is tightened.

The Windows software KSP fixture provisions separate reader and publisher recipient keys per epoch. It checks old-recipient rejection, stale-envelope rejection and the standard user's write denial on the fixture's selection marker. Before selection the fixture permits fallback to the approved prior epoch. After selection the previous epoch must fail the custody policy check before pipe startup. The selected epoch is a same-host administrative input.

A separate CI job exercises SCM STOP and STOP_PENDING with the default process DACL and the extended control handler. The cross-logon fixture verifies child shutdown through its test control signal. Native STOP returned 1061 in earlier runs with both the test-only and default process DACL. The lifecycle job reports its own acceptance result. The production service lifecycle requires separate acceptance.

## W012

On Windows, live sync records the new staging file's volume and file index. Before replacement it verifies the staging name, file identity, link count and ciphertext digest through an exclusive no-follow handle and checks the selected generation again. Recovery marks an inspected file for deletion through that handle. It considers names authenticated for the selected generation and its verified direct parent. Older generations, unrelated files and unverified entries remain in place.

Windows replacement still closes the verification handle before invoking the path-based operation that preserves destination permissions. A hostile namespace substitution in that final interval remains possible. The fixtures cover selected-generation changes during copying, same-byte identity replacement, hard links and reparse junction paths. The non-Windows implementation retains a cooperating writer model. The backup fixture uses a separate root and verifies it after selected-object loss in the live root. It exercises neither a separate device nor physical failure.

## EPS1 / EPF1

An operator key transition binds both keys to the workspace and first revision and requires both signatures. A domain-separated nonce challenge binds the complete floor to a proof. A separate local process answers the test challenge. The test retains an independently supplied minimum revision and then demonstrates that rolling back that input along with the record and key schedule allows an old view.

A second-machine design needs a provisioned operator identity, authenticated challenge transport and a durable floor whose administration is independent of the workstation. Binding the signing key to hardware would protect that key under the selected hardware policy. A persistent minimum and independently authenticated mutation are separate requirements. Windows TPM Base Services can block commands according to platform policy, so a usable hardware counter or CAS requires a separate provisioning and destructive recovery exercise.

Independent physical evidence, anti-rollback, hardware CAS, old-copy revocation and atomic key delivery remain open. The WPF secure-note entry point stays disabled.
