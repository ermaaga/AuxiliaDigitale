"use client";

import * as React from "react";
import { useRouter } from "next/navigation";
import { ImageUpIcon, Trash2Icon } from "lucide-react";
import { useTranslations } from "next-intl";
import { Button } from "@auxilia/ui/components/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@auxilia/ui/components/card";

import { useConfirm } from "@/components/confirm/confirm-provider";
import { UserAvatar } from "@/components/user-avatar";
import { useNotify } from "@/lib/notify";

import {
  deleteImage,
  IMAGE_TYPES,
  imageProblem,
  uploadImage,
  useProfileMutation,
  type Profile,
} from "../api";

/**
 * The own profile picture (F04): JPEG, PNG or WebP up to 2 MB, resized by the server to at most 400 × 400; removal asks
 * for confirmation. The header picks the new picture up at once (the layout is rendered again).
 */
export function ProfilePicture({ tenant, profile }: { tenant: string; profile: Profile }) {
  const t = useTranslations();
  const router = useRouter();
  const notify = useNotify();
  const confirm = useConfirm();
  const input = React.useRef<HTMLInputElement>(null);
  const [problem, setProblem] = React.useState<string>();
  const upload = useProfileMutation(tenant, uploadImage);
  const remove = useProfileMutation(tenant, deleteImage);
  const busy = upload.isPending || remove.isPending;
  const name = `${profile.firstName} ${profile.lastName}`.trim() || profile.userName;

  const onFile = async (event: React.ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    event.target.value = "";
    if (!file) {
      return;
    }

    const invalid = imageProblem(file);
    setProblem(invalid);
    if (invalid) {
      return;
    }

    try {
      await upload.mutateAsync(file);
      notify.success("app.profile.imageSaved");
      router.refresh();
    } catch (error) {
      notify.error(error);
    }
  };

  const onRemove = async () => {
    const confirmed = await confirm({
      description: t("app.profile.imageDeleteConfirm"),
      confirmLabel: t("Delete"),
      variant: "destructive",
    });
    if (!confirmed) {
      return;
    }

    try {
      await remove.mutateAsync(undefined);
      notify.success("app.profile.imageDeleted");
      router.refresh();
    } catch (error) {
      notify.error(error);
    }
  };

  return (
    <Card>
      <CardHeader>
        <CardTitle>
          <h2 className="text-base font-semibold">{t("app.profile.picture")}</h2>
        </CardTitle>
        <CardDescription id="profile-picture-hint">{t("app.profile.pictureHint")}</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-wrap items-center gap-4">
        <UserAvatar
          userId={profile.userId}
          name={name}
          imageVersion={profile.imageVersion}
          className="size-20 text-xl"
        />
        <div className="flex flex-col gap-2">
          <input
            ref={input}
            id="profile-picture-file"
            type="file"
            accept={IMAGE_TYPES.join(",")}
            className="sr-only"
            tabIndex={-1}
            aria-hidden
            onChange={(event) => void onFile(event)}
          />
          <div className="flex flex-wrap gap-2">
            <Button
              type="button"
              variant="outline"
              disabled={busy}
              aria-describedby="profile-picture-hint"
              onClick={() => input.current?.click()}
            >
              <ImageUpIcon aria-hidden /> {t("UploadImage")}
            </Button>
            {profile.imageVersion ? (
              <Button
                type="button"
                variant="outline"
                disabled={busy}
                onClick={() => void onRemove()}
              >
                <Trash2Icon aria-hidden /> {t("app.profile.imageDelete")}
              </Button>
            ) : null}
          </div>
          {problem ? (
            <p role="alert" className="text-sm text-destructive">
              {t(problem)}
            </p>
          ) : null}
        </div>
      </CardContent>
    </Card>
  );
}
