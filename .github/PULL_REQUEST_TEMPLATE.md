## What this changes

Describe the behaviour before and after. If this fixes something, say what made
it wrong rather than only what the fix does.

This is a reference till rather than retail software. A change that makes it
look more like a product without making it more correct is probably not the
change to make.

## How it was verified

State what you ran and what it said. If you did not run something, say that
instead of leaving it implied.

- [ ] `dotnet format --verify-no-changes`, build, and `dotnet test`
- [ ] Ran the till against a backend
- [ ] Not applicable, because:

## If it touches presentation

- [ ] Nothing assumes a particular currency or locale. Formatting is derived
      from what is being formatted, not from a fixed expectation.

## Anything a reviewer should look at first

Point at the part you are least sure about.
