@echo off
rem Runs the AppSec Ascent Engine from source (until the first NuGet release makes `dotnet tool restore` available).
dotnet run --project "%~dp0engine\src\Ascent.Cli\Ascent.Cli.csproj" --verbosity quiet -- %*
