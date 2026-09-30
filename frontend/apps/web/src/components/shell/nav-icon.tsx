import {
  BriefcaseIcon,
  CalendarIcon,
  CircleIcon,
  FolderIcon,
  LayersIcon,
  LayoutDashboardIcon,
  MegaphoneIcon,
  MessageSquareIcon,
  MonitorSmartphoneIcon,
  ShieldCheckIcon,
  UserCogIcon,
  UsersIcon,
  type LucideIcon,
} from "lucide-react";

/** Icons named by the module descriptors (`NavigationEntry.Icon`); unknown names get a neutral dot. */
const icons: Record<string, LucideIcon> = {
  briefcase: BriefcaseIcon,
  calendar: CalendarIcon,
  folder: FolderIcon,
  layers: LayersIcon,
  "layout-dashboard": LayoutDashboardIcon,
  megaphone: MegaphoneIcon,
  "message-square": MessageSquareIcon,
  "monitor-smartphone": MonitorSmartphoneIcon,
  "shield-check": ShieldCheckIcon,
  "user-cog": UserCogIcon,
  users: UsersIcon,
};

export function NavIcon({ name }: { name: string }) {
  const Icon = icons[name] ?? CircleIcon;
  return <Icon aria-hidden className="size-4" />;
}
