#!/usr/bin/env bash
set -euo pipefail

cd /home/user/VAN

docker run --rm \
  -v /home/user/VAN:/home/user/VAN \
  -w /home/user/VAN/src \
  mcr.microsoft.com/dotnet/sdk:8.0 \
  dotnet publish VANWebService.csproj -c Release -r linux-x64 --self-contained false -o /home/user/VAN/publish-dev

docker build -f Dockerfile.dev -t van-web-service:dev .

docker rm -f van-web-service-dev >/dev/null 2>&1 || true
docker run -d \
  --name van-web-service-dev \
  --network host \
  --restart no \
  --env-file /home/user/.config/van-web-service.env \
  -e VAN_DISABLE_STARTUP_EMAIL=true \
  -e VAN_DISABLE_HOSTED_SERVICES=true \
  -e ASPNETCORE_URLS=http://0.0.0.0:38041 \
  -v /home/user/.config/van-web-service.config.json:/app/config.json:ro \
  van-web-service:dev

docker logs --tail 120 van-web-service-dev
