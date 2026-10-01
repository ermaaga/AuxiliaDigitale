import { renderToStaticMarkup } from "react-dom/server";
import { NextIntlClientProvider } from "next-intl";
import { describe, expect, it } from "vitest";

import type { CustomFieldDefinition } from "@/components/data-table/table-model";

import { CustomFieldCell, badgeStyle, customFieldText } from "./custom-field-value";
import { CustomFieldsEditor } from "./custom-fields-editor";

const field = (
  key: string,
  type: string,
  extra: Partial<CustomFieldDefinition> = {},
): CustomFieldDefinition => ({
  key,
  label: key.toUpperCase(),
  type,
  visibleOnGrid: true,
  order: 0,
  ...extra,
});

describe("custom field values", () => {
  it("shows values as text and true booleans as their label", () => {
    expect(customFieldText(field("caf", "Boolean"), true)).toBe("CAF");
    expect(customFieldText(field("caf", "Boolean"), false)).toBeUndefined();
    expect(customFieldText(field("areas", "MultiSelect"), ["a", "b"])).toBe("a, b");
    expect(customFieldText(field("areas", "MultiSelect"), [])).toBeUndefined();
    expect(customFieldText(field("n", "Number"), 12.5)).toBe("12.5");
    expect(customFieldText(field("t", "Text"), "")).toBeUndefined();
    expect(customFieldText(field("t", "Text"), null)).toBeUndefined();
  });

  it("gives badges a readable text colour, or none for an invalid colour", () => {
    expect(badgeStyle("#72fa29")).toMatchObject({ backgroundColor: "#72fa29", color: "#0a0a0a" });
    expect(badgeStyle("#335cff")).toMatchObject({ backgroundColor: "#335cff", color: "#ffffff" });
    expect(badgeStyle("url(x)")).toBeUndefined();
  });

  it("renders a grouped column as coloured badges, like the legacy grid", () => {
    const html = renderToStaticMarkup(
      <CustomFieldCell
        fields={[
          field("caf", "Boolean", { groupName: "Area", badgeColor: "#72fa29" }),
          field("patronato", "Boolean", { groupName: "Area", badgeColor: "#335cff" }),
          field("note", "Text", { groupName: "Area" }),
        ]}
        values={{ caf: true, patronato: false, note: "ok" }}
      />,
    );
    expect(html).toContain("background-color:#72fa29");
    expect(html).toContain(">CAF<");
    expect(html).not.toContain("PATRONATO");
    expect(html).toContain("NOTE: ok");
    expect(
      renderToStaticMarkup(<CustomFieldCell fields={[field("x", "Text")]} values={{}} />),
    ).toContain("—");
  });
});

describe("custom fields editor", () => {
  it("renders an input per type, required marks and the API errors", () => {
    const html = renderToStaticMarkup(
      <NextIntlClientProvider
        locale="it"
        messages={{ validation: { customFields: { required: "Obbligatorio" } } }}
      >
        <CustomFieldsEditor
          idPrefix="client"
          definitions={[
            field("notes", "Text", { isRequired: true, order: 1 }),
            field("since", "Date", { order: 2 }),
            field("amount", "Number", { order: 3 }),
            field("caf", "Boolean", { order: 4 }),
            field("areas", "MultiSelect", { options: ["a", "b"], order: 5 }),
          ]}
          values={{ since: "2026-01-31", amount: 3 }}
          onChange={() => {}}
          errors={{ "customFields.notes": ["validation.customFields.required"] }}
        />
      </NextIntlClientProvider>,
    );
    expect(html).toContain("NOTES *");
    expect(html).toContain('type="date"');
    expect(html).toContain('value="2026-01-31"');
    expect(html).toContain('type="number"');
    expect(html).toContain("<legend");
    expect(html).toContain('id="client-notes-error"');
    expect(html).toContain("Obbligatorio");
    expect(html).toContain('aria-describedby="client-notes-error"');
  });
});
