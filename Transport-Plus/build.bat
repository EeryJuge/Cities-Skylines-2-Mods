@echo off
chcp 65001 >nul
cd /d "%~dp0"
call "%~dp0env.bat"
> build.log 2>&1 (
  echo === Oyun klasoru: %CS2DIR%
  if not defined TP_PROPS echo UYARI: oyun klasoru bulunamadi, arac zincirinin kayitli yollari kullaniliyor
  echo === C# derleme
  dotnet build -c Release -nologo -v:minimal %TP_PROPS%
  echo C# cikis kodu: %ERRORLEVEL%
  echo === UI paketleri
  pushd UI
  if not exist node_modules\esbuild call npm install --no-audit --no-fund
  echo === UI derleme
  call npm run build
  echo UI cikis kodu: %ERRORLEVEL%
  popd
  echo === Mod klasoru
  dir "%CSII_LOCALMODSPATH%\TransportPlus"
  echo === bitti
)
