@echo off
title Sim FFB - Calibracion
echo Gira el volante y pisa cada pedal. Mira que valor (X, Y, Z, RX, RY, RZ, S0, S1)
echo cambia con cada uno, y que numero de boton se enciende con cada leva.
echo Luego pon esos nombres en input.cfg.
echo (Cierra con Ctrl+C)
echo.
"%~dp0SimFfb.exe" calibrate
pause
