param([Parameter(Mandatory)][string]$HarnessDirectory, [switch]$Isolated, [switch]$NativeStop)

$ErrorActionPreference = 'Stop'
if ($Isolated -and $NativeStop) { throw 'Select one fixture mode.' }
$target = 'EmbasicTur32-' + [Guid]::NewGuid().ToString('N')
$peer = "$target-peer"
$stopProbe = "$target-stop"
$base = Join-Path $env:ProgramData $target
$control = Join-Path $base 'control'
$management = Join-Path $base 'management'
$runtime = Join-Path $base 'runtime'
$dotnet = (Get-Command dotnet -CommandType Application).Source
$harness = Join-Path $runtime 'Embasic.ENote.Tur28.Tests.dll'
$created = [System.Collections.Generic.List[string]]::new()

function Get-ServiceSid([string]$Name) {
    $text = (& sc.exe showsid $Name | Out-String)
    if ($LASTEXITCODE -ne 0 -or $text -notmatch 'S-1-5-80-(?:[0-9]+-){4}[0-9]+') { throw 'Service SID unavailable.' }
    return $Matches[0]
}

function Set-DirectoryAcl([string]$Path, [string]$ServiceRights) {
    $acl = Get-Acl -LiteralPath $Path
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($rule in @($acl.Access)) { $acl.RemoveAccessRuleSpecific($rule) }
    $admin = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    foreach ($sid in @('S-1-5-18','S-1-5-32-544',$admin,$targetSid,$peerSid,$stopSid)) {
        $rights = if ($sid -in @($targetSid,$peerSid,$stopSid)) { $ServiceRights } else { 'FullControl' }
        $identity = [System.Security.Principal.SecurityIdentifier]::new($sid)
        $acl.AddAccessRule([System.Security.AccessControl.FileSystemAccessRule]::new(
            $identity, $rights, 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
    }
    Set-Acl -LiteralPath $Path -AclObject $acl
}

function Start-Probe([string]$Name, [string]$Mode, [string]$Sid) {
    $binary = '"{0}" "{1}" {2} "{3}" "{4}" "{5}" "{6}"' -f $dotnet,$harness,$Mode,$control,$management,$Name,$Sid
    $account = 'NT AUTHORITY\LocalService'
    if ($Isolated -or $NativeStop) {
        $binary += ' "' + $peerSid + '"'
        if ($Name -eq $peer) { $account = 'NT AUTHORITY\NetworkService' }
    }
    & sc.exe create $Name binPath= $binary start= demand obj= $account
    if ($LASTEXITCODE -ne 0) { throw 'Probe registration failed.' }
    $created.Add($Name)
    & sc.exe sidtype $Name unrestricted
    if ($LASTEXITCODE -ne 0) { throw 'Probe SID configuration failed.' }
    & sc.exe start $Name
    if ($LASTEXITCODE -ne 0) { throw 'Probe startup failed.' }
}

function Wait-Result([string]$Name, [string]$Expected = 'PASS') {
    $path = Join-Path $control "$Name.result"
    $end = [DateTime]::UtcNow.AddSeconds(40)
    while (!(Test-Path -LiteralPath $path) -and [DateTime]::UtcNow -lt $end) { Start-Sleep -Milliseconds 100 }
    $result = Get-Content -LiteralPath $path -Raw
    if ($result.Trim() -ne $Expected) { throw "$Name failed: $result" }
    Write-Output "$Name PASS"
}

function Wait-Signal([string]$Name) {
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    $path = Join-Path $control $Name
    while (!(Test-Path -LiteralPath $path)) {
        if ([DateTime]::UtcNow -ge $deadline) { throw "Missing signal $Name." }
        Start-Sleep -Milliseconds 50
    }
}

try {
    $targetSid = Get-ServiceSid $target
    $peerSid = Get-ServiceSid $peer
    $stopSid = Get-ServiceSid $stopProbe
    New-Item -ItemType Directory -Path $base | Out-Null
    Set-DirectoryAcl $base 'ReadAndExecute'
    New-Item -ItemType Directory -Path $control,$management,$runtime | Out-Null
    Set-DirectoryAcl $control 'Modify'
    Set-DirectoryAcl $management 'ReadAndExecute'
    Copy-Item -Path (Join-Path $HarnessDirectory '*') -Destination $runtime -Recurse
    & $dotnet $harness 'tur32-win-setup' $control $management
    if ($LASTEXITCODE -ne 0) { throw 'Operator floor setup failed.' }
    if ($Isolated) {
        Start-Probe $target 'tur33-win-target' $targetSid
        Start-Probe $peer 'tur33-win-peer' $peerSid
        Wait-Result 'tur33-win-peer'
        New-Item -ItemType File -Path (Join-Path $control 'peer-finished') | Out-Null
        Wait-Result 'tur33-win-target' 'PASS child shutdown'
        (Get-Service -Name $peer).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(20))
        (Get-Service -Name $target).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(20))
        Write-Output '3/3 Tur33 separate logon, first handles and child token checks passed.'
    } elseif ($NativeStop) {
        Start-Probe $stopProbe 'tur33-win-stop' $stopSid
        Wait-Signal 'native-ready'
        (Get-Service -Name $stopProbe).WaitForStatus('Running', [TimeSpan]::FromSeconds(20))
        & sc.exe stop $stopProbe
        if ($LASTEXITCODE -ne 0) { throw 'Native SCM STOP failed.' }
        Wait-Signal 'native-received'
        if ((Get-Service -Name $stopProbe).Status -ne 'StopPending') { throw 'SCM did not report STOP_PENDING.' }
        New-Item -ItemType File -Path (Join-Path $control 'native-release') | Out-Null
        (Get-Service -Name $stopProbe).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(20))
        Wait-Result 'tur33-win-stop' 'PASS SCM STOP'
        Write-Output '1/1 Tur33 native SCM STOP lifecycle checks passed.'
    } else {
        Start-Probe $target 'tur32-win-target' $targetSid
        Start-Probe $peer 'tur32-win-peer' $peerSid
        Wait-Result 'tur32-win-peer'
        Wait-Result 'tur32-win-target'
        Write-Output '4/4 Tur32 preopened process, thread, token and operator floor ACL checks passed.'
    }
}
catch {
    foreach ($name in $created) { & sc.exe queryex $name }
    foreach ($entry in @(Get-ChildItem -LiteralPath $control -Filter '*.result' -ErrorAction SilentlyContinue)) {
        Write-Output "$($entry.Name) $(Get-Content -LiteralPath $entry.FullName -Raw)"
    }
    throw
}
finally {
    if (($Isolated -or $NativeStop) -and (Test-Path -LiteralPath $control)) {
        foreach ($name in @('peer-finished','native-release')) {
            New-Item -ItemType File -Path (Join-Path $control $name) -Force | Out-Null
        }
    }
    foreach ($name in $created) {
        $service = Get-Service -Name $name -ErrorAction SilentlyContinue
        if ($service -and $service.Status -ne 'Stopped') {
            & sc.exe stop $name | Out-Null
            try { $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(10)) }
            catch { Write-Warning "Fixture service $name did not stop during cleanup." }
        }
        & sc.exe delete $name | Out-Null
    }
    if (Test-Path -LiteralPath $base) { Remove-Item -LiteralPath $base -Recurse -Force -ErrorAction Continue }
}
