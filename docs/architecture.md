# Архитектура «Турбо-Бригады»

## 1. Архитектурные решения

- **Модульный монолит ASP.NET Core.** Монолит выбран в условиях хакатона, так же разден на модули для возможности миграции на микросервисы
- **Сервер авторитетен.** Сервер проверяет роль, текущий Шаг, Условия и таймер. Клиент отвечает за отображение и запись голоса, но не может самостоятельно продвинуть Рейс или изменить Шкалы.
- **Движок — редьюсер.** `TripEngine.Reduce(state, action)` не знает о БД, HTTP и часах. Это делает ветвление тестируемым и объяснимым.
- **Контент версионируется.** Событие хранится целым JSON-документом в `jsonb`; при старте Рейса фиксируются версии Событий, справочники и настройки. Правка CMS не меняет уже начатый Рейс.
- **Журнал вместо отдельного снимка состояния.** `TripJournalRecord` хранит действия и результаты, а `TripService` при чтении заново проигрывает журнал на зафиксированном контенте. Так сохраняется полный Разбор и воспроизводимость.
- **ИИ не оценивает сотрудников.** Голосовая модель переводит аудио в текст. Laya выбирает среди заранее описанных вариантов. Переходы и последствия применяет алгоритм `TripEngine`. Текстовая LLM формирует реплику пассажира на заданную тему.

## 2. Компоненты решения

```mermaid
flowchart LR
    subgraph Clients["Клиенты: Next.js + TypeScript, mobile-first"]
        Player["Проводник\nигры, Разборы, профиль, рейтинг"]
        Methodologist["Методист\nИсточники и CMS контента"]
        Manager["Руководитель\nаналитика подразделения"]
    end

    subgraph API["ASP.NET Core API: модульный монолит"]
        Edge["HTTP boundary\nJWT, роли, CORS, единый ProblemDetails"]
        Auth["Auth\nregister/login/demo-login, роли"]
        Trips["Trips\nTripService + server timer"]
        Engine["TripEngine\nграф, Условия, Флаги, Шкалы"]
        Voice["Voice\nSTT → Laya → оценка → SSE"]
        Swipes["Swipes\nколода, циклы Woodpecker, таймауты"]
        Content["Content/CMS\nИсточники, Вопросы, События, публикация"]
        Generation["Generation\nочищенный текст → JSON-черновики"]
        Scoring["Scoring\nосвоение, очки, профиль, рейтинг"]
        Analytics["Analytics\nлатентность и качество голосовых Шагов"]
        Integration["Integration API / xAPI\nцелевой контур PRD"]
    end

    DB[("PostgreSQL\nEF Core migrations")]
    STT["polza.ai\nGigaAM-v3 STT"]
    Laya["Laya\nвыбор Варианта и оценка"]
    LLM["polza.ai LLM\nреплика пассажира и генерация контента"]
    HR["HR / LMS\nцелевой внешний контур"]

    Player --> Edge
    Methodologist --> Edge
    Manager --> Edge
    Edge --> Auth
    Edge --> Trips
    Edge --> Swipes
    Edge --> Content
    Edge --> Scoring
    Edge --> Analytics
    Trips --> Engine
    Trips --> Voice
    Content --> Generation
    Trips --> Scoring
    Swipes --> Scoring
    Auth --> DB
    Trips --> DB
    Swipes --> DB
    Content --> DB
    Scoring --> DB
    Analytics --> DB
    Voice --> STT
    Voice --> Laya
    Voice --> LLM
    Generation --> LLM
    Integration -. "планируется" .-> DB
    Integration -. "xAPI / импорт externalId" .-> HR
```

### 2.1. Реализация компонентов в коде

| Компонент  | Основные API |
|---|---|
| HTTP boundary и Auth | `/api/auth/*`; Bearer JWT; роли |
| Симулятор рейса | `/api/trips`, `/variant`, `/voice`, `/timeout`, `/proactive`, `/debrief` |
| Голос | multipart `/api/trips/{id}/voice`, SSE `/api/trips/{id}/voice/{attemptId}/reply` |
| Свайпы | `/api/swipe-shifts/*` |
| Контент и CMS | `/api/cms/events/*`, `/questions/*`, `/sources/*` |
| Генерация | `/api/cms/sources/{id}/generate*` |
| Скоринг | `/api/profile`, `/api/leaderboard` |
| Аналитика | `/api/analytics/voice` для роли Руководителя |
| Данные | PostgreSQL |

## 3. Хранение данных

