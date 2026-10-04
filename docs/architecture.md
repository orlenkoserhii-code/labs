# Карта архітектури

Один server project (ASP.NET Core Minimal API) із трьома шарами-каталогами, PostgreSQL у Docker Compose і браузерний клієнт на Vanilla JavaScript. Опис прив'язано до маршруту `GET /api/incidents/severity-summary` (ЛР 1, baseline 2-A).

## Компоненти

| Компонент        | Розташування                   | Відповідальність                                                                    |
| ---------------- | ------------------------------ | ----------------------------------------------------------------------------------- |
| Browser client   | `src/SecureLab.Api/Client/`    | Викликає API через `fetch`, показує результат через DOM API (`textContent`)         |
| ASP.NET Core API | `src/SecureLab.Api/Program.cs` | DI, middleware pipeline, статичні файли, реєстрація endpoints                       |
| Presentation     | `Presentation/`                | Маршрути, читання зовнішніх параметрів, HTTP-відповіді, response DTO                |
| Application      | `Application/`                 | Сценарій отримання даних (`IncidentQueries`)                                        |
| EF Core / Data   | `Data/`                        | `SecureLabDbContext`, entities, migration, seed/reset; Npgsql перекладає LINQ в SQL |
| PostgreSQL       | `infra/compose.yaml`           | Зберігає навчальні дані у локальному контейнері                                     |

## Вибраний маршрут: `GET /api/incidents/severity-summary`

```text
Client/index.html  (блок «Підсумок за критичністю», <ul id="severity-summary">)
  → Client/app.js  loadSeveritySummary()  → apiFetch("/api/incidents/severity-summary")
  → GET /api/incidents/severity-summary
  → Presentation/Endpoints/IncidentEndpoints.cs  GetSeveritySummaryAsync
  → Application/Incidents/IncidentQueries.cs     GetSeveritySummaryAsync
  → Data/SecureLabDbContext.cs  Incidents  → таблиця incidents (GROUP BY severity)
  → Presentation/Contracts/IncidentResponses.cs  IncidentSeveritySummaryResponse(Severity, Count)
  → JSON (200)
  → Client/app.js  createElement + textContent у списку підсумку
```

Політика: повний перелік рівнів (`Enum.GetValues<IncidentSeverity>()`), відсутні групи мають `count: 0`; порядок сталий: Critical → High → Medium → Low. На штатному seed: Critical 0, High 1, Medium 1, Low 1.

### Ключові файли

| Роль            | Файл                                                       |
| --------------- | ---------------------------------------------------------- |
| Клієнт          | `Client/index.html`, `Client/app.js`                       |
| Endpoint        | `Presentation/Endpoints/IncidentEndpoints.cs`              |
| Application     | `Application/Incidents/IncidentQueries.cs`                 |
| Response DTO    | `Presentation/Contracts/IncidentResponses.cs`              |
| DbContext       | `Data/SecureLabDbContext.cs`                               |
| Entity          | `Data/Entities/Incident.cs` (`IncidentSeverity`)           |
| Таблиця         | `incidents` (колонка `severity`, enum зберігається рядком) |
| Тест            | `tests/SecureLab.Api.Tests/IncidentEndpointTests.cs`       |
| Ручний сценарій | `tests/http/incidents.http`                                |

## Межі довіри

| Межа                   | Які дані її перетинають                    | Чому даним ще не можна довіряти                                               | Де перевіряємо або обмежуємо                                                                                 |
| ---------------------- | ------------------------------------------ | ----------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------ |
| Browser client → API   | HTTP method, URL, headers, query-параметри | Запит можна створити поза UI (curl, Burp); клієнт контролює все               | Маршрутизація ASP.NET Core; summary без вхідних параметрів; для list-flow `Enum.TryParse` + `Enum.IsDefined` |
| API → PostgreSQL       | SQL від EF Core, результат `GROUP BY`      | Рядок із БД не має потрапляти в запит як конкатенація                         | EF Core параметризує запити; єдиний raw SQL (`TRUNCATE` у reset) — константа, лише Development               |
| PostgreSQL → API → DOM | `severity` (рядок) і `count` у JSON        | У БД може бути раніше збережений недовірений текст                            | DTO повертає лише два поля; клієнт пише через `textContent`; тест забороняє `innerHTML` у `app.js`           |
| Конфігурація → API     | connection string, прапорці міграцій/reset | Значення можуть бути підмінені середовищем; секрети не мають потрапляти в Git | `ConnectionStrings__SecureLab` поза Git; reset лише в Development з `Database:AllowReset=true`               |

## Конфігураційні входи та їх залежності

| Вхід                                    | Що задає                                                                        | Залежність                                                                                                     |
| --------------------------------------- | ------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------- |
| `global.json`                           | Версію .NET SDK (`10.0.302`)                                                    | Має відповідати `net10.0` у `SecureLab.Api.csproj`; інакше `dotnet` не знайде SDK                              |
| `Directory.Build.props`                 | Спільні налаштування збірки (`TreatWarningsAsErrors`)                           | Застосовується до API й тестів; попередження стилю ламають збірку                                              |
| `appsettings.json`                      | Безпечні значення за замовчуванням: міграції та reset вимкнені                  | Перевизначається `appsettings.Development.json`                                                                |
| `appsettings.Development.json`          | Локальний connection string, `ApplyMigrationsOnStartup=true`, `AllowReset=true` | Діє лише за `ASPNETCORE_ENVIRONMENT=Development`; від нього залежать migration при старті й `--reset-database` |
| `ConnectionStrings__SecureLab` (env)    | Перевизначає connection string з appsettings                                    | Пріоритет вище за JSON; порт і облікові дані мають збігатися з `infra/compose.yaml`                            |
| `infra/compose.yaml` (+ `.env.example`) | Версію PostgreSQL, порт, локальні навчальні облікові дані                       | Зміна порту в `infra/.env` вимагає узгодженого `ConnectionStrings__SecureLab` для API і тестів                 |

Реальні значення connection string у документі не наводяться.

## Повернення до відомого seed-стану

```bash
dotnet run --project src/SecureLab.Api -- --reset-database
```

Команда застосовує migrations, виконує `TRUNCATE` відомих навчальних таблиць і повторно заповнює seed (`DbSeeder`). Працює лише за `Development` і `Database:AllowReset=true`, інакше завершується помилкою. Після reset повторюють T-02 і T-06.
