# Лабораторна робота 6. Підсумковий security review

Обраний рівень «Добре». Baseline 2-A. Дата перевірки 04.11.2026.

## 1. Executive summary

Перевірено локальну робочу копію SecureLab, зокрема API, браузерний клієнт,
cookie authentication, PostgreSQL, конфігурацію, залежності та журнали.
Дві власні findings відтворено до змін і виправлено. Повний integration pack
пройшов, браузерні сценарії перевірено окремо. Перевірка стосується робочої
копії. Початковий прочитаний HEAD має hash
`206838049353f8281bfbc6c10c94e9a182273354`. Прийнятий commit, фінальний прогін
саме цього commit, анотований тег v1.0.0 та приватне подання ще має оформити
автор. Основні залишкові ризики охоплюють публічний search, глобальний
severity summary, локальні in-memory ліміти й відсутність перевірки реального
HTTPS deployment.

## 2. Scope та exclusions

Межі, активи, entry points і схема наведені в `docs/threat-model.md`.
Перевірка виконувалася на окремій тимчасовій БД `securelab_lr06`.
Реальні дані, зовнішні системи, Git history/stash/CI, distributed state,
конкурентні race conditions і production infrastructure виключено.
Погоджене advanced extension, Redis і третя finding поза checklist
не входять до обраного рівня «Добре».

## 3. System overview і trust boundaries

Browser HTML/JS викликає Minimal API. Identity встановлює principal з cookie.
IncidentAccess перевіряє видимість ресурсу та дозволені дії. EF Core/Npgsql
передає параметризовані запити в PostgreSQL. Конфігурація й NuGet graph
входять через окрему межу довіри. Diagnostic logs та audit trail є окремими
каналами виходу даних. Кожна межа має конкретний test у threat model.

## 4. Methodology та environment

- Windows, .NET SDK 10.0.401, runtime 10.0.12, PostgreSQL 18.4.
- NuGet lock files збережено. Нових package dependencies не додано.
- WebApplicationFactory з HTTPS BaseAddress перевіряє Development і Production.
- Chrome 156.0.8078.80 перевіряє реальний клієнт через HTTP localhost:5086.
- Для CORS використано localhost:4173 і localhost:4174 з окремими probe pages.
- До виправлення два нові targeted tests завершилися FAIL. Після виправлення
  повторено targeted tests, наявний integration pack і browser regression.
- Для збереження відтворюваності використовуються наявні CookieClient,
  SecureLabApiFactory, PostgreSQL fixtures і точні identifiers тестів.

## 5. Findings summary

| ID   | Пріоритет | Дефект                                           | Статус                | Retest         |
| ---- | --------- | ------------------------------------------------ | --------------------- | -------------- |
| F-01 | P1        | Cookie mutation без antiforgery validation       | Fixed у робочій копії | REG-CSRF-01    |
| F-02 | P2        | Security headers зникають після unexpected error | Fixed у робочій копії | REG-HEADERS-01 |

Спочатку усунуто пропуск перевірки, що допускав mutation. Потім виправлено
застосування browser policy до error responses. Картки містять окремі
reproduction, root cause, fix, retest, positive regression і residual risk.

## 6. Detailed findings

Повні картки подаються разом із цим review.

- `docs/findings/F-01-csrf.md`.
- `docs/findings/F-02-error-headers.md`.

Початковий targeted run фактично дав 2 failed / 0 passed. F-01 отримав 204
замість очікуваного 400, F-02 виявив відсутній X-Content-Type-Options у 500.
Виправлення відтворені в тестах, без тимчасового повернення вразливого коду.

## 7. Regression results

Команда повного pack наведена нижче. Пароль і connection string передаються
через environment. Порядок підготовки описаний у README, розділ 8.

```powershell
dotnet test tests/SecureLab.Api.Tests -c Release --no-restore
```

Фактичний прогін робочої копії 04.11.2026 дав 104 passed, 0 failed,
0 skipped. Окремий набір Lab06SecurityTests містить 13 test cases.
Після прийняття commit автор має повторити цей pack і зафіксувати результат
разом із hash у фінальному експорті.

