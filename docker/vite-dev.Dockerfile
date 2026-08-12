# Development Dockerfile for Vite dev server only
FROM node:22-alpine

# Match the lockfile's pnpm release instead of inheriting the latest major.
RUN npm install -g pnpm@10.28.2

WORKDIR /workspace/src/client

COPY --chmod=755 docker/run-vite-dev.sh /usr/local/bin/run-vite-dev

EXPOSE 5279

CMD ["run-vite-dev"]
