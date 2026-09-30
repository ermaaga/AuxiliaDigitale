"use client";

import * as React from "react";
import { CheckIcon, ChevronsUpDownIcon, Loader2Icon } from "lucide-react";
import { useTranslations } from "next-intl";
import { Button } from "@auxilia/ui/components/button";
import {
  Command,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
} from "@auxilia/ui/components/command";
import { Popover, PopoverContent, PopoverTrigger } from "@auxilia/ui/components/popover";
import { cn } from "@auxilia/ui/lib/utils";

export type ComboboxOption = { value: string; label: string; description?: string };

/**
 * Type-ahead select for large lookups (F34, legacy `SearchableSelect`): filters locally, or asks the caller with
 * `onSearch` (server-side search, e.g. clients) while `loading`. Keyboard: type, arrows, Enter, Esc.
 */
export function Combobox({
  id,
  options,
  value,
  onChange,
  placeholder,
  onSearch,
  loading,
  disabled,
  "aria-invalid": invalid,
  "aria-describedby": describedBy,
}: {
  id?: string;
  options: readonly ComboboxOption[];
  value: string | undefined;
  onChange: (value: string | undefined) => void;
  placeholder: string;
  onSearch?: (text: string) => void;
  loading?: boolean;
  disabled?: boolean;
  "aria-invalid"?: boolean;
  "aria-describedby"?: string;
}) {
  const t = useTranslations();
  const [open, setOpen] = React.useState(false);
  const selected = options.find((option) => option.value === value);

  return (
    <Popover open={open} onOpenChange={setOpen}>
      <PopoverTrigger asChild>
        <Button
          id={id}
          type="button"
          variant="outline"
          role="combobox"
          aria-expanded={open}
          aria-invalid={invalid}
          aria-describedby={describedBy}
          disabled={disabled}
          className="w-full justify-between font-normal"
        >
          <span className={cn("truncate", !selected && "text-muted-foreground")}>
            {selected?.label ?? placeholder}
          </span>
          <ChevronsUpDownIcon aria-hidden className="opacity-50" />
        </Button>
      </PopoverTrigger>
      <PopoverContent className="w-(--radix-popover-trigger-width) p-0" align="start">
        <Command shouldFilter={!onSearch}>
          <CommandInput placeholder={t("common.combobox.search")} onValueChange={onSearch} />
          <CommandList>
            {loading ? (
              <div className="flex justify-center p-3" role="status" aria-label={t("Loading")}>
                <Loader2Icon aria-hidden className="size-4 animate-spin" />
              </div>
            ) : (
              <CommandEmpty>{t("common.combobox.empty")}</CommandEmpty>
            )}
            <CommandGroup>
              {options.map((option) => (
                <CommandItem
                  key={option.value}
                  value={`${option.label} ${option.value}`}
                  onSelect={() => {
                    onChange(option.value === value ? undefined : option.value);
                    setOpen(false);
                  }}
                >
                  <CheckIcon
                    aria-hidden
                    className={cn("size-4", option.value === value ? "opacity-100" : "opacity-0")}
                  />
                  <span className="flex flex-col">
                    <span>{option.label}</span>
                    {option.description ? (
                      <span className="text-xs text-muted-foreground">{option.description}</span>
                    ) : null}
                  </span>
                </CommandItem>
              ))}
            </CommandGroup>
          </CommandList>
        </Command>
      </PopoverContent>
    </Popover>
  );
}
