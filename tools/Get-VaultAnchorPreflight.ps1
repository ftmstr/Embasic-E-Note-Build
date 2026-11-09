[CmdletBinding()]
param()

$result = [ordered]@{
    probe = 'READ_ONLY_TPM_CAPABILITY'
    status = 'NOT_WINDOWS'
    tpm_present = $null
    tpm_ready = $null
    independent_anchor_cas = 'NOT_RUN'
}

if ($env:OS -eq 'Windows_NT') {
    if ($null -eq (Get-Command Get-Tpm -CommandType Cmdlet -ErrorAction SilentlyContinue)) {
        $result.status = 'CMDLET_UNAVAILABLE'
    }
    else {
        try {
            $tpm = Get-Tpm -ErrorAction Stop
            $result.tpm_present = [bool]$tpm.TpmPresent
            $result.tpm_ready = [bool]$tpm.TpmReady
            $result.status = 'QUERIED'
        }
        catch {
            $result.status = 'QUERY_FAILED'
        }
    }
}

[pscustomobject]$result | ConvertTo-Json -Compress
