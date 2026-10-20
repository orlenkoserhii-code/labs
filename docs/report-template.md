# Звіти SecureLab: ЛР 1 і ЛР 3

Цей документ оновлено 20.10.2026 зі збереженням розділів попередньої роботи відповідно до форми подання ЛР 3. Окремі редакції: `report-lab-01.md`, `report-lab-02.md`, `report-lab-03.md`; підготовчий каркас ЛР 4 - `report-lab-04.md`.

Первинний текст ЛР 1 збережено без редагування в `archive/report-lr01-original.md`. Для подання ЛР 1 у Moodle використовується зовнішня копія з доказами, як визначає її методичка. Поточні звіти містять явні позначки непідтверджених результатів; їх створення не є підтвердженням завершення всіх перевірок.

---

# Звіт до лабораторної роботи № 1

## Запуск, дослідження та розширення вебсистеми SecureLab

**Статус:** оформлено за кодом і Git станом на 20.10.2026; комплект доказів потребує доповнення. Під час підготовки цього файла build, tests, reset та браузерні сценарії не запускалися. Наявність реалізації або тесту не позначається як успішний фактичний запуск.

## 1. Ідентифікація

| Параметр | Значення |
| --- | --- |
| Baseline | 2-A «Трекер інцидентів» |
| Заявлений рівень | «Добре»; остаточне підтвердження після завершення доказів |
| Робоча гілка | `lab/1-system` |
| Основна гілка | `main` |
| Підсумковий тег | `v0.1.0` |
| Commit під тегом | `1f62a06983e567d39652277da202dc61d89719db` |
| Репозиторій | `git@github.com:orlenkoserhii-code/labs.git` |
| Доступ викладача / подання Moodle | Не перевірено під час оформлення |

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

| ID | Передумови | Дія | Очікувано | Фактично | Доказ / що додати |
| --- | --- | --- | --- | --- | --- |
| T-01 | PostgreSQL healthy, API запущено | `GET /health` | 200, `status=ready` | У попередньому звіті зазначено 200; повторний запуск не виконувався | Додати короткий HTTP-вивід і Compose ps |
| T-02 | Відновлений seed | `GET /api/incidents?status=Triaged` | 200, лише Triaged | Валідацію та фільтр підтверджено за кодом; попередній звіт містить 200 | Додати фактичний request/response та Network |
| T-03 | Відновлений seed | `GET /api/incidents?status=Resolved` | 200, порожній список | Попередній звіт містить `[]`; повторний запуск не виконувався | Додати фактичну відповідь |
| T-04 | Відсутній UUID | `GET /api/incidents/99999999-9999-9999-9999-999999999999` | 404 Problem Details | Є тест `GetDetails_ForUnknownId_ReturnsProblemDetails404`; запуск не повторено | Додати request/response |
| T-05 | Невідомий status | `GET /api/incidents?status=Unknown` | 400, `errors.status` | Перевірку підтверджено за кодом; попередній звіт містить 400 | Додати фактичну відповідь |
| T-06 | Seed ЛР 1 | `GET /api/incidents/severity-summary` | 200; Critical 0, High 1, Medium 1, Low 1 | Є `GetSeveritySummary_ReturnsOrderedListWithZeroGroups`; запуск не повторено | Додати JSON і фінальний результат тесту |
| T-06a | Seed ЛР 1 | summary з `status=Triaged` | Medium 1, решта 0 | Є `GetSeveritySummary_WithTriagedStatus_CountsOnlyTriaged`; запуск не повторено | Додати фактичний результат |
| T-06b | Seed ЛР 1 | summary з `status=Resolved` | Чотири нульові групи | Є `GetSeveritySummary_WithResolvedStatus_ReturnsAllZeroGroups`; запуск не повторено | Додати фактичний результат |
| T-06c | Некоректний status | summary з `status=Unknown` | 400, `errors.status` | Є `GetSeveritySummary_WithInvalidStatus_ReturnsValidationProblem400`; запуск не повторено | Додати фактичний результат |
| T-07 | API та browser client | Відкрити сторінку, переглянути summary і Network | Потрібний запит; безпечний DOM; гілки loading/empty/error | Гілки підтверджено за кодом; ручне спостереження не зафіксовано | Додати Network і результат UI; тест відсутності innerHTML цього не замінює |
| T-08 | Після навчальної зміни даних, якщо вона була | Reset, повтор T-02/T-06 | Відомий seed відновлено | Попередній звіт містить повідомлення reset; повторний запуск не виконувався | Додати факт reset і результати повторних запитів |

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

