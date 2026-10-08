"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { unwrap, type components } from "@auxilia/api-client";

import { createBffClient } from "@/lib/api/client";
import { queryKey } from "@/lib/api/query-keys";

export type TwoFactorStatus = components["schemas"]["TwoFactorStatusResponse"];
export type TwoFactorEnrollment = components["schemas"]["TwoFactorEnrollmentResponse"];

/** Error codes of the second factor at sign-in (N04). */
export const TWO_FACTOR_ERRORS = {
  /** The password is right: the code of the app is needed. */
  required: "AUX-12072",
  /** Wrong, expired or already used code. */
  rejected: "AUX-12073",
  /** The user's roles require the app and none is set: enrol it on the setup page. */
  setupRequired: "AUX-12074",
} as const;

/** The six digits of a code, whatever the user typed between them (spaces of the app's display). */
export function normalizeCode(code: string): string {
  return code.replace(/\D/g, "").slice(0, 6);
}

const api = () => createBffClient("tenant");

export const twoFactorKey = (tenant: string) => queryKey(tenant, "identity", "two-factor");

export function useTwoFactorStatus(tenant: string) {
  return useQuery({
    queryKey: twoFactorKey(tenant),
    queryFn: async () => unwrap(await api().GET("/api/v1/me/two-factor")),
    retry: false,
  });
}

/** Enrolment, confirmation and disable of the own app; each refreshes the status. */
export function useTwoFactorMutations(tenant: string) {
  const client = useQueryClient();
  const refresh = () => client.invalidateQueries({ queryKey: twoFactorKey(tenant) });

  const begin = useMutation({
    mutationFn: async () => unwrap(await api().POST("/api/v1/me/two-factor/enrollment")),
  });
  const confirm = useMutation({
    mutationFn: async (code: string) =>
      unwrap(
        await api().POST("/api/v1/me/two-factor/confirm", { body: { code: normalizeCode(code) } }),
      ),
    onSuccess: refresh,
  });
  const disable = useMutation({
    mutationFn: async (password: string) =>
      unwrap(await api().POST("/api/v1/me/two-factor/disable", { body: { password } })),
    onSuccess: refresh,
  });

  return { begin, confirm, disable };
}

/** An Administrator removes the app of a user (lost phone): the user's sessions end (`identity.users.manage`). */
export async function resetUserTwoFactor(userId: string) {
  await unwrap(
    await api().DELETE("/api/v1/identity/users/{userId}/two-factor", {
      params: { path: { userId } },
    }),
  );
}
