"use client";

import * as React from "react";
import { MonitorIcon, MoonIcon, SunIcon } from "lucide-react";
import { useTheme } from "next-themes";

import { Button } from "./button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuTrigger,
} from "./dropdown-menu";

/** Translated texts of the toggle (skill auxilia-localization: the component has no text of its own). */
type ThemeToggleLabels = { toggle: string; light: string; dark: string; system: string };

/** Theme switch of the topbar: an icon button opening light / dark / system. */
function ThemeToggle({ labels }: { labels: ThemeToggleLabels }) {
  const { theme, setTheme } = useTheme();

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="ghost" size="icon" aria-label={labels.toggle}>
          <SunIcon
            className="scale-100 rotate-0 transition-all dark:scale-0 dark:-rotate-90"
            aria-hidden
          />
          <MoonIcon
            className="absolute scale-0 rotate-90 transition-all dark:scale-100 dark:rotate-0"
            aria-hidden
          />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        <DropdownMenuRadioGroup value={theme} onValueChange={setTheme}>
          <DropdownMenuRadioItem value="light">
            <SunIcon aria-hidden /> {labels.light}
          </DropdownMenuRadioItem>
          <DropdownMenuRadioItem value="dark">
            <MoonIcon aria-hidden /> {labels.dark}
          </DropdownMenuRadioItem>
          <DropdownMenuRadioItem value="system">
            <MonitorIcon aria-hidden /> {labels.system}
          </DropdownMenuRadioItem>
        </DropdownMenuRadioGroup>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}

export { ThemeToggle, type ThemeToggleLabels };
