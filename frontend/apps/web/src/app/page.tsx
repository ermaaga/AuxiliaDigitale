import { Button } from "@auxilia/ui/components/button";

// Placeholder until the tenant routing, BFF and shell are built (tasks P3-03…P3-06).
export default function Home() {
  return (
    <main className="flex flex-1 flex-col items-center justify-center gap-6 p-6">
      <h1 className="text-3xl font-semibold tracking-tight">Auxilia</h1>
      <Button disabled>AuxiliaDigitale</Button>
    </main>
  );
}
