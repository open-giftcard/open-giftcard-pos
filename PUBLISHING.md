# Public Publishing Workflow

## Repository identity

The canonical public repository is:

`https://github.com/open-giftcard/open-giftcard-pos`

`origin` must point there. A `legacy` remote may be retained for read-only
history, but its push URL must remain disabled. Never push legacy tags or use a
force push to connect unrelated histories.

## Publishing a change

Before publication:

1. Review staged paths and exclude secrets, device registrations, `.local`
   state, logs, build output, and private infrastructure names.
2. Verify `contracts/README.md` names an accepted public backend commit and its
   SHA-256 matches `backend.openapi.json`.
3. Run formatting, Release build, full tests, dependency review, and the live
   backend smoke gate when the payment contract or deployment boundary changes.
4. Keep changes under `CHANGELOG.md` `Unreleased` until the coordinated public
   candidate is actually created.

## Releases

Create no standalone POS tag. The first public POS tag must use the same
version as backend, portal, and cardholder, starting with the planned
`v0.5.0-rc.1`, and must be listed in the four-repository compatibility
manifest.

Tagging is permitted only after the source and deployment rows in
[`PRODUCTION_READINESS.md`](PRODUCTION_READINESS.md) have evidence or named
operator ownership. Follow [`DEPLOYMENT.md`](DEPLOYMENT.md) for the counter
acceptance gate. Passing CI alone is not release approval.
