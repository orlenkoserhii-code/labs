# SecureLab starter: трекер інцидентів

Це baseline starter для дисципліни «Прикладні технології програмування в
інформаційній безпеці», варіант **2-A**. Він містить один простий
трирівневий моноліт, локальний PostgreSQL, мінімальний Vanilla JavaScript
клієнт, відтворювані seed/reset і готовий `WebApplicationFactory` test harness.

Поточна робоча копія містить зміни **ЛР 6, рівень «Добре»**. Результати
перевірки й межі огляду наведено в `docs/security-review.md`.
Студент послідовно розвиває власний проєкт:
результат завершеної ЛР стає основою наступної. Якщо попередню роботу технічно
не завершено, її потрібно завершити або виправити перед переходом далі.

Навчальні перевірки виконуються локально зі штучними даними.

## 1. Передумови

- .NET SDK 10.0, обраний `global.json` через `10.0.100 / latestFeature`;
- Docker із підтримкою `docker compose`;
- Git;
- вільні локальні порти `5080` і `54329`.

Перевірте:

```bash
dotnet --version
docker --version
docker compose version
```

## 2. Перший запуск

У корені starter виконайте:

```bash
docker compose --env-file infra/.env.example -f infra/compose.yaml up -d --wait
dotnet tool restore
dotnet restore SecureLab.sln
dotnet run --project src/SecureLab.Api
```

Під час першого Development-запуску API застосує готову EF Core migration і
додасть фіксовані навчальні дані. Відкрийте:

- browser client: <http://localhost:5080/>;
- health/readiness: <http://localhost:5080/health>;
- інтерактивний OpenAPI: <http://localhost:5080/scalar/v1>;
- OpenAPI JSON: <http://localhost:5080/openapi/v1.json>.

Зупиніть API через `Ctrl+C`. Контейнер БД зупиняється командою:

```bash
docker compose --env-file infra/.env.example -f infra/compose.yaml down
```

## 3. Відомий початковий стан

Seed містить Alice, Bob, Morgan (Analyst) та Admin, три інциденти, два
публічні коментарі й одну зміну статусу. UUID не випадкові: їх можна
використовувати у `.http`-сценаріях і тестах.

Локальні значення `securelab / local-study-password` є лише відкритими
навчальними credentials для БД, прив'язаної до `127.0.0.1`. Це не секрет і
його заборонено повторно використовувати для іншої БД чи середовища. Реальний
connection string передавайте поза Git через `ConnectionStrings__SecureLab`.

Щоб застосувати наявні migrations, **очистити лише відомі навчальні таблиці**
локальної БД і повернути seed до відомого стану:

```bash
dotnet run --project src/SecureLab.Api -- --reset-database
```

Команда навмисно відмовляється працювати поза явно налаштованим Development
environment. Вона знищує поточні локальні навчальні дані у відомих таблицях
БД `securelab`, але не видаляє саму БД або її схему.

## 4. Перевірка

Коли PostgreSQL уже працює:

```bash
dotnet build src/SecureLab.Api/SecureLab.Api.csproj --configuration Release
dotnet test tests/SecureLab.Api.Tests/SecureLab.Api.Tests.csproj --configuration Release
```

На macOS/Linux або у Git Bash увесь сценарій підняття PostgreSQL і тестів
виконує одна команда:

```bash
bash scripts/test.sh
```

HTTP-сценарії для ручної перевірки розташовані у `tests/http/incidents.http`.
Вони охоплюють `200`, `400`, `404` та початковий `501` для точки розширення.

## 5. Завдання ЛР 1 у starter

Працюючий близький приклад — фільтр інцидентів за `status` через browser → API
→ EF Core → PostgreSQL → JSON → безпечні DOM sinks. Обмежена точка зміни —
`GET /api/incidents/severity-summary`, який у baseline повертає `501`.

Під час ЛР 1 студент реалізує підсумок кількості інцидентів за severity,
викликає endpoint із клієнта, безпечно відображає результат, додає
відтворюваний HTTP-сценарій або адаптує інтеграційний тест і доповнює
`docs/architecture.md`. Зміна схеми БД для мінімального маршруту не потрібна.

## 6. Структура

