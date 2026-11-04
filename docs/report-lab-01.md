# Звіт до лабораторної роботи № 1

## Запуск, дослідження та розширення вебсистеми SecureLab

**Статус:** оформлено за кодом і Git станом на 20.10.2026; комплект доказів потребує доповнення. Під час підготовки цього файла build, tests, reset та браузерні сценарії не запускалися. Наявність реалізації або тесту не позначається як успішний фактичний запуск.

## 1. Ідентифікація

| Параметр                          | Значення                                                  |
| --------------------------------- | --------------------------------------------------------- |
| Baseline                          | 2-A «Трекер інцидентів»                                   |
| Заявлений рівень                  | «Добре»; остаточне підтвердження після завершення доказів |
| Робоча гілка                      | `lab/1-system`                                            |
| Основна гілка                     | `main`                                                    |
| Підсумковий тег                   | `v0.1.0`                                                  |
| Commit під тегом                  | `1f62a06983e567d39652277da202dc61d89719db`                |
| Репозиторій                       | `git@github.com:orlenkoserhii-code/labs.git`              |
| Доступ викладача / подання Moodle | Не перевірено під час оформлення                          |

Hash перевірено через Git. У попередньому звіті було вказано старий merge commit `930dc7f`; цей запис замінено фактичним commit під тегом. Наявний `v0.1.0` є lightweight tag, хоча методичка вимагає annotated tag. Історію та вже створений тег під час оформлення не змінено.

## 2. Мета і маршрут запиту

Мета: запустити локальний трирівневий моноліт, простежити HTTP-запит крізь компоненти та реалізувати підсумок кількості інцидентів за критичністю.

```text
Client/index.html
  → Client/app.js: loadSeveritySummary()
  → GET /api/incidents/severity-summary
  → Presentation/Endpoints/IncidentEndpoints.cs: GetSeveritySummaryAsync
  → Application/Incidents/IncidentQueries.cs: GetSeveritySummaryAsync
  → SecureLabDbContext.Incidents → PostgreSQL incidents, GROUP BY severity
  → IncidentSeveritySummaryResponse → JSON
  → createElement / textContent у браузері
```

Карта компонентів, межі довіри, конфігураційні входи та спосіб reset описані в `docs/architecture.md` перевіреного стану. Browser/API є межею недовіреного вводу; API/БД - межею доступу до даних; JSON/DOM - межею безпечного відображення.

## 3. Реалізація і контракт

- Endpoint `GET /api/incidents/severity-summary` повертає DTO з полями `severity` і `count`.
- Необов'язковий `status` перевіряється за переліком назв enum до запиту до БД. Невідомі та числові значення відхиляються як 400.
- Фільтрація виконується перед групуванням. EF Core будує агрегацію на стороні БД.
- Повертається повний перелік: Critical → High → Medium → Low. Відсутні групи мають `count: 0`.
- UI містить стани завантаження, порожньої відповіді й безпечної помилки. Виведення здійснюється через DOM API та `textContent`.
- Структурований log містить фільтр і кількість груп. Фактичний фрагмент виконання ще потрібно додати до доказів.

Фрагмент маршруту в стані тега:

```csharp
return Results.Ok(await queries.GetSeveritySummaryAsync(parsedStatus, cancellationToken));

var query = dbContext.Incidents.AsNoTracking();
if (status is not null)
    query = query.Where(incident => incident.Status == status);
var groupedData = await query
    .GroupBy(incident => incident.Severity)
    .Select(group => new { Severity = group.Key, Count = group.Count() })
    .ToListAsync(cancellationToken);
```

## 4. Доказова таблиця

«Підтверджено за кодом» означає статичну перевірку. Старі записи про успішні запуски збережено в архіві; вони не підтверджують останній стан автоматично.

