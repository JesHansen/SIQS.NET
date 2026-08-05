# Security Policy

## Threat model: no security features are built in

SIQS.NET's distributed sieving service (the **Overlord**, hosted inside `SIQS.UI`) accepts network connections from volunteer clients, receives uploaded relation data over HTTP, and serves a self-contained sieve-client executable for download (`/api/dist/client`).

This service has **no authentication, no authorization, no transport encryption, and no sandboxing** of uploaded data or served binaries. The only integrity check performed is a mathematical verification that uploaded relations satisfy the expected congruence — it is not a security boundary, and it does nothing to authenticate the sender or protect the server or served executable from tampering.

**Only run the Overlord and its clients on a trusted LAN**, among machines and people you already trust, isolated from the public internet. Do not expose the listening port to an untrusted network, and do not bind it to a public interface. If you need this to cross a network you do not fully trust, put it behind your own authenticated, encrypted tunnel (e.g. a VPN or SSH port-forward) — SIQS.NET will not do that for you.

The same caution applies to the served `qs-sieve-client` executable: it is built and published by whoever is running the Overlord instance. Only fetch and run it from an Overlord you trust.

### What that means concretely

Anyone who can reach the listening port can, without credentials:

- **submit or displace a job** — `POST /api/dist/submit` is unauthenticated, so a stranger can start their own factorization on your hardware, or interfere with the one you are running;
- **lease work and upload relations** — uploaded relations are checked for mathematical validity, which stops a worker from corrupting a result but does not stop one from consuming server memory, disk, and ingest capacity;
- **read job state** — `GET /api/dist/status` and `GET /api/dist/job` disclose what you are factoring; and
- **download an executable that your machines then run** — `GET /api/dist/client/{platform}` serves a binary over plain HTTP with no signature and no published digest.

That last one is the sharpest edge. A worker downloading over HTTP has no way to tell a genuine client from one substituted in transit, and the thing it runs is a native executable with the privileges of the user who started it. **The trust decision a worker makes is not "do I trust this software", it is "do I trust this network path".**

### Reducing the risk today

Being honest that "run it on a trusted LAN" is a policy rather than a control, here is what an operator can actually enforce with what ships:

- **Put a reverse proxy in front of it.** Nginx, Caddy, or IIS terminating TLS with a client certificate or HTTP basic auth in front of `/api/dist/*` gives you the transport encryption and authentication the service does not have. Nothing in the protocol objects to being proxied.
- **Bind to a specific interface.** `--urls "http://10.0.0.5:5078"` rather than `0.0.0.0`, plus a host firewall rule limited to the worker subnet.
- **Verify the client binary out of band.** Compute the digest on the server and check it on each worker before the first run:

  ```powershell
  # on the server, after ./build.ps1
  Get-FileHash SIQS.UI/download/linux-x64/qs-sieve-client -Algorithm SHA256

  # on the worker, after downloading
  sha256sum ./qs-sieve-client
  ```

  This is manual because the server does not publish the digest — see below.

### Known gaps

These are absent, known, and not planned for any particular date. They are listed so nobody has to rediscover them:

- **No shared-secret or token authentication** on the distributed endpoints. A single pre-shared header checked by the server would be cheap and would raise the bar considerably; it is not implemented.
- **No published digest or signature for the client download.** The server knows the SHA-256 of the file it is serving and does not tell anyone, so a worker cannot verify what it received without an out-of-band channel.
- **No rate limiting or per-client quotas** beyond the inbox size cap (`Overlord:MaxRelationSpoolBytes`).
- **No TLS by default.** HTTPS is available through `launchSettings.json` in development and through a proxy in deployment, but the documented worker commands use `http://`.

Contributions implementing any of these are welcome; see [CONTRIBUTING.md](CONTRIBUTING.md).

## Supported versions

SIQS.NET is developed on `main`. What security fixes there may be will be made against the latest commit; there are no maintained release branches.

## Reporting a vulnerability

Please report suspected vulnerabilities privately via [GitHub Security Advisories](https://github.com/JesHansen/SIQS.NET/security/advisories/new) rather than opening a public issue. Include steps to reproduce and the affected component (factor base, sieving, filtering, linear algebra, square root, UI, or the distributed Overlord/client).