| Сценарій                      | Test / спосіб перевірки                                                     | Фактичний результат                                                                   |
| ----------------------------- | --------------------------------------------------------------------------- | ------------------------------------------------------------------------------------- |
| T-_, S-_, SQLi, validation    | Lab02ContractTests, SearchMechanicsTests                                    | PASS, включно з literal metacharacters, sort allowlist, DTO boundaries                |
| A-\* authentication           | Lab03AuthenticationTests, Lab03GoodLevelTests, AuthenticationMechanicsTests | PASS, cookie identity, failed login, registration, logout                             |
| A-\* authorization/IDOR       | Lab04AuthorizationTests, Lab04AuditTests                                    | PASS, чужі ресурси, ролі, DTO tampering, audit denial                                 |
| Наявний client/list/summary   | IncidentEndpointTests                                                       | PASS, збережено чинний контракт                                                       |
| REG-CSRF-01                   | REG_CSRF_01_DenialPreservesState_ValidPairAllowsUpdate                      | 400 без token, 400 з invalid token, стан незмінний, 204 з valid pair, стан змінений   |
| Cookie mutation perimeter     | CSRF_Perimeter_DenialPreservesDatabaseAndSession                            | PASS для create, delete, triage, logout                                               |
| Зміна identity                | CSRF_IdentityChange_RejectsOldTokenAndAcceptsFreshToken                     | Старий token дає 400, новий дозволяє logout                                           |
| REG-HEADERS-01                | REG_HEADERS_01_ProductionErrorRetainsHeadersAndTrace                        | 500, три headers, safe JSON, linked trace                                             |
| Profiles/headers/cookies      | HeadersAndCookies_NormalAndDeniedResponses                                  | PASS для Development і Production                                                     |
| REG-CORS-01                   | REG_CORS_01_ExactOriginAndPreflight та Chrome                               | 4173 readable 200, 4174 browser denial                                                |
| Search quota                  | SearchLimiter_IsolatesTwoSubjects_RejectsThenRecovers                       | 200, 200, 429 для Alice, 200 для Bob, spoof header не обходить квоту, після вікна 200 |
| Login quota                   | LoginRateLimit_SharesQuota_ThenRecovers                                     | Квота, 429 і відновлення підтверджені                                                 |
| Audit/logs                    | Audit_AllowedAndDeniedTriage_ContainsOnlyRequiredFields                     | Allowed і denied transition, required fields, 0 sensitive matches                     |
| REG-XSS-01, CSP і normal flow | Chrome, Alice writer і окремий Morgan reader                                | Обидва поля literal, 0 img у DOM, marker не виконався, 0 CSP violations               |

### Browser regression і повторення

1. Увійти як Alice на `/auth-lab.html`. На `/browser-lab.html` створити новий
   навчальний інцидент із HTML-подібним текстом в описі. Зберегти його id.
2. Для перевірки другого sink додати навчальний публічний коментар із тим
   самим текстом у цей fixture через локальну БД. UI не має route створення
   коментаря. У виконаній перевірці скопійовано description у comment.text
   лише для створеного fixture в окремій БД.
3. У новому cookie-сеансі увійти як Morgan і прочитати fixture. Порівняти
   textContent опису й коментаря з JSON. Після повторного читання перевірити
   кількість коментарів і відсутність HTML-елементів збереженого тексту.
4. Перевірити головну сторінку, login/create/read/logout і DevTools Console
   на CSP violations. Для виконаного прогону значення становило 0.
5. Відкрити probe pages на 4173 і 4174. Через fetch з credentials include
   і `X-Lab-Cors-Probe` прочитати `/api/me` на API 5086. Дозволений origin
   дав readable 200, контрольний завершився browser fetch rejection.
6. Видалити створений fixture. Виконаний fixture разом із коментарем прибрано.

У фактичному прогоні login дав 204, створення 201, читання 200, logout 204
і наступний `/api/me` 401. У separate-reader перевірці descriptionLiteral
і commentLiteral дорівнювали true, кількість повторних коментарів залишилася 1.

## 8. Hardening controls

| Control                     | Ризик                                      | Verification                          | Межа застосування                                    |
| --------------------------- | ------------------------------------------ | ------------------------------------- | ---------------------------------------------------- |
| Antiforgery token pair      | Cookie CSRF                                | REG-CSRF-01 і perimeter tests         | Не зупиняє same-origin XSS                           |
| IncidentAccess              | Читання/зміна чужого ресурсу               | Lab04AuthorizationTests               | Search/summary scope описано в residual risks        |
| CSP, nosniff, no-referrer   | Browser injection та небажане вбудовування | Headers matrix і Chrome               | Scalar CSP exemption лише в Development              |
| Exact-origin CORS           | Стороннє browser reading                   | REG-CORS-01                           | Діє в Development, server authorization окрема       |
| Safe ProblemDetails + trace | Розкриття внутрішніх деталей               | REG-HEADERS-01                        | Захист внутрішнього log sink залежить від deployment |
| Search/login rate limit     | Зловживання запитами                       | Quota, isolation, 429, recovery tests | In-memory, один API process                          |
| Structured audit            | Втрата відомостей про привілейовану дію    | Audit allowed/denied test             | Подальше зберігання/retention поза локальним scope   |

