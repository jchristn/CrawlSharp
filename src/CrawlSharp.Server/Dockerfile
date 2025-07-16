FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build-env

RUN apt-get update && apt-get install -y iputils-ping traceroute net-tools curl wget dnsutils iproute2 && rm -rf /var/lib/apt/lists/*

# Install dependencies for headless browser
RUN apt-get update

RUN apt-get install -y \
    libasound2 \
    libatk-bridge2.0-0 \
    libatk1.0-0 \
    libatspi2.0-0 \
    libc6 \
    libcairo2 \
    libcups2 \
    libdbus-1-3 \
    libdrm2 \
    libexpat1 \
    libfontconfig1 \
    libgbm1 \
    libgcc1 \
    libglib2.0-0 \
    libgtk-3-0 \
    libnspr4 \
    libnss3 \
    libpango-1.0-0 \
    libpangocairo-1.0-0 \
    libstdc++6 \
    libx11-6 \
    libx11-xcb1 \
    libxcb-dri3-0 \
    libxcb1 \
    libxcomposite1 \
    libxcursor1 \
    libxdamage1 \
    libxext6 \
    libxfixes3 \
    libxi6 \
    libxkbcommon0 \
    libxrandr2 \
    libxrender1 \
    libxss1 \
    libxtst6 \
    ca-certificates \
    fonts-liberation \
    lsb-release \
    wget \
    xdg-utils

RUN apt-get install -y \
    libgbm-dev \
    libxshmfence1 \
    libglu1-mesa \
    libegl1-mesa \
    libgl1-mesa-glx \
    libgl1-mesa-dri

WORKDIR /app
COPY . ./
EXPOSE 8000
ENTRYPOINT ["dotnet", "CrawlSharp.Server.dll", "*", "8000"]