| ID    | Передумови                                   | Дія                                                       | Очікувано                                                 | Фактично                                                                                  | Доказ / що додати                                                          |
| ----- | -------------------------------------------- | --------------------------------------------------------- | --------------------------------------------------------- | ----------------------------------------------------------------------------------------- | -------------------------------------------------------------------------- |
| T-01  | PostgreSQL healthy, API запущено             | `GET /health`                                             | 200, `status=ready`                                       | У попередньому звіті зазначено 200; повторний запуск не виконувався                       | Додати короткий HTTP-вивід і Compose ps                                    |
| T-02  | Відновлений seed                             | `GET /api/incidents?status=Triaged`                       | 200, лише Triaged                                         | Валідацію та фільтр підтверджено за кодом; попередній звіт містить 200                    | Додати фактичний request/response та Network                               |
| T-03  | Відновлений seed                             | `GET /api/incidents?status=Resolved`                      | 200, порожній список                                      | Попередній звіт містить `[]`; повторний запуск не виконувався                             | Додати фактичну відповідь                                                  |
| T-04  | Відсутній UUID                               | `GET /api/incidents/99999999-9999-9999-9999-999999999999` | 404 Problem Details                                       | Є тест `GetDetails_ForUnknownId_ReturnsProblemDetails404`; запуск не повторено            | Додати request/response                                                    |
| T-05  | Невідомий status                             | `GET /api/incidents?status=Unknown`                       | 400, `errors.status`                                      | Перевірку підтверджено за кодом; попередній звіт містить 400                              | Додати фактичну відповідь                                                  |
| T-06  | Seed ЛР 1                                    | `GET /api/incidents/severity-summary`                     | 200; Critical 0, High 1, Medium 1, Low 1                  | Є `GetSeveritySummary_ReturnsOrderedListWithZeroGroups`; запуск не повторено              | Додати JSON і фінальний результат тесту                                    |
| T-06a | Seed ЛР 1                                    | summary з `status=Triaged`                                | Medium 1, решта 0                                         | Є `GetSeveritySummary_WithTriagedStatus_CountsOnlyTriaged`; запуск не повторено           | Додати фактичний результат                                                 |
| T-06b | Seed ЛР 1                                    | summary з `status=Resolved`                               | Чотири нульові групи                                      | Є `GetSeveritySummary_WithResolvedStatus_ReturnsAllZeroGroups`; запуск не повторено       | Додати фактичний результат                                                 |
| T-06c | Некоректний status                           | summary з `status=Unknown`                                | 400, `errors.status`                                      | Є `GetSeveritySummary_WithInvalidStatus_ReturnsValidationProblem400`; запуск не повторено | Додати фактичний результат                                                 |
| T-07  | API та browser client                        | Відкрити сторінку, переглянути summary і Network          | Потрібний запит; безпечний DOM; гілки loading/empty/error | Гілки підтверджено за кодом; ручне спостереження не зафіксовано                           | Додати Network і результат UI; тест відсутності innerHTML цього не замінює |
| T-08  | Після навчальної зміни даних, якщо вона була | Reset, повтор T-02/T-06                                   | Відомий seed відновлено                                   | Попередній звіт містить повідомлення reset; повторний запуск не виконувався               | Додати факт reset і результати повторних запитів                           |

HTTP-сценарії вже є в `tests/http/incidents.http`. У `tests/SecureLab.Api.Tests/IncidentEndpointTests.cs` є власні summary-тести та baseline-регресії.

## 5. Стан «до», результат і залишкові обмеження

Baseline передбачав 501 для точки розширення. Оригінальний збережений HTTP-доказ 501 у перевірених матеріалах не знайдено. Не подавати реконструйовану відповідь як історичне спостереження.

Фінальна реалізація summary присутня в коді. ЛР 1 не вимагає окремого vulnerable commit. Безпечний `textContent` у цьому маршруті не є доказом відсутності всіх можливих XSS або інших проблем у системі.

## 6. Команди і фактичний підсумок

Команди виконуються на стані `v0.1.0`, а не на коді пізніших лабораторних. Reset застосовувати лише до власної локальної навчальної БД після зупинки API.

```powershell
docker compose --env-file infra/.env.example -f infra/compose.yaml up -d --wait
dotnet build src/SecureLab.Api/SecureLab.Api.csproj --configuration Release
dotnet test tests/SecureLab.Api.Tests/SecureLab.Api.Tests.csproj --configuration Release
```

| Перевірка                                          | Фактичний результат цього оформлення                                             |
| -------------------------------------------------- | -------------------------------------------------------------------------------- |
| Commit тега та наявність реалізації                | Перевірено статично 20.10.2026                                                   |
| Build / tests після останньої зміни                | Не запускалися; додати команду, дату, passed/failed/skipped                      |
| Compose ps, browser flow, log                      | Не виконувалися; додати докази                                                   |
| Попереднє `5/5`                                    | Історичний запис старого звіту; не використовується як підсумок фінального стану |
| Перегляд staged diff та історії перед передаванням | Повну перевірку ще виконати; факт відсутності секретів тут не засвідчується      |
