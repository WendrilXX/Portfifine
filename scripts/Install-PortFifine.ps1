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
    [switch]$NoPause,
    [Alias('Manual')]
    [switch]$Help,
    [Alias('ScanResources')]
    [switch]$Scan,
    [switch]$Diagnose,
    [switch]$Services,
    [switch]$Inspect
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$failed = $false
$restarted = $false

function Get-ManifestStatus {
    param([string]$Path)
    try {
        if (-not (Test-Path -LiteralPath $Path)) {
            return [PSCustomObject]@{ Valid = $false; Reason = 'manifest.json ausente' }
        }
        $raw = [System.IO.File]::ReadAllText($Path)
        $trimmed = $raw.TrimStart()
        if ($trimmed.Length -eq 0) {
            return [PSCustomObject]@{ Valid = $false; Reason = 'manifest.json vazio' }
        }
        if (-not $trimmed.StartsWith('{')) {
            return [PSCustomObject]@{ Valid = $false; Reason = 'manifesto nao esta em JSON aberto (possivel criptografia)' }
        }
        $null = $raw | ConvertFrom-Json -ErrorAction Stop
        return [PSCustomObject]@{ Valid = $true; Reason = 'manifest JSON valido' }
    } catch {
        return [PSCustomObject]@{ Valid = $false; Reason = 'manifest JSON invalido' }
    }
}

function Test-ManifestValid {
    param([string]$Path)
    return (Get-ManifestStatus -Path $Path).Valid
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

function Write-Header {
    param([string]$Title = 'Portfifine - Instalador para Fifine Control Deck')
    Write-Host '--------------------------------------------------' -ForegroundColor DarkGray
    Write-Host " $Title" -ForegroundColor Cyan
    Write-Host '--------------------------------------------------' -ForegroundColor DarkGray
}

function Find-FifineExecutable {
    $process = Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -eq 'fifine Control Deck' } | Select-Object -First 1
    if ($process) {
        try {
            if (-not [string]::IsNullOrWhiteSpace($process.MainModule.FileName)) {
                return $process.MainModule.FileName
            }
        } catch { }
        try {
            if (-not [string]::IsNullOrWhiteSpace($process.Path)) {
                return $process.Path
            }
        } catch { }
    }

    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'fifine Control Deck\fifine Control Deck.exe'),
        (Join-Path $env:ProgramFiles 'fifine Control Deck\fifine Control Deck.exe')
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }
    return $null
}

function Write-Manual {
    Write-Header -Title 'Portfifine - Manual rapido'
    Write-Host ' Uso:'
    Write-Host '   StreamDeckPortFifine.bat' -ForegroundColor Green
    Write-Host ''
    Write-Host ' Instalacao:'
    Write-Host '   -BundledOnly        Instala somente os recursos deste repositorio.'
    Write-Host '   -NoRestart          Nao reinicia o Fifine ao finalizar.'
    Write-Host '   -NoDesktopIconPacks Nao procura .streamDeckIconPack na Area de Trabalho.'
    Write-Host '   -NoPause            Nao pausa a janela ao finalizar (automacao).'
    Write-Host ''
    Write-Host ' Consulta somente leitura (nao altera arquivos nem reinicia o Fifine):'
    Write-Host '   -Diagnose  Verifica instalacoes, pastas e executavel do Fifine.'
    Write-Host '   -Scan      Lista plugins e pacotes de icones encontrados.'
    Write-Host '   -Services  Lista processos e servicos relacionados.'
    Write-Host '   -Inspect   Executa Diagnose + Scan + Services.' -ForegroundColor Green
    Write-Host ''
    Write-Host ' Exemplos:'
    Write-Host '   StreamDeckPortFifine.bat -Inspect'
    Write-Host '   StreamDeckPortFifine.bat -BundledOnly -NoRestart'
    Write-Host '   StreamDeckPortFifine.bat -Help'
}

function Write-Diagnosis {
    param([string]$RepoRoot)
    Write-Host ''
    Write-Host '[DIAGNOSTICO] Instalacoes e caminhos'
    $fifineRoot = Join-Path $env:APPDATA 'HotSpot\StreamDock'
    if (Test-Path -LiteralPath $fifineRoot) {
        $installedPlugins = @(Get-ChildItem -LiteralPath (Join-Path $fifineRoot 'plugins') -Directory -Filter '*.sdPlugin' -ErrorAction SilentlyContinue)
        $profiles = @(Get-ChildItem -LiteralPath (Join-Path $fifineRoot 'profiles') -Directory -ErrorAction SilentlyContinue)
        Write-Host "  OK: dados do Fifine encontrados em $fifineRoot" -ForegroundColor Green
        Write-Host "  INFO: plugins instalados: $($installedPlugins.Count); perfis locais: $($profiles.Count)" -ForegroundColor DarkGray
    } else {
        Write-Host "  AVISO: dados do Fifine nao encontrados em $fifineRoot" -ForegroundColor Yellow
    }

    $exePath = Find-FifineExecutable
    if ($exePath) {
        Write-Host "  OK: executavel do Fifine: $exePath" -ForegroundColor Green
    } else {
        Write-Host '  AVISO: executavel do Fifine nao localizado nos caminhos conhecidos.' -ForegroundColor Yellow
    }

    $elgatoRoot = Join-Path $env:APPDATA 'Elgato\StreamDeck'
    if (Test-Path -LiteralPath $elgatoRoot) {
        Write-Host "  OK: dados do Elgato encontrados em $elgatoRoot" -ForegroundColor Green
    } else {
        Write-Host '  INFO: dados do Elgato nao encontrados; a migracao sera ignorada.' -ForegroundColor DarkGray
    }

    if (Test-Path -LiteralPath $RepoRoot) {
        Write-Host "  OK: repositorio encontrado em $RepoRoot" -ForegroundColor Green
    } else {
        Write-Host "  AVISO: repositorio nao encontrado em $RepoRoot" -ForegroundColor Yellow
    }
}

