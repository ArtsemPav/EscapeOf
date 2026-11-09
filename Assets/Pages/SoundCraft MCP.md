<!-- version: 1 -->

# Подключение SoundCraft (DAW — звук, музыка, бонсы)

SoundCraft от ArtCraft подключён как MCP-сервер в Bezi — я могу работать со звуком: сессии, микширование, импорт аудио, рендер бонсов.

## Как это работает

- Конфиг: `%APPDATA%\com.bezi.app\mcp.json`, сервер `soundcraft` → `D:\ProgramFiles\soundCraft\SoundCraft\soundcraft-cli.exe mcp`
- Установка: `D:\ProgramFiles\soundCraft\SoundCraft\` (`soundcraft.exe` — DAW, `soundcraft-cli.exe` — CLI + MCP, v0.17.1)
- MCP-инструменты доступны в скриптах как `mcp_soundcraft.*` (проверять `beziDenied` перед чтением результата)
- Документация: [https://soundcraft.run/mcp](https://soundcraft.run/mcp) и `soundcraft-cli --help`

## Инструменты

- `list_commands(filter)` — список команд движка (id, params, shortcut); `execute(command, params)` — одна команда; `batch(calls, keep_going)` — до 1000 команд
- `new_session` / `open_session` / `save_session` / `inspect_session(detail)` — управление сессиями (.scraft)
- `import_audio(path, track, at)` — WAV/AIFF/FLAC/MP3/OGG/AAC на дорожку
- `bounce_mix(path, format, bit_depth, start, end, normalize)` — рендер микса с отчётом (peak, LUFS)
- `screenshot`, `ui_inspect`, `ui_set`, `menu_invoke`, `click`, `drag`, `key` — только в режиме APP (см. ниже)

## Режимы

- **Headless (сейчас активен):** `soundcraft-cli mcp` — работа с сессиями без окна программы. Новая сессия стартует пустой: 48 кГц, Int24, 4/4, 120 BPM.
- **APP-режим (управление окном):** запустить приложение с `--control <порт>`, затем в mcp.json сменить аргументы на `mcp --connect <порт>` и перезапустить Bezi. После этого доступны скриншоты, клики, меню.

## Ограничения

- Headless-режим не видит открытое окно SoundCraft — только свои сессии движка
- Позиции в командах — сэмплы (int), `{"seconds": x}` или `bars_beats:5|1|000`
- Изменения в mcp.json требуют перезапуска Bezi
- Самодельный MCP-сервер для микшеров Soundcraft Ui больше не нужен и удалён (микшера в сети нет)