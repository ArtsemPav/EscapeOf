<!-- version: 1 -->

# Подключение PhotoCraft (рисование / обработка изображений)

PhotoCraft подключён как MCP-сервер в Bezi — я могу открывать, редактировать и экспортировать изображения.

## Как это работает

- Конфиг: `%APPDATA%\com.bezi.app\mcp.json`, сервер `photocraft` (bridge-режим через `photocraft-cli mcp --bridge`)
- Установка: портативная, v0.9.0 — `D:\photocraft-0.9.0-windows-x64-portable\`
  - Приложение: `D:\photocraft-0.9.0-windows-x64-portable\PhotoCraft\photocraft.exe`
  - CLI: `D:\photocraft-0.9.0-windows-x64-portable\photocraft-cli.exe`
- Токен: `~\.config\opencode\photocraft-control.token` (shared с OpenCode)

## Запуск с управляющим каналом (обязательно)

Старого скрипта `Start-PhotoCraft-AI.ps1` больше нет — приложение нужно запускать с флагами:

```powershell
Start-Process "D:\photocraft-0.9.0-windows-x64-portable\PhotoCraft\photocraft.exe" -ArgumentList `
  '--control','43821',
  '--control-token-file',"$env:USERPROFILE\.config\opencode\photocraft-control.token",
  '--automation-read-root','D:\photocraft-0.9.0-windows-x64-portable',
  '--automation-write-root','D:\photocraft-0.9.0-windows-x64-portable'
```

- `--control 43821` — порт, к которому подключается bridge MCP-сервера
- `--control-token-file` — файл токена (64 hex); сервер может создать его сам, если не существует
- `--automation-read-root` / `--automation-write-root` — корни для файлов автоматизации

Полный список флагов: `photocraft.exe --help`.

## Ограничения

- Автоматизация ограничена read/write roots: `D:\photocraft-0.9.0-windows-x64-portable\` — файлы для работы нужно класть туда
- Если PhotoCraft запущен без `--control 43821` (или не запущен вовсе) — bridge не подключается, команды MCP падают с `connection refused`
- Изменения в mcp.json требуют перезапуска Bezi / переподключения воркспейса

## Возможности

- Открытие/сохранение PSD, PNG, JPG, TIF, WebP, EXR и др.
- Слои, маски, корректирующие слои, типографика, векторы, кисти
- Более 500 команд движка (список: `photocraft-cli commands --filter <text>`; из Bezi — MCP-команда `command_list` с фильтром)