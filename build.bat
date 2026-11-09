@echo off
chcp 65001 >nul
cd /d "%~dp0"
where dotnet >nul 2>nul || (
  echo Chua co .NET 10 SDK. Tai tai: https://dotnet.microsoft.com/download/dotnet/10.0  ^(muc SDK^)
  pause
  exit /b 1
)
for /f "tokens=1 delims=." %%v in ('dotnet --version') do set SDKMAJOR=%%v
if %SDKMAJOR% LSS 10 (
  echo SDK hien tai la ban %SDKMAJOR%, can .NET 10 SDK. Tai tai: https://dotnet.microsoft.com/download/dotnet/10.0  ^(muc SDK^)
  pause
  exit /b 1
)

rem tat ban dang chay de khong bi khoa file .exe
taskkill /F /T /IM SngTrans.exe >nul 2>nul

rem xoa du lieu build cu (tranh loi "Assets file ... doesn't have a target" khi doi phien ban .NET)
rmdir /s /q obj 2>nul
rmdir /s /q bin 2>nul
rmdir /s /q dist 2>nul

echo === [1/2] Ban NHE: SngTrans.exe ~ vai MB, can .NET 10 Desktop Runtime tren may chay ===
dotnet publish SngTrans.csproj -c Release -r win-x64 --self-contained false ^
  -p:PublishSingleFile=true -p:PublishReadyToRun=true -o dist\SngTrans-nhe || goto :err

echo.
echo === [2/2] Ban DAY DU: tu chua .NET, chep di dau cung chay, khong can cai gi ===
dotnet publish SngTrans.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:PublishReadyToRun=true ^
  -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -o dist\SngTrans-day-du || goto :err

echo.
echo XONG!
echo   dist\SngTrans-nhe\SngTrans.exe      (nho, can .NET 10 Desktop Runtime)
echo   dist\SngTrans-day-du\SngTrans.exe   (lon hon, chay moi may)
echo.
echo Dang mo SngTrans...
start "" "dist\SngTrans-day-du\SngTrans.exe"
timeout /t 3 >nul
exit /b 0
:err
echo.
echo Co loi khi bien dich. Copy toan bo dong chu do o tren gui lai de duoc sua.
pause
exit /b 1
