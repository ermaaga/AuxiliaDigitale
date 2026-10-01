"use client";

import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { unwrap, type components } from "@auxilia/api-client";

import { createBffClient } from "@/lib/api/client";
import { queryKey } from "@/lib/api/query-keys";

import { tenantKey } from "./tenant-api";

export type ResourceKey = components["schemas"]["ResourceKeyResponse"];
export type LanguageStats = components["schemas"]["LanguageStatsResponse"];

/** The editor uses technical endpoints of the tenant: a tenant-scoped platform token (D-21). */
const tenantClient = (slug: string) => createBffClient("platform", { tenant: slug });

/** Under `tenantKey(slug, "localization")`, invalidated as a whole after every change. */
export const localizationKey = (slug: string, entity: string, params?: Record<string, unknown>) =>
  queryKey("platform", "tenants", `${slug}:localization`, { entity, ...params });

export function useLanguageStats(slug: string) {
  return useQuery({
    queryKey: localizationKey(slug, "languages"),
    queryFn: async () => unwrap(await tenantClient(slug).GET("/api/v1/localization/languages")),
  });
}

export function useCategories(slug: string) {
  return useQuery({
    queryKey: localizationKey(slug, "categories"),
    queryFn: async () => unwrap(await tenantClient(slug).GET("/api/v1/localization/categories")),
  });
}

export type KeyQuery = {
  page: number;
  pageSize: number;
  sort?: string;
  search?: string;
  category?: string;
  missingLanguage?: string;
};

export function useResourceKeys(slug: string, query: KeyQuery) {
  return useQuery({
    queryKey: localizationKey(slug, "keys", query),
    queryFn: async () =>
      unwrap(
        await tenantClient(slug).GET("/api/v1/localization/keys", {
          params: {
            query: {
              page: query.page,
              pageSize: query.pageSize,
              sort: query.sort,
              search: query.search,
              "filter[category]": query.category,
              "filter[missingLanguage]": query.missingLanguage,
            },
          },
        }),
      ),
    placeholderData: keepPreviousData,
  });
}

export function useLocalizationMutation<TInput, TOutput>(
  slug: string,
  run: (input: TInput) => Promise<TOutput>,
) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: run,
    onSettled: () => client.invalidateQueries({ queryKey: tenantKey(slug, "localization") }),
  });
}

export async function setTranslation(slug: string, id: string, language: string, value: string) {
  await unwrap(
    await tenantClient(slug).PUT("/api/v1/localization/keys/{id}/translations/{language}", {
      params: { path: { id, language } },
      body: { value },
    }),
  );
}

export async function removeTranslation(slug: string, id: string, language: string) {
  await unwrap(
    await tenantClient(slug).DELETE("/api/v1/localization/keys/{id}/translations/{language}", {
      params: { path: { id, language } },
    }),
  );
}

export async function createKey(
  slug: string,
  body: {
    key: string;
    category: string;
    description: string | null;
    translations: Record<string, string> | null;
  },
) {
  return unwrap(await tenantClient(slug).POST("/api/v1/localization/keys", { body }));
}

export async function updateKey(
  slug: string,
  id: string,
  body: { category: string; description: string | null },
) {
  await unwrap(
    await tenantClient(slug).PUT("/api/v1/localization/keys/{id}", {
      params: { path: { id } },
      body,
    }),
  );
}

export async function deleteKey(slug: string, id: string) {
  await unwrap(
    await tenantClient(slug).DELETE("/api/v1/localization/keys/{id}", { params: { path: { id } } }),
  );
}

/** The value of a key in a language, or `undefined` when it is missing. */
export function translationOf(key: ResourceKey, language: string) {
  return key.translations.find((translation) => translation.languageCode === language);
}
