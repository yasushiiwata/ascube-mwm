@echo off
REM T12: leave a searchable marker in pcap / raw captures / audit logs during on-site testing.
REM Sends a C-ECHO whose Calling AE Title is "MARK-STEP<N>" (AE Titles travel on the wire as
REM plain ASCII, so you can grep pcap/T9 raw captures for this string to find "step N started here").
REM See scripts/README.md for details (Japanese).
REM
REM Usage: mark.bat <step number>
REM Example: mark.bat 3   (sends a C-ECHO with AE Title "MARK-STEP3")

setlocal
if "%~1"=="" (
  echo Usage: mark.bat ^<step number^>
  exit /b 2
)

echoscu -v -aet MARK-STEP%~1 -aec ASCUBE_MWM 127.0.0.1 11112
