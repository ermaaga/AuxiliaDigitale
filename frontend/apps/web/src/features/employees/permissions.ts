/** Permissions of the directory module the employee pages use (`DirectoryPermissions` in the API, F06, F22). */
export const EMPLOYEE_PERMISSIONS = {
  view: "directory.employees.view",
  manage: "directory.employees.manage",
  /** Assigning a client to the employee (the client side of the assignment, B-01). */
  assignClients: "directory.clients.assign",
  viewClients: "directory.clients.view",
} as const;
