param([string]$WpfUdfPath)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$root = Join-Path $env:RUNNER_TEMP "enote-uyap-$([guid]::NewGuid().ToString('N'))"
$null = New-Item -ItemType Directory $root
$installer = Join-Path $root 'UKI_V5.4.20.msi'
$editor = Join-Path $root 'editor'
$log = Join-Path $root 'extract.log'
$profile = Join-Path $root 'profile'
$null = New-Item -ItemType Directory $profile

Invoke-WebRequest 'https://rayp.adalet.gov.tr/resimler/2/dosya/UKI_V5.4.20.msi' -OutFile $installer -TimeoutSec 180
$expected = 'ED567324C80C45847FB28F4BAB50E4DB65F2487E51F221D0ECF29A46744B5694'
if ((Get-FileHash $installer -Algorithm SHA256).Hash -cne $expected) { throw 'The official package SHA-256 changed.' }
$setup = Start-Process msiexec.exe -ArgumentList @('/a', "`"$installer`"", "TARGETDIR=`"$editor`"", '/qn', '/L*v', "`"$log`"") -Wait -PassThru
if ($setup.ExitCode -ne 0) {
    Get-Content $log -Tail 35
    throw "Administrative extraction failed with code $($setup.ExitCode)."
}
$editorJar = @(Get-ChildItem $editor -Filter 'editor_lib.jar' -Recurse -File)
if ($editorJar.Count -ne 1) { throw 'Expected one official editor library.' }
$app = $editorJar[0].Directory.FullName
$javac = Join-Path $env:JAVA_HOME 'bin/javac.exe'
$java = Join-Path $env:JAVA_HOME 'bin/java.exe'
& $javac -d $root ./tools/Tur37UyapEditor.java
if ($LASTEXITCODE -ne 0) { throw 'UYAP acceptance harness compile failed.' }
function Invoke-EditorRoundTrip([string]$path) {
    Push-Location $app
    try {
        & $java "-Duser.home=$profile" '-Dfile.encoding=UTF-8' '-cp' "$root;$app\*" Tur37UyapEditor edit $path
        if ($LASTEXITCODE -ne 0) { throw 'Windows editor open/edit/save failed.' }
        & $java "-Duser.home=$profile" '-Dfile.encoding=UTF-8' '-cp' "$root;$app\*" Tur37UyapEditor reopen $path
        if ($LASTEXITCODE -ne 0) { throw 'Windows editor reopen failed.' }
    } finally { Pop-Location }
}

function Read-ContentXml([string]$path) {
    $archive = [IO.Compression.ZipFile]::OpenRead($path)
    try {
        if ($archive.Entries.Count -ne 1 -or $archive.Entries[0].FullName -cne 'content.xml') {
            throw 'The unsigned UDF should contain only content.xml.'
        }
        $reader = [IO.StreamReader]::new($archive.Entries[0].Open())
        try { return [xml]$reader.ReadToEnd() } finally { $reader.Dispose() }
    } finally { $archive.Dispose() }
}
function Assert-PageFormat($before, $after) {
    foreach ($attribute in $before.SelectSingleNode('/template/properties/pageFormat').Attributes) {
        if ($after.SelectSingleNode('/template/properties/pageFormat').GetAttribute($attribute.Name) -cne $attribute.Value) {
            throw "Page property changed: $($attribute.Name)."
        }
    }
}

function Assert-LiteralCopy([string]$original, [string]$copy, [string]$expectedPool) {
    $before = Read-ContentXml $original
    $after = Read-ContentXml $copy
    $savedPool = $after.SelectSingleNode('/template/content').InnerText
    if ($savedPool -cne $expectedPool -and $savedPool -cne ($expectedPool + "`n")) {
        throw 'Literal Markdown text changed beyond the edit and optional final newline.'
    }
    if (@($after.SelectNodes('/template/elements/paragraph')).Count -ne 4 -or
        $after.SelectSingleNode('/template/elements/table')) { throw 'The Markdown copy structure changed.' }
    Assert-PageFormat $before $after
}

