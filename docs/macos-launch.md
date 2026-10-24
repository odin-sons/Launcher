# macOS launch modes

The modded game on macOS runs in one of two modes. The launcher picks the mode on every launch.

## Native arm64

Used only when all of these hold:

- the host is Apple Silicon;
- the profile ships the universal Doorstop library, `libdoorstop.dylib` (UnityDoorstop 4.6.0 or
  newer), either at the profile root or in `doorstop_libs/`;
- the game executable has an arm64 slice;
- x86_64 is not forced (see below).

The game is started the way the official `run_bepinex.sh` does it:

```
arch -e DYLD_LIBRARY_PATH=<library folder> -e DYLD_INSERT_LIBRARIES=<libdoorstop.dylib>[:<Steam overlay libraries>] <executable> <doorstop arguments>
```

with `ARCHPREFERENCE=arm64,x86_64` in the environment. The `DYLD_*` variables go through `arch -e`
because dyld strips them across a platform binary. The Steam app id and the Steam overlay
libraries and variables are passed as in the x86_64 mode. Rosetta is not needed, so the Rosetta
check is skipped.

## x86_64 under Rosetta

Everything else — a profile that only has `doorstop_libs/libdoorstop_x64.dylib`, an Intel Mac, an
executable without an arm64 slice, or a forced x86_64 launch:

```
arch -x86_64 /usr/bin/env KEY=VALUE... <executable> <doorstop arguments>
```

The x86_64 library is `doorstop_libs/libdoorstop_x64.dylib`; if the profile has only the universal
`libdoorstop.dylib`, its x86_64 slice is used instead. On Apple Silicon the launcher first checks that Rosetta 2 is installed and shows install
instructions if it is not.

## Forcing x86_64

Set the environment variable `ODINSONS_FORCE_X64=1` before starting the launcher. Code that builds
the launch plan can also pass `forceX64: true` to `InjectorLauncher.BuildPlan` /
`InjectorLauncher.TryPrepareLaunch`.

## Status

Native mode has not been verified on real hardware yet; see issue #34 for the open questions
(Harmony/MonoMod detours, native mod libraries). It activates only for profiles that explicitly
contain the universal library.
