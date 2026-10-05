import type { SegmentCondition, SegmentRule } from "./api";

/** A condition as the builder edits it: every value as text until it is sent. */
export type ConditionDraft = { field: string; op: string; value: string; key: string };

export type GroupDraft = { match: "all" | "any"; conditions: ConditionDraft[] };

export type RuleDraft = {
  match: "all" | "any";
  conditions: ConditionDraft[];
  groups: GroupDraft[];
};

/** How the builder asks for the value of each field. */
export const VALUE_KINDS: Record<
  string,
  | "status"
  | "employee"
  | "tag"
  | "specialization"
  | "service"
  | "caseStatus"
  | "number"
  | "date"
  | "custom"
> = {
  status: "status",
  employee: "employee",
  tag: "tag",
  specialization: "specialization",
  service: "service",
  caseStatus: "caseStatus",
  age: "number",
  createdOn: "date",
  customField: "custom",
};

export const CLIENT_STATUSES = ["Active", "Inactive"] as const;
export const CASE_STATUSES = ["Inserted", "InProgress", "Sent", "Completed"] as const;

export const emptyCondition = (field = "status", op = "is"): ConditionDraft => ({
  field,
  op,
  value: "",
  key: "",
});

export const emptyRule = (): RuleDraft => ({
  match: "all",
  conditions: [emptyCondition()],
  groups: [],
});

/** A condition without a value is incomplete, except "employee: none". */
export const isComplete = (condition: ConditionDraft) =>
  condition.op === "none" ||
  (condition.value.trim() !== "" &&
    (condition.field !== "customField" || condition.key.trim() !== ""));

/** A custom field value as JSON: booleans and numbers as such, everything else as text. */
export function customValue(text: string): string | number | boolean {
  const trimmed = text.trim();
  if (trimmed === "true" || trimmed === "false") {
    return trimmed === "true";
  }

  return trimmed !== "" && !Number.isNaN(Number(trimmed)) ? Number(trimmed) : trimmed;
}

function toCondition(condition: ConditionDraft): SegmentCondition {
  const value =
    condition.op === "none"
      ? null
      : condition.field === "age"
        ? Number(condition.value)
        : condition.field === "customField"
          ? customValue(condition.value)
          : condition.value.trim();
  return {
    field: condition.field,
    op: condition.op,
    value: value as never,
    key: condition.field === "customField" ? condition.key.trim() : null,
  };
}

/** The rule for the API, or `undefined` while a condition is incomplete or there is none. */
export function toRule(draft: RuleDraft): SegmentRule | undefined {
  const all = [...draft.conditions, ...draft.groups.flatMap((group) => group.conditions)];
  if (
    all.length === 0 ||
    !all.every(isComplete) ||
    draft.groups.some((group) => group.conditions.length === 0)
  ) {
    return undefined;
  }

  return {
    match: draft.match,
    conditions: draft.conditions.map(toCondition),
    groups: draft.groups.map((group) => ({
      match: group.match,
      conditions: group.conditions.map(toCondition),
    })),
  };
}

function fromCondition(condition: SegmentCondition): ConditionDraft {
  const value = condition.value as unknown;
  return {
    field: condition.field,
    op: condition.op,
    value: value === null || value === undefined ? "" : String(value),
    key: condition.key ?? "",
  };
}

/** The draft of a saved rule. */
export function fromRule(rule: SegmentRule): RuleDraft {
  return {
    match: rule.match === "any" ? "any" : "all",
    conditions: rule.conditions.map(fromCondition),
    groups: (rule.groups ?? []).map((group) => ({
      match: group.match === "any" ? "any" : "all",
      conditions: group.conditions.map(fromCondition),
    })),
  };
}