### Cookie mutation perimeter

| Method і route                    | Передумова                        | Перевірка                                                       |
| --------------------------------- | --------------------------------- | --------------------------------------------------------------- |
| POST `/api/incidents`             | Authenticated cookie              | Missing token 400, count unchanged; valid create у Lab02/Lab03  |
| PUT `/api/incidents/{id}`         | Authenticated cookie + permission | REG-CSRF-01 та Lab04 allowed/denied cases                       |
| DELETE `/api/incidents/{id}`      | Owner, статус New                 | Missing token 400, resource unchanged; valid delete у Lab04     |
| POST `/api/incidents/{id}/triage` | Analyst, статус New               | Missing token 400; valid allowed/denied audit                   |
| POST `/api/auth/logout`           | Authenticated cookie              | Missing token 400, session alive; valid token 204, session ends |
| PATCH                             | Route відсутній                   | Method врахований middleware                                    |

Login і registration мають публічний credential contract. Token route
потребує авторизації. Cookie token HttpOnly, SameSite=Strict. Session cookie
HttpOnly, SameSite=Lax. У Production обидві cookie завжди Secure, у
Development використовують SameAsRequest. Token response має no-store.

Development має автоматичні migrations/seed, OpenAPI/Scalar і localhost CORS.
Production їх вимикає та потребує зовнішнього connection string.
HTTPS redirection і HSTS не ввімкнені на локальному HTTP-only стенді.
Реальний production transport потребує окремої конфігурації та перевірки.

## 9. Dependency and secret audit

### NuGet

04.11.2026, SDK 10.0.401. Обидві команди завершилися з exit code 0.

```powershell
dotnet restore SecureLab.sln --locked-mode
dotnet package list --vulnerable --include-transitive --no-restore
```

Очищений текст результату audit наведено нижче без локального
шляху до стандартного offline package source.

```text
Использовались следующие источники:
   https://api.nuget.org/v3/index.json
   Microsoft SDKs/NuGetPackages (локальне offline-джерело)
Указанный проект "SecureLab.Api" не содержит уязвимых пакетов для текущих источников.
Указанный проект "SecureLab.Api.Tests" не содержит уязвимых пакетов для текущих источников.
```

| Обсяг                                          | Результат                 | Рішення                                                       | Межа висновку                                 |
| ---------------------------------------------- | ------------------------- | ------------------------------------------------------------- | --------------------------------------------- |
| Direct і transitive dependencies обох проєктів | Advisories не повідомлені | Зберегти поточний graph і lock files, suppression не додавати | Відомі advisories цих sources на дату запуску |
| Наявний Microsoft.OpenApi 2.7.5 override       | Поточний audit чистий     | Зберегти вже наявний patched override                         | Досяжність невідомих дефектів не доведена     |

### Систематичний secret search

Git-команди не запускалися. Список поточних tracked paths прочитано
безпосередньо з `.git/index` у read-only режимі. Index v2 містив 121 шлях.
120 існуючих tracked files перевірено регулярним виразом на password
assignments, ConnectionStrings, bearer-like values, private key headers,
api keys і client secrets. Знайдено 38 keyword matches у 16 файлах.
Переглянуто кожну групу збігів.

| Група                                                                        | Matches | Рішення                                                                            |
| ---------------------------------------------------------------------------- | ------- | ---------------------------------------------------------------------------------- |
| README і попередні docs                                                      | 8       | Інструкції environment і назви параметрів, реальні credentials відсутні            |
| infra/compose.yaml, appsettings.Development.json, DesignTimeDbContextFactory | 5       | Публічні local-study значення для loopback БД, непридатні для реального deployment |
| check-lab04.py, auth-lab.js, DbSeeder.cs                                     | 7       | Читання environment/input і очищення password field, збереженого пароля немає      |
| Наявні tests                                                                 | 18      | Environment-backed seed password та імена DTO properties                           |

