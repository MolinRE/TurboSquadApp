<!-- BEGIN:nextjs-agent-rules -->

# This is NOT the Next.js you know

This version has breaking changes — APIs, conventions, and file structure may all differ from your training data. Read the relevant guide in `node_modules/next/dist/docs/` (resolved from this file's directory; in monorepos the `next` package may not be visible from the repo root) before writing any code. Heed deprecation notices.

This block is written and re-added by `next dev` — verify at `node_modules/next/dist/server/lib/generate-agent-files.js`. Removing it from a diff only re-creates the uncommitted change; committing it with your work keeps the tree clean.

<!-- END:nextjs-agent-rules -->

# Фронт «Турбо-Бригады»

Общие правила проекта (словарь, «жёсткие правила», команды по этапам) — в [../AGENTS.md](../AGENTS.md). Здесь только то, что касается `front/`.

- Запуск: `npm run dev` (http://localhost:3000). Проверка перед коммитом: `npm run lint` и `npm run build`.
- Next.js 16, App Router, TypeScript, Tailwind CSS v4 (тема в `src/app/globals.css`, файла `tailwind.config` нет).
