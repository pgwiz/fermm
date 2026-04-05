@echo off
REM FERMM Docker Build and Publish Script for Windows
REM Builds and publishes the FERMM server Docker image to Docker Hub
REM Usage: build-and-publish-docker.bat [version] [push-to-docker-hub]
REM Example: build-and-publish-docker.bat 1.0.0 true

setlocal enabledelayedexpansion

REM Configuration
set DOCKER_USERNAME=popox15
set IMAGE_NAME=fermm-server
set REGISTRY=docker.io
set FULL_IMAGE=%REGISTRY%/%DOCKER_USERNAME%/%IMAGE_NAME%

REM Get version from argument or default to latest
set VERSION=%1
if "%VERSION%"=="" set VERSION=latest

REM Parse push flag
set PUSH_TO_REGISTRY=%2
if "%PUSH_TO_REGISTRY%"=="" set PUSH_TO_REGISTRY=false

echo.
echo ════════════════════════════════════════
echo FERMM Docker Build ^& Publish Script
echo ════════════════════════════════════════
echo.
echo Repository: %FULL_IMAGE%
echo Version: %VERSION%
echo Push to registry: %PUSH_TO_REGISTRY%
echo.

REM Check if Docker is installed
docker --version >nul 2>&1
if errorlevel 1 (
  echo Error: Docker is not installed
  exit /b 1
)

REM Check if we're in the right directory
if not exist "docker-compose.yml" (
  echo Error: Must be run from project root directory
  exit /b 1
)

echo Step 1: Building Docker image...
docker build ^
  -f fermm-server\Dockerfile ^
  -t %FULL_IMAGE%:%VERSION% ^
  -t %FULL_IMAGE%:latest ^
  .

if errorlevel 1 (
  echo Build failed
  exit /b 1
)

echo.
echo Build complete!
echo.

if /i "%PUSH_TO_REGISTRY%"=="true" (
  echo Step 2: Pushing image to registry...
  docker push %FULL_IMAGE%:%VERSION%
  docker push %FULL_IMAGE%:latest
  
  echo.
  echo Image pushed successfully!
  echo.
  echo Available commands:
  echo   docker pull %FULL_IMAGE%:%VERSION%
  echo   docker pull %FULL_IMAGE%:latest
  echo.
  echo To use in docker-compose.yml:
  echo   image: %FULL_IMAGE%:%VERSION%
) else (
  echo Step 2: Skipping registry push
  echo.
  echo To push later, run:
  echo   docker push %FULL_IMAGE%:%VERSION%
  echo   docker push %FULL_IMAGE%:latest
)

echo.
echo ════════════════════════════════════════
echo Build complete!
echo ════════════════════════════════════════
