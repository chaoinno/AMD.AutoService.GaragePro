#!/usr/bin/env bash
# Run on the origin as its owner; this script uses sudo to back up/update the live vhost and reload nginx.
# Prerequisite before switching Cloudflare to Full (strict): the origin certificate must cover service.garage-pro.net.
#   sudo certbot --nginx --expand -d gpservice.garage-pro.net -d service.garage-pro.net
# Deploy add-service-domain.sh first so the canonical hostname is already served by this vhost.
# Re-running is safe: each matching HTTPS server block has its own marker-wrapped redirect.
set -euo pipefail

SITE=/etc/nginx/sites-available/gpservice.garage-pro.net
OLD=gpservice.garage-pro.net
NEW=service.garage-pro.net

command -v sudo >/dev/null || { echo "sudo is required" >&2; exit 1; }
command -v python3 >/dev/null || { echo "python3 is required" >&2; exit 1; }
sudo test -f "$SITE" || { echo "Missing $SITE; install the web vhost first." >&2; exit 1; }

candidate=$(mktemp)
trap 'rm -f "$candidate"' EXIT
sudo python3 - "$SITE" >"$candidate" <<'PY'
import re
import sys
from pathlib import Path

source = Path(sys.argv[1]).read_text()
# Tokenize braces while ignoring quoted strings and comments, including Certbot's nested if blocks.
tokens = [match for match in re.finditer(r'"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'|#[^\n]*|[{}]|[^\s{};]+|;', source) if not match.group().startswith('#')]
edits = []
matched = 0
for index, token in enumerate(tokens[:-1]):
    if token.group() != 'server' or tokens[index + 1].group() != '{':
        continue
    opening = tokens[index + 1]
    depth = 1
    closing = None
    for cursor in tokens[index + 2:]:
        if cursor.group() == '{':
            depth += 1
        elif cursor.group() == '}':
            depth -= 1
            if depth == 0:
                closing = cursor
                break
    if closing is None:
        raise SystemExit('Unbalanced server block; no changes made.')
    block = source[opening.end():closing.start()]
    uncommented = re.sub(r'#[^\n]*', '', block)
    names = re.findall(r'\bserver_name\s+([^;]+);', uncommented)
    listens = re.findall(r'\blisten\s+([^;]+);', uncommented)
    if not any('gpservice.garage-pro.net' in names_line.split() for names_line in names):
        continue
    if not any('ssl' in listen.split()[1:] for listen in listens):
        continue
    if not any('service.garage-pro.net' in names_line.split() for names_line in names):
        raise SystemExit('Canonical hostname is missing from an HTTPS block; run add-service-domain.sh first.')
    matched += 1
    redirect = 'if ($host = gpservice.garage-pro.net) { return 301 https://service.garage-pro.net$request_uri; }'
    marker = '# BEGIN SERVICEPRO CANONICAL'
    if marker in block:
        if redirect not in block or '# END SERVICEPRO CANONICAL' not in block:
            raise SystemExit('Existing canonical marker is incomplete; review the vhost manually.')
        continue
    edits.append((opening.end(), '\n    ' + marker + '\n    ' + redirect + '\n    # END SERVICEPRO CANONICAL\n'))
if not matched:
    raise SystemExit('No HTTPS server block serves gpservice.garage-pro.net; no changes made.')
for position, insertion in reversed(edits):
    source = source[:position] + insertion + source[position:]
sys.stdout.write(source)
PY

if sudo cmp -s "$SITE" "$candidate"; then
  echo "Canonical redirects are already installed."
else
  backup="$SITE.bak-canonical-$(date +%Y%m%d%H%M%S)-$$"
  sudo cp -p "$SITE" "$backup"
  echo "Backed up the live vhost to $backup"
  sudo cp "$candidate" "$SITE"
  if ! sudo nginx -t; then
    sudo cp -p "$backup" "$SITE"
    echo "nginx -t failed; restored the backup without reloading." >&2
    exit 1
  fi
fi
sudo nginx -t
sudo systemctl reload nginx

headers=$(curl --silent --show-error --retry 3 --retry-delay 2 --dump-header - --output /dev/null "https://$OLD/features")
code=$(printf '%s\n' "$headers" | awk '/^HTTP\// { code=$2 } END { print code }')
location=$(printf '%s\n' "$headers" | tr -d '\r' | awk 'tolower($1) == "location:" { value=$2 } END { print value }')
[[ "$code" == "301" && "$location" == "https://$NEW/features" ]] || {
  echo "Canonical check failed: $OLD/features returned $code, Location: $location" >&2
  exit 1
}
code=$(curl --silent --show-error --retry 3 --retry-delay 2 --output /dev/null --write-out '%{http_code}' "https://$NEW/healthz")
[[ "$code" == "200" ]] || { echo "$NEW/healthz returned $code; expected 200." >&2; exit 1; }
echo "Canonical redirect verified: $OLD/features → https://$NEW/features (301); canonical healthz = 200."
