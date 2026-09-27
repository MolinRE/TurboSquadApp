# Архитектура «Турбо-Бригады»

Здесь описано, из чего состоит решение: компоненты (раздел 2), хранение данных (3), голосовой Шаг Рейса (4), подготовка контента в CMS (5), развёртывание (6) и безопасность (7). Запуск описан в [README](../README.md), API для HR — в [integration.md](integration.md), термины — в [CONTEXT.md](../CONTEXT.md), ограничения и план развития — в [limitations-roadmap.md](limitations-roadmap.md).

## 1. Архитектурные решения

- **Frontend и Backend разделены.** Next.js (`front/`) и ASP.NET Core API (`back/`) — отдельные приложения и контейнеры, которые общаются только по HTTP API. Вся игровая логика живёт на бэкенде.
- **Бэкенд — модульный монолит.** Для хакатона выбран один сервис: его проще запускать и отлаживать. Код разбит на модули по папкам (таблица в разделе 2.1), у каждого модуля свои эндпоинты и сервис. Все модули пока работают с одной `AppDbContext`, поэтому перед выносом модуля в отдельный сервис его таблицы нужно отделить.
- **API можно масштабировать горизонтально.** В памяти процесса нет состояния, которое нужно между запросами. JWT работает без серверных сессий, состояние Рейса, Смены и Блица лежит в БД, таймеры считаются от меток времени в БД. Захват генерации реплики пассажира сделан условным `UPDATE`, от гонки двух публикаций События защищает уникальный ключ `(event_id, version)`. Поэтому API можно запустить в нескольких экземплярах за балансировщиком; на стенде работает один экземпляр, несколько экземпляров не проверяли.
- **Сценарии расширяются без правки ядра.** Новое Событие или новая развилка — это JSON-документ, который Методист правит и публикует в CMS, а валидатор проверяет граф до публикации. Шкалы и Классы обслуживания хранятся в справочниках в БД. Ачивки пока заданы списком правил в коде (`AchievementService`).
- **Сервер авторитетен.** Сервер проверяет роль, текущий Шаг, Условия и таймер. Клиент отвечает за отображение и запись голоса, но не может самостоятельно продвинуть Рейс или изменить Шкалы.
- **Движок — редьюсер.** `TripEngine.Reduce(state, action)` не знает о БД, HTTP и часах. Это делает ветвление тестируемым и объяснимым.
- **События версионируются.** Событие хранится целым JSON-документом в `jsonb`; при старте Рейса фиксируются версии Событий, справочники и настройки. Правка CMS не меняет уже начатый Рейс. Вопросы не версионируются: правка меняет запись на месте, поэтому Смена на свайпах и Блиц хранят снимки Вопросов у себя (подробнее в разделе 3).
- **Журнал вместо отдельного снимка состояния.** `TripJournalRecord` хранит действия и результаты, а `TripService` при чтении заново проигрывает журнал на зафиксированном контенте. Так сохраняется полный Разбор и воспроизводимость.
- **Ветвление и Очки определяет граф, а не ИИ.** Голосовая модель переводит аудио в текст. Laya выбирает среди заранее описанных Вариантов и дополнительно оценивает Коммуникацию (вежливость, этапы Ролевой модели, риск нарушения безопасности). Эту оценку видят проводник и Руководитель в аналитике, но на Очки и переходы она не влияет. Переходы и последствия применяет `TripEngine`, Очки начисляются по выбранному Варианту. Текстовая LLM пишет реплику пассажира и объяснение в Разборе (слой Б), но ничего не решает.

## 2. Компоненты решения