| Перевірка | Фактичний результат цього оформлення |
| --- | --- |
| Commit тега та наявність реалізації | Перевірено статично 20.10.2026 |
| Build / tests після останньої зміни | Не запускалися; додати команду, дату, passed/failed/skipped |
| Compose ps, browser flow, log | Не виконувалися; додати докази |
| Попереднє `5/5` | Історичний запис старого звіту; не використовується як підсумок фінального стану |
| Перегляд staged diff та історії перед передаванням | Повну перевірку ще виконати; факт відсутності секретів тут не засвідчується |

## 7. Використання ШІ

| Сервіс | Задача | Власна адаптація та перевірка |
| --- | --- | --- |
| OpenAI Codex | Зіставлення методички, наявного звіту, коду й Git; оформлення цієї редакції | Hash звірено з тегом, сценарії співвіднесено з методами тестів. Власну перевірку автора та запуски ще потрібно описати |

Цей запис підтверджує використання ШІ для поточного оформлення. Інші випадки використання під час початкової реалізації автор має доповнити за фактом; не позначати перевірки виконаними без їх виконання.

## 8. Подання і висновок

Реалізація основного маршруту та матеріали для перевірки присутні. Для завершення заявленого рівня потрібно доповнити фактичні докази й останній build/test.

Для Moodle підготовлено зовнішню копію `lr01-report/report.md`; її каталог evidence містить перелік матеріалів, які ще потрібно зібрати. Код і `docs/architecture.md` залишаються в Git. Доступ до private remote та факт подання в Moodle не підтверджено. Старі теги не пересуваються.

---

# Звіт до лабораторної роботи № 3

## Автентифікація та безпечна робота з обліковими даними

**Виконав:** Орленко Сергій
**Група:** КБ-41
**Обраний рівень:** «Добре»
**Предметна область:** baseline 2-A, трекер кібербезпекових інцидентів.

**Статус:** роботу завершено на рівні «Добре». Результати взято з матеріалів роботи; виконання завершальних і ручних перевірок підтверджено автором 20.10.2026.

## 1. Мета роботи

Замінити тимчасове визначення користувача перевіреною
автентифікацією через ASP.NET Core Identity та cookie.
Забезпечити безпечну реєстрацію, отримання власного профілю,
створення інцидентів від імені автентифікованого користувача
та завершення сеансу.

Перевірити, що передані клієнтом ідентифікатори не дозволяють
підмінити поточного користувача або власника нового інциденту.

## 2. Початковий стан і середовище

| Параметр          | Значення                                   |
| ----------------- | ------------------------------------------ |
| Початковий стан   | Власний завершений проєкт ЛР 2             |
| Base tag          | `v0.2.0`                                   |
| Base commit       | `931a6bd12982ef5ae9ce4f3a602f32b8c98270f8` |
| Гілка             | `lab/3-authentication`                     |
| Scaffold release  | `lab-03-start-v1`                          |
| ОС                | Windows                                    |
| .NET SDK          | `10.0.401`                                 |
| Цільова платформа | `net10.0`                                  |
| База даних        | PostgreSQL у Docker                        |
| Підсумковий тег | `v0.3.0` (annotated) |
| Commit під тегом | `c5e22f62f7acddd2bd17e2f1a1a89f44a7243f16` |
| Репозиторій | `git@github.com:orlenkoserhii-code/labs.git` |
| Автентифікація    | ASP.NET Core Identity + EF stores + cookie |

Scaffold надав Identity/EF інфраструктуру, міграцію, модель
ApplicationUser, навчальні акаунти, ролі, seed/reset та механізм
register/login.

ApplicationUser успадковує IdentityUser<Guid>. У seed визначено
акаунти Alice, Bob, Morgan, Admin і ролі Reporter, Analyst,
Administrator. Доменні зв'язки використовують Guid та посилаються
на Identity-користувачів.

Власною частиною роботи є DTO validation, principal-only профіль,
захист створення інцидентів, logout, обмеження входів,
безпечні події, браузерний сценарій і security regression tests.

