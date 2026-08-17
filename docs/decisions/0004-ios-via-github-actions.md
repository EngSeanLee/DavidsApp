# 0004 — iOS target, built via GitHub Actions (not locally)

**Status:** verified — CI produces a real unsigned .app/.ipa. Sideloadly install itself (the
user's own machine, their own Apple ID) is still unverified, as it always will be from here.

## Context

The build spec assumed an Android tablet as the field device, and 0001 explicitly dropped iOS/
macCatalyst from the client's `TargetFrameworks` on that basis. The user's actual personal test
device turned out to be an iPhone. iOS support was added after the fact, not as part of the
original plan.

## Decision

- Re-added `net10.0-ios` to `DavidsApp.Client.csproj`'s `TargetFrameworks`.
- Restored `Platforms/iOS/` (AppDelegate.cs, Program.cs, Info.plist, PrivacyInfo.xcprivacy) by
  scaffolding a throwaway `dotnet new maui` project elsewhere and copying its default iOS files in,
  since these were deleted in 0001's cleanup and needed to be regenerated rather than hand-written
  from memory.
- Added `Platforms/iOS/Speech/IosContinuousSpeechRecognizer.cs` (Speech framework +
  `AVAudioEngine`), following the same `IContinuousSpeechRecognizer` contract as the Android/
  Windows implementations. Restart-on-completion loop, same principle as Android's
  restart-on-silence-timeout — iOS recognition tasks end after each final result even though there's
  no ~5s wall-clock timeout forcing it.
- Added `NSMicrophoneUsageDescription` / `NSSpeechRecognitionUsageDescription` to Info.plist —
  required strings; iOS crashes the process on the permission prompt if either is missing, rather
  than denying gracefully.
- **This machine cannot build iOS** — Apple requires Xcode, which requires macOS, regardless of
  which cross-platform framework is used; this is not a MAUI limitation. Building happens on a
  GitHub-hosted macOS runner instead: `.github/workflows/build-ios.yml`, triggered manually or on
  a push touching `src/client/**`.
- The user has a free Apple ID, not a paid Apple Developer Program membership ($99/year). Ad-hoc/
  TestFlight distribution needs a paid account, so that path isn't available. Instead:
  1. The CI workflow builds a **throwaway-signed** `.app`. Empty `CodesignKey`/`CodesignProvision`
     turned out not to skip signing — the .NET-for-iOS device-RID build pipeline hard-fails before
     it even reaches the codesign step if the keychain contains zero codesigning identities
     ("No valid iOS code signing keys found in keychain"), verified against the first real Actions
     run. Since Sideloadly fully strips and replaces whatever signature CI produces anyway, the fix
     is a throwaway self-signed identity generated and imported into the runner's keychain at build
     time (openssl → `security import`), just to satisfy that gate — not a real Apple-issued cert.
     The resulting `.app` is then manually zipped into an IPA-shaped archive (`Payload/App.app`
     inside a `.zip` renamed `.ipa`) — this is the input format tools like **Sideloadly** and
     AltStore expect for self-signing, and is a more CI-reliable path than trying to get .NET's own
     `BuildIpa`/`ArchiveOnBuild` MSBuild targets to produce a real signed archive without valid
     Apple credentials in the pipeline.
  2. The user downloads that IPA from the Actions run and signs + installs it themselves via
     Sideloadly (runs on Windows too) using their own Apple ID — Claude never sees or handles
     those credentials, matching the "never enter passwords" rule regardless of whose account.
  3. **Known consequence of the free-tier path**: Apple's free personal-team signing expires
     installed apps after 7 days. Re-signing (re-running Sideloadly against the same or a fresh
     IPA) is a recurring manual step until/unless a paid developer account exists.

## Consequences

- **Verified 2026-08-16, five Actions runs to get a clean build**, each failure real and specific,
  not guessed away in advance:
  1. `CodesignKey=""`/`CodesignProvision=""` do **not** disable signing — a device-RID build hard-
     fails before reaching codesign if the keychain has zero codesigning identities at all
     ("No valid iOS code signing keys found in keychain"). Fixed by generating a throwaway self-
     signed identity in the runner's keychain at build time and pointing `CodesignKey` at it —
     irrelevant to the final result since Sideloadly fully replaces the signature anyway.
  2. `openssl pkcs12 -export`'s modern default (AES-256/SHA-256) isn't readable by macOS Keychain
     Services' importer — `-legacy` fixes it.
  3. A device build separately requires an actual provisioning profile — fixed with
     `-p:CodesignRequireProvisioningProfile=false` plus clearing `CodesignEntitlements`.
  4. One real C# compile error caught for the first time (`AVAudioEngine.Running`, not `IsRunning`).
  5. Clean build, IPA packaged, artifact uploaded (19.5 MB).
- The successful run still logs warnings ("app requests the entitlement 'keychain-access-groups'/
  'application-identifier', but no provisioning profile has been specified") — expected and
  harmless here, since Sideloadly discards whatever entitlements/signature CI produced and
  generates its own from the user's real Apple ID.
- Sideloadly install itself — on the user's own Windows machine, with their own Apple ID — is the
  one part of this pipeline that can't be verified from here at all.
- Every 7 days, the sideloaded app stops opening until re-signed — this is Apple's platform rule,
  not something fixable from this codebase.
