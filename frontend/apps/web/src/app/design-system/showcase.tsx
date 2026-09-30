"use client";

import { useTranslations } from "next-intl";
import { toast } from "sonner";
import { InfoIcon } from "lucide-react";
import { Alert, AlertDescription, AlertTitle } from "@auxilia/ui/components/alert";
import { Avatar, AvatarFallback } from "@auxilia/ui/components/avatar";
import { Badge } from "@auxilia/ui/components/badge";
import { Button } from "@auxilia/ui/components/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@auxilia/ui/components/card";
import { Checkbox } from "@auxilia/ui/components/checkbox";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@auxilia/ui/components/dialog";
import { Input } from "@auxilia/ui/components/input";
import { Label } from "@auxilia/ui/components/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@auxilia/ui/components/select";
import { Separator } from "@auxilia/ui/components/separator";
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
  SheetTrigger,
} from "@auxilia/ui/components/sheet";
import { Skeleton } from "@auxilia/ui/components/skeleton";
import { Switch } from "@auxilia/ui/components/switch";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@auxilia/ui/components/tabs";
import { Textarea } from "@auxilia/ui/components/textarea";
import { ThemeToggle } from "@auxilia/ui/components/theme-toggle";
import { Tooltip, TooltipContent, TooltipTrigger } from "@auxilia/ui/components/tooltip";

const colors = [
  "background",
  "foreground",
  "primary",
  "primary-foreground",
  "secondary",
  "muted",
  "muted-foreground",
  "accent",
  "accent-foreground",
  "destructive",
  "success",
  "warning",
  "border",
  "ring",
] as const;

