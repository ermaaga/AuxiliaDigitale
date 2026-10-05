"use client";

import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { unwrap, type components } from "@auxilia/api-client";

import { createBffClient } from "@/lib/api/client";
import { queryKey } from "@/lib/api/query-keys";

export type SegmentRule = components["schemas"]["SegmentRuleRequest"];
export type SegmentCondition = components["schemas"]["SegmentConditionRequest"];
export type SegmentGroup = components["schemas"]["SegmentGroupRequest"];
export type SegmentField = components["schemas"]["SegmentFieldResponse"];
export type Segment = components["schemas"]["SegmentResponse"];
export type SegmentListItem = components["schemas"]["SegmentListItemResponse"];
export type StaticList = components["schemas"]["StaticListResponse"];
export type AudienceMember = components["schemas"]["AudienceMemberResponse"];
export type EmailTemplate = components["schemas"]["EmailTemplateResponse"];
export type EmailTemplateListItem = components["schemas"]["EmailTemplateListItemResponse"];
export type Campaign = components["schemas"]["CampaignResponse"];
export type CampaignRecipient = components["schemas"]["CampaignRecipientResponse"];
export type Suppression = components["schemas"]["SuppressionResponse"];

export const CAMPAIGN_STATUSES = ["Draft", "Sending", "Sent", "Cancelled", "Failed"] as const;
export const RECIPIENT_STATUSES = ["Pending", "Sent", "Failed", "Excluded"] as const;

const api = () => createBffClient("tenant");

/** Everything about marketing lives under `marketingKey(tenant)`: a change invalidates it as a whole. */
export const marketingKey = (tenant: string, entity?: string, params?: Record<string, unknown>) =>
  entity === undefined
    ? queryKey(tenant, "marketing")
    : queryKey(tenant, "marketing", entity, params);

export function useMarketingMutation<TInput, TOutput>(
  tenant: string,
  run: (input: TInput) => Promise<TOutput>,
) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: run,
    onSettled: () => client.invalidateQueries({ queryKey: marketingKey(tenant) }),
  });
}

export function useSegmentFields(tenant: string) {
  return useQuery({
    queryKey: marketingKey(tenant, "segment-fields"),
    queryFn: async () => unwrap(await api().GET("/api/v1/marketing/segments/fields")),
    staleTime: Number.POSITIVE_INFINITY,
  });
}

export function useSegments(tenant: string) {
  return useQuery({
    queryKey: marketingKey(tenant, "segments"),
    queryFn: async () => unwrap(await api().GET("/api/v1/marketing/segments")),
  });
}

export function useSegment(tenant: string, id: string | undefined) {
  return useQuery({
    queryKey: marketingKey(tenant, "segment", { id }),
    queryFn: async () =>
      unwrap(await api().GET("/api/v1/marketing/segments/{id}", { params: { path: { id: id! } } })),
    enabled: id !== undefined,
    retry: false,
  });
}

/** The live count of a rule (debounced by the caller); disabled while the rule is incomplete. */
export function useSegmentPreview(tenant: string, rule: SegmentRule | undefined) {
  return useQuery({
    queryKey: marketingKey(tenant, "segment-preview", { rule }),
    queryFn: async () =>
      unwrap(await api().POST("/api/v1/marketing/segments/preview", { body: rule! })),
    enabled: rule !== undefined,
    placeholderData: keepPreviousData,
    retry: false,
  });
}

export function useSegmentMembers(tenant: string, id: string, page: number, pageSize: number) {
  return useQuery({
    queryKey: marketingKey(tenant, "segment-members", { id, page, pageSize }),
    queryFn: async () =>
      unwrap(
        await api().GET("/api/v1/marketing/segments/{id}/members", {
          params: { path: { id }, query: { page, pageSize } },
        }),
      ),
    placeholderData: keepPreviousData,
  });
}

export function useStaticLists(tenant: string, enabled = true) {
  return useQuery({
    queryKey: marketingKey(tenant, "lists"),
    queryFn: async () => unwrap(await api().GET("/api/v1/marketing/lists")),
    enabled,
  });
}

export function useStaticList(tenant: string, id: string) {
  return useQuery({
    queryKey: marketingKey(tenant, "list", { id }),
    queryFn: async () =>
      unwrap(await api().GET("/api/v1/marketing/lists/{id}", { params: { path: { id } } })),
    retry: false,
  });
}

export function useListMembers(tenant: string, id: string, page: number, pageSize: number) {
  return useQuery({
    queryKey: marketingKey(tenant, "list-members", { id, page, pageSize }),
    queryFn: async () =>
      unwrap(
        await api().GET("/api/v1/marketing/lists/{id}/members", {
          params: { path: { id }, query: { page, pageSize } },
        }),
      ),
    placeholderData: keepPreviousData,
  });
}

export function useEmailTemplates(tenant: string) {
  return useQuery({
    queryKey: marketingKey(tenant, "templates"),
    queryFn: async () => unwrap(await api().GET("/api/v1/marketing/templates")),
  });
}

export function useEmailTemplate(tenant: string, id: string | undefined) {
  return useQuery({
    queryKey: marketingKey(tenant, "template", { id }),
    queryFn: async () =>
      unwrap(
        await api().GET("/api/v1/marketing/templates/{id}", { params: { path: { id: id! } } }),
      ),
    enabled: id !== undefined,
    retry: false,
  });
}

