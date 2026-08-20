# Backend OpenAPI Contract

`backend.openapi.json` was captured from the authoritative public backend:

- Repository: https://github.com/open-giftcard/open-giftcard
- Branch: `main`
- Commit: `fbf3f7bd27479db66b7e3ae022576fc9db46278a`
- Endpoint: `/swagger/v1/swagger.json`
- SHA-256:
  `20BB4B338EE6FCA3A72146F8AA71D0F0644FEAE5816C610B08CAB9971C424B43`

That public commit was rebuilt and its served OpenAPI document was verified to
have exactly the SHA-256 recorded above, with the repository tree confirmed
identical to the published commit. Later backend changes do not silently move
this pin: updating the snapshot requires an explicit review and a new public
commit reference.

## Why this repository needs it more than the others

This till has no generated client. It hand-transcribes the routes it calls, so a
backend rename or a changed request field would otherwise surface at runtime, at
a counter, as a failed sale rather than as a failed build. This was the only
client whose backend coupling CI did not guard.

`BackendContractTests` asserts that every route and request field this
application actually sends exists in the document beside it. That catches the
transcription drifting from the contract. `scripts/verify-contract-pin.sh`
separately asserts that the document is the one this README claims, because a
recaptured file with a stale hash passes every in-repo assertion happily.

## Routes this client depends on

- `POST /api/v1/pos/auth/token`
- `POST /api/v1/pos/payment-provisions`
- `GET /api/v1/pos/payment-provisions/{provisionId}`
- `POST /api/v1/pos/payment-provisions/{provisionId}/cancel`
- `POST /api/v1/pos/payment-provisions/{provisionId}/confirm`

The backend also serves `POST /api/v1/pos/balance-inquiries` and
`POST /api/v1/pos/payment-provisions/{provisionId}/refunds`, which this till does
not yet call.

Update the snapshot only after reviewing backend contract changes at an
explicitly accepted backend commit. Never capture from a moving backend branch
without an explicit commit pin.
