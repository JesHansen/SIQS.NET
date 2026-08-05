# Security Policy

## Threat model: no security features are built in

SIQS.NET's distributed sieving service (the **Overlord**, hosted inside `SIQS.UI`) accepts network connections from volunteer clients, receives uploaded relation data over HTTP, and serves a self-contained sieve-client executable for download (`/api/dist/client`).

This service has **no authentication, no authorization, no transport encryption, and no sandboxing** of uploaded data or served binaries. The only integrity check performed is a mathematical verification that uploaded relations satisfy the expected congruence — it is not a security boundary, and it does nothing to authenticate the sender or protect the server or served executable from tampering.

**Only run the Overlord and its clients on a trusted LAN**, among machines and people you already trust, isolated from the public internet. Do not expose the listening port to an untrusted network, and do not bind it to a public interface. If you need this to cross a network you do not fully trust, put it behind your own authenticated, encrypted tunnel (e.g. a VPN or SSH port-forward) — SIQS.NET will not do that for you.

The same caution applies to the served `qs-sieve-client` executable: it is built and published by whoever is running the Overlord instance. Only fetch and run it from an Overlord you trust.

## Supported versions

SIQS.NET is developed on `main`. What security fixes there may be will be made against the latest commit; there are no maintained release branches.

## Reporting a vulnerability

Please report suspected vulnerabilities privately via [GitHub Security Advisories](https://github.com/JesHansen/SIQS.NET/security/advisories/new) rather than opening a public issue. Include steps to reproduce and the affected component (factor base, sieving, filtering, linear algebra, square root, UI, or the distributed Overlord/client).
