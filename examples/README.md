# Integrating a till

Working samples for calling this application's local API. Copy them into your
own codebase and change them. They are examples, not a library: there is no
package, no versioning, and no compatibility promise. If one of these stops
matching the API, the API is right and the sample is stale.

Every sample is short on purpose. Integrating should be an afternoon, and if
these needed to be long that would be a problem with the API rather than with
the samples.

## Which shape to use

**Your till has the scanner.** Send the amount and the scanned credential in one
call and read the result. See `take-payment` in any sample.

**This machine has the scanner.** Hand the sale over and ask how it went. The
cashier presents the card on this application's screen. See `hand-over` in any
sample.

## Three things integrators get wrong

**`outcome` has three values, not two.** `indeterminate` means the platform may
have taken the payment and did not say so before the call ended. A till that
treats it as `declined` will collect the whole amount again and the customer pays
twice. Read `paymentReference`, and check before taking payment again.

**`saleReference` is the idempotency key.** Retry a failed call with *the same*
reference, never a new one. The same reference returns the payment already taken;
a new one takes a second payment.

**The key is required and the API is loopback-only.** Set `Pos:LocalApiKey` on
the till application and send it as `X-Pos-Local-Key`. An unset key disables the
API rather than opening it, because every process on the machine can reach
loopback.

## Reading the amounts

`outstandingAmount` above zero is normal, not an error. It means the card did not
cover the whole sale, which is the usual case for a gift card, and your till
collects the difference by another tender.

## Samples

| File | Language | Shows |
| --- | --- | --- |
| `take-payment.sh` | curl | Both shapes, for checking a lane by hand |
| `take_payment.py` | Python | Both shapes, with retry and polling |
| `TakePayment.cs` | C# | Both shapes, typed |

None of these are compiled or run by this repository's build. `ExampleRoutesTests`
asserts only that the routes they name still exist, because the failure that
actually happens to samples is that the API moves and nobody updates them.