```mermaid
flowchart LR
    subgraph Clients["Клиенты: Next.js + TypeScript, mobile-first"]
        Player["Проводник\nигры, Разборы, профиль, рейтинг"]
        Methodologist["Методист\nИсточники и CMS контента"]
        Manager["Руководитель\nаналитика голосового конвейера"]
    end

    subgraph API["ASP.NET Core API: монолит"]
        Edge["HTTP boundary\nJWT, роли, CORS"]
        Auth["Auth\nregister/login/demo-login, роли"]
        Trips["Trips\nTripService, серверный таймер, Разбор"]
        Engine["TripEngine\nграф, Условия, Флаги, Шкалы"]
        Voice["Voice\nSTT → Laya, поток реплики по SSE"]
        Swipes["Swipes\nколода, циклы Woodpecker, таймауты"]
        Blitz["Blitz\nВопросы на время"]
        Content["Content/CMS\nИсточники, Вопросы, События, публикация"]
        Generation["Generation\nочищенный текст → JSON-черновики"]
        Scoring["Scoring\nосвоение, очки, профиль, рейтинг"]
        Achievements["Achievements\nвыдача Ачивок"]
        Analytics["Analytics\nлатентность и качество голосовых Шагов"]
        Integration["Integration API\nуспеваемость по X-Api-Key"]
    end

    DB[("PostgreSQL\nEF Core migrations")]
    STT["polza.ai\nGigaAM-v3 STT"]
    Laya["Laya\nвыбор Варианта и оценка"]
    LLM["polza.ai LLM\nреплика пассажира, Разбор, генерация контента"]
    HR["HR / LMS"]

    Player --> Edge
    Methodologist --> Edge
    Manager --> Edge
    Edge --> Auth
    Edge --> Trips
    Edge --> Swipes
    Edge --> Blitz
    Edge --> Content
    Edge --> Scoring
    Edge --> Analytics
    Trips --> Engine
    Trips --> Voice
    Content --> Generation
    Trips --> Scoring
    Swipes --> Scoring
    Trips --> Achievements
    Swipes --> Achievements
    Auth --> DB
    Trips --> DB
    Swipes --> DB
    Blitz --> DB
    Content --> DB
    Scoring --> DB
    Achievements --> DB
    Analytics --> DB
    Voice --> STT
    Voice --> Laya
    Voice --> LLM
    Trips --> LLM
    Generation --> LLM
    HR -- "X-Api-Key" --> Integration
    Integration --> DB
```

Блиц пока не начисляет Очки: ответы сохраняются только в его собственных таблицах. Импорт сотрудников и экспорт в xAPI не сделаны, см. [integration.md](integration.md).

### 2.1. Где что в коде

API описан в Swagger (`/swagger`), для HR — в [integration.md](integration.md).

| Компонент | Код в `back/` | Основные API |
|---|---|---|
| Auth | `Program.cs`, `Data/LoginService.cs`, `Data/JwtTokenService.cs` | `/api/auth/*` |
| Симулятор рейса | `Trips/`: движок `TripEngine.cs`, сервис `TripService.cs` | `/api/trips/*`: `/variant`, `/voice`, `/timeout`, `/proactive`, `/debrief` |
| Голос | `Voice/`: STT, Laya, LLM | multipart `/api/trips/{id}/voice`, SSE `/api/trips/{id}/voice/{attemptId}/reply` |
| Смена на свайпах | `Swipes/` | `/api/swipe-shifts/*` |
| Блиц | `Blitz/` | `/api/blitz-sessions/*` |
| Контент и CMS | `Events/` (граф и валидатор), `Questions/`, `Sources/` | `/api/cms/events/*`, `/api/cms/questions/*`, `/api/cms/sources/*` |
| Генерация | `Sources/*GenerationService.cs` | `/api/cms/sources/{id}/generate`, `/generate-events` |
| Скоринг и Ачивки | `Scoring/`, `Achievements/` | `/api/profile`, `/api/leaderboard`, `/api/achievements` |
| Аналитика | `Analytics/` | `/api/analytics/voice` |
| Интеграция | `Integration/` | `/api/integration/employees/*` по ключу `X-Api-Key` |
| Данные | `Data/`: записи, `AppDbContext`, миграции; `Content/`: стартовый контент | — |

## 3. Хранение данных

Все данные лежат в PostgreSQL, схема меняется только миграциями EF Core. Колонки описаны в `back/Data/*Records.cs`. Сплошная линия — внешний ключ, пунктир — снимок или ссылка через `jsonb` без внешнего ключа.

