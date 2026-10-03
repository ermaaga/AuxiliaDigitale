/** Permissions of the directory module the client pages use (`DirectoryPermissions` in the API, F22). */
export const DIRECTORY_PERMISSIONS = {
  viewClients: "directory.clients.view",
  manageClients: "directory.clients.manage",
  assignClients: "directory.clients.assign",
  deleteClients: "directory.clients.delete",
  resetClientPasswords: "directory.clients.credentials",
} as const;
