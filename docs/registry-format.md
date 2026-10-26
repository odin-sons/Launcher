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
`UnsupportedSchema` or `Invalid`.

## Fetching and caching

`RegistryClient` requests `<mirror>/index.json` from each registry mirror in order and accepts the
first index that passes the reader and is also:

- not expired (`Expired`);
- not older than the highest serial ever accepted (`Rollback`);
- usable by this launcher version (`LauncherTooOld`, which wins when it is the reason nothing was
  accepted, so the player is told to update).

A mirror that is down, slow or answers with garbage is skipped (`Unreachable`, `Malformed`, ...).
Requests retry on 429 and 5xx, time out after 15 seconds and read at most 1 MiB.

The accepted envelope is stored in the cache folder next to `serial.txt`, the serial floor. When no
mirror gives an acceptable index, the cached envelope is verified again against the pinned keys and
used only while it has not expired. The result says whether the index came from the network or the
cache, and lists why each mirror was passed over.

## Choosing a server

The launcher lists the built-in profile first, then every community the registry lists, by name.
`needs-approval` profiles show up only when already installed, `suspended` ones never. The choice is
kept per player as `SelectedProfile` in `config.ini`, and a profile that is no longer offered falls
back to the built-in one. At startup the list comes from the cached index, so the launcher opens
without waiting for the network; the registry is refreshed in the background. Each community keeps
publishing its own game servers in `servers.json`, which feed the server dropdown as before.
Switching profiles reopens the window with the new profile.
