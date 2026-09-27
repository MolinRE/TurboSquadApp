# Архитектура «Турбо-Бригады»

Документ описывает архитектуру хакатонного MVP по [PRD v7](prd-turbo-brigada-v7.md) и текущему коду репозитория. Текущая поставка уже содержит игровой контур «Симулятора рейса», «Смены на свайпах», CMS, авторизацию, скоринг и голосовой конвейер. Блоки, которых пока нет в коде, отмечены как **целевой контур PRD**.

## 1. Архитектурные решения

- **Модульный монолит ASP.NET Core.** Модули разделены по доменам и общаются через сервисы и явные модели; отдельные микросервисы для игрового движка не нужны на масштабе MVP.
- **Сервер авторитетен.** Сервер проверяет роль, текущий Шаг, Условия и таймер. Клиент отвечает за отображение и запись голоса, но не может самостоятельно продвинуть Рейс или изменить Шкалы.
- **Движок — чистый редьюсер.** `TripEngine.Reduce(state, action)` не знает о БД, HTTP и часах. Это делает ветвление тестируемым и объяснимым.
- **Контент версионируется.** Событие хранится целым JSON-документом в `jsonb`; при старте Рейса фиксируются версии Событий, справочники и настройки. Правка CMS не меняет уже начатый Рейс.
- **Журнал вместо отдельного снимка состояния.** `TripJournalRecord` хранит действия и результаты, а `TripService` при чтении заново проигрывает журнал на зафиксированном контенте. Так сохраняется полный Разбор и воспроизводимость.
- **ИИ не определяет правила игры.** STT переводит аудио в текст, Laya выбирает один из заранее описанных Вариантов и считает оценку, а переходы и последствия применяет `TripEngine`. LLM только формирует реплику пассажира.

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

| Компонент | Реализация | Основные API |
|---|---|---|
| HTTP boundary и Auth | `back/Program.cs`, `JwtTokenService`, `LoginService`, `RegistrationService` | `/api/auth/*`; Bearer JWT; роли `conductor`, `manager`, `methodologist` |
| Симулятор рейса | `back/Trips/TripService.cs`, `TripEngine.cs`, `TripState.cs` | `/api/trips`, `/variant`, `/voice`, `/timeout`, `/proactive`, `/debrief` |
| Голос | `back/Voice/VoicePipeline.cs`, `TripService.StreamVoiceReplyAsync` | multipart `/api/trips/{id}/voice`, SSE `/api/trips/{id}/voice/{attemptId}/reply` |
| Свайпы | `back/Swipes/SwipeShiftService.cs`, `ShiftRules.cs` | `/api/swipe-shifts/*` |
| Контент и CMS | `back/Events`, `back/Questions`, `back/Sources` | `/api/cms/events/*`, `/questions/*`, `/sources/*` |
| Валидация графа | `EventValidator`, `QuestionValidator` | проверка до сохранения/публикации |
| Генерация | `QuestionGenerationService`, `EventGenerationService` | `/api/cms/sources/{id}/generate*` |
| Скоринг | `KnowledgeScoringService`, profile/leaderboard endpoints | `/api/profile`, `/api/leaderboard` |
| Аналитика | `VoiceAnalyticsService` | `/api/analytics/voice` для роли Руководителя |
| Данные | `AppDbContext`, `back/Data/Migrations` | PostgreSQL, схема только через EF Core migrations |

## 3. Хранение данных

