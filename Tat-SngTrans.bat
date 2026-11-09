@echo off
rem Tat han SngTrans tu ben ngoai (giong Ket thuc tac vu trong Task Manager)
taskkill /F /T /IM SngTrans.exe >nul 2>nul && (echo Da tat SngTrans.exe) || (echo Khong tim thay SngTrans dang chay.)
timeout /t 2 >nul
