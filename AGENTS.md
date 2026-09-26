# AGENTS.md

## Где что искать
- Словарь терминов: CONTEXT.md
- Архитектурные решения: docs/adr/
- Дизайн-система и компоненты: docs/ui-kit/
- Фронт (Next.js): front/, свои правила — front/AGENTS.md

## Какую команду звать на каком этапе
- Завести тикет фичи → /to-tickets (mattpocock, пишет в GitHub Issues)
- Расписать сложную/рискованную фичу подробнее → /sdlc:to-spec
- Реализовать тикет → /implement (mattpocock, тянет tdd)
- Проверить перед мержем → /code-review (mattpocock, две параллельные оси)
- НЕ использовать to-tickets/implement/code-review из плагина sdlc

## Жёсткие правила
- Ни одного нового UI-компонента без проверки docs/ui-kit
- Ни одной ручной правки схемы БД — только через миграции EF Core
- Не менять контракт API без явного согласования вслух
- Каждое изменение в зоне Laya-скоринга — проверить latency (~33мс)
- Новая зависимость — сначала сообщение в чат, потом код
- PR закрывает тикет через "Closes #N" в описании

## Agent skills

### Issue tracker

Задачи ведутся в GitHub Issues репозитория MolinRE/TurboSquadApp (через `gh` CLI). См. `docs/agents/issue-tracker.md`.

### Domain docs

Single-context: один `CONTEXT.md` и `docs/adr/` в корне репозитория. См. `docs/agents/domain.md`.
