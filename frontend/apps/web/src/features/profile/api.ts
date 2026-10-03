"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { unwrap, type components } from "@auxilia/api-client";

import { createBffClient } from "@/lib/api/client";
import { queryKey } from "@/lib/api/query-keys";

export type Profile = components["schemas"]["ProfileResponse"];
export type UpdateProfile = components["schemas"]["UpdateProfileRequest"];

/** The themes of `PUT /me/preferences` (Q35) and their `next-themes` names. */
export const THEMES = { System: "system", Light: "light", Dark: "dark" } as const;
export type ApiTheme = keyof typeof THEMES;

/** Pictures the API accepts (B-03): JPEG, PNG or WebP up to 2 MB, resized by the server. */
export const IMAGE_TYPES = ["image/jpeg", "image/png", "image/webp"] as const;
export const IMAGE_MAX_BYTES = 2 * 1024 * 1024;

const api = () => createBffClient("tenant");

export const profileKey = (tenant: string) => queryKey(tenant, "identity", "profile");

export function useProfile(tenant: string) {
  return useQuery({
    queryKey: profileKey(tenant),
    queryFn: async () => unwrap(await api().GET("/api/v1/me/profile")),
    retry: false,
  });
}

/** A change of the own profile: the answer is the new profile, stored as the query data. */
export function useProfileMutation<TInput>(
  tenant: string,
  run: (input: TInput) => Promise<Profile>,
) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: run,
    onSuccess: (profile) => client.setQueryData(profileKey(tenant), profile),
  });
}

export async function getProfile() {
  return unwrap(await api().GET("/api/v1/me/profile"));
}

export async function updateProfile(body: UpdateProfile) {
  return unwrap(await api().PUT("/api/v1/me/profile", { body }));
}

export async function changeLanguage(languageCode: string) {
  return unwrap(await api().PUT("/api/v1/me/language", { body: { languageCode } }));
}

export async function changeTheme(theme: ApiTheme) {
  return unwrap(await api().PUT("/api/v1/me/preferences", { body: { theme } }));
}

export async function uploadImage(file: File) {
  const form = new FormData();
  form.append("file", file);
  return unwrap(await api().PUT("/api/v1/me/image", { body: form as never }));
}

export async function deleteImage() {
  return unwrap(await api().DELETE("/api/v1/me/image"));
}

export async function changePassword(currentPassword: string, newPassword: string) {
  await unwrap(await api().POST("/api/v1/me/password", { body: { currentPassword, newPassword } }));
}

/** The `next-themes` name of an API theme (unknown → system). */
export function toThemeName(theme: string): string {
  return THEMES[theme as ApiTheme] ?? THEMES.System;
}

/** The API theme of a `next-themes` name, `undefined` when it is not one of the three. */
export function toApiTheme(name: string): ApiTheme | undefined {
  return (Object.keys(THEMES) as ApiTheme[]).find((key) => THEMES[key] === name);
}

/** Why a picture cannot be uploaded (a translation key), before sending it; `undefined` when it can. */
export function imageProblem(file: Pick<File, "type" | "size">): string | undefined {
  if (!(IMAGE_TYPES as readonly string[]).includes(file.type)) {
    return "app.profile.imageType";
  }

  return file.size > IMAGE_MAX_BYTES ? "app.profile.imageTooLarge" : undefined;
}
