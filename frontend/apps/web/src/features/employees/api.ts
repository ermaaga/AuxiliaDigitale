"use client";

import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { unwrap, type components } from "@auxilia/api-client";

import { createBffClient } from "@/lib/api/client";
import { queryKey } from "@/lib/api/query-keys";

export type EmployeeListItem = components["schemas"]["EmployeeListItemResponse"];
export type EmployeeDetail = components["schemas"]["EmployeeDetailResponse"];
export type CreateEmployee = components["schemas"]["CreateEmployeeRequest"];
export type UpdateEmployee = components["schemas"]["UpdateEmployeeRequest"];
export type ClientListItem = components["schemas"]["ClientListItemResponse"];

/** Query of the employee list (F06): the legacy filters and sorts. */
export type EmployeeListParams = {
  page: number;
  pageSize: number;
  sort?: string;
  "filter[fullName]"?: string;
  "filter[email]"?: string;
  "filter[status]"?: string;
};

/** The grid of the employee list (F21, `directory.employees`). */
export const EMPLOYEES_GRID = "directory.employees";

const api = () => createBffClient("tenant");

/** Everything about employees lives under `employeesKey(tenant)`: a change invalidates it as a whole. */
export const employeesKey = (tenant: string, entity?: string, params?: Record<string, unknown>) =>
  entity === undefined
    ? queryKey(tenant, "directory", "employees")
    : queryKey(tenant, "directory", "employees", { entity, ...params });

/** The client queries (`clientsKey` of the clients feature): assignments change them too. */
const clientsKey = (tenant: string) => queryKey(tenant, "directory", "clients");

export function useEmployees(tenant: string, params: EmployeeListParams) {
  return useQuery({
    queryKey: employeesKey(tenant, "list", params),
    queryFn: async () =>
      unwrap(await api().GET("/api/v1/employees", { params: { query: params } })),
    placeholderData: keepPreviousData,
  });
}

export function useEmployee(tenant: string, id: string) {
  return useQuery({
    queryKey: employeesKey(tenant, "detail", { id }),
    queryFn: async () =>
      unwrap(await api().GET("/api/v1/employees/{id}", { params: { path: { id } } })),
    retry: false,
  });
}

export function useEmployeeSpecializations(tenant: string) {
  return useQuery({
    queryKey: employeesKey(tenant, "specializations"),
    queryFn: async () => unwrap(await api().GET("/api/v1/employees/specializations")),
    staleTime: 60_000,
  });
}

export function useAdministrators(tenant: string, enabled = true) {
  return useQuery({
    queryKey: employeesKey(tenant, "administrators"),
    queryFn: async () => unwrap(await api().GET("/api/v1/employees/administrators")),
    enabled,
    staleTime: 60_000,
  });
}

/** The clients in charge of an employee (B-02: the client list filtered by employee), paged. */
export function useEmployeeClients(
  tenant: string,
  employeeUserId: string,
  page: number,
  pageSize: number,
) {
  return useQuery({
    queryKey: queryKey(tenant, "directory", "clients", {
      entity: "of-employee",
      employeeUserId,
      page,
      pageSize,
    }),
    queryFn: async () =>
      unwrap(
        await api().GET("/api/v1/clients", {
          params: {
            query: { view: "all", "filter[employeeUserId]": employeeUserId, page, pageSize },
          },
        }),
      ),
    placeholderData: keepPreviousData,
  });
}

/** Clients matching a name, to choose the one to assign (server-side search, at most 20). */
export function useClientSearch(tenant: string, search: string, enabled: boolean) {
  return useQuery({
    queryKey: queryKey(tenant, "directory", "clients", { entity: "search", search }),
    queryFn: async () =>
      unwrap(
        await api().GET("/api/v1/clients", {
          params: {
            query: {
              view: "all",
              "filter[fullName]": search || undefined,
              page: 1,
              pageSize: 20,
            },
          },
        }),
      ),
    enabled,
    placeholderData: keepPreviousData,
  });
}

/** A change of an employee (or of its clients) that refreshes every employee and client query once settled. */
export function useEmployeeMutation<TInput, TOutput>(
  tenant: string,
  run: (input: TInput) => Promise<TOutput>,
) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: run,
    onSettled: async () => {
      await Promise.all([
        client.invalidateQueries({ queryKey: employeesKey(tenant) }),
        client.invalidateQueries({ queryKey: clientsKey(tenant) }),
      ]);
    },
  });
}

export async function createEmployee(body: CreateEmployee) {
  return unwrap(await api().POST("/api/v1/employees", { body }));
}

export async function updateEmployee(id: string, body: UpdateEmployee) {
  return unwrap(await api().PUT("/api/v1/employees/{id}", { params: { path: { id } }, body }));
}

export async function deleteEmployee(id: string) {
  await unwrap(await api().DELETE("/api/v1/employees/{id}", { params: { path: { id } } }));
}

export async function setEmployeeSignIn(id: string, canSignIn: boolean) {
  return unwrap(
    await api().PUT("/api/v1/employees/{id}/sign-in", {
      params: { path: { id } },
      body: { canSignIn },
    }),
  );
}

export async function makeDefaultEmployee(id: string) {
  return unwrap(await api().PUT("/api/v1/employees/{id}/default", { params: { path: { id } } }));
}

export async function setEmployeeSpecializations(id: string, specializationIds: readonly string[]) {
  return unwrap(
    await api().PUT("/api/v1/employees/{id}/specializations", {
      params: { path: { id } },
      body: { specializationIds: [...specializationIds] },
    }),
  );
}

export async function setEmployeeAdministrator(id: string, administratorUserId: string | null) {
  return unwrap(
    administratorUserId
      ? await api().PUT("/api/v1/employees/{id}/administrator", {
          params: { path: { id } },
          body: { administratorUserId },
        })
      : await api().DELETE("/api/v1/employees/{id}/administrator", { params: { path: { id } } }),
  );
}

export async function sendEmployeeInvitation(id: string) {
  return unwrap(
    await api().POST("/api/v1/employees/{id}/invitation", { params: { path: { id } } }),
  );
}

export async function resetEmployeePassword(id: string, sendLink: boolean) {
  return unwrap(
    await api().POST("/api/v1/employees/{id}/password-reset", {
      params: { path: { id } },
      body: { sendLink },
    }),
  );
}

export async function assignClient(clientId: string, employeeUserId: string) {
  return unwrap(
    await api().PUT("/api/v1/clients/{id}/employee", {
      params: { path: { id: clientId } },
      body: { employeeUserId },
    }),
  );
}

export async function unassignClient(clientId: string) {
  return unwrap(
    await api().DELETE("/api/v1/clients/{id}/employee", { params: { path: { id: clientId } } }),
  );
}

/** "Mario Rossi" style full name used in lists and headers. */
export function employeeName(employee: Pick<EmployeeListItem, "firstName" | "lastName">): string {
  return `${employee.firstName} ${employee.lastName}`.trim();
}
