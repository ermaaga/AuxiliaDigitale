import { describe, expect, it } from "vitest";

import { userImageUrl } from "@/components/user-avatar";

import { IMAGE_MAX_BYTES, imageProblem, toApiTheme, toThemeName } from "./api";
import { passwordSchema, profileSchema } from "./schemas";

describe("profile helpers", () => {
  it("maps the API themes (Q35) to next-themes and back", () => {
    expect(toThemeName("Dark")).toBe("dark");
    expect(toThemeName("Light")).toBe("light");
    expect(toThemeName("System")).toBe("system");
    expect(toThemeName("Pink")).toBe("system");
    expect(toApiTheme("dark")).toBe("Dark");
    expect(toApiTheme("sepia")).toBeUndefined();
  });

  it("refuses pictures the API would refuse, before uploading them", () => {
    expect(imageProblem({ type: "image/png", size: 1000 })).toBeUndefined();
    expect(imageProblem({ type: "image/webp", size: IMAGE_MAX_BYTES })).toBeUndefined();
    expect(imageProblem({ type: "image/gif", size: 1000 })).toBe("app.profile.imageType");
    expect(imageProblem({ type: "image/jpeg", size: IMAGE_MAX_BYTES + 1 })).toBe(
      "app.profile.imageTooLarge",
    );
  });

  it("builds the picture URL through the BFF only when there is a picture", () => {
    expect(userImageUrl("u1", "abc")).toBe("/api/bff/users/u1/image?v=abc");
    expect(userImageUrl("u1", null)).toBeUndefined();
    expect(userImageUrl("u1", undefined)).toBeUndefined();
  });
});

describe("profile schemas", () => {
  it("requires the e-mail and allows an empty phone", () => {
    expect(
      profileSchema.safeParse({
        firstName: "Maria",
        lastName: "Bianchi",
        email: "maria@example.test",
        phone: "",
      }).success,
    ).toBe(true);
    const result = profileSchema.safeParse({
      firstName: "",
      lastName: "B",
      email: "",
      phone: "12",
    });
    expect(result.success).toBe(false);
    expect(result.error?.issues.map((issue) => issue.path[0])).toEqual([
      "firstName",
      "email",
      "phone",
    ]);
  });

  it("asks for the new password twice", () => {
    const mismatch = passwordSchema.safeParse({
      currentPassword: "old",
      newPassword: "New-Password-1",
      confirmPassword: "New-Password-2",
    });
    expect(mismatch.error?.issues).toEqual([
      expect.objectContaining({ path: ["confirmPassword"], message: "app.auth.passwordMismatch" }),
    ]);
    expect(
      passwordSchema.safeParse({
        currentPassword: "old",
        newPassword: "New-Password-1",
        confirmPassword: "New-Password-1",
      }).success,
    ).toBe(true);
  });
});
