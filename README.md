# Gift Card POS Till

A counter-side till that takes gift card payment against the Gift Card
platform. Fourth repository alongside the backend, the finance portal, and the
cardholder application.

**This is a gift card payment component, not retail software.** The cashier
enters the amount still owed and presents the customer's card; the shop's own
till keeps the products, quantities and tax. There is no catalogue, stock,
pricing, tax engine, receipt printer,
cash drawer, or offline mode. It exists so the payment journey can be shown end
to end, and so the backend's POS contract has a real client exercising it.

### Maturity relative to the other repositories

This repository is deliberately behind the backend, portal, and cardholder, and
is published as a reference client rather than a released component:

- It does not pin the backend OpenAPI contract. The other two clients commit a
  snapshot under `contracts/` and fail their build on drift; this one
  hand-transcribes the four POS routes it calls, so a backend rename surfaces at
  runtime instead of at build time.
- It has no security-headers middleware, so no CSP, `nosniff`, or
  `Referrer-Policy`, unlike both browser clients.
- Data Protection keys are persisted only in Development. Outside it, antiforgery
  tokens do not survive a restart or span replicas.
- It exposes `/health` but no `/health/ready`.
- Coverage is 47 tests over credential formatting, amount entry, response
  security headers, both health probes, and the pinned backend contract.
  There are no integration or browser tests.

Treat these as the entry criteria for calling it released.

Everything financial happens on the platform. This application decides nothing
about money; it presents a credential, shows what the platform decided, and
reports it back.

## Why a separate repository

ADR-043 in the backend: POS software authenticates as an API client and is
**never given database credentials**. Every safety property the platform relies
on — permission evaluation, Row-Level Security, Ledger balancing and
immutability, append-only audit, idempotency, overspend protection — lives above
the tables. A till holding a database login would sit underneath all of it.

The same reasoning makes this a server-rendered application rather than a page
that calls the API from the browser: the POS client secret is a device
credential and stays server-side.

## The journey

1. **Amount.** The cashier types what is still owed, copied from their till.
2. **Present.** The cashier scans the customer's QR code or types their 12-digit
   code. A barcode scanner types the QR value straight into the field, so one
   input handles both; the till decides which form it received.
3. **Hold.** The platform reserves the entered amount for two minutes and posts
   nothing. The screen shows what is held and the time left. The customer cannot
   spend that value elsewhere meanwhile, and no money has moved.
4. **Confirm.** Charge the held amount, or less if a line was voided — the
   difference is released back to the card in the same operation. More than the
   hold is refused and needs a fresh code.
5. **Receipt.** What the platform recorded, including anything released back.

Releasing without charging is available throughout, and the hold expires on its
own if the cashier walks away.

## Requirements

