import { createHmac } from "node:crypto";

const ALPHABET = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
const STEP_MS = 30_000;

function base32(text: string): Buffer {
  let bits = "";
  for (const char of text.replace(/[\s=]/g, "").toUpperCase()) {
    const value = ALPHABET.indexOf(char);
    if (value < 0) {
      throw new Error(`Not base32: ${char}`);
    }

    bits += value.toString(2).padStart(5, "0");
  }

  const bytes: number[] = [];
  for (let i = 0; i + 8 <= bits.length; i += 8) {
    bytes.push(Number.parseInt(bits.slice(i, i + 8), 2));
  }

  return Buffer.from(bytes);
}

/** RFC 6238 code (SHA-1, 6 digits, 30 s) of the authenticator secret, as an authenticator app shows it. */
export function totp(secret: string, at = Date.now()): string {
  const counter = Buffer.alloc(8);
  counter.writeBigUInt64BE(BigInt(Math.floor(at / STEP_MS)));
  const hash = createHmac("sha1", base32(secret)).update(counter).digest();
  const offset = hash[hash.length - 1]! & 15;
  return String((hash.readUInt32BE(offset) & 0x7fffffff) % 1_000_000).padStart(6, "0");
}

/**
 * Waits for the next 30-second step: the API refuses a code of a step already used (replay protection), so a
 * sign-in right after the activation needs a new code.
 */
export async function nextTotpStep(): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, STEP_MS - (Date.now() % STEP_MS) + 500));
}
