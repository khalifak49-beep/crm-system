<#
  CRM System - Phase 1
  File   : Database/RunAll.ps1
  Purpose: Run all database scripts in the correct order.
  Usage  : powershell -ExecutionPolicy Bypass -File .\Database\RunAll.ps1
           powershell -ExecutionPolicy Bypass -File .\Database\RunAll.ps1 -Server ".\SQLEXPRESS"
           powershell -ExecutionPolicy Bypass -File .\Database\RunAll.ps1 -SkipTests
  Requires: sqlcmd (installed with SQL Server / SSMS). Uses Windows authentication.
#>
param(
    [string]$Server = ".",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

$scripts = @(
    "01_CreateDatabase.sql",
    "02_IdentityTables.sql",
    "03_CoreTables.sql",
    "04_Views.sql",
    "05_SeedData.sql"
)
if (-not $SkipTests) { $scripts += "06_TestRelationships.sql" }

foreach ($s in $scripts) {
    $path = Join-Path $here $s
    Write-Host "==> Running $s" -ForegroundColor Cyan
    # -E Windows auth | -C trust server cert | -I QUOTED_IDENTIFIER ON | -b stop on error | -f 65001 UTF-8 (Arabic text)
    & sqlcmd -S $Server -E -C -I -b -f 65001 -W -i $path
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAILED: $s (exit code $LASTEXITCODE)" -ForegroundColor Red
        exit $LASTEXITCODE
    }
}
Write-Host "Database CRMDatabase is ready." -ForegroundColor Green
