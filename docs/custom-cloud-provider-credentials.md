# Custom cloud provider credential setup

This guide explains how to create the **custom provider credentials** that can be entered in **Application Settings** when **Use custom provider credentials** is enabled.

> [!IMPORTANT]
> UsbFileSync now supports **Google Drive as both a source volume and a destination volume** when custom provider credentials are enabled. You can browse a Google Drive folder, select it as the source or destination, analyze it, and sync between Google Drive and local destinations.

## What UsbFileSync currently stores

The current settings UI stores:

- **Google Drive**: OAuth client ID
- **Google Drive client secret**: optional, only if your Google OAuth client requires it during token exchange
- **Dropbox**: app key / OAuth client ID
- **OneDrive**: application (client) ID
- **OneDrive tenant**: fixed to `common` in UsbFileSync settings

The current UI can now store an optional **Google Drive client secret** when a Google OAuth client requires it. Dropbox still uses only the app key, and OneDrive uses only the client ID while UsbFileSync keeps the tenant fixed to `common`.

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

   ![Select the Google Cloud project you want to use for UsbFileSync](<./images/Gdrive/Screenshot 2026-03-15 205648.png>)

3. Open **APIs & Services** → **Library**.
4. Search for **Google Drive API** from the API Library page.

   ![Open API Library and search for Google Drive](<./images/Gdrive/Screenshot 2026-03-15 205714.png>)

   ![Choose the Google Drive API from the search results](<./images/Gdrive/Screenshot 2026-03-15 205725.png>)

5. Open the **Google Drive API** result.
6. If the API is not enabled yet, click **Enable**.

   After the API is enabled, Google shows the Drive API details page like this:

   ![Google Drive API details page after the API is enabled](<./images/Gdrive/Screenshot 2026-03-15 205737.png>)

7. Open **APIs & Services** → **Credentials**.
8. Choose **Create credentials**.
9. In **Credential Type**:
   - Select **Google Drive API** for the API
   - Select **User data** for the data you will access
   - Choose **Next**

   ![Create credentials and choose Google Drive API with User data](<./images/Gdrive/Screenshot 2026-03-15 205752.png>)

10. In **OAuth Consent Screen** → **App information**:
   - Enter an app name such as `UsbFileSyncApp`
   - Choose your support email
   - Enter your developer contact email
   - Choose **Save and continue**

   ![Fill in the OAuth consent screen app information](<./images/Gdrive/Screenshot 2026-03-15 205807.png>)

11. In **Audience**:
   - Choose **External** if you want to sign in with a personal Google account
   - Keep the app in **Testing** while you are validating the setup
   - Under **Test users**, add the Google account you will use to sign in. For example, add `7effrey89@gmail.com` if that is the account you test with.
   - Choose **Save and continue**

   ![Set the audience to External, keep Testing enabled, and add your Google account as a test user](<./images/Gdrive/Screenshot 2026-03-15 210137.png>)

12. In **Scopes (optional)** choose **Add or remove scopes**.

   The first screenshot below shows the scope picker. The second shows the selected scopes before continuing.

13. Because UsbFileSync can now browse Drive folders and also write files when Google Drive is used as a destination, the OAuth client must allow full Drive access for the signed-in user:
   - Required: `.../auth/drive`
14. Avoid adding unrelated scopes such as `.../auth/docs` or `.../auth/drive.photos.readonly` unless you specifically need them for your own testing outside UsbFileSync.
15. Save the scope selection and choose **Save and continue**.

   ![Open the scope picker and select the Google Drive scopes you need](<./images/Gdrive/Screenshot 2026-03-15 210125.png>)

   ![Review the selected scopes before continuing](<./images/Gdrive/Screenshot 2026-03-15 205835.png>)

16. In **OAuth Client ID**:
   - Set **Application type** to **Desktop app**
   - Enter a name such as `UsbFileSync Desktop`
   - Choose **Create**

   ![Create a Desktop app OAuth client ID for UsbFileSync](<./images/Gdrive/Screenshot 2026-03-15 210154.png>)

