FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build-env
RUN apt-get update && apt-get install -y iputils-ping traceroute net-tools curl wget dnsutils iproute2 && rm -rf /var/lib/apt/lists/*
WORKDIR /app

# Copy everything from source directory into /App
COPY . ./
EXPOSE 8000
ENTRYPOINT ["dotnet", "CrawlSharp.Server.dll", "0.0.0.0", "8000"]
