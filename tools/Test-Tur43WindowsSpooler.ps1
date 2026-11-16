$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '../tests/Embasic.ENote.WpfAcceptance'
$driver = Get-PrinterDriver | Where-Object Name -Like 'Microsoft XPS Document Writer*' |
    Sort-Object Name -Descending | Select-Object -First 1
if (-not $driver) {
    throw 'BLOCKED: Microsoft XPS Document Writer driver is absent on this Windows runner.'
}
Write-Host "Using installed Windows print driver: $($driver.Name)."
$root = New-Item -ItemType Directory (Join-Path ([IO.Path]::GetTempPath()) "enote-tur43-$([guid]::NewGuid().ToString('N'))")
try {
    foreach ($scenario in @('portrait-range', 'landscape-current')) {
        $queue = "Embasic ENote TUR43 $($root.Name) $scenario"
        $output = Join-Path $root.FullName "$scenario.oxps"
        try {
            Add-PrinterPort -Name $output
            Add-Printer -Name $queue -DriverName $driver.Name -PortName $output
            dotnet run --project $project --configuration Release --no-build -- --spooler $queue $output $scenario
            if ($LASTEXITCODE -ne 0) { throw "Spooler acceptance failed for $scenario." }
        } finally {
            if (Get-Printer -Name $queue -ErrorAction SilentlyContinue) {
                Get-PrintJob -PrinterName $queue | Remove-PrintJob
                Remove-Printer -Name $queue
            }
            if (Get-PrinterPort -Name $output -ErrorAction SilentlyContinue) {
                Remove-PrinterPort -Name $output
            }
        }
    }
} finally {
    Remove-Item -LiteralPath $root.FullName -Recurse -Force
}
