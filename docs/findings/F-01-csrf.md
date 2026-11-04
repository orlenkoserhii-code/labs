# F-01. Відсутня перевірка antiforgery для cookie mutations

## Статус і пріоритет

Fixed у робочій копії. P1 у межах цієї лабораторної. Статус доказу
«спостережено локально». Активи охоплюють Incident і сеанс користувача.
Спочатку виправлено цей дефект, оскільки він дозволяв виконувати mutation
без перевірки request token. F-02 стосується додаткового захисту error response.

## Передумови й відтворення

Окремий локальний Development test host, cookie-aware client Alice,
навчальна БД та новий власний Incident зі статусом New. Початковий HEAD
`206838049353f8281bfbc6c10c94e9a182273354`, робоча копія перед змінами ЛР 6.

1. Увійти через POST `/api/auth/login`.
2. Прочитати власний Incident і зберегти title, description та інші поля.
3. Виконати PUT `/api/incidents/{id}` із валідним DTO без `X-CSRF-TOKEN`.
4. Очікування після захисту становить 400 і незмінний ресурс. До виправлення
   автоматизований тест фактично отримав 204 замість 400.

Початковий запуск `REG_CSRF_01_DenialPreservesState_ValidPairAllowsUpdate`
завершився FAIL, Expected BadRequest, Actual NoContent. Cookie й пароль
у доказі відсутні. Це локальне відтворення пропуску серверної валідації.
Browser exploit через довільний зовнішній сайт окремо не відтворювався.

## Вплив, першопричина й виправлення

JSON endpoints довіряли cookie та resource authorization без стандартної
antiforgery-перевірки. SameSite, CORS і вимоги Content-Type обмежують окремі
браузерні сценарії, але server-side token validation у cookie mutation
perimeter була відсутня.

У Program.cs додано AddAntiforgery, заголовок `X-CSRF-TOKEN` і явний виклик
ValidateRequestAsync до виконання mutation handler. Route
`GET /api/auth/csrf-token` потребує входу й повертає no-store response.
Захищено create, update, delete, triage і logout. PATCH endpoint у поточній
системі відсутній, middleware враховує цей method для майбутніх маршрутів.
Дві сторінки використовують спільний `csrf.js`, який отримує актуальний token
перед mutation та зберігає його лише в пам'яті поточного виклику.

## Retest і positive regression

```powershell
dotnet test tests/SecureLab.Api.Tests -c Release --filter 'FullyQualifiedName~REG_CSRF_01'
```

REG-CSRF-01 пройшов повністю. Відсутній та некоректний token дають 400,
повторний GET підтверджує незмінність стану. Валідна пара дає 204,
наступний GET повертає змінені title і description.

`CSRF_Perimeter_DenialPreservesDatabaseAndSession` перевіряє також create,
DELETE, triage та logout. `CSRF_IdentityChange_RejectsOldTokenAndAcceptsFreshToken`
перевіряє відмову старого токена після входу іншого користувача.
Наявні Lab02/Lab03/Lab04 тести отримують власний token після login та
перевіряють business validation, чужі ресурси й дозволені mutation.

## Залишковий ризик

Same-origin XSS може отримати token. Login/register мають окремий публічний
credential flow. GET routes мають залишатися без side effects. Новий спосіб
автентифікації потребуватиме перегляду perimeter і тестів.