17. Copy the **Client ID** shown by Google.
18. If Google also shows a client secret for that OAuth client and your sign-in flow later reports that a client secret is required, copy the **client secret** too.
19. In UsbFileSync, open **Application Settings**, turn on **Use custom provider credentials**, and paste the client ID into the **Google Drive** row under **OAuth client ID**. If needed, also paste the Google client secret into **Client secret (Google optional)**.
20. If you downloaded the full Google Desktop OAuth JSON file instead of copying the values manually, you can paste that raw JSON into the **Client secret (Google optional)** field. UsbFileSync will extract `installed.client_id` and `installed.client_secret` from the JSON automatically.
21. Click **Test Google Drive** in the settings dialog to confirm the configured values can complete sign-in and authorize Drive access. UsbFileSync will launch your browser for the OAuth flow the first time.
22. After the test succeeds, use the **Browse** button for either the source path or the destination path and choose the **Google Drive** root.
23. After sign-in finishes, return to UsbFileSync, browse to the Drive folder you want, and choose **Select current folder**.
24. When Google Drive is the destination, UsbFileSync will create folders, upload files, update files, and write sync metadata inside the selected Drive folder.

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

   ![Open the Clients page later to find the Desktop OAuth client again](<./images/Gdrive/Screenshot 2026-03-15 210301.png>)

### Notes

- If you download Google's desktop client JSON, UsbFileSync uses `installed.client_id` and can also use `installed.client_secret` when Google requires it for token exchange.
- You can paste the raw downloaded Desktop app JSON directly into the **Client secret (Google optional)** field, and UsbFileSync will extract both values automatically.
- The downloaded desktop client JSON may include `"redirect_uris": ["http://localhost"]`. That is normal for a Google desktop OAuth client.
- If Google later says `client_secret is missing` during sign-in, first confirm you are using a **Desktop app** OAuth client. If you are, paste that client's Google **client secret** into UsbFileSync as well and test again.
- UsbFileSync now supports **Google Drive as both a source and a destination**.
- Google Drive destination support uses full Drive scope so the app can create folders, upload files, rename temporary uploads into place, delete replaced files, and write sync metadata.
- Google Drive selection is currently path-by-name under `gdrive://root/...`. If a Drive folder contains multiple sibling items with the same name, UsbFileSync cannot represent that branch uniquely and will stop with a duplicate-name error.
- If Google warns that the app is still in testing, that is expected until the OAuth consent screen is fully published in your own project.

---

## Dropbox

### What you need from Dropbox

- **App key** (this is the value to use as the client ID in UsbFileSync)
- **Optional app secret** if your Dropbox app requires one during token exchange
- **Redirect URI**: `http://127.0.0.1:53682/`

### Step-by-step

1. Go to the **Dropbox App Console** at `https://www.dropbox.com/developers/apps`.
2. Choose **Create app**.
3. Select **Scoped access**.
4. Choose the access type that matches your future use:
   - **App folder** for a folder limited to your app
   - **Full Dropbox** for broader account access
5. Enter an app name and create the app.
6. Open the new app in the App Console.
7. Open the app **Permissions** section and enable these Dropbox scopes. Click 'Submit' in the bottom:
   - `files.metadata.read`
   - `files.content.read`
   - `files.metadata.write`
   - `files.content.write`
   These cover browsing cloud folders, reading files, creating folders, uploading files, replacing files, and writing sync metadata.
8. Open the app **Settings** page.
9. Under **OAuth 2**, add this exact redirect URI:
   - `http://127.0.0.1:53682/`
10. Copy the **App key**.
11. If your Dropbox app uses a secret for code exchange, copy the **App secret** too.
12. In UsbFileSync, paste the **App key** into the **Dropbox** client ID / app key field.
13. If needed, paste the **App secret** into the **Client secret / App secret** field.
14. Click **Test** on that Dropbox row, or browse to the Dropbox root from the source or destination picker.

