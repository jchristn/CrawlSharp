#!/bin/bash
set -e

# Fix ownership on all volume-mounted paths so ubuntu can write to them.
# Docker volume mounts inherit host-side ownership, which may not match
# the container's ubuntu user (UID 1000).  Running this at startup ensures
# permissions are correct regardless of the host environment.
chown -R ubuntu:ubuntu /app /home/ubuntu

# Drop privileges and run the application as ubuntu.
# Playwright's Firefox sandbox requires a non-root user.
exec gosu ubuntu dotnet CrawlSharp.Server.dll "*" "8000"