```mermaid
erDiagram
    DEPOTS ||--o{ BRIGADES : "бригады депо"
    BRIGADES ||--o{ APP_USERS : "сотрудники"
    APP_USERS ||--o{ USER_ROLES : "роли"

    APP_USERS ||--o{ TRIPS : "Рейсы"
    TRIPS ||--o{ TRIP_JOURNAL : "журнал"
    TRIPS }o..o{ EVENT_DOCUMENTS : "версии Событий"
    TRIPS }o..o{ SCALES : "снимок справочников"

    APP_USERS ||--o{ SWIPE_SHIFTS : "Смены"
    SWIPE_SHIFTS ||--o{ SWIPE_ANSWERS : "ответы"
    QUESTIONS ||--o{ SWIPE_ANSWERS : "Вопрос и его снимок"

    APP_USERS ||--o{ BLITZ_SESSIONS : "сессии Блица"
    BLITZ_SESSIONS ||--o{ BLITZ_ANSWERS : "ответы"
    BLITZ_SESSIONS }o..o{ QUESTIONS : "снимок колоды"

    SOURCES ||--o{ QUESTIONS : "генерация"
    SOURCES ||--o{ EVENT_DRAFTS : "генерация"
    EVENT_DRAFTS }o..o| EVENT_DOCUMENTS : "публикация"

    APP_USERS ||--o{ KNOWLEDGE_MASTERIES : "освоение"
    APP_USERS ||--o| CONDUCTOR_PROFILES : "Звание"
    APP_USERS ||--o{ ACHIEVEMENT_AWARDS : "Ачивки"
```

Ключевые правила хранения:

1. В `EventDocumentRecord` лежит полный граф События; новая публикация создаёт следующую версию.
2. В `TripRecord` лежат снимки справочников, настройки Рейса и `eventId → version`; поэтому исторический Разбор не зависит от текущей CMS.
3. В `TripJournalRecord` одна строка — одна запись журнала: Проактивный выбор (`proactiveChoice`), решение или таймаут на Шаге (`decision`: изменения Шкал, Флаги, критическая ошибка, переход), голосовая попытка (`voiceAttempt`: расшифровка, вердикт и оценка Laya, реплика пассажира, задержки STT/Laya/LLM) и итог События (`eventFinished`). Аудио не сохраняется. Источник для Разбора берётся из версии События, в журнале его нет.
4. Вопросы правятся на месте, поэтому ответ Смены на свайпах хранит снимок Вопроса на момент ответа, а сессия Блица — снимок всей колоды на старте. Изменение опубликованного Вопроса не переписывает историю.

## 4. Последовательность голосового Шага

Это основной сквозной сценарий для демонстрации архитектуры: ветвление выполняет собственный движок, а ИИ подключён как контролируемый адаптер.

