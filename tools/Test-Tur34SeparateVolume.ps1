param([Parameter(Mandatory)][string]$HarnessDirectory)
$ErrorActionPreference = 'Stop'
$root = Join-Path $env:RUNNER_TEMP ('enote34-volume-' + [Guid]::NewGuid().ToString('N'))
$disk = Join-Path $root 'backup.vhdx'
$instructions = Join-Path $root 'diskpart.txt'
$letter = 90..68 | ForEach-Object { [string][char]$_ } |
    Where-Object { !(Test-Path ("${_}:\")) -and !(Get-Volume -DriveLetter $_ -ErrorAction SilentlyContinue) } |
    Select-Object -First 1
if (!$letter) { throw 'No unused backup volume letter.' }
New-Item -ItemType Directory -Path $root | Out-Null
try {
    @(
        "create vdisk file=`"$disk`" maximum=128 type=expandable"
        "select vdisk file=`"$disk`""
        'attach vdisk'
        'create partition primary'
        'format fs=ntfs quick label=ENOTE34'
        "assign letter=$letter"
        'exit'
    ) | Set-Content -LiteralPath $instructions -Encoding ascii
    & diskpart.exe /s $instructions
    if ($LASTEXITCODE -ne 0) { throw 'VHDX provisioning failed.' }
    $volume = Get-Volume -DriveLetter $letter
    if ($volume.FileSystemLabel -ne 'ENOTE34' -or $volume.FileSystem -ne 'NTFS') {
        throw 'Expected the disposable NTFS backup volume.'
    }
    $backup = "${letter}:\snapshot-root"
    New-Item -ItemType Directory -Path $backup | Out-Null
    & dotnet (Join-Path $HarnessDirectory 'Embasic.ENote.Tur28.Tests.dll') tur34-backup (Join-Path $root 'workspace') $backup
    if ($LASTEXITCODE -ne 0) { throw 'Separate NTFS volume backup acceptance failed.' }
} finally {
    if (Test-Path -LiteralPath $disk) {
        @("select vdisk file=`"$disk`"", 'detach vdisk', 'exit') |
            Set-Content -LiteralPath $instructions -Encoding ascii
        & diskpart.exe /s $instructions | Out-Null
    }
    Remove-Item -LiteralPath $root -Recurse -Force
}
