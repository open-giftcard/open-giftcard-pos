"""Calling the till's local API from Python.

Copy this into your own codebase and change it. It is an example, not a library.

Standard library only, so it runs anywhere a till runs without you adding a
dependency to software that takes money.
"""

from __future__ import annotations

import json
import time
import urllib.error
import urllib.request
from dataclasses import dataclass

POS = "http://127.0.0.1:5190"
ROUTE_PAYMENT = "/local/v1/sale/payment"
ROUTE_START = "/local/v1/sale/start"
ROUTE_SALE = "/local/v1/sale/{sale_id}"


class PaymentIndeterminate(Exception):
    """The platform may have taken the payment and did not say so.

    Raised as its own type on purpose. The single most damaging mistake a till
    can make here is treating this as a decline and collecting the whole amount
    again, so it must not be catchable by accident alongside a refusal.
    """

    def __init__(self, payment_reference: str | None):
        super().__init__(
            "The payment may have been taken. Check reference "
            f"{payment_reference} before charging again."
        )
        self.payment_reference = payment_reference


@dataclass(frozen=True)
class Payment:
    approved: float
    outstanding: float
    currency: str
    reference: str

    @property
    def fully_paid(self) -> bool:
        return self.outstanding <= 0


class Till:
    def __init__(self, key: str, base_url: str = POS, timeout: float = 30.0):
        self._key = key
        self._base = base_url.rstrip("/")
        self._timeout = timeout

    def _call(self, method: str, path: str, body: dict | None = None) -> dict:
        data = json.dumps(body).encode() if body is not None else None
        request = urllib.request.Request(
            self._base + path,
            data=data,
            method=method,
            headers={
                "X-Pos-Local-Key": self._key,
                "Content-Type": "application/json",
            },
        )
        try:
            with urllib.request.urlopen(request, timeout=self._timeout) as response:
                return json.loads(response.read() or b"{}")
        except urllib.error.HTTPError as error:
            # A refusal is data, not a crash: "declined" is an ordinary outcome
            # at a counter and the body explains it.
            return json.loads(error.read() or b"{}")

    def take_payment(self, amount: float, sale_reference: str, credential: str) -> Payment:
        """Your till has the scanner.

        Retry a failed call with the SAME sale_reference. It is the idempotency
        key: the same one returns the payment already taken, a new one takes a
        second payment.
        """
        result = self._call(
            "POST",
            ROUTE_PAYMENT,
            {
                "amount": amount,
                "saleReference": sale_reference,
                "credential": credential,
            },
        )
        return self._interpret(result)

    def hand_over(
        self,
        amount: float,
        sale_reference: str,
        poll_seconds: float = 2.0,
        give_up_after: float = 300.0,
    ) -> Payment:
        """This machine has the scanner.

        The cashier presents the card on the till's own screen, so this asks
        rather than holding a request open across however long a person takes.
        """
        started = self._call(
            "POST", ROUTE_START, {"amount": amount, "saleReference": sale_reference}
        )
        sale_id = started.get("saleId")
        if not sale_id:
            raise RuntimeError(f"Sale was not accepted: {started.get('reason')}")

        deadline = time.monotonic() + give_up_after
        status = started
        while status.get("outcome") == "awaiting-card":
            if time.monotonic() > deadline:
                # Take the sale back rather than leaving it on the screen.
                self._call("POST", ROUTE_SALE.format(sale_id=sale_id) + "/cancel")
                raise RuntimeError("No card was presented.")
            time.sleep(poll_seconds)
            status = self._call("GET", ROUTE_SALE.format(sale_id=sale_id))

        return self._interpret(status)

    @staticmethod
    def _interpret(result: dict) -> Payment:
        outcome = result.get("outcome")
        if outcome == "indeterminate":
            raise PaymentIndeterminate(result.get("paymentReference"))
        if outcome != "approved":
            raise RuntimeError(result.get("reason") or outcome or "declined")

        return Payment(
            approved=float(result.get("approvedAmount") or 0),
            outstanding=float(result.get("outstandingAmount") or 0),
            currency=result.get("currency") or "",
            reference=str(result.get("paymentReference") or ""),
        )


if __name__ == "__main__":
    import os
    import sys

    till = Till(key=os.environ["POS_LOCAL_KEY"])
    sale_total = 12.50
    sale = "SALE-1234"

    try:
        if len(sys.argv) > 1:
            payment = till.take_payment(sale_total, sale, credential=sys.argv[1])
        else:
            print("Present the card on the till screen...")
            payment = till.hand_over(sale_total, sale)
    except PaymentIndeterminate as uncertain:
        # Do not charge again here. Somebody has to look.
        print(f"UNCERTAIN: {uncertain}")
        raise SystemExit(2)
    except RuntimeError as refused:
        print(f"Declined: {refused}")
        raise SystemExit(1)

    print(f"Took {payment.approved:.2f} {payment.currency}")
    if not payment.fully_paid:
        # Normal for a gift card, not an error: collect the rest by another tender.
        print(f"Still to pay: {payment.outstanding:.2f} {payment.currency}")
