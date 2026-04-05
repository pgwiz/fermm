# Stage 1: Build dashboard
FROM node:20-alpine AS dashboard-build

WORKDIR /dashboard
COPY fermm-dashboard/package*.json ./
RUN npm install
COPY fermm-dashboard/ ./
RUN npm run build

# Stage 2: Runtime - Alpine (lightweight)
FROM python:3.12-alpine AS runtime-alpine

WORKDIR /app

RUN apk add --no-cache gcc musl-dev postgresql-dev

COPY fermm-server/requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY fermm-server/ .

# Copy built dashboard
COPY --from=dashboard-build /dashboard/dist /app/../fermm-dashboard/dist

EXPOSE 8000

CMD ["uvicorn", "main:app", "--host", "0.0.0.0", "--port", "8000"]

# Stage 3: Runtime - Full Python (alternative for Windows Server compatibility)
FROM python:3.12-slim AS runtime-full

WORKDIR /app

COPY fermm-server/requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY fermm-server/ .

# Copy built dashboard
COPY --from=dashboard-build /dashboard/dist /app/../fermm-dashboard/dist

EXPOSE 8000

CMD ["uvicorn", "main:app", "--host", "0.0.0.0", "--port", "8000"]

# Default to Alpine for final output
FROM runtime-alpine
