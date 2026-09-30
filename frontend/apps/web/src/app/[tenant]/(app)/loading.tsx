import { Skeleton } from "@auxilia/ui/components/skeleton";

/** Skeleton of a page of the app while its data loads (skill auxilia-ui-design: no full-page spinners). */
export default function Loading() {
  return (
    <div className="flex flex-col gap-3" aria-busy="true">
      <Skeleton className="h-8 w-1/3" />
      <Skeleton className="h-4 w-2/3" />
      <Skeleton className="h-40 w-full" />
    </div>
  );
}
