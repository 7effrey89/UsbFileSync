# Custom cloud provider credential setup

This guide explains how to create the **custom provider credentials** that can be entered in **Application Settings** when **Use custom provider credentials** is enabled.

> [!IMPORTANT]
> UsbFileSync now supports **Google Drive as a source volume** when custom provider credentials are enabled. You can browse a Google Drive folder, select it as the source, analyze it, and sync it down to local destinations. Google Drive is still **not** supported as a destination on this branch.

## What UsbFileSync currently stores

The current settings UI stores:

- **Google Drive**: OAuth client ID
- **Google Drive client secret**: optional, only if your Google OAuth client requires it during token exchange
- **Dropbox**: app key / OAuth client ID
- **OneDrive**: application (client) ID
- **OneDrive tenant**: optional tenant ID override

The current UI can now store an optional **Google Drive client secret** when a Google OAuth client requires it. Dropbox and OneDrive still use only the fields listed above.

## Before you begin

1. Open **Application Settings** in UsbFileSync.
2. Decide whether to keep the default built-in mode or turn on **Use custom provider credentials**.
3. If you turn the custom mode on, keep this guide open and copy only the values that match the available fields in UsbFileSync.
4. Provider portals change over time, so if a label moves slightly, look for the same concepts described below.

---

## Google Drive

### What you need from Google

- **OAuth client ID**
- **Optional client secret** if your Google OAuth client requires one

### Step-by-step

1. Go to the **Google Cloud Console** at `https://console.cloud.google.com/`.
2. Create a new project, or select the project you want to use for UsbFileSync.
3. Open **APIs & Services** → **Library**.
4. Search for **Google Drive API**.
5. Open **Google Drive API** and choose **Enable**.
6. Open **APIs & Services** → **Credentials**.
7. Choose **Create credentials**.
8. In **Credential Type**:
   - Select **Google Drive API** for the API
   - Select **User data** for the data you will access
   - Choose **Next**
9. In **OAuth Consent Screen** → **App information**:
   - Enter an app name such as `UsbFileSyncApp`
   - Choose your support email
   - Enter your developer contact email
   - Choose **Save and continue**
10. In **Audience**:
   - Choose **External** if you want to sign in with a personal Google account
   - Keep the app in **Testing** while you are validating the setup
   - Under **Test users**, add the Google account you will use to sign in. For example, add `7effrey89@gmail.com` if that is the account you test with.
   - Choose **Save and continue**
11. In **Scopes (optional)** choose **Add or remove scopes**.
12. Because UsbFileSync now browses and reads real Drive folders, the OAuth client must allow full Drive read access for the signed-in user:
   - Required: `.../auth/drive.readonly`
13. Avoid adding unrelated scopes such as `.../auth/docs` or `.../auth/drive.photos.readonly` unless you specifically need them for your own testing outside UsbFileSync.
14. Save the scope selection and choose **Save and continue**.
15. In **OAuth Client ID**:
   - Set **Application type** to **Desktop app**
   - Enter a name such as `UsbFileSync Desktop`
   - Choose **Create**
16. Copy the **Client ID** shown by Google.
17. If Google also shows a client secret for that OAuth client and your sign-in flow later reports that a client secret is required, copy the **client secret** too.
18. In UsbFileSync, open **Application Settings**, turn on **Use custom provider credentials**, and paste the client ID into the **Google Drive** row under **OAuth client ID**. If needed, also paste the Google client secret into **Client secret (Google optional)**.
19. If you downloaded the full Google Desktop OAuth JSON file instead of copying the values manually, you can paste that raw JSON into the **Client secret (Google optional)** field. UsbFileSync will extract `installed.client_id` and `installed.client_secret` from the JSON automatically.
20. Click **Test Google Drive** in the settings dialog to confirm the configured values can complete sign-in and open Drive. UsbFileSync will launch your browser for the OAuth flow the first time.
21. After the test succeeds, use the **Browse** button for the source path and choose the **Google Drive** root.
22. After sign-in finishes, return to UsbFileSync, browse to the Drive folder you want, and choose **Select current folder**.

