# Custom cloud provider credential setup

This guide explains how to create the **custom provider credentials** that can be entered in **Application Settings** when **Use custom provider credentials** is enabled.

> [!IMPORTANT]
> The current branch only stores the provider values in settings. It does **not** yet complete cloud login, token refresh, folder browsing, or cloud file transfer.

## What UsbFileSync currently stores

The current settings UI stores:

- **Google Drive**: OAuth client ID
- **Dropbox**: app key / OAuth client ID
- **OneDrive**: application (client) ID
- **OneDrive tenant**: optional tenant ID override

The current UI does **not** ask for a client secret. If a provider portal also shows a client secret, do **not** paste it into UsbFileSync because there is no field for it on this branch.

## Before you begin

1. Open **Application Settings** in UsbFileSync.
2. Decide whether to keep the default built-in mode or turn on **Use custom provider credentials**.
3. If you turn the custom mode on, keep this guide open and copy only the values that match the available fields in UsbFileSync.
4. Provider portals change over time, so if a label moves slightly, look for the same concepts described below.

---

## Google Drive

### What you need from Google

- **OAuth client ID**

### Step-by-step

1. Go to the **Google Cloud Console** at `https://console.cloud.google.com/`.
2. Create a new project, or select an existing project that you want to use for UsbFileSync.
3. Open **APIs & Services**.
4. Open **Library**.
5. Search for **Google Drive API**.
6. Select **Google Drive API** and choose **Enable**.
7. Go back to **APIs & Services** and open **OAuth consent screen**.
8. Configure the consent screen for your project:
   - Choose the user type that fits your account
   - Enter the required app name and contact information
   - Save the consent screen settings
9. Open **APIs & Services** → **Credentials**.
10. Select **Create credentials** → **OAuth client ID**.
11. If prompted, finish any remaining consent screen steps.
12. For the application type, choose **Desktop app**.
13. Enter a name such as `UsbFileSync Desktop`.
14. Create the client.
15. Copy the **Client ID** shown by Google.
16. In UsbFileSync, paste that value into the **Google Drive** client ID field.

### Where to find it later

1. Return to **Google Cloud Console**.
2. Open **APIs & Services** → **Credentials**.
3. Open the OAuth client you created.
4. Copy the **Client ID** value again if you need it later.

### Notes

- Google may also show a client secret, but the current UsbFileSync settings page does not store it.
- If Google warns that the app is still in testing, that is expected until the OAuth consent screen is fully published in your own project.

---

## Dropbox

### What you need from Dropbox

- **App key** (this is the value to use as the client ID in UsbFileSync)

### Step-by-step

1. Go to the **Dropbox App Console** at `https://www.dropbox.com/developers/apps`.
2. Choose **Create app**.
3. Select **Scoped access**.
4. Choose the access type that matches your future use:
   - **App folder** for a folder limited to your app
   - **Full Dropbox** for broader account access
5. Enter an app name and create the app.
6. Open the new app in the App Console.
7. Review the **Permissions** section and enable the scopes you expect to need later.
8. Open the app **Settings** or overview page.
9. Copy the **App key**.
10. In UsbFileSync, paste the **App key** into the **Dropbox** client ID field.

### Where to find it later

1. Return to the **Dropbox App Console**.
2. Open your app.
3. On the app overview/settings page, copy the **App key** again.

### Notes

- Dropbox may also show an **App secret**, but the current UsbFileSync settings page does not store it.
- If Dropbox asks for redirect URIs during your own testing, configure them in the Dropbox app console, but only the **App key** is stored by this branch today.

---

## OneDrive

OneDrive custom credentials are managed through a **Microsoft Entra ID app registration**.

### What you need from Microsoft

- **Application (client) ID**
- **Directory (tenant) ID** only if you want to override the default tenant behavior

If you leave the tenant field blank in UsbFileSync, the app stores **`common`**.

### Step-by-step

1. Go to the **Azure portal** at `https://portal.azure.com/`.
2. Open **Microsoft Entra ID**.
3. Open **App registrations**.
4. Select **New registration**.
5. Enter a name such as `UsbFileSync`.
6. Choose the account type that matches your needs:
   - If you want the broadest compatibility, choose the option that allows both organizational directories and personal Microsoft accounts
   - If you only want your own tenant, choose the single-tenant option
7. Create the app registration.
8. After the app opens, stay on the **Overview** page.
9. Copy **Application (client) ID**.
10. Copy **Directory (tenant) ID** if you plan to use a fixed tenant.
11. In UsbFileSync:
    - Paste **Application (client) ID** into the **OneDrive** client ID field
    - Paste **Directory (tenant) ID** into the tenant field only if you want to force a specific tenant
12. If you want the default multi-tenant behavior instead, leave the tenant field empty so UsbFileSync stores `common`.

### Where to find it later

1. Return to **Azure portal**.
2. Open **Microsoft Entra ID** → **App registrations**.
3. Open your app registration.
4. On **Overview**, copy:
   - **Application (client) ID**
   - **Directory (tenant) ID**

### Notes

- For consumer and multi-tenant scenarios, leaving the tenant blank in UsbFileSync is usually the simplest option because the app stores `common`.
- If you use a single-tenant app registration, copy that tenant's **Directory (tenant) ID** into the OneDrive tenant field.
- Microsoft may also ask you to configure API permissions and redirect URIs for your own app registration. Those are part of the provider setup, but the current UsbFileSync settings page only stores the client ID and optional tenant override.

---

## Entering the values in UsbFileSync

1. Open **Application Settings**.
2. Turn on **Use custom provider credentials**.
3. Paste the values you collected:
   - **Google Drive** → Google **Client ID**
   - **Dropbox** → Dropbox **App key**
   - **OneDrive** → Microsoft **Application (client) ID**
   - **OneDrive tenant** → Microsoft **Directory (tenant) ID**, or leave it blank to use `common`
4. Save the settings.

## Troubleshooting

- **I only see a client secret, not a client ID**  
  Go back to the app overview/credentials page. UsbFileSync currently wants the provider's **client ID** value, not the secret.

- **I am not sure whether to enter a OneDrive tenant**  
  Leave it blank unless you specifically need to lock the login flow to one tenant. Blank stores `common`.

- **The provider portal does not exactly match these labels**  
  Provider dashboards are updated often. Look for the equivalent terms:
  - Google: **Client ID**
  - Dropbox: **App key**
  - Microsoft: **Application (client) ID** and optionally **Directory (tenant) ID**