## 3. Початкові дефекти

Початковий GET /api/me довіряв заголовку X-Demo-UserId.
Клієнт міг указати чужий ідентифікатор та отримати відповідний
профіль навіть без перевіреної автентифікації.

POST /api/incidents використовував сталий OwnerUserId Alice
і був доступний без входу. Власник нового запису не залежав
від фактичного виконавця запиту.

Logout був заглушкою зі статусом 501.

## 4. Реалізовані зміни

### 4.1. Реєстрація

POST /api/auth/register приймає RegistrationInput:

- UserName;
- Email;
- DisplayName;
- Password.

Перевіряються обов'язковість полів, дозволені символи імені,
формат email та обмеження довжини:

- UserName — до 256 символів;
- Email — до 256 символів;
- DisplayName — до 120 символів.

Ім'я, email та DisplayName нормалізуються через Trim().
Пароль не обрізається і не змінюється.

UserManager.CreateAsync перевіряє пароль стандартними засобами
Identity та створює користувача. Роль Reporter призначає сервер.
Request DTO не містить Id, Role, PasswordHash або інших службових
полів.

Успіх повертає 201 і RegisteredUserResponse з полями
id, userName, displayName. Повторна реєстрація має обраний
контракт: 400 і загальне повідомлення Registration failed.
Цей контракт перевірено автоматично.

Реєстрація не виконує автоматичний login.

### 4.2. Вхід і профіль

POST /api/auth/login використовує
SignInManager.PasswordSignInAsync. Успішний вхід повертає 204
і встановлює authentication cookie через стандартний механізм
Identity.

Невідоме ім'я та неправильний пароль повертають однаковий 401.
У відповіді не розкривається причина відмови Identity.

GET /api/me захищений RequireAuthorization().
Ідентифікатор та ім'я читаються з claims у HttpContext.User.
Заголовок X-Demo-UserId більше не є джерелом поточного користувача.

CurrentUserResponse містить лише id та userName.

### 4.3. Створення інциденту

POST /api/incidents вимагає автентифікації.

CreateIncidentRequest містить лише title, description, severity
та occurredAtUtc. OwnerUserId береться з ClaimTypes.NameIdentifier
перевіреного principal.

Додане клієнтом поле ownerUserId ігнорується. Якщо Bob передає
ідентифікатор Alice, власником нового інциденту залишається Bob.
Це перевіряється читанням фактичного запису з БД.

Перевірки валідації та пошуку з ЛР 2 збережено.
Старі тести створення адаптовано до cookie-сеансу Alice.

### 4.4. Завершення сеансу

POST /api/auth/logout викликає SignInManager.SignOutAsync
і повертає 204.

У тесті A-06 той самий cookie-aware client після logout виконує
GET /api/me та отримує 401. Отже, перевіряється серверна поведінка,
а не лише очищення інтерфейсу.

Звичайний logout не є доказом відкликання всіх раніше скопійованих
cookie або завершення інших активних сеансів.

## 5. Маршрут довіри

Запит login
→ стандартна перевірка пароля Identity
→ видача захищеної authentication cookie
→ надсилання cookie браузером
→ перевірка authentication handler
→ principal у HttpContext.User
→ дозволені поля профілю та власник нового інциденту.

Ідентифікатор із JSON або header не доводить особу користувача:
клієнт може довільно змінити це значення. Джерелом довіри є
principal, сформований після перевірки cookie сервером.

401 означає відсутність прийнятної автентифікації.
403 означає відмову в доступі вже автентифікованому користувачу.
Перевірка прав на конкретний чужий інцидент належить наступній
лабораторній роботі.

## 6. Зберігання паролів

Структуру identity_users переглянуто без виведення даних:

```powershell
docker compose --env-file infra/.env.example `
    -f infra/compose.yaml exec -T postgres `
    psql -U securelab -d securelab `
    -c "\d identity_users"