### Example values

These are **sanitized examples** that show the expected format only.

- Example **OAuth client ID** value to paste into **OAuth client ID**:

```text
123456789012-exampledesktopclientid.apps.googleusercontent.com
```

- Example **raw Desktop OAuth JSON** that can be pasted into **Client secret (Google optional)**:

```json
{
   "installed": {
      "client_id": "123456789012-exampledesktopclientid.apps.googleusercontent.com",
      "project_id": "example-usbfilesync-project",
      "auth_uri": "https://accounts.google.com/o/oauth2/auth",
      "token_uri": "https://oauth2.googleapis.com/token",
      "auth_provider_x509_cert_url": "https://www.googleapis.com/oauth2/v1/certs",
      "client_secret": "GOCSPX-exampleRedactedSecretValue",
      "redirect_uris": [
         "http://localhost"
      ]
   }
}
```

- If you copy values manually instead of pasting the JSON:
   - **OAuth client ID** field → `installed.client_id`
   - **Client secret (Google optional)** field → `installed.client_secret`

### Where to find it later

1. Return to **Google Cloud Console**.
2. Open **APIs & Services** → **Credentials**.
3. Open the OAuth client you created.
4. Copy the **Client ID** value again if you need it later.

### Notes

- If you download Google's desktop client JSON, UsbFileSync uses `installed.client_id` and can also use `installed.client_secret` when Google requires it for token exchange.
- You can paste the raw downloaded Desktop app JSON directly into the **Client secret (Google optional)** field, and UsbFileSync will extract both values automatically.
- The downloaded desktop client JSON may include `"redirect_uris": ["http://localhost"]`. That is normal for a Google desktop OAuth client.
- If Google later says `client_secret is missing` during sign-in, first confirm you are using a **Desktop app** OAuth client. If you are, paste that client's Google **client secret** into UsbFileSync as well and test again.
- UsbFileSync currently supports **Google Drive as a source only**. Google Drive destinations are still not implemented.
- Google Drive folder selection is currently path-by-name under `gdrive://root/...`. If you have duplicate folder names under the same parent in Drive, re-open the picker and confirm you selected the intended branch.
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

- **Google gave me a JSON blob with `client_id`, `client_secret`, and `redirect_uris`**  
   In the current UsbFileSync settings UI, enter only the Google `client_id`. Do not paste the JSON and do not paste the `client_secret`.

- **I selected a lot of Google Drive scopes and I am not sure that was correct**  
   For the current Google Drive source integration, UsbFileSync only needs Drive read access. Trim the registration back to `.../auth/drive.readonly` unless you have another separate reason to request more.

- **Google says the app is blocked and only developer-approved testers can access it**  
   That is a Google Cloud OAuth consent-screen setting, not a UsbFileSync bug. Open **Google Cloud Console** for the project that owns the client ID, then go to **Google Auth Platform** or **APIs & Services** → **OAuth consent screen**.
   If the app is in **Testing** mode and the audience is **External**, add the Google account you are signing in with to **Test users**.
   If you want broader access, publish the app instead, but Google may require additional consent-screen configuration or verification depending on the scopes and audience.
   For local testing with your own account, adding yourself as a **Test user** is usually the correct fix.

- **Google says `client_secret is missing` after the browser says connected**  
   First confirm the client is a **Desktop app** OAuth client. If it is, copy the Google **client secret** for that client and paste it into UsbFileSync under **Client secret (Google optional)**, then run **Test Google Drive** again.
   If the client is not a Desktop app client, create a new OAuth client with **Application type = Desktop app** and use that client ID instead.

- **I am not sure whether to enter a OneDrive tenant**  
  Leave it blank unless you specifically need to lock the login flow to one tenant. Blank stores `common`.

- **The provider portal does not exactly match these labels**  
  Provider dashboards are updated often. Look for the equivalent terms:
  - Google: **Client ID**
  - Dropbox: **App key**
  - Microsoft: **Application (client) ID** and optionally **Directory (tenant) ID**
