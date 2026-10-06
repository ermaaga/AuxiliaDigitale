/** Permissions of the marketing module (`MarketingPermissions` in the API, N01). */
export const MARKETING_PERMISSIONS = {
  viewCampaigns: "marketing.campaigns.view",
  manageCampaigns: "marketing.campaigns.manage",
  sendCampaigns: "marketing.campaigns.send",
  viewAudiences: "marketing.audiences.view",
  manageAudiences: "marketing.audiences.manage",
} as const;