### Where to find it later

1. Return to the **Dropbox App Console**.
2. Open your app.
3. On the app settings page, copy the **App key** again.
4. Confirm that **OAuth 2** still lists `http://127.0.0.1:53682/` as an allowed redirect URI.

### Notes

- UsbFileSync can store the Dropbox **App key** and an optional **App secret**.
- Dropbox browsing and testing use the fixed redirect URI `http://127.0.0.1:53682/`. If that URI is missing from your Dropbox app settings, Dropbox shows `Invalid redirect_uri` in the browser.
- UsbFileSync relies on the Dropbox app's configured permissions. The four scopes listed above must be enabled in the Dropbox App Console **before** you test or browse. UsbFileSync does not override them per-request; Dropbox grants whichever scopes the app has enabled.
- If the browser sign-in succeeds but UsbFileSync still reports a Dropbox folder-listing failure, re-check the four Dropbox permissions above, save them in the Dropbox App Console, then sign in again.

---

## OneDrive

OneDrive custom credentials are managed through a **Microsoft Entra ID app registration**.

### What you need from Microsoft

- **Application (client) ID**
- **Microsoft Graph delegated permissions**: `Files.ReadWrite`, `offline_access`, `User.Read`

### Step-by-step

1. Go to the **Azure portal** at `https://portal.azure.com/`.
2. Open **Microsoft Entra ID**.
3. Open **App registrations**.

   The page should look like the screenshot below before you create the app.

   ![Microsoft Entra ID App registrations page before creating the OneDrive app](<./images/OneDrive/Screenshot 2026-03-16 113433.png>)

4. Select **New registration**.
5. In **Register an application**, enter a name such as `UsbFileSync`.
6. Under **Supported account types**, choose **Personal accounts only**.
7. Under **Redirect URI (optional)**:
   - Select **Public client/native (mobile & desktop)**
   - Enter `http://localhost`
8. Click **Register**.

   These values should match the registration screen shown below.

   ![Register an application for OneDrive with Personal accounts only and the localhost public-client redirect](<./images/OneDrive/Screenshot 2026-03-16 130038.png>)

9. After the app opens, copy the **Application (client) ID** from **Overview**.
10. Open **API permissions**.
11. Make sure **Microsoft Graph** includes these delegated permissions:
   - `Files.ReadWrite`
   - `offline_access`
   - `User.Read`
12. Add any missing permission and complete consent if Microsoft asks for it.

   The API permissions page should match the screenshot below.

   ![Microsoft Graph delegated permissions for UsbFileSync OneDrive access](<./images/OneDrive/Screenshot 2026-03-16 130143.png>)

13. Do **not** create a client secret for UsbFileSync. OneDrive uses a public desktop-client flow here.
14. In UsbFileSync, open **Application Settings** and turn on **Use custom provider credentials**.
15. In the **OneDrive** row:
   - Paste the **Application (client) ID** into **OAuth client ID**
   - Leave the **Client secret** field empty
   - Leave the **Tenant ID** value as the fixed `common` value shown by UsbFileSync
16. Click **Test OneDrive** to verify that sign-in works and that UsbFileSync can open your OneDrive.
17. After the test succeeds, use **Browse** for either the source path or the destination path and choose the **OneDrive** root.
18. After sign-in finishes, return to UsbFileSync, browse to the folder you want, and choose **Select current folder**.
19. When OneDrive is the destination, UsbFileSync will create folders, upload files, update files, delete replaced items, and write sync metadata inside the selected OneDrive folder.

### Where to find it later

1. Return to **Azure portal**.
2. Open **Microsoft Entra ID** → **App registrations**.
3. Open your app registration.
4. On **Overview**, copy **Application (client) ID**.

### Notes

