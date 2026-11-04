# ЛР 6. Модель загроз

## Межі огляду

Рівень «Добре», baseline 2-A. Перевірено локальну робочу копію SecureLab
04.11.2026. Прочитаний HEAD має hash
`206838049353f8281bfbc6c10c94e9a182273354`. Він є орієнтиром початку роботи.
Перевірені зміни ЛР 6 перебувають у робочій копії. Відповідність робочої
копії commit, merge, тег і private delivery оформлює автор окремо.

У межах огляду API ASP.NET Core, PostgreSQL, Identity cookie authentication,
три сторінки Vanilla JS, конфігурація, NuGet graph, діагностика та audit events.
Стенд використовує .NET SDK 10.0.401, PostgreSQL 18.4, Windows, Chrome
156.0.8078.80. Для виконаних перевірок створено окрему БД `securelab_lr06`
на локальному порту 15432. Браузерна перевірка використовувала API 5086,
дозволений origin 4173 і контрольний origin 4174.

## Активи й точки входу

- Вміст, власник і статус Incident. Входи `/api/incidents`, `/{id}`, `/{id}/triage`.
- Облікові записи й сеанс. Входи `/api/auth/login`, `/register`, `/logout`, `/api/me`.
- Цілісність cookie-authenticated mutation. Вхід `/api/auth/csrf-token` і заголовок `X-CSRF-TOKEN`.
- Доступність пошуку й входу. Входи `/api/incidents/search?q=...` і `/api/auth/login`.
- Конфігурація, залежності та журнали. Входи environment, JSON configuration,
  NuGet restore, structured diagnostic logs і записи зміни статусу.

## Схема меж довіри

```text
Користувач / недовірений збережений текст
            |
            v
Браузер, DOM і cookie jar <--- сторонній origin 4173 або 4174
            | межа 1, HTTP та origin
            v
API, authentication -> authorization -> rate limit -> antiforgery
            | межа 2, параметризовані запити та перевірка ресурсу
            v
PostgreSQL, Identity та навчальні дані

Environment / NuGet ---> межа 3 ---> конфігурація і код API
API ---> межа 4 ---> diagnostic logs та audit trail
```

## Загрози, controls і перевірки

| Актив / межа | Загроза | Control | Конкретна перевірка |
|---|---|---|---|
| Сеанс / 1 | Підроблена identity, чужий ресурс | Identity cookie, IncidentAccess | Lab03AuthenticationTests, Lab04AuthorizationTests |
| Вміст / 1 | CSRF mutation | Standard antiforgery token pair | REG_CSRF_01_DenialPreservesState_ValidPairAllowsUpdate, CSRF_Perimeter_DenialPreservesDatabaseAndSession |
| DOM / 1 | Stored XSS, стороннє читання | textContent, CSP, exact-origin CORS | REG-XSS-01, REG-CORS-01 у security-review |
| Дані / 2 | SQL injection, invalid DTO | EF параметри, validation | Lab02ContractTests |
| Доступність / 1 | Зловживання search/login | Окремі fixed-window policies | SearchLimiter_IsolatesTwoSubjects_RejectsThenRecovers, LoginRateLimit_SharesQuota_ThenRecovers |
| Секрети / 3 | Потрапляння credentials до репозиторію | Environment, ignore rules, systematic scan | Розділ dependency and secret audit |
| Залежності / 3 | Відома вразливість пакета | NuGet audit, lock files | dotnet package list --vulnerable --include-transitive --no-restore |
| Діагностика / 4 | Внутрішні деталі в HTTP, credentials у log | ProblemDetails, trace, мінімальні structured fields | REG_HEADERS_01_ProductionErrorRetainsHeadersAndTrace, Audit_AllowedAndDeniedTriage_ContainsOnlyRequiredFields |

## Виключення й залишкові ризики

Реальні користувачі, зовнішні системи, TLS termination, production hosting,
distributed rate limiting, load testing, race conditions, Git history/stash,
інші клони й CI не перевірялися. Наявний search зберігає публічний контракт
ЛР 2, а severity summary агрегує всі інциденти. Ownership-scoping цих
маршрутів у попередній ЛР 4 був виключений із рівня «Добре» й потребує
окремого рішення перед використанням реальних даних.

Rate limits діють в одному процесі. XSS у власному origin може отримати
antiforgery token. Login/register приймають явні credentials і винесені
за межі cookie-authorized mutation policy. Production Secure cookies
потребують HTTPS. Доказ TestServer не перевіряє сертифікат або шифрування.
