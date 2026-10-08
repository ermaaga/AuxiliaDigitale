import { encode } from "uqr";

/**
 * A QR code drawn as SVG by React (`uqr`, no markup injection): one path with a square per dark module, inside the
 * 4-module quiet zone of the standard. Always black on white, also in the dark theme: many scanners do not read
 * inverted codes. Used for the `otpauth://` URI of the authenticator enrolment (N02, D-22).
 */
export function QrCode({
  value,
  label,
  size = 192,
}: {
  value: string;
  label: string;
  size?: number;
}) {
  const qr = encode(value, { ecc: "M", border: 4 });
  const path = qr.data
    .flatMap((row, y) => row.map((dark, x) => (dark ? `M${x} ${y}h1v1h-1z` : "")))
    .join("");

  return (
    <svg
      role="img"
      aria-label={label}
      width={size}
      height={size}
      viewBox={`0 0 ${qr.size} ${qr.size}`}
      shapeRendering="crispEdges"
      className="rounded-md bg-white text-black"
      data-testid="qr-code"
    >
      <path d={path} fill="currentColor" />
    </svg>
  );
}