Один tracked шлях `docs/ПТПІБ_ЛР5_Орленко.docx` уже був відсутній на початку
огляду. Однойменний файл у корені перевірено окремо через XML усередині DOCX,
отримано 0 matches. Відповідність цього переміщення index автор перевіряє
під час власного Git review. Нові файли ЛР 6 перевірено окремо.

PNG перевірено як bytes/metadata, OCR їхнього тексту не виконувався.
Історія, stash, archives поза tracked scope, інші клони й CI виключені.
Regex scan має false negatives і сам по собі не доводить відсутність усіх
можливих секретів. `.gitignore` виключає environment files, private keys,
local settings, dumps і logs. `.env.example` містить тільки порт.
Для реального leaked secret потрібні rotation/revocation і оцінка історії,
видалення поточного рядка не усуває попереднє розкриття.

### Log і audit review

Переглянуто captured ILogger events для successful login, failed login,
denied action, validation, unexpected error і privileged transition.
У normal scenarios точних збігів із тестовими password values,
`X-CSRF-TOKEN` і `Set-Cookie` не виявлено. У controlled error перевірено
відсутність пароля запиту й пароля недосяжної навчальної БД.
EF command logging у базовому profile встановлено Warning.

Для Incident.Status використовуються події `incident.triaged` і
`authorization.denied`. Перевірені поля охоплюють subject, action, resource,
result, time і trace. Reporter denial зберігає стан, Morgan transition
змінює New на Triaged. Audit test перевіряє allowlist полів.

Safe error response у виконаному targeted run мав status 500 і trace
`00-9c6489f88eb552b10a4083c2ca66f106-362ebcbc23a80a30-00`.
Captured diagnostic event категорії
`Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware` мав той самий
Activity ID. У стандартному console scope його короткий TraceId становить
`9c6489f88eb552b10a4083c2ca66f106`. Порівнюється цей сегмент із traceparent,
або повний Activity ID у test capture. Повні logs і secrets до звіту не включені.

## 10. Residual risks

- Публічний search і глобальний summary зберігають попередній scope.
  Перед реальними даними потрібне рішення щодо ownership-scoping.
- In-memory quota скидається при рестарті й не є спільною між instances.
- Same-origin XSS може отримати request token. Безпечні DOM sinks обов'язкові.
- Реальні TLS, HSTS, reverse proxy, retention і контроль доступу до logs
  ще потребують перевірки в цільовому deployment.
- Secret scan обмежений доступними файлами й описаним методом.
- Git delivery і оцінка на захисті не підтверджені цим локальним прогоном.

## 11. Conclusion і підготовка захисту

Код і локальні перевірки реалізовані для рівня «Добре». Перед поданням автор
перевіряє зміни, оформлює прийнятий commit, повторює full regression,
створює анотований v1.0.0 і додає hash та результат прогону до PDF export.
Далі подає PDF і private repository, або ZIP із кодом/tests/docs та hash.
Для потрібної оцінки необхідно також підтвердити розуміння на захисті.

Для репетиції змініть quota/window search limiter у test configuration,
передбачте запит із 429 та поясніть незалежність Bob від Alice. Інший
короткий сценарій полягає у вилученні token header з valid PUT і перевірці
незмінності ресурсу. Поясніть, чому CORS, SameSite, authentication,
authorization і antiforgery виконують різні функції.

## 12. Evidence appendix

| ID    | Доказ                                      | Як повторити                                                  |
| ----- | ------------------------------------------ | ------------------------------------------------------------- |
| EV-01 | F-01 before FAIL і fixed REG-CSRF-01       | Картка F-01, Lab06SecurityTests                               |
| EV-02 | F-02 before FAIL і fixed 500/trace/headers | Картка F-02, REG_HEADERS_01                                   |
| EV-03 | Full integration pack                      | dotnet test tests/SecureLab.Api.Tests -c Release --no-restore |
| EV-04 | Browser XSS/CSP/CORS                       | Кроки browser regression у розділі 7                          |
| EV-05 | Dependency audit і secret search           | Розділ 9, lock files, scan scope                              |
| EV-06 | Allowed/denied audit і safe logs           | Audit_AllowedAndDeniedTriage_ContainsOnlyRequiredFields       |

Наявний Python Client у `scripts/check-lab04.py` окремо пройшов login,
create 201, DTO validation 400 з `errors.title` та cleanup DELETE 204.
Його запуск для цієї перевірки виконувався в пам'яті без перезапису старих
evidence files ЛР 4.
