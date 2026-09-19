# ProjectDB Service Manager

[![GitHub Release](https://img.shields.io/github/v/release/pavel-elblaus/projectdb-service-manager?sort=semver&style=flat-square)](https://github.com/pavel-elblaus/projectdb-service-manager/releases/latest)
[![GitHub Downloads](https://img.shields.io/github/downloads/pavel-elblaus/projectdb-service-manager/total?style=flat-square)](https://github.com/pavel-elblaus/projectdb-service-manager/releases)
[![Build](https://github.com/pavel-elblaus/projectdb-service-manager/actions/workflows/build.yml/badge.svg)](https://github.com/pavel-elblaus/projectdb-service-manager/actions/workflows/build.yml)
[![License](https://img.shields.io/github/license/pavel-elblaus/projectdb-service-manager?style=flat-square)](LICENSE)
[![Windows x64](https://img.shields.io/badge/platform-Windows%20x64-0078D4?style=flat-square)](https://github.com/pavel-elblaus/projectdb-service-manager/releases)

This description and guide are also available [in English](README.md).

**ProjectDB Service Manager** — настольное приложение для Windows, которое устанавливает ProjectDB и позволяет запускать одно или несколько приложений ProjectDB как полноценные службы Windows.

В одном графическом интерфейсе можно регистрировать приложения, запускать и останавливать их, видеть состояние и последние события, открывать логи, обновлять общий ProjectDB и управлять локальной библиотекой `app.so`. Не нужно вручную создавать службы, настраивать WinSW или работать с `sc.exe`.

Сам ProjectDB доступен в репозитории [github.com/pavel-elblaus/projectdb](https://github.com/pavel-elblaus/projectdb) и на сайте [projectdb.pro](https://projectdb.pro).

## Зачем нужен ProjectDB Service Manager

Обычная Windows-сборка ProjectDB позволяет запустить приложение напрямую. Service Manager добавляет удобный слой управления и полноценную работу через службы Windows.

- **Одна установка — несколько приложений.** Бинарные файлы ProjectDB используются совместно, а каждое зарегистрированное приложение получает собственные настройки и службу Windows.
- **Автоматический запуск.** Зарегистрированные приложения работают как службы Windows и продолжают работу без открытого терминала и без необходимости держать пользовательскую сессию.
- **Простое управление службами.** Запуск, остановка и перезапуск из интерфейса. Все работающие приложения можно перезапустить одной кнопкой.
- **Состояние сразу видно.** Карточка приложения показывает состояние службы, её идентификатор, PID процесса и сведения об источнике/версии ProjectDB, если они доступны.
- **Последнее событие прямо в карточке.** Последняя структурированная запись лога видна без открытия файлов; предупреждения и ошибки выделяются.
- **Быстрый доступ к логам.** Папка логов конкретного приложения открывается одной кнопкой.
- **Безопасное удаление приложения.** Можно удалить одну регистрацию или все регистрации, не удаляя общий ProjectDB.
- **Управление библиотекой ProjectDB.** Локальный `app.so` можно выбрать или удалить прямо из Service Manager. Выбранный локальный файл сохраняется при обновлении ProjectDB.
- **Работа из системного трея.** Service Manager остаётся доступен через область уведомлений Windows и предоставляет быстрые действия.
- **Один Setup для установки и обновления.** Установщик сам определяет существующую установку, сохраняет зарегистрированные приложения и локальный `app.so`, обновляет общий runtime и перезапускает службы, которые работали до обновления.
- **Полностью офлайн-установка.** Опубликованный Setup уже содержит необходимую Windows-сборку ProjectDB и WinSW; во время установки доступ в интернет не требуется.
- **Проверка целостности.** Встроенные ProjectDB и WinSW проверяются по зафиксированным SHA-256 перед установкой.

## Скачать

Последнюю версию для **Windows x64** можно скачать в [GitHub Releases](https://github.com/pavel-elblaus/projectdb-service-manager/releases/latest):

`ProjectDB-Setup-<version>.exe`

Setup запрашивает права администратора, потому что устанавливает файлы в Program Files, создаёт службы Windows, настраивает права управления службами и добавляет Service Manager в автозапуск Windows.

Каталог установки по умолчанию:

`C:\Program Files\ProjectDB`

## Установка

1. Скачайте последний Setup из [GitHub Releases](https://github.com/pavel-elblaus/projectdb-service-manager/releases/latest).
2. Запустите `ProjectDB-Setup-<version>.exe`.
3. Подтвердите запрос прав администратора Windows.
4. Оставьте каталог установки по умолчанию или выберите другой.
5. Нажмите **Install**.
6. После завершения установки ProjectDB Service Manager запустится автоматически.

Setup устанавливает Windows runtime ProjectDB, ProjectDB Service Manager и необходимый компонент для запуска служб Windows.

Данные конкретного приложения во время установки не запрашиваются. Приложения регистрируются уже после установки из Service Manager.

## Добавление приложения

Откройте **ProjectDB Service Manager** и нажмите **Add application**.

Заполните:

| Поле | Назначение |
| --- | --- |
| **ProjectDB server address** | Сервер конфигурации приложения. По умолчанию используется `node.projectdb.pro`. |
| **Application name** | Идентификатор приложения ProjectDB, например `SHELL`. |
| **Access password** | Пароль для получения конфигурации приложения ProjectDB. |

Нажмите **Register**.

Service Manager создаст индивидуальную конфигурацию и службу Windows, после чего приложение появится в основном окне. Файлы ProjectDB используются повторно — отдельная копия ProjectDB для каждого приложения не создаётся.

## Управление приложениями

Каждое зарегистрированное приложение отображается отдельной карточкой.

Доступные действия:

| Действие | Назначение |
| --- | --- |
| **Start** | Запустить службу Windows приложения. |
| **Restart** | Перезапустить работающее приложение. |
| **Stop** | Остановить службу Windows приложения. |
| **Logs** | Открыть каталог логов приложения. |
| **Remove** | Удалить службу Windows, локальную конфигурацию службы и её логи. Общие файлы ProjectDB остаются установленными. |

В верхней панели доступны:

- **Add application** — зарегистрировать ещё одно приложение ProjectDB;
- **Restart all** — перезапустить все работающие зарегистрированные приложения;
- **Remove all** — удалить все зарегистрированные приложения, сохранив общий ProjectDB и Service Manager.

Идентификаторы служб используют короткий префикс `PDB`, имя приложения и стабильный суффикс для исключения конфликтов.

## Статус и последние события

Карточка приложения показывает состояние службы Windows цветным индикатором:

- зелёный — приложение работает и инициализировано;
- жёлтый — запуск, остановка или ожидание инициализации;
- красный — остановлено;
- серый — состояние недоступно или неизвестно.

Для работающего приложения также могут отображаться PID процесса и сведения об источнике/версии ProjectDB, которые сообщает само приложение.

В блоке **Last activity** отображается последняя структурированная запись лога. Сообщения с ошибками или сбоями выделяются, чтобы проблему было проще заметить.

## Управление `app.so`

Раздел **ProjectDB library** позволяет выбрать локальный файл `app.so`.

Выбранная локальная библиотека сохраняется в каталоге ProjectDB и имеет приоритет над штатным выбором библиотеки релиза. Service Manager сохраняет метаданные файла, а Setup не удаляет локальную библиотеку при обновлении ProjectDB.

Кнопка **Remove** в разделе библиотеки удаляет локальное переопределение и возвращает стандартный механизм выбора библиотеки ProjectDB.

## Обновление

Скачайте новый Setup и запустите его обычным способом.

Если существующая установка найдена, Setup автоматически переключается в режим **Update**. При обновлении он:

1. проверяет встроенные ProjectDB и WinSW;
2. запоминает, какие службы ProjectDB работали;
3. останавливает Service Manager и активные службы ProjectDB;
4. обновляет общий runtime ProjectDB и компоненты Service Manager;
5. сохраняет зарегистрированные приложения и локальный `app.so`;
6. при необходимости обновляет WinSW и команды существующих служб;
7. запускает обратно службы ProjectDB, которые работали до обновления;
8. снова запускает ProjectDB Service Manager.

При обычном обновлении удалять и заново регистрировать приложения не требуется.

## Удаление приложения и удаление ProjectDB — разные операции

Кнопка **Remove** в карточке приложения удаляет только службу этого приложения, её конфигурацию и логи. Общий ProjectDB и остальные приложения сохраняются.

**Remove all** удаляет все зарегистрированные приложения, но оставляет ProjectDB Service Manager и общий runtime ProjectDB установленными.

Чтобы удалить всю установку, используйте **Uninstall ProjectDB** в меню Service Manager в системном трее или стандартный раздел установленных приложений Windows.

## Структура установки

ProjectDB остаётся в корне установки ровно в том виде, в котором поставляется официальная Windows-сборка. Компоненты Service Manager находятся отдельно в `bin`.

Типовая структура:

```text
C:\Program Files\ProjectDB\
├─ projectdb.exe
├─ ... файлы релиза ProjectDB
├─ projectdb.ico
├─ THIRD-PARTY-NOTICES.txt
├─ bin\
│  ├─ projectdb-service-manager.exe
│  ├─ projectdb-service-control.exe
│  ├─ projectdb-log-wrapper.exe
│  ├─ projectdb-uninstall.exe
│  └─ winsw.exe
├─ service\
├─ log\
├─ lib\
└─ tmp\
```

Service Manager не переименовывает и не переносит файлы официальной сборки ProjectDB.

## Безопасность и управление службами

Установка выполняется с правами администратора. После установки службам ProjectDB назначаются права Windows, необходимые интерактивному пользователю для просмотра состояния и обычных операций Start/Stop/Restart из Service Manager без отдельного запроса UAC на каждое действие.

Вспомогательные EXE-файлы Service Manager, которые собираются локально во время установки, подписываются Authenticode локальным сертификатом издателя ProjectDB, созданным на компьютере и добавленным в локальные доверенные хранилища.

Перед копированием файлов офлайн-установщик проверяет SHA-256 встроенных пакетов ProjectDB и WinSW.

## Состав пакета

Текущая сборка включает:

| Компонент | Версия | Архитектура | Лицензия |
| --- | --- | --- | --- |
| ProjectDB | 3.4.0 | x64 | MIT |
| ProjectDB Service Manager | Текущий релиз | x64 | MIT |
| Windows Service Wrapper (WinSW) | 2.12.0 | x64 | MIT |

ProjectDB развивается отдельно в [репозитории ProjectDB](https://github.com/pavel-elblaus/projectdb).

Уведомления о сторонних лицензиях находятся в [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt) и устанавливаются вместе с программой.

## Сборка из исходного кода

Требования:

- Windows x64;
- Windows PowerShell;
- Go.

Клонируйте репозиторий и выполните:

```powershell
.\build-offline.ps1
```

Скрипт сборки скачивает зафиксированную Windows-сборку ProjectDB и WinSW, если их ещё нет в `payload`, проверяет SHA-256 и формирует автономный Setup EXE.

Крупные бинарные файлы ProjectDB и WinSW намеренно не хранятся в Git.

Тестовую сборку можно запустить вручную через **Actions → Build test installer**. В имя тестового артефакта добавляется номер GitHub Actions build; такой номер не является версией продукта и тестовые сборки не публикуются как Release.

Публичные релизы создаются автоматически по version tag через release workflow.

## Версионирование

Опубликованные версии используют Semantic Versioning.

- стабильные релизы: `1.0.0`, `1.1.0`, `2.0.0`, ...
- при необходимости release candidate: `1.0.0-rc.1`, ...
- разработческие версии могут иметь суффикс `-dev`.

Номера CI build используются только для идентификации тестовых артефактов и не являются версией продукта.

История изменений: [CHANGELOG.md](CHANGELOG.md).

## Лицензия

ProjectDB Service Manager распространяется по [лицензии MIT](LICENSE).

Сам ProjectDB также распространяется по MIT в отдельном репозитории. WinSW сохраняет собственные copyright-уведомления MIT; см. [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).

## Ссылки

- [ProjectDB](https://github.com/pavel-elblaus/projectdb)
- [Сайт ProjectDB](https://projectdb.pro)
- [Релизы ProjectDB Service Manager](https://github.com/pavel-elblaus/projectdb-service-manager/releases)
- [Windows Service Wrapper (WinSW)](https://github.com/winsw/winsw)
