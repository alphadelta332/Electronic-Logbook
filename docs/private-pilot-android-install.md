# Install The FlightLogX Android Preview

Status: participant-facing Preview instructions

Last checked: 2026-08-31

FlightLogX invitation-only Preview builds are distributed by Firebase App Distribution rather
than Google Play. Android therefore treats the APK as an app from outside the Play
Store and may show strong security warnings. These warnings are expected only when all
of the checks below match. If anything differs, stop and contact the FlightLogX Preview
owner.

## Before You Start

Confirm all of these details:

- You received a Firebase App Distribution invitation from the FlightLogX Preview owner.
- The tester page address begins with `https://distribution.firebase.google.com/`.
- The page shows **FlightLogX** and package
  `com.alphadelta.electroniclogbook`.
- You are signed in with the Google Account that accepted the invitation.
- You understand that a disposable test build must not contain real logbook data.

Never install a FlightLogX APK sent as an ordinary email attachment, chat attachment,
cloud-storage link, or unrelated website download.

## First Installation

1. On the Android phone, open the Firebase invitation email and select **Get started**.
2. Sign in with the invited Google Account and accept the invitation.
3. On the FlightLogX release page, select **Download**.
4. A grey **Download started...** button is not a progress indicator. Swipe down from
   the top of the phone and use the Chrome/Download Manager notification. If there is
   no notification, open Chrome's three-dot menu and select **Downloads**.
5. Chrome may warn that the APK or file might be harmful or dangerous. Recheck the
   Firebase address, FlightLogX name, and package above. Only then select **Download
   anyway**, **Keep**, or the equivalent confirmation shown by that Android version.
6. Open the downloaded `app-pilot.apk` file.
7. If Android says Chrome or Firebase App Tester cannot install unknown apps, select
   **Settings**, turn on **Allow from this source** for the app that downloaded the APK,
   return to the installer, and select **Install**.
8. Open FlightLogX. Do not enter real logbook data until the Preview owner confirms that
   the build and account are ready.
9. For an ordinary tester, turn the temporary installation permission back off:
   **Settings > Apps > Special app access > Install unknown apps**, select Chrome or
   Firebase App Tester, then turn off **Allow from this source**. Menu names can differ
   slightly between Android manufacturers. The owner may leave it enabled only during an
   explicitly active update-rehearsal session, then must turn it off when that session ends.

Firebase App Tester is optional. It can collect Preview releases in one place,
but installing it does not remove Android's unknown-app permission requirement.

## Stop Instead Of Continuing When

Contact the FlightLogX Preview owner and do not uninstall or clear any existing FlightLogX
app if:

- the download comes from any domain other than `distribution.firebase.google.com`;
- the app name or package does not match the values above;
- Android reports **App not installed**, a certificate conflict, or a package conflict;
- Android presents an **unverified developer** advanced flow, Developer options steps,
  or a 24-hour security delay; or
- the installed app opens an unexpected empty logbook where existing data was expected.

Do not work around these failures by removing an existing app. The Preview owner must
first check the package, signing certificate, account, and recoverability.

## Later Preview Updates

Use **Settings > Check for Preview update** inside FlightLogX. Android will still require
you to approve installation of the downloaded update. If **Allow from this source** was
turned off after the previous install, Android may ask you to enable it again; turn it
back off after the update completes. Android grants this permission separately to each
source app, so permission previously granted to Chrome may not cover an update downloaded
by FlightLogX itself.
