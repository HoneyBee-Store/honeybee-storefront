<#
    Recovers a forgotten PostgreSQL superuser password, and sets up the
    development database while it is at it.

    The password itself cannot be read back - PostgreSQL stores only a hash.
    The standard recovery is to let the server trust local connections for a
    few seconds, set a new password, then put authentication back.

    Run from an ELEVATED PowerShell (right-click -> Run as administrator):

        .\tools\reset-postgres-password.ps1

    The trust window lasts only as long as the two commands in the middle, and
    pg_hba.conf is restored in a finally block so it goes back even if
    something fails partway.
#>

[CmdletBinding()]
param(
    [string] $PgRoot = "C:\Program Files\PostgreSQL\18",
    [string] $ServiceName = "postgresql-x64-18"
)

$ErrorActionPreference = "Stop"

# ---- checks -------------------------------------------------------------

$identity  = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)

if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "This needs to run as administrator." -ForegroundColor Red
    Write-Host "Close this window, right-click PowerShell, choose 'Run as administrator', then run it again."
    exit 1
}

$dataDir = Join-Path $PgRoot "data"
$hba     = Join-Path $dataDir "pg_hba.conf"
$psql    = Join-Path $PgRoot "bin\psql.exe"

foreach ($path in @($hba, $psql)) {
    if (-not (Test-Path $path)) {
        Write-Host "Not found: $path" -ForegroundColor Red
        Write-Host "If PostgreSQL is installed somewhere else, pass -PgRoot with the right path."
        exit 1
    }
}

# ---- the new password ---------------------------------------------------

Write-Host ""
Write-Host "New password for the postgres superuser." -ForegroundColor Cyan
Write-Host "Write it down somewhere before you continue."
$secure = Read-Host "New password" -AsSecureString
$bstr   = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
$plain  = [Runtime.InteropServices.Marshal]::PtrToStringAuto($bstr)
[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)

if ([string]::IsNullOrWhiteSpace($plain)) {
    Write-Host "Nothing entered. Stopping." -ForegroundColor Red
    exit 1
}

# Escaped for the SQL string literal below.
$sqlSafe = $plain.Replace("'", "''")

$backup = $hba + ".before-reset"

try {
    # ---- open a trust window -------------------------------------------

    Copy-Item $hba $backup -Force
    Write-Host "Backed up pg_hba.conf to $backup"

    $lines = Get-Content $hba
    $patched = $lines | ForEach-Object {
        if ($_ -match '^\s*#' -or $_ -match '^\s*$') { $_ }
        else { $_ -replace '(scram-sha-256|md5|password)\s*$', 'trust' }
    }
    Set-Content $hba $patched -Encoding ascii

    Restart-Service $ServiceName -Force
    Start-Sleep -Seconds 3
    Write-Host "Authentication temporarily relaxed." -ForegroundColor Yellow

    # ---- set the password and build the dev database --------------------

    $alter = "ALTER USER postgres WITH PASSWORD '$sqlSafe';"
    & $psql -U postgres -h localhost -d postgres -v ON_ERROR_STOP=1 -c $alter
    if ($LASTEXITCODE -ne 0) { throw "Could not set the postgres password." }
    Write-Host "postgres password set." -ForegroundColor Green

    $roleQuery = "SELECT 1 FROM pg_roles WHERE rolname='honeybee';"
    $roleExists = & $psql -U postgres -h localhost -d postgres -tAc $roleQuery

    if ($roleExists -ne "1") {
        $script = Join-Path $PSScriptRoot "create-dev-database.sql"
        if (Test-Path $script) {
            & $psql -U postgres -h localhost -d postgres -v ON_ERROR_STOP=1 -f $script
            if ($LASTEXITCODE -eq 0) { Write-Host "Development database created." -ForegroundColor Green }
            else { Write-Host "The database script reported a problem - check the output above." -ForegroundColor Yellow }
        }
        else {
            Write-Host "create-dev-database.sql not found next to this script; skipping." -ForegroundColor Yellow
        }
    }
    else {
        Write-Host "The honeybee role already exists; leaving it alone."
    }
}
finally {
    # ---- always close the trust window ----------------------------------

    if (Test-Path $backup) {
        Copy-Item $backup $hba -Force
        Remove-Item $backup -Force
        Restart-Service $ServiceName -Force
        Start-Sleep -Seconds 3
        Write-Host "Authentication restored." -ForegroundColor Green
    }

    $plain = $null
    [GC]::Collect()
}

Write-Host ""
Write-Host "Done. The app connects as the honeybee role, so it does not need the" -ForegroundColor Cyan
Write-Host "postgres password at all - keep the new one for pgAdmin and admin tasks."