export function Showcase() {
  const theme = useTranslations("common.theme");
  return (
    <main className="mx-auto flex w-full max-w-5xl flex-col gap-8 p-4 sm:p-8">
      <header className="flex items-center justify-between gap-4">
        <div>
          <h1 className="text-2xl font-semibold tracking-tight">Design system</h1>
          <p className="text-sm text-muted-foreground">
            Tokens, components, themes and tenant branding.
          </p>
        </div>
        <ThemeToggle
          labels={{
            toggle: theme("toggle"),
            light: theme("light"),
            dark: theme("dark"),
            system: theme("system"),
          }}
        />
      </header>

      <div className="h-24 rounded-xl bg-brand" aria-hidden />

      <section aria-labelledby="colors" className="flex flex-col gap-3">
        <h2 id="colors" className="text-lg font-medium">
          Colour tokens
        </h2>
        <ul className="grid grid-cols-2 gap-3 sm:grid-cols-4 md:grid-cols-7">
          {colors.map((name) => (
            <li key={name} className="flex flex-col gap-1 text-xs">
              <span className="h-12 rounded-md border" style={{ background: `var(--${name})` }} />
              <code>--{name}</code>
            </li>
          ))}
        </ul>
      </section>

      <Separator />

      <section aria-labelledby="actions" className="flex flex-col gap-3">
        <h2 id="actions" className="text-lg font-medium">
          Actions and status
        </h2>
        <div className="flex flex-wrap items-center gap-2">
          <Button>Primary</Button>
          <Button variant="secondary">Secondary</Button>
          <Button variant="outline">Outline</Button>
          <Button variant="ghost">Ghost</Button>
          <Button variant="destructive">Delete</Button>
          <Button variant="link">Link</Button>
          <Button disabled>Disabled</Button>
          <Tooltip>
            <TooltipTrigger asChild>
              <Button variant="outline" size="icon" aria-label="Information">
                <InfoIcon />
              </Button>
            </TooltipTrigger>
            <TooltipContent>Tooltip text</TooltipContent>
          </Tooltip>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Badge>Active</Badge>
          <Badge variant="secondary">Inserted</Badge>
          <Badge variant="outline">In progress</Badge>
          <Badge variant="destructive">Rejected</Badge>
          <Avatar>
            <AvatarFallback>MR</AvatarFallback>
          </Avatar>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button variant="outline" onClick={() => toast.success("Saved successfully")}>
            Success toast
          </Button>
          <Button variant="outline" onClick={() => toast.error("Something went wrong (AUX-10000)")}>
            Error toast
          </Button>
        </div>
      </section>

      <Separator />

      <section aria-labelledby="forms" className="grid gap-6 md:grid-cols-2">
        <h2 id="forms" className="text-lg font-medium md:col-span-2">
          Forms
        </h2>
        <div className="flex flex-col gap-2">
          <Label htmlFor="ds-name">Full name</Label>
          <Input id="ds-name" placeholder="Mario Rossi" />
        </div>
        <div className="flex flex-col gap-2">
          <Label htmlFor="ds-fiscal-code">Fiscal code</Label>
          <Input
            id="ds-fiscal-code"
            aria-invalid
            aria-describedby="ds-fiscal-code-error"
            defaultValue="RSSMRA"
          />
          <p id="ds-fiscal-code-error" className="text-sm text-destructive">
            Enter a valid Italian fiscal code
          </p>
        </div>
        <div className="flex flex-col gap-2">
          <Label htmlFor="ds-service">Service</Label>
          <Select>
            <SelectTrigger id="ds-service" className="w-full">
              <SelectValue placeholder="Select" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="isee">ISEE</SelectItem>
              <SelectItem value="730">730</SelectItem>
            </SelectContent>
          </Select>
        </div>
        <div className="flex flex-col gap-2">
          <Label htmlFor="ds-notes">Notes</Label>
          <Textarea id="ds-notes" />
        </div>
        <div className="flex items-center gap-2">
          <Checkbox id="ds-consent" />
          <Label htmlFor="ds-consent">Privacy consent</Label>
        </div>
        <div className="flex items-center gap-2">
          <Switch id="ds-active" defaultChecked />
          <Label htmlFor="ds-active">Active</Label>
        </div>
      </section>

      <Separator />

      <section aria-labelledby="surfaces" className="grid gap-6 md:grid-cols-2">
        <h2 id="surfaces" className="text-lg font-medium md:col-span-2">
          Surfaces and states
        </h2>
        <Card>
          <CardHeader>
            <CardTitle>Card</CardTitle>
            <CardDescription>Content grouped on a surface.</CardDescription>
          </CardHeader>
          <CardContent>
            <Tabs defaultValue="overview">
              <TabsList>
                <TabsTrigger value="overview">Overview</TabsTrigger>
                <TabsTrigger value="cases">Cases</TabsTrigger>
              </TabsList>
              <TabsContent value="overview" className="text-sm">
                Overview tab
              </TabsContent>
              <TabsContent value="cases" className="text-sm">
                Cases tab
              </TabsContent>
            </Tabs>
          </CardContent>
        </Card>
        <div className="flex flex-col gap-3">
          <Alert>
            <InfoIcon />
            <AlertTitle>Information</AlertTitle>
            <AlertDescription>An alert with an icon and text.</AlertDescription>
          </Alert>
          <div className="flex flex-col gap-2" aria-hidden>
            <Skeleton className="h-4 w-3/4" />
            <Skeleton className="h-4 w-1/2" />
          </div>
          <div className="flex gap-2">
            <Dialog>
              <DialogTrigger asChild>
                <Button variant="outline">Dialog</Button>
              </DialogTrigger>
              <DialogContent closeLabel="Close">
                <DialogHeader>
                  <DialogTitle>Delete the case?</DialogTitle>
                  <DialogDescription>This cannot be undone.</DialogDescription>
                </DialogHeader>
                <DialogFooter closeLabel="Cancel">
                  <Button variant="destructive">Delete</Button>
                </DialogFooter>
              </DialogContent>
            </Dialog>
            <Sheet>
              <SheetTrigger asChild>
                <Button variant="outline">Sheet</Button>
              </SheetTrigger>
              <SheetContent closeLabel="Close">
                <SheetHeader>
                  <SheetTitle>New client</SheetTitle>
                  <SheetDescription>Short forms open in a sheet.</SheetDescription>
                </SheetHeader>
              </SheetContent>
            </Sheet>
          </div>
        </div>
      </section>
    </main>
  );
}
