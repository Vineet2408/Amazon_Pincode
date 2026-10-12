# Trusted release signing

No code-signing certificate with an accessible private key was found in the CurrentUser/My or LocalMachine/My certificate stores. Microsoft SignTool and Azure CLI were not found in the checked locations. The app remains unsigned; the Microsoft WebView2 DLLs have valid signatures.

To finish, supply the name of your existing certificate provider or cloud signing service. Do not put private keys, token PINs, passwords or service credentials into chat. Certificate issuance generally requires publisher identity verification and may have a cost; no account has been created or purchase made.

For a certificate exposed in the Windows certificate store by its provider, `sign-release.ps1` accepts the certificate thumbprint and a Microsoft-signed Windows SDK SignTool path. It validates the certificate, rebuilds the app, stages only release files, signs the EXE with SHA-256 and a timestamp, verifies every EXE/DLL, and creates a checksum manifest. Token PIN entry may be requested by the provider. No private key is exported or included in the release.

For cloud services whose certificates are not exposed in the Windows certificate store, the signing step needs the provider-specific integration once that provider is identified.

The script has been syntax-checked, but actual signing has not been run because no signing identity is available. A valid signature identifies the publisher and protects integrity; Windows application policy and antivirus still determine whether the application can run. The previous separate test executable's security warning remains unresolved.

Microsoft references:
- https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/code-signing-for-smart-app-control
- https://learn.microsoft.com/en-us/windows/win32/seccrypto/signtool
