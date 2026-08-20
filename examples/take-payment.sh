#!/bin/bash
# Checking a lane by hand. Not a till integration: it is what you run to find out
# whether the lane is reachable and configured before writing any code.
#
#   POS_LOCAL_KEY=... ./take-payment.sh 12.50 SALE-1234 "1234 5678 9012"
#
# Two things to carry into whatever you write next, because they are the
# mistakes that cost money rather than time:
#
#   outcome=indeterminate is NOT a decline. It means the platform may have taken
#   the payment and did not say so before the call ended. Collect the amount
#   again and the customer pays twice. Read paymentReference and check first.
#
#   saleReference is the idempotency key. Retry with the SAME one: it returns the
#   payment already taken, whereas a new reference takes a second payment.
set -euo pipefail

POS="${POS:-http://127.0.0.1:5190}"
KEY="${POS_LOCAL_KEY:?Set POS_LOCAL_KEY to the till's Pos:LocalApiKey}"
AMOUNT="${1:?amount, for example 12.50}"
SALE="${2:?your till's sale reference, for example SALE-1234}"
CREDENTIAL="${3:-}"

if [ -n "$CREDENTIAL" ]; then
  # Your till has the scanner: send the credential and read the result.
  curl -sS -X POST "$POS/local/v1/sale/payment" \
    -H "X-Pos-Local-Key: $KEY" \
    -H 'Content-Type: application/json' \
    -d "{\"amount\": $AMOUNT, \"saleReference\": \"$SALE\", \"credential\": \"$CREDENTIAL\"}"
  echo
  exit 0
fi

# This machine has the scanner: hand the sale over, then ask about it.
started=$(curl -sS -X POST "$POS/local/v1/sale/start" \
  -H "X-Pos-Local-Key: $KEY" \
  -H 'Content-Type: application/json' \
  -d "{\"amount\": $AMOUNT, \"saleReference\": \"$SALE\"}")
echo "$started"

sale_id=$(printf '%s' "$started" | sed -n 's/.*"saleId":"\([^"]*\)".*/\1/p')
[ -n "$sale_id" ] || { echo "No saleId in response; nothing to poll." >&2; exit 1; }

echo "Present the card on the till screen. Polling..." >&2
while true; do
  status=$(curl -sS "$POS/local/v1/sale/$sale_id" -H "X-Pos-Local-Key: $KEY")
  case "$status" in
    *'"outcome":"awaiting-card"'*) sleep 2 ;;
    *) echo "$status"; break ;;
  esac
done
