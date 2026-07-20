# SmartShell Club Panel

Локальная панель управления компьютерным клубом на Avalonia и .NET 8. Приложение интегрируется со SmartShell API и получает актуальную авторизацию из рабочего Chrome через расширение `SmartShellChromeBridge`.

## Возможности

- карта клуба и управление компьютерами;
- бронирования, тарифы, магазин и платежи;
- клиенты, дашборд и логи SmartShell;
- таймеры PlayStation и системные уведомления;
- аудит уборки компьютеров;
- автоматическая смена оператора через Chrome Bridge;
- работа в системном трее и автозапуск Windows.

## Сборка

Требуются Windows и .NET 8 SDK.

```powershell
dotnet restore
dotnet build SmartShellCleanerAvalonia.csproj
dotnet run --project SmartShellCleanerAvalonia.csproj
```

## Chrome Bridge

Инструкция по установке расширения находится в [SmartShellChromeBridge/README.md](SmartShellChromeBridge/README.md).

Локальные токены, журналы, состояние клуба и каталоги сборки исключены из Git.
