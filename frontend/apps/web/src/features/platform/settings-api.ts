"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { unwrap, type components } from "@auxilia/api-client";

import { createBffClient } from "@/lib/api/client";

import { tenantKey } from "./tenant-api";

export type TenantSetting = components["schemas"]["SettingResponse"];
export type TenantBranding = components["schemas"]["BrandingResponse"];
export type BrandingImageKind = "logo" | "background";

/** Settings and branding are technical endpoints of the tenant: a tenant-scoped platform token (D-21). */
const tenantClient = (slug: string) => createBffClient("platform", { tenant: slug });

export function useTenantSettings(slug: string) {
  return useQuery({
    queryKey: tenantKey(slug, "settings"),
    queryFn: async () => unwrap(await tenantClient(slug).GET("/api/v1/settings")),
  });
}

/** Sets (`value`) or restores (`null`) the tenant value of a setting; the answer replaces it in the list. */
export function useSaveSetting(slug: string) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: async ({ key, value }: { key: string; value: unknown }) => {
      const params = { params: { path: { key } } };
      return value === null
        ? unwrap(await tenantClient(slug).DELETE("/api/v1/settings/{key}", params))
        : unwrap(
            await tenantClient(slug).PUT("/api/v1/settings/{key}", {
              ...params,
              body: { value: value as never },
            }),
          );
    },
    onSuccess: (setting) => {
      client.setQueryData<TenantSetting[]>(tenantKey(slug, "settings"), (list) =>
        list?.map((item) => (item.key === setting.key ? setting : item)),
      );
      void client.invalidateQueries({ queryKey: tenantKey(slug, "branding") });
    },
  });
}

export function useTenantBranding(slug: string) {
  return useQuery({
    queryKey: tenantKey(slug, "branding"),
    queryFn: async () => unwrap(await tenantClient(slug).GET("/api/v1/branding")),
  });
}

/** Sets the tenant value of one setting (the branding form saves its changed fields one by one). */
export async function saveSetting(slug: string, key: string, value: unknown) {
  return unwrap(
    await tenantClient(slug).PUT("/api/v1/settings/{key}", {
      params: { path: { key } },
      body: { value: value as never },
    }),
  );
}

/** Uploads (`File`) or removes (`null`) a branding image; answers with the branding. */
export async function saveBrandingImage(slug: string, image: BrandingImageKind, file: File | null) {
  const params = { params: { path: { asset: image } } };
  if (file === null) {
    return unwrap(await tenantClient(slug).DELETE("/api/v1/branding/{asset}", params));
  }

  const form = new FormData();
  form.append("file", file);
  return unwrap(
    await tenantClient(slug).PUT("/api/v1/branding/{asset}", {
      ...params,
      body: form as never,
    }),
  );
}
