#!/usr/bin/env bash
# deploy-public.sh
# Обновляет dist-public/ из dist/ и перезапускает nginx.
# Запускать после изменения публичных страниц: index.html, stats.html, contact.html
# НЕ трогает авторизованную часть (dist/), НЕ перезапускает van-web-service.
set -euo pipefail

DIST="/home/user/VAN/webclient/dist"
DIST_PUB="/home/user/VAN/webclient/dist-public"
PUBLIC_FILES=(index.html stats.html contact.html contacts.html contact-layout-options.html home-news-staging-9f4k2m.html newsroom-preview.html about-preview.html alex-founder-photo.jpg alex-founder-photo-web.jpg van-logo.png)

echo "[deploy-public] $(date -u +'%Y-%m-%d %H:%M UTC')"

mkdir -p "$DIST_PUB"

for f in "${PUBLIC_FILES[@]}"; do
    if [[ -f "$DIST/$f" ]]; then
        cp "$DIST/$f" "$DIST_PUB/$f"
        echo "  copied: $f"
    else
        echo "  WARN: $f не найден в dist/, пропускаем"
    fi
done

echo "[deploy-public] reload nginx..."
docker exec van-frontend nginx -s reload
echo "[deploy-public] done"
