# syntax=docker/dockerfile:1
# Image of the Next.js tenant app and console with its BFF (apps/web), for deploy/compose.local.yml.
#   docker build -f frontend/Dockerfile frontend
# The BFF configuration is read at run time (AUXILIA_* variables); production hardening comes with H-03.

FROM node:24-alpine
ENV NEXT_TELEMETRY_DISABLED=1 \
    COREPACK_ENABLE_DOWNLOAD_PROMPT=0
RUN corepack enable
WORKDIR /app
COPY . .
RUN pnpm install --frozen-lockfile && pnpm --filter web build
ENV NODE_ENV=production
EXPOSE 3000
# The client application secrets are written by the bootstrap of the local stack to /shared/web.env.
CMD ["sh", "-c", "if [ -f /shared/web.env ]; then set -a; . /shared/web.env; set +a; fi; exec pnpm --filter web start --port 3000 --hostname 0.0.0.0"]
