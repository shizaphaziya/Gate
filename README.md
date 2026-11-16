# Gate

Gate — система удалённого управления службами **systemd** и просмотра журналов **journalctl** на Linux-серверах
с разграничением прав по ролям (RBAC) и журналом аудита.

- **Gate.Server** — демон на Linux. Принимает подключения по TCP (порт `5050`, TLS), проверяет логин и права,
  выполняет `systemctl` / `journalctl`, пишет аудит в PostgreSQL.
- **Gate.Client** — десктопное приложение (Avalonia, MVVM). Вход, список служб, запуск/остановка/перезапуск,
  логи, аудит, установка сервера по SSH, темы оформления.
- **Gate.Shared** — общий протокол и DTO.
- **Gate.Tests** — тесты xUnit.
- **test-env** — Docker-контейнер с Ubuntu 24.04, systemd и SSH для разработки.

## Структура

```
Gate.sln
├── Gate.Shared/            Enums.cs, Contracts.cs, PacketProtocol.cs
├── Gate.Server/
│   ├── Program.cs          запуск как systemd-служба, DI, сидирование БД
│   ├── Services/
│   │   ├── TcpGateServer.cs          TCP+TLS сервер, сессии, обработчики запросов
│   │   ├── TlsCertificateProvider.cs загрузка/генерация сертификата сервера
│   │   ├── SystemdManager.cs         systemctl / journalctl
│   │   └── PermissionChecker.cs      права по маске имени службы
│   ├── Database/           Entities.cs, GateDbContext.cs, DbInitializer.cs
│   └── appsettings*.json
├── Gate.Client/
│   ├── Views/              MainWindow, TestServerWindow
│   ├── ViewModels/         MainWindowViewModel и остальные
│   └── Services/
│       ├── GateNetworkClient.cs      TCP+TLS клиент
│       ├── CertificateTrustStore.cs  закрепление сертификатов серверов (как known_hosts)
│       ├── SshDeployerService.cs     установка, проверка и сброс Gate по SSH
│       ├── ServerPackageResolver.cs  поиск и сборка бинарника Gate.Server
│       ├── RepoLocator.cs            поиск корня репозитория
│       ├── TestServerManager.cs      управление тестовым Docker-контейнером
│       └── ThemeService.cs, StatusPalette.cs
├── Gate.Tests/             UnitTest1.cs
└── test-env/               Dockerfile, docker-compose.yml, start/stop-скрипты
```

## Протокол

Пакет: 4 байта длины (little-endian) + JSON `NetworkPacket { Type, SessionToken, PayloadJson }`.
Максимальный размер — 10 МБ. Поверх TCP используется TLS 1.2/1.3.

Типы пакетов: `AuthRequest/Response`, `GetServicesRequest/Response`, `ServiceActionRequest/Response`,
`GetLogsRequest/Response`, `GetAuditRequest/Response`. После входа сервер выдаёт токен сессии,
который клиент передаёт в каждом следующем пакете.

## Роли и права

Права задаются шаблоном имени службы (`*`, `nginx*`, точное имя) и флагами `CanManage` / `CanViewLogs`.
При первом запуске сидируются роли и пользователи:

| Пользователь | Пароль     | Роль      | Доступ                                         |
|--------------|------------|-----------|------------------------------------------------|
| `admin`      | `admin123` | Admin     | все службы: управление и логи                  |
| `dev`        | `dev123`   | Developer | `nginx*`, `dummy-*`, `test-*`: управление и логи |
| `auditor`    | `audit123` | Auditor   | все службы: только логи                        |

Каждое действие (успех, отказ `FORBIDDEN`, ошибка) записывается в таблицу аудита.

> Это демонстрационные учётные данные. Для реального использования смените их.

## Требования

- .NET SDK 8.0
- PostgreSQL (для сервера; при установке по SSH он ставится и настраивается автоматически)
- Docker — только для тестового окружения

## Быстрый старт с тестовым окружением

```bash
# 1. Поднять тестовый Linux (SSH на 127.0.0.1:2222, root/root, Gate на 5050)
./test-env/start-test-env.sh

# 2. Собрать бинарник сервера
dotnet publish Gate.Server -c Release -r linux-x64 --self-contained false \
    -p:PublishSingleFile=true

# 3. Запустить клиент
dotnet run --project Gate.Client
```

В клиенте откройте установку по SSH (`127.0.0.1`, порт `2222`, `root` / `root`) и установите Gate.
Затем войдите в Gate на `127.0.0.1:5050` под `admin` / `admin123`.
Если бинарник не найден, клиент сам попробует собрать его через `dotnet publish`.

Остановить окружение: `./test-env/stop-test-env.sh`.

## Что делает установка по SSH

1. Создаёт `/opt/gate`.
2. Проверяет PostgreSQL и создаёт базу `gatedb` и пользователя `gateuser`.
3. Создаёт демо-службу `dummy-worker.service`.
4. Загружает бинарник `Gate.Server` и `appsettings.json`.
5. Создаёт `gate.service` (`Type=notify`).
6. Включает и запускает службы, затем читает отпечаток сертификата сервера и закрепляет его в клиенте.

Также есть проверка с автоматическим восстановлением и полный сброс.

## TLS

Канал клиент–сервер шифруется TLS. Сервер при первом запуске создаёт самоподписанный сертификат
(`gate-server.pfx`, права `600`) и записывает его SHA-256 в `gate-server.pfx.sha256`.

Клиент закрепляет отпечаток для каждого `host:port` в `known_hosts.json`
(`~/.config/Gate/known_hosts.json` в Linux) по модели SSH:

- при установке по SSH отпечаток читается по уже проверенному SSH-каналу и закрепляется сразу;
- иначе сертификат закрепляется при первом подключении (trust on first use);
- если сертификат сменился, вход блокируется с сообщением о возможной подмене.
  Если Gate переустановлен, выполните установку заново или удалите запись о хосте.

Настройки сервера (`appsettings.json` или переменные окружения):

| Параметр                       | Переменная                          | По умолчанию                       |
|--------------------------------|-------------------------------------|------------------------------------|
| `Gate:Tls:CertificatePath`     | `Gate__Tls__CertificatePath`        | `gate-server.pfx` рядом с бинарником |
| `Gate:Tls:CertificatePassword` | `Gate__Tls__CertificatePassword`    | пустой                             |

Можно подставить собственный PFX-сертификат.

## Переменные окружения клиента

| Переменная           | Назначение                                              |
|----------------------|---------------------------------------------------------|
| `GATE_REPO_ROOT`     | корень репозитория (папка с `Gate.sln`)                 |
| `GATE_SERVER_BINARY` | путь к собранному `Gate.Server`                         |
| `GATE_TEST_ENV_DIR`  | путь к папке `test-env`                                 |
| `GATE_KNOWN_HOSTS`   | путь к файлу закреплённых сертификатов                  |

Без них пути ищутся относительно текущей папки и вверх от неё до `Gate.sln`.

## Тесты

```bash
dotnet test
```

Модульные тесты (протокол, RBAC, хранилище сертификатов) работают без окружения.
Интеграционные тесты требуют запущенный `test-env` (SSH на `2222`, `docker` в `PATH`);
тест установки пропускается, если бинарник `Gate.Server` не собран.

## Настройки сервера

`Gate.Server/appsettings.json`: строка подключения `ConnectionStrings:GateDb`
(по умолчанию `localhost:5432`, база `gatedb`, пользователь `gateuser`) и уровни логирования.
Порт TCP (`5050`) пока зашит в `TcpGateServer`.
