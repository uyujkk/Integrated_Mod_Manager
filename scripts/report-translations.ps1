[CmdletBinding()]
param()

# Lists how much of the English UI text each language file translates, as a Markdown table. In GitHub
# Actions the table goes to the job summary; elsewhere it is printed. Keys a file leaves out show English.
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$strings = Join-Path $root 'src\WinUI3\Strings'

function Read-Texts([string]$path) {
    $texts = @{}
    $json = [IO.File]::ReadAllText($path, [Text.Encoding]::UTF8) | ConvertFrom-Json
    foreach ($property in $json.PSObject.Properties) { $texts[$property.Name] = [string]$property.Value }
    return $texts
}

$english = Read-Texts (Join-Path $strings 'en-US.json')
$rows = foreach ($file in Get-ChildItem -LiteralPath $strings -Filter '*.json' | Sort-Object Name) {
    $language = $file.BaseName
    $texts = Read-Texts $file.FullName
    $translated = @($english.Keys | Where-Object { $texts.ContainsKey($_) }).Count
    $sameAsEnglish = if ($language -eq 'en-US') { 0 } else {
        @($english.Keys | Where-Object { $texts.ContainsKey($_) -and $texts[$_] -ceq $english[$_] }).Count
    }
    $percent = [Math]::Round(100.0 * $translated / [Math]::Max(1, $english.Count), 1)
    "| $language | $($texts['Language.Name']) | $translated / $($english.Count) ($percent%) | $sameAsEnglish |"
}

$table = @(
    '### UI text by language'
    ''
    '| File | Name | Keys present | Same as English |'
    '| --- | --- | --- | --- |'
) + $rows + @('', 'Keys a language leaves out show the English text. "Same as English" counts texts copied over unchanged, which may still need translating.')

if ($env:GITHUB_STEP_SUMMARY) {
    $table | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
}
$table