function Write-ResourceScan {
    param([string]$RepoRoot)
    Write-Host ''
    Write-Host '[VARREDURA] Recursos compativeis'

    $elgatoPlugins = Join-Path $env:APPDATA 'Elgato\StreamDeck\Plugins'
    if (Test-Path -LiteralPath $elgatoPlugins) {
        $plugins = @(Get-ChildItem -LiteralPath $elgatoPlugins -Directory -Filter '*.sdPlugin' -ErrorAction SilentlyContinue)
        Write-Host "  Elgato plugins: $($plugins.Count) encontrado(s)." -ForegroundColor DarkGray
        foreach ($plugin in $plugins) {
            $status = Get-ManifestStatus -Path (Join-Path $plugin.FullName 'manifest.json')
            if ($status.Valid) {
                Write-Host "    OK: $($plugin.Name)" -ForegroundColor Green
            } else {
                Write-Host "    AVISO: $($plugin.Name) - $($status.Reason)." -ForegroundColor Yellow
            }
        }
    } else {
        Write-Host '  INFO: pasta de plugins Elgato nao encontrada.' -ForegroundColor DarkGray
    }

    $bundledRoot = Join-Path $RepoRoot 'plugins'
    if (Test-Path -LiteralPath $bundledRoot) {
        $bundled = @(Get-ChildItem -LiteralPath $bundledRoot -Directory -Filter '*.sdPlugin' -ErrorAction SilentlyContinue)
        Write-Host "  Plugins incluidos: $($bundled.Count) encontrado(s)." -ForegroundColor DarkGray
        foreach ($plugin in $bundled) {
            $status = Get-ManifestStatus -Path (Join-Path $plugin.FullName 'manifest.json')
            if ($status.Valid) {
                Write-Host "    OK: $($plugin.Name)" -ForegroundColor Green
            } else {
                Write-Host "    AVISO: $($plugin.Name) - $($status.Reason)." -ForegroundColor Yellow
            }
        }
    } else {
        Write-Host '  INFO: pasta plugins do repositorio nao encontrada.' -ForegroundColor DarkGray
    }

    $paths = @($RepoRoot, [Environment]::GetFolderPath('Desktop')) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path -LiteralPath $_) }
    $packs = @($paths | ForEach-Object { Get-ChildItem -LiteralPath $_ -Filter '*.streamDeckIconPack' -File -ErrorAction SilentlyContinue })
    if ($packs.Count -eq 0) {
        Write-Host '  INFO: nenhum .streamDeckIconPack encontrado no repositorio ou Area de Trabalho.' -ForegroundColor DarkGray
    } else {
        Write-Host "  Icon packs: $($packs.Count) encontrado(s)." -ForegroundColor DarkGray
        foreach ($pack in $packs) { Write-Host "    OK: $($pack.FullName)" -ForegroundColor Green }
    }
}

function Write-ServiceStatus {
    Write-Host ''
    Write-Host '[SERVICOS] Processos e servicos relacionados'
    $pattern = 'fifine|streamdock|stream deck|elgato'
    try {
        $services = @(Get-Service -ErrorAction Stop | Where-Object { $_.Name -match $pattern -or $_.DisplayName -match $pattern })
        if ($services.Count -eq 0) {
            Write-Host '  INFO: nenhum servico Windows relacionado encontrado (normal para o Fifine).' -ForegroundColor DarkGray
        } else {
            foreach ($service in $services) {
                Write-Host "  OK: servico $($service.DisplayName) - $($service.Status)" -ForegroundColor Green
            }
        }
    } catch {
        Write-Host "  AVISO: nao foi possivel consultar servicos Windows: $_" -ForegroundColor Yellow
    }

    $processes = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -match 'fifine|hotspot|streamdock|streamdeck|elgato' })
    if ($processes.Count -eq 0) {
        Write-Host '  INFO: nenhum processo relacionado esta em execucao.' -ForegroundColor DarkGray
    } else {
        foreach ($process in $processes) {
            Write-Host "  OK: processo $($process.ProcessName) (PID $($process.Id))" -ForegroundColor Green
        }
    }
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

if ($Help) {
    Write-Manual
    exit 0
}

if ($Inspect -or $Diagnose -or $Scan -or $Services) {
    Write-Header -Title 'Portfifine - Consulta do ambiente'
    Write-Host " Repositorio : $RepositoryRoot" -ForegroundColor DarkGray
    Write-Host ' Modo        : somente leitura (nenhum arquivo sera alterado)' -ForegroundColor DarkGray
    if ($Inspect -or $Diagnose) { Write-Diagnosis -RepoRoot $RepositoryRoot }
    if ($Inspect -or $Scan) { Write-ResourceScan -RepoRoot $RepositoryRoot }
    if ($Inspect -or $Services) { Write-ServiceStatus }
    Write-Host ''
    Write-Host ' Resultado: consulta concluida.' -ForegroundColor Green
    exit 0
}

Write-Header
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
