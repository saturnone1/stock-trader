# =============================================================================
# Stage 1: Build
# Restore NuGet packages and compile the application in Release mode.
# Using the full SDK image so we have dotnet-publish available.
# =============================================================================
# Stage 0: Build React frontend
FROM node:22-slim AS frontend
WORKDIR /app/ClientApp
COPY ClientApp/package.json ClientApp/package-lock.json ./
RUN npm ci --include=dev
COPY ClientApp/ .
RUN npx vite build --mode production

# Stage 1: Build .NET backend
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY StockTrader.csproj ./
RUN dotnet restore StockTrader.csproj --locked-mode 2>/dev/null || dotnet restore StockTrader.csproj

COPY . .
# Copy React build output into source tree so it's included in publish
COPY --from=frontend /app/ClientApp/dist ./ClientApp/dist

RUN dotnet publish StockTrader.csproj \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

# =============================================================================
# Stage 2: Runtime
# Minimal ASP.NET Core runtime image — no SDK, significantly smaller.
# =============================================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# ── System dependencies ──────────────────────────────────────────────────────
# libsqlite3-0 : native SQLite library required by Microsoft.Data.Sqlite
# ca-certificates: up-to-date root CAs for outbound HTTPS calls (Alpaca, Yahoo)
# tzdata        : time-zone data for ET (America/New_York) used by DailyReportService
RUN apt-get update && apt-get install -y --no-install-recommends \
    libsqlite3-0 \
    ca-certificates \
    tzdata \
    && rm -rf /var/lib/apt/lists/*

# ── Non-root user for security ───────────────────────────────────────────────
# Running as root inside a container is a security risk.
# Create a dedicated user and group "stocktrader".
RUN groupadd --gid 1001 stocktrader \
    && useradd --uid 1001 --gid stocktrader --no-create-home --shell /bin/false stocktrader

# ── Persistent-data directories ─────────────────────────────────────────────
# /data      : SQLite database file (mounted as a named volume)
# /app/ml_models : ML model zip files (mounted as a named volume)
RUN mkdir -p /data /app/ml_models /app/Logs \
    && chown -R stocktrader:stocktrader /data /app/ml_models /app/Logs /app

# Copy published output from the build stage.
COPY --from=build --chown=stocktrader:stocktrader /app/publish .

# Copy React SPA build output.
COPY --from=frontend --chown=stocktrader:stocktrader /app/ClientApp/dist ./ClientApp/dist

# Switch to non-root user.
USER stocktrader

# ── Runtime configuration ────────────────────────────────────────────────────
# ASPNETCORE_URLS   : bind to all interfaces on port 5239 (plain HTTP).
#                     HTTPS is disabled in production per Program.cs logic.
# ASPNETCORE_ENVIRONMENT : default to Production; override with -e flag.
ENV ASPNETCORE_URLS="http://+:5239" \
    ASPNETCORE_ENVIRONMENT="Production" \
    DOTNET_RUNNING_IN_CONTAINER="true" \
    TZ="America/New_York"

# ConnectionStrings__DefaultConnection points SQLite to the /data volume.
# This can be overridden at runtime via environment variable.
ENV ConnectionStrings__DefaultConnection="Data Source=/data/stocktrader.db"

# ML model directory inside the container.
ENV ML__ModelDirectory="/app/ml_models"

# ── Volumes ───────────────────────────────────────────────────────────────────
# Declare mount points so docker-compose / docker run can bind named volumes.
VOLUME ["/data", "/app/ml_models", "/app/Logs"]

# ── Network ───────────────────────────────────────────────────────────────────
EXPOSE 5239

# ── Healthcheck ───────────────────────────────────────────────────────────────
# Polls the Blazor app root every 30 s; fails after 3 consecutive failures.
# Uses curl which is available in the aspnet base image.
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
    CMD curl -f http://localhost:5239/ || exit 1

# ── Entrypoint ────────────────────────────────────────────────────────────────
ENTRYPOINT ["dotnet", "StockTrader.dll"]
