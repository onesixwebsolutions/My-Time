# syntax=docker/dockerfile:1

# ---------------------------------------------------------------------------
# Build stage
# ---------------------------------------------------------------------------
FROM node:20-alpine AS build
WORKDIR /app

COPY package.json ./
RUN npm install

COPY . .
RUN npm run build -- --configuration production

# ---------------------------------------------------------------------------
# Runtime stage — served as static files via nginx, with /api and /hubs
# reverse-proxied to the api service (compose network hostname "api").
# ---------------------------------------------------------------------------
FROM nginx:alpine AS runtime
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/daygrid-web/browser /usr/share/nginx/html
EXPOSE 80
