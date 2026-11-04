# F-02. Втрата security headers після unexpected error

## Статус і пріоритет

Fixed у робочій копії. P2 у межах лабораторної. Статус доказу
«спостережено локально». Актив охоплює browser response policy.
Пріоритет нижчий за F-01, оскільки безпечний ProblemDetails уже працював,
а дефект порушував однакове застосування додаткових browser controls.

## Передумови й відтворення

Окремий TestServer у Production з перевіреним EnvironmentName. Connection
string вказує на недосяжний локальний порт 54399, automatic migrations
вимкнено. HTTPS BaseAddress використано для cookie semantics.

1. Виконати POST `/api/auth/login` з непорожнім навчальним input.
2. Отримати 500 ProblemDetails і перевірити traceId.
3. Зіставити trace із записом ExceptionHandlerMiddleware.
4. Перевірити X-Content-Type-Options, Referrer-Policy та CSP.

До виправлення `REG_HEADERS_01_ProductionErrorRetainsHeadersAndTrace`
пройшов перевірку status і trace, потім завершився FAIL на читанні
X-Content-Type-Options з повідомленням `The given header was not found`.

## Першопричина й виправлення

Headers встановлювалися до виконання endpoint усередині middleware,
розташованого після UseExceptionHandler. Під час обробки exception
Response.Clear видаляв уже встановлені заголовки.

Реєстрацію Response.OnStarting перенесено перед UseExceptionHandler.
Заголовки встановлюються безпосередньо перед відправленням відповіді.
Development Scalar має явний виняток CSP для API reference. Звичайні
сторінки й API responses отримують наявну політику CSP.

## Retest і positive regression

```powershell
dotnet test -c Release --filter 'FullyQualifiedName~REG_HEADERS_01'
dotnet test -c Release --filter 'FullyQualifiedName~HeadersAndCookies'
```

REG-HEADERS-01 повертає 500 з усіма трьома заголовками. JSON містить лише
type, title, status і traceId. Trace збігається з trace diagnostic event.
Пароль запиту й навчальне значення пароля недосяжної БД відсутні у
перехоплених логах.

`HeadersAndCookies_NormalAndDeniedResponses` пройшов у Development і
Production для сторінок, 200, 400, 401 і 404. Production cookie checks
підтвердили Secure/HttpOnly, SameSite=Lax для сеансу та Strict для
antiforgery cookie. Браузерні login/create/read/logout і CSP також перевірені.

## Залишковий ризик

Headers додає застосунок. Відповіді стороннього reverse proxy та помилки,
що виникли до запуску application pipeline, потребують окремої перевірки.
TestServer не доводить реальний HTTPS deployment або роботу HSTS.
