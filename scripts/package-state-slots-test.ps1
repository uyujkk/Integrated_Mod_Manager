[CmdletBinding()]
param([switch]$SkipBuild)

# Compatibility entry point; current source is the promoted 4.0-beta.
# Existing R1/R2/R3 packages are not modified by this script.
& (Join-Path $PSScriptRoot 'package-beta.ps1') -SkipBuild:$SkipBuild
