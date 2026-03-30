# Petrichor для Windows

English version: [README.md](README.md)

Эта папка содержит именно Windows-ветку Petrichor.Здесь лежат WPF-приложение, media-слой, SQLite persistence, Windows-интеграции и скрипты сборки.

## Что уже есть в Windows-версии

На текущем этапе это уже полноценный локальный музыкальный плеер, а не просто набросок миграции. Ветка уже закрывает основные пользовательские сценарии:

- импорт локальной музыкальной библиотеки из папок
- извлечение метаданных при импорте
- сохранение данных в SQLite
- восстановление очереди и playback state между запусками
- repeat и shuffle
- отдельные секции Library, Playlists и Folders
- рабочее воспроизведение через реальный backend
- эквалайзер с пресетами
- сохранение и удаление пользовательских EQ-пресетов
- import/export EQ-пресетов
- базовый DSP-слой с loudness и replay gain
- lyrics с каскадом fallback-источников
- авторизация и scrobbling через Last.fm
- tray-интеграция
- поддержка Windows media keys и SMTC
- ассоциации файлов для поддерживаемых аудиоформатов
- portable-сборка и базовый installer flow

То есть Windows-версия уже находится в состоянии, где ей можно пользоваться, ее можно тестировать и развивать как самостоятельное приложение.

## Что еще не доведено до конца

Самая заметная незавершенная часть сейчас связана не с базовым плеером, а с доводкой релизного контура. Portable packaging и installer уже есть, но installer/update flow все еще считается незавершенным и требует дальнейшей полировки.

Кроме этого,еще остаются задачи по более глубокой продуктовой parity: richer metadata views, дополнительные UX-слои, часть audio polish, QA hardening и release polish.

Если говорить честно, текущее состояние такое: Windows-версия уже рабочая, но это еще не финально отполированный релиз.

## Реальная структура папки Windows

Ниже показана фактическая структура `Windows` в этом репозитории. Здесь специально оставлена полезная часть дерева, без перечисления всех служебных `bin` и `obj`.

```text
Windows/
|- Configuration/
|  |- Secrets.props
|  \- Secrets.props.template
|- dist/
|- installer/
|  \- Petrichor.iss
|- Petrichor.App/
|  |- App.xaml
|  |- App.xaml.cs
|  |- MainWindow.xaml
|  |- MainWindow.xaml.cs
|  |- LyricsService.cs
|  |- LastFmScrobbleService.cs
|  |- WindowsSmtcService.cs
|  \- Petrichor.App.csproj
|- Petrichor.Core/
|  |- Abstractions/
|  |- Domain/
|  |- Migration/
|  |- Playback/
|  \- Petrichor.Core.csproj
|- Petrichor.Data/
|  |- Persistence/
|  |- Repositories/
|  |- Services/
|  \- Petrichor.Data.csproj
|- Petrichor.Media/
|  |- Playback/
|  \- Petrichor.Media.csproj
|- Petrichor.Platform.Windows/
|  |- Shell/
|  |- Storage/
|  \- Petrichor.Platform.Windows.csproj
|- Scripts/
|  \- Build-WindowsRelease.ps1
|- windows-migration/
|  |- BACKLOG.md
|  |- MACOS-DEPENDENCIES.md
|  |- PARITY-MATRIX.md
|  |- PLAN.md
|  \- VERIFICATION-CHECKLIST.md
|- Petrichor.Windows.slnx
|- Petrichor.WindowsLegacy.slnx
\- README.md
```

## Как эта структура делится логически

Хотя на диске это просто набор папок, по ролям Windows-ветка делится так:

- `Petrichor.App`  
  Основное WPF-приложение. Здесь находится shell, главное окно, навигация по секциям, UI-логика, tray, SMTC, lyrics, Last.fm и общее связывание сервисов.

- `Petrichor.Core`  
  Базовые модели и контракты. Здесь лежат доменные сущности, playback state, queue semantics и общие абстракции.

- `Petrichor.Data`  
  Слой данных: SQLite, schema, repositories, library import, metadata extraction и работа с локальной библиотекой.

- `Petrichor.Media`  
  Слой воспроизведения: playback backend, orchestration, EQ и DSP.

- `Petrichor.Platform.Windows`  
  Чисто Windows-специфичные вещи: storage paths, settings storage, shell/file associations и подобные интеграции.

- `Configuration`  
  Build-time конфигурация, включая шаблон для Last.fm секретов.

