@ECHO OFF
color f0
ECHO =============================
ECHO StreamDeck -^> Fifine Port Tool
ECHO Inclui plugins personalizados e .streamDeckIconPack
ECHO Author: WendrilXX - https://github.com/WendrilXX/Portfifine
ECHO =============================

:checkPrivileges
NET FILE 1>NUL 2>NUL
if "%errorlevel%"=="0" ( goto gotPrivileges ) else ( goto getPrivileges )

:getPrivileges
ECHO Solicitando permissao de Administrador...
powershell -Command "Start-Process cmd -ArgumentList '/c ""%~f0"" %*' -Verb RunAs"
exit /b

:gotPrivileges
cls
setlocal EnableDelayedExpansion
if not exist "%AppData%\HotSpot\StreamDock\" (
  ECHO ERRO: Fifine Control Deck nao foi encontrado em %%AppData%%\HotSpot\StreamDock\
  ECHO Instale e abra o Fifine Control Deck antes de executar este script.
  pause
  exit /b 1
)

ECHO [1/5] Copiando plugins Elgato -> Fifine (se existir)...
if exist "%AppData%\Elgato\StreamDeck\Plugins\" (
  xcopy "%AppData%\Elgato\StreamDeck\Plugins\*" "%AppData%\HotSpot\StreamDock\plugins\" /d /e /c /i /k /o /y /r >nul 2>&1
  ECHO  - Plugins copiados.
) else (
  ECHO  - Pasta Elgato Plugins nao encontrada, pulando.
)

ECHO [2/5] Copiando Icon Packs Elgato -> Fifine (se existir)...
if exist "%AppData%\Elgato\StreamDeck\IconPacks\" (
  xcopy "%AppData%\Elgato\StreamDeck\IconPacks\*" "%AppData%\HotSpot\StreamDock\icons\" /d /e /c /i /k /o /y /r >nul 2>&1
  ECHO  - IconPacks copiados.
) else (
  ECHO  - Pasta Elgato IconPacks nao encontrada, pulando.
)

ECHO [3/5] Instalando .streamDeckIconPack encontrados ao lado do .bat e na Desktop...
REM -- Usa PowerShell para extrair .streamDeckIconPack (que sao ZIPs) direto para HotSpot/icons --
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$icons='%AppData%\HotSpot\StreamDock\icons';" ^
  "$searchPaths=@('%~dp0','%USERPROFILE%\Desktop');" ^
  "$packs=@(); foreach($p in $searchPaths){ if(Test-Path $p){ $packs+=Get-ChildItem -LiteralPath $p -Filter '*.streamDeckIconPack' -File -ErrorAction SilentlyContinue } };" ^
  "if($packs.Count -eq 0){ Write-Host '  - Nenhum .streamDeckIconPack encontrado ao lado do BAT/Desktop.' -ForegroundColor Yellow };" ^
  "foreach($pack in $packs){" ^
  "  Write-Host \"  - Instalando: $($pack.Name)\";" ^
  "  $tmpZip=Join-Path $env:TEMP ('_sd_'+[Guid]::NewGuid().ToString('N')+'.zip');" ^
  "  Copy-Item -LiteralPath $pack.FullName -Destination $tmpZip -Force;" ^
  "  $tmpDir=Join-Path $env:TEMP ('_sd_'+[Guid]::NewGuid().ToString('N'));" ^
  "  New-Item -ItemType Directory -Path $tmpDir -Force | Out-Null;" ^
  "  try{ Expand-Archive -LiteralPath $tmpZip -DestinationPath $tmpDir -Force }catch{ Write-Host \"    ERRO ao extrair $($pack.Name): $_\" -ForegroundColor Red; continue };" ^
  "  $folder=Get-ChildItem -LiteralPath $tmpDir -Directory | Where-Object { $_.Name -like '*.sdIconPack' } | Select-Object -First 1;" ^
  "  if(-not $folder){ Write-Host '    ERRO: pasta .sdIconPack nao encontrada no pack' -ForegroundColor Red; continue };" ^
  "  $dest=Join-Path $icons $folder.Name;" ^
  "  if(Test-Path $dest){ Remove-Item -Recurse -Force -LiteralPath $dest };" ^
  "  Move-Item -LiteralPath $folder.FullName -Destination $dest -Force;" ^
  "  $cnt=(Get-ChildItem -LiteralPath (Join-Path $dest 'icons') -ErrorAction SilentlyContinue | Measure-Object).Count;" ^
  "  Write-Host \"    OK -> $dest ($cnt icones)\" -ForegroundColor Green;" ^
  "  Remove-Item -LiteralPath $tmpZip -Force -ErrorAction SilentlyContinue;" ^
  "  Remove-Item -Recurse -Force -LiteralPath $tmpDir -ErrorAction SilentlyContinue;" ^
  "}"

ECHO [4/5] Instalando plugins personalizados incluidos neste repositorio...
set "BUNDLED_PLUGINS=%~dp0plugins"
if exist "%BUNDLED_PLUGINS%\" (
  set "FOUND_PLUGIN=0"
  for /d %%P in ("%BUNDLED_PLUGINS%\*.sdPlugin") do (
    if exist "%%~fP\manifest.json" (
      set "FOUND_PLUGIN=1"
      ECHO  - Instalando: %%~nxP
      xcopy "%%~fP\*" "%AppData%\HotSpot\StreamDock\plugins\%%~nxP\" /e /c /i /k /o /y /r >nul 2>&1
    )
  )
  if "!FOUND_PLUGIN!"=="0" ECHO  - Nenhum plugin .sdPlugin encontrado na pasta plugins.
) else (
  ECHO  - Pasta plugins nao encontrada, pulando.
)

ECHO [5/5] Limpando cache do Fifine e reiniciando...
if exist "%AppData%\HotSpot\StreamDock\storecache\StoreCache.json" (
  del /S /Q /F "%AppData%\HotSpot\StreamDock\storecache\StoreCache.json" >nul 2>&1
  ECHO  - StoreCache limpo.
) else (
  ECHO  - StoreCache nao encontrado, pulando.
)
taskkill /f /IM "fifine Control Deck.EXE" >nul 2>&1
timeout /t 2 /nobreak >nul
if exist "C:\Program Files (x86)\fifine Control Deck\fifine Control Deck.exe" (
  start "" "C:\Program Files (x86)\fifine Control Deck\fifine Control Deck.exe"
  ECHO  - Fifine Control Deck reiniciado.
) else (
  ECHO  - Fifine exe nao encontrado em C:\Program Files (x86)\fifine Control Deck\
)

ECHO.
ECHO =============================
ECHO Concluido! Verifique as categorias de plugins e a Biblioteca de Icones no Fifine.
ECHO =============================
pause
