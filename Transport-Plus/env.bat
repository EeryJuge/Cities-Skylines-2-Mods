@echo off
rem Oyun guncellenince Windows klasor adi degisir (ornek 1.6.2.0 -> 1.6.4.0) ama mod arac zincirinin
rem ortam degiskenleri eski klasoru gostermeye devam eder. Bu dosya oyunun guncel yerini bulur ve
rem dotnet komutlarina verilecek yollari TP_PROPS degiskenine yazar. Kalici hicbir ayari degistirmez.
set "CS2DIR="
set "TP_PROPS="
for /f "usebackq delims=" %%i in (`powershell -NoProfile -Command "(Get-AppxPackage *CitiesSkylinesII* | Select-Object -First 1).InstallLocation"`) do set "CS2DIR=%%i"
if not defined CS2DIR goto :eof
set "TP_M=%CS2DIR%\Cities2_Data\Managed"
set "TP_T=%CS2DIR%\Cities2_Data\Content\Game\.ModdingToolchain"
set TP_PROPS="-p:ManagedPath=%TP_M%" "-p:MSCORLIBPath=%TP_M%\mscorlib.dll" "-p:ModPostProcessorPath=%TP_T%\ModPostProcessor\ModPostProcessor.exe" "-p:ModPublisherPath=%TP_T%\ModPublisher\ModPublisher.exe"