if ($WpfUdfPath) {
    $original = (Resolve-Path -LiteralPath $WpfUdfPath).ProviderPath
    $copy = Join-Path $root 'wpf-roundtrip.udf'
    $originalHash = (Get-FileHash -LiteralPath $original).Hash
    $before = Read-ContentXml $original
    $pool = "# EMBASIC E-NOTE TUR37`n`nKaydedilmemiş **Çığ ve İğdır**`nSON PARAGRAF TUR37`n"
    if ($before.SelectSingleNode('/template/content').InnerText -cne $pool) {
        throw 'Expected the exact synthetic WPF buffer.'
    }
    $page = $before.SelectSingleNode('/template/properties/pageFormat')
    $geometry = @{ mediaSizeName = '1'; paperOrientation = '1'; leftMargin = '48.19'
                   rightMargin = '65.20'; topMargin = '53.86'; bottomMargin = '76.54' }
    foreach ($name in $geometry.Keys) {
        if ($page.GetAttribute($name) -cne $geometry[$name]) { throw "Wrong WPF page property: $name." }
    }
    Copy-Item -LiteralPath $original -Destination $copy
    Invoke-EditorRoundTrip $copy
    $expectedPool = $pool.Replace('EMBASIC E-NOTE TUR37', 'EMBASIC E-NOTE TUR37 TUR37-EDIT')
    Assert-LiteralCopy $original $copy $expectedPool
    if ((Get-FileHash -LiteralPath $original).Hash -cne $originalHash) { throw 'The WPF output changed.' }
    Write-Host "WPF UDF input SHA-256 $originalHash"
    Write-Host '1/1 Tur40 WPF-produced A4 UDF through official Windows UYAP 5.4.20 checks passed.'
    return
}

$sample = Join-Path $root 'original.udf'
$copy = Join-Path $root 'roundtrip.udf'
dotnet run --project tests/Embasic.ENote.UdfWriter.Tests --configuration Release -- --rich-sample $sample
if ($LASTEXITCODE -ne 0) { throw 'Synthetic sample generation failed.' }
Copy-Item $sample $copy
$originalHash = (Get-FileHash $sample).Hash
Invoke-EditorRoundTrip $copy
$before = Read-ContentXml $sample
$after = Read-ContentXml $copy
$oldPool = $before.SelectSingleNode('/template/content').InnerText
$newPool = $after.SelectSingleNode('/template/content').InnerText
$expectedPool = $oldPool.Replace('EMBASIC E-NOTE TUR37', 'EMBASIC E-NOTE TUR37 TUR37-EDIT')
if ($newPool -cne $expectedPool -and $newPool -cne ($expectedPool + "`n")) {
    throw 'Saved text differs beyond the intended edit and optional final newline.'
}
if (@($after.SelectNodes('/template/elements/table/row/cell')).Count -ne 4) { throw 'Four cells were not retained.' }
$oldRuns = @($before.SelectNodes('/template/elements//content'))
$newRuns = @($after.SelectNodes('/template/elements//content'))
if ($oldRuns.Count -ne $newRuns.Count) { throw 'The paragraph run count changed.' }
for ($i = 0; $i -lt $oldRuns.Count; $i++) {
    $oldText = $oldPool.Substring([int]$oldRuns[$i].startOffset, [int]$oldRuns[$i].length)
    $newText = $newPool.Substring([int]$newRuns[$i].startOffset, [int]$newRuns[$i].length)
    if ($i -eq 0) { $oldText = $oldText.Replace('EMBASIC E-NOTE TUR37', 'EMBASIC E-NOTE TUR37 TUR37-EDIT') }
    if ($oldText -cne $newText) { throw "Paragraph run changed at $i." }
    foreach ($name in @('family', 'size', 'bold')) {
        if ($oldRuns[$i].GetAttribute($name) -cne $newRuns[$i].GetAttribute($name)) { throw "Run attribute changed: $name." }
    }
}
Assert-PageFormat $before $after
if ((Get-FileHash $sample).Hash -cne $originalHash) { throw 'Original sample changed.' }
Write-Host '1/1 Tur37 official Windows UYAP 5.4.20 edit save reopen and compare checks passed.'

$markdown = Join-Path $root 'markdown-original.udf'
$markdownCopy = Join-Path $root 'markdown-roundtrip.udf'
dotnet run --project tests/Embasic.ENote.UdfWriter.Tests --configuration Release --no-build -- --markdown-sample $markdown
if ($LASTEXITCODE -ne 0) { throw 'Synthetic Markdown copy generation failed.' }
Copy-Item $markdown $markdownCopy
$originalHash = (Get-FileHash $markdown).Hash
Invoke-EditorRoundTrip $markdownCopy
$before = Read-ContentXml $markdown
$oldPool = $before.SelectSingleNode('/template/content').InnerText
$expectedPool = $oldPool.Replace('EMBASIC E-NOTE TUR37', 'EMBASIC E-NOTE TUR37 TUR37-EDIT')
Assert-LiteralCopy $markdown $markdownCopy $expectedPool
if ((Get-FileHash $markdown).Hash -cne $originalHash) { throw 'The original Markdown UDF changed.' }
Write-Host '1/1 Tur38 official Windows UYAP literal Markdown copy checks passed.'