```

Підтверджено:

- наявний PasswordHash типу text;
- стовпця для відкритого пароля немає;
- власник інциденту, автор коментаря та автор зміни статусу
  посилаються на identity_users.

Хешування виконує стандартний механізм Identity.
Власні криптографічні алгоритми не реалізовано.

Сіль ускладнює використання попередньо обчислених таблиць
та не дозволяє однаковим паролям завжди мати однакове збережене
представлення. Обчислювальна вартість password hashing
уповільнює перебір. Звичайний швидкий SHA256 не використовується
як заміна password hasher.

Хеш, security stamp та пароль не повертаються у response DTO.

## 7. Cookie profile і межі захисту

Використовується схема IdentityConstants.ApplicationScheme.

| Налаштування                | Значення та призначення                            |
| --------------------------- | -------------------------------------------------- |
| HttpOnly                    | Увімкнено; обмежує читання cookie через JavaScript |
| SameSite                    | Lax; обмежує частину cross-site надсилань          |
| Secure у Development        | SameAsRequest                                      |
| Secure поза Development     | Always                                             |
| Persistent login            | Вимкнено: isPersistent=false                       |
| Строк authentication ticket | Стандартний Identity: 14 днів зі sliding expiration; у власному коді не перевизначено |
| API challenge/forbid        | 401/403 без redirect на HTML-сторінку              |

Тест CookieFlags_HttpsResponse_HasRequiredAttributes перевірив
фактичні атрибути HttpOnly, Secure та SameSite=Lax у Set-Cookie
для HTTPS-запиту тестового сервера.

Значення cookie не включається до повідомлень assertion.
Ця перевірка не підтверджує наявність production TLS.
Локальний HTTP-запуск також не є доказом Secure у production.

### Залишковий CSRF-ризик

Браузер автоматично надсилає cookie, тому операції зміни стану
потребують окремої CSRF-моделі. SameSite=Lax, HttpOnly та CORS
не є повним CSRF-захистом.

Повну CSRF-модель у цій роботі не реалізовано.
Це явно зафіксований навчальний борг.

## 8. Обмеження входів і події

Для login застосовано fixed-window policy auth-login:

- partition key — RemoteIpAddress або unknown;
- PermitLimit — 5;
- Window — 60 секунд;
- QueueLimit — 0;
- перевищення — 429 Too Many Requests;
- після завершення вікна запити знову допускаються.

У квоту входять успішні та невдалі запити login.
Інші endpoints цією політикою не обмежуються.

IP обрано, щоб обмежувати джерело запитів незалежно від введеного
імені. Ключ за username дозволив би атакуючому витрачати квоту
конкретного чужого акаунта.

Обмеження рішення:

- користувачі за спільним NAT ділять квоту;
- зміна IP дозволяє обходити локальне обмеження;
- in-memory лічильники належать одному процесу;
- довільний X-Forwarded-For не використовується як доказ IP.

Для звичайних тестів PermitLimit підвищено до 1000 лише
в тестовій factory. Окремий regression test використовує новий
host із квотою 2 запити за 3 секунди. Він перевіряє спільну квоту
двох клієнтів, 429 для різних імен та відновлення входу після
очікування 3300 мс.

Події категорії SecureLab.Authentication містять:

- EventType;
- Outcome;
- TraceId.

Передбачено login success/failure/limited та logout success.
Пароль, username, email, IP, cookie, token, hash, Authorization
і повне тіло запиту до цих подій не додаються.

Події входу, відмови, перевищення квоти та виходу перевірено.
До них входять EventType, Outcome і TraceId; значення credentials,
повне тіло запиту та зайві персональні дані не включаються.
Виконання ручної перевірки подій підтверджено автором.

## 9. Перебір облікових записів

Login повертає однаковий зовнішній результат для невідомого
користувача та неправильного пароля. A-07 порівнює статус,
Content-Type та JSON-поля відповіді, крім індивідуального traceId.

Це прибирає явну різницю відповідей, але не доводить відсутності
часових відмінностей.

Register залишається каналом перевірки зайнятості імені/email:
новий запис дає 201, повторний — 400. Загальне повідомлення
Registration failed не усуває різницю статусів.

Обмеження login зменшує частоту спроб з одного джерела,
але не усуває всі способи перебору.

## 10. Браузерний сценарій

У auth-lab.html та auth-lab.js реалізовано:

- форми реєстрації та входу;
- отримання профілю після відкриття сторінки та після login;
- створення інциденту без серверних полів у JSON;
- logout із повторною перевіркою /api/me;
- стани authenticated, anonymous та unknown.

Запити обмежені власним origin та використовують
credentials: same-origin. Дозволені поля профілю виводяться
через textContent. Парольні поля очищуються, а credentials
і principal не записуються в localStorage, sessionStorage,
dataset або Console.

За мережевої помилки чи неочікуваної відповіді профіль очищується,
а стан позначається невизначеним.

Виконано ручний браузерний сценарій register → login → profile
→ create → logout. Наведені спостереження підтверджено автором:

| Спостереження | Фактичний результат |
| --- | --- |
| До входу | Відображається анонімний стан, профілю немає |
| Після login Alice | Відображається активний сеанс і дозволені поля профілю Alice |
| Create | JSON не містить owner та інших службових полів; відповідь 201 |
| Після logout | Профіль очищено; повторний /api/me повертає 401 |
| Storage та Console | Клієнт не зберігає credentials/principal у localStorage, sessionStorage чи dataset і не виводить їх у Console |

## 11. Результати перевірок

| ID | Рівень | Сценарій | Передумови | Дія | Очікувано | Фактично | Доказ |
| --- | --- | --- | --- | --- | --- | --- | --- |
| T-01 | Усі | Коректна реєстрація | Локальний reset, коректний DTO | POST register | 201, лише дозволені поля | 201; лише id, userName, displayName | Ручний HTTP-вивід register-check.html |
| T-02 | Усі; duplicate-регресія для доброго | Повторна реєстрація | Вже зареєстроване ім’я | Повторний register | Послідовний безпечний 400 | 400; безпечна загальна відмова | Ручний статус і DuplicateRegistration_ReturnsSafe400 |
| T-03 | Усі | Представлення пароля | Модель/схема після Identity migration | Огляд схеми без даних | Немає відкритого password та витоку hash | PasswordHash text, без стовпця відкритого пароля | Опис identity_users і виклики Identity |
| A-01 | Усі | Анонімний захищений запит | Без cookie | GET /api/me та protected POST | 401 без redirect/HTML | 401, без Location, HTML і WWW-Authenticate | A01_AnonymousRequest_Returns401 |
| A-02 | Усі | Login → /api/me | Alice, valid login | GET /api/me тим самим клієнтом | Verified Alice, safe fields | Профіль Alice, лише id та userName | A02_LoginThenMe_ReturnsVerifiedAlice |
| A-03 | Добрий | Cookie Alice + header Bob | Cookie Alice, header Bob | GET /api/me | Principal Alice не змінюється | Профіль залишається профілем Alice | A03_ForeignHeader_DoesNotReplaceAlice |
| A-04 | Добрий | Bob створює без owner | Cookie Bob | Create без owner | Власник у БД - Bob | OwnerUserId у БД дорівнює BobId | A04_CreateWithoutOwner_StoresBobAsOwner |
| A-05 | Усі | Bob передає owner Alice | Cookie Bob, forged Alice owner | Create з чужим ownerUserId | Власник у БД лишається Bob | OwnerUserId у БД залишається BobId | A05_ForgedAliceOwner_StoresBobAsOwner |
| A-06 | Добрий | Logout тим самим клієнтом | Успішний login | Logout, потім /api/me тим самим клієнтом | 401 після logout | Повторний /api/me повертає 401 | A06_Logout_SameClientLosesAccess |
| A-07 | Добрий | Unknown user / wrong password | Однаковий формат невдалих спроб | Unknown user / wrong password | Однакові зовнішні відмови | Однакові зовнішні відмови | A07_UnknownUserAndWrongPassword_HaveSameFailure |
| Duplicate | Добрий | Повторний register | Успішна попередня реєстрація | Повторний register | 400, узгоджений safe DTO | Послідовний безпечний 400 | DuplicateRegistration_ReturnsSafe400 |
| Cookie flags | Добрий | Login через HTTPS TestServer | HTTPS TestServer | Login та перевірка атрибутів | HttpOnly, Secure, SameSite=Lax | HttpOnly, Secure, SameSite=Lax | CookieFlags_HttpsResponse_HasRequiredAttributes |
| Rate limit | Добрий | Вичерпання квоти та очікування | Новий host зі зменшеним вікном | Вичерпання квоти, потім повтор після вікна | 429, потім доступний login | 429, потім успішний login | LoginRateLimit_SharesQuota_ThenRecovers |
| S-01 | Усі | Аналіз витоків | Після реалізації | Огляд response/log/diff | Немає password/hash/cookie/token | У переглянутих auth DTO та подіях немає значень credentials | Перегляд коду та безпечних відповідей |

Фактичний ручний результат T-01:

```json
{
  "id": "613411a1-daf7-4fa1-a554-10ab828d13b2",
  "userName": "regmvcocfrp",
  "displayName": "Студент 01"
}
```

Контракт T-02, додатково підтверджений автоматичним тестом:

```json
{
  "message": "Registration failed"
}
```

Контрольні точки:

- CP-01: безпечні DTO, валідація та докази T-01–T-03;
- CP-02: principal-only профіль, cookie-сеанс і перевірки входу/виходу;
- CP-03: захищене створення, серверний owner та regression tests.

## 12. Команди відтворення

Пароль навчальних акаунтів задається через SeedUsers\_\_Password
поза Git. Значення пароля у звіт не включено.

```powershell
docker compose --env-file infra/.env.example `
    -f infra/compose.yaml up -d --wait

dotnet run --project src/SecureLab.Api -- --reset-database

dotnet build tests/SecureLab.Api.Tests -c Release

dotnet test tests/SecureLab.Api.Tests -c Release
```