```text
src/SecureLab.Api/
  Presentation/   endpoints і зовнішні response contracts
  Application/    сценарії застосунку
  Data/           DbContext, entities, migration, seed/reset
  Client/         HTML/CSS/Vanilla JavaScript
tests/
  SecureLab.Api.Tests/   інтеграційні й security regression приклади
  http/                  ручні HTTP-сценарії
infra/                   локальний PostgreSQL
docs/                    карта архітектури, варіант, матриця доступу, звіт
```

Це не Clean/Onion Architecture, DDD, мікросервіси або вимога TDD. Каталоги
позначають відповідальність коду в одному server project.

## 7. Типові проблеми запуску

| Ознака | Перевірка |
|---|---|
| `connection refused` до PostgreSQL | `docker compose ... ps`, порт `54329`, стан healthcheck |
| SDK не знайдено | `dotnet --version` має повернути встановлену стабільну версію 10.0 у цій директорії |
| порт API зайнятий | зупиніть інший процес на `5080` або задайте `ASPNETCORE_URLS` |
| migration не застосовується | перевірте `ASPNETCORE_ENVIRONMENT=Development` і connection string |
| змінили credentials у compose | узгоджено перевизначте `ConnectionStrings__SecureLab`; не записуйте реальне значення до Git |

Перед commit перегляньте `git status`, `git diff --staged` і переконайтеся, що
до Git не потрапили `.env`, cookies, tokens, реальні passwords, dumps або logs.

## 8. ЛР 6. Запуск і регресія

Для наявної навчальної БД використайте її seed password. Для нової порожньої
БД задайте власний навчальний пароль. Перевірки створюють і прибирають свої
тестові записи. API і тести мають отримати однаковий connection string.

```powershell
$studySecret = Read-Host 'Навчальний пароль Alice, Bob, Morgan' -AsSecureString
$env:SeedUsers__Password = [Net.NetworkCredential]::new('', $studySecret).Password
Remove-Variable studySecret
# За нестандартного порту/назви БД задайте ConnectionStrings__SecureLab у цьому процесі.
dotnet restore SecureLab.sln --locked-mode
dotnet package list --vulnerable --include-transitive --no-restore
dotnet test tests/SecureLab.Api.Tests -c Release --no-restore
dotnet run --project src/SecureLab.Api -c Release --no-launch-profile --environment Development --urls http://localhost:5080
```

Відкрийте `/auth-lab.html`, увійдіть, створіть інцидент і перевірте вихід.
На `/browser-lab.html` можна створити запис і прочитати його з іншого
сеансу Analyst. Обидві сторінки використовують спільний `csrf.js`.

Для ручного API-клієнта порядок такий. Спочатку виконайте login, потім
`GET /api/auth/csrf-token` тією самою cookie-сесією. Передавайте поле `token`
у `X-CSRF-TOKEN` для POST/PUT/PATCH/DELETE захищених ресурсів і logout.
Після зміни користувача отримайте новий токен. REST Client-приклади
адаптовано у `tests/http/lab-02-checks.http` і `tests/http/lab-04-access.http`.

```powershell
dotnet test tests/SecureLab.Api.Tests -c Release --filter 'FullyQualifiedName~Lab06SecurityTests'
```

Звичайний search limiter має 60 запитів на 60 секунд. Ключ для авторизованого
клієнта береться з перевіреного subject ID, для анонімного з'єднання з IP.
`SearchRateLimit__PermitLimit` і `SearchRateLimit__WindowSeconds` змінюють
параметри запуску. Тести quota використовують 2 запити / 3 секунди.
Загальний test factory підвищує квоти до 1000, а окремі limiter-тести явно
встановлюють низькі квоти й перевіряють 429 та відновлення.

Production потребує `ConnectionStrings__SecureLab`, вимикає automatic
migrations/reset, OpenAPI, Scalar і Development CORS, вимагає Secure cookies.
Production integration-тест використовує TestServer із HTTPS BaseAddress.
Справжній HTTPS deployment і HSTS перевіряються окремо.

Після власного прийняття змін повторіть повний pack на прийнятому commit,
створіть анотований `v1.0.0` і внесіть його hash у фінальний експорт звіту.
Git-дії та приватне подання виконує автор роботи.
