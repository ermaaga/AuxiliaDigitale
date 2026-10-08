import { renderToStaticMarkup } from "react-dom/server";
import { encode } from "uqr";
import { describe, expect, it } from "vitest";

import { QrCode } from "./qr-code";

const URI =
  "otpauth://totp/Auxilia:ops%40example.test?secret=JBSWY3DPEHPK3PXP&issuer=Auxilia&algorithm=SHA1&digits=6&period=30";

describe("QR code of the authenticator enrolment", () => {
  it("draws every dark module inside the quiet zone, black on white, with an accessible name", () => {
    const html = renderToStaticMarkup(<QrCode value={URI} label="Codice QR" />);
    const qr = encode(URI, { ecc: "M", border: 4 });
    const dark = qr.data.flat().filter(Boolean).length;

    expect(html).toContain('role="img"');
    expect(html).toContain('aria-label="Codice QR"');
    expect(html).toContain(`viewBox="0 0 ${qr.size} ${qr.size}"`);
    expect(html).toContain("bg-white text-black");
    expect(html.match(/h1v1h-1z/g)).toHaveLength(dark);
    // The quiet zone: the first and last four rows and columns are light.
    for (const row of [...qr.data.slice(0, 4), ...qr.data.slice(-4)]) {
      expect(row.some(Boolean)).toBe(false);
    }
  });
});
