@echo off
chcp 65001 >nul
cd /d "%~dp0"
rmdir /s /q obj 2>nul
dotnet build SngTrans.csproj -c Release -o bin\run || goto :err
echo.
echo === Dang chay SngTrans (cua so nay hien nhat ky, dong lai de thoat) ===
start "" /wait "bin\run\SngTrans.exe" --debug
echo.
echo Chuong trinh da dung. Neu co loi, xem phia tren hoac file %%APPDATA%%\SngTrans\sngtrans.log
pause
exit /b 0
:err
echo Co loi khi bien dich. Copy dong chu do o tren gui lai de duoc sua.
pause
