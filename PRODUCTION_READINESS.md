# Production Readiness

Open Giftcard POS is a reference payment component. Its source exercises the
platform's till contract, but this repository does not claim that a counter
deployment has been certified for production.

Last reviewed: 2026-08-24.

| Area | Status | Boundary |
| --- | --- | --- |
| Server-rendered credential handling | Implemented and tested | The POS client secret and device token never reach the browser. |
| Payment hold, confirmation, cancellation, and receipt | Implemented and tested | The backend remains the only financial authority. |
| Local till handoff API | Reference implementation | Loopback plus `X-Pos-Local-Key`; unset key disables the API. |
| Antiforgery, CSP, framing, referrer, and `no-store` headers | Implemented and tested | Operator still controls the host and browser policy. |
| Durable Data Protection keys | Implemented fail-closed outside Development | Every instance serving one lane must share protected key storage. |
| Liveness and backend readiness | Implemented | Readiness proves backend availability, not device-secret validity. |
| Pinned public backend contract | Implemented and tested | The pin must be aligned with the coordinated candidate. |
| Live backend transaction smoke | Implemented from the backend repository | Browser and physical-device acceptance remain incomplete. |
| HTTPS backend enforcement outside Development | Implemented and tested | Development alone may use a plain HTTP backend. |
| Allowed host policy | Implemented fail-closed outside Development | Production must name exact local hosts; wildcard entries are refused. |
| Trusted ingress policy | Not applicable to the supported lane topology | Bind the UI and local API to loopback; POS calls the backend as an outbound client. |
| Lane state across replicas or restarts | Explicitly unsupported | Pending cashier handoff state is process-local; deploy one process per lane. |
| Device secret custody and rotation | Deployment responsibility | User secrets are development storage, not a production device vault. |
| Installer, signing, enrollment, updates, kiosk policy, retirement | Deployment responsibility | No device-management artifacts exist in this repository. |
| Metrics, alerts, logs, incident response | Deployment responsibility | Health and structured framework logs are source evidence only. |
| Human browser, scanner, receipt, and counter acceptance | Blocked | Required before calling the component released. |
| Coordinated public release | In progress | The four repositories share a verified compatibility contract. Canonical tags and the final exact-commit evidence manifest do not exist yet. |

## Supported deployment boundary

The candidate supports one POS process per physical or logical lane. It does
not support load-balancing multiple processes behind one lane URL because the
pending handoff queue is deliberately in memory. A process restart ends a sale
that is still waiting for a scan; a hold already accepted by the platform must
be recovered through its payment reference rather than guessed locally.

An existing retail till may call the loopback local API. It must treat
`indeterminate` as an unknown outcome and query the recorded payment reference
before attempting another charge. It must never interpret an unavailable
Open Giftcard process as approval.

## Release gate

The POS member of `v0.5.0-rc.1` requires:

1. A contract pin matching the exact backend candidate commit and SHA-256.
2. Release build, format, full tests, dependency review, and code scanning.
3. The backend repository's live readiness, RLS, payment, report, and refund
   smoke gate against the exact candidate.
4. Human validation in the supported browsers with a keyboard-wedge scanner,
   typed numeric code, cancel, timeout, partial approval, and lost-response
   recovery.
5. A named device-secret store, enrollment, rotation, revocation, update, and
   retirement procedure for the target deployment.
6. An immutable artifact, checksum, SBOM, provenance, and matching public tag.

Passing source tests alone is not counter-device certification.
