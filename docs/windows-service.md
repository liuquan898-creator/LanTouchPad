# Windows service (PC 1.0.4)

The installer registers the `LanTouchPad` service as LocalSystem with automatic startup. The service starts before a user signs in. The existing per-user logon task now opens only the tray management UI; the UI does not own the network listener when the service is installed.

Session 0 cannot send input to the sign-in screen. The service therefore duplicates its own SYSTEM token into the active physical console session and launches the installed executable with `--session-receiver` on `winsta0\default`. It waits for a console session if none is available and replaces the receiver when the session changes or the receiver exits. A job object terminates the child if the service exits unexpectedly. A SYSTEM-owned event permits graceful shutdown and releases held input before stopping.

Each controller has a dedicated input thread with no windows or hooks. Before processing input it opens the current input desktop with full desktop access, changes its thread desktop when necessary, and releases held buttons and fractional movement during a desktop change. This permits ordinary, lock and sign-in desktops without disabling UAC, enabling automatic logon or storing a Windows password. The mobile protocol and APK do not change.

The service and its executable are installed in Program Files. Normal users cannot replace that executable or reconfigure the service. Existing LAN-only firewall and private-address restrictions still apply. Authentication is still disabled as requested, including at the sign-in screen; use only a trusted LAN. No entered text or passwords are logged.

## Management

- The tray UI uses loopback-only `/receiver/state`, `/receiver/pause` and `/receiver/resume` endpoints. Browser Origin requests are refused. Pause cancels the active controller and rejects new control connections while leaving the local management listener available.
- Closing the window keeps the service running. The tray command to exit and stop receiving stops the service too.
- Disabling automatic startup changes the service to manual startup and removes the current user's tray task. A running service remains active until explicitly stopped or Windows shuts down.
- The installer stops the service before upgrading its files. Uninstall stops and deletes the service and removes the current user's tray task and firewall rules.

## Verification

Build with `scripts/build-windows.ps1`. Run `scripts/verify-windows-service.ps1` as administrator after installing. It checks automatic startup, LocalSystem identity, a receiver in a nonzero interactive session, full access to the Winlogon desktop without switching the screen, child cleanup on service stop and recovery after service restart. Results go to `dist/service-report.json`.

`tests/DesktopSmoke --isolated --automated` uses a separate loopback receiver and temporary input window to check input and management endpoints. Omit `--isolated` to test the installed receiver after disconnecting any phone controller. It does not lock, log out, reboot or type into existing application windows.

Automatic startup configuration and service restart can be verified without rebooting. Actual cold-boot discovery and password/PIN entry at the Windows sign-in screen still require a reboot or lock-screen acceptance test. Machines requiring Ctrl+Alt+Delete need that secure attention sequence through a supported Windows mechanism; this version does not change the policy for software-generated SAS. BitLocker pre-boot prompts and RDP sessions are not supported.
