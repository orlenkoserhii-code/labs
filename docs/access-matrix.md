# Матриця доступу ЛР 4

Baseline 2-A. Рівень: «Добре». Основний ресурс - Incident;
дочірній - IncidentComment. Ролі та user id беруться лише з verified principal.

| Дія | Рівень | Анонім | Reporter | Analyst | Administrator | Ownership/context | Реалізація / доказ |
| --- | --- | --- | --- | --- | --- | --- | --- |
| List | Добрий | deny 401 | Власні, 200 | Усі для аналізу, 200 | Порожня множина, 200, без окремих прав | Owner для Reporter | IncidentAccess.Visible; A-05 |
| Details | Усі | deny 401 | Власні 200; чужі 404 | 200 для аналізу | deny 404 | Owner/role | IncidentAccess.Read/Visible; A-02/A-03/A-03R |
| Create | Успадковано | deny 401 | Для себе, 201 | Для себе, 201 | Успадкований authenticated create для себе, 201 | Owner задає сервер | Lab02Endpoints; ЛР 3 |
| Update content | Добрий | deny 401 | Власні 204; чужі 404 | deny 403 для видимого ресурсу | deny 404 | Лише title/description | IncidentAccess.Update; A-02/A-04 |
| Delete | Добрий | deny 401 | Власний New: 204; інший власний status: 403; чужий: 404 | deny 403 для видимого ресурсу | deny 404 | Owner і New - обране правило видалення | IncidentAccess.Delete; A-02/A-04 і non-New regression |
| Triage | Добрий | deny 401 | Власний: 403; чужий: 404 | New → Triaged: 204; інший status: 403 | deny 404 | Analyst і фактичний New у БД | IncidentAccess.Triage; A-06/A-07/A-09 |
| Public comment | Добрий | deny 401 | 200 за parent authorization; інакше 404 | 200 за parent authorization | deny 404 | IncidentComment.Incident | Comment; A-12 |
| Internal comment | Добрий | deny 401 | deny 404; відсутній у details | 200 після parent authorization | deny 404 | Analyst і parent read | Comment і details projection; A-12 |
| Regular update з невідомими полями owner/role/status/severity/id/time | Добрий | deny 401 | 400, стан незмінний | 400 на порушений DTO-контракт | 400 на порушений DTO-контракт | JSON/DTO validation до handler | UpdateIncidentRequest Disallow; A-08b |
| Search/severity-summary ownership-scoping | Відмінний | Не виконувалось - «Добре» | Не виконувалось - «Добре» | Не виконувалось - «Добре» | Не виконувалось - «Добре» | A-11 поза рівнем | Summary успадковує authentication групи; search окремий |
| Export/audit read endpoint | Відмінний, якщо є | Не застосовується | Не застосовується | Не застосовується | Не застосовується | Flow відсутній | Не створювався |

## HTTP policy

Чужий і неіснуючий Incident повертають однакові 404 Problem Details без id
або приватних полів. Порівняння виключає лише traceId/requestId. Це правило
діє для валідних запитів до details, update/delete та direct child access.
Порушення JSON/DTO-контракту дає 400 незалежно від існування ресурсу.

Заборонена дія над видимим Incident повертає 403. Ownership не дає triage;
Analyst не отримує довільного content update/delete. Administrator не
отримує неоголошених предметних прав за назвою ролі.

Denied triage створює structured authorization.denied: subject id, action,
resource reference, result, UTC time і той самий traceId, що у відповіді.
Credentials, raw request body та приватний текст до події не входять.

## Межі виконання

List/details/update/delete/triage/child охоплені політикою. Search і
агрегації з попередніх робіт не отримують ownership-scoping: це A-11
«Відмінно». Глобальне приховування Incident через усі маршрути не заявляється.
Cookie mutation зберігає CSRF teaching debt до ЛР 6; concurrency/version
check у цьому обсязі не реалізовано. Фактичний результат: 91/91 automated
tests (30 сценаріїв ЛР 4), 33/33 HTTP-перевірки після виправлення;
browser owner/foreign перевірено. Докази - `report-lab-04.md` та
`evidence/lab-04/summary.json`. Стани збережено окремими commits:
vulnerable - `a48a477121186815c90209ef424793293a768908`;
fixed - `e00d237f4bd09ad47772520441a6c084e4b19a0d`.
Локальний тег `v0.4.0` указує на fixed commit.
