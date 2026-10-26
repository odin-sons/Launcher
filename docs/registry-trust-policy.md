# Server registry: threat model and trust policy

A modpack is executable code on the player's machine. The registry decides which communities'
modpacks the launcher offers, so it is a trust anchor: whoever controls it can run code on every
player's PC. This document fixes the model before the registry is built.

## Decision: one official registry

The launcher supports exactly one registry, the official one, whose public key is compiled into the
launcher. There is no setting, config key, command-line option or environment variable that adds,
replaces or points to another registry.

Reasons:

* A user-supplied registry turns "add this link to get the server" into a one-step malware delivery
  channel. A warning dialog does not change that for the people most likely to click through it.
* Support gets simpler: every report refers to the same index, and a bad entry has one place to be
  fixed.
* Decentralisation is kept at the source level: the registry is a public git repository, anyone can
  fork it and ship a launcher build that pins their own key. That is a different product with its own
  trust owner, not a mode of this one.

A community that is not in the registry cannot be added to the launcher.

## What a signature guarantees

The index is signed with an Ed25519 key. The launcher pins the public key(s) and verifies the
signature before it reads anything else from the index.

Guaranteed:

* The index was produced by the holder of the registry signing key.
* The index was not altered in transit or by a mirror. Mirrors may be untrusted, which is why the
  index is served from several of them.
* Each entry carries the digest of its content; a profile or manifest that does not match the
  digest in a verified index is rejected.

Not guaranteed:

* That a listed server is safe. The signature says "the registry published this", not "the registry
  audited this". Vetting is a process (below), and it has gaps.
* That the index is the newest one. A signature alone does not stop replay (see "Expiry").
* Anything about the content of a community's files after the digest was recorded (see "Time of
  check vs time of use").

## Key pinning and rotation

* The launcher ships a list of accepted public keys, each with a key id. The signed index names the
  key id that signed it.
* Rotation is done by shipping a launcher build that accepts the new key in addition to the old
  one, then signing with the new key, then removing the old key in a later build. A signed index
  never delivers a new trusted key by itself; otherwise one leaked key could be used to install
  another.
* Compromise: the leaked key is removed from the next launcher build. Players on older builds keep
  trusting it until they update, so the launcher's own update check (already signed by the
  existing release process) is the revocation channel. The index also carries a `minLauncher`
  version; a launcher older than that shows "update required" instead of using the index.
* The private key is held offline by the owner and is not available to CI. CI builds and validates
  the index; signing is a manual step, or uses a hardware-backed key.

## Expiry and replay

An attacker who can serve files (a compromised mirror, a network position) could replay an old,
validly signed index, for example one that still lists a server that was later removed as malicious.

* Every index has `issuedAt` and `expiresAt`, inside the signed part. The validity window is short
  (days, not months) and the index is re-signed on a schedule even when nothing changed.
* The launcher remembers the highest `serial` it has accepted and rejects an index with a lower one.
* An expired index is not used to add or update anything. The launcher keeps working with the last
  verified cache for already-installed profiles, marks the registry as "stale" in the UI, and does
  not install or update from stale data. Install and update need a fresh index, so a removal
  published in a fresh index always wins.
* Mirrors disagreeing is resolved by taking the verified index with the highest serial.

## Vetting and what VirusTotal can and cannot catch

Submissions are pull requests against the registry repository. CI checks the structure and the
links, then submits every referenced archive and executable to VirusTotal.

VirusTotal is a signal, not a verdict:

* It does catch known malware and commodity droppers.
* It does not catch new or targeted code, logic that only triggers later or on specific
  conditions, or a clean loader that downloads its payload afterwards.
* It produces false positives on this ecosystem. BepInEx, Doorstop and several injectors are flagged
  by a few engines because they do what malware does (load code into a process). A raw "any engine
  flags it" rule would reject everything, so the rule is a threshold plus a list of known-benign
  hashes (the BepInEx and Doorstop builds the launcher already ships), reviewed by a human.
* A scan is a snapshot. Files change after approval.

Human review therefore stays in the loop: a maintainer reads the diff, checks who the community is,
and looks at what the mods are, before approving. Automation narrows what the reviewer must read; it
does not replace the reader.

## Time of check vs time of use

A server operator can pass review with a clean modpack and later swap the files on their own host.

* The index stores a digest for each profile and for the modpack manifest. The launcher refuses a
  profile whose digest differs from the one in the verified index. Changing the modpack means a new
  registry entry, a new review and a new signature.
* Individual files are covered by the manifest's SHA-256 values, which the launcher already checks
  on download. The manifest is therefore the unit that is pinned.
* A scheduled re-scan re-submits every approved entry. A changed verdict moves the entry to
  `needs-approval` in the next index, which the launcher honours as soon as it fetches that index.
* Remaining window: between a swap and the next index. The window is bounded by the index validity
  period, and a swap alone does not even take effect, because the digest check fails.

## Launcher behaviour per status

| Status | Meaning | Launcher behaviour |
|--------|---------|--------------------|
| `approved` | Reviewed, scans clean at the last check | Listed normally. Install and update allowed. Trust badge shown. |
| `needs-approval` | Previously approved; a re-scan or a change needs a new review | Not listed for new installs. If already installed: the game can still be started from the existing files, updates are paused, and a clear notice explains why. |
| `suspended` | Removed by the registry for a security reason | Not listed. If installed: the launcher does not start the modded game from that profile, tells the player why, and offers to remove the files. |

The trust badge is drawn by the launcher and is not themable, so a community's theme cannot imitate
it.

## What this model does not defend against

* A malicious but well-disguised mod that passes review. Mitigations are the manifest pinning, the
  community's reputation and re-scans, not a guarantee.
* Compromise of the owner's signing key together with a mirror at the same time, before a launcher
  update ships. Offline key storage and the short validity window shrink this, they do not remove it.
* A compromised player machine, or a player who turns the checks off by running a modified launcher.

## Open points

* Exact threshold and the known-benign hash list for VirusTotal results.
* Whether the signing key is a hardware token or an offline key file.
* Whether a way to try a not-yet-listed community (for its own operator while the PR is open) is needed, and how to keep it from becoming a side door.
