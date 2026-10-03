"use client";

import * as React from "react";

type Access = { userId?: string; roles: readonly string[]; permissions: ReadonlySet<string> };

const AccessContext = React.createContext<Access>({ roles: [], permissions: new Set() });

/**
 * Roles and effective permissions of the signed-in user (F22, `GET /me`), loaded once by the `(app)` layout. Hiding an
 * action with {@link useCan} is UX only: the API checks every permission again.
 */
export function PermissionsProvider({
  userId,
  roles,
  permissions,
  children,
}: {
  userId?: string;
  roles: readonly string[];
  permissions: readonly string[];
  children: React.ReactNode;
}) {
  const value = React.useMemo(
    () => ({ userId, roles, permissions: new Set(permissions) }),
    [userId, roles, permissions],
  );
  return <AccessContext.Provider value={value}>{children}</AccessContext.Provider>;
}

/** The user holds the permission (e.g. `directory.clients.delete`). */
export function useCan(permission: string): boolean {
  return React.useContext(AccessContext).permissions.has(permission);
}

/** The user has the role (`Administrator`, `Employee`, `Client`). */
export function useHasRole(role: string): boolean {
  return React.useContext(AccessContext).roles.includes(role);
}

/** The id of the signed-in user (e.g. an employee's own appointments); undefined outside the `(app)` layout. */
export function useCurrentUserId(): string | undefined {
  return React.useContext(AccessContext).userId;
}
