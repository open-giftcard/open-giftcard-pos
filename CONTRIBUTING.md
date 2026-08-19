# Contributing

Thanks for looking. Open an issue before a large change so we can agree on the
shape.

## Read this first

This repository is a demonstration till, published as a reference client rather
than a released component. It is deliberately behind the other three, and its
README lists how. The most useful contributions right now are the ones that
close that gap:

- Pin the backend OpenAPI contract under `contracts/`, as the portal and
  cardholder do, so a backend rename fails the build instead of surfacing at a
  counter.
- Add security-headers middleware. The till sends no CSP, `nosniff`, or
  `Referrer-Policy`.
- Enforce a Data Protection key path outside Development, so antiforgery tokens
  survive a restart.
- Add `/health/ready` alongside the existing liveness endpoint.
- Add integration coverage. There are nineteen unit tests and nothing else.

Please do not add retail features. The basket is a fixed mock on purpose; this
exists so the payment journey has a real client exercising it, not to become a
point-of-sale system.

## What this repository decides

Nothing financial. The backend holds and confirms value, and owns every rule
about balances and refunds. A finding or a change there belongs in the
`open-giftcard` repository.

## Getting a working copy

You need .NET 10 and a running backend. No database, no Node.

Register a till through the backend's permission-protected API, capture the
client secret it returns once, then:

```bash
dotnet user-secrets set --project src/GiftCardPos.Web "Pos:ClientSecret" "<secret>"
dotnet run --project src/GiftCardPos.Web
```

The application refuses to start without that secret rather than running in a
degraded state. The codes in `appsettings.Development.json` match the ones the
README tells you to register; keep them in step if you change either.

## Running the tests

```bash
dotnet test GiftCardPos.slnx -c Release
```

## What a good pull request looks like

Behavioural changes carry tests. The build treats warnings as errors, and CI
runs `dotnet format --verify-no-changes`, the build, and the tests.

Report results honestly, including anything you could not run.

## Security

Do not open a public issue for a suspected vulnerability. See
[SECURITY.md](SECURITY.md).

## Licence

Contributions are accepted under the Apache License 2.0.