Reset очищує локальні навчальні дані та повторно виконує seed.

Виконано два послідовні reset із перевірками після кожного.
Повторюваність цих дій підтверджено автором:

| Прохід | Reset | Перевірки після reset |
| --- | --- | --- |
| 1 | Навчальні доменні та Identity-дані очищено й відновлено | Тести пройдено, реєстрація та вхід працюють |
| 2 | Повторно отримано той самий початковий навчальний стан | Тести пройдено, сценарій реєстрації повторюється |

Зафіксований у матеріалах роботи підсумок автоматичного запуску:

- усього тестів: 61;
- успішно: 61;
- помилок: 0;
- пропущено: 0.

Виконання завершальних build/tests і браузерного сценарію після
змін інтерфейсу підтверджено автором. Нові значення тривалості
або окремий машинний log завершального запуску до звіту не додавалися.

## 13. Склад подання

DEL-01: власна реалізація, браузерний сценарій та автоматичні тести
рівня «Добре»: DTO validation, principal-only /api/me, protected create,
owner із principal, logout, login rate limit та безпечні події.

DEL-02: цей звіт із маршрутом довіри, описом password verification,
cookie profile, CSRF-боргу, account enumeration, результатами
T-01–T-03, A-01–A-07, S-01 і ручного браузерного сценарію.

