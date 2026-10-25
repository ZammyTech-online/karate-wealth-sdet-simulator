@echo off
set "DOTNET_ROOT=%LocalAppData%\Microsoft\dotnet"
set "PATH=%LocalAppData%\Microsoft\dotnet;%PATH%"
echo Starting Söderberg Wealth .NET API on port 5125...
start "SoderbergWealthApi" /D "D:\WORKSPACE_KARATE\soderberg-wealth-api" "%LocalAppData%\Microsoft\dotnet\dotnet.exe" run --urls http://localhost:5125
echo Starting Karate Master Academy Bot on port 3000...
start "KarateAcademyBot" /D "D:\WORKSPACE_KARATE\karate-debug-bot" node server.js
echo Services started!
