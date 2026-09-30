import * as React from "react";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@auxilia/ui/components/card";

/**
 * Frame of the public pages (F01, F23): the tenant brand gradient beside the form on large screens (legacy login
 * background), the app name, one card. Branding (logo, background image, app name) comes with S-02.
 */
export function AuthCard({
  appName,
  title,
  description,
  toolbar,
  children,
}: {
  appName: string;
  title: string;
  description?: string;
  toolbar?: React.ReactNode;
  children: React.ReactNode;
}) {
  return (
    <div className="grid min-h-dvh flex-1 lg:grid-cols-2">
      <div
        className="hidden bg-brand p-10 text-white lg:flex lg:flex-col lg:justify-end"
        aria-hidden
      >
        <p className="text-4xl font-semibold tracking-tight drop-shadow-sm">{appName}</p>
      </div>
      <main id="main" className="flex flex-col p-4 sm:p-8">
        <div className="flex items-center justify-between gap-2">
          <p className="text-lg font-semibold tracking-tight lg:invisible">{appName}</p>
          <div className="flex items-center gap-1">{toolbar}</div>
        </div>
        <div className="flex flex-1 items-center justify-center py-8">
          <Card className="w-full max-w-sm">
            <CardHeader>
              <CardTitle>
                <h1 className="text-xl font-semibold">{title}</h1>
              </CardTitle>
              {description ? <CardDescription>{description}</CardDescription> : null}
            </CardHeader>
            <CardContent>{children}</CardContent>
          </Card>
        </div>
      </main>
    </div>
  );
}
