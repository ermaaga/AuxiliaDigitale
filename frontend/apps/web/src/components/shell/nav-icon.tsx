import {
  Building2Icon,
  BriefcaseIcon,
  CalendarIcon,
  CircleIcon,
  FolderIcon,
  GaugeIcon,
  LayersIcon,
  LayoutDashboardIcon,
  MailIcon,
  MegaphoneIcon,
  MessageSquareIcon,
  MonitorSmartphoneIcon,
  PaletteIcon,
  SettingsIcon,
  ShieldCheckIcon,
  SlidersHorizontalIcon,
  Table2Icon,
  UserCogIcon,
  UsersIcon,
  type LucideIcon,
} from "lucide-react";

/** Icons named by the module descriptors (`NavigationEntry.Icon`) and the console; unknown names get a neutral dot. */
const icons: Record<string, LucideIcon> = {
  briefcase: BriefcaseIcon,
  building: Building2Icon,
  calendar: CalendarIcon,
  folder: FolderIcon,
  gauge: GaugeIcon,
  layers: LayersIcon,
  "layout-dashboard": LayoutDashboardIcon,
  mail: MailIcon,
  megaphone: MegaphoneIcon,
  "message-square": MessageSquareIcon,
  "monitor-smartphone": MonitorSmartphoneIcon,
  palette: PaletteIcon,
  settings: SettingsIcon,
  "shield-check": ShieldCheckIcon,
  sliders: SlidersHorizontalIcon,
  table: Table2Icon,
  "user-cog": UserCogIcon,
  users: UsersIcon,
};

export function NavIcon({ name }: { name: string }) {
  const Icon = icons[name] ?? CircleIcon;
  return <Icon aria-hidden className="size-4" />;
}
