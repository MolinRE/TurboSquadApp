# Backend

The backend uses EF Core with PostgreSQL. Set `ConnectionStrings:Postgres` in local configuration or through `ConnectionStrings__Postgres`. Set the database credentials with `POSTGRES_USER` and `POSTGRES_PASSWORD`; keep secrets out of the repository. The default connection string points at the Supabase transaction pooler (port 6543) with `No Reset On Close=true`: the pooler does not answer the `DISCARD ALL` that Npgsql sends when it reuses a pooled connection, and the query hangs.

After restoring packages, create and apply migrations with:

```bash
dotnet ef migrations add Initial --project back --output-dir Data/Migrations
dotnet ef database update --project back
```

`DatabaseSession` uses `AppDbContext` directly. The project does not add Unit of Work or a generic repository.

For authentication, set `Jwt__Key` to a random value of at least 32 characters. Optional settings are `Jwt__Issuer`, `Jwt__Audience`, and `Jwt__ExpirationMinutes` (1–1440, default 60). The login endpoint is `POST /api/auth/login` with `username` and `password`; it returns a Bearer token. Registration is available at `POST /api/auth/register`.

The voice pipeline reads `POLZA_API_KEY` and `LAYA_API_TOKEN` (`LAYA_API_KEY` is accepted as a local alias). Optional `Voice__PolzaBaseUrl`, `Voice__SttModel`, `Voice__LayaEndpoint`, `Voice__LayaModel`, `Voice__MinimumConfidence` and `Voice__MaxAudioBytes` override the defaults. During local development the root `.env` is also read for the two provider keys; it is ignored by Git.

To seed the three demo accounts, set `DemoAccounts__SeedOnStartup=true` and provide `DemoAccounts__conductor-star__Password`, `DemoAccounts__conductor-novice__Password`, and `DemoAccounts__manager-methodologist__Password`. The seed is idempotent and adds missing roles without replacing existing passwords. `GET /api/auth/me` requires authentication; the manager and methodologist role checks are available under `/api/auth/role-check/manager` and `/api/auth/role-check/methodologist`.

In development, Swagger UI is available at `/swagger`, backed by the OpenAPI document at `/openapi/v1.json`. Use the **Authorize** button with `Bearer <token>` to call protected endpoints. The documentation routes are enabled only in development.

Starter content for the trip is seeded on startup: the shift-start event, situations №6 and №33 and trip settings from `Content/Seeds/*.json`, the scale and service-class directories from `ContentDirectory.Default` in `Events/ContentDirectory.cs`. Seed events must pass the event validator, otherwise startup fails; only missing records are added, so restarts create no duplicates and methodologist edits are kept. Apply migrations first; to start without a database, set `ContentSeed__SeedOnStartup=false`.

The trip engine lives in `Trips/`. `TripEngine.Reduce(state, action)` is a pure reducer with no database, HTTP or clock: actions are `StartTrip`, `ChooseVariant`, `TimeOut` and `ChooseProactive`. A rejected action returns the same state and a `Rejection` with a reason code, for example a variant hidden by a condition, a timeout on a step without a timer, or a move after the trip has ended. The engine refuses to start with content that fails the event validator or does not match the trip settings. `TripState.Choices` lists the current step's variants with the conditions that hide them. `Debrief.Build` produces layer A of the debrief from the journal after arrival or failure.

The trip API is under `/api/trips` and requires a Bearer token; a trip is visible only to its owner. `POST /api/trips` starts a trip on the latest published event versions and pins its content: event versions by reference, the scale and class directories and the trip settings as a snapshot, so later edits change neither running nor finished trips; `GET /api/trips/{id}` returns the state with only the variants the conductor may choose and the step timer's `expiresAt`; `POST /variant`, `/timeout` and `/proactive` are the moves, and `/variant` and `/timeout` carry the `eventId` and `stepId` being answered, so a repeated click on a passed step is rejected with `StaleStep`. A voice Step exposes no variants and accepts `POST /api/trips/{id}/voice` as multipart fields `eventId`, `stepId` and `audio`; the server streams the audio to GigaAM-v3, sends the transcript and structured choice criteria to Laya, and applies the returned `choice` through the same engine. A provider error or confidence below the configured threshold returns 422, records the attempt, and leaves the Step unchanged; audio is never persisted. `GET /debrief` returns layer A after arrival or failure, including voice attempts. The server owns the timer: an answer later than the timer plus a 1 s tolerance follows the timeout branch, and `/timeout` before expiry is rejected. Engine rejections return 409 with a `reason` code. The trip state is not stored: `TripService` replays the `trip_journal` rows through the engine on the pinned content. Tables `trips` and `trip_journal` come from the `AddTrips` and `AddVoiceAttempts` migrations.
