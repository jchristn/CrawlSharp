# =============================================================================
# CrawlSharp.Server Dockerfile
# =============================================================================
#
# Multi-stage build:
#   1. Build stage   – restores, publishes, and pre-installs Playwright Firefox
#   2. Runtime stage – slim image with system deps, non-root user, and gosu
#
# Playwright note:
#   CrawlSharp uses Playwright Firefox for headless browser crawling.  The
#   browser binary is downloaded in the build stage (via playwright.ps1) and
#   copied into the runtime image so the first crawl doesn't incur a download.
#   Firefox's sandbox requires a non-root user, so the app runs as "ubuntu".
#
# =============================================================================


# -----------------------------------------------------------------------------
# Build stage
# -----------------------------------------------------------------------------

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore dependencies first (layer caching optimisation)
COPY CrawlSharp/CrawlSharp.csproj CrawlSharp/
COPY CrawlSharp.Server/CrawlSharp.Server.csproj CrawlSharp.Server/
RUN dotnet restore CrawlSharp.Server/CrawlSharp.Server.csproj

# Build and publish the application
COPY . .
RUN dotnet publish CrawlSharp.Server/CrawlSharp.Server.csproj -c Release -f net10.0 -o /app/publish

# Pre-install the Playwright Firefox browser into /root/.cache/ms-playwright.
# This is copied to the runtime image later so it is available immediately.
RUN pwsh /app/publish/playwright.ps1 install firefox


# -----------------------------------------------------------------------------
# Runtime stage
# -----------------------------------------------------------------------------

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# -- Utilities and Playwright system dependencies ------------------------------
# gosu:           drop root privileges in entrypoint.sh
# curl..end:      handy for debugging inside the container
# libnss3..end:   native libs required by Playwright's headless Firefox
#
# This list must match what `pwsh playwright.ps1 install-deps firefox` would
# install.  We maintain it manually because pwsh is not available in the
# aspnet runtime image.  If Playwright reports missing deps at runtime,
# cross-reference its error output and add the missing packages here.
RUN apt-get update && apt-get install -y --no-install-recommends \
    gosu \
    curl wget iputils-ping dnsutils traceroute vim net-tools \
    libnss3 \
    libnspr4 \
    libatk1.0-0 \
    libatk-bridge2.0-0 \
    libcups2 \
    libdrm2 \
    libdbus-1-3 \
    libatspi2.0-0 \
    libx11-6 \
    libx11-xcb1 \
    libxcb1 \
    libxcb-shm0 \
    libxcomposite1 \
    libxcursor1 \
    libxdamage1 \
    libxext6 \
    libxfixes3 \
    libxi6 \
    libxrandr2 \
    libxrender1 \
    libxshmfence1 \
    libxkbcommon0 \
    libgbm1 \
    libpango-1.0-0 \
    libpangocairo-1.0-0 \
    libcairo2 \
    libcairo-gobject2 \
    libgdk-pixbuf-2.0-0 \
    libglib2.0-0t64 \
    libasound2t64 \
    libfreetype6 \
    libfontconfig1 \
    fonts-liberation \
    libgtk-3-0t64 \
    xdg-utils \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

# Copy published application from build stage
COPY --from=build /app/publish .

# Copy pre-installed Playwright browsers from build stage.
# The base image already has an 'ubuntu' user at UID 1000, which matches
# the default host user — so volume-mounted files stay accessible without sudo.
# Playwright's Firefox sandbox requires a non-root user.
COPY --from=build /root/.cache/ms-playwright /home/ubuntu/.cache/ms-playwright

RUN chown -R ubuntu:ubuntu /app \
    && chown -R ubuntu:ubuntu /home/ubuntu

COPY CrawlSharp.Server/entrypoint.sh /entrypoint.sh
RUN sed -i 's/\r$//' /entrypoint.sh && chmod +x /entrypoint.sh

EXPOSE 8000
ENTRYPOINT ["/entrypoint.sh"]
