# Sending photos from a phone to a computer

## How it works

- **Backup (since 2026-11-13; Jamie: the phone connects to the computer):** the computer waits for phones while it runs (`ComputerPairing` with its `PhoneDoor`, port 47616 or any free one, `MdnsAdvertiser` answering `_photosense-pc._tcp`; started by the server's `OpenToPhones`). A phone it knows is handed to `ComputerBackup.ServeAsync`; one it does not know is turned away (`Pairing.ComputerAsync(..., strangers: false, ...)`) unless Pair a phone is open. The phone (`PhoneBackup`) finds the computer by id (`IComputerFinder`), proves the pairing, says `backup`; the computer checks what it holds (`IBackupStore`, re-hashing a file whose size or time changed), sends `held` checksums in parts (`Lists`, `ComputerBackup.ListPart`), takes the `offer` in parts, and `get`s each file; `got` carries the checksum; the phone keeps which checksums the computer holds (`IBackupLedger`). Safe to delete (`PhoneBackup.RemoveAsync`) erases only items every file of which is held, through `IPhotoChangesTogether.EraseAll`, counted by `DailyRemovals`.
- **Who connects, for the old Send:** the phone waits (`TcpTransferListener`, port 47615 or any free port), and the computer connects. The page no longer offers it.
- **Finding the phone on Wi-Fi:** the phone says it is there with Bonjour, `_photosense._tcp` with TXT `id` and `name` (`BonjourAdvertiser` on iOS, `NsdAdvertiser` on Android). The computer asks with `MdnsPhoneFinder`.
- **Finding it on a cable:** the computer finds an iPhone through Apple's device service with `UsbmuxPhoneFinder`: 127.0.0.1:27015 on Windows, `/var/run/usbmuxd` on a Mac. There is no cable route for Android.
- **Messages:** `Wire` writes frames, each a four-byte length then the bytes. A message is JSON with what it is in `t`.
- **Pairing** (`Pairing`):
  - Each connection makes a new key and agrees a secret from it (ECDH, P-256).
  - The phone commits to its key before it sees the computer's nonce.
  - The first time, both show a six-digit `Code` and the person confirms it on each. They then keep a secret (`IPairingStore`) and prove it to each other on every later connection.
- **Sealed:** after pairing, everything is sealed by `SecureChannel` (AES-GCM, a key for each direction, a counter as the nonce).
- **Time limits** (`TimedConnection`): each side wraps its connection so that a read or write the other side leaves silent for `Silence` (60 s) throws `TransferException` "The other device stopped answering. Try again."; connecting has the same limit (`WithinAsync`). The two waits on the person comparing numbers read through `TimedConnection.Unlimited`. Work of a side's own that can take long (the phone opening a file, which may come from iCloud; the computer hashing a file already there) runs in `SecureChannel.MeanwhileAsync`, which sends `{"t":"wait"}` every 20 s; `ReceiveAsync` passes those over. Each runner takes a `TimeProvider` for this.
- **Ending:** `RunAsync` and `ServeAsync` set the problem in `finally`, so no error of any kind leaves `Receiving` or `Sending` standing. The computer writes arriving files through to the disk (`DiskFileSystem.CreateNew` with `WriteThrough`) before saying "got".
- **Checked:** each file is checked by SHA-256. The computer writes it under `.{name}.{guid}.photosense-part` and gives it its own name (`FreePlace`) only once whole. A file identical to one already there is not written again.
- **Phone side of the old Send:** `PhoneSending`. Its states are `Idle`, `Waiting`, `Comparing`, `Sending`, `Sent` and `Failed`, plus `Deletable`. `DeleteSent` erases only the units the computer confirmed whole, through `IPhotoChangesTogether.EraseAll`, as Safe to delete does: rule 2's exception.
- **Computer side:** `PhoneReceiving`. Its states are `Connecting`, `Comparing`, `Receiving`, `Done` and `Failed`. `Plain(name)` makes a phone's name a plain file name.
- **Endpoints:** `BackupEndpoints` (`backup`, `backup/*` on a phone; `backup/phones*` on a computer) and `PairingEndpoints`; the old `TransferEndpoints` (`send/*`; `phones`, `receive/*`). Each program answers its own half and 404s the other.

## Tests

- `BackupTests` runs a phone's whole stack against a computer's door and backups over loopback, and against a computer of the test's own (`ScriptedComputerAsync`); `ComputerBackupTests` runs `ComputerBackup.ServeAsync` against a phone of the test's own, over two `SecureChannel`s made with keys of the test's own. A test that plays one end must send every message the other waits for, or the other waits for ever: a raw stream has no minute's limit.
- `TransferTests` runs a phone's whole stack and a computer's receiving against each other over loopback. It also runs each against a device of the test's own that says what it likes, for broken messages, damaged files and refusals.
- `PairingTests` covers `Pairing`, `Wire` and `SecureChannel`.
- `TransferLinkTests` covers the listener, mDNS with packets built by the test, and usbmux through `ScriptedStream`.
- `TimedConnectionTests` covers the connection's limit and the "wait" message. A silence is tested with `HurriedClock.Minute`, on which a minute passes in two seconds; give it to only the side whose patience is being tested, and to both only when the test is of "wait" (`FakePhotoLibrary.Fetching` makes a fetch slow, or fail).

## Checked by hand so far

- **Done:** Windows' own mDNS answering for a stand-in phone, and Receive from phone in Chrome end to end. The stand-in phone was a console program using `Pairing`, with Windows' `DnsServiceRegister` from `dnsapi.dll`.
- **Never tried:** a real phone, the cable, iOS's local-network question, or a locked screen.