- `Scripts`  
  Скрипты сборки релизов и упаковки.

- `installer`  
  Конфигурация Inno Setup.

- `windows-migration`  
  Документация по миграции, backlog, parity и проверкам.

Для повседневной работы основной entry point сейчас: `Petrichor.Windows.slnx`.

## Архитектура в простых словах

Windows-версия сделана как local-first desktop player.

UI построен на WPF. Состояние приложения и библиотека хранятся в SQLite. При импорте треков метаданные извлекаются и сохраняются локально, чтобы приложение могло восстанавливать библиотеку, плейлисты, очередь, playback state, EQ state и другие пользовательские данные между запусками.

Воспроизведение заведено через `Petrichor.Media`. Это не фейковая оркестрация на уровне кнопок, а реальная playback-цепочка с Windows-ориентированным backend-слоем. EQ и DSP применяются внутри media-слоя, а не имитируются только в UI.

Поверх этого есть Windows-специфичная интеграция: tray, media keys, SMTC, ассоциации файлов и хранение пользовательских данных в профиле пользователя.

## Технологический стек

Сейчас в Windows-ветке используются:

- .NET 10
- WPF
- SQLite через `Microsoft.Data.Sqlite`
- `TagLibSharp` для чтения метаданных
- `NAudio` как более сильный playback backend path
- Windows media/session API для SMTC
- Windows Registry для file associations
- Inno Setup для installer packaging

Текущие target frameworks:

- `Petrichor.App`: `net10.0-windows10.0.19041.0`
- `Petrichor.Media`: `net10.0-windows`
- `Petrichor.Data`: `net10.0`

Если говорить проще, это нативное Windows-приложение на современном .NET, а не компромиссная кроссплатформенная оболочка.

## Поддерживаемые форматы

На текущем этапе Windows-ветка распознает эти расширения и в library import, и в shell registration:

- `.mp3`
- `.flac`
- `.wav`
- `.m4a`
- `.aac`
- `.ogg`
- `.wma`
- `.aiff`
- `.alac`

Метаданные, которые уже извлекаются и используются приложением:

- title
- artist
- album
- genre
- year
- duration

Этого уже достаточно для library sorting, filtering, playlist workflows, отображения current track, lyrics lookup и Last.fm scrobbling.

## Что уже умеет Windows-версия для пользователя

### Library

- добавлять папки в библиотеку
- сканировать поддерживаемые аудиофайлы
- сохранять библиотеку в SQLite
- искать, сортировать и фильтровать треки
- обновлять библиотеку через watcher baseline
- восстанавливать library-backed playback после перезапуска

### Playback

- play, pause, seek, next, previous
- управление громкостью
- repeat и shuffle
- восстановление очереди
- открытие одиночного файла напрямую в плеере
- переключение playback source между library, folders и playlists

### Playlists и Folders

- создание плейлистов
- rename/delete плейлистов
- добавление текущего трека в плейлист
- сохранение текущей очереди как плейлиста
- reorder и remove playlist tracks
- import/export `.m3u` и `.m3u8`
- запуск воспроизведения из folders и playlists
- smart playlist baseline

### Audio

- встроенные EQ-пресеты
- пользовательские пресеты
- сохранение и удаление пользовательских пресетов
- import/export пресетов
- loudness baseline
- replay gain baseline

### Windows integration

- tray icon с playback-действиями
- hide-to-tray поведение
- media keys через SMTC
- системный media overlay с метаданными и timeline
- регистрация поддерживаемых форматов для "Open with Petrichor"

### Online services

- Last.fm login flow
- now playing updates
- scrobbling с retry
- lyrics из внешнего `.lrc`
- lyrics из внешнего `.srt`
- lyrics из embedded tags
- online fallback через LRCLIB, если он разрешен

## Что нужно для разработки

Для разработки нужен такой базовый набор:

- Windows 10 версии 2004 или новее
- .NET 10 SDK
- Inno Setup 6, если вы хотите собирать installer, а не только portable build

Для запуска self-contained релизной сборки отдельная установка .NET пользователю не нужна.

## Как запустить локально

Восстановление и сборка:

```powershell
dotnet restore .\Windows\Petrichor.Windows.slnx
dotnet build .\Windows\Petrichor.Windows.slnx
```

Запуск приложения:

```powershell
dotnet run --project .\Windows\Petrichor.App\Petrichor.App.csproj
```