```mermaid
erDiagram
    DEPOTS ||--o{ BRIGADES : contains
    DEPOTS ||--o{ APP_USERS : assigns
    BRIGADES ||--o{ APP_USERS : assigns
    APP_USERS ||--o{ USER_ROLES : has

    APP_USERS ||--o{ TRIPS : starts
    TRIPS ||--o{ TRIP_JOURNAL : contains

    APP_USERS ||--o{ SWIPE_SHIFTS : starts
    SWIPE_SHIFTS ||--o{ SWIPE_ANSWERS : contains
    SWIPE_SHIFTS ||--o{ SWIPE_SHIFTS : previous_cycle
    QUESTIONS ||--o{ SWIPE_ANSWERS : answers

    APP_USERS ||--o{ BLITZ_SESSIONS : starts
    BLITZ_SESSIONS ||--o{ BLITZ_ANSWERS : contains

    SOURCES ||--o{ QUESTIONS : generates
    SOURCES ||--o{ EVENT_DRAFTS : generates

    APP_USERS ||--o{ KNOWLEDGE_MASTERIES : develops
    APP_USERS ||--o| CONDUCTOR_PROFILES : has
    APP_USERS ||--o{ ACHIEVEMENT_AWARDS : earns

    DEPOTS {
        uuid id PK
        string name
    }

    BRIGADES {
        uuid id PK
        uuid depot_id FK
        string name
    }

    APP_USERS {
        uuid id PK
        string username UK
        string display_name
        string password_hash
        uuid depot_id FK
        uuid brigade_id FK
        datetime created_at
    }

    USER_ROLES {
        uuid user_id PK, FK
        string role PK
    }

    SOURCES {
        uuid id PK
        string title
        text text
        datetime created_at
    }

    QUESTIONS {
        string id PK
        string type
        string status
        string statement
        jsonb options
        text explanation_text
        text explanation_key_fact
        string quote
        string source
        uuid source_id FK
        string topic
        jsonb categories
        jsonb service_classes
        float base_frequency
        int time_limit_sec
        int knowledge_cost
    }

    EVENT_DOCUMENTS {
        string event_id PK
        int version PK
        jsonb document
        datetime published_at
    }

    EVENT_DRAFTS {
        uuid id PK
        uuid source_id FK
        jsonb document
        datetime created_at
    }

    SCALES {
        string code PK
        string name
        int min
        int max
        int start
        int failure_threshold
        boolean mandatory
        string failure_reason
    }

    SERVICE_CLASSES {
        string code PK
        string name
        string description
        int sort_order
    }

    TRIP_SETTINGS {
        string id PK
        jsonb document
    }

    TRIPS {
        uuid id PK
        uuid user_id FK
        string service_class
        string status
        string failure_cause
        string failure_scale
        jsonb directory
        jsonb settings
        jsonb event_versions
        datetime started_at
        datetime finished_at
        datetime step_started_at
    }

    TRIP_JOURNAL {
        uuid trip_id PK, FK
        int seq PK
        string kind
        string event_id
        int event_version
        string step_id
        string variant_id
        boolean timed_out
        int knowledge_delta
        jsonb scale_changes
        jsonb flags_set
        text voice_transcript
        string voice_choice
        text voice_error
        text voice_passenger_reply
        int voice_latency_ms
        datetime created_at
    }

    SWIPE_SHIFTS {
        uuid id PK
        uuid user_id FK
        string mode
        int cycle
        uuid previous_cycle_id FK
        string status
        string failure_scale
        jsonb deck
        jsonb scales
        datetime started_at
        datetime finished_at
        datetime card_shown_at
    }

    SWIPE_ANSWERS {
        uuid shift_id PK, FK
        int seq PK
        string question_id FK
        string answer
        string verdict
        boolean is_repeat
        int elapsed_ms
        int knowledge_delta
        jsonb question_snapshot
        jsonb scale_changes
        datetime answered_at
    }

    BLITZ_SESSIONS {
        uuid id PK
        uuid user_id FK
        string status
        jsonb deck
        datetime started_at
        datetime finished_at
        datetime question_shown_at
    }

    BLITZ_ANSWERS {
        uuid session_id PK, FK
        int seq PK
        string question_id
        jsonb selected_option_ids
        boolean timed_out
        string verdict
        int elapsed_ms
        datetime answered_at
    }

    KNOWLEDGE_MASTERIES {
        uuid user_id PK, FK
        string unit_type PK
        string unit_id PK
        string competence PK
        boolean is_mastered
        int awarded_cost
        datetime last_attempt_at
    }

    CONDUCTOR_PROFILES {
        uuid user_id PK, FK
        int rank_index
    }

    ACHIEVEMENT_AWARDS {
        uuid user_id PK, FK
        string achievement_id PK
        datetime earned_at
    }
```

Ключевые правила хранения:

1. В `EventDocumentRecord` лежит полный граф События; новая публикация создаёт следующую версию.
2. В `TripRecord` лежат снимки справочников и `eventId → version`; поэтому исторический Разбор не зависит от текущей CMS.
3. В `TripJournalRecord` сохраняются решения, таймауты, изменения Шкал, Флаги, ссылки на источник и метрики STT/Laya/LLM. Аудио не сохраняется.
4. Для Свайпов ответ дополнительно содержит снимок Вопроса, чтобы изменение опубликованного Вопроса не переписало историю.

## 4. Последовательность голосового Шага

