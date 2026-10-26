# Registry entry schema and signed index

Companion to [registry-trust-policy.md](registry-trust-policy.md). The reader is
`Launcher.Core/RegistryIndexReader.cs`; the samples are in [registry-samples](registry-samples).

## Entry files

The registry repository holds one file per community, `servers/<id>.json`:

| Field | Type | Rules |
|-------|------|-------|
| `id` | string | `^[a-z0-9][a-z0-9-]{1,31}$`; also the file name; unique |
| `name` | string | 1 to 48 characters, no control characters |
| `status` | string | `approved`, `needs-approval` or `suspended`; set by maintainers |
| `mirrors` | string[] | 1 to 8 `https` base URLs ending in `/`, no credentials, query or fragment; most preferred first |
| `website`, `community` | string or null | optional `https` links; `community` is the community's own chat (any messenger) |
| `digests` | object | 1 to 64 entries: path relative to a mirror base URL (no `..`, no leading `/` or `\`) to the lowercase hex SHA-256 of that file |

Unknown fields are ignored, so a newer registry can add optional fields without breaking an older
launcher. A change that an older launcher must not misread bumps `schema`.

## index.json

An envelope that carries the signed bytes as they were signed, so nothing is re-serialised or
canonicalised before verification:

```json
{ "keyId": "r1", "signature": "<base64>", "payload": "<base64>" }
```

- `payload` is the base64 of the UTF-8 JSON document below.
- `signature` is ECDSA P-256 over the payload bytes with SHA-256, DER encoded (what
  `openssl dgst -sha256 -sign` produces).
- `keyId` names one of the public keys pinned in the launcher; an unknown id is rejected.

The payload (see [index.payload.json](registry-samples/index.payload.json)):

| Field | Rules |
|-------|-------|
| `schema` | integer; the launcher refuses any value it does not support, and only looks at it after the signature has verified |
| `serial` | positive integer, higher with every issued index |
| `issuedAt`, `expiresAt` | UTC, exactly `yyyy-MM-ddTHH:mm:ssZ`; `expiresAt` after `issuedAt` by at most 31 days |
| `minLauncher` | version; older launchers show "update required" |
| `servers` | up to 500 entries, same fields as the entry files, unique ids |

The envelope is at most 1 MiB.

## What the reader checks

Read in this order: envelope shape, key id, signature, then the payload. Failures are reported as
`RegistryException` with a `RegistryFailure`: `Malformed`, `UnknownKey`, `BadSignature`,
`UnsupportedSchema` or `Invalid`. Expiry and serial rollback belong to fetching and caching, and
come with the index fetch (`IsExpired` is already on the index).
