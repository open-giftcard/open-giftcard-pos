# Security Policy

## Reporting a vulnerability

Use GitHub's private vulnerability reporting on this repository: **Security →
Report a vulnerability**. Please do not open a public issue for anything you
believe is exploitable.

There is no bounty and no formal response-time commitment.

## Supported versions

None. This repository is a demonstration till, published as a reference client
rather than a released component, and it is deliberately behind the other
repositories. `main` is the only branch.

## Where the boundary is

The till decides nothing financial. It holds and confirms value through the
platform backend, which is the only authority for balances, refunds, and every
rule around them. A finding in any of those belongs in the `open-giftcard`
repository.

**The till authenticates as a device, not a user.** It exchanges a client code
and secret for a 15-minute access token. There is no refresh credential by
design, and the token carries the POS client and terminal identity, so a till
cannot act for another client.

**The client secret never reaches the browser.** It is supplied through user
secrets or the environment, is held only in the server process, and the
application refuses to start without it rather than running in a degraded state.
Registration returns the secret exactly once and stores only its hash; there is
no recovery endpoint.

**Payment credentials are treated as secrets.** A scanned QR value or typed
numeric code is single use, valid for 60 seconds, and resolved server-side.

## Known gaps

This remains a reference client rather than a released counter product:

- **No physical-device or human browser certification.** The backend's local
  smoke gate now exercises live device authentication, payment, reporting, and
  refund. Scanner hardware, browser policy, receipt handoff, and counter use
  against a named staging deployment remain unverified.
- **No hardware-backed device secret custody.** Configuration and .NET user
  secrets are appropriate for development. A deployed till needs an operating
  system or hardware-backed store with an installation and rotation procedure.
- **No managed device lifecycle.** Installer signing, terminal enrollment,
  controlled updates, kiosk policy, and device retirement remain deployment
  responsibilities.

Treat these as entry criteria for calling this component released, not as
accepted risk in something that ships.

## Scope

In scope: handling of the client secret and access token, and the presentation
of payment credentials.

Out of scope: the gaps listed above, which are known and recorded rather than
undiscovered; and all financial correctness, which the backend enforces.