- The screenshots in this section show a **Personal accounts only** app registration. That is the recommended setup for a personal OneDrive account like the one you tested.
- UsbFileSync keeps the OneDrive tenant fixed to `common` in the settings dialog and automatically retries with `consumers` if Microsoft rejects `/common` for a personal-account-only registration.
- For a desktop sign-in flow, the **Public client/native (mobile & desktop)** redirect with `http://localhost` is the expected setup.
- The delegated Microsoft Graph permissions that best match UsbFileSync's current OneDrive flow are `Files.ReadWrite`, `offline_access`, and `User.Read`.
- UsbFileSync currently uses a public desktop-client flow for OneDrive, so a client secret is not used or stored in the settings dialog.
- Microsoft may also ask you to grant user consent for the delegated permissions depending on the account and tenant policy. Those consent decisions happen in Entra ID, but the current UsbFileSync settings page only stores the client ID for OneDrive.

---

## Entering the values in UsbFileSync

1. Open **Application Settings**.
2. Turn on **Use custom provider credentials**.
3. Paste the values you collected:
   - **Google Drive** → Google **Client ID**
   - **Dropbox** → Dropbox **App key** and optional **App secret**
   - **OneDrive** → Microsoft **Application (client) ID**
   - **OneDrive tenant** → leave the fixed `common` value as-is
4. Save the settings.

## Troubleshooting

- **I only see a client secret, not a client ID**  
  Go back to the app overview/credentials page. UsbFileSync currently wants the provider's **client ID** value, not the secret.

- **Google gave me a JSON blob with `client_id`, `client_secret`, and `redirect_uris`**  
   You can paste the raw desktop OAuth JSON into **Client secret (Google optional)** and UsbFileSync will extract `client_id` and `client_secret` automatically, or copy those values manually into the matching fields.

- **I selected a lot of Google Drive scopes and I am not sure that was correct**  
   For the current Google Drive source and destination integration, UsbFileSync needs full Drive access. Trim the registration back to `.../auth/drive` unless you have another separate reason to request more.

- **Google says the app is blocked and only developer-approved testers can access it**  
   That is a Google Cloud OAuth consent-screen setting, not a UsbFileSync bug. Open **Google Cloud Console** for the project that owns the client ID, then go to **Google Auth Platform** or **APIs & Services** → **OAuth consent screen**.
   If the app is in **Testing** mode and the audience is **External**, add the Google account you are signing in with to **Test users**.
   If you want broader access, publish the app instead, but Google may require additional consent-screen configuration or verification depending on the scopes and audience.
   For local testing with your own account, adding yourself as a **Test user** is usually the correct fix.

- **Google says `client_secret is missing` after the browser says connected**  
   First confirm the client is a **Desktop app** OAuth client. If it is, copy the Google **client secret** for that client and paste it into UsbFileSync under **Client secret (Google optional)**, then run **Test Google Drive** again.

- **Dropbox shows `Invalid redirect_uri` in the browser**  
   Open your app in the **Dropbox App Console**, go to **Settings**, and add `http://127.0.0.1:53682/` under **OAuth 2 redirect URIs**. Then retry the Dropbox test or browse flow.

- **Dropbox says the local callback listener could not start**  
   Confirm the Dropbox app is using `http://127.0.0.1:53682/`, not `http://localhost:53682/`. The app now uses the numeric loopback address to avoid Windows `localhost` URL reservation conflicts.
   If the client is not a Desktop app client, create a new OAuth client with **Application type = Desktop app** and use that client ID instead.

- **Dropbox connects in the browser but UsbFileSync says folder listing failed**  
   Open the Dropbox app's **Permissions** page and enable `files.metadata.read`, `files.content.read`, `files.metadata.write`, and `files.content.write`. Save the permissions, then sign in again so Dropbox issues a token with the updated scopes.

- **I am not sure whether to enter a OneDrive tenant**  
  Leave it blank unless you specifically need to lock the login flow to one tenant. Blank stores `common`.

- **The provider portal does not exactly match these labels**  
  Provider dashboards are updated often. Look for the equivalent terms:
  - Google: **Client ID**
  - Dropbox: **App key**
  - Microsoft: **Application (client) ID** and optionally **Directory (tenant) ID**