export function useTemplatePreview(tenant: string, id: string | undefined, version: number) {
  return useQuery({
    queryKey: marketingKey(tenant, "template-preview", { id, version }),
    queryFn: async () =>
      unwrap(
        await api().POST("/api/v1/marketing/templates/{id}/preview", {
          params: { path: { id: id! } },
          body: { clientId: null },
        }),
      ),
    enabled: id !== undefined,
    retry: false,
  });
}

export function useCampaigns(tenant: string, page: number, pageSize: number) {
  return useQuery({
    queryKey: marketingKey(tenant, "campaigns", { page, pageSize }),
    queryFn: async () =>
      unwrap(
        await api().GET("/api/v1/marketing/campaigns", { params: { query: { page, pageSize } } }),
      ),
    placeholderData: keepPreviousData,
    refetchInterval: (query) =>
      query.state.data?.items.some((campaign) => campaign.status === "Sending") ? 3000 : false,
  });
}

export function useCampaign(tenant: string, id: string) {
  return useQuery({
    queryKey: marketingKey(tenant, "campaign", { id }),
    queryFn: async () =>
      unwrap(await api().GET("/api/v1/marketing/campaigns/{id}", { params: { path: { id } } })),
    retry: false,
    refetchInterval: (query) => (query.state.data?.status === "Sending" ? 3000 : false),
  });
}

export function useCampaignRecipients(
  tenant: string,
  id: string,
  status: string | undefined,
  page: number,
  pageSize: number,
  enabled: boolean,
) {
  return useQuery({
    queryKey: marketingKey(tenant, "campaign-recipients", { id, status, page, pageSize }),
    queryFn: async () =>
      unwrap(
        await api().GET("/api/v1/marketing/campaigns/{id}/recipients", {
          params: { path: { id }, query: { status, page, pageSize } },
        }),
      ),
    enabled,
    placeholderData: keepPreviousData,
  });
}

export function useSuppressions(tenant: string) {
  return useQuery({
    queryKey: marketingKey(tenant, "suppressions"),
    queryFn: async () => unwrap(await api().GET("/api/v1/marketing/suppressions")),
  });
}

export async function saveSegment(
  id: string | undefined,
  body: components["schemas"]["SaveSegmentRequest"],
) {
  if (id) {
    await unwrap(
      await api().PUT("/api/v1/marketing/segments/{id}", { params: { path: { id } }, body }),
    );
    return id;
  }

  return unwrap(await api().POST("/api/v1/marketing/segments", { body })).id;
}

export async function deleteSegment(id: string) {
  await unwrap(await api().DELETE("/api/v1/marketing/segments/{id}", { params: { path: { id } } }));
}

export async function saveList(id: string | undefined, name: string, description: string | null) {
  const body = { name, description };
  if (id) {
    await unwrap(
      await api().PUT("/api/v1/marketing/lists/{id}", { params: { path: { id } }, body }),
    );
    return id;
  }

  return unwrap(await api().POST("/api/v1/marketing/lists", { body })).id;
}

export async function deleteList(id: string) {
  await unwrap(await api().DELETE("/api/v1/marketing/lists/{id}", { params: { path: { id } } }));
}

export async function addListMembers(id: string, clientIds: readonly string[]) {
  return unwrap(
    await api().POST("/api/v1/marketing/lists/{id}/members", {
      params: { path: { id } },
      body: { clientIds: [...clientIds] },
    }),
  );
}

export async function removeListMembers(id: string, clientIds: readonly string[]) {
  return unwrap(
    await api().POST("/api/v1/marketing/lists/{id}/members/remove", {
      params: { path: { id } },
      body: { clientIds: [...clientIds] },
    }),
  );
}

export async function saveTemplate(
  id: string | undefined,
  body: components["schemas"]["SaveEmailTemplateRequest"],
) {
  if (id) {
    await unwrap(
      await api().PUT("/api/v1/marketing/templates/{id}", { params: { path: { id } }, body }),
    );
    return id;
  }

  return unwrap(await api().POST("/api/v1/marketing/templates", { body })).id;
}

export async function deleteTemplate(id: string) {
  await unwrap(
    await api().DELETE("/api/v1/marketing/templates/{id}", { params: { path: { id } } }),
  );
}

export async function testTemplate(id: string, email: string) {
  await unwrap(
    await api().POST("/api/v1/marketing/templates/{id}/test", {
      params: { path: { id } },
      body: { email },
    }),
  );
}

export async function createCampaign(body: components["schemas"]["SaveCampaignRequest"]) {
  return unwrap(await api().POST("/api/v1/marketing/campaigns", { body })).id;
}

export async function campaignAudience(id: string) {
  return unwrap(
    await api().GET("/api/v1/marketing/campaigns/{id}/audience", { params: { path: { id } } }),
  ).count;
}

export async function sendCampaign(id: string) {
  await unwrap(
    await api().POST("/api/v1/marketing/campaigns/{id}/send", { params: { path: { id } } }),
  );
}

export async function cancelCampaign(id: string) {
  await unwrap(
    await api().POST("/api/v1/marketing/campaigns/{id}/cancel", { params: { path: { id } } }),
  );
}

export async function deleteCampaign(id: string) {
  await unwrap(
    await api().DELETE("/api/v1/marketing/campaigns/{id}", { params: { path: { id } } }),
  );
}

export async function addSuppression(email: string, reason: string | null) {
  return unwrap(await api().POST("/api/v1/marketing/suppressions", { body: { email, reason } }));
}

export async function removeSuppression(id: string) {
  await unwrap(
    await api().DELETE("/api/v1/marketing/suppressions/{id}", { params: { path: { id } } }),
  );
}
