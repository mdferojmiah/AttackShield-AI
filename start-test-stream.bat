@echo off
REM ──────────────────────────────────────────────────
REM  Local RTSP Test Server using FFmpeg
REM  Streams your webcam as an RTSP source for testing
REM  URL: rtsp://127.0.0.1:8554/live
REM ──────────────────────────────────────────────────

SET CAMERA_NAME=Integrated Camera
SET RTSP_PORT=8554

echo ====================================
echo  AttackShield AI - Test RTSP Server
echo ====================================
echo.
echo Camera : %CAMERA_NAME%
echo RTSP URL: rtsp://127.0.0.1:%RTSP_PORT%/live
echo.
echo Press Ctrl+C to stop.
echo.

ffmpeg -f dshow -i video="%CAMERA_NAME%" ^
  -c:v libx264 -preset ultrafast -tune zerolatency ^
  -f rtsp -rtsp_transport tcp ^
  rtsp://127.0.0.1:%RTSP_PORT%/live

pause