```mermaid
erDiagram
    APP_USER ||--o{ APP_USER_ROLE : has
    DEPOT ||--o{ BRIGADE : contains
    BRIGADE ||--o{ APP_USER : assigns
    APP_USER ||--o{ TRIP : starts
    TRIP ||--o{ TRIP_JOURNAL : records
    APP_USER ||--o{ SWIPE_SHIFT : starts
    SWIPE_SHIFT ||--o{ SWIPE_ANSWER : contains
    SOURCE ||--o{ QUESTION : generates
    SOURCE ||--o{ EVENT_DRAFT : generates
    APP_USER ||--o{ KNOWLEDGE_MASTERY : learns

    TRIP {
      uuid id
      string service_class
      jsonb directory_snapshot
      jsonb settings_snapshot
      jsonb event_versions
      string status
      datetime step_started_at
    }
    TRIP_JOURNAL {
      int seq
      string kind
      string event_id
      int event_version
      string variant_id
      jsonb scale_changes
      text voice_transcript
      string voice_choice
      text passenger_reply
    }
    EVENT_DOCUMENT {
      string event_id
      int version
      jsonb document
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

- Таймер стартует от `TripRecord.StepStartedAt`; клиентское время не используется. Допуск на сетевую задержку — около одной секунды.
- `attemptId` делает повторную отправку идемпотентной, а атомарный claim не допускает две генерации одной реплики.
- Для принятого голоса используется двухфазное применение: сначала в журнале фиксируется `VoiceAttempt` и клиент получает предпросмотр ветки, затем после успешного SSE-потока атомарно коммитятся решение, скоринг и реплика пассажира. Пока попытка pending, следующий ход блокируется.
- Laya может выбрать только Вариант из переданного списка. Переход, критическая ошибка, пороги Шкал и Срыв вычисляются `TripEngine`.
- Ответы API с отказом действия используют `ProblemDetails` и код причины (`StaleStep`, `HiddenVariant`, `TimerNotExpired` и т. п.), поэтому UI может безопасно повторить запрос или показать объяснение.

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
    Browser[Браузер] --> Front[container: front\nNext.js :3000]
    Front --> Api[container: api\nASP.NET Core :8080]
    Api --> Pg[(PostgreSQL\nобщая БД из .env)]
    Api --> Polza[polza.ai\nSTT и LLM]
    Api --> Laya[Laya HTTP endpoint]
```

`docker-compose.yml` поднимает `front` и `api`; подключение к PostgreSQL и секреты приходят из `.env`, а при старте API применяются EF Core migrations и seed-контент. В PRD описан отдельный `laya-serve` контейнер, но текущий compose использует настроенный HTTP endpoint Laya. Это точка развития для закрытого контура заказчика.

Текущий код не содержит отдельного Integration API/xAPI, Блица, сезонов, челленджей, push/e-mail уведомлений и двусторонней записи в HR. Эти возможности предусмотрены границей модульного монолита и могут быть добавлены отдельными модулями без изменения `TripEngine` и формата журнала.

## 7. Безопасность и эксплуатационные свойства

- Все игровые, CMS и аналитические методы защищены JWT и ролевыми политиками; регистрация и login — анонимные методы.
- Секреты (`Jwt`, `POLZA_API_KEY`, `LAYA_API_TOKEN`) не входят в код и передаются через `.env`/переменные окружения.
- При генерации CMS-текста вход очищается через `SourceText.RemovePersonalData`; аудиофайл живёт только во время запроса, в БД остаётся расшифровка и метаданные. Для промышленного контура тот же scrubber нужно применить к голосовой расшифровке до формирования `PassengerReplyContext`.
- EF Core migrations — единственный способ изменить схему; `Database:MigrateOnStartup` используется в demo compose.
- Голосовые метрики STT/Laya/LLM собираются в журнале и доступны через `/api/analytics/voice`, что позволяет проверять целевую задержку и находить проблемные Шаги.

## 8. Что показать на защите

1. **Самостоятельность:** открыть `TripEngine` и показать, что состояние меняется чистой функцией, а граф События редактируется и валидируется в CMS.
2. **Надёжность:** изменить Вариант в CMS, опубликовать новую версию и начать новый Рейс; старый Рейс не изменится благодаря снимку версий.
3. **ИИ под контролем:** в голосовом Шаге STT и Laya дают текст/классификацию, но только заранее описанный граф решает, какая ветка и какие Шкалы применяются.
4. **Объяснимость:** открыть Разбор — журнал содержит цитату Источника, решение, изменения Шкал, таймаут, скоринг и метрики голосового ответа.
