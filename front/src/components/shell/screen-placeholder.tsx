import { Badge } from "@/components/ui/badge";
import { getScreen, stageLabels } from "@/lib/screens";
import { cn } from "cn";

/** Заглушка экрана: что здесь будет по PRD. Заменяется настоящим экраном. */
export function ScreenPlaceholder({
  id,
  className,
}: {
  id: string;
  className?: string;
}) {
  const screen = getScreen(id);

  return (
    <section
      className={cn(
        "flex max-w-2xl flex-col gap-4 rounded-2xl border border-dashed border-input bg-card/60 p-5",
        className,
      )}
    >
      <div className="flex flex-wrap items-center gap-2">
        <Badge variant={screen.stage === 1 ? "default" : "secondary"}>
          {stageLabels[screen.stage]}
        </Badge>
        <span className="text-xs text-muted-foreground">{screen.role}</span>
      </div>
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-extrabold tracking-tight text-balance">
          {screen.title}
        </h1>
        <p className="text-muted-foreground">{screen.summary}</p>
      </div>
      <div className="flex flex-col gap-2">
        <h2 className="text-xs font-bold tracking-wider text-muted-foreground uppercase">
          Что здесь будет
        </h2>
        <ul className="flex list-disc flex-col gap-1 pl-5 text-sm marker:text-brand">
          {screen.contents.map((item) => (
            <li key={item}>{item}</li>
          ))}
        </ul>
      </div>
    </section>
  );
}
