/** Permissions of the tasks and the client timeline (`EngagementPermissions` in the API, B-26). */
export const TASKS_PERMISSIONS = {
  view: "engagement.tasks.view",
  manage: "engagement.tasks.manage",
  viewActivities: "engagement.activities.view",
  manageActivities: "engagement.activities.manage",
} as const;
