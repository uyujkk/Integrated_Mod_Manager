[CmdletBinding()]
param(
    [string]$Version = '4.5.2-preview.1',
    [string]$BaseTag = 'v4.5.1',
    [string]$VerificationDirectory = 'artifacts\verification\v4.5.2-preview.1',
    [switch]$ForGitHubPrerelease,
    [string]$CiUrl = ''
)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'package-prerelease.ps1') @PSBoundParameters
