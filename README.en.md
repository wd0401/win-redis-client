# WinRedis · A Modern Redis Desktop Client

English | [简体中文](README.md)

A local-first Redis desktop client for Windows: a modern WPF interface, real Redis data, and safe defaults (`SCAN` only, local-first search, confirmations for destructive operations, DPAPI-encrypted passwords). The UI ships in Chinese and English (Chinese by default; switch in Settings and restart to apply).

![Data browser](docs/screenshots/01-browser-hash.png)

## Features

- **Connections**: create / edit / test / connect / disconnect / delete; collapsible group tree with drag-and-drop, right-click quick move and preset groups; warning colors; environment tags; sanitized config copy
- **Data browser**: DB switcher with key-count preview, batched SCAN loading, fuzzy / exact / database search (with performance confirmation), type and TTL filters, folder tree view with persisted expansion state
- **Editors for six types**: String (UTF-8/GBK decoding, wrap / horizontal scroll for long values, JSON formatting), Hash, List, Set, ZSet, Stream (paging and element-level writes)
- **Monitoring**: memory, ops/sec, clients, hit rate, top-10 command stats, SlowLog sampling
- **Command console**: L0–L3 risk levels, production and destructive confirmations, read-only enforcement, command history
- **Key compare**: diff String / Hash / List / Set / ZSet across two read-only sessions, exportable
- **BigKey analysis**: SCAN + MEMORY USAGE driven detection with CSV export and side preview
- **Global search (Ctrl+K)**: pages, recent keys and command history
- **Appearance**: 8 themes (Light / Dark / System / Salt / Sugar / Forest / Flower / Wind), global font scaling, Chinese / English UI
- **Close behavior**: choose to minimize to the system tray (keeps running, restore from the tray icon) or quit, with a remember option and a setting
- **Local data**: `connections.json`, `settings.json`, `history.json` under `%AppData%\WinRedis`; nothing is uploaded

## Safety

- Passwords are encrypted with Windows DPAPI; copied / exported connection configs never include passwords or SSH credentials
- Keys are iterated with `SCAN`; `KEYS *` is never used
- Deletes, batch operations, `FLUSHDB` / `FLUSHALL` / `CONFIG SET` / `SHUTDOWN` and other dangerous commands require confirmation at the execution layer; L3 commands require typing the command name
- Optional "confirm every write on production"; read-only connections block all writes
- Database-wide fuzzy search is off by default and warns about the performance impact before enabling

## Build & Run

Requirements: Windows 10/11 and the .NET 10 SDK.

```powershell
dotnet build WinRedisClient.slnx -c Release
dotnet test WinRedisClient.slnx -c Release
dotnet run --project src/WinRedisClient.App/WinRedisClient.App.csproj
```

### Publish a single-file exe

```powershell
dotnet publish src/WinRedisClient.App/WinRedisClient.App.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true `
  -p:EnableCompressionInSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o publish/win-x64
```

Output: `publish/win-x64/WinRedisClient.exe` (~70 MB, self-contained).

### Build an installer

See [docs/安装包制作.md](docs/安装包制作.md) (Inno Setup script: `tools/installer/WinRedis.iss`).

## Repository layout

```
src/
  WinRedisClient.App            WPF UI (views, dialogs, themes, icons)
  WinRedisClient.Presentation   View models and presentation logic (WPF-free)
  WinRedisClient.Core           Models, abstractions, services (localization, TTL, diff)
  WinRedisClient.Redis          StackExchange.Redis implementation
  WinRedisClient.Transport      SSH tunnels (SSH.NET)
  WinRedisClient.Infrastructure JSON persistence with DPAPI encryption
tests/                          unit, integration, performance and Redis tests
docs/                           delivery checklist and screenshots
tools/                          brand assets, localization and installer scripts
```

## Localization

UI text is resolved through `Loc.T("中文原文")` in code and `{loc:Tr '中文原文'}` in XAML. English translations live in `src/WinRedisClient.Core/Localization/en.json`, keyed by the Chinese source text; missing entries fall back to Chinese. Switch language under Settings → Appearance → Language; restart to apply.

## License

[MIT License](LICENSE).

Third-party: [StackExchange.Redis](https://github.com/StackExchange/StackExchange.Redis) (MIT), [SSH.NET](https://github.com/sshnet/SSH.NET) (MIT), [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) (MIT), [Microsoft.Extensions.Logging](https://github.com/dotnet/runtime) (MIT).

## Disclaimer

WinRedis runs commands against real Redis instances. Verify that confirmations and read-only settings match your safety requirements before using it against production; any data change caused by misuse is the operator's responsibility.
