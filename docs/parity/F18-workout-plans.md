# F18 — Workout plans (Training) — REMOVED

Status: [x] removed by decision D-09 (2026-09-29) · Quirks: Q25

The product manages **clients, cases, appointments and marketing campaigns**; the gym-related code in the legacy was dead code (backend only, no page). It is **not** ported and must not be reintroduced.

## What is dropped (legacy evidence)
- Entity `WorkoutPlan` and `User.WorkoutPlans`; `IWorkoutPlanService`/`WorkoutPlanService`; `ExerciseDto`.
- `DocumentService.GenerateWorkoutPlanPdf`, `EmailService.SendWorkoutPlanEmailAsync`.
- Notification type `WorkoutPlan` and its header route `/client/workoutplans`; client dashboard card "Active workout plans"; `PageConfiguration` rows `WorkoutPlans` (Employee, Client); translation keys about workouts/trainer.
- Gym wording elsewhere is renamed, not dropped: "Trainer" → "Operator" (appointments, F13), "Ask my personal trainer" → "Ask my operator" (requests, F15), "Approvato dal trainer" → processed by operator (F03).

## Acceptance criteria
- [ ] No `training` schema, module, permission, route, navigation entry or translation key exists in the new platform.
- [ ] Legacy import: `WorkoutPlans` rows are not migrated; the dry-run report lists their count so the user can confirm nothing of value is lost.
