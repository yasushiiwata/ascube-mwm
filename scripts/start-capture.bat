@echo off
REM T12: ring-buffer capture using dumpcap (bundled with Wireshark; not shipped with this repo,
REM GPL/non-OSS licensing concerns per rule 12). Requires Wireshark/dumpcap (and Npcap) to be
REM installed separately on the target machine, with dumpcap on PATH.
REM See scripts/README.md for details (Japanese).
REM
REM Usage: start-capture.bat [interface number] [output dir]
REM   Run "dumpcap -D" first to list interface numbers if unsure.
REM
REM Ring settings: -b filesize:102400 (KB, =100MB) -b files:100 (keeps ~10GB, rotating)
REM Filter: tcp port 11112 or icmp

setlocal
set IFACE=%~1
if "%IFACE%"=="" set IFACE=1
set OUTDIR=%~2
if "%OUTDIR%"=="" set OUTDIR=%~dp0..\captures\pcap

if not exist "%OUTDIR%" mkdir "%OUTDIR%"

echo dumpcap -i %IFACE% -f "tcp port 11112 or icmp" -w "%OUTDIR%\capture.pcapng" -b filesize:102400 -b files:100
dumpcap -i %IFACE% -f "tcp port 11112 or icmp" -w "%OUTDIR%\capture.pcapng" -b filesize:102400 -b files:100
