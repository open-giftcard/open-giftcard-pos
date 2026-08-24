# POS Deployment Contract

This document describes the reference deployment boundary for the POS member
of the coordinated Open Giftcard candidate. It does not provide retail device
management, a signed installer, or a production secret store.

## Topology

```text
retail till -- loopback + local key --> Open Giftcard POS -- HTTPS --> backend
cashier browser -----------------------> 127.0.0.1 lane UI
```

Deploy one POS process per lane. Do not load-balance replicas for one lane: the
pending handoff queue is process-local by design.

## Required configuration

```text
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://127.0.0.1:5190
AllowedHosts=<exact-local-host>

Pos__BackendBaseUrl=https://api.<deployment-domain>
Pos__ClientCode=<registered-client-code>
Pos__ClientSecret=<device-secret-reference>
Pos__TerminalCode=<registered-terminal-code>
Pos__Currency=<ISO-4217-currency>
Pos__LocalApiKey=<independent-random-local-integration-key>
DataProtection__KeysPath=<absolute-protected-persistent-directory>
```

`Pos:ClientSecret` authenticates this device to the platform. `Pos:LocalApiKey`
authenticates local retail software to this process. They are different trust
boundaries and must never share a value. The browser receives neither.

The backend URL must be HTTPS outside Development. Startup refuses a plain HTTP
backend before the till can accept a sale.

## Device lifecycle

The deployment owner must provide:

- a device-bound or operating-system-protected store for both secrets;
- enrollment that returns the POS client secret once and places it directly in
  that store;
- rotation and revocation without writing a secret to logs, shell history, or
  an installer response file;
- terminal retirement and loss response;
- signed application updates with a rollback policy;
- kiosk and browser policy appropriate to the counter;
- an inventory mapping client and terminal codes to physical lanes.

User secrets and plain environment values are local development mechanisms.
They are not production custody evidence.

## Health and recovery

- `GET /health` proves only that the local process is alive.
- `GET /health/ready` cascades backend readiness and returns 503 when the
  platform cannot serve.
- A readiness success does not prove the configured POS secret is valid. The
  staging acceptance flow must perform device authentication and a reversed
  payment.
- Data Protection keys must survive application replacement so an in-flight
  antiforgery form remains valid. Protect and back up the directory according
  to the lane recovery policy.
- Waiting scanner handoff state does not survive restart. The retail till must
  receive `expired` or an unavailable response and begin again.
- A payment with an indeterminate response is recovered by its stable sale and
  payment references. Never charge again with a new reference until the first
  outcome is known.

## Staging promotion checklist

1. Record the exact POS and backend commits and verify the contract pin.
2. Verify HTTPS backend transport, exact host policy, CSP, antiforgery, and
   `no-store` responses.
3. Authenticate the enrolled device, run readiness, and confirm a wrong secret
   is refused without revealing which credential failed.
4. Exercise QR, numeric code, partial approval, confirmation, cancellation,
   timeout, lost response, retry with the same sale reference, and full refund.
5. Restart the process and verify Data Protection continuity and the documented
   loss of pending local handoff state.
6. Exercise scanner input, keyboard-only use, zoom, reduced motion, receipt
   handoff, and the supported browser/device policy.
7. Verify secret rotation, device revocation, signed update, rollback, logs,
   monitoring, alerts, and incident ownership.

Record results in the release evidence. A source tag without this evidence is
not a certified counter deployment.
