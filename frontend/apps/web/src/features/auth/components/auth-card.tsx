import * as React from "react";
import Image from "next/image";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@auxilia/ui/components/card";

/**
 * Frame of the public pages (F01, F23): the tenant login background (gradient, colour or image; the brand gradient
 * by default) beside the form on large screens, the app name or the tenant logo, one card.
 */
export function AuthCard({
  appName,
  logoUrl,
  background,
  title,
  description,
  toolbar,
  children,
}: {
  appName: string;
  /** Tenant logo: shown instead of the name, which stays its alternative text. */
  logoUrl?: string;
  /** Style of the background panel (from `loginBackground`); the brand gradient when absent. */
  background?: React.CSSProperties;
  title: string;
  description?: string;
  toolbar?: React.ReactNode;
  children: React.ReactNode;
}) {
  return (
    <div className="grid min-h-dvh flex-1 lg:grid-cols-2">
      <div
        className={
          background
            ? "hidden p-10 lg:flex lg:flex-col lg:justify-end"
            : "hidden bg-brand p-10 text-white lg:flex lg:flex-col lg:justify-end"
        }
        style={background}
        data-testid="login-background"
        aria-hidden
      >
        {logoUrl ? null : (
          <p className="text-4xl font-semibold tracking-tight drop-shadow-sm">{appName}</p>
        )}
      </div>
      <main id="main" className="flex flex-col p-4 sm:p-8">
        <div className="flex items-center justify-between gap-2">
          {logoUrl ? (
            <Image
              src={logoUrl}
              alt={appName}
              width={160}
              height={40}
              unoptimized
              className="h-10 w-auto max-w-40 object-contain"
            />
          ) : (
            <p className="text-lg font-semibold tracking-tight lg:invisible">{appName}</p>
          )}
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
