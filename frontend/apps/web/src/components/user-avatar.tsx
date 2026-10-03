"use client";

import { Avatar, AvatarFallback, AvatarImage } from "@auxilia/ui/components/avatar";
import { cn } from "@auxilia/ui/lib/utils";

import { initials } from "@/components/shell/shell-frame";

/**
 * The profile picture of a user through the tenant BFF (`GET /users/{id}/image?v=`, F04): the version (picture hash)
 * makes the URL change with the picture, so the browser may cache it. `undefined` without a picture.
 */
export function userImageUrl(userId: string, imageVersion: string | null | undefined) {
  return imageVersion
    ? `/api/bff/users/${encodeURIComponent(userId)}/image?v=${encodeURIComponent(imageVersion)}`
    : undefined;
}

/** A user's picture, or the initials of the name while it loads or when there is none (F04, legacy grids and headers). */
export function UserAvatar({
  userId,
  name,
  imageVersion,
  size = "default",
  className,
}: {
  userId: string;
  name: string;
  imageVersion: string | null | undefined;
  size?: "default" | "sm" | "lg";
  className?: string;
}) {
  const src = userImageUrl(userId, imageVersion);
  return (
    <Avatar size={size} className={cn(className)}>
      {src ? <AvatarImage src={src} alt="" /> : null}
      <AvatarFallback aria-hidden>{initials(name)}</AvatarFallback>
    </Avatar>
  );
}
