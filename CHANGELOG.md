# Changelog

All notable changes to this project are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

There is no released version and nothing has been deployed anywhere, so there
are no version headings yet. Everything below has landed on `main` since the
first public commit. The tags that predate the open-source cleanup are not
usable and are not listed.

## Unreleased

### Added

- A security policy with a private reporting channel, and a contributor guide.
- Community health files: code of conduct, issue and pull request templates,
  and code owners.

### Changed

- The default currency is USD, overridable through `Pos:Currency`, and the
  demonstration basket is plainly named goods rather than a locale-specific one.
- `MockCartTests` derives its expected string from the line it formats and
  asserts that no comma appears under a comma-decimal culture, so it tests the
  formatting rule rather than a fixed basket price.
