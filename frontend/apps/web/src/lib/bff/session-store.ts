import type { BffSession } from "./session";

/**
 * Server-side store of BFF sessions (skill auxilia-security: `bff:sess:{sid}`). The in-memory store serves a single
 * Next.js instance (development, single node); several instances need a shared store (Redis) behind this interface.
 */
export interface SessionStore {
  get(id: string): Promise<BffSession | undefined>;
  set(session: BffSession, ttlSeconds: number): Promise<void>;
  delete(id: string): Promise<void>;
}

export class InMemorySessionStore implements SessionStore {
  private readonly entries = new Map<string, { session: BffSession; expiresAt: number }>();

  constructor(private readonly now: () => number = Date.now) {}

  async get(id: string): Promise<BffSession | undefined> {
    const entry = this.entries.get(id);
    if (entry === undefined) {
      return undefined;
    }

    if (entry.expiresAt <= this.now()) {
      this.entries.delete(id);
      return undefined;
    }

    return structuredClone(entry.session);
  }

  async set(session: BffSession, ttlSeconds: number): Promise<void> {
    this.sweep();
    this.entries.set(session.id, {
      session: structuredClone(session),
      expiresAt: this.now() + ttlSeconds * 1000,
    });
  }

  async delete(id: string): Promise<void> {
    this.entries.delete(id);
  }

  private sweep() {
    const now = this.now();
    for (const [id, entry] of this.entries) {
      if (entry.expiresAt <= now) {
        this.entries.delete(id);
      }
    }
  }
}

const globalStore = globalThis as typeof globalThis & { __auxiliaSessionStore?: SessionStore };

/** One store per server process (kept on globalThis so development hot reloads do not sign everybody out). */
export function sessionStore(): SessionStore {
  globalStore.__auxiliaSessionStore ??= new InMemorySessionStore();
  return globalStore.__auxiliaSessionStore;
}