- .NET 10 SDK
- A running Gift Card Platform backend (see that repository's README)

## Setup

### 1. Register this till with the platform

A platform operator with `platform.pos.clients.manage` registers the client and its
terminal. Against a local backend, through Swagger or `curl`:

```text
POST /api/v1/pos/clients                 { "code": "TILL-DEMO", "displayName": "Demo till" }
POST /api/v1/pos/clients/{id}/terminals  { "code": "T-01", "storeReference": "STORE-DEMO" }
```

The client secret is returned **once** by the first call. Only its hash is
stored, so if it is lost the client must be registered again.

### 2. Configure the till

Codes are not secret and live in `appsettings.Development.json`:

```json
{
  "Pos": {
    "BackendBaseUrl": "http://localhost:5143",
    "ClientCode": "TILL-DEMO",
    "TerminalCode": "T-01",
    "Currency": "TRY"
  }
}
```

The secret never goes in a committed file:

```powershell
dotnet user-secrets set --project src/GiftCardPos.Web "Pos:ClientSecret" "<secret from registration>"
```

The application refuses to start without it. A till that cannot authenticate is
broken, and discovering that mid-sale is the worst possible moment.

### 3. Run

```powershell
dotnet run --project src/GiftCardPos.Web
```

Then open <http://localhost:5190>.

## Demonstrating it

Have a cardholder with a claimed, funded card open the cardholder application and
request a payment credential. Type the 12-digit code into the till, or scan the
QR.

The moment worth pausing on is **step 3**. Between the hold and the confirmation,
refresh the cardholder's card view: the amount is reserved and unspendable, but
the posted balance has not changed and the Ledger has no entry. That gap is the
difference between authorising a payment and taking one, and it is the clearest
way to show why the platform models them separately.

Then confirm for *less* than the amount held. The receipt shows the released
difference, and the cardholder's available balance reflects it immediately.

## Tests

```powershell
dotnet test
```

The suite covers the credential-form decision and amount entry. There is no
integration suite here: the behaviour worth testing against a real database
belongs to the platform and is tested there, and duplicating it would assert the
same invariants twice while proving nothing extra about this client.

## What this deliberately does not do

- Refunds. The platform supports them; a counter refund flow needs supervisor
  authorisation and a returns policy, neither of which is modelled.
- Cash, card, or split tender.
- Offline operation. A till that cannot reach the platform cannot take gift card
  payment, by design: PostgreSQL is the authority for the outcome.
- Any local record of a sale. The platform's report is the record.

## Layout

```text
src/GiftCardPos.Web/
  Backend/     platform client, device-token handling, contract subset
  Display/     how money is written on screen and on a receipt
  Security/    response security headers
  Pages/       amount entry, held sale, receipt
tests/GiftCardPos.Tests/
```

## Integrating an existing till

A shop that already runs a till does not need this application's screen. It can
send the sale to a small JSON API on this machine and print the answer on its own
receipt.

```
POST http://127.0.0.1:5190/local/v1/sale/payment
X-Pos-Local-Key: <the key configured as Pos:LocalApiKey>

{ "amount": 12.50, "saleReference": "SALE-1234", "credential": "1234 5678 9012" }
```

```json
{ "outcome": "approved", "approvedAmount": 8.00, "outstandingAmount": 4.50,
  "currency": "USD", "paymentReference": "019f..." }
```

`outstandingAmount` above zero means the card did not cover the sale and the
till collects the rest by another tender. That is the ordinary case for a gift
card, not an error.

**Three rules for whoever integrates this.**

`outcome` has three values, not two. `indeterminate` means the platform may have
taken the payment and did not say so before the call ended. A till that treats it
as `declined` will charge the customer twice. Read `paymentReference` and check
before retaking payment.

`saleReference` is the idempotency key. Repeating a request whose response was
lost returns the payment already taken rather than taking another, so retry with
the same reference rather than a new one.

The key is required. An unset `Pos:LocalApiKey` disables this surface rather than
opening it: loopback is not authentication, because every process on the machine
can reach it. The API also refuses any request that did not arrive over loopback.

### When the cashier holds the scanner

If the scanner is attached to this machine rather than to the till, the till
hands the sale over and asks about it instead of sending a credential.

```
POST /local/v1/sale/start   { "amount": 12.50, "saleReference": "SALE-1234" }
   -> 201 { "saleId": "...", "outcome": "awaiting-card" }

GET  /local/v1/sale/{saleId}
   -> 200 { "outcome": "approved", "approvedAmount": 8.00, "outstandingAmount": 4.50 }

POST /local/v1/sale/{saleId}/cancel      take the sale back
```

The sale appears on this screen, the cashier presents the card, and the till sees
the outcome the next time it asks.

**Ask rather than hold the line open.** A cashier takes as long as a person
takes, so the till polls instead of waiting on one long request. Nothing couples
the till's timeout to how fast someone finds their phone.

**One sale at a time.** A lane has one reader and one person standing at it, so
starting a second sale while one waits is refused with `lane_busy`. Starting the
same `saleReference` twice returns the sale already waiting rather than putting a
second one in front of the cashier.

**A sale nobody scans lapses** after five minutes and reads back as `expired`,
so a till learns why rather than being told the sale never existed. A `cancel`
that arrives after the cashier has already taken payment is refused with 409
rather than reporting success.