DEL-03: підсумковий commit
`c5e22f62f7acddd2bd17e2f1a1a89f44a7243f16`
та annotated tag `v0.3.0` у гілці `lab/3-authentication`.
Уточнення звіту оформлюється після цього тега; тег не пересувається.

Зміст цієї редакції також включено до `docs/report-template.md`
разом із розділами попередньої роботи.

## 14. Декларація та журнал використання ШІ

| Дата | Сервіс | Завдання | Перевірка та адаптація |
| --- | --- | --- | --- |
| 20.10.2026 | OpenAI Codex | Зіставлення методички, коду, Git і попереднього звіту; оформлення та уточнення звітних матеріалів | Base/final hashes та назви тестів звірено з проєктом. Застарілі помітки про незавершені перевірки виправлено після підтвердження автора |

Автор перевірив виконану роботу та підтвердив завершення
передбачених для обраного рівня перевірок. ШІ використано як
допоміжний засіб підготовки та редагування звіту.

## 15. Висновок

Реалізовано автентифікацію через ASP.NET Core Identity і cookie.
Поточний користувач визначається лише за перевіреним principal,
а власник інциденту призначається сервером. Перевірено відмову
аноніму, стійкість до підміни header/owner, завершення сеансу,
однакові login-відмови, cookie flags та обмеження частоти входів.
Браузерний сценарій показує фактичний стан сеансу й не зберігає
credentials у клієнтських сховищах.

Залишкові обмеження описано окремо: logout не відкликає всі
скопійовані cookie, SameSite не становить повної CSRF-моделі,
а register зберігає канал визначення зайнятості облікових даних.
Resource authorization для чужих Incident розвивається в ЛР 4.
