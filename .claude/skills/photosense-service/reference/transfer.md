# Sending photos from a phone to a computer

## How it works

- **Who connects:** the phone waits (`TcpTransferListener`, port 47615 or any free port), and the computer connects.
- **Finding the phone on Wi-Fi:** the phone says it is there with Bonjour, `_photosense._tcp` with TXT `id` and `name` (`BonjourAdvertiser` on iOS, `NsdAdvertiser` on Android). The computer asks with `MdnsPhoneFinder`.
- **Finding it on a cable:** the computer finds an iPhone through Apple's device service with `UsbmuxPhoneFinder`: 127.0.0.1:27015 on Windows, `/var/run/usbmuxd` on a Mac. There is no cable route for Android.
- **Messages:** `Wire` writes frames, each a four-byte length then the bytes. A message is JSON with what it is in `t`.
- **Pairing** (`Pairing`):
  - Each connection makes a new key and agrees a secret from it (ECDH, P-256).
  - The phone commits to its key before it sees the computer's nonce.
  - The first time, both show a six-digit `Code` and the person confirms it on each. They then keep a secret (`IPairingStore`) and prove it to each other on every later connection.
- **Sealed:** after pairing, everything is sealed by `SecureChannel` (AES-GCM, a key for each direction, a counter as the nonce).
- **Checked:** each file is checked by SHA-256. The computer writes it under `.{name}.{guid}.photosense-part` and gives it its own name (`FreePlace`) only once whole. A file identical to one already there is not written again.
- **Phone side:** `PhoneSending`. Its states are `Idle`, `Waiting`, `Comparing`, `Sending`, `Sent` and `Failed`, plus `Deletable`. `DeleteSent` erases only the units the computer confirmed whole, through `IPhotoChangesTogether.EraseAll`. This is rule 2's one exception.
- **Computer side:** `PhoneReceiving`. Its states are `Connecting`, `Comparing`, `Receiving`, `Done` and `Failed`. `Plain(name)` makes a phone's name a plain file name.
- **Endpoints:** in `TransferEndpoints`. The phone has `send/*`; the computer has `phones` and `receive/*`. Each program answers its own half and 404s the other.

## Tests

- `TransferTests` runs a phone's whole stack and a computer's receiving against each other over loopback. It also runs each against a device of the test's own that says what it likes, for broken messages, damaged files and refusals.
- `PairingTests` covers `Pairing`, `Wire` and `SecureChannel`.
- `TransferLinkTests` covers the listener, mDNS with packets built by the test, and usbmux through `ScriptedStream`.

## Checked by hand so far

- **Done:** Windows' own mDNS answering for a stand-in phone, and Receive from phone in Chrome end to end. The stand-in phone was a console program using `Pairing`, with Windows' `DnsServiceRegister` from `dnsapi.dll`.
- **Never tried:** a real phone, the cable, iOS's local-network question, or a locked screen.