Это основной сквозной сценарий для демонстрации архитектуры: ветвление выполняет собственный движок, а ИИ подключён как контролируемый адаптер.

```mermaid
sequenceDiagram
    autonumber
    actor C as Проводник
    participant F as Next.js
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
    E-->>A: Состояние: первый Шаг, Шкалы, доступные Варианты
    A->>D: TripRecord + начальная запись журнала
    A-->>F: TripView (expiresAt считается сервером)

    C->>F: Ответить голосом
    F->>A: POST /api/trips/{id}/voice (audio, eventId, stepId, attemptId)
    A->>D: Загрузить Рейс и проиграть журнал
    A->>A: Проверить JWT, текущий voice-Шаг, размер аудио и таймер
    A->>S: audio/transcriptions (GigaAM-v3)
    S-->>A: transcript
    A->>L: situation + brief + transcript + доступные Варианты
    L-->>A: choice, confidence, score, RoleModel stages, safety
    A->>E: RecordVoiceAttempt
    E-->>A: Голосовая попытка добавлена в журнал

    alt confidence ниже порога или неизвестный Вариант
        A->>D: Сохранить не применённую попытку и ошибку
        A-->>F: 422 + объяснение, Рейс остаётся на Шаге
    else таймер истёк
        A->>E: TimeOut
        E-->>A: Ветка timeout, последствия и следующий Шаг
        A->>D: Сохранить таймаут и изменения Шкал
        A-->>F: TripView с новой позицией
    else попытка принята
        A->>E: ChooseVariant(choice)
        E-->>A: Проверить Условия, изменить Шкалы/Флаги, выбрать переход
        A->>D: Сохранить VoiceAttempt (решение пока pending)
        A-->>F: Предпросмотр новой позиции + pending passenger reply
    end

    F->>A: GET /api/trips/{id}/voice/{attemptId}/reply
    A->>D: Атомарно занять генерацию по attemptId
    A->>E: Предварительно применить Вариант для контекста пассажира
    A->>G: Стримить контекст События и последствий
    loop токены
        G-->>A: token
        A-->>F: SSE token
    end
    A->>D: Сохранить passengerReply, решение, скоринг и latency
    A-->>F: SSE done + актуальный TripView
    F-->>C: Показать реплику и следующий Шаг
```

### Гарантии сценария

- Таймер стартует от `TripRecord.StepStartedAt`; клиентское время не используется
- `attemptId` делает повторную отправку идемпотентной
- Для принятого голоса используется двухфазное применение: сначала в журнале фиксируется `VoiceAttempt` и клиент получает предпросмотр ветки, затем после успешного SSE-потока коммитятся решение, скоринг и реплика пассажира. Пока попытка pending, следующий ход блокируется.
- Laya может выбрать только Вариант из переданного списка. Переход, критическая ошибка, пороги Шкал и Срыв вычисляются `TripEngine`.
- Ответы API с отказом действия используют `ProblemDetails` и код причины (`StaleStep`, `HiddenVariant`, `TimerNotExpired` и т. п.) для управления на UI

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
    A->>A: Разбить текст по структуре, удалить ПДн
    A->>G: Отправить только очищенный фрагмент и JSON-схему
    G-->>A: JSON-кандидат
    A->>V: Проверить схему, цитаты, справочники и граф
    V-->>A: ошибки/предупреждения или валидный черновик
    A->>D: Сохранить QuestionRecord/EventDraftRecord
    A-->>F: Список черновиков рядом с цитатами
    M->>F: Исправить и опубликовать
    F->>A: POST /api/cms/events/{id}/publish
    A->>V: Повторная проверка перед публикацией
    A->>D: Записать новую неизменяемую версию EventDocumentRecord
    A-->>F: Опубликованная версия
```

Новый Рейс читает последнюю опубликованную версию; уже начатые Рейсы продолжают использовать свой снимок.

## 6. Развёртывание и границы MVP

```mermaid
flowchart LR
    Browser[Браузер] --> Front[container: front\nNext.js, React, TS, Tailwind]
    Front --> Api[container: api\nASP.NET Core Web API, EF Core, Swagger]
    Api --> Pg[(PostgreSQL кластер)]
    Api --> Polza[polza.ai\nSTT и LLM решения]
    Api --> Laya[laya кластер]
```

`docker-compose.yml` поднимает `front` и `api`; подключение к PostgreSQL и секреты приходят из `.env`, а при старте API применяются EF Core migrations и seed-контент

## 7. Безопасность и эксплуатационные свойства

- Все игровые, CMS и аналитические методы защищены JWT и ролевыми политиками
- При генерации CMS-текста вход очищается через `SourceText.RemovePersonalData`; аудиофайл живёт только во время запроса, в БД остаётся расшифровка и метаданные. Для промышленного контура тот же scrubber нужно применить к голосовой расшифровке до формирования `PassengerReplyContext`.
- Голосовые метрики STT/Laya/LLM собираются в журнале и доступны через `/api/analytics/voice`, что позволяет проверять целевую задержку и находить проблемные Шаги.