```mermaid
sequenceDiagram
    autonumber
    actor C as Проводник
    participant F as Клиент Next.js
    participant A as API / TripService
    participant D as PostgreSQL
    participant E as TripEngine
    participant S as polza.ai STT
    participant L as Laya
    participant G as polza.ai LLM

    C->>F: Нажать «Начать Рейс» / выбрать Класс
    F->>A: POST /api/trips
    A->>D: Загрузить опубликованный контент и справочники
    A->>E: Reduce(Initial, StartTrip)
    E-->>A: Состояние: первый Шаг Заступа, Шкалы, доступные Варианты
    A->>D: TripRecord со снимком контента (журнал пока пуст)
    A-->>F: TripView (expiresAt считается сервером)

    C->>F: Ответить голосом
    F->>A: POST /api/trips/{id}/voice (audio, eventId, stepId, attemptId, recordingMs)
    A->>D: Загрузить Рейс и проиграть журнал
    A->>A: attemptId уже есть → вернуть сохранённый результат
    A->>A: Проверить: нет pending-попытки, Рейс идёт, это текущий voice-Шаг, размер аудио
    A->>S: audio/transcriptions (GigaAM-v3)
    S-->>A: transcript
    A->>L: situation + brief + transcript + доступные Варианты
    L-->>A: choice, confidence, score, RoleModel stages, safety
    A->>E: RecordVoiceAttempt
    E-->>A: Голосовая попытка добавлена в журнал

    alt ответ начат позже таймера (с допуском 2 с)
        A->>E: TimeOut
        E-->>A: Ветка timeout, последствия и следующий Шаг
        A->>D: Сохранить попытку (VoiceDeadlineExceeded) и таймаут
        A-->>F: 200 TripView с новой позицией
    else ошибка STT/Laya, confidence ниже порога или неизвестный Вариант
        A->>D: Сохранить не применённую попытку и код ошибки
        A-->>F: 422 {reason, message, attempt, trip}, Рейс остаётся на Шаге
        Note over F,A: при LowConfidence клиент открывает тот же SSE-поток<br/>и получает уточняющий вопрос пассажира
    else Условия скрывают выбранный Вариант
        A->>D: Сохранить попытку с кодом отказа
        A-->>F: 409 ProblemDetails (reason)
    else попытка принята
        A->>E: ChooseVariant(choice)
        E-->>A: Проверить Условия, изменить Шкалы/Флаги, выбрать переход
        A->>D: Сохранить VoiceAttempt (решение пока pending)
        A-->>F: Предпросмотр новой позиции + pending passenger reply
    end

    F->>A: GET /api/trips/{id}/voice/{attemptId}/reply
    A->>E: Предварительно применить Вариант для контекста пассажира
    A->>D: Атомарно занять генерацию по attemptId
    A-->>F: SSE started
    A->>G: Стримить контекст События и последствий
    loop токены
        G-->>A: token
        A-->>F: SSE token
    end
    A->>D: Сохранить реплику, решение, Очки и latency, выдать Ачивки
    A-->>F: SSE done + актуальный TripView
    F-->>C: Показать реплику и следующий Шаг
```

### Гарантии сценария

- Таймер считается по часам сервера от `TripRecord.StepStartedAt`. На кнопочном Шаге ответ позже таймера больше чем на 1 с засчитывается как таймаут, а `/timeout` раньше срока отклоняется. На голосовом Шаге таймер — время, чтобы начать отвечать: клиент сообщает только длительность записи `recordingMs` (не больше 60 с), сервер вычитает её из момента прихода аудио и даёт допуск 2 с на загрузку.
- `attemptId` делает повторную отправку идемпотентной: уникальный индекс `(trip_id, voice_attempt_id)`, повтор возвращает сохранённый результат.
- Для принятого голоса используется двухфазное применение: сначала в журнале фиксируется `VoiceAttempt` и клиент получает предпросмотр ветки, затем после успешного SSE-потока коммитятся решение, Очки и реплика пассажира. Пока попытка pending, следующий ход блокируется (`VoiceReplyPending`), кроме таймаута после истечения таймера. Если LLM не ответила или клиент оборвал поток, попытка получает код ошибки и не применяется: Рейс остаётся на Шаге.
- Laya может выбрать только Вариант из переданного списка. Переход, критическая ошибка, пороги Шкал и Срыв вычисляются `TripEngine`.
- Отказ действия в Рейсе — `409 ProblemDetails` с кодом причины в поле `reason` (`StaleStep`, `HiddenVariant`, `TimerNotExpired`, `VoiceReplyPending` и т. п.), по нему UI решает, что показать. Неудачная голосовая попытка возвращает `422` с телом `{reason, message, attempt, trip}`, ошибка в SSE-потоке приходит событием `error` с тем же `reason`. Единого формата ошибок на все модули нет: CMS отвечает своими телами `{message}` и `{reason, latestVersion}`.

## 5. Последовательность подготовки контента

