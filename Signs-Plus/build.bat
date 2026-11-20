@echo off
chcp 65001 >nul
cd /d "%~dp0"
call "%~dp0env.bat"
> build.log 2>&1 (
  echo === Oyun klasoru: %CS2DIR%
  if not defined TP_PROPS echo UYARI: oyun klasoru bulunamadi, arac zincirinin kayitli yollari kullaniliyor
  rem UI paketleri C# derlemesinden once kurulur: csproj'daki BuildUI hedefi derleme sirasinda esbuild'i calistirir
  echo === UI paketleri
  pushd UI
  if not exist node_modules\esbuild call npm install --no-audit --no-fund
  popd
  echo === C# derleme ve UI modulu
  dotnet build -c Release -nologo -v:minimal %TP_PROPS%
  rem Oyunun kendi DLL'leri tools\ref klasorune kopyalanir: yalnizca inceleme icin, moda dahil edilmez
  echo === Referans DLL'ler
  if defined TP_PROPS (
    if not exist tools\ref mkdir tools\ref
    for %%f in (Game.dll Colossal.Core.dll Colossal.Mathematics.dll Colossal.Collections.dll Colossal.Logging.dll Colossal.Localization.dll Colossal.IO.AssetDatabase.dll Colossal.UI.dll Colossal.UI.Binding.dll Colossal.PSI.Common.dll Unity.Entities.dll Unity.Mathematics.dll Unity.Collections.dll Unity.Burst.dll Unity.InputSystem.dll UnityEngine.CoreModule.dll UnityEngine.ImageConversionModule.dll) do (
      if exist "%TP_M%\%%f" xcopy /D /Y /Q "%TP_M%\%%f" tools\ref\ >nul
    )
    dir /b tools\ref
  )
  echo === Mod klasoru
  dir "%CSII_LOCALMODSPATH%\SignsPlus"
  echo === bitti
)