Для обычной разработки этого достаточно.

## Настройка Last.fm

Windows-ветка работает: пользователь не должен вручную вбивать application credentials в runtime UI. Эти данные добавляются на этапе сборки, а потом пользователь уже авторизует свой собственный Last.fm-аккаунт через браузер.

Чтобы включить Last.fm в своей сборке:

1. Скопируйте `Windows/Configuration/Secrets.props.template` в `Windows/Configuration/Secrets.props`.
2. Заполните `LastFmApiKey` и `LastFmSharedSecret`.
3. Пересоберите приложение.

Пример:

```powershell
Copy-Item .\Windows\Configuration\Secrets.props.template .\Windows\Configuration\Secrets.props
```

Если `Secrets.props` отсутствует или пустой, приложение все равно соберется, но Last.fm connect будет недоступен.

Важно: это credentials самого приложения, а не аккаунта конечного пользователя. Пользователь потом логинится в свой Last.fm-аккаунт отдельно, и уже его прослушивания scrobble'ятся через ваше приложение.

Локальная сессия Last.fm на Windows сохраняется в зашифрованном виде для текущего пользователя.

## Как работает lyrics fallback

Текущий порядок такой:

1. внешний `.lrc` рядом с треком
2. внешний `.srt` рядом с треком
3. embedded lyrics в тегах файла
4. online fetch через LRCLIB, если это разрешено

Это уже дает нормальный практический сценарий для локального плеера.

## Где Windows-версия хранит свои данные

Локальные пользовательские данные лежат вне папки установки, по пути:

```text
%LocalAppData%\Petrichor
```

Это важно, потому что переустановка приложения не должна автоматически уничтожать библиотеку, настройки и пользовательское состояние.

Сейчас там хранятся:

- `petrichor.db`
- `ArtworkCache\`
- `playback-state.json`
- `settings.json`
- `equalizer-profile.json`
- `dsp-profile.json`
- `equalizer-presets.json`
- `lastfm-session.bin`

Именно там находится основное runtime-состояние Windows-версии.

## Сборка релиза

В репозитории уже есть release script для self-contained Windows-сборки.

Portable build:

```powershell
.\Windows\Scripts\Build-WindowsRelease.ps1 -Configuration Release -Runtime win-x64 -Version 0.1.0
```

Portable build + installer:

```powershell
.\Windows\Scripts\Build-WindowsRelease.ps1 -Configuration Release -Runtime win-x64 -Version 0.1.0 -BuildInstaller
```

Что делает скрипт:

- публикует приложение как self-contained
- включает single-file publish
- включает compression для single-file
- включает ReadyToRun
- складывает результат в `Windows/dist/release`
- создает portable zip
- при необходимости вызывает Inno Setup для сборки installer

Installer собирается по `Windows/installer/Petrichor.iss`.

Текущая директория установки:

```text
%LocalAppData%\Programs\Petrichor
```

Этот контур уже рабочий, но installer/update flow пока все еще считается незавершенным и требует дальнейшей полировки.

## File associations

Windows-версия умеет регистрировать себя как вариант "Open with Petrichor" для поддерживаемых аудиофайлов. Сейчас эта регистрация идет на уровне текущего пользователя, а не как жесткая machine-wide системная ассоциация.

На практике это дает нормальную нативную точку входа: пользователь может открыть поддерживаемый файл из Проводника напрямую в Petrichor.

## Заметки для разработчиков

В этой ветке уже достаточно много работающих вещей, поэтому главное правило простое: новые задачи нельзя делать ценой поломки уже существующих сценариев.

Минимальный smoke-check после заметных изменений:

- playback: play, pause, next, previous, seek, volume, repeat, shuffle
- library: import, search, sort, filter, открытие трека из библиотеки
- playlists: create, rename, delete, add current track, remove track, reorder, restore playback from playlist
- audio: EQ presets, пользовательский EQ state, loudness, replay gain
- platform: tray actions, file open, SMTC, startup/shutdown behavior


## Короткий честный вывод

Windows-ветка уже стала самостоятельным нативным desktop-портом. В ней есть реальный shell, persistence layer, playback wiring, Windows integration, library management, playlist workflows, EQ/DSP baseline, lyrics и Last.fm.

То, чего ей сейчас не хватает, это не создание приложения с нуля, а нормальный финальный слой доводки: installer hardening, дополнительная parity-полировка, QA-coverage и финальный релизный polish.
