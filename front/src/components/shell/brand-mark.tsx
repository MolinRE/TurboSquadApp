import { TrainFront } from "lucide-react";
import { cn } from "cn";

export function BrandMark({ className }: { className?: string }) {
  return (
    <span
      className={cn(
        "grid size-8 shrink-0 place-items-center rounded-lg bg-brand text-white",
        className,
      )}
    >
      <TrainFront className="size-[18px]" aria-hidden />
    </span>
  );
}
