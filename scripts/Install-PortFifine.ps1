#Requires -Version 5.1
<#
.SYNOPSIS
    Installs Portfifine resources into Fifine Control Deck / StreamDock.

.DESCRIPTION
    Verifies the Fifine app-data root, migrates compatible Elgato plugins and
    icon packs (when present and not skipped via -BundledOnly), installs
    .streamDeckIconPack files found beside the repository or on the Desktop,
    installs bundled .sdPlugin plugins (managed mirror), clears the Fifine store
    cache, and restarts Fifine Control Deck. No administrator privileges are
    required to run this script.
#>

param(
    [string]$RepositoryRoot,
    [switch]$NoRestart,
    [switch]$BundledOnly,
    [switch]$NoDesktopIconPacks,
    [switch]$SelfTest,
    [switch]$NoPause
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$failed = $false
$restarted = $false

function Test-ManifestValid {
    param([string]$Path)
    try {
        if (-not (Test-Path -LiteralPath $Path)) { return $false }
        $raw = [System.IO.File]::ReadAllText($Path)
        $trimmed = $raw.TrimStart()
        if ($trimmed.Length -eq 0 -or -not $trimmed.StartsWith('{')) { return $false }
        $null = $raw | ConvertFrom-Json -ErrorAction Stop
        return $true
    } catch {
        return $false
    }
}

function Format-RoboPath {
    param([string]$Path)
    # Wrap in double quotes so robocopy treats the whole value as a single
    # path even when it contains spaces. A trailing backslash must be doubled
    # so the Windows command-line parser does not treat it as escaping the
    # closing quote (otherwise a path like "C:\foo\" breaks parsing).
    $p = $Path
    if ($p.EndsWith('\')) { $p = $p + '\' }
    return '"{0}"' -f $p
}

function Invoke-RoboCopy {
    param([string]$Source, [string]$Destination, [string[]]$ExtraArgs)
    $argList = @((Format-RoboPath $Source), (Format-RoboPath $Destination)) + $ExtraArgs
    $proc = Start-Process -FilePath 'robocopy.exe' -ArgumentList $argList -NoNewWindow -Wait -PassThru
    $code = $proc.ExitCode
    return [PSCustomObject]@{ ExitCode = $code; Success = ($code -ge 0 -and $code -le 7) }
}

if ($SelfTest) {
    # Deterministic validation of robocopy path quoting using paths that
    # contain spaces. Does not touch Fifine, deploy plugins, or restart.
    $base = Join-Path $env:TEMP 'Portfifine RoboCopy Test'
    $src = Join-Path $base 'Source With Spaces'
    $dst = Join-Path $base 'Destination With Spaces'
    try {
        if (Test-Path -LiteralPath $base) { Remove-Item -LiteralPath $base -Recurse -Force }
        New-Item -ItemType Directory -Path $src -Force | Out-Null
        New-Item -ItemType Directory -Path $dst -Force | Out-Null
        $marker = Join-Path $src 'marker.txt'
        Set-Content -LiteralPath $marker -Value 'robocopy spaces test' -Encoding ASCII
        $rc = Invoke-RoboCopy -Source $src -Destination $dst -ExtraArgs @('/R:1', '/W:1', '/NFL', '/NDL', '/NJH', '/NJS', '/NP')
        $copied = Test-Path -LiteralPath (Join-Path $dst 'marker.txt')
        if ($rc.Success -and $copied) {
            Write-Host "OK: robocopy copiou arquivos com espacos no caminho (codigo $($rc.ExitCode))." -ForegroundColor Green
            exit 0
        }
        Write-Host "FALHA: robocopy com espacos no caminho (codigo $($rc.ExitCode), copiado=$copied)." -ForegroundColor Red
        exit 1
    } finally {
        if (Test-Path -LiteralPath $base) { Remove-Item -LiteralPath $base -Recurse -Force -ErrorAction SilentlyContinue }
    }
}

# Resolve repository root (defaults to the parent of this script's folder).
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = Split-Path -Parent $PSScriptRoot
}
$RepositoryRoot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($RepositoryRoot)

Write-Host '--------------------------------------------------' -ForegroundColor DarkGray
Write-Host ' Portfifine - Instalador para Fifine Control Deck' -ForegroundColor Cyan
Write-Host '--------------------------------------------------' -ForegroundColor DarkGray
Write-Host " Repositorio : $RepositoryRoot" -ForegroundColor DarkGray
Write-Host " Opcoes      : BundledOnly=$BundledOnly NoRestart=$NoRestart NoDesktopIconPacks=$NoDesktopIconPacks" -ForegroundColor DarkGray

# Step 1 - verify Fifine root and ensure plugins/icons folders exist.
Write-Host ''
Write-Host '[1/6] Verificando pasta do Fifine...'
$fifineRoot = Join-Path $env:APPDATA 'HotSpot\StreamDock'
if (-not (Test-Path -LiteralPath $fifineRoot)) {
    Write-Host "  ERRO: pasta do Fifine nao encontrada em: $fifineRoot" -ForegroundColor Red
    Write-Host "  Instale e abra o Fifine Control Deck ao menos uma vez e tente novamente." -ForegroundColor Red
    exit 1
}
$fifinePlugins = Join-Path $fifineRoot 'plugins'
$fifineIcons = Join-Path $fifineRoot 'icons'
foreach ($dir in @($fifinePlugins, $fifineIcons)) {
    if (-not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        Write-Host "  Pasta criada: $dir" -ForegroundColor DarkGray
    }
}
Write-Host "  OK: Fifine encontrado em $fifineRoot" -ForegroundColor Green

# Steps 2-3 - Elgato migration (skipped with -BundledOnly).
if (-not $BundledOnly) {
    Write-Host ''
    Write-Host '[2/6] Migrando plugins compativeis do Elgato...'
    $elgatoPlugins = Join-Path $env:APPDATA 'Elgato\StreamDeck\Plugins'
    if (Test-Path -LiteralPath $elgatoPlugins) {
        $elgatoDirs = @(Get-ChildItem -LiteralPath $elgatoPlugins -Directory -ErrorAction SilentlyContinue)
        if ($elgatoDirs.Count -eq 0) {
            Write-Host '  Nenhum plugin do Elgato encontrado; etapa ignorada.' -ForegroundColor DarkGray
        }
        foreach ($src in $elgatoDirs) {
            $manifest = Join-Path $src.FullName 'manifest.json'
            if (-not (Test-Path -LiteralPath $manifest)) {
                Write-Host "  AVISO: ignorado $($src.Name): manifest.json ausente." -ForegroundColor Yellow
                continue
            }
            if (-not (Test-ManifestValid -Path $manifest)) {
                Write-Host "  AVISO: ignorado $($src.Name): manifest.json invalido ou criptografado." -ForegroundColor Yellow
                continue
            }
            $dest = Join-Path $fifinePlugins $src.Name
            $rc = Invoke-RoboCopy -Source $src.FullName -Destination $dest -ExtraArgs @('/E', '/R:1', '/W:1', '/NFL', '/NDL', '/NJH', '/NJS', '/NP')
            if ($rc.Success) {
                Write-Host "  OK: $($src.Name) migrado." -ForegroundColor Green
            } else {
                Write-Host "  AVISO: falha ao migrar $($src.Name) (robocopy codigo $($rc.ExitCode))." -ForegroundColor Yellow
                $failed = $true
            }
        }
    } else {
        Write-Host '  Pasta de plugins do Elgato nao encontrada; etapa ignorada.' -ForegroundColor DarkGray
    }

    Write-Host ''
    Write-Host '[3/6] Migrando pacotes de icones do Elgato...'
    $elgatoIconPacks = Join-Path $env:APPDATA 'Elgato\StreamDeck\IconPacks'
    if (Test-Path -LiteralPath $elgatoIconPacks) {
        $rc = Invoke-RoboCopy -Source $elgatoIconPacks -Destination $fifineIcons -ExtraArgs @('/E', '/R:1', '/W:1', '/NFL', '/NDL', '/NJH', '/NJS', '/NP')
        if ($rc.Success) {
            Write-Host '  OK: pacotes de icones migrados.' -ForegroundColor Green
        } else {
            Write-Host "  AVISO: falha ao migrar pacotes de icones (robocopy codigo $($rc.ExitCode))." -ForegroundColor Yellow
            $failed = $true
        }
    } else {
        Write-Host '  Pasta IconPacks do Elgato nao encontrada; etapa ignorada.' -ForegroundColor DarkGray
    }
} else {
    Write-Host ''
    Write-Host '[2/6] Migrando plugins compativeis do Elgato... ignorado (BundledOnly).' -ForegroundColor DarkGray
    Write-Host ''
    Write-Host '[3/6] Migrando pacotes de icones do Elgato... ignorado (BundledOnly).' -ForegroundColor DarkGray
}

# Step 4 - .streamDeckIconPack installation.
Write-Host ''
Write-Host '[4/6] Instalando pacotes .streamDeckIconPack...'
$searchPaths = New-Object System.Collections.Generic.List[string]
$searchPaths.Add($RepositoryRoot)
if (-not $NoDesktopIconPacks) {
    $desktop = [Environment]::GetFolderPath('Desktop')
    if (-not [string]::IsNullOrWhiteSpace($desktop)) { $searchPaths.Add($desktop) }
}
$packs = New-Object System.Collections.Generic.List[System.IO.FileInfo]
foreach ($sp in $searchPaths) {
    if (Test-Path -LiteralPath $sp) {
        $found = Get-ChildItem -LiteralPath $sp -Filter '*.streamDeckIconPack' -File -ErrorAction SilentlyContinue
        foreach ($f in $found) { $packs.Add($f) }
    }
}
if ($packs.Count -eq 0) {
    Write-Host '  Nenhum .streamDeckIconPack encontrado ao lado do repositorio ou na Area de Trabalho.' -ForegroundColor DarkGray
} else {
    foreach ($pack in $packs) {
        $tmpZip = Join-Path $env:TEMP ('_pf_' + [Guid]::NewGuid().ToString('N') + '.zip')
        $tmpDir = Join-Path $env:TEMP ('_pf_' + [Guid]::NewGuid().ToString('N'))
        try {
            Copy-Item -LiteralPath $pack.FullName -Destination $tmpZip -Force
            New-Item -ItemType Directory -Path $tmpDir -Force | Out-Null
            try {
                Expand-Archive -LiteralPath $tmpZip -DestinationPath $tmpDir -Force
            } catch {
                Write-Host "  AVISO: ignorado $($pack.Name): falha na extracao." -ForegroundColor Yellow
                continue
            }
            $sdIconPack = Get-ChildItem -LiteralPath $tmpDir -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like '*.sdIconPack' } | Select-Object -First 1
            if (-not $sdIconPack) {
                Write-Host "  AVISO: ignorado $($pack.Name): pasta .sdIconPack nao encontrada no pacote." -ForegroundColor Yellow
                continue
            }
            $dest = Join-Path $fifineIcons $sdIconPack.Name
            if (Test-Path -LiteralPath $dest) {
                Remove-Item -LiteralPath $dest -Recurse -Force
            }
            Move-Item -LiteralPath $sdIconPack.FullName -Destination $dest -Force
            $iconCount = (Get-ChildItem -LiteralPath (Join-Path $dest 'icons') -ErrorAction SilentlyContinue | Measure-Object).Count
            Write-Host "  OK: $($pack.Name) -> $($sdIconPack.Name) ($iconCount icones)." -ForegroundColor Green
        } finally {
            if (Test-Path -LiteralPath $tmpZip) { Remove-Item -LiteralPath $tmpZip -Force -ErrorAction SilentlyContinue }
            if (Test-Path -LiteralPath $tmpDir) { Remove-Item -LiteralPath $tmpDir -Recurse -Force -ErrorAction SilentlyContinue }
        }
    }
}

# Step 5 - bundled plugins.
Write-Host ''
Write-Host '[5/6] Instalando plugins inclusos...'
$bundledRoot = Join-Path $RepositoryRoot 'plugins'
if (Test-Path -LiteralPath $bundledRoot) {
    $bundled = @(Get-ChildItem -LiteralPath $bundledRoot -Directory -Filter '*.sdPlugin' -ErrorAction SilentlyContinue)
    if ($bundled.Count -eq 0) {
        Write-Host '  Nenhum plugin incluso (.sdPlugin) encontrado.' -ForegroundColor DarkGray
    }
    foreach ($src in $bundled) {
        $manifest = Join-Path $src.FullName 'manifest.json'
        if (-not (Test-Path -LiteralPath $manifest)) {
            Write-Host "  AVISO: ignorado $($src.Name): manifest.json ausente." -ForegroundColor Yellow
            continue
        }
        if (-not (Test-ManifestValid -Path $manifest)) {
            Write-Host "  AVISO: ignorado $($src.Name): manifest.json invalido ou criptografado." -ForegroundColor Yellow
            continue
        }
        $dest = Join-Path $fifinePlugins $src.Name
        $rc = Invoke-RoboCopy -Source $src.FullName -Destination $dest -ExtraArgs @('/MIR', '/R:1', '/W:1', '/NFL', '/NDL', '/NJH', '/NJS', '/NP')
        if ($rc.Success) {
            Write-Host "  OK: $($src.Name) instalado/atualizado." -ForegroundColor Green
        } else {
            Write-Host "  AVISO: falha ao instalar $($src.Name) (robocopy codigo $($rc.ExitCode))." -ForegroundColor Yellow
            $failed = $true
        }
    }
} else {
    Write-Host '  Pasta de plugins inclusos nao encontrada; etapa ignorada.' -ForegroundColor DarkGray
}

# Step 6 - clear store cache and restart Fifine.
Write-Host ''
Write-Host '[6/6] Limpando cache e reiniciando o Fifine...'
$storeCacheCandidates = @(
    (Join-Path $fifineRoot 'storecache\StoreCache.json'),
    (Join-Path $fifineRoot 'StoreCache.json')
)
$cleared = $false
foreach ($c in $storeCacheCandidates) {
    if (Test-Path -LiteralPath $c) {
        Remove-Item -LiteralPath $c -Force
        Write-Host "  Cache limpo: $c" -ForegroundColor DarkGray
        $cleared = $true
        break
    }
}
if (-not $cleared) {
    Write-Host '  StoreCache.json nao encontrado; nada a limpar.' -ForegroundColor DarkGray
}

if ($NoRestart) {
    Write-Host '  Reinicio ignorado (NoRestart ativo).' -ForegroundColor DarkGray
} else {
    $exePath = $null
    $proc = Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -eq 'fifine Control Deck' } | Select-Object -First 1
    if ($proc) {
        try { $exePath = $proc.MainModule.FileName } catch { $exePath = $null }
        if ([string]::IsNullOrWhiteSpace($exePath)) {
            try { $exePath = $proc.Path } catch { $exePath = $null }
        }
    }
    if ([string]::IsNullOrWhiteSpace($exePath)) {
        $candidates = @(
            (Join-Path ${env:ProgramFiles(x86)} 'fifine Control Deck\fifine Control Deck.exe'),
            (Join-Path $env:ProgramFiles 'fifine Control Deck\fifine Control Deck.exe')
        )
        foreach ($c in $candidates) {
            if (Test-Path -LiteralPath $c) { $exePath = $c; break }
        }
    }
    if ([string]::IsNullOrWhiteSpace($exePath)) {
        Write-Host '  AVISO: executavel do Fifine nao localizado; reinicio ignorado. Abra o Fifine Control Deck manualmente.' -ForegroundColor Yellow
    } else {
        if ($proc) {
            try {
                $proc | Stop-Process -Force
                $null = $proc.WaitForExit(15000)
            } catch {
                Write-Host "  AVISO: nao foi possivel encerrar o Fifine: $_" -ForegroundColor Yellow
            }
        }
        try {
            Start-Process -FilePath $exePath
            Write-Host "  Fifine Control Deck reiniciado: $exePath" -ForegroundColor Green
            $restarted = $true
        } catch {
            Write-Host "  AVISO: falha ao iniciar o Fifine Control Deck: $_" -ForegroundColor Yellow
        }
    }
}

# Final summary.
Write-Host ''
Write-Host '--------------------------------------------------' -ForegroundColor DarkGray
if ($failed) {
    Write-Host ' Resultado: concluido com avisos. Verifique os avisos acima.' -ForegroundColor Yellow
    exit 1
} else {
    $note = if ($restarted) { 'Fifine reiniciado' } else { 'Fifine nao reiniciado' }
    Write-Host ' Resultado: concluido com sucesso. Recursos do Fifine instalados.' -ForegroundColor Green
    Write-Host "            ($note)" -ForegroundColor Green
    exit 0
}