```mermaid
sequenceDiagram
    actor M as Методист
    participant F as CMS Next.js
    participant A as Source/Content API
    participant D as PostgreSQL
    participant G as polza.ai LLM
    participant V as Event/Question Validator

    M->>F: Создать Источник и вставить документ
    F->>A: POST /api/cms/sources
    A->>D: Сохранить SourceRecord
    M->>F: Запустить генерацию Вопросов или События
    F->>A: POST /generate или /generate-events
    A->>A: Разбить текст на пункты, удалить ПДн
    loop каждый пункт, до 3 попыток
        A->>G: Очищенный пункт, справочник Тем и JSON-схема (+ ошибки прошлой попытки)
        G-->>A: JSON-кандидат
        A->>V: Проверить схему, цитату из пункта, Тему, справочники и граф
        V-->>A: ошибки или валидный черновик
    end
    A->>D: Сохранить QuestionRecord (status=draft) / EventDraftRecord, дубли отбросить
    A-->>F: Черновики и ошибки по пунктам
    M->>F: Исправить и опубликовать
    alt Вопрос
        F->>A: POST /api/cms/questions/{id}/publish
        A->>D: Проверить и сменить status на published
    else Черновик События
        F->>A: POST /api/cms/events/drafts/{draftId}/publish
        A->>V: Повторная проверка
        A->>D: Записать версию 1 EventDocumentRecord, удалить черновик
    else Правка опубликованного События
        F->>A: POST /api/cms/events/{id}/publish (expectedVersion)
        A->>V: Повторная проверка
        A->>D: Записать следующую версию или вернуть 409 VersionConflict
    end
    A-->>F: Опубликованная версия
```

Новый Рейс читает последнюю опубликованную версию; уже начатые Рейсы продолжают использовать свой снимок. Блиц снимает колоду опубликованных Вопросов на старте, Смена на свайпах сохраняет снимок Вопроса в момент ответа.

## 6. Развёртывание

```mermaid
flowchart LR
    Browser[Браузер] -- "страницы" --> Front[container: front :3000\nNext.js, React, TS, Tailwind]
    Browser -- "fetch и SSE, CORS" --> Api[container: api :5017\nASP.NET Core Web API, EF Core, Swagger]
    Api --> Pg[(PostgreSQL 16\nобщая БД на VPS)]
    Api --> Polza[polza.ai\nGigaAM-v3 STT и LLM Qwen]
    Api --> Laya[Laya\nтестовая ВМ на CPU]
```

`docker-compose.yml` поднимает `front` и `api`, отдельного контейнера с базой нет: подключение к общей PostgreSQL и секреты приходят из `.env`. Next.js только отдаёт страницы, данные браузер запрашивает у API напрямую по адресу `NEXT_PUBLIC_API_BASE_URL` (по умолчанию `http://localhost:5017`), CORS разрешает origin `localhost:3000`. При старте API применяет EF Core migrations, добавляет недостающий стартовый контент и демо-аккаунты. Swagger доступен, потому что Compose запускает API в режиме Development. Пошаговый запуск описан в [README](../README.md).

## 7. Безопасность и эксплуатационные свойства

- Методы защищены JWT и ролевыми политиками: Рейс доступен любому вошедшему пользователю; Смена, Блиц, профиль, рейтинг и Ачивки — роли Проводника; CMS — Методисту; аналитика — Руководителю. Без токена открыты регистрация, вход, демо-вход и проверка JSON События `/api/events/validate`. Интеграционный API вместо JWT проверяет ключ сервиса в заголовке `X-Api-Key`.
- `/api/auth/demo-login` выдаёт токен трём демо-аккаунтам без пароля (для кнопок «Войти как…» и жюри). По умолчанию он включён; на реальном стенде его нужно выключить настройкой `DemoAccounts:LoginEnabled=false`.
- При генерации CMS-текста вход очищается через `SourceText.RemovePersonalData`; аудиофайл живёт только во время запроса, в БД остаётся расшифровка и метаданные. Голосовая расшифровка сейчас уходит в Laya и LLM (реплика пассажира, объяснение в Разборе) без вычистки. Для промышленного контура тот же scrubber нужно применить к ней до формирования `PassengerReplyContext` и запроса объяснения.
- Голосовые метрики STT/Laya/LLM собираются в журнале и доступны через `/api/analytics/voice`, что позволяет проверять целевую задержку и находить проблемные Шаги.
